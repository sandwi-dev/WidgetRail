using WidgetRail.OverlayFrontend.WinUI.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private EmbeddedMediaOwner? mediaOwner;
    private Browser.BrowserOwner? browserOwner;
    private readonly MediaFullscreenView fullscreenView = new();
    private readonly MediaFullscreenLayer fullscreenLayer = new();
    internal bool IsMediaFullscreen => fullscreenLayer.IsOpen;
    internal event Action? MediaPresentationChanged;

    private void InitializeFullscreenView()
    {
        fullscreenLayer.Attach((Grid)Content);
        fullscreenLayer.Park(fullscreenView);
        fullscreenLayer.CanRestoreFocus = () => !retired && visible && foreground;
        fullscreenLayer.Changed += RefreshFullscreenChrome;
        fullscreenView.ExitRequested += () => fullscreenLayer.Close();
        fullscreenView.CommandRequested += command => mediaOwner?.DispatchFullscreen(command);
    }

    private void InitializeMediaOwner()
    {
        if (owner is null || mediaOwner is not null || retired) return;
        browserOwner = new(owner.Session, MediaParking, options.SettingsRoot)
        { OpenExternal = OpenBrowserExternalAsync };
        mediaOwner = new(owner.Session, MediaParking)
        {
            CreateStage = fullscreenLayer.CreateMediaStage,
            Diagnostic = (widget, code) =>
            {
                System.Diagnostics.Trace.WriteLine("WinUI embedded media: " + code);
                Diagnostics.FrontendFailureLog.Current.Write("embedded-media", null, $"widget={widget} code={code}");
            },
            FailureChanged = widget =>
            {
                if (widget == activeWidget && owner?.Session.GetState(widget) is { Failure: null, LastGood: { } frame })
                    ShowPresentationStatus(frame.Descriptor.Name);
            },
        };
        mediaOwner.SetFullscreenHost(fullscreenView.SurfaceHost);
        mediaOwner.FullscreenChanged += RefreshFullscreenView;
        mediaOwner.CompactChanged += RefreshCompactView;
        ReconcileMediaHostState();
    }

    private void RefreshFullscreenView()
    {
        if (mediaOwner?.FullscreenState is { } state)
        {
            fullscreenView.Show(state.Declaration);
            if (!fullscreenLayer.IsOpen)
            {
                var previous = FocusManager.GetFocusedElement(XamlRoot) as Control;
                var stage = mediaOwner.FullscreenStage;
                var origin = fullscreenLayer.BoundsOf(mediaOwner.FullscreenOrigin);
                if (origin is { } viewport) origin = MediaTransitionGeometry.Fit(viewport, state.Declaration.AspectRatio);
                stage?.EnterFullscreen();
                _ = fullscreenLayer.ShowAsync(new(fullscreenView, fullscreenView, HandleEmbeddedFullscreen,
                    fullscreenView.MoveFocus, fullscreenView.Enter,
                    () => { fullscreenView.Hide(); fullscreenLayer.Park(fullscreenView); mediaOwner?.ExitFullscreen(); },
                    () => { if (previous is { IsLoaded: true, IsEnabled: true }) previous.Focus(FocusState.Keyboard); else QueueEntryFocus(); },
                    Origin: origin, Chrome: fullscreenView.TransitionChrome, MediaBounds: () => stage?.VideoBounds ?? fullscreenView.VideoBounds,
                    MotionContent: stage));
            }
        }
        else if (ReferenceEquals(fullscreenLayer.Owner, fullscreenView)) fullscreenLayer.Close(restoreFocus: false);
    }

    private void RefreshFullscreenChrome()
    {
        var showing = fullscreenLayer.IsOpen;
        surface?.SettleWidgetResize();
        WidgetHost.IsEnabled = Tray.IsEnabled = !showing;
        if (showing)
        {
            surface?.ResetPressedStyles();
            ProductionLayout.Visibility = fullscreenLayer.IsTransitioning ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            if (shellViewport.Width > 0 && shellViewport.Height > 0) ConfigureProductionViewport(shellViewport);
            ProductionLayout.Visibility = Visibility.Visible;
            ProductionLayout.UpdateLayout();
        }
        MediaPresentationChanged?.Invoke();
        SizingChanged?.Invoke();
    }

    private bool HandleEmbeddedFullscreen(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase != ControllerEventPhase.Pressed && !(phase == ControllerEventPhase.Repeated &&
            button is ControllerButton.LeftTrigger or ControllerButton.RightTrigger)) return true;
        if (button == ControllerButton.B) return false;
        if (button != ControllerButton.A) fullscreenView.RevealControls();
        if (button == ControllerButton.X) mediaOwner?.DispatchFullscreen(EmbeddedMediaHostCommand.TogglePlayback);
        else if (button == ControllerButton.LeftTrigger) mediaOwner?.DispatchFullscreen(EmbeddedMediaHostCommand.SeekBackward);
        else if (button == ControllerButton.RightTrigger) mediaOwner?.DispatchFullscreen(EmbeddedMediaHostCommand.SeekForward);
        else if (button == ControllerButton.A) fullscreenView.ActivateFocused();
        return true;
    }

    private bool RouteFullscreenButton(ControllerButton button, ControllerEventPhase phase)
    {
        if (IsMediaFullscreen && button == ControllerButton.View && phase == ControllerEventPhase.Pressed)
        {
            // Own this gesture before revealing the tray.
            fullscreenLayer.Route(button, phase);
            fullscreenLayer.Close(restoreFocus: false);
            SetInteractive(false); FocusTray(); return true;
        }
        return fullscreenLayer.Route(button, phase);
    }
    private void ReconcileMediaHostState() => mediaOwner?.SetHostState(activeWidget,
        !retired && PresentationVisible, !retired && visible && !switching && interactive && foreground,
        acceptsDashboardPlayback: !retired && visible && foreground && !switching && !interactive &&
            trayMenu is null && !reordering && activeWidget == requestedWidget,
        retainFullscreenDuringExit: retainingExitPresentation && !retired);

    private void ShowPresentationStatus(string widgetName)
    {
        if (retired) return;
        var code = activeWidget is { } id ? mediaOwner?.GetFailure(id) : null;
        var message = code switch
        {
            null => null,
            "media-session-capacity" => "Four media sessions are already open. Close one before opening another.",
            "media-document-admission-failed" => "The widget's media could not be loaded. Retry to restart this widget.",
            _ => "The widget's media stopped unexpectedly. Retry to restart this widget.",
        };
        if (message is null)
        {
            CompleteStartupPresentation();
            ++recoveryAnnouncementVersion;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ProductionRoot, widgetName);
            StatusChrome.Visibility = Visibility.Collapsed;
            Status.Text = widgetName; // diagnostic/accessibility lookup, no permanent status row
            Retry.Visibility = Visibility.Collapsed;
            UpdateTrayHelp();
        }
        else ShowRecovery(message, true);
    }

    private void ShowRecovery(string message, bool canRetry)
    {
        CompleteStartupPresentation(failed: true);
        var changed = !RecoveryVisible || Status.Text != message;
        openingIndicator?.Clear();
        Status.Text = message;
        StatusChrome.Visibility = Visibility.Visible;
        Retry.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrayHelp();
        if (changed) QueueRecoveryAnnouncement();
        if (canRetry && visible && foreground && interactive)
            DispatcherQueue.TryEnqueue(() => { if (!retired && RecoveryVisible && visible && foreground && interactive) Retry.Focus(FocusState.Keyboard); });
    }

    private bool RecoveryVisible => StatusChrome.Visibility == Visibility.Visible;

    private long recoveryAnnouncementVersion;
    private void QueueRecoveryAnnouncement()
    {
        var version = ++recoveryAnnouncementVersion;
        // Publish only the latest visible status after XAML has observed its
        // text/visibility changes. Recovery must not steal rail/radial focus.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != recoveryAnnouncementVersion || retired || !visible || !foreground ||
                !RecoveryVisible || !Status.IsLoaded) return;
            if (FrameworkElementAutomationPeer.CreatePeerForElement(Status) is { } peer)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                RecoveryAnnounced();
            }
        });
    }
    partial void RecoveryAnnounced();
}
