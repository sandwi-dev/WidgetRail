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
        if(pins.FirstOrDefault(pin=>ReferenceEquals(pin.Popup?.Child,source)) is { } existing)
        { await UnpinAsync(save:true,existing);return; }
        if (catalogItems.FirstOrDefault(widget => widget.Id == source.WidgetId)?.FullWidgetPinningSupported != true) return;
        await PinBrowserPopupAsync(source.WidgetId,expectedChild:source);
    }

    private async Task PinBrowserPopupAsync(string widgetId, PinnedSurface? expected = null, BrowserSurface? expectedChild = null, string? preferredPlacementId = null)
    {
        if (retired || owner is null || browserOwner is not { } browsers ||
            expected is not null && !pins.Contains(expected)) return;
        var child = expectedChild ?? browsers.PinnablePopup(widgetId);
        if (child is null || ReferenceEquals(pinned?.Popup?.Child, child) || !browsers.CanPinPopup(widgetId, child)) return;
        var intent = pinIntent;
        var guard = CapturePinIntentFailureGuard(intent);
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                intent=++pinIntent;
                guard = CapturePinIntentFailureGuard(intent);
                if (retired || !browsers.CanPinPopup(widgetId, child) ||
                    owner.Session.GetState(widgetId)?.LastGood is not { Descriptor.FullWidgetPinningSupported: true } frame ||
                    expected is not null && !pins.Contains(expected)) return;
                // Detach a child before retiring its parent's pinned XamlRoot.
                if(expected is null && pins.Count>=MaximumPinnedSurfaces){ReportPinCapacity(target=>PinBrowserPopupAsync(widgetId,target,child,preferredPlacementId));return;}
                if (expected?.Popup is not null) await RemovePinnedCoreAsync(target:expected,forgetRemembered:true);
                var preferred = preferredPlacementId ?? (expected?.Popup is not null && expected.WidgetId == widgetId ? expected.LayoutId : null);
                var placementId = preferred is not null && !pins.Any(pin => pin.WidgetId == widgetId && pin.LayoutId == preferred) ? preferred :
                    Enumerable.Range(1,MaximumPinnedSurfaces).Select(index=>index==1?PopupPinLayout:PopupPinLayout+"."+index)
                        .First(id=>!pins.Any(pin=>pin.WidgetId==widgetId&&pin.LayoutId==id));
                var saved = pinnedPreferences.Find(widgetId, placementId);
                var window = new PinnedWidgetWindow(frame.Descriptor.Name + " additional page", (nint)hostWindow);
                var slot = new BrowserSlot { Active = true, AcceptsInput = false, IsTabStop = true };
                AutomationProperties.SetAutomationId(slot, "Browser.PinnedPopup");
                AutomationProperties.SetName(slot, frame.Descriptor.Name + " additional page");
                BrowserPopupPresentation? lease = null;
                PinnedSurface? created = null;
                try
                {
                    var (placement, limits, displayId) = await PreparePinnedPlacementAsync(window, PopupPinHints, saved, expected);
                    if (retired || intent != pinIntent || !browsers.CanPinPopup(widgetId, child)) return;
                    window.InterfaceScale = AppearanceForDisplay(displayId).InterfaceScale;
                    window.SetContent(slot); window.Place(placement.Bounds); window.SetOpacity(saved?.OpacityPercent ?? 100);
                    window.PrepareContent();
                    var deadline = Environment.TickCount64 + 2000;
                    while (!slot.CanDisplay)
                    {
                        if (retired || intent != pinIntent || Environment.TickCount64 > deadline) return;
                        await Task.Delay(16, lifetime.Token);
                    }
                    lease = await browsers.PinPopupAsync(child, slot);
                    if (lease is not { IsCurrent: true } || retired || intent != pinIntent) return;
                    if(expected is not null && pins.Contains(expected))await RemovePinnedCoreAsync(target:expected,forgetRemembered:true);
                    if (!lease.IsCurrent || retired || intent != pinIntent) return;
                    created = new(null, null, window, limits, placement.Monitor)
                    {
                        Popup = lease, PopupPlacementId=placementId, Appearance = WidgetSurfaceAppearance.Theme, DisplayId = displayId,
                        LogicalPlacement = PinnedPlacementPolicy.Capture(placement.Bounds, placement.Monitor, limits, placementId, window.OpacityPercent),
                    };
                    var current = created;
                    current.PopupTheme.Attach(window.AutomationRoot);
                    slot.InteractionChanged = PublishControllerGuide;
                    window.CloseRequested += () => _ = UnpinAsync(save: true, current);
                    window.ThemeChanged += () => ApplyPinnedAppearance(current);
                    window.PlacementEnvironmentChanged += () => QueuePinnedPlacementEnvironment(current);
                    RegisterPin(current);
                    ApplyPinnedAppearance(current);
                    window.Show();
                    await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(widgetId), LifecycleFor(widgetId), lifetime.Token);
                    if(saved is null)await SavePinnedAsync(current);
                    UpdateTrayHelp(); UpdateDiagnostics();
                    if (foreground) QueueEntryFocus();
                }
                finally
                {
                    if (created is null || !pins.Contains(created))
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
        if (pins.FirstOrDefault(pin=>ReferenceEquals(pin.Popup,popup)) is not { } current)return;
        if(ReferenceEquals(current,pinned))ExitPinnedInteraction(restoreMain: !retired && visible);
        current.SetInput(false);
        current.Window.Hide();
        // Parent stop/profile teardown can own transitions. Queue the shared
        // removal rather than awaiting it from the child's close callback.
        DispatcherQueue.TryEnqueue(() => _ = UnpinAsync(save: false, current));
    }
}
