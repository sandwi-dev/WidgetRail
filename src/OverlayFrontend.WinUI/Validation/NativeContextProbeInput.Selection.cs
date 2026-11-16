using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.System;
namespace WidgetRail.OverlayFrontend.WinUI.Validation;
internal static partial class NativeContextProbeInput
{
    internal static async Task SelectionClickAsync(FrameworkElement target, bool control = false, bool shift = false)
    {
        try
        {
            if (control) SelectionModifier(VirtualKey.Control, false);
            if (shift) SelectionModifier(VirtualKey.Shift, false);
            await LeftClickAsync(target, new Point(Math.Max(1,target.ActualWidth * .65), target.ActualHeight / 2));
        }
        finally
        {
            if (shift) SelectionModifier(VirtualKey.Shift, true);
            if (control) SelectionModifier(VirtualKey.Control, true);
        }
    }
    internal static void SelectionAllKey()
    {
        try { SelectionModifier(VirtualKey.Control, false); PressKey(VirtualKey.A); }
        finally { SelectionModifier(VirtualKey.Control, true); }
    }
    private static unsafe void SelectionModifier(VirtualKey key, bool up)
    {
        if (!up) DemandOwnForeground();
        var input = Key(key,up);
        if (SendInput(1,&input,sizeof(NativeInput)) != 1) throw new InvalidOperationException("Selection modifier injection failed.");
    }
}
