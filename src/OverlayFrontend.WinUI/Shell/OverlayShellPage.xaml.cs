using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>
/// Production bridge consumer. Owns one bridge and a bounded set of native widget surfaces.
/// Serializes catalog/lifecycle changes; view publications retain session authority.
/// Controller polling and window activation remain in the platform adapter.
/// </summary>
internal sealed partial class OverlayShellPage : Page, IAsyncDisposable
{
    private readonly OverlayShellOptions options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim transitions = new(1, 1);
    private readonly InteractionAdmission interactionAdmission;
    private readonly Windows.UI.ViewManagement.UISettings systemUi = new();
    private OwnedBridgeProcess? owner;
    private WidgetViewPresenter? surface;
    private Task? startup;
    private Task? disposal;
    private string? requestedWidget;
    private string? activeWidget;
    private long selectionVersion;
    private long publication;
    private bool retired;
    private bool visible = true;
    private bool interactive = true;
    private bool foreground = true;
    private bool switching;
    private bool catalogQueued;
    private bool catalogDirty;
    private bool layoutCaptureQueued;
    private bool retrying;
    private long visibleSince = Environment.TickCount64;
    private readonly Queue<JsonObject> focusDiagnostics = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<BridgeWidgetDescriptor> catalogItems = [];
    internal event Action? HideRequested;
    internal event Action<WidgetPresentationHostEffect>? TaskWindowActivationRequested;
    internal event Action<AppearanceSettings>? AppearanceLoaded;
    internal event Action? BridgeReady;
    internal AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;

