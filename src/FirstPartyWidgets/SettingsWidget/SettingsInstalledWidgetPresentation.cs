using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformSettings;

namespace WidgetRail.FirstPartyWidgets.Settings;

/// <summary>Pure snapshot-only composition for installed-widget Settings pages.</summary>
internal static class SettingsInstalledWidgetPresentation
{
    public static WidgetView RenderInstalledWidgets(
        StackElement header,
        bool busy,
        SettingsInstalledWidgetState state,
        PlatformSettingsDocument? settings = null)
    {
        settings ??= PlatformSettingsDocument.Default;
        var snapshot = state.Catalog;
        var builtIn = state.BuiltIns;
        var valid = state.CatalogValid;
        var diagnostic = state.Diagnostic;
        var page = state.Page;
        var selectedBuiltInId = state.SelectedBuiltInId;

        if (!valid && snapshot.Widgets.Count == 0) return RenderInstalledCatalogRecoveryList(
            header, busy, diagnostic, state);

        var lastPage = SettingsInstalledWidgetPolicy.LastCatalogPage(snapshot);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * SettingsWidget.InstalledWidgetsPerPage;
        var visible = snapshot.Widgets.Skip(start).Take(SettingsWidget.InstalledWidgetsPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text("Widgets", "installed.heading", "Widgets").Classes("page-heading"),
            UI.Text(valid
                    ? "Built-in widgets come with WidgetRail. Only install community widgets from sources you trust. WidgetRail has not verified who made them."
                    : $"{diagnostic ?? "Installed widget catalog is unavailable."} Last-good installed versions remain available for review; retry after the catalog changes.",
                "installed.help", "Installed widget help")
                .Classes(valid ? "page-help" : "diagnostic-error"),
            UI.Button(
                    "Install local widget",
                    "host.install-local-widget",
                    "installed.install-local")
                .Disabled(!valid).Busy(busy)
                .Classes("setting-row", "primary-button"),
            UI.Text(
                    "Choose a .wrwidget file from your PC. New widgets stay off until you review their permissions and turn them on.",
                    "installed.install-local.help",
                    "Local widget installation safety")
                .Classes("page-help"),
        };
        children.Add(UI.Button(valid ? "Refresh widgets" : "Retry", "refresh", "installed.retry")
            .Busy(busy).Classes("setting-row", "secondary-button"));

        if (builtIn.Count != 0)
        {
            children.Add(UI.Text("BUILT IN", "installed.builtin.heading", "Built-in widgets")
                .Classes("section-heading"));
            for (var index = 0; index < builtIn.Count; index++)
            {
                var manifest = builtIn[index];
                children.Add(UI.Button(
                        $"{manifest.Name} · {manifest.Version} · {(settings.BuiltInWidgets.IsEnabled(manifest.Id) ? "Enabled" : "Disabled")}",
                        $"installed.builtin.select.{index}", $"installed.builtin.item.{index}")
                    .Disabled(!valid).Busy(busy)
                    .Selected(settings.BuiltInWidgets.IsEnabled(manifest.Id))
                    .Classes("setting-row", "is-built-in", settings.BuiltInWidgets.IsEnabled(manifest.Id) ? "is-enabled" : "is-disabled"));
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
            var superseded = builtIn.Any(manifest => manifest.Id == package.Id);
            var status = superseded ? "Unused copy · built-in version takes priority" : package.Enabled
                ? compatibility.IsSupported ? "Enabled" : "Enabled · incompatible"
                : compatibility.IsSupported ? "Disabled · review before enabling" : "Incompatible";
            children.Add(UI.Button(
                    $"{package.Name} · {package.ActiveVersion.Version} · " +
                    (valid ? status : $"Last good · {status}"),
                    $"installed.select.{index}", $"installed.item.{index}")
                .Busy(busy)
                .Selected(package.Enabled && !superseded)
                .Classes("setting-row", package.Enabled && !superseded ? "is-enabled" : "is-disabled"));
        }

        if (visible.Length == 0)
            children.Add(UI.Text(valid ? "No community widgets are installed." : "No package actions are available.",
                "installed.empty", "Installed widget list status").Classes("diagnostic-line"));
        if (page > 0)
            children.Add(UI.Button("Previous page", "installed.previous-page", "installed.previous-page")
                .Classes("secondary-button"));
        if (page < lastPage)
            children.Add(UI.Button("Next page", "installed.next-page", "installed.next-page")
                .Classes("secondary-button"));
        children.Add(UI.Button("Back", "back", "installed.back").Classes("secondary-button"));
        SettingsPresentation.LinkVertical(children);

        var scope = UI.VerticalScroll("installed.widgets", children.ToArray())
            .InputScope("installed.widgets")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        var selectedBuiltInIndex = builtIn
            .Select((manifest, index) => (manifest, index))
            .Where(item => item.manifest.Id == selectedBuiltInId)
            .Select(item => item.index)
            .FirstOrDefault(-1);
        var initialFocus = selectedBuiltInIndex >= 0
            ? $"installed.builtin.item.{selectedBuiltInIndex}"
            : builtIn.Count != 0
                ? "installed.builtin.item.0"
                : visible.Length == 0 ? "installed.install-local" : $"installed.item.{start}";
        return SettingsPresentation.View(header, scope, initialFocus, "installed.widgets");
    }

    public static WidgetView RenderInstalledCatalogRecoveryList(
        StackElement header,
        bool busy,
        string? diagnostic,
        SettingsInstalledWidgetState state)
    {
        var health = state.Health;
        var page = state.Page;
        var removable = health.Candidates.Where(item => item.CanRemove).ToArray();
        var lastPage = Math.Max(0, (removable.Length - 1) / SettingsWidget.InstalledWidgetsPerPage);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * SettingsWidget.InstalledWidgetsPerPage;
        var visible = removable.Skip(start).Take(SettingsWidget.InstalledWidgetsPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text("Catalog recovery", "installed.repair.heading", "Installed widget catalog recovery")
                .Classes("page-heading"),
            UI.Text(
                diagnostic ?? "Installed widget catalog is unavailable.",
                "installed.repair.failure", "Catalog failure")
                .Classes("diagnostic-error"),
            UI.Text(
                "Recovery reads only canonical package and version directory names. Package manifests and executable content are not trusted. Only inactive, non-selected versions can be retired, even when the selected version is enabled.",
                "installed.repair.help", "Catalog recovery safety")
                .Classes("page-help"),
        };
        if (lastPage != 0)
            children.Add(UI.Text($"Page {page + 1} of {lastPage + 1}",
                "installed.repair.page-label", "Catalog recovery page").Classes("page-counter"));
        for (var offset = 0; offset < visible.Length; offset++)
        {
            var candidate = visible[offset];
            children.Add(UI.Button(
                    $"Review inactive version · {candidate.Id} · {candidate.Version}",
                    $"installed.repair.select.{start + offset}",
                    $"installed.repair.item.{start + offset}")
                .Busy(busy).Classes("danger-button"));
        }
        if (visible.Length == 0)
            children.Add(UI.Text(
                "No safely removable inactive versions are available. Use diagnostics or reinstall the selected version.",
                "installed.repair.empty", "No safe catalog repair candidates")
                .Classes("diagnostic-line"));
        if (page > 0)
            children.Add(UI.Button("Previous page", "installed.previous-page", "installed.repair.previous-page")
                .Classes("secondary-button"));
        if (page < lastPage)
            children.Add(UI.Button("Next page", "installed.next-page", "installed.repair.next-page")
                .Classes("secondary-button"));
        children.Add(UI.Button("Back", "back", "installed.repair.back")
            .Classes("secondary-button"));
        SettingsPresentation.LinkVertical(children);
        var scope = UI.VerticalScroll("installed.repair.list", children.ToArray())
            .InputScope("installed.repair.list")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        return SettingsPresentation.View(
            header, scope,
            visible.Length == 0 ? "installed.repair.back" : $"installed.repair.item.{start}",
            "installed.repair.list");
    }

    public static WidgetView RenderInstalledWidgetRecovery(
        StackElement header,
        bool busy,
        SettingsInstalledWidgetState state)
    {
        var candidate = state.SelectedRepair;
        if (candidate is null || !candidate.CanRemove)
            return SettingsPresentation.View(header,
                SettingsPresentation.PageScope("installed.repair.confirm",
                    UI.Text("Recovery candidate unavailable", "installed.repair.confirm.heading",
                        "Recovery candidate unavailable").Classes("page-heading"),
                    UI.Button("Back", "back", "installed.repair.confirm.back")
                        .Classes("secondary-button")),
                "installed.repair.confirm.back", "installed.repair.confirm");
        return SettingsPresentation.View(header,
            SettingsPresentation.PageScope("installed.repair.confirm",
                UI.Text("Remove inactive version?", "installed.repair.confirm.heading",
                    "Confirm inactive version removal").Classes("page-heading"),
                UI.Text($"Package ID: {candidate.Id}", "installed.repair.confirm.id", "Package ID")
                    .Classes("diagnostic-line"),
                UI.Text($"Version: {candidate.Version}", "installed.repair.confirm.version", "Version")
                    .Classes("diagnostic-line"),
                UI.Text(
                    "This retires only the named inactive, non-selected version. The selected version remains protected even when enabled. Recovery does not execute or trust package content and cannot be undone.",
                    "installed.repair.confirm.help", "Recovery confirmation")
                    .Classes("page-help"),
                UI.Button("Remove inactive version", "installed.repair.remove",
                    "installed.repair.confirm.remove").Busy(busy).Classes("danger-button"),
                UI.Button("Cancel", "back", "installed.repair.confirm.back")
                    .Classes("secondary-button")),
            "installed.repair.confirm.back", "installed.repair.confirm");
    }

    public static WidgetView RenderInstalledWidgetDetails(
        StackElement header,
        bool busy,
        SettingsInstalledWidgetState state,
        SettingsPermissionState permissionState,
        PlatformSettingsDocument settings)
    {
        var package = state.SelectedInstalled;
        var builtIn = state.SelectedBuiltIn;
        var valid = state.CatalogValid;
        var permissionCatalogValid = permissionState.Projection.CatalogValid;
        var permissionPackageIds = permissionState.Projection.Packages
            .Select(item => item.Id)
            .ToArray();
        static ButtonElement EnabledActionButton(string label, string actionId, bool enabled, bool canToggle, bool busy) =>
            UI.Button(label, actionId, "installed.details.toggle")
                .Disabled(!canToggle).Busy(busy).Classes(enabled ? "danger-button" : "primary-button");
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
            var enabled = settings.BuiltInWidgets.IsEnabled(builtIn.Id);
            var builtInControls = new List<WidgetElement>
            {
                UI.Button(builtInHasPermissions ? "Permissions & configuration" : "No host permissions requested",
                        "installed.permissions.open", "installed.details.permissions")
                    .Disabled(!builtInHasPermissions).Busy(busy).Classes("setting-row"),
                EnabledActionButton(enabled ? "Disable widget" : "Enable widget", "installed.builtin.toggle",
                    enabled, builtIn.Id != BuiltInWidgetSettings.SettingsWidgetId, busy),
                LocalDataButton(state, busy),
                UI.Button("Back", "back", "installed.details.back").Classes("secondary-button"),
            };
            SettingsPresentation.LinkVertical(builtInControls);
            return SettingsPresentation.View(header,
                SettingsPresentation.PageScope("installed.details", [
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
                        builtIn.Id == BuiltInWidgetSettings.SettingsWidgetId ? "Settings stays enabled so you can manage WidgetRail." :
                            settings.BuiltInWidgets.IsEnabled(builtIn.Id) ? "Enabled · Included and updated with WidgetRail." : "Disabled · Enable this widget to show it in the overlay.",
                        "installed.details.status", "Built-in widget management status")
                        .Classes("page-help"),
                    .. builtInControls]),
                builtInHasPermissions ? "installed.details.permissions" :
                    builtIn.Id != BuiltInWidgetSettings.SettingsWidgetId ? "installed.details.toggle" : "installed.details.back",
                "installed.details");
        }
        if (package is null)
            return SettingsPresentation.View(header,
                SettingsPresentation.PageScope("installed.details",
                    UI.Text("Package unavailable", "installed.details.heading", "Package unavailable")
                        .Classes("page-heading"),
                    UI.Text("Return to the installed widget list and reload Settings.",
                        "installed.details.help", "Package unavailable help").Classes("diagnostic-error"),
                    UI.Button("Back", "back", "installed.details.back").Classes("secondary-button")),
                "installed.details.back", "installed.details");

