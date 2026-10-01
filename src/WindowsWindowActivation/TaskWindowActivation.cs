namespace WidgetRail.WindowsWindowActivation;

/// <summary>Host-private identity admitted by the broker. Never accept raw widget handles.</summary>
public sealed record TaskWindowTarget(ulong Handle, uint ProcessId, ulong ProcessCreated, string ClassName);

public interface ITaskWindowActivationPlatform
{
    long UptimeMilliseconds { get; }
    bool IsOverlayForeground { get; }
    bool IsCurrent(TaskWindowTarget target);
    bool RequestActivation(TaskWindowTarget target);
}

public enum TaskWindowActivationResult { Rejected, Requested, Denied }

/// <summary>
/// Serialized owner-thread handoff. Closing releases overlay input before Windows
/// activation; the identity and worker authority are checked again after closing.
/// No retry or overlay reopen is permitted when Windows declines the request.
/// </summary>
public sealed class TaskWindowActivation(ITaskWindowActivationPlatform platform)
{
    public TaskWindowActivationResult Execute(TaskWindowTarget target, long initiatedAt,
        long visibleSince, Func<bool> authorityCurrent, Action hideOverlay, Action<string>? diagnostic = null)
    {
        if (!Prepare(target, initiatedAt, visibleSince, authorityCurrent, hideOverlay, diagnostic)) return TaskWindowActivationResult.Rejected;
        var accepted = platform.RequestActivation(target);
        Trace(diagnostic, "phase=activation-returned accepted=" + accepted);
        return accepted ? TaskWindowActivationResult.Requested : TaskWindowActivationResult.Denied;
    }

    public async Task<TaskWindowActivationResult> ExecuteAsync(TaskWindowTarget target, long initiatedAt,
        long visibleSince, Func<bool> authorityCurrent, Action hideOverlay,
        Func<Task<TaskWindowActivationResult>> activate, Action<string>? diagnostic = null,
        Func<Task<bool>>? prepareHandoff = null)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (prepareHandoff is not null)
        {
            if (!Admit(target, initiatedAt, visibleSince, authorityCurrent, diagnostic)) return TaskWindowActivationResult.Rejected;
            // Preparation retains foreground and UI context while the selecting
            // gesture and close motion finish. Revalidate all admission below.
            if (!await prepareHandoff()) return TaskWindowActivationResult.Rejected;
        }
        if (!Prepare(target, initiatedAt, visibleSince, authorityCurrent, hideOverlay, diagnostic)) return TaskWindowActivationResult.Rejected;
        return await activate().ConfigureAwait(false);
    }

    private bool Prepare(TaskWindowTarget target, long initiatedAt, long visibleSince,
        Func<bool> authorityCurrent, Action hideOverlay, Action<string>? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(hideOverlay);
        if (!Admit(target, initiatedAt, visibleSince, authorityCurrent, diagnostic)) return false;
        Trace(diagnostic, "phase=before-hide");
        hideOverlay();
        bool Reject(string reason) { Trace(diagnostic, "rejected reason=" + reason); return false; }
        if (!Fresh(initiatedAt, visibleSince)) return Reject("stale-after-hide");
        if (!authorityCurrent()) return Reject("authority-after-hide");
        if (!platform.IsCurrent(target)) return Reject("target-after-hide");
        Trace(diagnostic, "phase=request-activation");
        return true;
    }

    private bool Fresh(long initiatedAt, long visibleSince)
    {
        var now = platform.UptimeMilliseconds;
        return initiatedAt >= 0 && initiatedAt >= visibleSince && initiatedAt <= now && now - initiatedAt <= 2000;
    }

    private bool Admit(TaskWindowTarget target, long initiatedAt, long visibleSince,
        Func<bool> authorityCurrent, Action<string>? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(authorityCurrent);
        bool Reject(string reason) { Trace(diagnostic, "rejected reason=" + reason); return false; }
        if (!Fresh(initiatedAt, visibleSince)) return Reject("stale-before-hide");
        if (!authorityCurrent()) return Reject("authority-before-hide");
        if (!platform.IsOverlayForeground) return Reject("not-foreground");
        if (!platform.IsCurrent(target)) return Reject("target-before-hide");
        return true;
    }
    private static void Trace(Action<string>? diagnostic, string message)
    { try { diagnostic?.Invoke(message); } catch { /* Diagnostics cannot alter activation. */ } }
}
