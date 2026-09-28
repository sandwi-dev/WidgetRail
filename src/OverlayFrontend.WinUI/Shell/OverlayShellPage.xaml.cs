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
/// Production bridge consumer. Owns one bridge and one active native widget surface.
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
    private long visibleSince = Environment.TickCount64;
    private readonly Queue<object> focusDiagnostics = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<BridgeWidgetDescriptor> catalogItems = [];
    internal event Action? HideRequested;
    internal event Action<WidgetPresentationHostEffect>? TaskWindowActivationRequested;
    internal event Action<AppearanceSettings>? AppearanceLoaded;
    internal event Action? BridgeReady;
    internal AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;

    internal OverlayShellPage(OverlayShellOptions options)
    {
        this.options = options;
        interactionAdmission = new(transitions);
        InitializeComponent();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) systemUi.AnimationsEnabledChanged += SystemAnimationsChanged;
        Tray.ItemsSource = catalogItems;
        Loaded += (_, _) => startup ??= StartAsync();
        Tray.GotFocus += (_, _) =>
        {
            RecordFocusTransfer("tray");
            // Native ListView activation may focus its item after ItemClick has
            // already begun an asynchronous widget entry. That late event is part
            // of the same activation, not a request to return to tray ownership.
            if (!switching) SetInteractive(false);
        };
        WidgetHost.GotFocus += (_, _) => { RecordFocusTransfer("widget"); SetInteractive(true); };
        Tray.ContainerContentChanging += (_, args) =>
        {
            if (args.Item is BridgeWidgetDescriptor item)
            {
                AutomationProperties.SetAutomationId(args.ItemContainer, "Overlay.Widget." + item.Id);
                AutomationProperties.SetName(args.ItemContainer, item.Name);
            }
        };
    }

    private async Task StartAsync()
    {
        try
        {
            await LoadAppearanceAsync();
            owner = await OwnedBridgeProcess.StartAsync(new(options.InstallationRoot,
                options.SettingsRoot, options.InstalledCatalogRoot), lifetime.Token);
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
                ?? catalog.Widgets.FirstOrDefault();
            if (initial is not null) await SelectAsync(initial.Id);
            else Status.Text = "No installed widgets are available.";
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private void SetCatalog(WidgetPresentationCatalog catalog)
    {
        var selected = requestedWidget;
        for (var index = 0; index < catalog.Widgets.Count; ++index)
        {
            var next = catalog.Widgets[index];
            var existing = catalogItems.Select((item, position) => (item, position)).FirstOrDefault(pair => pair.item.Id == next.Id);
            if (existing.item is null) catalogItems.Insert(index, next);
            else
            {
                if (existing.position != index) catalogItems.Move(existing.position, index);
                // Keep native tray containers and focus for unchanged descriptors.
                if (catalogItems[index].Name != next.Name || catalogItems[index].RuntimeGeneration != next.RuntimeGeneration ||
                    catalogItems[index].PresentationGeneration != next.PresentationGeneration) catalogItems[index] = next;
            }
        }
        while (catalogItems.Count > catalog.Widgets.Count) catalogItems.RemoveAt(catalogItems.Count - 1);
        Tray.SelectedItem = catalogItems.FirstOrDefault(widget => widget.Id == selected);
        ReconcileMediaHostState();
        UpdateDiagnostics();
    }

    private async void TrayItemClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is BridgeWidgetDescriptor descriptor) await SelectAsync(descriptor.Id);
    }

    private async Task SelectAsync(string id)
    {
        if (retired || owner is null) return;
        interactionAdmission.Invalidate();
        requestedWidget = id;
        var version = ++selectionVersion;
        switching = true;
        ReconcileMediaHostState();
        surface?.ResetPressedStyles();
        surface?.DismissTransientControl();
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || version != selectionVersion) return;
                if (activeWidget != id)
                {
                    if (activeWidget is { } old)
                        await TryBackgroundAsync(old);
                    if (surface is not null) await surface.DisposeAsync();
                    WidgetHost.Content = null;
                    activeWidget = id;
                    publication = 0;
                    surface = new() { Session = owner.Session, MediaOwner = mediaOwner, DispatchActionAsync = InvokeAsync, EnsureInteractionAsync = EnsureInteractionAsync, Failed = ReportFailure };
                    surface.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
                    WidgetHost.Content = surface;
                }
                interactive = true;
                var next = await owner.Session.EstablishPresentationAsync(owner.Session.GetTarget(id),
                    DesiredLifecycle, lifetime.Token);
                if (retired || version != selectionVersion) return;
                if (visible) surface!.Apply(next);
                UpdateSurfaceHints(next.Snapshot.Surface);
                ShowPresentationStatus(next.Descriptor.Name);
                Tray.SelectedItem = Tray.Items.Cast<BridgeWidgetDescriptor>().FirstOrDefault(widget => widget.Id == id);
                if (visible) surface!.Enter(restoreNativeFocus: true);
                UpdateDiagnostics();
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { if (version == selectionVersion) ReportFailure(error); }
        finally
        {
            if (version == selectionVersion)
            {
                switching = false;
                ReconcileMediaHostState();
                // Establishment can overlap unsolicited refresh publications.
                // Replay only the latest admitted state, never queued old views.
                if (!retired && owner?.Session.GetState(id) is { } latest) ApplyState(latest);
            }
        }
    }

    private WidgetLifecycleState DesiredLifecycle => !visible ? WidgetLifecycleState.Background :
        interactive && foreground ? WidgetLifecycleState.Interactive : WidgetLifecycleState.Visible;

    private async Task TryBackgroundAsync(string id)
    {
        try { await owner!.Session.SetLifecycleAsync(owner.Session.GetTarget(id), WidgetLifecycleState.Background, lifetime.Token); }
        catch (WidgetPresentationSessionException error) when (error.Code is "unknown_widget" or "catalog_stale" or "presentation_stale") { }
    }

    private void Changed(object? sender, WidgetPresentationChangedEventArgs args) => DispatcherQueue.TryEnqueue(() => ApplyState(args.State));

    private void ApplyState(WidgetPresentationState state)
    {
        if (retired || switching || !visible || state.WidgetId != activeWidget || state.PublicationRevision <= publication) return;
        publication = state.PublicationRevision;
        if (state.Failure is { } failure) { Status.Text = $"{failure.Message} ({failure.Code})"; Retry.Visibility = Visibility.Visible; return; }
        if (state.LastGood is not { } next || surface is null) return;
        try { surface.Apply(next); UpdateSurfaceHints(next.Snapshot.Surface); ShowPresentationStatus(next.Descriptor.Name); }
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
                    if (activeWidget is { } id && !catalog.Widgets.Any(widget => widget.Id == id))
                    {
                        ++selectionVersion;
                        requestedWidget = activeWidget = null;
                        ReconcileMediaHostState();
                        if (surface is not null) await surface.DisposeAsync();
                        surface = null;
                        WidgetHost.Content = null;
                        Status.Text = "The selected widget is no longer installed.";
                        FocusTray();
                    }
                }
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
        finally { catalogQueued = false; }
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
        interactionAdmission.Invalidate();
        visible = value;
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
        ReconcileMediaHostState();
        UpdateDiagnostics();
        _ = ReconcileLifecycleAsync(restore: false);
    }

    internal void SetForeground(bool value)
    {
        if (foreground == value || retired) return;
        interactionAdmission.Invalidate();
        foreground = value;
        ReconcileMediaHostState();
        _ = ReconcileLifecycleAsync(restore: false);
    }

    private async Task ReconcileLifecycleAsync(bool restore)
    {
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (retired || owner is null || activeWidget is null) return;
                var target = owner.Session.GetTarget(activeWidget);
                if (restore && visible)
                {
                    var next = await owner.Session.EstablishPresentationAsync(target, DesiredLifecycle, lifetime.Token);
                    if (visible && !retired) { surface?.Apply(next); UpdateSurfaceHints(next.Snapshot.Surface); QueueEntryFocus(); }
                }
                else await owner.Session.SetLifecycleAsync(target, DesiredLifecycle, lifetime.Token);
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    internal void QueueEntryFocus()
    {
        if (retired || !visible) return;
        if (interactive && surface is not null) surface.Enter(restoreNativeFocus: true);
        else FocusTray();
    }

    private void FocusTray()
    {
        var index = Math.Max(0, Tray.SelectedIndex);
        if (Tray.Items.Count == 0) return;
        Tray.ScrollIntoView(Tray.Items[index]);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!retired && visible) (Tray.ContainerFromIndex(index) as Control)?.Focus(FocusState.Keyboard);
        });
    }

    internal void ResetInputPresentation()
    {
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
        return await interactionAdmission.EnsureAsync(ownership, Current,
            token => capturedOwner.Session.SetLifecycleAsync(target, WidgetLifecycleState.Interactive, token), cancellation.Token);

        bool Current() => !retired && visible && foreground && !switching &&
            capturedSelection == selectionVersion && ReferenceEquals(surface, capturedSurface) &&
            ReferenceEquals(owner, capturedOwner) && activeWidget == authority.WidgetId &&
            capturedSurface.IsInteractionCurrent(authority);
    }

    private async Task InvokeAsync(WidgetActionRequest request)
    {
        if (retired || !visible || switching || owner is null || request.Authority.WidgetId != activeWidget) return;
        try
        {
            if (await EnsureInteractionAsync(request.Authority, lifetime.Token))
                await owner.Session.SendActionAsync(request.Displayed, request.Action, lifetime.Token);
        }
        catch (WidgetPresentationSessionException error) when (error.Code is "ordinary_input_stale" or "snapshot_stale" or "input_scope_stale" or "presentation_stale") { }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportFailure(error); }
    }

    private async void RetryClicked(object sender, RoutedEventArgs args)
    {
        if (owner is not null && activeWidget is { } id)
        {
            try
            {
                await transitions.WaitAsync(lifetime.Token);
                try { interactionAdmission.Invalidate(); await owner.Session.RestartAsync(owner.Session.GetTarget(id), lifetime.Token); }
                finally { transitions.Release(); }
                await SelectAsync(id);
            }
            catch (OperationCanceledException) when (retired) { }
            catch (Exception error) { ReportFailure(error); }
        }
    }

    internal void ReportFailure(Exception error)
    {
        if (retired) return;
        Status.Text = error.Message;
        Retry.Visibility = owner is not null ? Visibility.Visible : Visibility.Collapsed;
        System.Diagnostics.Trace.TraceError("WinUI overlay: {0}", error);
    }

    private void UpdateDiagnostics() => AutomationProperties.SetHelpText(Status,
        System.Text.Json.JsonSerializer.Serialize(new { activeWidget, publication, visible, interactive,
            foreground, sizing = SizingDiagnostics, catalogCount = catalogItems.Count, bridgePid = owner?.ProcessId,
            focusTransfers = focusDiagnostics.ToArray() }));

    private void RecordFocusTransfer(string destination)
    {
        if (options.LayoutDiagnosticsPath is null) return;
        focusDiagnostics.Enqueue(new { destination, switching, applying = surface?.IsApplyingPresentation == true,
            publication, interactive });
        while (focusDiagnostics.Count > 8) focusDiagnostics.Dequeue();
        UpdateDiagnostics();
    }

    public ValueTask DisposeAsync() => new(disposal ??= StopAsync());
    private async Task StopAsync()
    {
        interactionAdmission.Invalidate();
        retired = true;
        ReconcileMediaHostState();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) systemUi.AnimationsEnabledChanged -= SystemAnimationsChanged;
        lifetime.Cancel();
        if (startup is not null) await startup;
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
            try { if (surface is not null) await surface.DisposeAsync(); }
            finally
            {
                try { if (mediaOwner is not null) await mediaOwner.DisposeAsync(); }
                finally { if (owner is not null) await owner.DisposeAsync(); }
            }
        }
        finally { transitions.Release(); lifetime.Dispose(); }
    }
}
