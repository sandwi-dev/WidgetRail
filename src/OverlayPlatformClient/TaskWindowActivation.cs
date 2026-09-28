namespace WidgetRail.OverlayPlatformClient;

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
        long visibleSince, Func<bool> authorityCurrent, Action hideOverlay)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(authorityCurrent);
        ArgumentNullException.ThrowIfNull(hideOverlay);
        bool Fresh()
        {
            var now = platform.UptimeMilliseconds;
            return initiatedAt >= 0 && initiatedAt >= visibleSince && initiatedAt <= now && now - initiatedAt <= 2000;
        }
        if (!Fresh() || !authorityCurrent() || !platform.IsOverlayForeground || !platform.IsCurrent(target))
            return TaskWindowActivationResult.Rejected;
        hideOverlay();
        if (!Fresh() || !authorityCurrent() || !platform.IsCurrent(target))
            return TaskWindowActivationResult.Rejected;
        return platform.RequestActivation(target) ? TaskWindowActivationResult.Requested : TaskWindowActivationResult.Denied;
    }
}
