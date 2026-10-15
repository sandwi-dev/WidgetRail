using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private WidgetViewPresenter? guideLayoutWait;
    private bool guideUpdateQueued;

    private void PublishControllerGuide()
    {
        IReadOnlyList<ControllerGuideHint>? shell = null;
        object? shellContext = null;
        if (HostChoiceActive)
        {
            shell = hostChoiceDialog?.SupportsMessageScroll == true
                ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A),
                    new(ControllerPrompt.B, "Cancel", ControllerButton.B, Required: true), new(ControllerPrompt.RightStickMove, "Scroll")]
                : (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A), new(ControllerPrompt.B, "Back", ControllerButton.B, Required: true)];
            shellContext = "host-choice";
            if (hostChoiceDialog?.SupportsDismissChoice == true)
                shell = (ControllerGuideHint[])[.. (hostChoiceDialog.FocusedChoiceIsActionable ? new ControllerGuideHint[] { new(ControllerPrompt.A, "Open", ControllerButton.A) } : []),
                    new(ControllerPrompt.X, "Dismiss", ControllerButton.X), new(ControllerPrompt.B, "Close", ControllerButton.B, Required: true)];
            if (hostChoicePin is not null)
                shell = (ControllerGuideHint[])[.. shell, new(ControllerPrompt.View, "Leave pin", ControllerButton.View)];
        }
        else if (!interactive && notificationBell.FocusState != FocusState.Unfocused)
        {
            shell = (ControllerGuideHint[])[new(ControllerPrompt.A, "Notifications", ControllerButton.A),
                new(ControllerPrompt.B, "Hide overlay", ControllerButton.B, Required: true)];
            shellContext = "notification-bell";
        }
        else if (SystemFilePickerOpen)
        { shell = Array.Empty<ControllerGuideHint>(); shellContext = "system-file-picker"; }
        else if (nativeBrowserPickerActive)
        {
            shell = (ControllerGuideHint[])[new(ControllerPrompt.LeftStickMove, "Pointer"),
                new(ControllerPrompt.A, "Click / hold to drag", ControllerButton.A),
                new(ControllerPrompt.RightStickMove, "Scroll"), new(ControllerPrompt.B, nativeBrowserPickerStatus ?? "Cancel", ControllerButton.B)];
            shellContext = ("native-browser-picker", nativeBrowserPickerStatus);
        }
        else if (LocalInstallActive)
        {
            shell = SystemFilePickerOpen ? Array.Empty<ControllerGuideHint>() : (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A),
                new(ControllerPrompt.B, localInstallDialog?.Finished == true ? "Done" : "Cancel", ControllerButton.B, Required: true)];
            shellContext = ("local-install", SystemFilePickerOpen, localInstallDialog?.Finished);
        }
        else if (pinnedAdjustment is not null || savingPinnedAdjustment)
        { shell = ControllerGuideModel.WithHost(PinnedPlacementHints()); shellContext = "pin-placement"; }
        else if (trayMenu is null && interactive && showingStoppedWidget)
        {
            shell = ControllerGuideModel.WithHost((ControllerGuideHint[])[new(ControllerPrompt.X, "Reload", ControllerButton.X),
                new(ControllerPrompt.B, "Open radial", ControllerButton.B, Required: true)]);
            shellContext = "widget-stopped";
        }
        else if (trayMenu is not null || interactive && RecoveryVisible)
        {
            shell = ControllerGuideModel.WithHost(ControllerGuideModel.ResolveShellHints(interactive, RecoveryVisible,
                Retry.Visibility == Visibility.Visible, trayMenu is not null, () => []) ?? []);
            shellContext = trayMenu is not null ? "menu" : "recovery";
        }
        else if (reordering)
        { shell = ControllerGuideModel.TrayHints(true); shellContext = "reorder"; }
        else if (expandedBrowser is { } browser)
        {
            var hints = browser.Surface?.ControllerTextEntry is { } keyboard
                ? WidgetViewPresenter.CaptureKeyboardGuide(keyboard) : WidgetViewPresenter.CaptureBrowserGuide(browser);
            shell = hints.Any(hint => hint.Button == ControllerButton.B) ? hints
                : (ControllerGuideHint[])[.. hints, new(ControllerPrompt.B, "Return to widget", ControllerButton.B)];
            shellContext = (browser, browser.Surface?.IsBrowsing, browser.Surface?.HasDialog);
        }
        else if (PinnedInputActive && pinned?.Popup is { } popup)
        {
            var hints = popup.Child.ControllerTextEntry is { } keyboard
                ? WidgetViewPresenter.CaptureKeyboardGuide(keyboard) : WidgetViewPresenter.CaptureBrowserGuide(popup.Slot);
            shell = hints.Any(hint => hint.Button == ControllerButton.B) ? hints
                : (ControllerGuideHint[])[.. hints, new(ControllerPrompt.B, "Return to overlay", ControllerButton.B)];
            shellContext = (popup, popup.Child.IsBrowsing, popup.Child.HasDialog);
        }
        if (shell is not null)
        {
            WaitForGuideLayout(null);
            // Explicit host interactions (such as changing pin opacity) are
            // already settled and must not debounce their own live readout.
            trayGuide.Present((shellContext, pinnedAdjustment?.Placement.OpacityPercent, savingPinnedAdjustment), true, [], shell);
            return;
        }

        var host = interactive ? ControllerGuideModel.WithHost([]) : RadialOpen
            ? ControllerGuideModel.WithHost(RadialGuideHints()) : ControllerGuideModel.TrayHints(false);
        if (!interactive && !RadialOpen)
            host = host.Select(hint => hint.Button == ControllerButton.B ? hint with { Label = "Open radial" } : hint).ToArray();
        var committed = !retired && visible && !switching && activeWidget == requestedWidget && surface is not null &&
            activeWidget is { } id && retainedSurfaces.TryGetValue(id, out var retained) &&
            ReferenceEquals(retained.Presenter, surface) && retained.PresentedLifecycle == DesiredLifecycle;
        var ready = committed && surface!.IsGuidePresentationReady(requireFocus: interactive);
        WaitForGuideLayout(committed && !ready ? surface : null);
        IReadOnlyList<ControllerGuideHint> actions = ready ? interactive
            ? surface!.CaptureControllerGuide() : DashboardGuideHints() : [];
        // Requested input mode is not a new displayed owner. Keep informational
        // actions through the lifecycle/row handoff; readiness commits their new
        // meaning. Input routing changes immediately; all guide pixels publish
        // together so old widget hints never mix with new navigation hints.
        trayGuide.Present((surface, activeWidget, surface?.ControllerGuideContext), ready, actions, host);
    }

    private void WaitForGuideLayout(WidgetViewPresenter? presenter)
    {
        if (ReferenceEquals(guideLayoutWait, presenter)) return;
        if (guideLayoutWait is not null) guideLayoutWait.LayoutUpdated -= GuideLayoutUpdated;
        guideLayoutWait = presenter;
        if (guideLayoutWait is not null) guideLayoutWait.LayoutUpdated += GuideLayoutUpdated;
    }

    private void GuideLayoutUpdated(object? sender, object args)
    {
        if (guideUpdateQueued) return;
        guideUpdateQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            guideUpdateQueued = false;
            if (!retired && guideLayoutWait is not null) PublishControllerGuide();
        });
    }

    private void InitializeSemanticGuide()
    {
        trayGuide.Invoked += async hint =>
        {
            if (hint.Prompt == ControllerPrompt.Guide) { HideRequested?.Invoke(); return; }
            if (hint.Button is not { } button) return;
            // Existing routing validates current widget/scope/row authority. The
            // guide never dispatches a captured action or acquires native focus.
            await RouteButtonAsync(button, ControllerEventPhase.Pressed, ControllerInputOrigin.AccessibilityAutomation);
            await RouteButtonAsync(button, ControllerEventPhase.Released, ControllerInputOrigin.AccessibilityAutomation);
        };
    }
}
