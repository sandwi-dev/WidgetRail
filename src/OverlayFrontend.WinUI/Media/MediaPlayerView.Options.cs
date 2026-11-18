using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private MenuFlyout? optionsMenu;
    private NativeMenuNavigator? optionsNavigation;
    private IDisposable? optionsTheme;
    internal bool HasOptionsMenu => optionsMenu is not null;

    private void OpenPlaybackOptions()
    {
        if (!input || disposed || changingPlacement || player is not { } owner || definition is not { } expected ||
            !playbackOptions.IsLoaded || optionsMenu is not null) return;
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight };
        bool Current() => !disposed && active && input && ReferenceEquals(optionsMenu, menu) &&
            ReferenceEquals(player, owner) && definition?.Source == expected.Source && definition.Revision == expected.Revision &&
            currentAuthority?.Invoke() != false;
        MenuFlyoutItem Choice(string id, string label, Action action, bool selected = false)
        {
            var item = new MenuFlyoutItem { Text = label,
                Icon = selected ? new SymbolIcon(Symbol.Accept) : null,
                Command = new RelayCommand(() =>
                {
                    if (!Current()) return;
                    action(); ClosePlaybackOptions(restoreFocus: true);
                }) };
            AutomationProperties.SetAutomationId(item, "MediaPlayer.Options." + id);
            AutomationProperties.SetName(item, selected ? label + ", current" : label);
            return item;
        }
        menu.Items.Add(Choice("restart", "Restart from beginning", Replay));
        var speeds = new MenuFlyoutSubItem { Text = "Playback speed" };
        AutomationProperties.SetAutomationId(speeds, "MediaPlayer.Options.Speed");
        foreach (var rate in new[] { .5, .75, 1, 1.25, 1.5, 2 })
            speeds.Items.Add(Choice("speed." + rate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                rate.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + "×", () => SetPlaybackRate(rate),
                Math.Abs(owner.PlaybackSession.PlaybackRate - rate) < .01));
        menu.Items.Add(speeds);
        menu.Items.Add(Choice("loop", owner.IsLoopingEnabled ? "Turn looping off" : "Turn looping on", ToggleLoop));
        optionsMenu = menu;
        optionsNavigation = new(menu, Current);
        optionsTheme = NativePopupTheme.Menu(menu, this);
        Input.GamepadKeyBoundary.ObserveFlyout(menu, playbackOptions);
        menu.Opened += (_, _) => { if (Current()) optionsNavigation?.FocusFirst(); };
        menu.Closed += (_, _) => { if (ReferenceEquals(optionsMenu, menu)) ClosePlaybackOptions(restoreFocus: false); };
        StopMediaChrome();
        menu.ShowAt(playbackOptions);
        InteractionChanged?.Invoke();
    }

    private void ClosePlaybackOptions(bool restoreFocus = false)
    {
        if (optionsMenu is not { } menu) return;
        optionsMenu = null;
        optionsNavigation?.Dispose(); optionsNavigation = null;
        optionsTheme?.Dispose(); optionsTheme = null;
        menu.Hide();
        if (restoreFocus && input && !disposed && playbackOptions.IsLoaded) playbackOptions.Focus(FocusState.Keyboard);
        if (input && !disposed) videoChrome.Reveal();
        InteractionChanged?.Invoke();
    }

    private void SetPlaybackRate(double rate)
    {
        if (!input || player is null) return;
        requestedRate = rate;
        player.PlaybackSession.PlaybackRate = rate;
        Refresh(); InteractionChanged?.Invoke();
    }
}
