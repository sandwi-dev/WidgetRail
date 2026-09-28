using System.Diagnostics;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private RetainedWidgetSurface? preparingSurface;
    private CancellationTokenSource? switchCancellation;
    private (long Selection, Exception Error)? preparationFailure;
    private readonly PlatformInputDiagnostics? switchDiagnostics;

    private async Task SelectAsync(string id, bool enterWidget = true)
    {
        if (retired || owner is null) return;
        ResetTrayInteraction();
        ClearTrayFocus();
        interactionAdmission.Invalidate();
        switchCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        switchCancellation = cancellation;
        var token = cancellation.Token;
        var started = Stopwatch.GetTimestamp();
        requestedWidget = id;
        var version = ++selectionVersion;
        preparationFailure = null;
        switching = true;
        interactive = enterWidget;
        UpdateTrayHelp();
        surface?.SetAutomaticFocusEnabled(false);
        surface?.SetPresentationInputEnabled(false);
        surface?.ResetPressedStyles();
        surface?.DismissTransientControl();
        ReconcileMediaHostState();
        Mark("requested");
        try
        {
            await transitions.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (!visible) return;
                var previousId = activeWidget;
                var previous = previousId is not null && retainedSurfaces.TryGetValue(previousId, out var displayed) ? displayed : null;
                if (previous is not null && previous.Presenter.CapturePresentationState() is { } memory)
                    presentationMemory.Remember(StateOwner(previous.Descriptor), memory);
                // The outgoing tree remains drawable, including live regions, but
                // neither it nor the incoming tree may dispatch input during preparation.
                if (previousId is not null) await TryBackgroundAsync(previousId);
                token.ThrowIfCancellationRequested();
                var incoming = await PrepareWidgetSurfaceAsync(id, token);
                preparingSurface = incoming;
                try
                {
                    Mark("preparing");
                    var next = await owner.Session.EstablishPresentationAsync(owner.Session.GetTarget(id), LifecycleFor(id), token);
                    token.ThrowIfCancellationRequested();
                    ApplyWidgetSurfaceFrame(incoming, next);
                    SizePreparingSurface(incoming);
                    incoming.Presenter.Opacity = ReferenceEquals(surface, incoming.Presenter) ? 1 : 0;
                    incoming.Presenter.Visibility = Visibility.Visible;
                    await incoming.Presenter.SetPresentationActiveAsync(true);
                    await WidgetPresentationReadiness.WaitAsync(incoming.Presenter, token);
                    token.ThrowIfCancellationRequested();
                    Mark("layout-ready");
                    // No await inside this publication: identity, extent, background,
                    // native content and authority change in one dispatcher operation.
                    CommitPreparedSurface(incoming);
                    Mark("committed");
                    ShowPresentationStatus(incoming.Descriptor.Name);
                    Tray.SelectedItem = Tray.Items.Cast<BridgeWidgetDescriptor>().FirstOrDefault(widget => widget.Id == id);
                    preferences = preferences with { LastWidget = id, ReopenWidget = true };
                    if (previous is not null && !ReferenceEquals(previous, incoming))
                    {
                        previous.Previews?.SetVisible(false);
                        previous.Presenter.Visibility = Visibility.Collapsed;
                        await previous.Presenter.SetPresentationActiveAsync(false);
                        if (previousId == id) await DisposeRetainedSurfaceAsync(previous);
                        else await TryBackgroundAsync(previousId!);
                    }
                }
                finally
                {
                    preparingSurface = null;
                    if (!ReferenceEquals(surface, incoming.Presenter))
                    {
                        incoming.Presenter.Visibility = Visibility.Collapsed;
                        incoming.Presenter.Opacity = 1;
                        await incoming.Presenter.SetPresentationActiveAsync(false);
                        if (!retainedSurfaces.TryGetValue(id, out var cached) || !ReferenceEquals(cached, incoming))
                            await DisposeRetainedSurfaceAsync(incoming);
                        await TryBackgroundAsync(id);
                    }
                }
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Mark(retired || !visible ? "hidden-or-retired" : version != selectionVersion ? "superseded" :
                preparationFailure is { } failed && failed.Selection == version ? "failed" : "timed-out");
            if (!retired && visible && version == selectionVersion)
            {
                if (preparationFailure is { } failure && failure.Selection == version) ReportFailure(failure.Error);
                else ShowRecovery("The widget is taking too long to open. Try again.", true);
            }
        }
        catch (Exception error)
        {
            Mark("failed");
            if (version == selectionVersion) ReportFailure(error);
        }
        finally
        {
            if (ReferenceEquals(switchCancellation, cancellation)) switchCancellation = null;
            if (version == selectionVersion)
            {
                switching = false;
                surface?.SetPresentationInputEnabled(visible && activeWidget == requestedWidget);
                surface?.SetAutomaticFocusEnabled(MainFocusEnabled && activeWidget == requestedWidget);
                if (MainFocusEnabled && activeWidget == requestedWidget) surface?.Enter(restoreNativeFocus: true);
                else if (visible && !enterWidget && activeWidget == requestedWidget) FocusTray();
                ReconcilePreviewVisibility();
                ReconcileMediaHostState();
                if (!retired && activeWidget == requestedWidget && owner?.Session.GetState(id) is { } latest) ApplyState(latest);
                UpdateDiagnostics();
                if (!retired) _ = ReconcileLifecycleAsync(restore: false);
            }
        }
        void Mark(string phase) => switchDiagnostics?.Write(new System.Text.Json.Nodes.JsonObject
        {
            ["phase"] = phase, ["version"] = version, ["requested"] = id, ["displayed"] = activeWidget,
            ["elapsedMilliseconds"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            ["monotonicTimestamp"] = Stopwatch.GetTimestamp(),
        }.ToJsonString());
    }

    private void CommitPreparedSurface(RetainedWidgetSurface incoming)
    {
        var next = incoming.Frame!;
        surface = incoming.Presenter;
        previewRenderer = incoming.Previews;
        activeWidget = incoming.Descriptor.Id;
        retainedSurfaces[activeWidget] = incoming;
        publication = 0;
        surface.Opacity = 1;
        UpdateSurfaceHints(next.Snapshot.Surface);
        if (shellViewport.Width > 0) ConfigureProductionViewport(shellViewport);
        ReconcilePreviewVisibility();
        ReconcileMediaHostState();
    }

    private void SizePreparingSurface(RetainedWidgetSurface incoming)
    {
        if (incoming.Frame is null || shellViewport.Width <= 0) return;
        var zoom = Appearance.InterfaceScale;
        var bands = ProductionShellGeometry.Bands(Math.Max(1, shellViewport.Height - 38 / zoom));
        var available = new SurfaceExtent(Math.Max(1, shellViewport.Width - 48 / zoom), bands.ContentHeight);
        var presenter = incoming.Presenter;
        var extent = OverlaySurfaceSizing.Resolve(incoming.Frame.Snapshot.Surface, available, new(0, 0), Appearance.TextScale,
            constraint =>
            {
                presenter.Width = presenter.Height = double.NaN;
                presenter.Measure(new(constraint.Width, constraint.Height));
                var result = new SurfaceExtent(presenter.DesiredSize.Width, presenter.DesiredSize.Height);
                presenter.InvalidateMeasure();
                return result;
            });
        ConfigurePresenterExtent(presenter, extent);
    }

    private void ConfigurePresenterExtent(WidgetViewPresenter presenter, SurfaceExtent extent)
    {
        presenter.Width = extent.Width;
        presenter.Height = extent.Height;
        presenter.VerticalAlignment = VerticalAlignment.Bottom;
        presenter.HorizontalAlignment = Appearance.OverlayPosition switch
        {
            OverlayPosition.BottomLeft => HorizontalAlignment.Left,
            OverlayPosition.BottomRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
    }
}
