using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private const string PopupPinLayout = "host.browser.popup";
    private static readonly WidgetSurfaceHints PopupPinHints = new()
    { PreferredWidth = 640, PreferredHeight = 400, MinimumWidth = 320, MinimumHeight = 240 };

    private async Task RequestBrowserPopupPinAsync(BrowserSurface source)
    {
        if (retired || !visible || browserOwner is null || !source.IsReady) return;
        var expected = pinned;
        if (ReferenceEquals(expected?.Popup?.Child, source))
        { await UnpinAsync(save: true, expected); return; }
        if (catalogItems.FirstOrDefault(widget => widget.Id == source.WidgetId)?.FullWidgetPinningSupported != true) return;
        if (expected is not null && await ShowBrowserChoiceAsync(source, "Replace pin?",
            "The current pinned surface will return to its widget. This live pop-out will become the pin.",
            (HostDialogChoice[])[new("replace", "Replace pin")], lifetime.Token) != "replace") return;
        if (retired || !visible || !ReferenceEquals(pinned, expected)) return;
        await PinBrowserPopupAsync(source.WidgetId, expected, source);
    }

    private async Task PinBrowserPopupAsync(string widgetId, PinnedSurface? expected = null, BrowserSurface? expectedChild = null)
    {
        if (retired || owner is null || browserOwner is not { } browsers ||
            expected is not null && !ReferenceEquals(pinned, expected)) return;
        var child = expectedChild ?? browsers.PinnablePopup(widgetId);
        if (child is null || ReferenceEquals(pinned?.Popup?.Child, child) || !browsers.CanPinPopup(widgetId, child)) return;
        var intent = ++pinIntent;
        var guard = CapturePinIntentFailureGuard(intent);
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || intent != pinIntent || !browsers.CanPinPopup(widgetId, child) ||
                    owner.Session.GetState(widgetId)?.LastGood is not { Descriptor.FullWidgetPinningSupported: true } frame ||
                    expected is not null && !ReferenceEquals(pinned, expected)) return;
                // Detach a child before moving/destroying its parent's pinned
                // XamlRoot. A different popup lease must first return home so
                // the owner retains its single borrowed-child contract.
                if (pinned?.Popup is not null) await RemovePinnedCoreAsync();
                var saved = pinnedPreferences.Placements.GetValueOrDefault(widgetId);
                var window = new PinnedWidgetWindow(frame.Descriptor.Name + " pop-out", (nint)hostWindow);
                var slot = new BrowserSlot { Active = true, AcceptsInput = false, IsTabStop = true };
                AutomationProperties.SetAutomationId(slot, "Browser.PinnedPopup");
                AutomationProperties.SetName(slot, frame.Descriptor.Name + " pop-out");
                BrowserPopupPresentation? lease = null;
                PinnedSurface? created = null;
                try
                {
                    var (placement, limits, displayId) = await PreparePinnedPlacementAsync(window, PopupPinHints, saved);
                    if (retired || intent != pinIntent || !browsers.CanPinPopup(widgetId, child)) return;
                    window.InterfaceScale = AppearanceForDisplay(displayId).InterfaceScale;
                    window.SetContent(slot); window.Place(placement.Bounds); window.SetOpacity(saved?.OpacityPercent ?? 100);
                    window.Show();
                    var deadline = Environment.TickCount64 + 2000;
                    while (!slot.CanDisplay)
                    {
                        if (retired || intent != pinIntent || Environment.TickCount64 > deadline) return;
                        await Task.Delay(16, lifetime.Token);
                    }
                    lease = await browsers.PinPopupAsync(child, slot);
                    if (lease is not { IsCurrent: true } || retired || intent != pinIntent) return;
                    await RemovePinnedCoreAsync();
                    if (!lease.IsCurrent || retired || intent != pinIntent) return;
                    created = new(null, null, window, limits, placement.Monitor)
                    {
                        Popup = lease, Appearance = WidgetSurfaceAppearance.Theme, DisplayId = displayId,
                        LogicalPlacement = PinnedPlacementPolicy.Capture(placement.Bounds, placement.Monitor, limits, PopupPinLayout, window.OpacityPercent),
                    };
                    var current = created;
                    current.PopupTheme.Attach(slot);
                    slot.InteractionChanged = PublishControllerGuide;
                    window.CloseRequested += () => _ = UnpinAsync(save: true, current);
                    window.ThemeChanged += () => ApplyPinnedAppearance(current);
                    window.PlacementEnvironmentChanged += () => QueuePinnedPlacementEnvironment(current);
                    window.ForegroundChanged += focused =>
                    {
                        current.HasForeground = focused;
                        if (focused) current.FocusAcquired?.TrySetResult();
                        else if (ReferenceEquals(pinned, current)) ExitPinnedInteraction(restoreMain: false);
                        PublishControllerGuide();
                    };
                    pinned = current;
                    ApplyPinnedAppearance(current);
                    await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(widgetId), LifecycleFor(widgetId), lifetime.Token);
                    await SavePinnedAsync(current);
                    UpdateTrayHelp(); UpdateDiagnostics();
                    if (foreground) QueueEntryFocus();
                }
                finally
                {
                    if (created is null || !ReferenceEquals(pinned, created))
                    {
                        try { if (lease is not null) await browsers.ReleasePopupPresentationAsync(lease); }
                        finally { slot.Retire(); window.Dispose(); }
                    }
                }
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportOperationFailure(error, guard); }
    }

    private void InvalidatePopupPin(BrowserPopupPresentation popup)
    {
        if (pinned is not { } current || !ReferenceEquals(current.Popup, popup)) return;
        ExitPinnedInteraction(restoreMain: !retired && visible);
        current.SetInput(false);
        current.Window.Hide();
        // Parent stop/profile teardown can own transitions. Queue the shared
        // removal rather than awaiting it from the child's close callback.
        DispatcherQueue.TryEnqueue(() => _ = UnpinAsync(save: false, current));
    }
}
