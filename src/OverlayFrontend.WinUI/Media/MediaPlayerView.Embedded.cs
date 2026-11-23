using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private sealed record EmbeddedSlot(EmbeddedMediaOwner Owner, WidgetPresentationAuthority Authority,
        string ElementId, EmbeddedMediaSession Session);
    private EmbeddedSlot? embeddedSlot;
    private WidgetMediaViewport? embeddedViewport;
    private Grid? embeddedStage;
    private Border? embeddedToolbar;
    private Button[] embeddedControls = [];
    private bool embeddedReady;
    private MediaControlsVisibility? embeddedChrome;
    private Button? embeddedWake;
    private Button? lastEmbeddedControl;
    private bool embeddedPlaying;

    internal void ConfigureEmbedded(EmbeddedMediaOwner? owner, WidgetPresentationAuthority authority, string elementId,
        EmbeddedMediaSession? session)
    {
        var slot = owner is not null && session is not null ? new EmbeddedSlot(owner, authority, elementId, session) : null;
        if (embeddedSlot?.Session.Id != slot?.Session.Id || embeddedSlot?.Owner != slot?.Owner ||
            embeddedSlot?.Authority.WorkerRun != slot?.Authority.WorkerRun)
            CloseEmbedded();
        embeddedSlot = slot;
    }

    private void OpenEmbedded()
    {
        if (embeddedSlot is not { } slot || slot.Session.Id != definition?.Source.Location ||
            currentAuthority?.Invoke() != true)
        { EndUnavailableSource(true); return; }
        embeddedViewport = new WidgetMediaViewport();
        embeddedViewport.Configure(slot.Session, true);
        embeddedViewport.CanAcceptInput = () => input && currentAuthority?.Invoke() == true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        embeddedControls = [Add("Play", "Play", () => EmbeddedCommand(EmbeddedMediaCommand.TogglePlayback)),
            Add("Unmute", "Mute", () => EmbeddedCommand(EmbeddedMediaCommand.ToggleMute)),
            Add("Fullscreen", "Expand", () => _ = ToggleExpandedAsync())];
        embeddedStage = new Grid();
        embeddedStage.Children.Add(embeddedViewport);
        embeddedToolbar = new Border
        {
            Child = buttons, Background = galleryChromeBackground, CornerRadius = inlineRadius,
            Padding = new(4), Margin = new(8), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        embeddedStage.Children.Add(embeddedToolbar);
        embeddedWake = new Button { Opacity = 0, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Label(embeddedWake, "Show playback controls", "MediaPlayer.Embedded.ShowControls", showToolTip: false);
        embeddedStage.Children.Add(embeddedWake);
        embeddedChrome = new([embeddedToolbar], () => input && embeddedPlaying && !changingPlacement, () =>
        {
            var park = DialogChoiceNavigation.ContainsFocus(embeddedToolbar);
            embeddedWake.Visibility = Visibility.Visible;
            if (park) embeddedWake.Focus(FocusState.Programmatic);
        }, () => Input.PointerCaptureLifetime.HasCaptureWithin(embeddedToolbar), hideWhileHovered: true);
        embeddedChrome.Initialize();
        embeddedWake.Click += (_, _) => RevealEmbeddedControls(enter: true);
        embeddedWake.GotFocus += (_, _) =>
        {
            if (embeddedReady && embeddedChrome?.Entered == false && !changingPlacement) RevealEmbeddedControls(enter: true);
        };
        embeddedWake.PreviewKeyDown += (_, args) =>
        {
            if (!Input.GamepadKeyBoundary.Owns(this, args) && args.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or
                Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
                args.Handled = RevealEmbeddedControls(enter: true);
        };
        embeddedStage.PointerMoved += (_, args) => { if (embeddedChrome?.PointerMoved(args) == true) RevealEmbeddedControls(); };
        embeddedToolbar.KeyDown += (_, _) => { if (input) embeddedChrome?.Reveal(); };
        embeddedToolbar.GotFocus += (_, _) =>
        {
            if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button button && embeddedControls.Contains(button))
                lastEmbeddedControl = button;
            if (!changingPlacement) embeddedChrome?.Reveal();
        };
        Grid.SetRow(embeddedStage, 1);
        var frame = fullscreenGallery ?? InlineGalleryParent;
        var controlsIndex = frame.Children.IndexOf(galleryControls);
        frame.Children.Insert(controlsIndex >= 0 ? controlsIndex : frame.Children.Count, embeddedStage);
        body.Visibility = Visibility.Collapsed;
        slot.Owner.Bind(slot.Authority, slot.ElementId, slot.Session.Id, embeddedViewport, definition);
        RefreshEmbedded();

        Button Add(string label, string key, Action activate)
        {
            var button = new Button { Content = label, MinHeight = 44, MinWidth = 70, UseSystemFocusVisuals = true,
                FontSize = play.FontSize, FontFamily = play.FontFamily, Foreground = play.Foreground,
                Background = play.Background, BorderBrush = play.BorderBrush, BorderThickness = play.BorderThickness,
                CornerRadius = play.CornerRadius, FocusVisualPrimaryBrush = play.FocusVisualPrimaryBrush,
                FocusVisualSecondaryBrush = play.FocusVisualSecondaryBrush };
            Label(button, label, "MediaPlayer.Embedded." + key);
            button.Click += (_, _) => { if (input) { RevealEmbeddedControls(); activate(); } };
            buttons.Children.Add(button);
            return button;
        }
    }

    private EmbeddedMediaSurface? EmbeddedSurface() => embeddedSlot is { } slot && embeddedViewport is { } viewport
        ? slot.Owner.GallerySurface(slot.Authority.WidgetId, viewport) : null;
    private void EmbeddedCommand(EmbeddedMediaCommand command) => EmbeddedSurface()?.Dispatch(command);

    private void RefreshEmbedded()
    {
        if (embeddedViewport is null || embeddedSlot is not { } slot) return;
        var surface = EmbeddedSurface();
        var playback = surface?.Playback;
        if (slot.Owner.GetFailure(slot.Authority.WidgetId) is not null ||
            playback?.State == EmbeddedMediaPlaybackState.Error && (!embeddedReady || playback.CommandSequence != 0))
        {
            CloseEmbedded();
            EndUnavailableSource(true);
            return;
        }
        var ready = playback is { MediaKey.Length: > 0 } && (embeddedReady || playback.State is
            EmbeddedMediaPlaybackState.Playing or EmbeddedMediaPlaybackState.Paused or EmbeddedMediaPlaybackState.Ended or EmbeddedMediaPlaybackState.Ready);
        var firstReady = ready && !embeddedReady;
        embeddedReady = ready;
        embeddedControls[0].IsEnabled = input && ready && slot.Session.Commands.Contains(EmbeddedMediaCommand.TogglePlayback);
        embeddedControls[1].IsEnabled = embeddedControls[2].IsEnabled = input && ready;
        embeddedControls[1].Visibility = slot.Session.Commands.Contains(EmbeddedMediaCommand.ToggleMute) ? Visibility.Visible : Visibility.Collapsed;
        if (embeddedWake is not null && embeddedChrome?.Entered == false)
            embeddedWake.Visibility = input && ready ? Visibility.Visible : Visibility.Collapsed;
        var playing = input && ready && playback?.State == EmbeddedMediaPlaybackState.Playing;
        if (firstReady || playing != embeddedPlaying)
        {
            embeddedPlaying = playing;
            RevealEmbeddedControls();
        }
        if (!ready) return;
        SetLabel(embeddedControls[0], playback!.State == EmbeddedMediaPlaybackState.Playing ? "Pause" : "Play");
        SetLabel(embeddedControls[1], playback.Muted ? "Unmute" : "Mute");
        SetLabel(embeddedControls[2], IsExpanded ? "Collapse" : "Fullscreen");
        if (firstReady && input && XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this))
            FocusMediaEntry();

        static void SetLabel(Button button, string label)
        {
            if (Equals(button.Content, label)) return;
            button.Content = label;
            AutomationProperties.SetName(button, label);
            NativePopupTheme.SetToolTip(button, label);
        }
    }

    private bool MoveEmbeddedFocus(FocusNavigationDirection direction)
    {
        if (!embeddedReady || XamlRoot is null || direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right)) return false;
        var available = embeddedControls.Where(control => control.IsEnabled && control.Visibility == Visibility.Visible).ToArray();
        var index = Array.IndexOf(available, FocusManager.GetFocusedElement(XamlRoot) as Button);
        if (index < 0) return false;
        return available[Math.Clamp(index + (direction == FocusNavigationDirection.Left ? -1 : 1), 0, available.Length - 1)]
            .Focus(FocusState.Keyboard);
    }

    private bool RevealEmbeddedControls(bool enter = false)
    {
        if (embeddedChrome is null || embeddedWake is null || !input || !embeddedReady || !enter && !embeddedChrome.Entered) return false;
        var wasHidden = !embeddedChrome.Visible;
        if (enter) embeddedChrome.Enter();
        else embeddedChrome.Reveal();
        if (embeddedWake.Visibility == Visibility.Visible)
        {
            if (XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), embeddedWake))
                PreferredEmbeddedControl.Focus(FocusState.Keyboard);
            embeddedWake.Visibility = Visibility.Collapsed;
        }
        return wasHidden;
    }

    private Control PreferredEmbeddedControl => lastEmbeddedControl is { IsEnabled: true, Visibility: Visibility.Visible } last
        ? last : (Control?)embeddedControls.FirstOrDefault(button => button.IsEnabled && button.Visibility == Visibility.Visible) ?? this;

    private void CloseEmbedded()
    {
        if (embeddedViewport is null) return;
        var viewport = embeddedViewport;
        embeddedViewport = null;
        embeddedReady = false;
        embeddedChrome?.Stop();
        embeddedChrome = null; embeddedWake = lastEmbeddedControl = null; embeddedPlaying = false;
        embeddedSlot?.Owner.Unbind(viewport);
        viewport.Retire();
        if (embeddedStage?.Parent is Grid frame) frame.Children.Remove(embeddedStage);
        embeddedStage = null;
        embeddedToolbar = null;
        embeddedControls = [];
        body.Visibility = Visibility.Visible;
    }
}
