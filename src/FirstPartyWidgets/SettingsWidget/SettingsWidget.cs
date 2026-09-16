using System.Globalization;
using System.Text.Json;
using WidgetRail.PlatformSettings;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.WidgetCatalog;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

namespace WidgetRail.FirstPartyWidgets.Settings;

public enum SettingsPage
{
    Root,
    Appearance,
    ThemePicker,
    ThemeVersion,
    ThemeRemoval,
    Accessibility,
    AccessibilityVisual,
    Overlay,
    Controllers,
    InstalledWidgets,
    InstalledWidgetDetails,
    InstalledWidgetVersions,
    InstalledWidgetVersionRemoval,
    InstalledWidgetUpdate,
    InstalledWidgetRecovery,
    InstalledWidgetLocalData,
    InstalledWidgetUninstall,
    Permissions,
    PermissionDiagnostics,
    PackageCapabilities,
    CapabilityDecision,
    Diagnostics,
    AuthorityRecovery,
    Reset,
}

public sealed class SettingsWidget : Widget
{
    public const int InstalledWidgetsPerPage = 5;
    public const int InstalledVersionsPerPage = 5;
    private const int MaximumBundledDirectories = 64;
    private const int MaximumManifestBytes = 1024 * 1024;

    private readonly PlatformSettingsStore _store;
    private readonly ThemeCatalog _catalog;
    private readonly IStartupRegistration _startup;
    private StartupRegistrationStatus _startupStatus = new(false, false, "Checking Windows startup settings…");
    private readonly ThemeCatalogMutationPolicy _themeMutations;
    private readonly CatalogService _widgetCatalog;
    private readonly ConsentStore _consentStore;
    private readonly IPlatformDiagnosticsService _diagnosticsService;
    private readonly IPlatformDiagnosticsService _readinessDiagnosticsService;
    private readonly SettingsInstalledWidgetUninstallOperation _packageUninstall;
    private readonly string? _bundledWidgetRoot;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private OverlayDisplayContext _display = OverlayDisplayContext.Unavailable;
    private PlatformSettingsDocument _settings = PlatformSettingsDocument.Default;
    private ThemeCatalogSnapshot _themes;
    private PlatformDiagnosticsSnapshot _diagnostics = PlatformDiagnosticsSnapshot.Unavailable();
    private SettingsInstalledWidgetState _installedState = SettingsInstalledWidgetState.Empty;
    private SettingsPermissionState _permissionState = SettingsPermissionState.Empty;
    private SettingsAuthorityRecoverySelection? _selectedAuthorityRecovery;
    private SettingsThemeSelection? _selectedTheme;
    private string? _themePickerFocusId;
    private SettingsPage _page;
    private int _activationLoadCount;
    private Task _settingsStatusTask = Task.CompletedTask;
    private Task _initializationTask = Task.CompletedTask;
    private bool _initializing;
    internal Task InitializationTask => _initializationTask;
    private bool _settingsValid = true;
    private bool _busy;
    private bool _error;
    private readonly WidgetTimedMutation _toastExpiry;
    private string _statusText = "Settings load when this widget becomes visible";
    private bool _toastVisible;
    private long _toastGeneration;
    internal Task ToastExpiryTask => _toastExpiry.WhenIdleAsync();

    // All status writes occur under _stateLock. Replacements retire the previous
    // notification deadline, including repeated messages from separate actions.
    private string StatusMessage
    {
        get => _statusText;
        set
        {
            _statusText = value;
            var generation = ++_toastGeneration;
            _toastVisible = value is not "Ready" and not "Settings load when this widget becomes visible";
            _toastExpiry.Cancel();
            if (!_toastVisible) return;
            _toastExpiry.ScheduleLatest(UI.DefaultToastDuration, () =>
            {
                lock (_stateLock)
                {
                    if (_toastGeneration != generation) return;
                    _toastVisible = false;
                }
                Invalidate();
            });
        }
    }

    public SettingsWidget(
        PlatformSettingsStore? store = null,
        ThemeCatalog? catalog = null,
        CatalogService? widgetCatalog = null,
        ConsentStore? consentStore = null,
        IPlatformDiagnosticsService? diagnostics = null,
        string? bundledWidgetRoot = null,
        IPlatformDiagnosticsService? readinessDiagnostics = null,
        TimeProvider? timeProvider = null,
        IStartupRegistration? startup = null)
    {
        _toastExpiry = CreateTimedMutation(timeProvider: timeProvider);
        _startup = startup ?? StartupRegistration.CreateForSettingsWorker();
        var paths = store?.Paths ?? PlatformSettingsPaths.CreateDefault();
        _store = store ?? new PlatformSettingsStore(paths);
        _catalog = catalog ?? new ThemeCatalog(paths);
        _themeMutations = new ThemeCatalogMutationPolicy(_store, _catalog);
        _widgetCatalog = widgetCatalog ?? new CatalogService(
            Path.Combine(paths.RootDirectory, "widgets"));
        _consentStore = consentStore ?? new ConsentStore(
            Path.Combine(paths.RootDirectory, "consent"));
        _diagnosticsService = diagnostics ?? UnavailablePlatformDiagnosticsService.Instance;
        _readinessDiagnosticsService = readinessDiagnostics ?? _diagnosticsService;
        _packageUninstall = new SettingsInstalledWidgetUninstallOperation(_diagnosticsService);
        _bundledWidgetRoot = string.IsNullOrWhiteSpace(bundledWidgetRoot)
            ? null
            : Path.GetFullPath(bundledWidgetRoot);
        var builtIn = _catalog.BuiltInDefault;
        _themes = new ThemeCatalogSnapshot(
            [new ThemeCatalogEntry(builtIn.Descriptor, builtIn.IsValid, builtIn.Diagnostics)]);
    }

    public SettingsPage CurrentPage
    {
        get { lock (_stateLock) return _page; }
    }

    public int ActivationLoadCount => Volatile.Read(ref _activationLoadCount);

