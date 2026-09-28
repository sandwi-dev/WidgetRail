using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private sealed class PinnedSurface(WidgetPinnedSelection selection, WidgetViewPresenter presenter,
        PinnedWidgetWindow window, PinnedPlacementLimits limits, PinnedMonitor monitor)
    {
        internal WidgetPinnedSelection Selection { get; } = selection;
        internal WidgetViewPresenter Presenter { get; } = presenter;
        internal PinnedWidgetWindow Window { get; } = window;
        internal PinnedPlacementLimits Limits { get; } = limits;
        internal PinnedMonitor Monitor { get; set; } = monitor;
        internal bool HasForeground;
        internal TaskCompletionSource? FocusAcquired;
        internal long Publication;
        internal WidgetSurfaceAppearance Appearance;
    }
    private PinnedSurface? pinned;
    private string? pendingPinWidget;
    private PinnedPreferences pinnedPreferences = PinnedPreferences.Empty;
    private PinnedPreferencesStore? pinnedStore;
    private long pinIntent;
    internal event Action? ReturnFromPinnedRequested;
    private bool PinnedInputActive => pinned is { Window.Interactive: true, HasForeground: true };
    private bool PinnedInteractionRequested => pinned?.Window.Interactive == true;
    private bool MainFocusEnabled => visible && interactive && foreground && !switching && activeWidget == requestedWidget && !PinnedInteractionRequested;

    private WidgetLifecycleState LifecycleFor(string id)
    {
        var main = !retired && visible && (id == activeWidget || switching && preparingSurface?.Descriptor.Id == id);
        var pin = !retired && pinned is { Window.IsVisible: true } current && current.Selection.WidgetId == id;
        if (main && id == activeWidget && !switching && interactive && foreground && !PinnedInputActive || pin && PinnedInputActive)
            return WidgetLifecycleState.Interactive;
        return main || pin || id == pendingPinWidget ? WidgetLifecycleState.Visible : WidgetLifecycleState.Background;
    }

    private async Task LoadPinnedPreferencesAsync()
    {
        pinnedStore = new(options.SettingsRoot);
        pinnedPreferences = await pinnedStore.LoadAsync(lifetime.Token);
    }

    private async Task RestorePinnedAsync()
    {
        if (pinnedPreferences.WidgetId is not { } widget || !pinnedPreferences.Placements.TryGetValue(widget, out var placement) ||
            owner is null || !catalogItems.Any(item => item.Id == widget)) return;
        await PinAsync(widget, placement.LayoutId);
    }

    private void AddPinnedCommands(BridgeWidgetDescriptor descriptor, Action<string, string, Func<Task>> add)
    {
        if (pinned is { } current)
        {
            add("Interact with pinned widget", "Pin.Interact", EnterPinnedAsync);
            add("Unpin widget", "Pin.Remove", () => UnpinAsync(save: true));
        }
        else if (owner?.Session.GetState(descriptor.Id)?.LastGood is { } frame)
        {
            if (descriptor.PinningSupported)
                foreach (var layout in frame.Snapshot.PinnedLayouts)
                    add("Pin " + layout.Name, "Pin." + layout.Id, () => PinAsync(descriptor.Id, layout.Id));
            if (descriptor.FullWidgetPinningSupported && frame.Snapshot.EmbeddedMediaSession is null)
                add("Pin full widget", "Pin.Full", () => PinAsync(descriptor.Id, WidgetPinnedProjection.FullWidgetLayoutId));
        }
    }

    private async Task PinAsync(string widgetId, string layoutId)
    {
        if (owner is null || retired) return;
        var intent = ++pinIntent;
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || intent != pinIntent) return;
                await RemovePinnedCoreAsync();
                pendingPinWidget = widgetId;
                var frame = await owner.Session.EstablishPresentationAsync(owner.Session.GetTarget(widgetId), LifecycleFor(widgetId), lifetime.Token);
                if (retired || intent != pinIntent) return;
                var projection = owner.Session.ResolvePinnedProjection(frame, layoutId);
                var selection = await owner.Session.SelectPinnedLayoutAsync(projection, cancellationToken: lifetime.Token);
                // Selection can synchronously publish its demanded content. Use
                // the latest genuine frame rather than the pre-selection shell.
                if (owner.Session.GetState(widgetId)?.LastGood is { } selectedFrame)
                    projection = owner.Session.ResolvePinnedProjection(selectedFrame, layoutId);
                WidgetViewPresenter? presenter = null;
                PinnedWidgetWindow? window = null;
                try
                {
                    if (retired || intent != pinIntent)
                    {
                        if (owner.Session.GetState(widgetId)?.LastGood is { } last)
                            await RevokePinSelectionAsync(selection, last);
                        return;
                    }
                    var hints = projection.Snapshot.Surface;
                    var zoom = Appearance.InterfaceScale;
                    var minWidth = Math.Max(240, (hints?.MinimumWidth ?? 240) * zoom);
                    var minHeight = Math.Max(135, (hints?.MinimumHeight ?? 135) * zoom);
                    var limits = new PinnedPlacementLimits(minWidth, minHeight, Math.Max(960, Math.Max(minWidth, (hints?.PreferredWidth ?? 0) * zoom)),
                        Math.Max(540, Math.Max(minHeight, (hints?.PreferredHeight ?? 0) * zoom)));
                    var saved = pinnedPreferences.Placements.GetValueOrDefault(widgetId);
                    var placement = PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(), saved, limits,
                        (hints?.PreferredWidth ?? 480) * zoom, (hints?.PreferredHeight ?? 270) * zoom)
                        ?? throw new InvalidOperationException("No display has enough room for this pinned layout.");
                    presenter = new() { Session = owner.Session };
                    presenter.SetAutomaticFocusEnabled(false);
                    await presenter.SetPresentationActiveAsync(false);
                    presenter.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
                    window = new(frame.Descriptor.Name);
                    var surface = new PinnedSurface(selection, presenter, window, limits, placement.Monitor)
                    { Appearance = projection.Snapshot.Surface?.Appearance ?? WidgetSurfaceAppearance.Theme };
                    window.ThemeChanged += () => ApplyPinnedAppearance(surface);
                    presenter.EnsureInteractionAsync = (authority, token) => EnsurePinnedInteractionAsync(surface, authority, token);
                    presenter.Failed = error => { if (ReferenceEquals(pinned, surface)) ReportFailure(error); };
                    window.CloseRequested += () => _ = UnpinAsync(save: true, surface);
                    window.ForegroundChanged += focused =>
                    {
                        surface.HasForeground = focused;
                        if (focused) surface.FocusAcquired?.TrySetResult();
                        if (!focused && ReferenceEquals(pinned, surface)) ExitPinnedInteraction(restoreMain: false);
                    };
                    window.SetContent(presenter);
                    window.Place(placement.Bounds);
                    window.SetOpacity(saved?.OpacityPercent ?? 100);
                    presenter.ApplyPinned(selection, projection);
                    pinned = surface;
                    ApplyPinnedAppearance(surface);
                    window.Show();
                    await presenter.SetPresentationActiveAsync(true);
                    if (owner.Session.GetState(widgetId) is { } currentState) ApplyPinnedState(currentState);
                    await SavePinnedAsync(surface);
                    UpdateDiagnostics();
                }
                catch
                {
                    if (ReferenceEquals(pinned?.Presenter, presenter)) pinned = null;
                    try { if (presenter is not null) await presenter.DisposeAsync(); }
                    finally { window?.Dispose(); }
                    throw;
                }
            }
            catch
            {
                // A canceled/failed select can have reached the worker. Revoke
                // the session's recorded attempt before another pin can replace it.
                if (owner.Session.GetState(widgetId)?.LastGood is { } latest)
                {
                    try
                    {
                        if (owner.Session.GetPinnedSelection(latest) is { } attempt && !ReferenceEquals(pinned?.Selection, attempt))
                            await RevokePinSelectionAsync(attempt, latest);
                    }
                    catch (WidgetPresentationSessionException error) when (error.Code is "pinned_input_stale" or "presentation_stale" or "unknown_widget") { }
                }
                throw;
            }
            finally
            {
                pendingPinWidget = null;
                try
                {
                    if (!retired && pinned?.Selection.WidgetId != widgetId) await TryBackgroundAsync(widgetId);
                }
                finally { transitions.Release(); }
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "pinned_input_stale" or "catalog_stale" or "unknown_widget") { }
        catch (Exception error) { ReportFailure(error); }
    }

    private async Task<bool> EnsurePinnedInteractionAsync(PinnedSurface surface, WidgetPresentationAuthority authority, CancellationToken token)
    {
        if (!Current()) return false;
        await transitions.WaitAsync(token);
        try
        {
            if (!Current() || owner is null) return false;
            var target = owner.Session.GetTarget(authority.WidgetId);
            if (target.Descriptor.InstanceId != authority.WidgetInstanceId || target.Descriptor.RuntimeGeneration != authority.RuntimeGeneration ||
                target.Descriptor.PresentationGeneration != authority.PresentationGeneration) return false;
            await owner.Session.SetLifecycleAsync(target, WidgetLifecycleState.Interactive, token);
            return Current();
        }
        finally { transitions.Release(); }
        bool Current() => !retired && visible && ReferenceEquals(pinned, surface) && surface.Selection.IsCurrent &&
            surface.Window.Interactive && surface.HasForeground && authority.WidgetId == surface.Selection.WidgetId;
    }

    private async Task EnterPinnedAsync()
    {
        if (pinned is not { } current || retired || !visible || !foreground || !current.Selection.IsCurrent) return;
        ClearTrayFocus();
        surface?.SetAutomaticFocusEnabled(false);
        current.FocusAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        current.Window.SetInteraction(true);
        try
        {
            current.Window.NativeWindow.Activate();
            if (!current.HasForeground) await current.FocusAcquired.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
            if (retired || !visible || !ReferenceEquals(pinned, current) || !current.Selection.IsCurrent) return;
            await ReconcilePinnedLifecycleAsync();
            current.Presenter.SetAutomaticFocusEnabled(true);
            current.Presenter.Enter(restoreNativeFocus: true);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ExitPinnedInteraction(restoreMain: false); ReportFailure(error); }
        finally { current.FocusAcquired = null; }
    }

    private void ExitPinnedInteraction(bool restoreMain)
    {
        if (pinned is not { Window.Interactive: true } current) return;
        current.Window.SetInteraction(false);
        current.Presenter.SetAutomaticFocusEnabled(false);
        current.Presenter.ResetPressedStyles(); current.Presenter.DismissTransientControl();
        surface?.SetAutomaticFocusEnabled(MainFocusEnabled);
        _ = ReconcilePinnedLifecycleAsync();
        if (restoreMain && visible && !retired) ReturnFromPinnedRequested?.Invoke();
    }

    private async Task ReconcilePinnedLifecycleAsync()
    {
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (!retired && owner is not null && pinned is { } current)
                    await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(current.Selection.WidgetId), LifecycleFor(current.Selection.WidgetId), lifetime.Token);
                UpdateDiagnostics();
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private void ApplyPinnedState(WidgetPresentationState state)
    {
        if (retired || owner is null || pinned is not { } current || current.Selection.WidgetId != state.WidgetId ||
            state.PublicationRevision <= current.Publication) return;
        current.Publication = state.PublicationRevision;
        if (state.Failure is not null || !current.Selection.IsCurrent)
        { current.Window.Hide(); _ = UnpinAsync(save: false, current); return; }
        if (state.LastGood is not { } frame) return;
        try
        {
            var projection = owner.Session.ResolvePinnedProjection(frame, current.Selection.LayoutId);
            current.Presenter.ApplyPinned(current.Selection, projection);
            var appearance = projection.Snapshot.Surface?.Appearance ?? WidgetSurfaceAppearance.Theme;
            if (current.Appearance != appearance) { current.Appearance = appearance; ApplyPinnedAppearance(current); }
        }
        catch (WidgetPresentationSessionException) { current.Window.Hide(); _ = UnpinAsync(save: false, current); }
        catch (Exception error) { current.Window.Hide(); ReportFailure(error); _ = UnpinAsync(save: false, current); }
    }

    private async Task UnpinAsync(bool save, PinnedSurface? expected = null)
    {
        if (expected is not null && !ReferenceEquals(pinned, expected)) return;
        if (save) ++pinIntent;
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (expected is not null && !ReferenceEquals(pinned, expected)) return;
                await RemovePinnedCoreAsync();
                if (save)
                {
                    pinnedPreferences = pinnedPreferences with { WidgetId = null };
                    if (pinnedStore is not null) await pinnedStore.SaveAsync(pinnedPreferences, lifetime.Token);
                }
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private async Task RemovePinnedCoreAsync()
    {
        if (pinned is not { } current) return;
        pinned = null;
        current.Window.Hide();
        try { await current.Presenter.DisposeAsync(); }
        finally { current.Window.Dispose(); }
        if (owner is not null && owner.Session.GetState(current.Selection.WidgetId)?.LastGood is { } frame)
        {
            await RevokePinSelectionAsync(current.Selection, frame);
            if (!retired) await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(current.Selection.WidgetId), LifecycleFor(current.Selection.WidgetId), lifetime.Token);
        }
        UpdateDiagnostics();
    }

    private async Task SavePinnedAsync(PinnedSurface current)
    {
        var values = new Dictionary<string, PinnedPlacement>(pinnedPreferences.Placements);
        if (values.Count >= ShellPreferences.MaximumWidgets && !values.ContainsKey(current.Selection.WidgetId)) values.Remove(values.Keys.First());
        values[current.Selection.WidgetId] = PinnedPlacementPolicy.Capture(current.Window.Bounds, current.Monitor, current.Limits,
            current.Selection.LayoutId, current.Window.OpacityPercent);
        pinnedPreferences = new(1, current.Selection.WidgetId, values);
        if (pinnedStore is not null)
        {
            try { await pinnedStore.SaveAsync(pinnedPreferences, lifetime.Token); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine("Pinned placement could not be saved: " + error.GetType().Name); }
        }
    }

    private async Task RevokePinSelectionAsync(WidgetPinnedSelection selection, WidgetPresentationFrame frame)
    {
        if (owner is null) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await owner.Session.ClearPinnedSelectionAsync(selection, frame, cancellationToken: deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { System.Diagnostics.Trace.WriteLine("Pinned teardown acknowledgement timed out; local authority is retired."); }
        catch (WidgetPresentationSessionException error) when (error.Code is "pinned_input_stale" or "presentation_stale" or "unknown_widget") { }
    }

    private void ApplyPinnedAppearance(PinnedSurface current)
    {
        if (!ReferenceEquals(pinned, current)) return;
        current.Window.InterfaceScale = Appearance.InterfaceScale;
        current.Presenter.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
        var policy = OverlayAppearancePolicy.Resolve(Appearance, current.Selection.WidgetId, current.Appearance, current.Window.SystemHighContrast);
        OverlaySurfacePaint.Apply(current.Window.SurfaceBackground, policy, ShellPalette);
    }

    private void ReconcilePinnedCatalog(WidgetPresentationCatalog catalog)
    {
        if (pinned is not { } current) return;
        var descriptor = catalog.Widgets.FirstOrDefault(item => item.Id == current.Selection.WidgetId);
        if (!current.Selection.IsCurrent || descriptor is null && catalog.IsComplete ||
            descriptor is not null && current.Presenter.CurrentBinding is { } binding && !SameSurfaceOwner(descriptor, binding.Frame.Descriptor))
        { current.Window.Hide(); _ = UnpinAsync(save: false, current); }
    }
}
