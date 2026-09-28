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
    private long visibleSince = Environment.TickCount64;
    private readonly System.Collections.ObjectModel.ObservableCollection<BridgeWidgetDescriptor> catalogItems = [];
    internal event Action? HideRequested;
    internal event Action<AppearanceSettings>? AppearanceLoaded;
    internal AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;

    internal OverlayShellPage(OverlayShellOptions options)
    {
        this.options = options;
        InitializeComponent();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) systemUi.AnimationsEnabledChanged += SystemAnimationsChanged;
        Tray.ItemsSource = catalogItems;
        Loaded += (_, _) => startup ??= StartAsync();
        Tray.GotFocus += (_, _) => SetInteractive(false);
        WidgetHost.GotFocus += (_, _) => SetInteractive(true);
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
            owner.Session.PresentationChanged += Changed;
            owner.Session.CatalogChanged += CatalogChanged;
            owner.Session.AppearanceChanged += AppearanceChanged;
            owner.Session.HostEffectReceived += HostEffectReceived;
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
        UpdateDiagnostics();
    }

    private async void TrayItemClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is BridgeWidgetDescriptor descriptor) await SelectAsync(descriptor.Id);
    }

    private async Task SelectAsync(string id)
    {
        if (retired || owner is null) return;
        requestedWidget = id;
        var version = ++selectionVersion;
        switching = true;
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
                    surface = new() { Session = owner.Session, DispatchActionAsync = InvokeAsync, Failed = ReportFailure };
                    surface.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
                    WidgetHost.Content = surface;
                }
                interactive = true;
                var next = await owner.Session.EstablishPresentationAsync(owner.Session.GetTarget(id),
                    DesiredLifecycle, lifetime.Token);
                if (retired || version != selectionVersion) return;
                if (visible) surface!.Apply(next);
                Retry.Visibility = Visibility.Collapsed;
                Status.Text = next.Descriptor.Name;
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
        try { surface.Apply(next); Status.Text = next.Descriptor.Name; Retry.Visibility = Visibility.Collapsed; }
        catch (Exception error) { ReportFailure(error); }
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
            Appearance = (await new PlatformSettingsStore(new(options.SettingsRoot)).LoadAsync(lifetime.Token)).Appearance;
            if (!retired)
            {
                surface?.ApplyAppearance(Appearance, systemUi.AnimationsEnabled);
                AppearanceLoaded?.Invoke(Appearance);
            }
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
        // Native task-window activation requires revalidating target process/window
        // identity. It is not executed by this catalog/lifecycle checkpoint.
    });

    internal void SetVisible(bool value)
    {
        if (retired || visible == value) return;
        visible = value;
        ResetInputPresentation();
        if (value) visibleSince = Environment.TickCount64;
        UpdateDiagnostics();
        _ = ReconcileLifecycleAsync(restore: value);
    }

    private void SetInteractive(bool value)
    {
        if (interactive == value || retired) return;
        interactive = value;
        UpdateDiagnostics();
        _ = ReconcileLifecycleAsync(restore: false);
    }

    internal void SetForeground(bool value)
    {
        if (foreground == value || retired) return;
        foreground = value;
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
                    if (visible && !retired) { surface?.Apply(next); QueueEntryFocus(); }
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

    private async Task InvokeAsync(WidgetActionRequest request)
    {
        if (retired || !visible || switching || owner is null || request.Authority.WidgetId != activeWidget) return;
        try { await owner.Session.SendActionAsync(request.Authority, request.Action, lifetime.Token); }
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
                try { await owner.Session.RestartAsync(owner.Session.GetTarget(id), lifetime.Token); }
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
            foreground, catalogCount = catalogItems.Count, bridgePid = owner?.ProcessId }));

    public ValueTask DisposeAsync() => new(disposal ??= StopAsync());
    private async Task StopAsync()
    {
        retired = true;
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
            finally { if (owner is not null) await owner.DisposeAsync(); }
        }
        finally { transitions.Release(); lifetime.Dispose(); }
    }
}
