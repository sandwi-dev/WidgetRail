namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>One host-owned settling deadline. Updating content for the same source never postpones it.</summary>
internal sealed class FocusPreviewDelayState
{
    private object? identity;
    private int delay;
    private long due;
    internal bool Select(object? next, int delayMilliseconds, long now)
    {
        if (Equals(identity, next) && delay == delayMilliseconds) return false;
        identity = next; delay = delayMilliseconds; due = now + delayMilliseconds;
        return true;
    }
    internal void Complete(long now) { if (identity is not null) due = now; }
    internal bool Ready(long now) => identity is not null && now >= due;
    internal int Remaining(long now) => identity is null ? 0 : (int)Math.Clamp(due - now, 0, 1500);
    internal void Clear() { identity = null; delay = 0; due = 0; }
}