    public override WidgetView Render()
    {
        PlatformSettingsDocument settings;
        ThemeCatalogSnapshot themes;
        SettingsPage page;
        bool busy;
        bool loading;
        bool error;
        bool settingsValid;
        PlatformDiagnosticsSnapshot diagnostics;
        SettingsInstalledWidgetState installedState;
        SettingsPermissionState permissionState;
        SettingsThemeSelection? selectedTheme;
        string? themePickerFocusId;
        string? selectedAuthorityRecoveryId;
        string status;
        StartupRegistrationStatus startupStatus;
        OverlayDisplayContext display;
        lock (_stateLock)
        {
            startupStatus = _startupStatus;
            display = _display;
            settings = _settings;
            themes = _themes;
            loading = _initializing;
            page = loading ? SettingsPage.Root : _page;
            busy = _busy || loading;
            error = _error;
            settingsValid = _settingsValid;
            diagnostics = _diagnostics;
            installedState = _installedState;
            permissionState = _permissionState;
            selectedAuthorityRecoveryId = _selectedAuthorityRecovery?.RecoveryId;
            selectedTheme = _selectedTheme;
            themePickerFocusId = _themePickerFocusId;
            status = !loading && _toastVisible ? StatusMessage : "Ready";
        }

        var presentation = new SettingsPresentationState(
            page,
            settings,
            themes,
            diagnostics,
            selectedAuthorityRecoveryId,
            status,
            settingsValid,
            busy,
            error,
            selectedTheme,
            themePickerFocusId, startupStatus, display, loading);
        if (SettingsPresentation.TryRender(presentation, out var view)) return view;
        var header = SettingsPresentation.Header(presentation);
        return page switch
        {
            SettingsPage.InstalledWidgets =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgets(
                    header, busy, installedState, settings),
            SettingsPage.InstalledWidgetDetails =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgetDetails(
                    header, busy, installedState, permissionState, settings),
            SettingsPage.InstalledWidgetUpdate => SettingsUpdatePresentation.Render(header, busy, installedState),
            SettingsPage.InstalledWidgetVersionRemoval => SettingsVersionRemovalPresentation.Render(header, busy, installedState),
            SettingsPage.InstalledWidgetVersions =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgetVersions(
                    header, busy, installedState),
            SettingsPage.InstalledWidgetRecovery =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgetRecovery(
                    header, busy, installedState),
            SettingsPage.InstalledWidgetLocalData =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgetLocalData(
                    header, busy, installedState),
            SettingsPage.InstalledWidgetUninstall =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgetUninstall(
                    header, busy, installedState),
            SettingsPage.Permissions =>
                SettingsPermissionPresentation.RenderPermissionPackages(
                    header, busy, permissionState),
            SettingsPage.PermissionDiagnostics =>
                SettingsPermissionPresentation.RenderPermissionDiagnostics(
                    header, permissionState),
            SettingsPage.PackageCapabilities =>
                SettingsPermissionPresentation.RenderPackageCapabilities(
                    header, busy, permissionState),
            SettingsPage.CapabilityDecision =>
                SettingsPermissionPresentation.RenderCapabilityDecision(
                    header, busy, permissionState),
            _ => throw new InvalidOperationException($"Unsupported Settings page {page}."),
        };
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationLoadCount);
        lock (_stateLock) { _initializing = true; _busy = true; StatusMessage = "Loading settings…"; }
        _initializationTask = Task.Run(async () =>
        {
            try
            {
                await ReadDisplayAsync(activeLifetime).ConfigureAwait(false);
                await ReloadAsync(activeLifetime).ConfigureAwait(false);
                EnsureSettingsStatusPolling();
            }
            catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested) { }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformSettingsException)
            {
                SetOperation("Settings could not load. Select Refresh to try again.", false, true);
            }
            finally
            {
                lock (_stateLock) { _initializing = false; _busy = false; }
                Invalidate();
            }
        }, CancellationToken.None);
        Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        await _initializationTask.ConfigureAwait(false);
        await _settingsStatusTask.ConfigureAwait(false);
        lock (_stateLock) _toastVisible = false;
    }

    private void EnsureSettingsStatusPolling()
    {
        if (_settingsStatusTask.IsCompleted &&
            !ActiveCancellationToken.IsCancellationRequested)
            _settingsStatusTask = PollSettingsStatusAsync(ActiveCancellationToken);
    }

    private async Task PollSettingsStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1_000, cancellationToken).ConfigureAwait(false);
                if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) continue;
                try
                {
                    await ReadPackageNotificationAsync(cancellationToken).ConfigureAwait(false);
                    if (CurrentPage is SettingsPage.Overlay or SettingsPage.Accessibility)
                        await ReadDisplayAsync(cancellationToken).ConfigureAwait(false);
                    if (CurrentPage != SettingsPage.Controllers) continue;
                    ControllerControlStatus status;
                    try { status = (await _diagnosticsService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Controllers; }
                    catch (PlatformDiagnosticsException) { status = ControllerControlStatus.Unavailable; }
                    bool changed;
                    lock (_stateLock)
                    {
                        changed = _diagnostics.Controllers != status;
                        _diagnostics = _diagnostics with { Controllers = status };
                    }
                    if (changed) Invalidate();
                }
                finally { _operationGate.Release(); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ReadDisplayAsync(CancellationToken cancellationToken)
    {
        OverlayDisplayContext display;
        try { display = await _readinessDiagnosticsService.GetOverlayDisplayAsync(cancellationToken).ConfigureAwait(false); }
        catch (PlatformDiagnosticsException) { display = OverlayDisplayContext.Unavailable; }
        bool changed;
        lock (_stateLock) { changed = _display != display; _display = display; }
        if (changed) Invalidate();
    }

    internal async Task ReadPackageNotificationAsync(CancellationToken cancellationToken)
    {
        try
        {
            // A small non-blocking companion read; never enumerate catalogs or
            // call external providers just to retrieve transient feedback.
            var notification = await _readinessDiagnosticsService.TakeWidgetPackageNotificationAsync(cancellationToken).ConfigureAwait(false);
            if (notification.Message.Length != 0)
                SetOperation(notification.Message, false, notification.Failed);
        }
        catch (PlatformDiagnosticsException) { }
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_stateLock) if (_initializing) return;
        // ReloadAsync owns the same operation gate used by normal actions. Keep
        // refresh outside that critical section so controller refresh cannot
        // deadlock while still serializing against every other mutation.
        if (action.ActionId == "refresh")
        {
            await ReloadAsync(cancellationToken, "Settings refreshed").ConfigureAwait(false);
            if (CurrentPage == SettingsPage.InstalledWidgetDetails)
            {
                await InspectSelectedWidgetLocalDataAsync(cancellationToken).ConfigureAwait(false);
                await InspectSelectedWidgetUninstallAsync(cancellationToken).ConfigureAwait(false);
            }
            return;
        }
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (action.ActionId == "back" && CurrentPage == SettingsPage.InstalledWidgetVersionRemoval)
            {
                CancelVersionRemoval();
                return;
            }
            if (action.ActionId == "installed.uninstall.cancel" ||
                action.ActionId == "back" && CurrentPage == SettingsPage.InstalledWidgetUninstall)
            {
                CancelInstalledWidgetUninstall();
                return;
            }
            if (SettingsNavigationPolicy.TryResolve(
                    action.ActionId,
                    CurrentPage,
                    PackageCapabilitiesReturnPage(),
                    out var targetPage))
            {
                if (targetPage is SettingsPage.Overlay or SettingsPage.Accessibility)
                    await ReadDisplayAsync(cancellationToken).ConfigureAwait(false);
                Navigate(targetPage);
                return;
            }
            if (SettingsPreferencePolicy.TryCreate(action.ActionId, out var preference))
            {
                await PersistPreferenceAsync(preference, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (action.ActionId == "controllers.open-shortcut.toggle" && CurrentPage == SettingsPage.Controllers)
            {
                await ToggleControllerShortcutAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            if (action.ActionId is "controllers.exclusive-control.toggle" or "controllers.restore" &&
                CurrentPage == SettingsPage.Controllers)
            {
                await SetExclusiveControlAsync(cancellationToken, action.ActionId == "controllers.restore").ConfigureAwait(false);
                return;
            }
            switch (action.ActionId)
            {
                case "startup.toggle":
                    var startup = _startup.Read();
                    var changed = startup.CanChange ? _startup.SetEnabled(!startup.Registered) : startup;
                    lock (_stateLock) _startupStatus = changed;
                    SetOperation(changed.Message, false, changed.Error);
                    break;
                case "application.quit":
                case "application.restart":
                    await RequestApplicationControlAsync(action.ActionId == "application.restart", cancellationToken).ConfigureAwait(false);
                    break;
                case "installed.permissions.open": OpenSelectedInstalledPermissions(); break;
                case "open.permission-diagnostics": OpenPermissionDiagnostics(); break;
                case "authority.recovery.retry": await RetrySelectedAuthorityRecoveryAsync(
                    cancellationToken).ConfigureAwait(false); break;
                case "installed.previous-page": ChangeInstalledWidgetPage(-1); break;
                case "installed.next-page": ChangeInstalledWidgetPage(1); break;
                case "installed.versions.open": OpenInstalledWidgetVersions(); break;
                case "installed.versions.previous-page": ChangeInstalledVersionPage(-1); break;
                case "installed.versions.next-page": ChangeInstalledVersionPage(1); break;
                case "installed.repair.remove": await RemoveSelectedRepairCandidateAsync(
                    cancellationToken).ConfigureAwait(false); break;
                case "installed.local-data.open": OpenInstalledWidgetLocalData(); break;
                case "installed.local-data.clear": await ClearSelectedWidgetLocalDataAsync(
                    cancellationToken).ConfigureAwait(false); break;
                case "installed.uninstall.open": await OpenInstalledWidgetUninstallAsync(cancellationToken).ConfigureAwait(false); break;
                case "installed.update.open": OpenWidgetUpdate(); break;
                case "installed.update.review": await ReviewWidgetUpdateAsync(cancellationToken).ConfigureAwait(false); break;
                case "installed.version-removal.cancel": CancelVersionRemoval(); break;
                case "installed.version-removal.confirm": await RemoveSelectedVersionAsync(cancellationToken).ConfigureAwait(false); break;
                case "installed.uninstall.confirm": await UninstallSelectedWidgetAsync(
                    cancellationToken).ConfigureAwait(false); break;
                case "installed.toggle": await ToggleSelectedInstalledWidgetAsync(cancellationToken)
                    .ConfigureAwait(false); break;
                case "installed.builtin.toggle": await ToggleSelectedBuiltInWidgetAsync(cancellationToken).ConfigureAwait(false); break;
                case "installed.builtin.open": await OpenSelectedBuiltInCopyAsync(cancellationToken).ConfigureAwait(false); break;
                case "capability.grant": await ChangeConsentAsync(
                    ConsentDecision.Grant, cancellationToken).ConfigureAwait(false); break;
                case "capabilities.grant-all": await AllowAllPermissionsAsync(cancellationToken).ConfigureAwait(false); break;
                case "capability.deny": await ChangeConsentAsync(
                    ConsentDecision.Deny, cancellationToken).ConfigureAwait(false); break;
                case "reset.confirm": await ResetAsync(cancellationToken).ConfigureAwait(false); break;
                case "theme.select": await SelectSelectedThemeAsync(cancellationToken)
                    .ConfigureAwait(false); break;
                case "theme.remove.request": OpenThemeRemoval(); break;
                case "theme.remove.confirm": await RemoveSelectedThemeAsync(cancellationToken)
                    .ConfigureAwait(false); break;
                case "theme.remove.cancel": Navigate(SettingsPage.ThemeVersion); break;
                default:
                    if (TryThemeIndex(action.ActionId, out var index))
                        OpenThemeVersion(index);
                    else if (TryIndexedAction(action.ActionId, "installed.select.", out index))
                        await SelectInstalledWidgetAsync(index, cancellationToken)
                            .ConfigureAwait(false);
                    else if (TryIndexedAction(action.ActionId, "installed.builtin.select.", out index))
                        await SelectBuiltInWidgetAsync(index, cancellationToken)
                            .ConfigureAwait(false);
                    else if (TryIndexedAction(action.ActionId, "installed.version.select.", out index))
                        await SelectInstalledVersionAsync(index, cancellationToken).ConfigureAwait(false);
                    else if (TryIndexedAction(action.ActionId, "installed.version.remove.", out index))
                        OpenVersionRemoval(index);
                    else if (TryIndexedAction(action.ActionId, "installed.repair.select.", out index))
                        SelectRepairCandidate(index);
                    else if (TryIndexedAction(action.ActionId, "permission.select.", out index))
                        SelectPermissionPackage(index);
                    else if (TryIndexedAction(action.ActionId, "capability.select.", out index))
                        SelectCapability(index);
                    else if (TryIndexedAction(action.ActionId, "authority.recovery.select.", out index))
                        SelectAuthorityRecovery(index);
                    break;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ReloadAsync(
        CancellationToken cancellationToken,
        string successStatus = "Ready")
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetOperation("Loading settings…", busy: true, error: false);
            PlatformSettingsDocument settings;
            var settingsValid = true;
            string? warning = null;
            try
            {
                settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PlatformSettingsException exception)
            {
                settings = PlatformSettingsDocument.Default;
                settingsValid = false;
                warning = $"Settings were invalid ({exception.Code}); safe defaults are shown";
            }

            ThemeCatalogSnapshot themes;
            try
            {
                themes = _catalog.Discover();
            }
            catch (PlatformSettingsException exception)
            {
                var builtIn = _catalog.BuiltInDefault;
                themes = new ThemeCatalogSnapshot(
                    [new ThemeCatalogEntry(builtIn.Descriptor, builtIn.IsValid, builtIn.Diagnostics)]);
                warning ??= $"Theme catalog unavailable ({exception.Code}); default theme only";
            }

            var selectedInstalled = themes.Themes.Any(entry =>
                entry.IsValid &&
                string.Equals(entry.Descriptor.Id, settings.Appearance.ThemeId, StringComparison.Ordinal) &&
                string.Equals(entry.Descriptor.Version.ToString(), settings.Appearance.ThemeVersion, StringComparison.Ordinal));
            if (!selectedInstalled)
                warning ??= "Selected theme is unavailable; choose an installed theme";
            // Both sections describe the same catalog revision. Validate its
            // package files once per reload, rather than hashing every installed
            // version again for the permissions projection.
            var catalogRead = SettingsReadinessRetry.CatalogAsync(
                _widgetCatalog.DiscoverAsync, cancellationToken);
            var installedWarning = await ReloadInstalledWidgetsAsync(cancellationToken, catalogRead)
                .ConfigureAwait(false);
            warning ??= installedWarning;
            var permissionWarning = await ReloadPermissionsAsync(cancellationToken, catalogRead)
                .ConfigureAwait(false);
            warning ??= permissionWarning;
            PlatformDiagnosticsSnapshot diagnostics;
            try
            {
                diagnostics = await SettingsReadinessRetry.RegistryAsync(
                        _readinessDiagnosticsService.GetSnapshotAsync,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (PlatformDiagnosticsException exception)
            {
                diagnostics = PlatformDiagnosticsSnapshot.Unavailable(
                    $"Runtime diagnostics unavailable ({exception.Code})");
                warning ??= $"Runtime diagnostics unavailable ({exception.Code})";
            }
            var startupStatus = _startup.Read();
            lock (_stateLock)
            {
                _startupStatus = startupStatus;
                _settings = settings;
                _settingsValid = settingsValid;
                _themes = themes;
                _diagnostics = diagnostics;
                _busy = false;
                _error = warning is not null;
                StatusMessage = warning ?? successStatus;
            }
            Invalidate();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OpenThemeVersion(int index)
    {
        ThemeCatalogEntry? entry;
        lock (_stateLock)
            entry = index >= 0 && index < _themes.Themes.Count ? _themes.Themes[index] : null;
        if (entry is null) return;
        lock (_stateLock)
        {
            _selectedTheme = new SettingsThemeSelection(
                entry.CatalogId,
                entry.CatalogVersion);
            _themePickerFocusId = $"theme.item.{index}.action";
            _page = SettingsPage.ThemeVersion;
            StatusMessage = $"Reviewing {entry.Descriptor.Name} {entry.Descriptor.Version}";
        }
        Invalidate();
    }

    private void OpenThemeRemoval()
    {
        lock (_stateLock)
        {
            var entry = FindSelectedThemeLocked();
            if (entry is null || entry.Descriptor.IsBuiltIn || IsSelectedThemeLocked(entry)) return;
            _page = SettingsPage.ThemeRemoval;
            StatusMessage = $"Confirm removal of {entry.Descriptor.Name} {entry.Descriptor.Version}";
        }
        Invalidate();
    }

    private async Task SelectSelectedThemeAsync(CancellationToken cancellationToken)
    {
        ThemeCatalogEntry? entry;
        lock (_stateLock) entry = FindSelectedThemeLocked();
        if (entry is null || !entry.IsValid) return;
        SetOperation("Selecting theme…", busy: true, error: false);
        try
        {
            var saved = await _themeMutations.SelectAsync(
                entry.Descriptor.Id,
                entry.Descriptor.Version.ToString(),
                cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _settingsValid = true;
                _page = SettingsPage.ThemePicker;
                _busy = false;
                _error = false;
                StatusMessage = $"Selected {entry.Descriptor.Name} {entry.Descriptor.Version}";
            }
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Theme selection failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private async Task RemoveSelectedThemeAsync(CancellationToken cancellationToken)
    {
        ThemeCatalogEntry? entry;
        int priorIndex;
        lock (_stateLock)
        {
            entry = FindSelectedThemeLocked();
            priorIndex = entry is null ? 0 : IndexOfThemeLocked(entry);
        }
        if (entry is null || entry.Descriptor.IsBuiltIn) return;
        SetOperation("Removing theme version…", busy: true, error: false);
        try
        {
            var result = await _themeMutations.RetireAsync(
                entry.CatalogId,
                entry.CatalogVersion,
                cancellationToken).ConfigureAwait(false);
            var themes = _catalog.Discover();
            var focusIndex = themes.Themes.Count == 0 ? -1 : Math.Min(priorIndex, themes.Themes.Count - 1);
            lock (_stateLock)
            {
                _themes = themes;
                _selectedTheme = null;
                _themePickerFocusId = focusIndex < 0 ? null : $"theme.item.{focusIndex}.action";
                _page = SettingsPage.ThemePicker;
                _busy = false;
                _error = result.CleanupPending;
                StatusMessage = result.CleanupPending
                    ? "Theme removed; retired files are pending cleanup"
                    : $"Removed {result.Name} {result.Version}";
            }
        }
        catch (OperationCanceledException)
        {
            SetOperation("Theme removal cancelled", busy: false, error: false);
            return;
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Theme removal failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private ThemeCatalogEntry? FindSelectedThemeLocked() => _selectedTheme is null
        ? null
        : _themes.Themes.FirstOrDefault(entry =>
            string.Equals(entry.CatalogId, _selectedTheme.ThemeId, StringComparison.Ordinal) &&
            string.Equals(entry.CatalogVersion, _selectedTheme.Version, StringComparison.Ordinal));

    private bool IsSelectedThemeLocked(ThemeCatalogEntry entry) =>
        string.Equals(entry.Descriptor.Id, _settings.Appearance.ThemeId, StringComparison.Ordinal) &&
        string.Equals(entry.Descriptor.Version.ToString(), _settings.Appearance.ThemeVersion, StringComparison.Ordinal);

    private int IndexOfThemeLocked(ThemeCatalogEntry entry)
    {
        for (var index = 0; index < _themes.Themes.Count; index++)
            if (ReferenceEquals(_themes.Themes[index], entry) || _themes.Themes[index] == entry) return index;
        return 0;
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        SetOperation("Resetting settings…", busy: true, error: false);
        try
        {
            var saved = await _store.ReplaceAsync(
                PlatformSettingsDocument.Default,
                cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _settingsValid = true;
                _page = SettingsPage.Root;
                _busy = false;
                _error = false;
                StatusMessage = "Settings reset to defaults";
            }
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Reset failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private async Task SetExclusiveControlAsync(CancellationToken cancellationToken, bool restore = false)
    {
        SetOperation("Checking controller requirements…", busy: true, error: false);
        try
        {
            var current = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            bool failed;
            lock (_stateLock) failed = _diagnostics.Controllers.State == ControllerControlState.Failed;
            var enabled = !restore && (failed || !current.Controllers.ExclusiveControl);
            var result = await _diagnosticsService.SetExclusiveControlAsync(enabled, cancellationToken)
                .ConfigureAwait(false);
            var saved = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _diagnostics = _diagnostics with { Controllers = result.Status };
                _busy = false;
                _error = !result.Accepted;
                StatusMessage = result.Accepted ? "Controller preference saved" :
                    "Exclusive control could not be changed. Check the driver status and try again.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOperation("Controller change cancelled", busy: false, error: false);
            return;
        }
        catch (Exception exception) when (exception is PlatformSettingsException or
            PlatformDiagnosticsException or IOException or UnauthorizedAccessException)
        {
            SetOperation("Controller settings are unavailable. Check again.", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private async Task PersistPreferenceAsync(
        SettingsPreferenceMutation mutation,
        CancellationToken cancellationToken)
    {
        if (mutation.IsScale)
        {
            await ReadDisplayAsync(cancellationToken).ConfigureAwait(false);
            OverlayDisplayContext currentDisplay;
            lock (_stateLock) currentDisplay = _display;
            if (currentDisplay.Id.Length == 0 || mutation.DisplayId != currentDisplay.Id)
            {
                SetOperation("The display changed or is unavailable. Check the display name and try again.", false, true);
                return;
            }
            mutation = mutation with { DisplayId = currentDisplay.Id };
        }
        SetOperation("Saving…", busy: true, error: false);
        try
        {
            bool valid;
            PlatformSettingsDocument fallback;
            lock (_stateLock)
            {
                valid = _settingsValid;
                fallback = _settings;
            }
            var saved = await SettingsPreferencePolicy.PersistAsync(
                    _store, fallback, valid, mutation, cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _settingsValid = true;
                _busy = false;
                _error = false;
                StatusMessage = mutation.SuccessStatus;
            }
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Save failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private async Task ToggleControllerShortcutAsync(CancellationToken cancellationToken)
    {
        SetOperation("Saving controller shortcut…", busy: true, error: false);
        try
        {
            var saved = await _store.UpdateAsync(current => current with
            {
                Controllers = current.Controllers with
                {
                    OpenShortcut = current.Controllers.OpenShortcut == ControllerOpenShortcut.Guide
                        ? ControllerOpenShortcut.ViewMenu : ControllerOpenShortcut.Guide,
                },
            }, cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _settingsValid = true;
                _busy = false;
                _error = false;
                StatusMessage = saved.Controllers.OpenShortcut == ControllerOpenShortcut.Guide
                    ? "Controller shortcut set to Guide"
                    : "Controller shortcut set to View + Menu";
            }
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Save failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private void SelectAuthorityRecovery(int displayIndex)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.Diagnostics) return;
            if (!SettingsAuthorityRecoveryPolicy.TrySelect(
                    _diagnostics, displayIndex, out var selection)) return;
            _selectedAuthorityRecovery = selection;
            _page = SettingsPage.AuthorityRecovery;
        }
        Invalidate();
    }

    private async Task RetrySelectedAuthorityRecoveryAsync(CancellationToken cancellationToken)
    {
        SettingsAuthorityRecoveryRequest request;
        bool authorized;
        lock (_stateLock)
        {
            var selection = _page == SettingsPage.AuthorityRecovery
                ? _selectedAuthorityRecovery
                : null;
            authorized = SettingsAuthorityRecoveryPolicy.TryAuthorize(
                _diagnostics, selection, out request);
        }
        if (!authorized)
        {
            lock (_stateLock)
                ApplyAuthorityTransitionLocked(SettingsAuthorityRecoveryPolicy.Changed());
            Invalidate();
            return;
        }

        SetOperation(
            $"Retrying verified recovery for {request.DisplayName}…",
            busy: true,
            error: false);
        PlatformAuthorityRecoveryRetryResult result;
        try
        {
            result = await _diagnosticsService.RetryAuthorityRecoveryAsync(
                    request.ConfirmationToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_stateLock)
                ApplyAuthorityTransitionLocked(
                    SettingsAuthorityRecoveryPolicy.Cancelled(request));
            Invalidate();
            throw;
        }
        catch (PlatformDiagnosticsException exception)
        {
            PlatformDiagnosticsSnapshot? refreshed = null;
            try
            {
                refreshed = await _diagnosticsService.GetSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (PlatformDiagnosticsException)
            {
                // Retain the last sanitized snapshot when the follow-up read also fails.
            }
            lock (_stateLock)
            {
                if (refreshed is not null) _diagnostics = refreshed;
                ApplyAuthorityTransitionLocked(
                    SettingsAuthorityRecoveryPolicy.Failure(
                        _diagnostics, request, exception.Code));
            }
            Invalidate();
            return;
        }

        PlatformDiagnosticsSnapshot diagnostics;
        try
        {
            diagnostics = await _diagnosticsService.GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PlatformDiagnosticsException exception)
        {
            lock (_stateLock)
                ApplyAuthorityTransitionLocked(
                    SettingsAuthorityRecoveryPolicy.RefreshFailure(
                        result, request, exception.Code));
            Invalidate();
            return;
        }
        lock (_stateLock)
        {
            _diagnostics = diagnostics;
            ApplyAuthorityTransitionLocked(
                SettingsAuthorityRecoveryPolicy.Result(result, diagnostics, request));
        }
        Invalidate();
    }

    private void ApplyAuthorityTransitionLocked(SettingsAuthorityRecoveryTransition transition)
    {
        _page = transition.Page;
        _selectedAuthorityRecovery = transition.Selection;
        _busy = false;
        _error = transition.IsError;
        StatusMessage = transition.Status;
    }

    private void Navigate(SettingsPage page)
    {
        var startup = page == SettingsPage.Overlay ? _startup.Read() : null;
        lock (_stateLock)
        {
            if (startup is not null) _startupStatus = startup;
            var previousPage = _page;
            _page = page;
            if (page == SettingsPage.Permissions &&
                previousPage != SettingsPage.PermissionDiagnostics)
                _permissionState = _permissionState with { DiagnosticsReturnFocus = false };
        }
        EnsureSettingsStatusPolling();
        Invalidate();
    }

    private void SetOperation(string status, bool busy, bool error)
    {
        lock (_stateLock)
        {
            StatusMessage = status;
            _busy = busy;
            _error = error;
        }
        Invalidate();
    }

    private SettingsPage PackageCapabilitiesReturnPage()
    {
        lock (_stateLock) return _permissionState.PackageCapabilitiesReturnPage;
    }

    private async Task<string?> ReloadPermissionsAsync(CancellationToken cancellationToken,
        Task<WidgetCatalogSnapshot>? catalogRead = null)
    {
        IReadOnlyList<SettingsPermissionPackage> packages = [];
        var catalogValid = true;
        var catalogComplete = true;
        string? catalogDiagnostic = null;
        var unknownDeclarations = new SettingsUnknownDeclarationAccumulator();
        try
        {
            var catalog = await (catalogRead ?? SettingsReadinessRetry.CatalogAsync(
                    _widgetCatalog.DiscoverAsync,
                    cancellationToken))
                .ConfigureAwait(false);
            var discovered = new Dictionary<string, SettingsPermissionPackage>(StringComparer.Ordinal);
            if (_bundledWidgetRoot is not null)
            {
                foreach (var manifest in DiscoverBundledManifests(_bundledWidgetRoot))
                {
                    var package = SettingsPermissionProjectionPolicy.CreatePackage(
                        manifest, manifest.Publisher, unknownDeclarations);
                    if (package.Capabilities.Count != 0)
                        discovered.TryAdd(package.Id, package);
                }
            }
            foreach (var widget in catalog.Widgets.Take(
                         SettingsPermissionProjectionPolicy.MaximumPermissionPackages))
            {
                var manifest = widget.ActiveVersion.Manifest;
                var package = SettingsPermissionProjectionPolicy.CreatePackage(
                    manifest,
                    InstalledWidgetAuthority.PublisherId(widget.ActiveVersion),
                    unknownDeclarations,
                    widget.ActiveVersion.ContentDigest);
                if (package.Capabilities.Count != 0)
                    discovered.TryAdd(package.Id, package);
            }
            packages = discovered.Values
                .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Id, StringComparer.Ordinal)
                .Take(SettingsPermissionProjectionPolicy.MaximumPermissionPackages)
                .ToArray();
            if (catalog.Widgets.Count > SettingsPermissionProjectionPolicy.MaximumPermissionPackages ||
                discovered.Count > SettingsPermissionProjectionPolicy.MaximumPermissionPackages)
            {
                catalogComplete = false;
                catalogDiagnostic = $"Installed package list is limited to {SettingsPermissionProjectionPolicy.MaximumPermissionPackages} entries";
            }
        }
        catch (WidgetPackageException exception)
        {
            catalogValid = false;
            catalogComplete = false;
            catalogDiagnostic = $"Catalog unavailable ({exception.Code})";
        }
        catch (IOException)
        {
            catalogValid = false;
            catalogComplete = false;
            catalogDiagnostic = "Catalog unavailable (io_error)";
        }
        catch (UnauthorizedAccessException)
        {
            catalogValid = false;
            catalogComplete = false;
            catalogDiagnostic = "Catalog unavailable (access_denied)";
        }

        ConsentDocument consent = ConsentDocument.Empty;
        var consentValid = true;
        string? consentDiagnostic = null;
        try
        {
            consent = await _consentStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BrokerException exception)
        {
            consentValid = false;
            consentDiagnostic = $"Consent unavailable ({exception.Code})";
        }
        catch (IOException)
        {
            consentValid = false;
            consentDiagnostic = "Consent unavailable (io_error)";
        }
        catch (UnauthorizedAccessException)
        {
            consentValid = false;
            consentDiagnostic = "Consent unavailable (access_denied)";
        }

        var projection = SettingsPermissionProjectionPolicy.Compose(
            packages,
            consent,
            catalogValid,
            catalogComplete,
            consentValid,
            catalogDiagnostic,
            consentDiagnostic,
            unknownDeclarations);
        lock (_stateLock)
        {
            var transition = SettingsPermissionPolicy.Reconcile(
                _permissionState, projection, _page);
            _permissionState = transition.State;
            _page = transition.Page;
        }
        return !catalogValid || !consentValid ? projection.Diagnostic : null;
    }

    private static IReadOnlyList<WidgetManifest> DiscoverBundledManifests(string root)
    {
        if (!Directory.Exists(root)) return [];
        RejectReparsePoint(root);
        var manifests = new List<WidgetManifest>();
        var directories = Directory.EnumerateDirectories(root)
            .Order(StringComparer.Ordinal)
            .Take(MaximumBundledDirectories + 1)
            .ToArray();
        if (directories.Length > MaximumBundledDirectories)
            throw new IOException("Bundled widget directory limit exceeded.");
        foreach (var directory in directories)
        {
            RejectReparsePoint(directory);
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            RejectReparsePoint(manifestPath);
            if (new FileInfo(manifestPath).Length > MaximumManifestBytes)
                throw new IOException("Bundled widget manifest exceeds its bound.");
            WidgetManifest manifest;
            try { manifest = ManifestJson.Deserialize(File.ReadAllBytes(manifestPath)); }
            catch (JsonException exception)
            {
                throw new WidgetPackageException(
                    "invalid_bundled_manifest", "Bundled widget manifest is invalid.", exception);
            }
            if (WidgetManifestValidator.Validate(manifest).Count != 0)
                throw new WidgetPackageException(
                    "invalid_bundled_manifest", "Bundled widget manifest failed validation.");
            manifests.Add(manifest);
        }
        return manifests;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new WidgetPackageException(
                "unsafe_bundled_catalog", "Bundled widget catalog path is unsafe.");
    }

    private async Task RequestApplicationControlAsync(bool restart, CancellationToken cancellationToken)
    {
        SetOperation(restart ? "Restarting WidgetRail…" : "Closing WidgetRail…", true, false);
        try
        {
            var result = await _diagnosticsService.RequestApplicationControlAsync(restart, cancellationToken).ConfigureAwait(false);
            if (!result.Accepted) SetOperation("WidgetRail could not accept the request. Try again.", false, true);
        }
        catch (PlatformDiagnosticsException) { SetOperation("WidgetRail is unavailable. Try again.", false, true); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { SetOperation("Request cancelled", false, false); }
    }

    private async Task AllowAllPermissionsAsync(CancellationToken cancellationToken)
    {
        SettingsPermissionPackage? selected;
        lock (_stateLock)
            selected = _page == SettingsPage.PackageCapabilities && _permissionState.Projection.ConsentValid &&
                _permissionState.Projection.CatalogValid ? _permissionState.SelectedPackage : null;
        if (selected is null || selected.Capabilities.Count == 0) return;
        SetOperation("Allowing permissions…", busy: true, error: false);
        try
        {
            await ReloadPermissionsAsync(cancellationToken).ConfigureAwait(false);
            SettingsPermissionPackage? current;
            lock (_stateLock)
                current = _permissionState.Projection.CatalogValid && _permissionState.Projection.ConsentValid
                    ? _permissionState.SelectedPackage : null;
            if (current is null || current.Id != selected.Id || current.AuthorityPublisher != selected.AuthorityPublisher ||
                !current.Capabilities.SequenceEqual(selected.Capabilities))
            {
                SetOperation("The widget's permission requests changed. Review the list before allowing access.", false, true);
                return;
            }
            var updated = await _consentStore.SetDecisionsAsync(SettingsPermissionPolicy.ConsentIdentity(current),
                current.Capabilities.Select(capability => capability.Id).ToArray(), ConsentDecision.Grant, cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
                _permissionState = _permissionState with { Projection = _permissionState.Projection with { Consent = updated } };
            SetOperation($"All listed permissions allowed for {current.Name}", false, false);
        }
        catch (BrokerException exception) { SetOperation($"Permission change failed ({exception.Code})", false, true); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { SetOperation("Permission change cancelled", false, false); }
    }

    private async Task ChangeConsentAsync(
        ConsentDecision decision, CancellationToken cancellationToken)
    {
        SettingsPermissionPackage? package;
        SettingsDeclaredCapability? capability;
        bool consentValid;
        bool confirmationActive;
        lock (_stateLock)
        {
            package = _permissionState.SelectedPackage;
            capability = package?.Capabilities.FirstOrDefault(item => string.Equals(
                item.Id, _permissionState.SelectedCapabilityId, StringComparison.Ordinal));
            consentValid = _permissionState.Projection.ConsentValid;
            confirmationActive = _page == SettingsPage.CapabilityDecision;
        }
        if (!confirmationActive || !consentValid || package is null || capability is null ||
            !PlatformCapabilities.TryGet(capability.Id, out _))
        {
            SetOperation("Permission change denied; declaration or consent state is unavailable",
                busy: false, error: true);
            return;
        }
        SetOperation(decision == ConsentDecision.Grant ? "Granting capability…" : "Denying capability…",
            busy: true, error: false);
        try
        {
            var updated = await _consentStore.SetDecisionAsync(
                SettingsPermissionPolicy.ConsentIdentity(package),
                capability.Id,
                decision,
                cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _permissionState = _permissionState with
                {
                    Projection = _permissionState.Projection with { Consent = updated },
                };
                _busy = false;
                _error = false;
                StatusMessage = decision == ConsentDecision.Grant
                    ? $"Granted {SettingsPermissionPresentation.CapabilityName(capability.Id)}"
                    : $"Denied {SettingsPermissionPresentation.CapabilityName(capability.Id)}";
            }
        }
        catch (BrokerException exception)
        {
            lock (_stateLock)
            {
                if (exception.Code is "invalid_consent" or "unsafe_consent_store")
                    _permissionState = _permissionState with
                    {
                        Projection = _permissionState.Projection with { ConsentValid = false },
                    };
                _busy = false;
                _error = true;
                StatusMessage = $"Permission change failed ({exception.Code})";
            }
        }
        Invalidate();
    }

    private void SelectPermissionPackage(int index)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.Permissions ||
                !SettingsPermissionPolicy.TrySelectPackage(
                    _permissionState, index, out var transition)) return;
            _permissionState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
    }

    private void OpenSelectedInstalledPermissions()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetDetails)
                return;
            var packageId = _installedState.SelectedInstalled?.ActiveVersion.Manifest.Id ??
                            _installedState.SelectedBuiltIn?.Id;
            if (!SettingsPermissionPolicy.TryOpenInstalledPackage(
                    _permissionState,
                    _installedState.CatalogValid,
                    packageId,
                    out var transition))
            {
                StatusMessage = "This widget does not request host permissions";
                _error = false;
            }
            else
            {
                _permissionState = transition.State;
                _page = transition.Page;
            }
        }
        Invalidate();
    }

    private void OpenPermissionDiagnostics()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.Permissions ||
                !SettingsPermissionPolicy.TryOpenDiagnostics(
                    _permissionState, out var transition))
                return;
            _permissionState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
    }

    private void SelectCapability(int index)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.PackageCapabilities ||
                !SettingsPermissionPolicy.TrySelectCapability(
                    _permissionState, index, out var transition))
                return;
            _permissionState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
    }

    private async Task<string?> ReloadInstalledWidgetsAsync(CancellationToken cancellationToken,
        Task<WidgetCatalogSnapshot>? catalogRead = null)
    {
        try
        {
            var snapshot = await (catalogRead ?? SettingsReadinessRetry.CatalogAsync(
                    _widgetCatalog.DiscoverAsync,
                    cancellationToken))
                .ConfigureAwait(false);
            var builtIn = _bundledWidgetRoot is null
                ? []
                : DiscoverBundledManifests(_bundledWidgetRoot)
                    .GroupBy(manifest => manifest.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(manifest => manifest.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(manifest => manifest.Id, StringComparer.Ordinal)
                    .ToArray();
            lock (_stateLock)
            {
                var transition = SettingsInstalledWidgetPolicy.Reconcile(
                    _installedState, snapshot, builtIn, _page);
                _installedState = transition.State;
                _page = transition.Page;
            }
            return null;
        }
        catch (WidgetPackageException exception)
        {
            WidgetCatalogHealthSnapshot health;
            try
            {
                health = await _widgetCatalog.InspectHealthAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception inspectionException) when (inspectionException is
                WidgetPackageException or IOException or UnauthorizedAccessException)
            {
                health = new WidgetCatalogHealthSnapshot(exception.Code, []);
            }
            return SetInstalledWidgetCatalogFailure(
                $"Installed widget catalog unavailable ({exception.Code})", health);
        }
        catch (IOException)
        {
            return SetInstalledWidgetCatalogFailure("Installed widget catalog unavailable (io_error)");
        }
        catch (UnauthorizedAccessException)
        {
            return SetInstalledWidgetCatalogFailure("Installed widget catalog unavailable (access_denied)");
        }
    }

    private string SetInstalledWidgetCatalogFailure(
        string diagnostic,
        WidgetCatalogHealthSnapshot? health = null)
    {
        lock (_stateLock)
        {
            var transition = SettingsInstalledWidgetPolicy.Failure(
                _installedState,
                diagnostic,
                health ?? new WidgetCatalogHealthSnapshot(null, []),
                _page);
            _installedState = transition.State;
            _page = transition.Page;
        }
        return diagnostic;
    }

    private void SelectRepairCandidate(int index)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgets ||
                !SettingsInstalledWidgetPolicy.TrySelectRepair(
                    _installedState, index, out var transition)) return;
            _installedState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
    }

    private async Task RemoveSelectedRepairCandidateAsync(CancellationToken cancellationToken)
    {
        WidgetCatalogRepairCandidate? candidate;
        lock (_stateLock)
            candidate = _page == SettingsPage.InstalledWidgetRecovery
                ? _installedState.SelectedRepair
                : null;
        if (candidate is null || !candidate.CanRemove) return;
        SetOperation($"Removing {candidate.Id} {candidate.Version}…", busy: true, error: false);
        try
        {
            var result = await _widgetCatalog.RemoveInactiveVersionAsync(
                candidate.Id, candidate.Version, cancellationToken).ConfigureAwait(false);
            var warning = await ReloadInstalledWidgetsAsync(cancellationToken).ConfigureAwait(false);
            if (warning is null)
                warning = await ReloadPermissionsAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _page = SettingsPage.InstalledWidgets;
                _installedState = _installedState with { SelectedRepair = null };
                _busy = false;
                _error = warning is not null;
                StatusMessage = warning ??
                    $"Removed inactive {result.Id} {result.Version}" +
                    (result.CleanupPending ? "; staging cleanup is pending" : string.Empty);
            }
        }
        catch (WidgetPackageException exception)
        {
            SetOperation($"Catalog repair failed ({exception.Code})", busy: false, error: true);
            return;
        }
        catch (KeyNotFoundException)
        {
            SetOperation("Catalog repair failed (package_not_found)", busy: false, error: true);
            return;
        }
        catch (IOException)
        {
            SetOperation("Catalog repair failed (io_error)", busy: false, error: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            SetOperation("Catalog repair failed (access_denied)", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private void ChangeInstalledWidgetPage(int delta)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgets) return;
            _installedState = SettingsInstalledWidgetPolicy.ChangeCatalogPage(
                _installedState, delta);
        }
        Invalidate();
    }

    private async Task SelectInstalledWidgetAsync(int index, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (!SettingsInstalledWidgetPolicy.TrySelectInstalled(
                    _installedState, index, out var transition)) return;
            _installedState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
        await InspectSelectedWidgetLocalDataAsync(cancellationToken).ConfigureAwait(false);
        await InspectSelectedWidgetUninstallAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SelectBuiltInWidgetAsync(int index, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (!SettingsInstalledWidgetPolicy.TrySelectBuiltIn(
                    _installedState, index, out var transition)) return;
            _installedState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
        await InspectSelectedWidgetLocalDataAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task OpenSelectedBuiltInCopyAsync(CancellationToken cancellationToken)
    {
        int index;
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetDetails || !_installedState.CatalogValid ||
                _installedState.SelectedInstalledBuiltIn is not { } builtIn) return;
            index = _installedState.BuiltIns.ToList().FindIndex(item => item.Id == builtIn.Id);
        }
        await SelectBuiltInWidgetAsync(index, cancellationToken).ConfigureAwait(false);
    }

    private async Task InspectSelectedWidgetLocalDataAsync(CancellationToken cancellationToken)
    {
        string? widgetId;
        bool unusedCopyEnabled = false;
        bool builtIn;
        lock (_stateLock)
        {
            builtIn = _installedState.SelectedBuiltIn is not null;
            if (_installedState.SelectedInstalled is { Enabled: true } unused &&
                _installedState.SelectedInstalledBuiltIn is not null)
            {
                _installedState = _installedState with
                {
                    LocalData = new(unused.Id, unused.Name, false, "unused_copy_enabled", null),
                };
                unusedCopyEnabled = true;
            }
            widgetId = _installedState.SelectedInstalled?.ActiveVersion.Manifest.Id ??
                       _installedState.SelectedBuiltIn?.Id;
        }
        if (unusedCopyEnabled) { Invalidate(); return; }
        if (widgetId is null) return;
        PlatformWidgetLocalDataInspection inspection;
        try
        {
            inspection = await (builtIn
                ? _diagnosticsService.InspectBuiltInWidgetLocalDataAsync(widgetId, cancellationToken)
                : _diagnosticsService.InspectWidgetLocalDataAsync(widgetId, cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (PlatformDiagnosticsException exception)
        {
            inspection = new(widgetId, widgetId, false, exception.Code, null);
        }
        lock (_stateLock)
        {
            var currentId = _installedState.SelectedInstalled?.ActiveVersion.Manifest.Id ??
                            _installedState.SelectedBuiltIn?.Id;
            if (!string.Equals(currentId, inspection.WidgetId, StringComparison.Ordinal) ||
                builtIn != (_installedState.SelectedBuiltIn is not null)) return;
            _installedState = _installedState with { LocalData = inspection };
        }
        Invalidate();
    }

    private async Task InspectSelectedWidgetUninstallAsync(CancellationToken cancellationToken)
    {
        string? widgetId;
        lock (_stateLock)
            widgetId = _installedState.SelectedInstalled?.Id;
        if (widgetId is null) return;
        try
        {
            var inspection = await _packageUninstall.InspectAsync(widgetId, cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                if (!string.Equals(_installedState.SelectedInstalled?.Id,
                        inspection.WidgetId, StringComparison.Ordinal)) return;
                _installedState = _installedState with { PackageUninstall = inspection };
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task OpenInstalledWidgetUninstallAsync(CancellationToken cancellationToken)
    {
        if (CurrentPage != SettingsPage.InstalledWidgetDetails) return;
        await InspectSelectedWidgetUninstallAsync(cancellationToken).ConfigureAwait(false);
        bool opened;
        lock (_stateLock)
        {
            opened = SettingsInstalledWidgetUninstallPolicy.TryOpen(_installedState, out var transition);
            if (opened)
            {
                _installedState = transition.State;
                _page = transition.Page;
            }
        }
        if (!opened) SetOperation("This widget could not be checked for removal. Refresh its details and try again.", false, true);
        else Invalidate();
    }

    private void CancelInstalledWidgetUninstall()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetUninstall) return;
            var transition = SettingsInstalledWidgetUninstallPolicy.Cancel(_installedState);
            _installedState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
    }

    private async Task UninstallSelectedWidgetAsync(CancellationToken cancellationToken)
    {
        PlatformWidgetPackageUninstallInspection? displayed;
        CatalogWidget? selected;
        lock (_stateLock) selected = _installedState.CatalogValid ? _installedState.SelectedInstalled : null;
        lock (_stateLock)
            displayed = _page == SettingsPage.InstalledWidgetUninstall
                ? _installedState.PackageUninstall
                : null;
        if (selected is null || displayed is null || !SettingsInstalledWidgetUninstallPolicy.Matches(selected, displayed)) return;

        SetOperation("Checking current installed package…", busy: true, error: false);
        try
        {
            if (selected.Enabled)
            {
                var current = await _packageUninstall.InspectAsync(selected.Id, cancellationToken).ConfigureAwait(false);
                if (!SettingsInstalledWidgetUninstallPolicy.Matches(selected, current))
                { SetOperation("This widget changed. Open its details and confirm again.", false, true); return; }
                await _widgetCatalog.SetEnabledAsync(selected.Id, false, cancellationToken).ConfigureAwait(false);
                await ReloadInstalledWidgetsAsync(CancellationToken.None).ConfigureAwait(false);
                displayed = await _packageUninstall.InspectAsync(selected.Id, cancellationToken).ConfigureAwait(false);
                if (!SettingsInstalledWidgetUninstallPolicy.Matches(selected, displayed) || !displayed.CanUninstall || displayed.ConfirmationToken is null)
                { SetOperation("Widget disabled. It could not be uninstalled yet; open its details and try again.", false, true); return; }
            }
            if (!displayed.CanUninstall || displayed.ConfirmationToken is null)
            { SetOperation("Uninstall is not available yet. Open this widget again and retry.", false, true); return; }
            var execution = await _packageUninstall.ExecuteAsync(displayed, cancellationToken)
                .ConfigureAwait(false);
            string? warning = null;
            if (execution.ReloadCatalog)
            {
                warning = await ReloadInstalledWidgetsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                if (warning is null)
                    warning = await ReloadPermissionsAsync(CancellationToken.None)
                        .ConfigureAwait(false);
            }
            lock (_stateLock)
            {
                if (!execution.ReloadCatalog)
                {
                    _installedState = _installedState with
                    {
                        PackageUninstall = execution.Current,
                    };
                }
                _page = execution.ReloadCatalog
                    ? SettingsPage.InstalledWidgets
                    : SettingsPage.InstalledWidgetDetails;
                _busy = false;
                _error = warning is not null || execution.Error;
                StatusMessage = warning ?? execution.Status;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOperation("Widget uninstall cancelled", busy: false, error: true);
            return;
        }
        catch (Exception exception) when (exception is WidgetPackageException or IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            SetOperation("The widget could not be uninstalled. Open its details and try again.", false, true);
            return;
        }
        finally
        {
            lock (_stateLock)
            {
                _busy = false;
                if (_page == SettingsPage.InstalledWidgetUninstall) _page = SettingsPage.InstalledWidgetDetails;
            }
            Invalidate();
        }
    }

    private void OpenInstalledWidgetLocalData()
    {
        lock (_stateLock)
        {
            var widgetId = _installedState.SelectedInstalled?.ActiveVersion.Manifest.Id ??
                           _installedState.SelectedBuiltIn?.Id;
            if (_page != SettingsPage.InstalledWidgetDetails ||
                _installedState.LocalData is not { Exists: true, ConfirmationToken: not null } data ||
                !string.Equals(data.WidgetId, widgetId, StringComparison.Ordinal)) return;
            _page = SettingsPage.InstalledWidgetLocalData;
        }
        Invalidate();
    }

    private async Task ClearSelectedWidgetLocalDataAsync(CancellationToken cancellationToken)
    {
        string? widgetId;
        string? displayedToken;
        bool builtIn;
        lock (_stateLock)
        {
            builtIn = _installedState.SelectedBuiltIn is not null;
            widgetId = _installedState.SelectedInstalled?.ActiveVersion.Manifest.Id ??
                       _installedState.SelectedBuiltIn?.Id;
            displayedToken = _page == SettingsPage.InstalledWidgetLocalData
                ? _installedState.LocalData?.ConfirmationToken
                : null;
        }
        if (widgetId is null || displayedToken is null) return;
        SetOperation("Checking current local data…", busy: true, error: false);
        PlatformWidgetLocalDataInspection current;
        try
        {
            current = await (builtIn
                ? _diagnosticsService.InspectBuiltInWidgetLocalDataAsync(widgetId, cancellationToken)
                : _diagnosticsService.InspectWidgetLocalDataAsync(widgetId, cancellationToken)).ConfigureAwait(false);
            if (!string.Equals(current.ConfirmationToken, displayedToken, StringComparison.Ordinal))
            {
                lock (_stateLock)
                {
                    _installedState = _installedState with { LocalData = current };
                    _page = SettingsPage.InstalledWidgetDetails;
                    _busy = false;
                    _error = true;
                    StatusMessage = "Local data changed; review and confirm again";
                }
                Invalidate();
                return;
            }
            SetOperation("Clearing local data and restarting widget…", busy: true, error: false);
            var result = await (builtIn
                ? _diagnosticsService.ClearBuiltInWidgetLocalDataAsync(widgetId, displayedToken, cancellationToken)
                : _diagnosticsService.ClearWidgetLocalDataAsync(widgetId, displayedToken, cancellationToken)).ConfigureAwait(false);
            lock (_stateLock)
            {
                _page = SettingsPage.InstalledWidgetDetails;
                _installedState = _installedState with
                {
                    LocalData = result.Status is PlatformWidgetLocalDataClearStatus.Cleared or
                        PlatformWidgetLocalDataClearStatus.NoState
                        ? new(widgetId, current.DisplayName, false, "no_local_data", null)
                        : current,
                };
                _busy = false;
                _error = result.Status is not (
                    PlatformWidgetLocalDataClearStatus.Cleared or
                    PlatformWidgetLocalDataClearStatus.NoState);
                StatusMessage = result.Status switch
                {
                    PlatformWidgetLocalDataClearStatus.Cleared => "Local data cleared; widget restarted",
                    PlatformWidgetLocalDataClearStatus.NoState => "No local data remained to clear",
                    PlatformWidgetLocalDataClearStatus.Stale => "Local data changed; confirm again",
                    PlatformWidgetLocalDataClearStatus.RestartFailed =>
                        "Widget restart failed; inspect current local data before retrying",
                    _ => $"Local data clear failed ({result.Code})",
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOperation("Local data clear cancelled", busy: false, error: true);
            return;
        }
        catch (PlatformDiagnosticsException exception)
        {
            SetOperation($"Local data clear failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
        await InspectSelectedWidgetUninstallAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OpenWidgetUpdate()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetDetails || !_installedState.CatalogValid || _installedState.SelectedInstalled is not { } package) return;
            _installedState = _installedState with { UpdateFromVersion = package.ActiveVersion.Version };
            _page = SettingsPage.InstalledWidgetUpdate;
        }
        Invalidate();
    }

    private async Task ReviewWidgetUpdateAsync(CancellationToken cancellationToken)
    {
        string? id;
        Version? previous;
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetUpdate) return;
            id = _installedState.SelectedInstalledId;
            previous = _installedState.UpdateFromVersion;
        }
        if (id is null || previous is null) return;
        var warning = await ReloadInstalledWidgetsAsync(cancellationToken).ConfigureAwait(false);
        if (warning is not null) { SetOperation(warning, false, true); return; }
        CatalogWidget? current;
        lock (_stateLock) current = _installedState.SelectedInstalled;
        if (current is null || current.Id != id || current.ActiveVersion.Version <= previous)
        { SetOperation("The update is not ready yet. Choose an update file and wait for installation to finish.", false, false); return; }
        warning = await ReloadPermissionsAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateLock) _page = SettingsPage.InstalledWidgetDetails;
        await InspectSelectedWidgetLocalDataAsync(cancellationToken).ConfigureAwait(false);
        await InspectSelectedWidgetUninstallAsync(cancellationToken).ConfigureAwait(false);
        if (warning is null) OpenSelectedInstalledPermissions();
        SetOperation(warning ?? $"Version {current.ActiveVersion.Version} is selected. Review permissions, then enable the widget from its details.", false, warning is not null);
    }

    private void OpenVersionRemoval(int index)
    {
        lock (_stateLock)
        {
            var package = _installedState.CatalogValid ? _installedState.SelectedInstalled : null;
            if (_page != SettingsPage.InstalledWidgetVersions || package is null || index < 0 || index >= package.Versions.Count) return;
            var version = package.Versions[index];
            if (version.Version == package.ActiveVersion.Version) return;
            _installedState = _installedState with
            {
                VersionRemoval = new(package.Id, package.Name, version.Version, version.ContentDigest),
                VersionFocusId = $"installed.version.remove.{index}",
            };
            _page = SettingsPage.InstalledWidgetVersionRemoval;
        }
        Invalidate();
    }

    private void CancelVersionRemoval()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetVersionRemoval) return;
            _installedState = _installedState with { VersionRemoval = null };
            _page = SettingsPage.InstalledWidgetVersions;
        }
        Invalidate();
    }

    private async Task RemoveSelectedVersionAsync(CancellationToken cancellationToken)
    {
        SettingsVersionRemoval? selected;
        lock (_stateLock) selected = _page == SettingsPage.InstalledWidgetVersionRemoval && _installedState.CatalogValid
            ? _installedState.VersionRemoval : null;
        if (selected is null) return;
        SetOperation($"Removing version {selected.Version}…", true, false);
        try
        {
            var result = await _widgetCatalog.RemoveInactiveVersionConfirmedAsync(selected.WidgetId, selected.Version,
                selected.Digest, cancellationToken).ConfigureAwait(false);
            var warning = await ReloadInstalledWidgetsAsync(CancellationToken.None).ConfigureAwait(false);
            lock (_stateLock)
            {
                _installedState = _installedState with { VersionRemoval = null, VersionFocusId = null };
                _page = _installedState.SelectedInstalled is null ? SettingsPage.InstalledWidgets : SettingsPage.InstalledWidgetVersions;
            }
            SetOperation(warning ?? (result.CleanupPending ? "Version removed. Windows will finish deleting its files later." : $"Version {selected.Version} removed"), false, warning is not null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { SetOperation("Version removal cancelled", false, false); }
        catch (Exception exception) when (exception is WidgetPackageException or IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            await ReloadInstalledWidgetsAsync(CancellationToken.None).ConfigureAwait(false);
            SetOperation(exception is WidgetPackageException { Code: "selected_version" or "version_changed" }
                ? "This version changed or is now in use. Review the version list and try again."
                : "This version could not be removed. It may still be in use; try again shortly.", false, true);
        }
    }

    private void OpenInstalledWidgetVersions()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetDetails ||
                !SettingsInstalledWidgetPolicy.TryOpenVersions(
                    _installedState, out var transition))
                return;
            _installedState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
    }

    private void ChangeInstalledVersionPage(int delta)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetVersions) return;
            if (_installedState.SelectedInstalled is null) return;
            _installedState = SettingsInstalledWidgetPolicy.ChangeVersionPage(
                _installedState, delta);
        }
        Invalidate();
    }

    private async Task SelectInstalledVersionAsync(int index, CancellationToken cancellationToken)
    {
        CatalogWidget? selected;
        InstalledWidgetVersion? requested;
        lock (_stateLock)
        {
            selected = _installedState.CatalogValid ? _installedState.SelectedInstalled : null;
            requested = selected is not null && index >= 0 && index < selected.Versions.Count
                ? selected.Versions[index]
                : null;
        }
        if (selected is null || requested is null) return;
        if (selected.Enabled)
        {
            SetOperation("Disable the widget before changing versions", busy: false, error: true);
            return;
        }
        if (requested.Version == selected.ActiveVersion.Version) return;

        SetOperation($"Selecting {requested.Version}…", busy: true, error: false);
        try
        {
            await _widgetCatalog.SetActiveVersionAsync(
                selected.Id, requested.Version, cancellationToken).ConfigureAwait(false);
            var refreshed = await _widgetCatalog.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            // Version changes can replace declarations and always replace the
            // installed package authority. Refresh the permission projection
            // before reporting success so this still-visible Settings worker
            // can never grant against the previously selected version.
            var permissionWarning = await ReloadPermissionsAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                var transition = SettingsInstalledWidgetPolicy.Reconcile(
                    _installedState, refreshed, _installedState.BuiltIns, _page);
                _installedState = transition.State;
                _page = transition.Page;
                _busy = false;
                _error = permissionWarning is not null;
                StatusMessage = permissionWarning ??
                    $"{selected.Name} {requested.Version} selected; review its permissions before enabling";
            }
        }
        catch (WidgetPackageException exception)
        {
            SetOperation($"Version change failed ({exception.Code})", busy: false, error: true);
            return;
        }
        catch (KeyNotFoundException)
        {
            SetOperation("Version change failed (package_not_found)", busy: false, error: true);
            return;
        }
        catch (IOException)
        {
            SetOperation("Version change failed (io_error)", busy: false, error: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            SetOperation("Version change failed (access_denied)", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private async Task ToggleSelectedBuiltInWidgetAsync(CancellationToken cancellationToken)
    {
        WidgetManifest? selected;
        lock (_stateLock)
            selected = _page == SettingsPage.InstalledWidgetDetails && _installedState.CatalogValid && _settingsValid
                ? _installedState.SelectedBuiltIn : null;
        if (selected is null || selected.Id == BuiltInWidgetSettings.SettingsWidgetId) return;
        SetOperation("Updating widget…", true, false);
        try
        {
            var updated = await _store.UpdateAsync(current => current with
            {
                BuiltInWidgets = new BuiltInWidgetSettings
                {
                    DisabledIds = current.BuiltInWidgets.IsEnabled(selected.Id)
                        ? current.BuiltInWidgets.DisabledIds.Append(selected.Id).Order(StringComparer.Ordinal).ToArray()
                        : current.BuiltInWidgets.DisabledIds.Where(id => id != selected.Id).ToArray(),
                },
            }, cancellationToken).ConfigureAwait(false);
            lock (_stateLock) _settings = updated;
            SetOperation($"{selected.Name} {(updated.BuiltInWidgets.IsEnabled(selected.Id) ? "enabled" : "disabled")}", false, false);
        }
        catch (PlatformSettingsException exception) { SetOperation($"Widget change failed ({exception.Code})", false, true); }
    }

    private async Task ToggleSelectedInstalledWidgetAsync(CancellationToken cancellationToken)
    {
        CatalogWidget? selected;
        lock (_stateLock)
            selected = _installedState.CatalogValid ? _installedState.SelectedInstalled : null;
        if (selected is null) return;

        var nextEnabled = !selected.Enabled;
        bool hasBuiltInCopy;
        lock (_stateLock) hasBuiltInCopy = _installedState.BuiltIns.Any(item => item.Id == selected.Id);
        if (nextEnabled && hasBuiltInCopy)
        {
            SetOperation("This widget is included with WidgetRail. Enable its built-in version instead.", false, false);
            return;
        }
        if (nextEnabled && !WidgetHostCompatibility.Evaluate(selected.ActiveVersion.Manifest).IsSupported)
        {
            SetOperation("Widget cannot be enabled because it is incompatible with this host",
                busy: false, error: true);
            return;
        }
        SetOperation(nextEnabled ? "Enabling widget…" : "Disabling widget…", busy: true, error: false);
        try
        {
            var trustApproval = nextEnabled && WidgetManifestTrust.Resolve(
                    selected.ActiveVersion.Manifest) ==
                WidgetExecutionTrust.FullTrustCurrentUser
                    ? WidgetPackageTrustApproval.FullTrustCurrentUser
                    : WidgetPackageTrustApproval.None;
            await _widgetCatalog.SetEnabledAsync(
                selected.Id, nextEnabled, trustApproval, cancellationToken)
                .ConfigureAwait(false);
            var refreshed = await _widgetCatalog.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                var transition = SettingsInstalledWidgetPolicy.Reconcile(
                    _installedState, refreshed, _installedState.BuiltIns, _page);
                _installedState = transition.State;
                _page = transition.Page;
                _busy = false;
                _error = false;
                StatusMessage = nextEnabled ? $"{selected.Name} enabled" : $"{selected.Name} disabled";
            }
        }
        catch (WidgetPackageException exception)
        {
            SetOperation($"Widget change failed ({exception.Code})", busy: false, error: true);
            return;
        }
        catch (KeyNotFoundException)
        {
            SetOperation("Widget change failed (package_not_found)", busy: false, error: true);
            return;
        }
        catch (IOException)
        {
            SetOperation("Widget change failed (io_error)", busy: false, error: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            SetOperation("Widget change failed (access_denied)", busy: false, error: true);
            return;
        }
        Invalidate();
        await InspectSelectedWidgetLocalDataAsync(cancellationToken).ConfigureAwait(false);
        await InspectSelectedWidgetUninstallAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryThemeIndex(string action, out int index)
    {
        index = -1;
        return action.StartsWith("theme.open.", StringComparison.Ordinal) &&
               int.TryParse(action["theme.open.".Length..], NumberStyles.None,
                   CultureInfo.InvariantCulture, out index);
    }

    private static bool TryIndexedAction(string action, string prefix, out int index)
    {
        index = -1;
        return action.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(action[prefix.Length..], NumberStyles.None,
                   CultureInfo.InvariantCulture, out index);
    }

}
