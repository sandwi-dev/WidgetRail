using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>One keyboard edit gesture. Hardware samples, never timers, own its lifetime.</summary>
internal sealed class TextEntryRepeat
{
    private const ushort RepeatButtons = 0x1000 | 0x4000 | 0x100 | 0x200;
    private ushort held;
    private object? identity;
    private long deadline, started;
    private int characterRepeats;
    internal bool ByWord { get; private set; }

    internal void Reset() { held = 0; identity = null; deadline = 0; started = 0; characterRepeats = 0; ByWord = false; }

    // The caller has already delivered the initial press exactly once. A changed
    // key, conflicting buttons, or an interrupted owner require a fresh press.
    internal ControllerButton? Sample(ushort down, ushort pressed, object? character, long now, bool allowWords = true)
    {
        var selected = (ushort)(down & RepeatButtons);
        if (selected == 0 || (selected & (selected - 1)) != 0 || selected == 0x1000 && character is null)
        { Reset(); return null; }
        object target = selected == 0x1000 ? character! : selected;
        if (held == 0)
        {
            if ((pressed & selected) != 0) { held = selected; identity = target; started = now; deadline = now + 400; }
            return null;
        }
        if (held != selected || !Equals(identity, target))
        {
            Reset();
            if ((pressed & selected) != 0) { held = selected; identity = target; started = now; deadline = now + 400; }
            return null;
        }
        if (now < deadline) return null;
        // Require actual character repeats as well as elapsed time. A stalled
        // input pump must never resume with an unexpected whole-word deletion.
        ByWord = allowWords && selected != 0x1000 && characterRepeats >= 4 && now - started >= 1000;
        if (!ByWord) characterRepeats++;
        deadline = now + (ByWord ? 250 : 90);
        return selected switch
        {
            0x1000 => ControllerButton.A, 0x4000 => ControllerButton.X,
            0x100 => ControllerButton.LeftBumper, _ => ControllerButton.RightBumper,
        };
    }
}

/// <summary>Release-only utility shortcuts leave the global View+Menu chord untouched.</summary>
internal sealed class TextEntryUtilityShortcuts
{
    private ushort pending;
    private bool blocked;
    internal void Reset() { pending=0;blocked=false; }
    internal ControllerButton? Sample(ushort down,ushort pressed)
    {
        var held=(ushort)(down&0x30);
        if(held==0x30||(held!=0||pending!=0)&&(down&~0x30)!=0){pending=0;blocked=true;return null;}
        if(held==0)
        {
            var result=blocked||pending==0?null:(ControllerButton?)(pending==0x10?ControllerButton.Menu:ControllerButton.View);
            Reset();return result;
        }
        if(blocked)return null;
        if(pending!=0&&pending!=held){pending=0;blocked=true;return null;}
        if((pressed&held)!=0)pending=held;
        return null;
    }
}
