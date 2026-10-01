using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private sealed class PinnedSurface(WidgetPinnedSelection? selection, WidgetViewPresenter? presenter,
        PinnedWidgetWindow window, PinnedPlacementLimits limits, PinnedMonitor monitor)
    {
        internal WidgetPinnedSelection? Selection { get; } = selection;
        internal WidgetViewPresenter? Presenter { get; } = presenter;
        internal WidgetMediaPresentation? Media;
        internal MediaFullscreenView? MediaView;
        internal ShellChromeStyles? MediaStyles;
        internal Func<bool>? MediaCurrent;
        internal string WidgetId => Selection?.WidgetId ?? Media!.Document.Authority.WidgetId;
        internal string LayoutId => Selection?.LayoutId ?? CompactMediaLayout;
        internal bool IsCurrent => Selection?.IsCurrent ?? MediaCurrent?.Invoke() == true;
        internal void SetInput(bool enabled)
        {
            Presenter?.SetPresentationInputEnabled(enabled);
            Presenter?.SetAutomaticFocusEnabled(enabled);
            MediaView?.SetInteraction(enabled);
        }
        internal void Enter()
        { if (Presenter is { } widget) widget.Enter(restoreNativeFocus: true); else MediaView?.Enter(); }
        internal PinnedWidgetWindow Window { get; } = window;
        internal PinnedPlacementLimits Limits { get; set; } = limits;
        internal PinnedMonitor Monitor { get; set; } = monitor;
        internal bool HasForeground;
        internal TaskCompletionSource? FocusAcquired;
        internal long Publication;
        internal WidgetSurfaceAppearance Appearance;
        internal bool PlacementRefreshQueued;
        internal PinnedPlacement? LogicalPlacement;
        internal string? DisplayId;
        internal long DisplayRevision;
        internal NativePopupTheme PopupTheme { get; } = new();
    }
    private PinnedSurface? pinned;
    private string? pendingPinWidget;
    private PinnedPreferences pinnedPreferences = PinnedPreferences.Empty;
    private PinnedPreferencesStore? pinnedStore;
    private long pinIntent;
    internal event Action? ReturnFromPinnedRequested;
    private bool PinnedInputActive => pinned is { Window.Interactive: true, HasForeground: true };
    private bool PinnedInteractionRequested => pinned?.Window.Interactive == true;
    private bool MainFocusEnabled => visible && interactive && foreground && !switching && activeWidget == requestedWidget &&
        !PinnedInteractionRequested && !PinnedAdjustmentActive && !LocalInstallActive && !HostChoiceActive;

    private WidgetLifecycleState LifecycleFor(string id)
    {
        var main = !retired && visible && (id == activeWidget || switching && preparingSurface?.Descriptor.Id == id);
        var pin = !retired && pinned is { Window.IsVisible: true } current && current.WidgetId == id;
        if (main && id == activeWidget && !switching && interactive && (foreground || SystemFilePickerOpen) && !PinnedInputActive || pin && PinnedInputActive)
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
            owner is null || catalogItems.FirstOrDefault(item => item.Id == widget) is not { PinningSupported: true } descriptor ||
            placement.LayoutId == WidgetPinnedProjection.FullWidgetLayoutId && !descriptor.FullWidgetPinningSupported) return;
        // A compact media preference never starts an unrequested document or playback.
        // It is restored only after the user explicitly opens that media widget.
        if (placement.LayoutId == CompactMediaLayout) { restoreCompactWidget = widget; TryRestoreCompact(); }
        else await PinAsync(widget, placement.LayoutId, restoring: true);
    }

    private void AddPinnedCommands(BridgeWidgetDescriptor descriptor, Action<string, string, Func<Task>> add)
    {
        if (pinned is { } current)
        {
            add("Interact with pinned widget", "Pin.Interact", EnterPinnedAsync);
            add("Move / resize pinned widget", "Pin.Adjust", BeginPinnedAdjustmentAsync);
            add($"Pinned opacity: {current.Window.OpacityPercent}%", "Pin.Opacity", () => BeginPinnedAdjustmentAsync(opacityOnly: true));
            add("Unpin widget", "Pin.Remove", () => UnpinAsync(save: true));
        }
        else if (owner?.Session.GetState(descriptor.Id)?.LastGood is { } frame)
        {
            if (descriptor.PinningSupported && mediaOwner?.CanPin(descriptor.Id) == true &&
                frame.Snapshot.EmbeddedMediaSession?.SupportedPresentations.Contains(MediaPresentationKind.CompactPinned) == true)
                add("Pin media", "Pin.Media", () => PinMediaAsync(descriptor.Id));
            if (descriptor.PinningSupported)
                foreach (var layout in frame.Snapshot.PinnedLayouts)
                    add("Pin " + layout.Name, "Pin." + layout.Id, () => PinAsync(descriptor.Id, layout.Id));
            if (descriptor.FullWidgetPinningSupported && frame.Snapshot.EmbeddedMediaSession is null)
                add("Pin full widget", "Pin.Full", () => PinAsync(descriptor.Id, WidgetPinnedProjection.FullWidgetLayoutId));
        }
    }

    private async Task PinAsync(string widgetId, string layoutId, bool restoring = false)
    {
        if (owner is null || retired) return;
        var intent = ++pinIntent;
        var failureCurrent = CapturePinIntentFailureGuard(intent);
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
                // Apply the same full-widget media restriction as the tray's
                // offered commands. A saved layout is a preference, not an
                // admission to a media placement the new host cannot own.
                if (restoring && layoutId == WidgetPinnedProjection.FullWidgetLayoutId && frame.Snapshot.EmbeddedMediaSession is not null) return;
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
                    var saved = pinnedPreferences.Placements.GetValueOrDefault(widgetId);
                    window = new(frame.Descriptor.Name);
                    var prepared = await PreparePinnedPlacementAsync(window, hints, saved);
                    if (retired || intent != pinIntent || !selection.IsCurrent)
                        throw new WidgetPresentationSessionException("pinned_input_stale", "Pinned placement owner retired.");
                    var (placement, limits, displayId) = prepared;
                    presenter = new() { Session = owner.Session };
                    presenter.SetAutomaticFocusEnabled(false);
                    presenter.SetPresentationInputEnabled(false);
                    await presenter.SetPresentationActiveAsync(false);
                    presenter.ApplyAppearance(AppearanceForDisplay(displayId), systemUi.AnimationsEnabled);
                    var surface = new PinnedSurface(selection, presenter, window, limits, placement.Monitor)
                    { Appearance = projection.Snapshot.Surface?.Appearance ?? WidgetSurfaceAppearance.Theme, DisplayId = displayId };
                    surface.PopupTheme.Attach(presenter);
                    window.ThemeChanged += () => ApplyPinnedAppearance(surface);
                    window.PlacementEnvironmentChanged += () => QueuePinnedPlacementEnvironment(surface);
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
                    surface.LogicalPlacement = PinnedPlacementPolicy.Capture(placement.Bounds, placement.Monitor, limits, layoutId, window.OpacityPercent);
                    presenter.ApplyPinned(selection, projection);
                    pinned = surface;
                    ApplyPinnedAppearance(surface);
                    window.Show();
                    await presenter.SetPresentationActiveAsync(true);
                    if (owner.Session.GetState(widgetId) is { } currentState) ApplyPinnedState(currentState);
                    // Display clamping/DPI rounding on startup is temporary.
                    // Preserve the user's durable bounds until an explicit edit.
                    if (!restoring) await SavePinnedAsync(surface);
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
                    if (!retired && pinned?.WidgetId != widgetId) await TryBackgroundAsync(widgetId);
                }
                finally { transitions.Release(); }
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "pinned_input_stale" or "catalog_stale" or "unknown_widget") { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
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
        bool Current() => !retired && visible && ReferenceEquals(pinned, surface) && surface.IsCurrent &&
            pinnedAdjustment is null && !savingPinnedAdjustment && surface.Window.Interactive && surface.HasForeground && authority.WidgetId == surface.WidgetId;
    }

    private async Task EnterPinnedAsync()
    {
        if (pinned is not { } current || retired || !visible || !foreground || !current.IsCurrent ||
            pinnedAdjustment is not null || savingPinnedAdjustment || current.FocusAcquired is not null) return;
        var requestOwner = owner;
        var intent = pinIntent;
        var visibleSession = visibleSince;
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failureCurrent = CapturePinnedFailureGuard(current);
        ClearTrayFocus();
        surface?.SetAutomaticFocusEnabled(false);
        current.FocusAcquired = acquired;
        current.Window.SetInteraction(true);
        try
        {
            current.Window.Activate();
            if (!current.HasForeground) await acquired.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
            if (!EntryCurrent()) return;
            if (!await SetPinnedLifecycleAsync(current) || !EntryCurrent() || !current.HasForeground)
            {
                if (EntryCurrent()) ExitPinnedInteraction(restoreMain: false);
                return;
            }
            current.SetInput(true);
            if (current.Media is not null) mediaOwner?.SetCompactInput(true);
            current.Enter();
        }
        catch (OperationCanceledException) when (retired || !EntryCurrent()) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error))
        { if (EntryCurrent()) ExitPinnedInteraction(restoreMain: false); }
        catch (Exception error)
        {
            var report = failureCurrent() && EntryCurrent();
            if (EntryCurrent()) ExitPinnedInteraction(restoreMain: false);
            ReportOperationFailure(error, () => report);
        }
        finally { if (ReferenceEquals(current.FocusAcquired, acquired)) current.FocusAcquired = null; }

        bool EntryCurrent() => !retired && visible && visibleSince == visibleSession &&
            intent == pinIntent && ReferenceEquals(owner, requestOwner) && ReferenceEquals(pinned, current) && current.IsCurrent &&
            ReferenceEquals(current.FocusAcquired, acquired) && current.Window.Interactive &&
            pinnedAdjustment is null && !savingPinnedAdjustment;
    }

    private void ExitPinnedInteraction(bool restoreMain)
    {
        if (pinned is not { Window.Interactive: true } current) return;
        current.FocusAcquired?.TrySetCanceled();
        current.FocusAcquired = null;
        // Retain the widget's logical target before revoking input. The window
        // suppresses focused presentation without moving native/XAML focus.
        current.SetInput(false);
        if (current.Media is not null) mediaOwner?.SetCompactInput(false);
        current.Presenter?.ResetPressedStyles(); current.Presenter?.DismissTransientControl();
        current.Window.SetInteraction(false);
        surface?.SetAutomaticFocusEnabled(MainFocusEnabled);
        _ = ReconcilePinnedLifecycleAsync();
        if (restoreMain && visible && !retired) ReturnFromPinnedRequested?.Invoke();
    }

    private async Task ReconcilePinnedLifecycleAsync()
    {
        if (pinned is not { } current) return;
        var failureCurrent = CapturePinnedFailureGuard(current);
        try
        {
            await SetPinnedLifecycleAsync(current);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error)) { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    // Entry awaits this throwing core. The fire-and-forget passive reconciliation
    // catches separately, so a failed lifecycle can never enable pinned input.
    private async Task<bool> SetPinnedLifecycleAsync(PinnedSurface current)
    {
        var requestOwner = owner;
        var intent = pinIntent;
        if (requestOwner is null || !Current()) return false;
        await transitions.WaitAsync(lifetime.Token);
        try
        {
            if (!Current()) return false;
            await requestOwner.Session.SetLifecycleAsync(requestOwner.Session.GetTarget(current.WidgetId), LifecycleFor(current.WidgetId), lifetime.Token);
            if (!Current()) return false;
            UpdateDiagnostics();
            return true;
        }
        finally { transitions.Release(); }
        bool Current() => !retired && intent == pinIntent && ReferenceEquals(owner, requestOwner) &&
            ReferenceEquals(pinned, current) && current.IsCurrent;
    }

    private Func<bool> CapturePinIntentFailureGuard(long intent)
    {
        var presentationCurrent = CapturePresentationFailureGuard();
        return () => intent == pinIntent && presentationCurrent();
    }

    private Func<bool> CapturePinnedFailureGuard(PinnedSurface current)
    {
        var intentCurrent = CapturePinIntentFailureGuard(pinIntent);
        return () => intentCurrent() && ReferenceEquals(pinned, current) && current.IsCurrent;
    }

    private void ApplyPinnedState(WidgetPresentationState state)
    {
        if (retired || owner is null || pinned is not { } current || current.WidgetId != state.WidgetId ||
            state.PublicationRevision <= current.Publication) return;
        current.Publication = state.PublicationRevision;
        if (state.Failure is not null || !current.IsCurrent)
        { current.Window.Hide(); _ = UnpinAsync(save: false, current); return; }
        if (state.LastGood is not { } frame) return;
        if (current.Media is not null) { RefreshCompactView(); return; }
        try
        {
            var projection = owner.Session.ResolvePinnedProjection(frame, current.LayoutId);
            current.Presenter!.ApplyPinned(current.Selection!, projection);
            var appearance = projection.Snapshot.Surface?.Appearance ?? WidgetSurfaceAppearance.Theme;
            if (current.Appearance != appearance) { current.Appearance = appearance; ApplyPinnedAppearance(current); }
        }
        catch (WidgetPresentationSessionException) { current.Window.Hide(); _ = UnpinAsync(save: false, current); }
        catch (Exception error) { current.Window.Hide(); ReportFailure(error); _ = UnpinAsync(save: false, current); }
    }

    private async Task UnpinAsync(bool save, PinnedSurface? expected = null)
    {
        if (expected is not null && !ReferenceEquals(pinned, expected)) return;
        expected ??= pinned;
        var intent = save ? ++pinIntent : pinIntent;
        var requestOwner = owner;
        var failureCurrent = CapturePinIntentFailureGuard(intent);
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (intent != pinIntent || !ReferenceEquals(owner, requestOwner) || !ReferenceEquals(pinned, expected)) return;
                await RemovePinnedCoreAsync();
                if (save && intent == pinIntent)
                {
                    pinnedPreferences = pinnedPreferences with { WidgetId = null };
                    if (pinnedStore is not null) await pinnedStore.SaveAsync(pinnedPreferences, lifetime.Token);
                }
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error)) { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    private async Task RemovePinnedCoreAsync()
    {
        if (pinned is not { } current) return;
        CancelPinnedAdjustment();
        current.FocusAcquired?.TrySetCanceled();
        current.FocusAcquired = null;
        pinned = null;
        if (current.Media is not null && mediaOwner is not null) await mediaOwner.ReleaseCompactAsync();
        current.Window.Hide();
        try { if (current.Presenter is { } presenter) await presenter.DisposeAsync(); }
        finally { current.MediaStyles?.Dispose(); current.Window.Dispose(); }
        if (owner is not null && owner.Session.GetState(current.WidgetId)?.LastGood is { } frame)
        {
            if (current.Selection is { } selection) await RevokePinSelectionAsync(selection, frame);
            if (!retired) await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(current.WidgetId), LifecycleFor(current.WidgetId), lifetime.Token);
        }
        UpdateDiagnostics();
    }

    private async Task SavePinnedAsync(PinnedSurface current, bool required = false)
    {
        var values = new Dictionary<string, PinnedPlacement>(pinnedPreferences.Placements);
        if (values.Count >= ShellPreferences.MaximumWidgets && !values.ContainsKey(current.WidgetId)) values.Remove(values.Keys.First());
        values[current.WidgetId] = PinnedPlacementPolicy.Capture(current.Window.Bounds, current.Monitor, current.Limits,
            current.LayoutId, current.Window.OpacityPercent);
        var next = new PinnedPreferences(1, current.WidgetId, values);
        if (required && pinnedStore is null) throw new InvalidOperationException("Pinned placement storage is unavailable.");
        if (pinnedStore is not null)
        {
            try { await pinnedStore.SaveAsync(next, lifetime.Token); }
            catch (Exception error) when (!required && error is (IOException or UnauthorizedAccessException))
            { System.Diagnostics.Trace.WriteLine("Pinned placement could not be saved: " + error.GetType().Name); return; }
        }
        pinnedPreferences = next;
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
        var appearance = AppearanceForDisplay(current.DisplayId);
        if (current.Window.InterfaceScale != appearance.InterfaceScale)
        {
            CancelPinnedAdjustment();
            if (owner?.Session.GetState(current.WidgetId)?.LastGood is { } frame)
            {
                var hints = current.Media is not null ? CompactMediaHints : owner.Session.ResolvePinnedProjection(frame, current.LayoutId).Snapshot.Surface;
                current.Limits = ResolvePinnedLimits(hints, appearance.InterfaceScale);
                ReconcilePinnedPlacementEnvironment(current);
            }
        }
        current.Window.InterfaceScale = appearance.InterfaceScale;
        current.Presenter?.ApplyAppearance(appearance, systemUi.AnimationsEnabled);
        current.MediaStyles?.Update(ShellPalette, appearance, systemUi.AnimationsEnabled);
        current.PopupTheme.Update(ShellPalette, appearance);
        var policy = OverlayAppearancePolicy.Resolve(appearance, current.WidgetId, current.Appearance, current.Window.SystemHighContrast);
        OverlaySurfacePaint.Apply(current.Window.SurfaceBackground, policy, ShellPalette);
        current.Window.ApplyFocusAppearance(ShellChromePalette.Resolve(ShellPalette, appearance));
    }

    private static PinnedPlacementLimits ResolvePinnedLimits(WidgetSurfaceHints? hints, double zoom)
    {
        var minimumWidth = Math.Max(240, (hints?.MinimumWidth ?? 240) * zoom);
        var minimumHeight = Math.Max(135, (hints?.MinimumHeight ?? 135) * zoom);
        return new(minimumWidth, minimumHeight, Math.Max(960, Math.Max(minimumWidth, (hints?.PreferredWidth ?? 0) * zoom)),
            Math.Max(540, Math.Max(minimumHeight, (hints?.PreferredHeight ?? 0) * zoom)));
    }

    private void ReconcilePinnedCatalog(WidgetPresentationCatalog catalog)
    {
        if (pinned is not { } current) return;
        var descriptor = catalog.Widgets.FirstOrDefault(item => item.Id == current.WidgetId);
        if (!current.IsCurrent || descriptor is null && catalog.IsComplete ||
            descriptor is not null && current.Presenter?.CurrentBinding is { } binding && !SameSurfaceOwner(descriptor, binding.Frame.Descriptor))
        { current.Window.Hide(); _ = UnpinAsync(save: false, current); }
    }
}
