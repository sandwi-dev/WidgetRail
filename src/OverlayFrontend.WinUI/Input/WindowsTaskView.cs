namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal enum WindowsTaskViewResult { Requested, KeyboardBusy, InputRejected, Cancelled }

/// <summary>Explicit shell shortcut request. Never used to obtain activation rights for another action.</summary>
internal static class WindowsTaskView
{
    private static ReadOnlySpan<ushort> Keys => [0x5B, 0x09];
    internal static bool KeyboardBusy() => GuardedKeyboardChord.KeyboardBusy(Keys);
    internal static WindowsTaskViewResult Open() => Convert(GuardedKeyboardChord.Request(Keys));
    internal static WindowsTaskViewResult Request(Func<int, bool> isDown,
        Func<GuardedKeyboardChord.NativeInput[], uint> send) => Convert(GuardedKeyboardChord.Request(Keys, isDown, send));

    private static WindowsTaskViewResult Convert(KeyboardChordResult result) => result switch
    {
        KeyboardChordResult.Requested => WindowsTaskViewResult.Requested,
        KeyboardChordResult.KeyboardBusy => WindowsTaskViewResult.KeyboardBusy,
        _ => WindowsTaskViewResult.InputRejected
    };
}
