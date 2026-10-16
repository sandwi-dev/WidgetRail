using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayPlatformClient;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    /// <summary>Explicit hardware-free shell regression route; uses the production normalized-input consumer.</summary>
    private bool validationInputReplayEnabled;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, object> replayRoots = new();
    internal void EnableValidationInputReplay()
    { validationInputReplayEnabled = true; AttachValidationInputReplay(this); }
    private void AttachValidationInputReplay(UIElement root)
    {
        if (!validationInputReplayEnabled || replayRoots.TryGetValue(root, out _)) return;
        replayRoots.Add(root, new());
        root.AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
        {
            ushort mask = args.Key switch
            {
                VirtualKey.F5 => 0x10, // Menu
                VirtualKey.F6 => 0x2000, // B
                VirtualKey.F7 => 0x100, // LB
                VirtualKey.F8 => 0x200, // RB
                VirtualKey.F9 => 0x1000, // A
                VirtualKey.F10 => 0x8000, // Y
                _ => 0,
            };
            if (mask == 0) return;
            args.Handled = true;
            var frame = ControllerFrame.Create();
            frame.Connected = 1;
            Receive(frame); // Neutral arms newly opened dialogs before this test gesture.
            frame.PressedButtons = frame.State.Buttons = mask;
            Receive(frame);
            frame.PressedButtons = frame.State.Buttons = 0;
            frame.ReleasedButtons = mask;
            Receive(frame);
        }), true);
    }
}
