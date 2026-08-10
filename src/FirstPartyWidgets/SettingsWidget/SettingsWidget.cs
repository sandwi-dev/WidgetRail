using System.Globalization;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;
using GameBarAlternative.WidgetCatalog;
using CatalogService = GameBarAlternative.WidgetCatalog.WidgetCatalog;

namespace GameBarAlternative.FirstPartyWidgets.Settings;

public enum SettingsPage
{
    Root,
    Appearance,
    ThemePicker,
    Accessibility,
    AccessibilityVisual,
    Overlay,
    InstalledWidgets,
    InstalledWidgetDetails,
    InstalledWidgetVersions,
    InstalledWidgetRecovery,
    Permissions,
    PermissionDiagnostics,
    PackageCapabilities,
    CapabilityDecision,
    Diagnostics,
    AuthorityRecovery,
    Reset,
}

public sealed partial class SettingsWidget : Widget
{
    private readonly PlatformSettingsStore _store;
    private readonly ThemeCatalog _catalog;
    private readonly CatalogService _widgetCatalog;
    private readonly ConsentStore _consentStore;
    private readonly IPlatformDiagnosticsService _diagnosticsService;
    private readonly string? _bundledWidgetRoot;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private PlatformSettingsDocument _settings = PlatformSettingsDocument.Default;
    private ThemeCatalogSnapshot _themes;
    private PlatformDiagnosticsSnapshot _diagnostics = PlatformDiagnosticsSnapshot.Unavailable();
    private WidgetCatalogSnapshot _installedWidgets = new([]);
    private WidgetCatalogHealthSnapshot _installedCatalogHealth = new(null, []);
    private IReadOnlyList<WidgetManifest> _builtInWidgets = [];
    private bool _installedWidgetCatalogValid = true;
    private string? _installedWidgetDiagnostic;
    private int _installedWidgetPage;
    private int _installedVersionPage;
    private string? _selectedInstalledWidgetId;
    private string? _selectedBuiltInWidgetId;
    private WidgetCatalogRepairCandidate? _selectedRepairCandidate;
    private SettingsAuthorityRecoverySelection? _selectedAuthorityRecovery;
    private SettingsPage _page;
    private int _activationLoadCount;
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
        string? bundledWidgetRoot = null)
    {
        var paths = store?.Paths ?? PlatformSettingsPaths.CreateDefault();
        _store = store ?? new PlatformSettingsStore(paths);
        _catalog = catalog ?? new ThemeCatalog(paths);
        _widgetCatalog = widgetCatalog ?? new CatalogService(
            Path.Combine(paths.RootDirectory, "widgets"));
        _consentStore = consentStore ?? new ConsentStore(
            Path.Combine(paths.RootDirectory, "consent"));
        _diagnosticsService = diagnostics ?? UnavailablePlatformDiagnosticsService.Instance;
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
            selectedAuthorityRecoveryId = _selectedAuthorityRecovery?.RecoveryId;
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
            error);
        if (SettingsPresentation.TryRender(presentation, out var view)) return view;
        var header = SettingsPresentation.Header(presentation);
        return page switch
        {
            SettingsPage.InstalledWidgets => RenderInstalledWidgets(header, busy),
            SettingsPage.InstalledWidgetDetails => RenderInstalledWidgetDetails(header, busy),
            SettingsPage.InstalledWidgetVersions => RenderInstalledWidgetVersions(header, busy),
            SettingsPage.InstalledWidgetRecovery => RenderInstalledWidgetRecovery(header, busy),
            SettingsPage.Permissions => RenderPermissionPackages(header, busy),
            SettingsPage.PermissionDiagnostics => RenderPermissionDiagnostics(header),
            SettingsPage.PackageCapabilities => RenderPackageCapabilities(header, busy),
            SettingsPage.CapabilityDecision => RenderCapabilityDecision(header, busy),
            _ => throw new InvalidOperationException($"Unsupported Settings page {page}."),
        };
    }

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationLoadCount);
        await ReloadAsync(activeLifetime).ConfigureAwait(false);
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
                case "installed.toggle": await ToggleSelectedInstalledWidgetAsync(cancellationToken)
                    .ConfigureAwait(false); break;
                case "capability.grant": await ChangeConsentAsync(
                    ConsentDecision.Grant, cancellationToken).ConfigureAwait(false); break;
                case "capability.deny": await ChangeConsentAsync(
                    ConsentDecision.Deny, cancellationToken).ConfigureAwait(false); break;
                case "reset.confirm": await ResetAsync(cancellationToken).ConfigureAwait(false); break;
                default:
                    if (TryThemeIndex(action.ActionId, out var index))
                        await SelectThemeAsync(index, cancellationToken).ConfigureAwait(false);
                    else if (TryIndexedAction(action.ActionId, "installed.select.", out index))
                        SelectInstalledWidget(index);
                    else if (TryIndexedAction(action.ActionId, "installed.builtin.select.", out index))
                        SelectBuiltInWidget(index);
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
                diagnostics = await _diagnosticsService.GetSnapshotAsync(cancellationToken)
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

    private async Task SelectThemeAsync(int index, CancellationToken cancellationToken)
    {
        ThemeCatalogEntry? entry;
        lock (_stateLock)
            entry = index >= 0 && index < _themes.Themes.Count ? _themes.Themes[index] : null;
        if (entry is null || !entry.IsValid) return;
        await PersistPreferenceAsync(
            SettingsPreferencePolicy.Theme(
                entry.Descriptor.Id,
                entry.Descriptor.Version.ToString(),
                entry.Descriptor.Name),
            cancellationToken).ConfigureAwait(false);
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

    private static ScrollElement PageScope(string id, params WidgetElement[] children) =>
        SettingsPresentation.PageScope(id, children);

    private static WidgetView View(
        StackElement header,
        WidgetElement content,
        string? initialFocus,
        string activeScope) =>
        SettingsPresentation.View(header, content, initialFocus, activeScope);

    private void Navigate(SettingsPage page)
    {
        lock (_stateLock)
        {
            var previousPage = _page;
            _page = page;
            if (page == SettingsPage.Permissions &&
                previousPage != SettingsPage.PermissionDiagnostics)
                _permissionDiagnosticsReturnFocus = false;
        }
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
        lock (_stateLock) return _packageCapabilitiesReturnPage;
    }

    private static bool TryThemeIndex(string action, out int index)
    {
        index = -1;
        return action.StartsWith("theme.select.", StringComparison.Ordinal) &&
               int.TryParse(action["theme.select.".Length..], NumberStyles.None,
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
