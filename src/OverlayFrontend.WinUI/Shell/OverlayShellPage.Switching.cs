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
        // Closing focused switcher chrome can synchronously give XAML focus to
        // the preview's first control. Capture/revoke before that happens so a
        // native fallback cannot replace the widget's remembered user target.
        switching = true;
        surface?.SetPresentationInputEnabled(false);
        surface?.SetAutomaticFocusEnabled(false);
        ResetTrayInteraction();
        ClearTrayFocus();
        interactionAdmission.Invalidate();
        switchCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        switchCancellation = cancellation;
        var token = cancellation.Token;
        var started = Stopwatch.GetTimestamp();
        var inputPublished = false;
        requestedWidget = id;
        var version = ++selectionVersion;
        BeginOpening(version, id);
        preparationFailure = null;
        interactive = enterWidget;
        UpdateTrayHelp();
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
                    // Prepare the declaration for the destination input mode.
                    // Host input remains revoked until commit; preparing as
                    // Visible first exposes disabled preview rows to entry focus.
                    var preparedLifecycle = interactive && foreground && !PinnedInteractionRequested
                        ? WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive : LifecycleFor(id);
                    await EstablishWidgetPresentationAsync(incoming, preparedLifecycle, token);
                    SizePreparingSurface(incoming);
                    incoming.Presenter.Opacity = ReferenceEquals(surface, incoming.Presenter) ? 1 : 0;
                    incoming.Presenter.Visibility = Visibility.Visible;
                    await incoming.Presenter.SetPresentationActiveAsync(true);
                    // Admission must already be acknowledged before exposing an
                    // interactive page. Outgoing cleanup remains serialized and
                    // awaited, but is not part of the incoming input lifetime.
                    await interactionAdmission.EstablishSerializedAsync(
                        InteractionOwner(incoming.Presenter, incoming.Frame!.Authority),
                        () => version == selectionVersion && !retired && visible && foreground && interactive &&
                            !PinnedInteractionRequested && !RadialOpen,
                        async cancellationToken =>
                        {
                            if (preparedLifecycle == WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive) return;
                            // A lifecycle acknowledgement does not publish its UI.
                            // Restore focus only against the Interactive declaration;
                            // Visible-state rows may still be disabled in the preview.
                            await EstablishWidgetPresentationAsync(incoming, WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive, cancellationToken);
                        }, token);
                    // Lifecycle callbacks can publish a newer declaration. Its
                    // final native layout must still precede visible publication.
                    await WidgetPresentationReadiness.WaitAsync(incoming.Presenter, token);
                    token.ThrowIfCancellationRequested();
                    Mark("layout-ready");
                    // No await inside this publication: identity, extent, background,
                    // native content and authority change in one dispatcher operation.
                    var previousSize = previous?.Presenter.PresentedWidgetSize;
                    previous?.Presenter.SettleWidgetResize();
                    // Freeze loading chrome in its presented position before
                    // the incoming extent changes. Its fade does not gate input.
                    openingIndicator?.Complete(version);
                    CommitPreparedSurface(incoming);
                    if (previousId != id && previousSize is { X: > 0, Y: > 0 } extent)
                        incoming.Presenter.ResizeWidgetContent(extent, WidgetSurface);
                    Mark("committed");
                    ShowPresentationStatus(incoming.Descriptor.Name);
                    Tray.SelectedItem = Tray.Items.Cast<BridgeWidgetDescriptor>().FirstOrDefault(widget => widget.Id == id);
                    preferences = preferences with { LastWidget = id, ReopenWidget = true };
                    Task outgoingSuspension = Task.CompletedTask;
                    if (previous is not null && !ReferenceEquals(previous, incoming))
                    {
                        previous.Previews?.SetVisible(false);
                        previous.Presenter.Visibility = Visibility.Collapsed;
                        // Revoke the old tree while native focus fallback still
                        // observes a switch; only the asynchronous drain remains.
                        outgoingSuspension = previous.Presenter.SetPresentationActiveAsync(false);
                    }
                    preparingSurface = null;
                    switching = false;
                    inputPublished = true;
                    PublishInputOwnership();
                    Mark("input-ready");
                    if (previous is not null && !ReferenceEquals(previous, incoming))
                    {
                        await outgoingSuspension;
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
                openingIndicator?.Complete(version);
                switching = false;
                // Cleanup completion cannot re-enter or reset a page the user
                // has already started navigating (or deliberately left).
                if (!inputPublished) PublishInputOwnership();
                ReconcilePreviewVisibility();
                ReconcileMediaHostState();
                if (!retired && activeWidget == requestedWidget && owner?.Session.GetState(id) is { } latest) ApplyState(latest);
                UpdateDiagnostics();
                if (!retired) _ = ReconcileLifecycleAsync(restore: false);
            }
        }
        void PublishInputOwnership()
        {
            surface?.SetPresentationInputEnabled(visible && activeWidget == requestedWidget);
            surface?.SetAutomaticFocusEnabled(MainFocusEnabled && activeWidget == requestedWidget);
            if (MainFocusEnabled && activeWidget == requestedWidget) surface?.Enter(restoreNativeFocus: true);
            else if (visible && !enterWidget && activeWidget == requestedWidget) FocusTray();
            ReconcileMediaHostState();
            UpdateTrayHelp();
        }
        void Mark(string phase)
        {
            if (version == selectionVersion)
                Diagnostics.FrontendFailureLog.Current.SetContext(
                    $"phase={phase} selection={version} active={activeWidget} requested={id}");
            switchDiagnostics?.Write(new System.Text.Json.Nodes.JsonObject
            {
                ["phase"] = phase, ["version"] = version, ["requested"] = id, ["displayed"] = activeWidget,
                ["elapsedMilliseconds"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                ["monotonicTimestamp"] = Stopwatch.GetTimestamp(),
            }.ToJsonString());
        }
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
                var measured = presenter.MeasureSurfaceContent(new(constraint.Width, constraint.Height), incoming.Frame.Snapshot.Surface);
                return new(measured.Width, measured.Height);
            });
        ConfigurePresenterExtent(presenter, extent);
    }

    private void ConfigurePresenterExtent(WidgetViewPresenter presenter, SurfaceExtent extent)
    {
        presenter.SetSurfaceCornerRadius(OverlaySurfacePaint.CornerRadius(ShellPalette));
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
