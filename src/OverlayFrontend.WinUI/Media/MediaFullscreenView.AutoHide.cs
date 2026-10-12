using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaFullscreenView
{
    private MediaControlsVisibility controlsVisibility = null!;
    private readonly Button videoFocus = new() { Width = 1, Height = 1, Opacity = 0, IsTabStop = false, IsHitTestVisible = false };
    private bool controlsInteractive = true;

    internal bool ControlsVisible => commands.Visibility == Visibility.Visible;

    private void InitializeAutoHide()
    {
        controlsInteractive = !compact;
        Children.Add(videoFocus);
        AutomationProperties.SetName(videoFocus, "Video. Activate to show playback controls");
        AutomationProperties.SetAutomationId(videoFocus, compact ? "Overlay.MediaCompact.VideoFocus" : "Overlay.MediaFullscreen.VideoFocus");
        videoFocus.Click += (_, _) => Enter();
        controlsVisibility = new([commands, progress, title], () => controlsInteractive && Visibility == Visibility.Visible, ParkControlFocus);
        controlsVisibility.Initialize();
        PointerMoved += (_, _) => RevealControls();
        PointerPressed += (_, _) => RevealControls();


        KeyDown += (_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or
                Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or
                Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter)
            {
                if (!ControlsVisible) { Enter(); args.Handled = true; }
                else RevealControls();
            }
        };
    }

    internal void RevealControls()
    {
        if (!controlsInteractive || Visibility != Visibility.Visible) return;
        controlsVisibility.Reveal();
        title.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HideControls() => controlsVisibility.Hide();

    private void ParkControlFocus()
    {
        if (controlsInteractive && XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button focused && actions.ContainsKey(focused))
            videoFocus.Focus(FocusState.Programmatic);
    }
    private void FocusControls() => (remembered is { IsEnabled: true } ? remembered : back).Focus(FocusState.Keyboard);

    internal void MoveFocus(FocusNavigationDirection direction)
    {
        if (!controlsInteractive) return;
        if (!ControlsVisible) { Enter(); return; }
        RevealControls();
        FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = commands });
    }
}
