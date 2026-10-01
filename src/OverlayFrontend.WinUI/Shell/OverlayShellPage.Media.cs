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
    private readonly MediaFullscreenView fullscreenView = new();
    private readonly HashSet<ControllerButton> fullscreenOwnedButtons = [];
    internal bool IsMediaFullscreen => mediaOwner?.FullscreenWidgetId is not null;
    internal event Action? MediaPresentationChanged;

    private void InitializeFullscreenView()
    {
        ((Grid)Content).Children.Add(fullscreenView);
        fullscreenView.ExitRequested += () => mediaOwner?.ExitFullscreen();
        fullscreenView.CommandRequested += command => mediaOwner?.DispatchFullscreen(command);
    }

    private void InitializeMediaOwner()
    {
        if (owner is null || mediaOwner is not null || retired) return;
        mediaOwner = new(owner.Session, MediaParking)
        {
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
        mediaOwner.Changed += TryRestoreCompact;
        ReconcileMediaHostState();
    }

    private void RefreshFullscreenView()
    {
        var showing = mediaOwner?.FullscreenState is not null;
        // Complete any ordinary resize before borrowing the media viewport. The
        // fullscreen layer has its own geometry and is not a widget size change.
        surface?.SettleWidgetResize();
        WidgetHost.IsEnabled = Tray.IsEnabled = !showing;
        if (showing)
        {
            surface?.ResetPressedStyles(); surface?.DismissTransientControl();
            ProductionLayout.Visibility = Visibility.Collapsed;
            fullscreenView.Show(mediaOwner!.FullscreenState!.Declaration);
            // A handoff is infrequent and must publish a fully arranged native
            // destination before EmbeddedMediaOwner reparents the live WebView.
            fullscreenView.UpdateLayout();
        }
        else
        {
            fullscreenView.Hide();
            if (shellViewport.Width > 0 && shellViewport.Height > 0) ConfigureProductionViewport(shellViewport);
            ProductionLayout.Visibility = Visibility.Visible;
            ProductionLayout.UpdateLayout();
        }
        MediaPresentationChanged?.Invoke();
        SizingChanged?.Invoke();
        if (!retired && visible && foreground) DispatcherQueue.TryEnqueue(() => QueueEntryFocus());
    }

    private bool RouteFullscreenButton(ControllerButton button, ControllerEventPhase phase)
    {
        // A B/View/A press can close this presentation. Its release still belongs
        // to the host, never to a shortcut on the newly revealed widget or tray.
        if (phase == ControllerEventPhase.Released && fullscreenOwnedButtons.Remove(button)) return true;
        if (!IsMediaFullscreen) return false;
        if (phase != ControllerEventPhase.Pressed && !(phase == ControllerEventPhase.Repeated &&
            button is ControllerButton.LeftTrigger or ControllerButton.RightTrigger)) return true;
        fullscreenOwnedButtons.Add(button);
        if (button != ControllerButton.A) fullscreenView.RevealControls();
        if (button == ControllerButton.B) mediaOwner!.ExitFullscreen();
        else if (button == ControllerButton.View) { mediaOwner!.ExitFullscreen(); SetInteractive(false); FocusTray(); }
        else if (button == ControllerButton.X) mediaOwner!.DispatchFullscreen(EmbeddedMediaHostCommand.TogglePlayback);
        else if (button == ControllerButton.LeftTrigger) mediaOwner!.DispatchFullscreen(EmbeddedMediaHostCommand.SeekBackward);
        else if (button == ControllerButton.RightTrigger) mediaOwner!.DispatchFullscreen(EmbeddedMediaHostCommand.SeekForward);
        else if (button == ControllerButton.A) fullscreenView.ActivateFocused();
        return true;
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
