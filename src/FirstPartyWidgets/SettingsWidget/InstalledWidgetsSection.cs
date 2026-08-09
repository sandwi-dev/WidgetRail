using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.Settings;

public sealed partial class SettingsWidget
{
    public const int InstalledWidgetsPerPage = 5;
    public const int InstalledVersionsPerPage = 5;

    private async Task<string?> ReloadInstalledWidgetsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _widgetCatalog.DiscoverAsync(cancellationToken).ConfigureAwait(false);
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
                _installedWidgets = snapshot;
                _builtInWidgets = builtIn;
                _installedWidgetCatalogValid = true;
                _installedWidgetDiagnostic = null;
                _installedWidgetPage = Math.Clamp(
                    _installedWidgetPage, 0, LastInstalledWidgetPage(snapshot));
                if (_selectedInstalledWidgetId is not null &&
                    !snapshot.Widgets.Any(widget => widget.Id == _selectedInstalledWidgetId))
                {
                    _selectedInstalledWidgetId = null;
                    _installedVersionPage = 0;
                    if (_page is SettingsPage.InstalledWidgetDetails or SettingsPage.InstalledWidgetVersions)
                        _page = SettingsPage.InstalledWidgets;
                }
                if (_selectedBuiltInWidgetId is not null &&
                    !builtIn.Any(manifest => manifest.Id == _selectedBuiltInWidgetId))
                {
                    _selectedBuiltInWidgetId = null;
                    if (_page == SettingsPage.InstalledWidgetDetails)
                        _page = SettingsPage.InstalledWidgets;
                }
            }
            return null;
        }
        catch (WidgetPackageException exception)
        {
            return SetInstalledWidgetCatalogFailure($"Installed widget catalog unavailable ({exception.Code})");
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

    private string SetInstalledWidgetCatalogFailure(string diagnostic)
    {
        lock (_stateLock)
        {
            _installedWidgets = new WidgetCatalogSnapshot([]);
            _builtInWidgets = [];
            _installedWidgetCatalogValid = false;
            _installedWidgetDiagnostic = diagnostic;
            _installedWidgetPage = 0;
            _selectedInstalledWidgetId = null;
            _selectedBuiltInWidgetId = null;
            _installedVersionPage = 0;
            if (_page is SettingsPage.InstalledWidgetDetails or SettingsPage.InstalledWidgetVersions)
                _page = SettingsPage.InstalledWidgets;
        }
        return diagnostic;
    }

    private WidgetView RenderInstalledWidgets(StackElement header, bool busy)
    {
        WidgetCatalogSnapshot snapshot;
        IReadOnlyList<WidgetManifest> builtIn;
        bool valid;
        string? diagnostic;
        int page;
        string? selectedBuiltInId;
        lock (_stateLock)
        {
            snapshot = _installedWidgets;
            builtIn = _builtInWidgets;
            valid = _installedWidgetCatalogValid;
            diagnostic = _installedWidgetDiagnostic;
            page = _installedWidgetPage;
            selectedBuiltInId = _selectedBuiltInWidgetId;
        }

        var lastPage = LastInstalledWidgetPage(snapshot);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * InstalledWidgetsPerPage;
        var visible = snapshot.Widgets.Skip(start).Take(InstalledWidgetsPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text("Installed widgets", "installed.heading", "Installed widgets").Classes("page-heading"),
            UI.Text(valid
                    ? "Built-in widgets are included with the app. Community packages are unsigned: review their exact sealed bytes and requested capabilities before enabling. Publisher labels are unverified."
                    : diagnostic ?? "Installed widget catalog is unavailable.",
                "installed.help", "Installed widget help")
                .Classes(valid ? "page-help" : "diagnostic-error"),
        };

        if (builtIn.Count != 0)
        {
            children.Add(UI.Text("BUILT IN", "installed.builtin.heading", "Built-in widgets")
                .Classes("section-heading"));
            for (var index = 0; index < builtIn.Count; index++)
            {
                var manifest = builtIn[index];
                children.Add(UI.Button(
                        $"{manifest.Name} · {manifest.Version} · Built-in",
                        $"installed.builtin.select.{index}", $"installed.builtin.item.{index}")
                    .Disabled(!valid).Busy(busy)
                    .Selected(manifest.Id == selectedBuiltInId)
                    .Classes("setting-row", "is-built-in"));
            }
        }

        children.Add(UI.Text("COMMUNITY", "installed.community.heading", "Community widgets")
            .Classes("section-heading"));
        if (lastPage != 0)
            children.Add(UI.Text($"Page {page + 1} of {lastPage + 1}",
                "installed.page-label", "Community widget page").Classes("page-counter"));

        for (var offset = 0; offset < visible.Length; offset++)
        {
            var index = start + offset;
            var package = visible[offset];
            var compatibility = WidgetHostCompatibility.Evaluate(package.ActiveVersion.Manifest);
            var status = package.Enabled
                ? compatibility.IsSupported ? "Enabled" : "Enabled · incompatible"
                : compatibility.IsSupported ? "Disabled · unsigned review required" : "Incompatible";
            children.Add(UI.Button(
                    $"{package.Name} · {package.ActiveVersion.Version} · {status}",
                    $"installed.select.{index}", $"installed.item.{index}")
                .Disabled(!valid).Busy(busy)
                .Selected(package.Enabled)
                .Classes("setting-row", package.Enabled ? "is-enabled" : "is-disabled"));
        }

        if (visible.Length == 0)
            children.Add(UI.Text(valid ? "No community widgets are installed." : "No package actions are available.",
                "installed.empty", "Installed widget list status").Classes("diagnostic-line"));
        children.Add(UI.Button("Back", "back", "installed.back").Classes("secondary-button"));
        LinkVertical(children);

        var scope = UI.VerticalScroll("installed.widgets", children.ToArray())
            .InputScope("installed.widgets")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        if (page > 0) scope = scope.Shortcut(ControllerButton.LeftBumper, "installed.previous-page");
        if (page < lastPage) scope = scope.Shortcut(ControllerButton.RightBumper, "installed.next-page");
        var selectedBuiltInIndex = builtIn
            .Select((manifest, index) => (manifest, index))
            .Where(item => item.manifest.Id == selectedBuiltInId)
            .Select(item => item.index)
            .FirstOrDefault(-1);
        var initialFocus = selectedBuiltInIndex >= 0
            ? $"installed.builtin.item.{selectedBuiltInIndex}"
            : builtIn.Count != 0
                ? "installed.builtin.item.0"
                : visible.Length == 0 ? "installed.back" : $"installed.item.{start}";
        return View(header, scope, initialFocus, "installed.widgets");
    }

    private WidgetView RenderInstalledWidgetDetails(StackElement header, bool busy)
    {
        CatalogWidget? package;
        WidgetManifest? builtIn;
        bool valid;
        bool permissionCatalogValid;
        IReadOnlyList<string> permissionPackageIds;
        lock (_stateLock)
        {
            package = SelectedInstalledWidgetLocked();
            builtIn = SelectedBuiltInWidgetLocked();
            valid = _installedWidgetCatalogValid;
            permissionCatalogValid = _permissionCatalogValid;
            permissionPackageIds = _permissionPackages.Select(item => item.Id).ToArray();
        }
        if (builtIn is not null && valid)
        {
            var builtInHasPermissions = permissionCatalogValid &&
                permissionPackageIds.Contains(builtIn.Id, StringComparer.Ordinal);
            var builtInCompatibility = WidgetHostCompatibility.Evaluate(builtIn);
            var builtInRequiredPermissions = builtIn.Permissions.Count == 0
                ? "None"
                : string.Join(", ", builtIn.Permissions.Order(StringComparer.Ordinal));
            var builtInOptionalPermissions = builtIn.OptionalPermissions.Count == 0
                ? "None"
                : string.Join(", ", builtIn.OptionalPermissions.Order(StringComparer.Ordinal));
            return View(header,
                PageScope("installed.details",
                    UI.Text(builtIn.Name, "installed.details.heading", "Built-in widget name")
                        .Classes("page-heading"),
                    UI.Text("Source: Built-in", "installed.details.source", "Built-in widget source")
                        .Classes("diagnostic-ok"),
                    UI.Text($"ID: {builtIn.Id}", "installed.details.id", "Package ID")
                        .Classes("diagnostic-line"),
                    UI.Text($"Publisher: {builtIn.Publisher}", "installed.details.publisher", "Package publisher")
                        .Classes("diagnostic-line"),
                    UI.Text($"Version: {builtIn.Version}", "installed.details.version", "Built-in version")
                        .Classes("diagnostic-line"),
                    UI.Text($"Runtime: {builtIn.Entrypoint.Runtime}", "installed.details.runtime", "Package runtime")
                        .Classes("diagnostic-line"),
                    UI.Text($"Compatibility: {builtInCompatibility.Message}",
                        "installed.details.compatibility", "Package compatibility")
                        .Classes(builtInCompatibility.IsSupported ? "diagnostic-ok" : "diagnostic-error"),
                    UI.Text($"Required capabilities: {builtInRequiredPermissions}",
                        "installed.details.required-permissions", "Required capabilities")
                        .Classes("page-help"),
                    UI.Text($"Optional capabilities: {builtInOptionalPermissions}",
                        "installed.details.optional-permissions", "Optional capabilities")
                        .Classes("page-help"),
                    UI.Text(
                        "Included with Game Bar Alternative. Built-in widgets are updated with the app and cannot be disabled or version-managed here.",
                        "installed.details.status", "Built-in widget management status")
                        .Classes("page-help"),
                    UI.Button(
                            builtInHasPermissions
                                ? "Permissions & configuration"
                                : "No host permissions requested",
                            "installed.permissions.open", "installed.details.permissions")
                        .Disabled(!builtInHasPermissions).Busy(busy).Classes("setting-row"),
                    UI.Button("Back", "back", "installed.details.back").Classes("secondary-button")),
                builtInHasPermissions ? "installed.details.permissions" : "installed.details.back",
                "installed.details");
        }
        if (package is null || !valid)
            return View(header,
                PageScope("installed.details",
                    UI.Text("Package unavailable", "installed.details.heading", "Package unavailable")
                        .Classes("page-heading"),
                    UI.Text("Return to the installed widget list and reload Settings.",
                        "installed.details.help", "Package unavailable help").Classes("diagnostic-error"),
                    UI.Button("Back", "back", "installed.details.back").Classes("secondary-button")),
                "installed.details.back", "installed.details");

        var manifest = package.ActiveVersion.Manifest;
        var compatibility = WidgetHostCompatibility.Evaluate(manifest);
        var requiredPermissions = manifest.Permissions.Count == 0
            ? "None"
            : string.Join(", ", manifest.Permissions.Order(StringComparer.Ordinal));
        var optionalPermissions = manifest.OptionalPermissions.Count == 0
            ? "None"
            : string.Join(", ", manifest.OptionalPermissions.Order(StringComparer.Ordinal));
        var residency = WidgetResidencyPolicies.Resolve(manifest);
        var residencyDescription = residency.Mode switch
        {
            WidgetResidencyMode.KeepAlive =>
                "Keep alive · process stays resident in Background; lifecycle callbacks still pause presentation work",
            WidgetResidencyMode.SuspendWhenHidden =>
                "Suspend when hidden · cooperative Background lifecycle; no process/thread suspension",
            WidgetResidencyMode.UnloadAfterIdle =>
                $"Unload after idle · Destroying after {residency.IdleDuration?.TotalSeconds:0} seconds; last view is cached",
            _ => "Unknown",
        };
        var contentDigest = package.ActiveVersion.ContentDigest.ToLowerInvariant();
        var action = package.Enabled ? "Disable widget" : "Enable unsigned widget";
        var canToggle = package.Enabled || compatibility.IsSupported;
        var hasPermissions = permissionCatalogValid &&
            permissionPackageIds.Contains(manifest.Id, StringComparer.Ordinal);
        var versionsButton = UI.Button(
                $"Manage versions ({package.Versions.Count})", "installed.versions.open",
                "installed.details.versions")
            .Busy(busy).Classes("setting-row");
        var permissionsButton = UI.Button(
                hasPermissions
                    ? "Permissions & configuration"
                    : "No host permissions requested",
                "installed.permissions.open", "installed.details.permissions")
            .FocusUp("installed.details.versions")
            .FocusDown(canToggle ? "installed.details.toggle" : "installed.details.back")
            .Disabled(!hasPermissions).Busy(busy).Classes("setting-row");
        var actionButton = UI.Button(action, "installed.toggle", "installed.details.toggle")
            .FocusUp("installed.details.permissions")
            .FocusDown("installed.details.back").Busy(busy).Disabled(!canToggle)
            .Classes(package.Enabled ? "danger-button" : "primary-button");
        versionsButton = versionsButton.FocusDown("installed.details.permissions");
        var back = UI.Button("Back", "back", "installed.details.back")
            .FocusUp(canToggle ? "installed.details.toggle" : "installed.details.permissions")
            .Classes("secondary-button");
        return View(header,
            PageScope("installed.details",
                UI.Text(package.Name, "installed.details.heading", "Installed widget name").Classes("page-heading"),
                UI.Text("Trust: Unsigned · publisher unverified", "installed.details.trust",
                    "Unsigned package trust status").Classes("diagnostic-error"),
                UI.Text($"ID: {manifest.Id}", "installed.details.id", "Package ID").Classes("diagnostic-line"),
                UI.Text($"Declared publisher (unverified): {manifest.Publisher}",
                    "installed.details.publisher", "Unverified declared package publisher")
                    .Classes("diagnostic-line"),
                UI.Text($"Active version: {manifest.Version}", "installed.details.version", "Package version")
                    .Classes("diagnostic-line"),
                UI.Text("Sealed content SHA-256:", "installed.details.digest-label",
                    "Sealed content digest label").Classes("diagnostic-line"),
                UI.CodeText(contentDigest, "installed.details.digest", "Sealed content SHA-256 digest"),
                UI.Text($"Runtime: {manifest.Entrypoint.Runtime}", "installed.details.runtime", "Package runtime")
                    .Classes("diagnostic-line"),
                UI.Text($"Host API: {manifest.HostApi.Minimum} through major {manifest.HostApi.MaximumMajor}",
                    "installed.details.host-api", "Supported host API range").Classes("diagnostic-line"),
                UI.Text($"Architectures: {string.Join(", ", manifest.Architectures)}",
                    "installed.details.architectures", "Supported architectures").Classes("diagnostic-line"),
                UI.Text($"Residency: {residencyDescription}",
                    "installed.details.residency", "Worker residency policy").Classes("page-help"),
                UI.Text($"Compatibility: {compatibility.Message}",
                    "installed.details.compatibility", "Package compatibility")
                    .Classes(compatibility.IsSupported ? "diagnostic-ok" : "diagnostic-error"),
                UI.Text($"Required capabilities: {requiredPermissions}",
                    "installed.details.required-permissions", "Required capabilities")
                    .Classes("page-help"),
                UI.Text($"Optional capabilities: {optionalPermissions}",
                    "installed.details.optional-permissions", "Optional capabilities")
                    .Classes("page-help"),
                UI.Text(package.Enabled
                        ? compatibility.IsSupported
                            ? "This unsigned widget is available in the overlay. Its declared publisher remains unverified. Disable it to stop future activation."
                            : "This widget is marked enabled but cannot run on this host. Disable it before installing a compatible update."
                        : compatibility.IsSupported
                            ? "Enabling confirms review of these exact unsigned bytes and declared capabilities, not publisher identity. Capability access still requires separate consent."
                            : "Install a version that supports this host API and architecture before enabling. Capability consent is a separate decision.",
                    "installed.details.status", "Package enabled status").Classes("page-help"),
                versionsButton,
                permissionsButton,
                actionButton,
                back),
            canToggle ? "installed.details.toggle" : "installed.details.back", "installed.details");
    }

    private WidgetView RenderInstalledWidgetVersions(StackElement header, bool busy)
    {
        CatalogWidget? package;
        int page;
        lock (_stateLock)
        {
            package = _installedWidgetCatalogValid ? SelectedInstalledWidgetLocked() : null;
            page = _installedVersionPage;
        }
        if (package is null)
            return View(header,
                PageScope("installed.versions",
                    UI.Text("Versions unavailable", "installed.versions.heading", "Versions unavailable")
                        .Classes("page-heading"),
                    UI.Button("Back", "back", "installed.versions.back").Classes("secondary-button")),
                "installed.versions.back", "installed.versions");

        var lastPage = LastInstalledVersionPage(package);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * InstalledVersionsPerPage;
        var visible = package.Versions.Skip(start).Take(InstalledVersionsPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text($"{package.Name} versions", "installed.versions.heading", "Installed versions")
                .Classes("page-heading"),
            UI.Text(package.Enabled
                    ? "Disable this widget before changing executable versions."
                    : "Select an exact unsigned version. Selection does not enable it; review the full sealed digest and capabilities on package details before enabling.",
                "installed.versions.help", "Version selection help").Classes("page-help"),
            UI.Text($"Page {page + 1} of {lastPage + 1}", "installed.versions.page-label",
                "Installed version page").Classes("page-counter"),
        };
        for (var offset = 0; offset < visible.Length; offset++)
        {
            var index = start + offset;
            var installed = visible[offset];
            var isActive = installed.Version == package.ActiveVersion.Version;
            var compatibility = WidgetHostCompatibility.Evaluate(installed.Manifest);
            var direction = isActive
                ? "Active"
                : installed.Version < package.ActiveVersion.Version ? "Rollback" : "Select newer";
            children.Add(UI.Button(
                    $"{direction} · {installed.Version} · " +
                    (compatibility.IsSupported ? "Compatible" : "Incompatible") +
                    $" · SHA-256 {ShortContentDigest(installed.ContentDigest)}…",
                    $"installed.version.select.{index}", $"installed.version.item.{index}")
                .Disabled(package.Enabled || isActive).Busy(busy).Selected(isActive)
                .Classes("setting-row", isActive ? "is-enabled" : "is-disabled"));
        }
        children.Add(UI.Button("Back", "back", "installed.versions.back").Classes("secondary-button"));
        LinkVertical(children);

        var scope = UI.Stack("installed.versions", children.ToArray())
            .InputScope("installed.versions")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        if (page > 0)
            scope = scope.Shortcut(ControllerButton.LeftBumper, "installed.versions.previous-page");
        if (page < lastPage)
            scope = scope.Shortcut(ControllerButton.RightBumper, "installed.versions.next-page");
        var initialFocus = package.Enabled
            ? "installed.versions.back"
            : visible.Select((version, offset) => (version, offset))
                .Where(item => item.version.Version != package.ActiveVersion.Version)
                .Select(item => $"installed.version.item.{start + item.offset}")
                .FirstOrDefault() ?? "installed.versions.back";
        return View(header, scope, initialFocus, "installed.versions");
    }

    private void ChangeInstalledWidgetPage(int delta)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgets) return;
            _installedWidgetPage = Math.Clamp(
                _installedWidgetPage + delta, 0, LastInstalledWidgetPage(_installedWidgets));
        }
        Invalidate();
    }

    private void SelectInstalledWidget(int index)
    {
        lock (_stateLock)
        {
            if (!_installedWidgetCatalogValid || index < 0 || index >= _installedWidgets.Widgets.Count) return;
            _selectedInstalledWidgetId = _installedWidgets.Widgets[index].Id;
            _selectedBuiltInWidgetId = null;
            _installedVersionPage = 0;
            _page = SettingsPage.InstalledWidgetDetails;
        }
        Invalidate();
    }

    private void SelectBuiltInWidget(int index)
    {
        lock (_stateLock)
        {
            if (!_installedWidgetCatalogValid || index < 0 || index >= _builtInWidgets.Count) return;
            _selectedBuiltInWidgetId = _builtInWidgets[index].Id;
            _selectedInstalledWidgetId = null;
            _installedVersionPage = 0;
            _page = SettingsPage.InstalledWidgetDetails;
        }
        Invalidate();
    }

    private void OpenInstalledWidgetVersions()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetDetails ||
                SelectedInstalledWidgetLocked() is null ||
                SelectedBuiltInWidgetLocked() is not null)
                return;
            _page = SettingsPage.InstalledWidgetVersions;
        }
        Invalidate();
    }

    private void ChangeInstalledVersionPage(int delta)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.InstalledWidgetVersions) return;
            var selected = SelectedInstalledWidgetLocked();
            if (selected is null) return;
            _installedVersionPage = Math.Clamp(
                _installedVersionPage + delta, 0, LastInstalledVersionPage(selected));
        }
        Invalidate();
    }

    private async Task SelectInstalledVersionAsync(int index, CancellationToken cancellationToken)
    {
        CatalogWidget? selected;
        InstalledWidgetVersion? requested;
        lock (_stateLock)
        {
            selected = _installedWidgetCatalogValid ? SelectedInstalledWidgetLocked() : null;
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
                _installedWidgets = refreshed;
                _installedWidgetCatalogValid = true;
                _installedWidgetDiagnostic = null;
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
            selected = _installedWidgetCatalogValid ? SelectedInstalledWidgetLocked() : null;
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
            await _widgetCatalog.SetEnabledAsync(selected.Id, nextEnabled, cancellationToken).ConfigureAwait(false);
            var refreshed = await _widgetCatalog.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _installedWidgets = refreshed;
                _installedWidgetCatalogValid = true;
                _installedWidgetDiagnostic = null;
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
    }

    private CatalogWidget? SelectedInstalledWidgetLocked() => _installedWidgets.Widgets.FirstOrDefault(
        widget => widget.Id == _selectedInstalledWidgetId);

    private WidgetManifest? SelectedBuiltInWidgetLocked() => _builtInWidgets.FirstOrDefault(
        manifest => manifest.Id == _selectedBuiltInWidgetId);

    private static int LastInstalledWidgetPage(WidgetCatalogSnapshot snapshot) =>
        Math.Max(0, (snapshot.Widgets.Count - 1) / InstalledWidgetsPerPage);

    private static int LastInstalledVersionPage(CatalogWidget widget) =>
        Math.Max(0, (widget.Versions.Count - 1) / InstalledVersionsPerPage);

    private static string ShortContentDigest(string digest) =>
        digest.Length >= 12
            ? digest[..12].ToLowerInvariant()
            : "invalid-digest";
}
