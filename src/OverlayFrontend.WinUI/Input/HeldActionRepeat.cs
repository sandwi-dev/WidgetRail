namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Native-host cadence; callers revalidate semantic ownership on every tick.</summary>
internal sealed class HeldActionRepeat
{
    private object? identity;
    private long deadline;
    internal bool Active => identity is not null;
    internal void Begin(object target, long now) { identity = target; deadline = now + 360; }
    internal void Reset() => identity = null;
    internal bool Tick(object? current, bool down, bool available, long now)
    {
        if (!down || identity is null || !Equals(identity, current)) { Reset(); return false; }
        // A busy owner or in-flight action waits without queuing missed repeats.
        if (!available || now < deadline) return false;
        deadline = now + 125;
        return true;
    }
}
