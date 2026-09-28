using WidgetRail.OverlayFrontend.WinUI.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        Grid.SetRowSpan(fullscreenView, 3);
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
            },
            FailureChanged = widget =>
            {
                if (widget == activeWidget && owner?.Session.GetState(widget) is { Failure: null, LastGood: { } frame })
                    ShowPresentationStatus(frame.Descriptor.Name);
            },
        };
        mediaOwner.SetFullscreenHost(fullscreenView.SurfaceHost);
        mediaOwner.FullscreenChanged += RefreshFullscreenView;
        ReconcileMediaHostState();
    }

    private void RefreshFullscreenView()
    {
        var showing = mediaOwner?.FullscreenState is not null;
        WidgetHost.IsEnabled = Tray.IsEnabled = !showing;
        if (showing) { surface?.ResetPressedStyles(); surface?.DismissTransientControl(); fullscreenView.Show(mediaOwner!.FullscreenState!.Declaration); }
        else fullscreenView.Hide();
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
        if (phase != ControllerEventPhase.Pressed) return true;
        fullscreenOwnedButtons.Add(button);
        if (button == ControllerButton.B) mediaOwner!.ExitFullscreen();
        else if (button == ControllerButton.View) { mediaOwner!.ExitFullscreen(); SetInteractive(false); FocusTray(); }
        else if (button == ControllerButton.X) mediaOwner!.DispatchFullscreen(EmbeddedMediaHostCommand.TogglePlayback);
        else if (button == ControllerButton.LeftTrigger) mediaOwner!.DispatchFullscreen(EmbeddedMediaHostCommand.SeekBackward);
        else if (button == ControllerButton.RightTrigger) mediaOwner!.DispatchFullscreen(EmbeddedMediaHostCommand.SeekForward);
        else if (button == ControllerButton.A) fullscreenView.ActivateFocused();
        return true;
    }

    private void ReconcileMediaHostState() => mediaOwner?.SetHostState(activeWidget,
        !retired && visible && !switching, !retired && visible && !switching && interactive && foreground);

    private void ShowPresentationStatus(string widgetName)
    {
        if (retired) return;
        var code = activeWidget is { } id ? mediaOwner?.GetFailure(id) : null;
        Status.Text = code switch
        {
            null => widgetName,
            "media-session-capacity" => "Four media sessions are already open. Close one before opening another.",
            "media-document-admission-failed" => "The widget's media could not be loaded. Retry to restart this widget.",
            _ => "The widget's media stopped unexpectedly. Retry to restart this widget.",
        };
        Retry.Visibility = code is not null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
