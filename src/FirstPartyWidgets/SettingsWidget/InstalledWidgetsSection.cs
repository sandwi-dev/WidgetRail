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
            lock (_stateLock)
            {
                _installedWidgets = snapshot;
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
            _installedWidgetCatalogValid = false;
            _installedWidgetDiagnostic = diagnostic;
            _installedWidgetPage = 0;
            _selectedInstalledWidgetId = null;
            _installedVersionPage = 0;
            if (_page is SettingsPage.InstalledWidgetDetails or SettingsPage.InstalledWidgetVersions)
                _page = SettingsPage.InstalledWidgets;
        }
        return diagnostic;
    }

    private WidgetView RenderInstalledWidgets(StackElement header, bool busy)
    {
        WidgetCatalogSnapshot snapshot;
        bool valid;
        string? diagnostic;
        int page;
        lock (_stateLock)
        {
            snapshot = _installedWidgets;
            valid = _installedWidgetCatalogValid;
            diagnostic = _installedWidgetDiagnostic;
            page = _installedWidgetPage;
        }

        var lastPage = LastInstalledWidgetPage(snapshot);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * InstalledWidgetsPerPage;
        var visible = snapshot.Widgets.Skip(start).Take(InstalledWidgetsPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text("Installed widgets", "installed.heading", "Installed widgets").Classes("page-heading"),
            UI.Text(valid
                    ? "Review package identity and permissions before enabling a newly installed widget."
                    : diagnostic ?? "Installed widget catalog is unavailable.",
                "installed.help", "Installed widget help")
                .Classes(valid ? "page-help" : "diagnostic-error"),
            UI.Text($"Page {page + 1} of {lastPage + 1}", "installed.page-label", "Installed widget page")
                .Classes("page-counter"),
        };

        for (var offset = 0; offset < visible.Length; offset++)
        {
            var index = start + offset;
            var package = visible[offset];
            var compatibility = WidgetHostCompatibility.Evaluate(package.ActiveVersion.Manifest);
            var status = package.Enabled
                ? compatibility.IsSupported ? "Enabled" : "Enabled · incompatible"
                : compatibility.IsSupported ? "Disabled · review required" : "Incompatible";
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

        var scope = UI.Stack("installed.widgets", children.ToArray())
            .InputScope("installed.widgets")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        if (page > 0) scope = scope.Shortcut(ControllerButton.LeftBumper, "installed.previous-page");
        if (page < lastPage) scope = scope.Shortcut(ControllerButton.RightBumper, "installed.next-page");
        var initialFocus = visible.Length == 0 ? "installed.back" : $"installed.item.{start}";
        return View(header, scope, initialFocus, "installed.widgets");
    }

    private WidgetView RenderInstalledWidgetDetails(StackElement header, bool busy)
    {
        CatalogWidget? package;
        bool valid;
        lock (_stateLock)
        {
            package = SelectedInstalledWidgetLocked();
            valid = _installedWidgetCatalogValid;
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
        var action = package.Enabled ? "Disable widget" : "Enable reviewed widget";
        var canToggle = package.Enabled || compatibility.IsSupported;
        var versionsButton = UI.Button(
                $"Manage versions ({package.Versions.Count})", "installed.versions.open",
                "installed.details.versions")
            .Busy(busy).Classes("setting-row");
        var actionButton = UI.Button(action, "installed.toggle", "installed.details.toggle")
            .FocusUp("installed.details.versions")
            .FocusDown("installed.details.back").Busy(busy).Disabled(!canToggle)
            .Classes(package.Enabled ? "danger-button" : "primary-button");
        versionsButton = versionsButton.FocusDown(canToggle
            ? "installed.details.toggle"
            : "installed.details.back");
        var back = UI.Button("Back", "back", "installed.details.back")
            .FocusUp(canToggle ? "installed.details.toggle" : "installed.details.versions")
            .Classes("secondary-button");
        return View(header,
            PageScope("installed.details",
                UI.Text(package.Name, "installed.details.heading", "Installed widget name").Classes("page-heading"),
                UI.Text($"ID: {manifest.Id}", "installed.details.id", "Package ID").Classes("diagnostic-line"),
                UI.Text($"Publisher: {manifest.Publisher}", "installed.details.publisher", "Package publisher")
                    .Classes("diagnostic-line"),
                UI.Text($"Active version: {manifest.Version}", "installed.details.version", "Package version")
                    .Classes("diagnostic-line"),
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
                            ? "This widget is available in the overlay. Disable it to stop future activation."
                            : "This widget is marked enabled but cannot run on this host. Disable it before installing a compatible update."
                        : compatibility.IsSupported
                            ? "Enabling confirms that you reviewed this identity and its declared capabilities. Capability access still requires separate consent."
                            : "Install a version that supports this host API and architecture before enabling. Capability consent is a separate decision.",
                    "installed.details.status", "Package enabled status").Classes("page-help"),
                versionsButton,
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
                    : "Select an exact reviewed version. Selection does not enable the widget.",
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
                    (compatibility.IsSupported ? "Compatible" : "Incompatible"),
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
            _installedVersionPage = 0;
            _page = SettingsPage.InstalledWidgetDetails;
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
                    $"{selected.Name} {requested.Version} selected; review before enabling";
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

    private static int LastInstalledWidgetPage(WidgetCatalogSnapshot snapshot) =>
        Math.Max(0, (snapshot.Widgets.Count - 1) / InstalledWidgetsPerPage);

    private static int LastInstalledVersionPage(CatalogWidget widget) =>
        Math.Max(0, (widget.Versions.Count - 1) / InstalledVersionsPerPage);
}
