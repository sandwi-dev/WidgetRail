using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private const string CompactMediaLayout = "host.embedded-media.compact";
    private static readonly WidgetSurfaceHints CompactMediaHints = new()
    { PreferredWidth = 480, PreferredHeight = 270, MinimumWidth = 320, MinimumHeight = 180 };

    private async Task PinMediaAsync(string widgetId, PinnedSurface? expected = null)
    {
        if (retired || owner is null || mediaOwner is null || expected is not null && !ReferenceEquals(pinned, expected)) return;
        var intent = ++pinIntent;
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || intent != pinIntent || widgetId != activeWidget ||
                    expected is not null && !ReferenceEquals(pinned, expected) ||
                    owner.Session.GetState(widgetId)?.LastGood is not { } frame || !mediaOwner.IsReady(widgetId) ||
                    !mediaOwner.CanPin(widgetId, expected?.Media) || !frame.Descriptor.PinningSupported ||
                    frame.Snapshot.EmbeddedMediaSession?.SupportedPresentations.Contains(MediaPresentationKind.CompactPinned) != true) return;
                await RemovePinnedCoreAsync();
                var saved = pinnedPreferences.Placements.GetValueOrDefault(widgetId);
                var window = new PinnedWidgetWindow(frame.Descriptor.Name);
                var view = new MediaFullscreenView(compact: true);
                var chrome = new ShellChromeStyles();
                PinnedSurface? created = null;
                try
                {
                    var (placement, limits, displayId) = await PreparePinnedPlacementAsync(window, CompactMediaHints, saved);
                    if (retired || intent != pinIntent || widgetId != activeWidget || !mediaOwner.IsReady(widgetId)) return;
                    view.RegisterChrome(chrome);
                    var appearance = AppearanceForDisplay(displayId);
                    window.InterfaceScale = appearance.InterfaceScale;
                    chrome.Update(ShellPalette, appearance, systemUi.AnimationsEnabled);
                    view.Show(frame.Snapshot.EmbeddedMediaSession!); view.SetInteraction(false);
                    window.SetContent(view); window.Place(placement.Bounds); window.SetOpacity(saved?.OpacityPercent ?? 100);
                    window.Show();
                    // Wait for the peer root without blocking the native dispatcher.
                    var deadline = Environment.TickCount64 + 2000;
                    while (!view.SurfaceHost.IsLoaded)
                    {
                        if (retired || intent != pinIntent || Environment.TickCount64 > deadline) return;
                        await Task.Delay(16, lifetime.Token);
                    }
                    var receipt = mediaOwner.EnterCompact(frame, view.SurfaceHost);
                    if (receipt is null) return;
                    await mediaOwner.WaitForCompactPlacementAsync();
                    if (!owner.Session.IsMediaPresentationCurrent(receipt) || !ReferenceEquals(mediaOwner.CompactPresentation, receipt)) return;
                    created = new(null, null, window, limits, placement.Monitor)
                    {
                        Media = receipt, MediaView = view, MediaStyles = chrome,
                        MediaCurrent = () => owner.Session.IsMediaPresentationCurrent(receipt) && ReferenceEquals(mediaOwner.CompactPresentation, receipt),
                        Appearance = WidgetSurfaceAppearance.Theme,
                        DisplayId = displayId,
                        LogicalPlacement = PinnedPlacementPolicy.Capture(placement.Bounds, placement.Monitor, limits, CompactMediaLayout, window.OpacityPercent),
                    };
                    var current = created;
                    current.PopupTheme.Attach(view);
                    view.CommandRequested += command => { if (ReferenceEquals(pinned, current) && PinnedInputActive) mediaOwner.DispatchCompact(command); };
                    view.NavigationRequested += command => { if (ReferenceEquals(pinned, current) && PinnedInputActive) mediaOwner.DispatchCompact(command); };
                    view.ExitRequested += () => { if (ReferenceEquals(pinned, current)) ExitPinnedInteraction(restoreMain: true); };
                    window.CloseRequested += () => _ = UnpinAsync(save: true, current);
                    window.ThemeChanged += () => ApplyPinnedAppearance(current);
                    window.PlacementEnvironmentChanged += () => QueuePinnedPlacementEnvironment(current);
                    window.ForegroundChanged += focused =>
                    {
                        current.HasForeground = focused;
                        if (focused) current.FocusAcquired?.TrySetResult();
                        else if (ReferenceEquals(pinned, current)) ExitPinnedInteraction(restoreMain: false);
                    };
                    pinned = current;
                    ApplyPinnedAppearance(current); RefreshCompactView();
                    await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(widgetId), LifecycleFor(widgetId), lifetime.Token);
                    await SavePinnedAsync(current);
                    UpdateTrayHelp(); UpdateDiagnostics();
                }
                finally
                {
                    if (created is null || !ReferenceEquals(pinned, created))
                    { await mediaOwner.ReleaseCompactAsync(); chrome.Dispose(); window.Dispose(); }
                }
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "ordinary_input_stale" or "embedded_media_stale") { }
        catch (Exception error) { ReportFailure(error); }
    }

    private void RefreshCompactView()
    {
        if (pinned is not { Media: not null } current || mediaOwner is null) return;
        if (!current.IsCurrent || mediaOwner.CompactState is not { } state)
        { current.Window.Hide(); _ = UnpinAsync(save: false, current); return; }
        current.MediaView!.Show(state.Declaration);
        current.MediaView.UpdatePlayback(mediaOwner.CompactPlayback);
    }

    private bool RouteCompactButton(PinnedSurface pin, ControllerButton button, ControllerEventPhase phase)
    {
        if (button == ControllerButton.B) return false;
        if (!pin.IsCurrent || !PinnedInputActive || phase != ControllerEventPhase.Pressed &&
            !(phase == ControllerEventPhase.Repeated && button is ControllerButton.LeftTrigger or ControllerButton.RightTrigger)) return true;
        if (button != ControllerButton.A) pin.MediaView!.RevealControls();
        switch (button)
        {
            case ControllerButton.A: pin.MediaView!.ActivateFocused(); break;
            case ControllerButton.X: mediaOwner?.DispatchCompact(EmbeddedMediaHostCommand.TogglePlayback); break;
            case ControllerButton.LeftTrigger: mediaOwner?.DispatchCompact(EmbeddedMediaHostCommand.SeekBackward); break;
            case ControllerButton.RightTrigger: mediaOwner?.DispatchCompact(EmbeddedMediaHostCommand.SeekForward); break;
            case ControllerButton.LeftBumper: mediaOwner?.DispatchCompact(EmbeddedMediaCommand.Previous); break;
            case ControllerButton.RightBumper: mediaOwner?.DispatchCompact(EmbeddedMediaCommand.Next); break;
        }
        return true;
    }
}