    internal OverlayShellPage(OverlayShellOptions options, ulong hostWindow = 0, bool startService = true)
    {
        this.options = options;
        if (options.SwitchDiagnosticsPath is { } diagnostics) switchDiagnostics = new(diagnostics);
        this.hostWindow = hostWindow;
        preferencesStore = new(options.SettingsRoot);
        interactionAdmission = new(transitions);
        InitializeComponent();
        InitializeSystemStatus(startService);
        InitializeTrayCommands();
        UpdateTrayHelp();
        InitializeFullscreenView();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) systemUi.AnimationsEnabledChanged += SystemAnimationsChanged;
        Tray.ItemsSource = catalogItems;
        Tray.Loaded += (_, _) => AttachRailScroll();
        catalogItems.CollectionChanged += (_, _) =>
        {
            if (shellViewport.Width > 0) ConfigureProductionViewport(shellViewport);
        };
        if (startService) Loaded += (_, _) => startup ??= StartAsync();
        Tray.GotFocus += TrayGotFocus;
        Tray.GettingFocus += TrayGettingFocus;
        WidgetHost.GotFocus += (_, _) => { RecordFocusTransfer("widget"); SetInteractive(true); };
        Tray.ContainerContentChanging += (_, args) =>
        {
            if (args.Item is BridgeWidgetDescriptor item)
            {
                AutomationProperties.SetAutomationId(args.ItemContainer, "Overlay.Widget." + item.Id);
                AutomationProperties.SetName(args.ItemContainer, item.Name);
                ToolTipService.SetToolTip(args.ItemContainer, item.Name);
            }
        };
    }

    private async Task StartAsync()
    {
        try
        {
            preferences = await preferencesStore.LoadAsync(lifetime.Token);
            preferencesLoaded = true;
            await LoadPinnedPreferencesAsync();
            await LoadAppearanceAsync();
            await InitializePreviewCapturesAsync();
            owner = await OwnedBridgeProcess.StartAsync(new(options.InstallationRoot,
                options.SettingsRoot, options.InstalledCatalogRoot) { WindowPreviews = previewCaptures is not null }, lifetime.Token);
            InitializeMediaOwner();
            owner.Session.PresentationChanged += Changed;
            owner.Session.CatalogChanged += CatalogChanged;
            owner.Session.AppearanceChanged += AppearanceChanged;
            owner.Session.HostEffectReceived += HostEffectReceived;
            BridgeReady?.Invoke();
            WidgetPresentationCatalog catalog;
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                do
                {
                    catalog = await owner.Session.ListWidgetsAsync(deadline.Token);
                    if (catalog.IsComplete) break;
                    await Task.Delay(50, deadline.Token);
                } while (true);
            }
            finally { transitions.Release(); }
            SetCatalog(catalog);
            var initial = catalog.Widgets.FirstOrDefault(widget => widget.Id == options.InitialWidgetId)
                ?? catalogItems.FirstOrDefault(widget => widget.Id == preferences.LastWidget)
                ?? catalogItems.FirstOrDefault();
            if (initial is not null) await SelectAsync(initial.Id,
                enterWidget: options.InitialWidgetId is not null || preferences.ReopenWidget);
            else ShowRecovery("No installed widgets are available.", false);
            await RestorePinnedAsync();
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private void SetCatalog(WidgetPresentationCatalog catalog)
    {
        ReconcilePresentationMemory(catalog);
        var selected = requestedWidget;
        preferences = preferences.Reconcile(catalog.Widgets.Select(widget => widget.Id), catalog.IsComplete);
        var byId = catalog.Widgets.ToDictionary(widget => widget.Id, StringComparer.Ordinal);
        var ordered = preferences.AvailableOrder(byId.Keys).Select(id => byId[id]).ToArray();
        for (var index = 0; index < ordered.Length; ++index)
        {
            var next = ordered[index];
            var existing = catalogItems.Select((item, position) => (item, position)).FirstOrDefault(pair => pair.item.Id == next.Id);
            if (existing.item is null) catalogItems.Insert(index, next);
            else
            {
                if (existing.position != index) catalogItems.Move(existing.position, index);
                // Keep native tray containers and focus for unchanged descriptors.
                if (catalogItems[index].Name != next.Name || catalogItems[index].InstanceId != next.InstanceId ||
                    !catalogItems[index].QuickActions.SequenceEqual(next.QuickActions) || catalogItems[index].RuntimeGeneration != next.RuntimeGeneration ||
                    catalogItems[index].PresentationGeneration != next.PresentationGeneration || catalogItems[index].InstanceId != next.InstanceId ||
                    catalogItems[index].PackageContentDigest != next.PackageContentDigest || catalogItems[index].Icon != next.Icon ||
                    catalogItems[index].PackageIcon != next.PackageIcon || !catalogItems[index].IconAssets.SequenceEqual(next.IconAssets)) catalogItems[index] = next;
            }
        }
        while (catalogItems.Count > ordered.Length) catalogItems.RemoveAt(catalogItems.Count - 1);
        Tray.SelectedItem = catalogItems.FirstOrDefault(widget => widget.Id == selected);
        ReconcilePinnedCatalog(catalog);
        if (trayMenu is { } menu && !TrayOwnerCurrent(menu.Owner, menu.Selection)) CloseTrayMenu(false);
        ReconcileMediaHostState();
        UpdateDiagnostics();
    }

    private async void TrayItemClicked(object sender, ItemClickEventArgs args)
    {
        if (reordering) { FinishTrayReorder(); return; }
        if (args.ClickedItem is BridgeWidgetDescriptor descriptor) await SelectAsync(descriptor.Id);
    }

    private WidgetLifecycleState DesiredLifecycle => activeWidget is { } id ? LifecycleFor(id) : WidgetLifecycleState.Background;

    private async Task TryBackgroundAsync(string id)
    {
        try { await owner!.Session.SetLifecycleAsync(owner.Session.GetTarget(id), LifecycleFor(id), lifetime.Token); }
        catch (WidgetPresentationSessionException error) when (error.Code is "unknown_widget" or "catalog_stale" or "presentation_stale") { }
    }

    private void Changed(object? sender, WidgetPresentationChangedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    { ApplyPinnedState(args.State); ApplyState(args.State); });

    private void ApplyState(WidgetPresentationState state)
    {
        if (retired || !visible) return;
        if (switching)
        {
            if (preparingSurface is { } preparing && state.WidgetId == requestedWidget &&
                state.LastGood is { } incoming && SameSurfaceOwner(preparing.Descriptor, incoming.Descriptor))
            {
                try
                {
                    ApplyWidgetSurfaceFrame(preparing, incoming);
                    SizePreparingSurface(preparing);
                }
                catch (Exception error)
                {
                    preparationFailure = (selectionVersion, error);
                    switchCancellation?.Cancel();
                }
            }
            return;
        }
        if (state.WidgetId != activeWidget || state.PublicationRevision <= publication) return;
        publication = state.PublicationRevision;
        if (state.Failure is { } failure) { ShowRecovery(failure.Message, true); return; }
        if (state.LastGood is not { } next || surface is null) return;
        if (retainedSurfaces.TryGetValue(state.WidgetId, out var committed) && !SameSurfaceOwner(committed.Descriptor, next.Descriptor))
        {
            _ = SelectAsync(state.WidgetId, interactive);
            return;
        }
        try
        {
            ApplyWidgetSurfaceFrame(next); UpdateSurfaceHints(next.Snapshot.Surface); ShowPresentationStatus(next.Descriptor.Name);
            if (mediaOwner?.FullscreenState is { } fullscreen) fullscreenView.Show(fullscreen.Declaration);
        }
        catch (Exception error) { ReportFailure(error); }
        if (!layoutCaptureQueued && options.LayoutDiagnosticsPath is { } diagnosticPath)
        {
            layoutCaptureQueued = true;
            var captured = surface;
            _ = CaptureLayoutAsync();
            async Task CaptureLayoutAsync()
            {
                try
                {
                    await Task.Delay(500, lifetime.Token);
                    if (!retired && ReferenceEquals(captured, surface)) captured.WriteLayoutDiagnostics(diagnosticPath);
                }
                catch (OperationCanceledException) when (retired) { }
                catch (Exception error) { System.Diagnostics.Trace.WriteLine(error.GetType().Name); }
                finally { layoutCaptureQueued = false; }
            }
        }
        UpdateDiagnostics();
    }

    private void CatalogChanged(object? sender, WidgetRevisionChangedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        catalogDirty = true;
        if (!catalogQueued) _ = RefreshCatalogAsync();
    });

    private async Task RefreshCatalogAsync()
    {
        catalogQueued = true;
        (string Id, long Selection, bool Enter)? replacement = null;
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                while (catalogDirty && !retired && owner is not null)
                {
                    catalogDirty = false;
                    var catalog = await owner.Session.ListWidgetsAsync(lifetime.Token);
                    SetCatalog(catalog);
                    foreach (var retained in retainedSurfaces.Values.ToArray())
                    {
                        var current = catalog.Widgets.FirstOrDefault(widget => widget.Id == retained.Descriptor.Id);
                        if (retained.Descriptor.Id != activeWidget &&
                            (current is not null && !SameSurfaceOwner(retained.Descriptor, current) || current is null && catalog.IsComplete))
                            await RetireWidgetSurfaceAsync(retained.Descriptor.Id);
                    }
                    if (activeWidget is { } id && retainedSurfaces.TryGetValue(id, out var selected))
                    {
                        var current = catalog.Widgets.FirstOrDefault(widget => widget.Id == id);
                        if (current is not null && !SameSurfaceOwner(selected.Descriptor, current) || current is null && catalog.IsComplete)
                        {
                            interactionAdmission.Invalidate();
                            var selection = ++selectionVersion;
                            if (current is not null) replacement = (id, selection, interactive);
                            else
                            {
                                activeWidget = null;
                                ReconcileMediaHostState();
                                await RetireWidgetSurfaceAsync(id);
                                requestedWidget = null;
                                interactive = false;
                                ShowRecovery("The selected widget is no longer installed.", false);
                                FocusTray();
                            }
                        }
                    }
                }
            }
            finally { transitions.Release(); }
            if (replacement is { } pending && pending.Selection == selectionVersion && !retired)
                await SelectAsync(pending.Id, pending.Enter);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
        finally
        {
            catalogQueued = false;
            if (catalogDirty && !retired) _ = RefreshCatalogAsync();
        }
    }

    private void AppearanceChanged(object? sender, WidgetRevisionChangedEventArgs args) =>
        DispatcherQueue.TryEnqueue(() => _ = LoadAppearanceAsync());

    private async Task LoadAppearanceAsync()
    {
        try
        {
            savedAppearance = (await new PlatformSettingsStore(new(options.SettingsRoot)).LoadAsync(lifetime.Token)).Appearance;
            if (!retired) ApplyEffectiveAppearance();
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private void SystemAnimationsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!retired) surface?.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
        if (!retired && pinned is { } current) ApplyPinnedAppearance(current);
    });

    private void HostEffectReceived(object? sender, WidgetHostEffectEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        var effect = args.Effect;
        if (retired || !visible || effect.Authority.WidgetId != activeWidget || effect.InitiatedAtMilliseconds < visibleSince ||
            owner?.Session.IsHostEffectAuthorityCurrent(effect.Authority) != true) return;
        if (effect.Kind == WidgetHostEffectKind.CloseOverlayAfterAppLaunch) HideRequested?.Invoke();
        else if (effect.Kind == WidgetHostEffectKind.ActivateTaskWindow) TaskWindowActivationRequested?.Invoke(effect);
    });

    internal void ActivateTaskWindow(WidgetPresentationHostEffect effect,
        WidgetRail.OverlayPlatformClient.TaskWindowActivation activation, Action hide)
    {
        if (retired || !visible || effect.Kind != WidgetHostEffectKind.ActivateTaskWindow || effect.WindowTarget is not { } target) return;
        bool Current() => !retired && effect.Authority.WidgetId == activeWidget &&
            owner?.Session.IsHostEffectAuthorityCurrent(effect.Authority) == true;
        var result = activation.Execute(new(target.Handle, target.ProcessId, target.ProcessCreated, target.ClassName),
            effect.InitiatedAtMilliseconds, visibleSince, Current, hide);
        if (result == WidgetRail.OverlayPlatformClient.TaskWindowActivationResult.Denied)
            System.Diagnostics.Debug.WriteLine("Task window activation request was not accepted by Windows.");
    }

    internal void SetVisible(bool value)
    {
        if (retired || visible == value) return;
        if (!value) ExitPinnedInteraction(restoreMain: true);
        interactionAdmission.Invalidate();
        visible = value;
        if (!value) switchCancellation?.Cancel();
        surface?.SetPresentationInputEnabled(value && !switching && activeWidget == requestedWidget);
        ReconcileSystemStatus();
        surface?.SetAutomaticFocusEnabled(false);
        if (!value) _ = SavePreferencesAsync();
        ReconcilePreviewVisibility();
        ReconcileMediaHostState();
        ResetInputPresentation();
        if (value) visibleSince = Environment.TickCount64;
        UpdateDiagnostics();
        _ = ReconcileLifecycleAsync(restore: value);
    }

    private void SetInteractive(bool value)
    {
        if (interactive == value || retired) return;
        if (!value) interactionAdmission.Invalidate();
        interactive = value;
        if (value) ResetTrayInteraction();
        UpdateTrayHelp();
        surface?.SetAutomaticFocusEnabled(MainFocusEnabled);
        ReconcileMediaHostState();
        UpdateDiagnostics();
        _ = ReconcileLifecycleAsync(restore: false);
    }

    internal void SetForeground(bool value)
    {
        if (foreground == value || retired) return;
        interactionAdmission.Invalidate();
        foreground = value;
        // The main HWND also deactivates when focus transfers to the pinned
        // peer. That peer owns its activation/loss notifications; treating
        // main deactivation as pin loss would cancel the handoff itself.
        if (!value) ResetTrayInteraction();
        surface?.SetAutomaticFocusEnabled(MainFocusEnabled);
        ReconcileMediaHostState();
        _ = ReconcileLifecycleAsync(restore: false);
    }

    private async Task ReconcileLifecycleAsync(bool restore)
    {
        if (restore && visible && requestedWidget is { } requested)
        {
            await SelectAsync(requested, interactive);
            return;
        }
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || owner is null || activeWidget is null) return;
                var target = owner.Session.GetTarget(activeWidget);
                if (!visible) await SuspendWidgetSurfaceAsync();
                await owner.Session.SetLifecycleAsync(target, DesiredLifecycle, lifetime.Token);
                UpdateDiagnostics();
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    internal void QueueEntryFocus()
    {
        if (retired || !visible || switching) return;
        if (PinnedInputActive) { pinned!.Presenter.Enter(restoreNativeFocus: true); return; }
        if (IsMediaFullscreen) fullscreenView.Enter();
        else if (interactive && RecoveryVisible && Retry.Visibility == Visibility.Visible) Retry.Focus(FocusState.Keyboard);
        else if (interactive && surface is not null) surface.Enter(restoreNativeFocus: true);
        else FocusTray();
    }

    private void FocusTray()
    {
        var index = Math.Max(0, Tray.SelectedIndex);
        if (Tray.Items.Count == 0) return;
        RequestTrayFocus(Tray.Items[index]);
    }

    internal void ResetInputPresentation()
    {
        ResetTrayInteraction();
        rightStick.Reset();
        surface?.ResetPressedStyles();
        surface?.DismissTransientControl();
    }

    private async Task<bool> EnsureInteractionAsync(WidgetPresentationAuthority authority, CancellationToken cancellationToken)
    {
        var capturedSurface = surface;
        var capturedOwner = owner;
        var capturedSelection = selectionVersion;
        if (capturedSurface is null || capturedOwner is null || !Current()) return false;
        // UIA Invoke does not necessarily move native focus. Explicit widget input
        // is an interaction intent, but never implies foreground/visibility authority.
        var target = capturedOwner.Session.GetTarget(authority.WidgetId);
        if (target.Descriptor.RuntimeGeneration != authority.RuntimeGeneration ||
            target.Descriptor.PresentationGeneration != authority.PresentationGeneration ||
            target.Descriptor.InstanceId != authority.WidgetInstanceId) return false;
        interactive = true;
        ReconcileMediaHostState();
        UpdateDiagnostics();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var ownership = (capturedSurface, authority.RuntimeGeneration, authority.PresentationGeneration,
            authority.SessionGeneration, authority.WidgetInstanceId);
        var admitted = await interactionAdmission.EnsureAsync(ownership, Current,
            token => capturedOwner.Session.SetLifecycleAsync(target, WidgetLifecycleState.Interactive, token), cancellation.Token);
        if (admitted) capturedSurface.SetAutomaticFocusEnabled(MainFocusEnabled);
        return admitted;

        bool Current() => !retired && visible && foreground && !switching && !PinnedInteractionRequested &&
            capturedSelection == selectionVersion && ReferenceEquals(surface, capturedSurface) &&
            ReferenceEquals(owner, capturedOwner) && activeWidget == authority.WidgetId &&
            capturedSurface.IsInteractionCurrent(authority);
    }

    private async Task InvokeAsync(WidgetActionRequest request)
    {
        if (retired || !visible || switching || IsMediaFullscreen || owner is null || request.Authority.WidgetId != activeWidget) return;
        try
        {
            if (await EnsureInteractionAsync(request.Authority, lifetime.Token))
            {
                if (request.Action.ActionId == WidgetRail.WidgetPresentationSession.WidgetPresentationSession.EnterMediaFullscreenAction)
                    mediaOwner?.EnterFullscreen(request.Displayed, request.Action);
                else await owner.Session.SendActionAsync(request.Displayed, request.Action, lifetime.Token);
            }
        }
        catch (WidgetPresentationSessionException error) when (error.Code is "ordinary_input_stale" or "snapshot_stale" or "input_scope_stale" or "presentation_stale") { }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private async void RetryClicked(object sender, RoutedEventArgs args) => await RetryPresentationAsync();

    private async Task RetryPresentationAsync()
    {
        if (!retrying && owner is not null && requestedWidget is { } id)
        {
            retrying = true;
            Retry.IsEnabled = false;
            try
            {
                await transitions.WaitAsync(lifetime.Token);
                try { interactionAdmission.Invalidate(); await owner.Session.RestartAsync(owner.Session.GetTarget(id), lifetime.Token); }
                finally { transitions.Release(); }
                await SelectAsync(id);
            }
            catch (OperationCanceledException) when (retired) { }
            catch (Exception error) { ReportFailure(error); }
            finally { retrying = false; Retry.IsEnabled = true; }
        }
    }

    internal void ReportFailure(Exception error)
    {
        if (retired) return;
        ShowRecovery("The widget could not be displayed. Try again.", owner is not null);
        System.Diagnostics.Trace.TraceError("WinUI overlay: {0}", error);
    }

    private void UpdateDiagnostics()
    {
        var json = new JsonObject
        {
            ["activeWidget"] = activeWidget, ["requestedWidget"] = requestedWidget,
            ["preparingWidget"] = preparingSurface?.Descriptor.Id, ["selectionVersion"] = selectionVersion, ["publication"] = publication, ["visible"] = visible, ["interactive"] = interactive,
            ["foreground"] = foreground, ["switching"] = switching, ["sizing"] = SizingDiagnostics?.DeepClone(),
            ["catalogCount"] = catalogItems.Count, ["bridgePid"] = owner?.ProcessId, ["retainedSurfaceCount"] = retainedSurfaces.Count,
            ["presentationMemoryCount"] = presentationMemory.Count, ["memoryRestoreCount"] = memoryRestoreCount,
            ["pinnedWidget"] = pinned?.Selection.WidgetId, ["pinnedLayout"] = pinned?.Selection.LayoutId, ["pinnedInput"] = PinnedInputActive,
            ["pinnedSelectionCurrent"] = pinned?.Selection.IsCurrent,
            ["focusTransfers"] = new JsonArray(focusDiagnostics.Select(value => value.DeepClone()).ToArray()),
        }.ToJsonString();
        AutomationProperties.SetHelpText(Status, json);
        AutomationProperties.SetHelpText(ProductionRoot, json);
        if (pinned is { } current && options.LayoutDiagnosticsPath is not null)
        {
            AutomationProperties.SetHelpText(current.Window.AutomationRoot, json);
        }
    }

    private void RecordFocusTransfer(string destination)
    {
        if (options.LayoutDiagnosticsPath is null) return;
        focusDiagnostics.Enqueue(new JsonObject { ["destination"] = destination, ["switching"] = switching,
            ["applying"] = surface?.IsApplyingPresentation == true, ["publication"] = publication, ["interactive"] = interactive });
        while (focusDiagnostics.Count > 8) focusDiagnostics.Dequeue();
        UpdateDiagnostics();
    }

    public ValueTask DisposeAsync() => new(disposal ??= StopAsync());
    private async Task StopAsync()
    {
        interactionAdmission.Invalidate();
        retired = true;
        await DisposeSystemStatusAsync();
        ResetTrayInteraction();
        ClearTrayFocus();
        ReconcilePreviewVisibility();
        ReconcileMediaHostState();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) systemUi.AnimationsEnabledChanged -= SystemAnimationsChanged;
        switchCancellation?.Cancel();
        lifetime.Cancel();
        if (startup is not null) await startup;
        await SavePreferencesAsync();
        await transitions.WaitAsync();
        try
        {
            if (owner is not null)
            {
                owner.Session.PresentationChanged -= Changed;
                owner.Session.CatalogChanged -= CatalogChanged;
                owner.Session.AppearanceChanged -= AppearanceChanged;
                owner.Session.HostEffectReceived -= HostEffectReceived;
            }
            try
            {
                try { await RemovePinnedCoreAsync(); }
                finally { await DisposeWidgetSurfacesAsync(); }
            }
            finally
            {
                try { if (mediaOwner is not null) await mediaOwner.DisposeAsync(); }
                finally
                {
                    try { if (previewCaptures is not null) await previewCaptures.DisposeAsync(); }
                    finally { if (owner is not null) await owner.DisposeAsync(); }
                }
            }
        }
        finally { transitions.Release(); switchDiagnostics?.Dispose(); lifetime.Dispose(); }
    }
}
