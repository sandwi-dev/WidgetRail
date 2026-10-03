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
    private long settingsReadVersion;
    private long visibleSince = Environment.TickCount64;
    private readonly Queue<JsonObject> focusDiagnostics = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<BridgeWidgetDescriptor> catalogItems = [];
    internal event Action? HideRequested;
    internal event Action? ImmediateHideRequested;
    internal event Action? MotionPolicyChanged;
    internal bool SystemAnimationsEnabled => systemUi.AnimationsEnabled;
    internal bool HasForeground => foreground;
    private bool retainingExitPresentation;
    private bool PresentationVisible => visible || retainingExitPresentation;
    internal Action<string>? WindowActivationDiagnostic { get; set; }
    internal event Action<WidgetPresentationHostEffect>? TaskWindowActivationRequested;
    internal event Action<AppearanceSettings>? AppearanceLoaded;
    internal event Action<ControllerSettings>? ControllerSettingsLoaded;
    internal event Action? BridgeReady;
    internal AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;

    internal OverlayShellPage(OverlayShellOptions options, ulong hostWindow = 0, bool startService = true,
        bool initiallyVisible = true)
    {
        this.options = options;
        visible = initiallyVisible;
        foreground = initiallyVisible;
        if (options.SwitchDiagnosticsPath is { } diagnostics) switchDiagnostics = new(diagnostics);
        this.hostWindow = hostWindow;
        preferencesStore = new(options.SettingsRoot);
        interactionAdmission = new(transitions);
        InitializeComponent();
        InitializeSystemStatus(startService);
        InitializeTrayCommands();
        InitializePinnedPlacement();
        InitializeRadialChooser();
        InitializeOpeningIndicator();
        if (startService) InitializeStartupPresentation();
        UpdateTrayHelp();
        InitializeFullscreenView();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) systemUi.AnimationsEnabledChanged += SystemAnimationsChanged;
        Tray.ItemsSource = catalogItems;
        Tray.Loaded += (_, _) => AttachRailScroll();
        catalogItems.CollectionChanged += (_, _) =>
        {
            if (shellViewport.Width > 0) ConfigureProductionViewport(shellViewport);
        };
        if (startService)
        {
            Loaded += (_, _) => EnsureStarted();
            serviceEnabled = true;
        }
        Tray.GotFocus += TrayGotFocus;
        Tray.GettingFocus += TrayGettingFocus;
        var explicitWidgetFocusTransfer = false;
        WidgetHost.GettingFocus += (_, args) => explicitWidgetFocusTransfer =
            args.FocusState == FocusState.Pointer || args.Direction != FocusNavigationDirection.None;
        WidgetHost.GotFocus += (_, _) =>
        {
            // Native fallback while a preview is being reconciled or switcher
            // chrome closes is not an instruction to enter that widget.
            if (retired || !visible || switching || surface?.IsApplyingPresentation == true ||
                !interactive && !explicitWidgetFocusTransfer) return;
            RecordFocusTransfer("widget"); SetInteractive(true);
        };
        Tray.ContainerContentChanging += (_, args) =>
        {
            if (args.Item is BridgeWidgetDescriptor item)
            {
                AutomationProperties.SetAutomationId(args.ItemContainer, "Overlay.Widget." + item.Id);
                AutomationProperties.SetName(args.ItemContainer, item.Name);
                NativePopupTheme.SetToolTip(args.ItemContainer, item.Name);
            }
        };
    }

    private readonly bool serviceEnabled;

    private void EnsureStarted()
    {
        // WinUI can load the tree of a hidden HWND. Loading is not permission
        // to start workers; wait until that tree is actually requested onscreen.
        if (serviceEnabled && visible && IsLoaded && !retired) startup ??= StartAsync();
    }

    private async Task StartAsync()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        void Phase(string phase) => switchDiagnostics?.Write($"startup phase={phase} elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}");
        try
        {
            Phase("begin");
            preferences = await preferencesStore.LoadAsync(lifetime.Token);
            preferencesLoaded = true;
            await LoadPinnedPreferencesAsync();
            await LoadAppearanceAsync();
            Phase("preferences-ready");
            await InitializePreviewCapturesAsync();
            Phase("preview-capability-ready");
            owner = await OwnedBridgeProcess.StartAsync(new(options.InstallationRoot,
                options.SettingsRoot, options.InstalledCatalogRoot)
            {
                WindowPreviews = previewCaptures is not null,
                ExclusiveControllerControl = ControllerControlStatusRequested is not null,
                ForegroundDelegationDiagnostic = message => WindowActivationDiagnostic?.Invoke(message),
                ProcessOwnerJobName = options.Development?.JobName,
            }, lifetime.Token);
            Phase("bridge-connected");
            InitializeMediaOwner();
            owner.Session.PresentationChanged += Changed;
            owner.Session.CatalogChanged += CatalogChanged;
            owner.Session.AppearanceChanged += AppearanceChanged;
            owner.Session.HostEffectReceived += HostEffectReceived;
            applicationControlPump = PumpApplicationControlAsync();
            capturePump = PumpCaptureAsync();
            if (ControllerControlStatusRequested is not null) controllerControlPump = PumpControllerControlAsync();
            BridgeReady?.Invoke();
            Phase("bridge-notifications-queued");
            await InitializeStartupCatalogAsync(Phase);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        {
            ReportFailure(error);
            if (owner is null && !retired)
                ShowRecovery("The widget service could not start. Try again.", true);
        }
    }

    private async Task InitializeStartupCatalogAsync(Action<string> Phase)
    {
        var startupOwner = owner ?? throw new InvalidOperationException("The widget service is not connected.");
        WidgetPresentationCatalog catalog;
        await transitions.WaitAsync(lifetime.Token);
        Phase("catalog-lock-acquired");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var firstResponse = true;
            do
            {
                catalog = await startupOwner.Session.ListWidgetsAsync(deadline.Token);
                if (firstResponse) { Phase(catalog.IsComplete ? "first-catalog-complete" : "first-catalog-pending"); firstResponse = false; }
                if (StartupCatalogPolicy.CanOpenInitial(catalog.IsComplete, options.InitialWidgetId,
                    catalog.Widgets.Select(widget => widget.Id))) break;
                await Task.Delay(50, deadline.Token);
            } while (true);
        }
        finally { transitions.Release(); }
        Phase(catalog.IsComplete ? "initial-catalog-complete" : "initial-widget-admitted");
        SetCatalog(catalog);
        var explicitInitial = catalog.Widgets.FirstOrDefault(widget => widget.Id == options.InitialWidgetId);
        if (options.Development is { } development && (explicitInitial is null || explicitInitial.InstanceId != development.InstanceId))
            throw new InvalidDataException("The development catalog does not contain the requested widget instance.");
        // A fresh host launch mirrors native OpenWidgetWithTrayFocus(settings).
        // Session hide/reopen is handled separately and retains its current widget.
        var startupSettings = catalogItems.FirstOrDefault(widget => widget.Id == "settings");
        var initial = explicitInitial ?? startupSettings
            ?? catalogItems.FirstOrDefault(widget => widget.Id == preferences.LastWidget)
            ?? catalogItems.FirstOrDefault();
        if (initial is not null) await SelectAsync(initial.Id,
            enterWidget: explicitInitial is not null || startupSettings is null && preferences.ReopenWidget);
        else ShowRecovery("No installed widgets are available.", false);
        Phase("initial-widget-ready");
        await PublishDevelopmentReadyAsync();
        // Settings is already in the trusted catalog. Keep its first view
        // independent of installed-package validation, but restore saved pins
        // only after the complete admitted catalog is available.
        if (!catalog.IsComplete)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            do
            {
                await Task.Delay(50, deadline.Token);
                await transitions.WaitAsync(deadline.Token);
                try { catalog = await startupOwner.Session.ListWidgetsAsync(deadline.Token); }
                finally { transitions.Release(); }
            } while (!catalog.IsComplete);
            SetCatalog(catalog);
            Phase("catalog-complete");
        }
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
        var version = ++settingsReadVersion;
        try
        {
            var settings = await new PlatformSettingsStore(new(options.SettingsRoot)).LoadAsync(lifetime.Token);
            if (retired || version != settingsReadVersion) return;
            savedAppearance = settings.Appearance;
            ControllerSettingsLoaded?.Invoke(settings.Controllers);
            ApplyEffectiveAppearance();
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private void SystemAnimationsChanged(Windows.UI.ViewManagement.UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!retired) surface?.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
        if (!retired && pinned is { } current) ApplyPinnedAppearance(current);
        if (!retired) MotionPolicyChanged?.Invoke();
    });

    private void HostEffectReceived(object? sender, WidgetHostEffectEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        var effect = args.Effect;
        var rejection = retired ? "retired" : !visible ? "hidden" : effect.Authority.WidgetId != activeWidget ? "widget-changed" :
            effect.InitiatedAtMilliseconds < visibleSince ? "old-visible-session" :
            owner?.Session.IsHostEffectAuthorityCurrent(effect.Authority) != true ? "stale-authority" : null;
        if (rejection is not null)
        {
            if (effect.Kind == WidgetHostEffectKind.ActivateTaskWindow) TraceTaskActivation(effect, "effect-rejected reason=" + rejection);
            return;
        }
        if (effect.Kind == WidgetHostEffectKind.CloseOverlayAfterAppLaunch) ImmediateHideRequested?.Invoke();
        else if (effect.Kind == WidgetHostEffectKind.ActivateTaskWindow) TaskWindowActivationRequested?.Invoke(effect);
    });

    internal async Task ActivateTaskWindowAsync(WidgetPresentationHostEffect effect,
        WidgetRail.WindowsWindowActivation.TaskWindowActivation activation, Action hide, Func<Task<bool>>? prepareHandoff = null,
        CancellationToken handoffCancellation = default)
    {
        if (retired || !visible || effect.Kind != WidgetHostEffectKind.ActivateTaskWindow || effect.WindowTarget is not { } target)
        { TraceTaskActivation(effect, "host-rejected retired=" + retired + " visible=" + visible + " kind=" + effect.Kind); return; }
        bool Current() => !retired && effect.Authority.WidgetId == activeWidget &&
            owner?.Session.IsHostEffectAuthorityCurrent(effect.Authority) == true;
        try
        {
            var result = await activation.ExecuteAsync(new(target.Handle, target.ProcessId, target.ProcessCreated, target.ClassName),
                effect.InitiatedAtMilliseconds, visibleSince, Current, () =>
                {
                    owner!.RefreshForegroundPermissionForHandoff();
                    hide();
                }, async () =>
                {
                    var completed = await owner!.Session.CompleteTaskActivationAsync(effect, lifetime.Token);
                    TraceTaskActivation(effect, $"brokerPid={completed.ProcessId} code={completed.Code} {completed.NativeTrace}");
                    return completed.Result switch
                    {
                        "Requested" => WidgetRail.WindowsWindowActivation.TaskWindowActivationResult.Requested,
                        "Denied" => WidgetRail.WindowsWindowActivation.TaskWindowActivationResult.Denied,
                        _ => WidgetRail.WindowsWindowActivation.TaskWindowActivationResult.Rejected,
                    };
                }, message => TraceTaskActivation(effect, message), prepareHandoff);
            TraceTaskActivation(effect, "result=" + result);
            _ = ObserveTaskActivationAsync(effect);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested || handoffCancellation.IsCancellationRequested) { }
        catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("task-activation", error); }
    }

    private void TraceTaskActivation(WidgetPresentationHostEffect effect, string message) =>
        WindowActivationDiagnostic?.Invoke($"Task activation widget={effect.Authority.WidgetId} targetHwnd={effect.WindowTarget?.Handle} targetPid={effect.WindowTarget?.ProcessId} initiated={effect.InitiatedAtMilliseconds} visibleSince={visibleSince} {message}");

    private async Task ObserveTaskActivationAsync(WidgetPresentationHostEffect effect)
    {
        try
        {
            await Task.Delay(150, lifetime.Token);
            if (!retired) TraceTaskActivation(effect, "phase=foreground-observed-after-150ms");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    internal void SetVisible(bool value, bool retainExitPresentation = false)
    {
        if (retired || visible == value) return;
        retainingExitPresentation = !value && retainExitPresentation;
        ProductionRoot.IsHitTestVisible = value;
        if (!value) localInstallCancellation?.Cancel();
        if (!value) intentCancellation?.Cancel();
        if (!value) CancelPinnedAdjustment(restoreFocus: false);
        if (!value) ExitPinnedInteraction(restoreMain: false);
        interactionAdmission.Invalidate();
        visible = value;
        RefreshStartupPresentation();
        // Reopen can await worker/lifecycle admission. Keep the last committed
        // pixels visible during that wait; its inactive presenter still rejects
        // input until SelectAsync publishes current authority again.
        if (value && surface is { } retained)
        {
            retained.Opacity = 1;
            retained.Visibility = Visibility.Visible;
            if (interactive) retained.RestoreRetainedFocusPresentation();
        }
        if (value) EnsureStarted();
        if (!value)
        {
            switchCancellation?.Cancel();
            if (!retainingExitPresentation) openingIndicator?.Clear();
            CloseTrayMenu(restoreFocus: false);
            surface?.DismissTransientControl();
        }
        // Retained focus presentation may return immediately; command authority
        // remains revoked until the foreground lifecycle has been admitted.
        surface?.SetPresentationInputEnabled(false);
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

    internal void CompleteExitPresentation()
    {
        if (!retainingExitPresentation) return;
        retainingExitPresentation = false;
        if (retired || visible) return;
        openingIndicator?.Clear();
        ReconcilePreviewVisibility();
        ReconcileMediaHostState();
        _ = ReconcileLifecycleAsync(restore: false);
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
        if (!value) intentCancellation?.Cancel();
        if (!value) CancelPinnedAdjustment(restoreFocus: false);
        interactionAdmission.Invalidate();
        foreground = value;
        if (value) intentForegroundAcquired?.TrySetResult();
        // The main HWND also deactivates when focus transfers to the pinned
        // peer. That peer owns its activation/loss notifications; treating
        // main deactivation as pin loss would cancel the handoff itself.
        if (!value) { heldAction.Reset(); ResetTrayInteraction(); }
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
        var failureCurrent = CapturePresentationFailureGuard();
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || owner is null || activeWidget is null) return;
                failureCurrent = CapturePresentationFailureGuard();
                var target = owner.Session.GetTarget(activeWidget);
                if (!PresentationVisible) await SuspendWidgetSurfaceAsync();
                var desired = DesiredLifecycle;
                if (desired != WidgetLifecycleState.Background && retainedSurfaces.TryGetValue(activeWidget, out var retained))
                {
                    if (retained.PresentedLifecycle != desired)
                        await EstablishWidgetPresentationAsync(retained, desired, lifetime.Token);
                }
                else
                {
                    await owner.Session.SetLifecycleAsync(target, desired, lifetime.Token);
                    if (retainedSurfaces.TryGetValue(activeWidget, out var hidden)) hidden.PresentedLifecycle = null;
                }
                UpdateTrayHelp();
                UpdateDiagnostics();
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error))
        { if (failureCurrent() && surface?.CurrentBinding is { } binding) owner?.Session.RequestInputRefresh(binding.Frame); }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    internal void QueueEntryFocus()
    {
        if (retired || !visible || startupPresentationPending || LocalInstallActive || HostChoiceActive) return;
        if (switching)
        {
            if (interactive && activeWidget == requestedWidget) surface?.RestoreRetainedFocusPresentation();
            return;
        }
        if (PinnedInputActive) { pinned!.Enter(); return; }
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
        heldAction.Reset();
        ResetTrayInteraction();
        rightStick.Reset();
        radialInput.Reset();
        surface?.ResetPressedStyles();
        surface?.DismissTransientControl();
    }

    private async Task<bool> EnsureInteractionAsync(WidgetPresentationAuthority authority, CancellationToken cancellationToken)
    {
        var capturedSurface = surface;
        var capturedOwner = owner;
        var capturedSelection = selectionVersion;
        if (capturedSurface is null || capturedOwner is null || !Current()) return false;
        if (!retainedSurfaces.TryGetValue(authority.WidgetId, out var retained) || !ReferenceEquals(retained.Presenter, capturedSurface)) return false;
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
        var ownership = InteractionOwner(capturedSurface, authority);
        var admitted = await interactionAdmission.EnsureAsync(ownership, Current,
            token => EstablishWidgetPresentationAsync(retained, WidgetLifecycleState.Interactive, token), cancellation.Token);
        if (admitted) { capturedSurface.SetAutomaticFocusEnabled(MainFocusEnabled); UpdateTrayHelp(); }
        return admitted;

        bool Current() => !retired && visible && foreground && !switching && !RadialOpen && !PinnedInteractionRequested && !PinnedAdjustmentActive &&
            capturedSelection == selectionVersion && ReferenceEquals(surface, capturedSurface) &&
            ReferenceEquals(owner, capturedOwner) && activeWidget == authority.WidgetId &&
            capturedSurface.IsInteractionCurrent(authority);
    }

    private static object InteractionOwner(WidgetViewPresenter presenter, WidgetPresentationAuthority authority) =>
        (presenter, authority.RuntimeGeneration, authority.PresentationGeneration,
            authority.SessionGeneration, authority.WidgetInstanceId);

    private async Task InvokeAsync(WidgetActionRequest request)
    {
        if (retired || !visible || switching || IsMediaFullscreen || owner is null || request.Authority.WidgetId != activeWidget) return;
        var failureCurrent = CapturePresentationFailureGuard();
        var requestOwner = owner;
        try
        {
            if (await EnsureInteractionAsync(request.Authority, lifetime.Token))
            {
                if (request.IndexedLease is not null) await InvokeIntentAsync(request);
                else if (request.Action.ActionId == WidgetRail.WidgetPresentationSession.WidgetPresentationSession.InstallLocalWidgetAction)
                    await InstallLocalWidgetAsync(request);
                else if (request.Action.ActionId == WidgetRail.WidgetPresentationSession.WidgetPresentationSession.EnterMediaFullscreenAction)
                {
                    if (pinned is { Media: not null } mediaPin && mediaPin.WidgetId == request.Authority.WidgetId)
                        await UnpinAsync(save: true, mediaPin);
                    mediaOwner?.EnterFullscreen(request.Displayed, request.Action);
                }
                else if (request.Action.ActionId == WidgetRail.WidgetPresentationSession.WidgetPresentationSession.ExitMediaWidgetAction)
                {
                    owner.Session.ValidateEmbeddedMediaBack(request.Displayed, request.Action);
                    PrepareRadialBackEntry(); SetInteractive(false); FocusTray();
                }
                else if (owner.Session.HasIntentAction(request.Displayed, request.Action)) await InvokeIntentAsync(request);
                else await owner.Session.SendActionAsync(request.Displayed, request.Action, lifetime.Token);
            }
        }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error))
        { if (failureCurrent()) requestOwner.Session.RequestInputRefresh(request.Displayed); }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    private async void RetryClicked(object sender, RoutedEventArgs args) => await RetryPresentationAsync();

    private async Task RetryPresentationAsync()
    {
        if (!retrying && serviceEnabled && requestedWidget is null)
        {
            retrying = true;
            Retry.IsEnabled = false;
            BeginStartupPresentation();
            try
            {
                startup = owner is null ? StartAsync() : InitializeStartupCatalogAsync(_ => { });
                await startup;
            }
            catch (OperationCanceledException) when (retired) { }
            catch (Exception error) { ReportFailure(error); }
            finally { retrying = false; Retry.IsEnabled = true; }
            return;
        }
        if (!retrying && owner is not null && requestedWidget is { } id)
        {
            var failureCurrent = CapturePresentationFailureGuard();
            retrying = true;
            Retry.IsEnabled = false;
            try
            {
                await transitions.WaitAsync(lifetime.Token);
                try { interactionAdmission.Invalidate(); await owner.Session.RestartAsync(owner.Session.GetTarget(id), lifetime.Token); }
                finally { transitions.Release(); }
                if (failureCurrent()) await SelectAsync(id);
            }
            catch (OperationCanceledException) when (retired) { }
            catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error)) { }
            catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
            finally { retrying = false; Retry.IsEnabled = true; }
        }
    }

    // Unlike a session failure notification, a direct awaited operation has no
    // publication owner. Capture its presentation before awaiting; an old error
    // must not replace a newer widget, reopen, or worker incarnation with recovery.
    private Func<bool> CapturePresentationFailureGuard()
    {
        var capturedSelection = selectionVersion;
        var capturedVisibleSession = visibleSince;
        var capturedSurface = surface;
        var capturedOwner = owner;
        var capturedBinding = surface?.CurrentBinding;
        return () => !retired && visible && capturedSelection == selectionVersion && capturedVisibleSession == visibleSince &&
            ReferenceEquals(capturedOwner, owner) && ReferenceEquals(capturedSurface, surface) &&
            (capturedBinding is null || capturedBinding.SameSurface(surface?.CurrentBinding));
    }

    internal void ReportFailure(Exception error)
    {
        if (retired) return;
        RecordValidationFailure(error);
        Diagnostics.FrontendFailureLog.Current.Write("shell-handled", error);
        ShowRecovery("The widget could not be displayed. Try again.", owner is not null);
        System.Diagnostics.Trace.TraceError("WinUI overlay: {0}", error);
    }

    private void ReportOperationFailure(Exception error, Func<bool> ownerCurrent)
    {
        if (ownerCurrent()) ReportFailure(error);
        else Diagnostics.FrontendFailureLog.Current.Write("retired-widget-operation", error);
    }

    partial void RecordValidationFailure(Exception error);

    internal string CaptureFailureLayout() => new JsonObject
    {
        ["activeWidget"] = activeWidget, ["requestedWidget"] = requestedWidget,
        ["selection"] = selectionVersion, ["switching"] = switching,
        ["radialOpen"] = RadialOpen, ["visible"] = visible, ["interactive"] = interactive,
        // Incoming first: a failure during preparation must not be hidden by a
        // large outgoing tree. Capture properties only, never UpdateLayout.
        ["preparing"] = Capture(preparingSurface?.Presenter), ["active"] = Capture(surface),
    }.ToJsonString();

    private static JsonNode? Capture(WidgetViewPresenter? presenter)
    {
        try { return presenter?.CaptureLayoutDiagnostics(32); }
        catch (Exception error) { return new JsonObject { ["captureFailure"] = error.GetType().Name, ["hresult"] = error.HResult }; }
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
            ["pinnedWidget"] = pinned?.WidgetId, ["pinnedLayout"] = pinned?.LayoutId, ["pinnedInput"] = PinnedInputActive,
            ["pinnedSelectionCurrent"] = pinned?.IsCurrent,
            ["pinnedAdjusting"] = pinnedAdjustment is not null, ["pinnedSaving"] = savingPinnedAdjustment,
            ["pinnedBounds"] = pinned is { } pin ? new JsonObject { ["x"] = pin.Window.Bounds.X, ["y"] = pin.Window.Bounds.Y,
                ["width"] = pin.Window.Bounds.Width, ["height"] = pin.Window.Bounds.Height } : null,
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
        localInstallCancellation?.Cancel();
        openingIndicator?.Dispose();
        intentCancellation?.Cancel();
        ++startupPresentationVersion;
        startupIndicator?.Dispose();
        StopStartupReveal();
        await DisposeSystemStatusAsync();
        ResetTrayInteraction();
        ClearTrayFocus();
        ReconcilePreviewVisibility();
        ReconcileMediaHostState();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) systemUi.AnimationsEnabledChanged -= SystemAnimationsChanged;
        switchCancellation?.Cancel();
        lifetime.Cancel();
        if (startup is not null) await startup;
        if (applicationControlPump is not null) await applicationControlPump;
        if (controllerControlPump is not null) await controllerControlPump;
        if (capturePump is not null) await capturePump;
        if (capturing is not null) await capturing;
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
                try { if (browserOwner is not null) await browserOwner.DisposeAsync(); }
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
        }
        finally { transitions.Release(); switchDiagnostics?.Dispose(); lifetime.Dispose(); }
    }
}