        if (state.SelectedInstalledBuiltIn is not null)
            return RenderUnusedInstalledCopy(header, busy, state);

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
        var fullTrust = WidgetManifestTrust.Resolve(manifest) ==
            WidgetExecutionTrust.FullTrustCurrentUser;
        var action = package.Enabled
            ? "Disable widget"
            : fullTrust ? "Enable full-trust application" : "Enable unsigned widget";
        var canToggle = package.Enabled || compatibility.IsSupported;
        var canToggleNow = valid && canToggle;
        var hasPermissions = permissionCatalogValid &&
            permissionPackageIds.Contains(manifest.Id, StringComparer.Ordinal);
        var versionsButton = UI.Button(
                $"Manage versions ({package.Versions.Count})", "installed.versions.open",
                "installed.details.versions")
            .Disabled(!valid).Busy(busy).Classes("setting-row");
        var permissionsButton = UI.Button(
                hasPermissions
                    ? "Permissions & configuration"
                    : "No host permissions requested",
                "installed.permissions.open", "installed.details.permissions")
            .FocusUp("installed.details.versions")
            .FocusDown(canToggleNow ? "installed.details.toggle" : "installed.details.back")
            .Disabled(!valid || !hasPermissions).Busy(busy).Classes("setting-row");
        var actionButton = EnabledActionButton(action, "installed.toggle", package.Enabled, canToggleNow, busy)
            .FocusUp("installed.details.permissions")
            .FocusDown("installed.details.local-data");
        var canUninstall = valid;
        var localDataButton = LocalDataButton(state, busy)
            .Disabled(!valid)
            .FocusUp(canToggleNow ? "installed.details.toggle" : "installed.details.permissions")
            .FocusDown(canUninstall ? SettingsInstalledWidgetUninstallPolicy.FocusId :
                "installed.details.back");
        var uninstallButton = UI.Button(
                "Uninstall widget", "installed.uninstall.open",
                SettingsInstalledWidgetUninstallPolicy.FocusId)
            .FocusUp("installed.details.local-data")
            .FocusDown("installed.details.back")
            .Disabled(!valid).Busy(busy).Classes("danger-button");
        versionsButton = versionsButton.FocusDown("installed.details.permissions");
        var back = UI.Button("Back", "back", "installed.details.back")
            .FocusUp(canUninstall ? SettingsInstalledWidgetUninstallPolicy.FocusId :
                "installed.details.local-data")
            .Classes("secondary-button");
        var controls = new List<WidgetElement>
        {
            UI.Button("Update from file", "installed.update.open", "installed.details.update").Disabled(!valid).Busy(busy).Classes("primary-button"),
            versionsButton, permissionsButton,
            actionButton, localDataButton,
        };
        controls.Add(uninstallButton);
        controls.Add(UI.Button("Refresh details", "refresh", "installed.details.refresh").Busy(busy).Classes("secondary-button"));
        controls.Add(back);
        SettingsPresentation.LinkVertical(controls);
        var details = new List<WidgetElement>
        {
                UI.Text(package.Name, "installed.details.heading", "Installed widget name").Classes("page-heading"),
                UI.Text(valid
                        ? "Installed catalog is current."
                        : "Installed catalog refresh is required. This last-good package remains reviewable, but package mutations are disabled until Retry succeeds.",
                    "installed.details.catalog-status", "Installed catalog status")
                    .Classes(valid ? "diagnostic-ok" : "diagnostic-error"),
                UI.Text(fullTrust
                        ? "Trust: Full trust · ordinary current-user process · not AppContainer sandboxed"
                        : "Trust: Unsigned · publisher unverified", "installed.details.trust",
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
                            ? fullTrust
                                ? "Enabling explicitly approves these exact unsigned bytes to use ordinary current-user authority, including files, network, registry, databases, and child processes. No AppContainer or broker capability boundary applies."
                                : "Enabling confirms review of these exact unsigned bytes and declared capabilities, not publisher identity. Capability access still requires separate consent."
                            : "Install a version that supports this host API and architecture before enabling. Capability consent is a separate decision.",
                    "installed.details.status", "Package enabled status").Classes("page-help"),
        };
        details.AddRange(controls);
        return SettingsPresentation.View(header,
            SettingsPresentation.PageScope("installed.details", details.ToArray()),
            canUninstall && state.DetailsFocusId == SettingsInstalledWidgetUninstallPolicy.FocusId
                ? SettingsInstalledWidgetUninstallPolicy.FocusId
                : canToggleNow ? "installed.details.toggle" : "installed.details.back",
            "installed.details");
    }

    private static WidgetView RenderUnusedInstalledCopy(
        StackElement header, bool busy, SettingsInstalledWidgetState state)
    {
        var package = state.SelectedInstalled!;
        var controls = new List<WidgetElement>
        {
            UI.Button("Manage built-in version", "installed.builtin.open", "installed.details.builtin")
                .Disabled(!state.CatalogValid).Busy(busy).Classes("primary-button"),
        };
        if (package.Enabled)
            controls.Add(UI.Button("Disable unused copy", "installed.toggle", "installed.details.toggle")
                .Disabled(!state.CatalogValid).Busy(busy).Classes("danger-button"));
        controls.Add(LocalDataButton(state, busy));
        controls.Add(UI.Button($"Manage versions ({package.Versions.Count})", "installed.versions.open", "installed.details.versions")
            .Disabled(!state.CatalogValid).Busy(busy).Classes("setting-row"));
        controls.Add(UI.Button("Uninstall unused copy", "installed.uninstall.open", SettingsInstalledWidgetUninstallPolicy.FocusId)
            .Disabled(!state.CatalogValid).Busy(busy).Classes("danger-button"));
        controls.Add(UI.Button("Back", "back", "installed.details.back").Classes("secondary-button"));
        SettingsPresentation.LinkVertical(controls);
        return SettingsPresentation.View(header,
            SettingsPresentation.PageScope("installed.details", [
                UI.Text(package.Name, "installed.details.heading", "Widget name").Classes("page-heading"),
                UI.Text("Unused installed copy", "installed.details.source", "Widget source").Classes("section-heading"),
                UI.Text("WidgetRail includes this widget. The built-in version takes priority, even when it is disabled. Use Manage built-in version to turn it on or change its settings.",
                    "installed.details.status", "Widget copy status").Classes("page-help"),
                UI.Text($"Installed copy: {package.ActiveVersion.Version} · Built-in version: {state.SelectedInstalledBuiltIn!.Version}",
                    "installed.details.version", "Widget versions").Classes("diagnostic-line"),
                UI.Text("You can remove this extra copy without removing the built-in widget. Disable the extra copy first to manage its stored data or uninstall it.",
                    "installed.details.cleanup", "Unused copy cleanup").Classes("page-help"),
                .. controls]),
            state.DetailsFocusId == SettingsInstalledWidgetUninstallPolicy.FocusId
                ? SettingsInstalledWidgetUninstallPolicy.FocusId : "installed.details.builtin",
            "installed.details");
    }

    public static WidgetView RenderInstalledWidgetUninstall(
        StackElement header,
        bool busy,
        SettingsInstalledWidgetState state)
    {
        var package = state.SelectedInstalled;
        var inspection = state.PackageUninstall;
        if (!state.CatalogValid || package is null || inspection is null ||
            !(package.Enabled && inspection.StatusCode == "widget_enabled" || inspection is { CanUninstall: true, ConfirmationToken: not null }) ||
            !SettingsInstalledWidgetUninstallPolicy.Matches(package, inspection))
            return SettingsPresentation.View(header,
                SettingsPresentation.PageScope("installed.uninstall.confirm",
                    UI.Text("Package changed", "installed.uninstall.heading",
                        "Package changed").Classes("page-heading"),
                    UI.Text("Return to details and inspect the current installed package again.",
                        "installed.uninstall.help", "Package changed help")
                        .Classes("diagnostic-error"),
                    UI.Button("Back", "installed.uninstall.cancel",
                        "installed.uninstall.cancel").Classes("secondary-button")),
                "installed.uninstall.cancel", "installed.uninstall.confirm");

        return SettingsPresentation.View(header,
            SettingsPresentation.PageScope("installed.uninstall.confirm",
                UI.Text("Uninstall widget?", "installed.uninstall.heading",
                    "Confirm widget uninstall").Classes("page-heading"),
                UI.Text(inspection.DisplayName, "installed.uninstall.name",
                    "Selected widget").Classes("diagnostic-line"),
                UI.Text($"Package ID: {inspection.WidgetId}", "installed.uninstall.id",
                    "Package ID").Classes("diagnostic-line"),
                UI.Text($"Publisher authority: {inspection.PublisherId}",
                    "installed.uninstall.publisher", "Package publisher authority")
                    .Classes("diagnostic-line"),
                UI.Text($"Active version: {inspection.ActiveVersion}; installed versions: {inspection.VersionCount}",
                    "installed.uninstall.version", "Installed package versions")
                    .Classes("diagnostic-line"),
                UI.Text(
                    "This removes the widget and all its installed versions. Your saved data and sign-ins stay. Use Clear local data separately if you want to remove them.",
                    "installed.uninstall.help", "Widget uninstall scope")
                    .Classes("page-help"),
                UI.Button(package.Enabled ? "Disable and uninstall" : "Uninstall widget", "installed.uninstall.confirm",
                    "installed.uninstall.action").Busy(busy).Classes("danger-button"),
                UI.Button("Cancel", "installed.uninstall.cancel",
                    "installed.uninstall.cancel").Classes("secondary-button")),
            "installed.uninstall.cancel", "installed.uninstall.confirm");
    }

    public static WidgetView RenderInstalledWidgetLocalData(
        StackElement header,
        bool busy,
        SettingsInstalledWidgetState state)
    {
        var name = state.SelectedInstalled?.Name ?? state.SelectedBuiltIn?.Name;
        var data = state.LocalData;
        if (name is null || data is not { Exists: true, ConfirmationToken: not null })
            return SettingsPresentation.View(header,
                SettingsPresentation.PageScope("installed.local-data.confirm",
                    UI.Text("Local data changed", "installed.local-data.heading",
                        "Local data changed").Classes("page-heading"),
                    UI.Text("Return to details and inspect the current widget state again.",
                        "installed.local-data.help", "Local data changed help")
                        .Classes("diagnostic-error"),
                    UI.Button("Back", "back", "installed.local-data.back")
                        .Classes("secondary-button")),
                "installed.local-data.back", "installed.local-data.confirm");
        return SettingsPresentation.View(header,
            SettingsPresentation.PageScope("installed.local-data.confirm",
                UI.Text("Clear local data?", "installed.local-data.heading",
                    "Confirm local data clear").Classes("page-heading"),
                UI.Text(name, "installed.local-data.widget", "Selected widget")
                    .Classes("diagnostic-line"),
                UI.Text(
                    "This stops the selected widget, clears only its overlay-owned private state, then starts a fresh worker generation. It does not remove packages, credentials, provider data, themes, settings, or user files.",
                    "installed.local-data.help", "Local data clear scope")
                    .Classes("page-help"),
                UI.Button("Clear local data", "installed.local-data.clear",
                    "installed.local-data.clear").Busy(busy).Classes("danger-button"),
                UI.Button("Cancel", "back", "installed.local-data.back")
                    .Classes("secondary-button")),
            "installed.local-data.back", "installed.local-data.confirm");
    }

    private static ButtonElement LocalDataButton(
        SettingsInstalledWidgetState state,
        bool busy)
    {
        var data = state.LocalData;
        var label = data switch
        {
            { Exists: true, ConfirmationToken: not null } => "Clear local data",
            { StatusCode: "no_local_data" } => "No local data stored",
            { StatusCode: "unused_copy_enabled" } => "Local data · disable this unused copy first",
            null => "Checking local data…",
            _ => $"Local data unavailable ({data.StatusCode})",
        };
        return UI.Button(label, "installed.local-data.open", "installed.details.local-data")
            .Disabled(data is not { Exists: true, ConfirmationToken: not null })
            .Busy(busy).Classes(data is { Exists: true } ? "danger-button" : "setting-row");
    }

    public static WidgetView RenderInstalledWidgetVersions(
        StackElement header,
        bool busy,
        SettingsInstalledWidgetState state)
    {
        var package = state.CatalogValid ? state.SelectedInstalled : null;
        var page = state.VersionPage;
        if (package is null)
            return SettingsPresentation.View(header,
                SettingsPresentation.PageScope("installed.versions",
                    UI.Text("Versions unavailable", "installed.versions.heading", "Versions unavailable")
                        .Classes("page-heading"),
                    UI.Button("Back", "back", "installed.versions.back").Classes("secondary-button")),
                "installed.versions.back", "installed.versions");

        var lastPage = SettingsInstalledWidgetPolicy.LastVersionPage(package);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * SettingsWidget.InstalledVersionsPerPage;
        var visible = package.Versions.Skip(start).Take(SettingsWidget.InstalledVersionsPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text($"{package.Name} versions", "installed.versions.heading", "Installed versions")
                .Classes("page-heading"),
            UI.Text(package.Enabled
                    ? "Disable this widget to switch versions. You can still remove versions you are not using."
                    : "Choose a version to use, or remove one you no longer need. Your current version cannot be removed.",
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
            if (!isActive)
                children.Add(UI.Button($"Remove version {installed.Version}", $"installed.version.remove.{index}",
                    $"installed.version.remove.{index}").Busy(busy).Classes("danger-button"));
        }
        if (page > 0)
            children.Add(UI.Button("Previous page", "installed.versions.previous-page", "installed.versions.previous-page")
                .Classes("secondary-button"));
        if (page < lastPage)
            children.Add(UI.Button("Next page", "installed.versions.next-page", "installed.versions.next-page")
                .Classes("secondary-button"));
        children.Add(UI.Button("Back", "back", "installed.versions.back").Classes("secondary-button"));
        SettingsPresentation.LinkVertical(children);

        var scope = UI.VerticalScroll("installed.versions", children.ToArray())
            .InputScope("installed.versions")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        var initialFocus = package.Enabled
            ? visible.Select((version, offset) => (version, offset)).Where(item => item.version.Version != package.ActiveVersion.Version)
                .Select(item => $"installed.version.remove.{start + item.offset}").FirstOrDefault() ?? "installed.versions.back"
            : visible.Select((version, offset) => (version, offset))
                .Where(item => item.version.Version != package.ActiveVersion.Version)
                .Select(item => $"installed.version.item.{start + item.offset}")
                .FirstOrDefault() ?? "installed.versions.back";
        if (state.VersionFocusId is { } remembered && children.OfType<ButtonElement>().Any(button => button.Id == remembered && button.IsDisabled != true)) initialFocus = remembered;
        return SettingsPresentation.View(header, scope, initialFocus, "installed.versions");
    }
}
