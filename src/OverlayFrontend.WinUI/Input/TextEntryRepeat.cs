using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>One keyboard edit gesture. Hardware samples, never timers, own its lifetime.</summary>
internal sealed class TextEntryRepeat
{
    private const ushort RepeatButtons = 0x1000 | 0x4000 | 0x100 | 0x200;
    private ushort held;
    private object? identity;
    private long deadline;

    internal void Reset() { held = 0; identity = null; deadline = 0; }
    internal void CancelCharacter() { if (held == 0x1000) Reset(); }

    // The caller has already delivered the initial press exactly once. A changed
    // key, conflicting buttons, or an interrupted owner require a fresh press.
    internal ControllerButton? Sample(ushort down, ushort pressed, object? character, long now)
    {
        var selected = (ushort)(down & RepeatButtons);
        if (selected == 0 || (selected & (selected - 1)) != 0 || selected == 0x1000 && character is null)
        { Reset(); return null; }
        object target = selected == 0x1000 ? character! : selected;
        if (held == 0)
        {
            if ((pressed & selected) != 0) { held = selected; identity = target; deadline = now + 400; }
            return null;
        }
        if (held != selected || !Equals(identity, target)) { Reset(); return null; }
        if (now < deadline) return null;
        deadline = now + 90;
        return selected switch
        {
            0x1000 => ControllerButton.A, 0x4000 => ControllerButton.X,
            0x100 => ControllerButton.LeftBumper, _ => ControllerButton.RightBumper,
        };
    }
}
