using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>UI-thread release boundary; drains queued frames, never polls or delays independently.</summary>
internal sealed class ControllerHandoffRelease
{
    private TaskCompletionSource<bool>? pending;
    private bool connected;
    internal bool IsPending => pending is not null;
    internal Task<bool> Begin()
    {
        if (pending is not null) throw new InvalidOperationException("A controller handoff is already pending.");
        // Require a fresh, fully drained observation even if the last frame was
        // neutral. A queued historical release must not authorize a new handoff.
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return pending.Task;
    }
    internal void Observe(ControllerFrame frame)
    {
        var disconnected = connected && frame.Connected == 0;
        connected = frame.Connected != 0;
        if (pending is null) return;
        if (disconnected) { Cancel(); return; }
        if (frame.RemainingFrames != 0) return;
        // Ignore stick drift. Buttons/triggers can activate a destination; both
        // must finish before releasing the foreground controller read lease.
        if (frame.State.Buttons != 0 || frame.State.LeftTrigger > 30 || frame.State.RightTrigger > 30) return;
        Complete(true);
    }
    internal void Cancel() => Complete(false);
    private void Complete(bool value)
    {
        var previous = pending; pending = null; previous?.TrySetResult(value);
    }
}
