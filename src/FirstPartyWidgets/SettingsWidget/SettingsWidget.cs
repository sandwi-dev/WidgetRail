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
    private readonly ThemeCatalogMutationPolicy _themeMutations;
    private readonly CatalogService _widgetCatalog;
    private readonly ConsentStore _consentStore;
    private readonly IPlatformDiagnosticsService _diagnosticsService;
    private readonly IPlatformDiagnosticsService _readinessDiagnosticsService;
    private readonly SettingsInstalledWidgetUninstallOperation _packageUninstall;
    private readonly string? _bundledWidgetRoot;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
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
    private Task _controllerStatusTask = Task.CompletedTask;
    private bool _settingsValid = true;
    private bool _busy;
    private bool _error;
    private string _status = "Settings load when this widget becomes visible";

    public SettingsWidget(
        PlatformSettingsStore? store = null,
        ThemeCatalog? catalog = null,
        CatalogService? widgetCatalog = null,
        ConsentStore? consentStore = null,
        IPlatformDiagnosticsService? diagnostics = null,
        string? bundledWidgetRoot = null,
        IPlatformDiagnosticsService? readinessDiagnostics = null)
    {
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
        bool error;
        bool settingsValid;
        PlatformDiagnosticsSnapshot diagnostics;
        SettingsInstalledWidgetState installedState;
        SettingsPermissionState permissionState;
        SettingsThemeSelection? selectedTheme;
        string? themePickerFocusId;
        string? selectedAuthorityRecoveryId;
        string status;
        lock (_stateLock)
        {
            settings = _settings;
            themes = _themes;
            page = _page;
            busy = _busy;
            error = _error;
            settingsValid = _settingsValid;
            diagnostics = _diagnostics;
            installedState = _installedState;
            permissionState = _permissionState;
            selectedAuthorityRecoveryId = _selectedAuthorityRecovery?.RecoveryId;
            selectedTheme = _selectedTheme;
            themePickerFocusId = _themePickerFocusId;
            status = _status;
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
            themePickerFocusId);
        if (SettingsPresentation.TryRender(presentation, out var view)) return view;
        var header = SettingsPresentation.Header(presentation);
        return page switch
        {
            SettingsPage.InstalledWidgets =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgets(
                    header, busy, installedState),
            SettingsPage.InstalledWidgetDetails =>
                SettingsInstalledWidgetPresentation.RenderInstalledWidgetDetails(
                    header, busy, installedState, permissionState, settings),
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

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationLoadCount);
        await ReloadAsync(activeLifetime).ConfigureAwait(false);
        EnsureControllerStatusPolling();
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken) =>
        await _controllerStatusTask.ConfigureAwait(false);

    private void EnsureControllerStatusPolling()
    {
        if (_controllerStatusTask.IsCompleted &&
            !ActiveCancellationToken.IsCancellationRequested)
            _controllerStatusTask = PollControllerStatusAsync(ActiveCancellationToken);
    }

    private async Task PollControllerStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1_000, cancellationToken).ConfigureAwait(false);
                if (CurrentPage != SettingsPage.Controllers ||
                    !await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) continue;
                try
                {
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

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        // ReloadAsync owns the same operation gate used by normal actions. Keep
        // refresh outside that critical section so controller refresh cannot
        // deadlock while still serializing against every other mutation.
        if (action.ActionId == "refresh")
        {
            await ReloadAsync(cancellationToken, "Settings refreshed").ConfigureAwait(false);
            return;
        }
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
                Navigate(targetPage);
                return;
            }
            if (SettingsPreferencePolicy.TryCreate(action.ActionId, out var preference))
            {
                await PersistPreferenceAsync(preference, cancellationToken).ConfigureAwait(false);
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
                case "installed.uninstall.open": OpenInstalledWidgetUninstall(); break;
                case "installed.uninstall.confirm": await UninstallSelectedWidgetAsync(
                    cancellationToken).ConfigureAwait(false); break;
                case "installed.toggle": await ToggleSelectedInstalledWidgetAsync(cancellationToken)
                    .ConfigureAwait(false); break;
                case "installed.surface-appearance.cycle":
                    await CycleSelectedWidgetSurfaceAppearanceAsync(cancellationToken)
                        .ConfigureAwait(false); break;
                case "capability.grant": await ChangeConsentAsync(
                    ConsentDecision.Grant, cancellationToken).ConfigureAwait(false); break;
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
            var installedWarning = await ReloadInstalledWidgetsAsync(cancellationToken)
                .ConfigureAwait(false);
            warning ??= installedWarning;
            var permissionWarning = await ReloadPermissionsAsync(cancellationToken)
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
            lock (_stateLock)
            {
                _settings = settings;
                _settingsValid = settingsValid;
                _themes = themes;
                _diagnostics = diagnostics;
                _busy = false;
                _error = warning is not null;
                _status = warning ?? successStatus;
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
            _status = $"Reviewing {entry.Descriptor.Name} {entry.Descriptor.Version}";
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
            _status = $"Confirm removal of {entry.Descriptor.Name} {entry.Descriptor.Version}";
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
                _status = $"Selected {entry.Descriptor.Name} {entry.Descriptor.Version}";
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
                _status = result.CleanupPending
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
                _status = "Settings reset to defaults";
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
            var enabled = !restore && !current.Controllers.ExclusiveControl;
            var result = await _diagnosticsService.SetExclusiveControlAsync(enabled, cancellationToken)
                .ConfigureAwait(false);
            var saved = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _diagnostics = _diagnostics with { Controllers = result.Status };
                _busy = false;
                _error = !result.Accepted;
                _status = result.Accepted ? "Controller preference saved" :
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
                _status = mutation.SuccessStatus;
            }
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Save failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private async Task CycleSelectedWidgetSurfaceAppearanceAsync(
        CancellationToken cancellationToken)
    {
        string? widgetId;
        bool valid;
        PlatformSettingsDocument fallback;
        lock (_stateLock)
        {
            widgetId = _installedState.SelectedInstalled?.ActiveVersion.Manifest.Id ??
                       _installedState.SelectedBuiltIn?.Id;
            valid = _settingsValid;
            fallback = _settings;
        }
        if (widgetId is null) return;
        SetOperation("Saving widget surface…", busy: true, error: false);
        try
        {
            PlatformSettingsDocument Apply(PlatformSettingsDocument current)
            {
                var overrides = new Dictionary<string, WidgetSurfaceAppearanceOverride>(
                    current.Appearance.WidgetSurfaceAppearanceOverrides,
                    StringComparer.Ordinal);
                var existing = overrides.GetValueOrDefault(
                    widgetId, WidgetSurfaceAppearanceOverride.Widget);
                var next = existing switch
                {
                    WidgetSurfaceAppearanceOverride.Widget => WidgetSurfaceAppearanceOverride.Theme,
                    WidgetSurfaceAppearanceOverride.Theme => WidgetSurfaceAppearanceOverride.Transparent,
                    WidgetSurfaceAppearanceOverride.Transparent => WidgetSurfaceAppearanceOverride.Solid,
                    _ => WidgetSurfaceAppearanceOverride.Widget,
                };
                if (next == WidgetSurfaceAppearanceOverride.Widget)
                    overrides.Remove(widgetId);
                else
                    overrides[widgetId] = next;
                return current with
                {
                    Appearance = current.Appearance with
                    {
                        WidgetSurfaceAppearanceOverrides = overrides,
                    },
                };
            }
            var saved = valid
                ? await _store.UpdateAsync(Apply, cancellationToken).ConfigureAwait(false)
                : await _store.ReplaceAsync(Apply(fallback), cancellationToken)
                    .ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _settingsValid = true;
                _busy = false;
                _error = false;
                _status = $"Surface preference saved for {widgetId}";
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
        _status = transition.Status;
    }

    private void Navigate(SettingsPage page)
    {
        lock (_stateLock)
        {
            var previousPage = _page;
            _page = page;
            if (page == SettingsPage.Permissions &&
                previousPage != SettingsPage.PermissionDiagnostics)
                _permissionState = _permissionState with { DiagnosticsReturnFocus = false };
        }
        EnsureControllerStatusPolling();
        Invalidate();
    }

    private void SetOperation(string status, bool busy, bool error)
    {
        lock (_stateLock)
        {
            _status = status;
            _busy = busy;
            _error = error;
        }
        Invalidate();
    }

    private SettingsPage PackageCapabilitiesReturnPage()
    {
        lock (_stateLock) return _permissionState.PackageCapabilitiesReturnPage;
    }

    private async Task<string?> ReloadPermissionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SettingsPermissionPackage> packages = [];
        var catalogValid = true;
        var catalogComplete = true;
        string? catalogDiagnostic = null;
        var unknownDeclarations = new SettingsUnknownDeclarationAccumulator();
        try
        {
            var catalog = await SettingsReadinessRetry.CatalogAsync(
                    _widgetCatalog.DiscoverAsync,
                    cancellationToken)
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
                _status = decision == ConsentDecision.Grant
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
                _status = $"Permission change failed ({exception.Code})";
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
                _status = "This widget does not request host permissions";
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

    private async Task<string?> ReloadInstalledWidgetsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await SettingsReadinessRetry.CatalogAsync(
                    _widgetCatalog.DiscoverAsync,
                    cancellationToken)
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
                _status = warning ??
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

    private async Task InspectSelectedWidgetLocalDataAsync(CancellationToken cancellationToken)
    {
        string? widgetId;
        lock (_stateLock)
            widgetId = _installedState.SelectedInstalled?.ActiveVersion.Manifest.Id ??
                       _installedState.SelectedBuiltIn?.Id;
        if (widgetId is null) return;
        PlatformWidgetLocalDataInspection inspection;
        try
        {
            inspection = await _diagnosticsService.InspectWidgetLocalDataAsync(
                widgetId, cancellationToken).ConfigureAwait(false);
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
            if (!string.Equals(currentId, inspection.WidgetId, StringComparison.Ordinal)) return;
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

    private void OpenInstalledWidgetUninstall()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetDetails ||
                !SettingsInstalledWidgetUninstallPolicy.TryOpen(
                    _installedState, out var transition)) return;
            _installedState = transition.State;
            _page = transition.Page;
        }
        Invalidate();
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
        lock (_stateLock)
            displayed = _page == SettingsPage.InstalledWidgetUninstall
                ? _installedState.PackageUninstall
                : null;
        if (displayed is not { CanUninstall: true, ConfirmationToken: not null }) return;

        SetOperation("Checking current installed package…", busy: true, error: false);
        try
        {
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
                _status = warning ?? execution.Status;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOperation("Widget uninstall cancelled", busy: false, error: true);
            return;
        }
        Invalidate();
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
        lock (_stateLock)
        {
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
            current = await _diagnosticsService.InspectWidgetLocalDataAsync(
                widgetId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current.ConfirmationToken, displayedToken, StringComparison.Ordinal))
            {
                lock (_stateLock)
                {
                    _installedState = _installedState with { LocalData = current };
                    _page = SettingsPage.InstalledWidgetDetails;
                    _busy = false;
                    _error = true;
                    _status = "Local data changed; review and confirm again";
                }
                Invalidate();
                return;
            }
            SetOperation("Clearing local data and restarting widget…", busy: true, error: false);
            var result = await _diagnosticsService.ClearWidgetLocalDataAsync(
                widgetId, displayedToken, cancellationToken).ConfigureAwait(false);
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
                _status = result.Status switch
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
                _status = permissionWarning ??
                    $"{selected.Name} {requested.Version} selected; review its unsigned digest and capabilities before enabling";
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

    private async Task ToggleSelectedInstalledWidgetAsync(CancellationToken cancellationToken)
    {
        CatalogWidget? selected;
        lock (_stateLock)
            selected = _installedState.CatalogValid ? _installedState.SelectedInstalled : null;
        if (selected is null) return;

        var nextEnabled = !selected.Enabled;
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
                _status = nextEnabled ? $"{selected.Name} enabled" : $"{selected.Name} disabled";
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
