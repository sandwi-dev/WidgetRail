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
/// Serialized owner-thread handoff. Input release precedes activation and exact
/// identity/authority is revalidated at the commit boundary. Broker completion
/// uses delegated permission after hide; direct host activation precedes hide.
/// </summary>
public sealed class TaskWindowActivation(ITaskWindowActivationPlatform platform)
{
    /// <summary>Direct activation keeps the host HWND foreground until Windows
    /// accepts the target. Unlike the broker path, there is no other process with
    /// a delegated foreground grant to complete activation after hiding.</summary>
    public async Task<TaskWindowActivationResult> ExecuteDirectAsync(TaskWindowTarget target, long initiatedAt,
        long visibleSince, Func<bool> authorityCurrent, Action releaseInput, Action hideOverlay,
        Func<Task<bool>> prepareHandoff, Action<string>? diagnostic = null)
    {
        if (!Admit(target, initiatedAt, visibleSince, authorityCurrent, diagnostic) || !await prepareHandoff())
            return TaskWindowActivationResult.Rejected;
        if (!Admit(target, initiatedAt, visibleSince, authorityCurrent, diagnostic)) return TaskWindowActivationResult.Rejected;
        releaseInput();
        if (!Admit(target, initiatedAt, visibleSince, authorityCurrent, diagnostic)) return TaskWindowActivationResult.Rejected;
        Trace(diagnostic, "phase=request-activation");
        var accepted = platform.RequestActivation(target);
        Trace(diagnostic, "phase=activation-returned accepted=" + accepted);
        if (!accepted) return TaskWindowActivationResult.Denied;
        hideOverlay();
        return TaskWindowActivationResult.Requested;
    }

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
