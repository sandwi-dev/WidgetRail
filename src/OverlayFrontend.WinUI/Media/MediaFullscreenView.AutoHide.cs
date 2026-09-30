using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaFullscreenView
{
    private readonly DispatcherTimer controlsTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Button videoFocus = new() { Width = 1, Height = 1, Opacity = 0, IsTabStop = false, IsHitTestVisible = false };
    private bool controlsInteractive = true;
    private bool pointerOverControls;
    internal bool ControlsVisible => commands.Visibility == Visibility.Visible;

    private void InitializeAutoHide()
    {
        controlsInteractive = !compact;
        Children.Add(videoFocus);
        AutomationProperties.SetName(videoFocus, "Video. Activate to show playback controls");
        AutomationProperties.SetAutomationId(videoFocus, compact ? "Overlay.MediaCompact.VideoFocus" : "Overlay.MediaFullscreen.VideoFocus");
        videoFocus.Click += (_, _) => Enter();
        controlsTimer.Tick += (_, _) => { if (!pointerOverControls) HideControls(); };
        PointerMoved += (_, _) => RevealControls();
        PointerPressed += (_, _) => RevealControls();
        commands.PointerEntered += (_, _) => { pointerOverControls = true; controlsTimer.Stop(); };
        commands.PointerExited += (_, _) => { pointerOverControls = false; RevealControls(); };
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
        commands.Visibility = progress.Visibility = Visibility.Visible;
        title.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        commands.IsHitTestVisible = true;
        controlsTimer.Stop();
        if (IsLoaded && !pointerOverControls) controlsTimer.Start();
    }

    private void HideControls()
    {
        controlsTimer.Stop();
        // Preserve the command for restoration, but never leave native focus on
        // an invisible button or let A accidentally execute that hidden command.
        if (controlsInteractive && XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button focused && actions.ContainsKey(focused))
            videoFocus.Focus(FocusState.Programmatic);
        commands.IsHitTestVisible = false;
        commands.Visibility = title.Visibility = progress.Visibility = Visibility.Collapsed;
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
