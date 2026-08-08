using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using System.Text.Json;

namespace GameBarAlternative.FirstPartyWidgets.Settings;

public sealed partial class SettingsWidget
{
    public const int PermissionPackagesPerPage = 5;
    public const int CapabilitiesPerPage = 4;
    private const int MaximumPermissionPackages = 256;
    private const int MaximumBundledDirectories = 64;
    private const int MaximumManifestBytes = 1024 * 1024;

    private sealed record DeclaredCapability(string Id, bool IsRequired);
    private sealed record PermissionPackage(
        string Id,
        string Publisher,
        string AuthorityPublisher,
        string Name,
        IReadOnlyList<DeclaredCapability> Capabilities);

    private IReadOnlyList<PermissionPackage> _permissionPackages = [];
    private ConsentDocument _consent = ConsentDocument.Empty;
    private int _permissionPackagePage;
    private int _capabilityPage;
    private string? _selectedPackageId;
    private string? _selectedPublisherId;
    private string? _selectedCapabilityId;
    private bool _permissionCatalogValid = true;
    private bool _consentValid = true;
    private string? _permissionDiagnostic;
    private int _unknownDeclarations;
    private int _hiddenConsentEntries;

    private async Task<string?> ReloadPermissionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionPackage> packages = [];
        var catalogValid = true;
        string? catalogDiagnostic = null;
        var unknownDeclarations = 0;
        try
        {
            var catalog = await _widgetCatalog.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            var discovered = new Dictionary<string, PermissionPackage>(StringComparer.Ordinal);
            if (_bundledWidgetRoot is not null)
            {
                foreach (var manifest in DiscoverBundledManifests(_bundledWidgetRoot))
                {
                    var package = CreatePermissionPackage(
                        manifest, installed: false, ref unknownDeclarations);
                    if (package.Capabilities.Count != 0)
                        discovered.TryAdd(package.Id, package);
                }
            }
            foreach (var widget in catalog.Widgets.Take(MaximumPermissionPackages))
            {
                var manifest = widget.ActiveVersion.Manifest;
                var package = CreatePermissionPackage(
                    manifest, installed: true, ref unknownDeclarations);
                if (package.Capabilities.Count != 0)
                    discovered.TryAdd(package.Id, package);
            }
            packages = discovered.Values
                .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Id, StringComparer.Ordinal)
                .Take(MaximumPermissionPackages)
                .ToArray();
            if (catalog.Widgets.Count > MaximumPermissionPackages ||
                discovered.Count > MaximumPermissionPackages)
                catalogDiagnostic = $"Installed package list is limited to {MaximumPermissionPackages} entries";
        }
        catch (WidgetPackageException exception)
        {
            catalogValid = false;
            catalogDiagnostic = $"Catalog unavailable ({exception.Code})";
        }
        catch (IOException)
        {
            catalogValid = false;
            catalogDiagnostic = "Catalog unavailable (io_error)";
        }
        catch (UnauthorizedAccessException)
        {
            catalogValid = false;
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

        var declaredKeys = packages
            .SelectMany(package => package.Capabilities.Select(capability =>
                ConsentKey(package.Id, package.AuthorityPublisher, capability.Id)))
            .ToHashSet(StringComparer.Ordinal);
        var hiddenConsentEntries = consent.Entries.Count(entry =>
            !declaredKeys.Contains(ConsentKey(
                entry.PackageId, entry.PublisherId, entry.CapabilityId)));
        var diagnostic = string.Join("; ", new[] { catalogDiagnostic, consentDiagnostic }
            .Where(value => value is not null));
        lock (_stateLock)
        {
            _permissionPackages = packages;
            _consent = consent;
            _permissionCatalogValid = catalogValid;
            _consentValid = consentValid;
            _permissionDiagnostic = diagnostic.Length == 0 ? null : diagnostic;
            _unknownDeclarations = unknownDeclarations;
            _hiddenConsentEntries = hiddenConsentEntries;
            _permissionPackagePage = Math.Clamp(
                _permissionPackagePage, 0, LastPage(packages.Count, PermissionPackagesPerPage));
            var selected = SelectedPackageLocked();
            if (selected is null)
            {
                _selectedPackageId = null;
                _selectedPublisherId = null;
                _selectedCapabilityId = null;
                _capabilityPage = 0;
                if (_page is SettingsPage.PackageCapabilities or SettingsPage.CapabilityDecision)
                    _page = SettingsPage.Permissions;
            }
            else
            {
                _capabilityPage = Math.Clamp(_capabilityPage, 0,
                    LastPage(selected.Capabilities.Count, CapabilitiesPerPage));
                if (_selectedCapabilityId is not null &&
                    !selected.Capabilities.Any(capability => capability.Id == _selectedCapabilityId))
                {
                    _selectedCapabilityId = null;
                    if (_page == SettingsPage.CapabilityDecision)
                        _page = SettingsPage.PackageCapabilities;
                }
            }
        }
        return !catalogValid || !consentValid ? diagnostic : null;
    }

    private static PermissionPackage CreatePermissionPackage(
        WidgetManifest manifest,
        bool installed,
        ref int unknownDeclarations)
    {
        var capabilities = new List<DeclaredCapability>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in manifest.Permissions)
        {
            if (PlatformCapabilities.TryGet(id, out _) && seen.Add(id))
                capabilities.Add(new(id, IsRequired: true));
            else if (!PlatformCapabilities.TryGet(id, out _))
                unknownDeclarations++;
        }
        foreach (var id in manifest.OptionalPermissions)
        {
            if (PlatformCapabilities.TryGet(id, out _) && seen.Add(id))
                capabilities.Add(new(id, IsRequired: false));
            else if (!PlatformCapabilities.TryGet(id, out _))
                unknownDeclarations++;
        }
        return new PermissionPackage(
            manifest.Id,
            manifest.Publisher,
            installed
                ? InstalledWidgetAuthority.PublisherId(manifest)
                : manifest.Publisher,
            manifest.Name,
            capabilities.OrderByDescending(capability => capability.IsRequired)
                .ThenBy(capability => capability.Id, StringComparer.Ordinal)
                .ToArray());
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

    private WidgetView RenderPermissionPackages(StackElement header, bool busy)
    {
        IReadOnlyList<PermissionPackage> packages;
        int page;
        bool catalogValid;
        string? diagnostic;
        int unknown;
        int hidden;
        lock (_stateLock)
        {
            packages = _permissionPackages;
            page = _permissionPackagePage;
            catalogValid = _permissionCatalogValid;
            diagnostic = _permissionDiagnostic;
            unknown = _unknownDeclarations;
            hidden = _hiddenConsentEntries;
        }
        var lastPage = LastPage(packages.Count, PermissionPackagesPerPage);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * PermissionPackagesPerPage;
        var visible = packages.Skip(start).Take(PermissionPackagesPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text("Widget access", "permissions.heading", "Widget permissions and capabilities")
                .Classes("page-heading"),
            UI.Text(catalogValid
                    ? "Widget authors request required or optional access in their manifest. " +
                      "A request is not permission: you allow or block it here, and the host enforces your choice."
                    : diagnostic ?? "Installed package catalog is unavailable.",
                "permissions.help", "Permissions help")
                .Classes(catalogValid ? "page-help" : "diagnostic-error"),
            UI.Text(catalogValid
                    ? "Windows may separately require a system permission, such as location for nearby Wi-Fi."
                    : "Permission controls are unavailable until the package catalog is valid.",
                "permissions.system-help", "Windows permission help")
                .Classes(catalogValid ? "page-help" : "diagnostic-error"),
            UI.Text($"Page {page + 1} of {lastPage + 1}", "permissions.page-label", "Package page")
                .Classes("page-counter"),
        };
        if (unknown != 0 || hidden != 0)
        {
            children.Add(UI.Text(
                $"Hidden: {unknown} unknown declarations, {hidden} undeclared or unavailable decisions",
                "permissions.hidden", "Hidden permission diagnostics").Classes("diagnostic-line"));
        }
        for (var offset = 0; offset < visible.Length; offset++)
        {
            var index = start + offset;
            var package = visible[offset];
            var required = package.Capabilities.Count(capability => capability.IsRequired);
            var optional = package.Capabilities.Count - required;
            var label = $"{package.Name} · {required} required · {optional} optional";
            var button = UI.Button(label, $"permission.select.{index}", $"permission.item.{index}")
                .Disabled(!catalogValid).Busy(busy).Classes("setting-row");
            if (offset > 0) button = button.FocusUp($"permission.item.{index - 1}");
            if (offset + 1 < visible.Length) button = button.FocusDown($"permission.item.{index + 1}");
            children.Add(button);
        }
        children.Add(UI.Button("Back", "back", "permissions.back").Classes("secondary-button"));
        var scope = UI.Stack("permissions.packages", children.ToArray())
            .InputScope("permissions.packages")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        if (page > 0) scope = scope.Shortcut(ControllerButton.LeftBumper, "permission.previous-page");
        if (page < lastPage) scope = scope.Shortcut(ControllerButton.RightBumper, "permission.next-page");
        var initial = visible.Length == 0 ? "permissions.back" : $"permission.item.{start}";
        return View(header, scope, initial, "permissions.packages");
    }

    private WidgetView RenderPackageCapabilities(StackElement header, bool busy)
    {
        PermissionPackage? package;
        ConsentDocument consent;
        int page;
        bool consentValid;
        string? diagnostic;
        lock (_stateLock)
        {
            package = SelectedPackageLocked();
            consent = _consent;
            page = _capabilityPage;
            consentValid = _consentValid;
            diagnostic = _permissionDiagnostic;
        }
        if (package is null)
            return MissingPermissionSelection(header, "Installed package is no longer available.",
                "capabilities.package");
        var lastPage = LastPage(package.Capabilities.Count, CapabilitiesPerPage);
        page = Math.Clamp(page, 0, lastPage);
        var start = page * CapabilitiesPerPage;
        var visible = package.Capabilities.Skip(start).Take(CapabilitiesPerPage).ToArray();
        var children = new List<WidgetElement>
        {
            UI.Text(package.Name, "capabilities.heading", "Selected package").Classes("page-heading"),
            UI.Text($"Publisher: {package.Publisher}", "capabilities.publisher", "Package publisher")
                .Classes("page-help"),
            UI.Text(
                    "Required access is needed for a declared widget feature; blocking it can limit that feature. " +
                    "Optional access only enables extra functionality. Neither type is allowed automatically.",
                    "capabilities.help", "Required and optional access help")
                .Classes("page-help"),
            UI.Text(consentValid ? $"Page {page + 1} of {lastPage + 1}" :
                    diagnostic ?? "Consent data is unavailable; changes are disabled.",
                "capabilities.page-label", "Capability page")
                .Classes(consentValid ? "page-counter" : "diagnostic-error"),
        };
        for (var offset = 0; offset < visible.Length; offset++)
        {
            var index = start + offset;
            var capability = visible[offset];
            var decision = FindDecision(consent, package, capability.Id);
            var label = $"{CapabilityName(capability.Id)} · " +
                        $"{(capability.IsRequired ? "Required" : "Optional")} · {DecisionLabel(decision)}";
            var button = UI.Button(label, $"capability.select.{index}", $"capability.item.{index}")
                .Disabled(!consentValid).Busy(busy)
                .Selected(decision == ConsentDecision.Grant).Classes("setting-row");
            if (offset > 0) button = button.FocusUp($"capability.item.{index - 1}");
            if (offset + 1 < visible.Length) button = button.FocusDown($"capability.item.{index + 1}");
            children.Add(button);
        }
        if (visible.Length == 0)
            children.Add(UI.Text("This package declares no supported capabilities.",
                "capabilities.empty", "No supported capabilities").Classes("page-help"));
        children.Add(UI.Button("Back", "back", "capabilities.back").Classes("secondary-button"));
        var scope = UI.Stack("capabilities.package", children.ToArray())
            .InputScope("capabilities.package")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        if (page > 0) scope = scope.Shortcut(ControllerButton.LeftBumper, "capability.previous-page");
        if (page < lastPage) scope = scope.Shortcut(ControllerButton.RightBumper, "capability.next-page");
        var initial = visible.Length == 0 ? "capabilities.back" : $"capability.item.{start}";
        return View(header, scope, initial, "capabilities.package");
    }

    private WidgetView RenderCapabilityDecision(StackElement header, bool busy)
    {
        PermissionPackage? package;
        DeclaredCapability? capability;
        ConsentDocument consent;
        bool consentValid;
        lock (_stateLock)
        {
            package = SelectedPackageLocked();
            capability = package?.Capabilities.FirstOrDefault(item => item.Id == _selectedCapabilityId);
            consent = _consent;
            consentValid = _consentValid;
        }
        if (package is null || capability is null || !PlatformCapabilities.TryGet(capability.Id, out _))
            return MissingPermissionSelection(header, "Capability is no longer declared by this package.",
                "capability.decision");
        var decision = FindDecision(consent, package, capability.Id);
        var granted = decision == ConsentDecision.Grant;
        var state = DecisionLabel(decision);
        var requirement = capability.IsRequired ? "Required access" : "Optional access";
        var children = new List<WidgetElement>
        {
            UI.Text(CapabilityName(capability.Id), "capability.heading", "Capability confirmation")
                .Classes("page-heading"),
            UI.Text($"{package.Name} · {requirement} · {state}",
                "capability.state", "Capability state").Classes("settings-summary"),
            UI.Text(CapabilityDescription(capability.Id),
                "capability.description", "Capability description").Classes("page-help"),
            UI.Text(
                    "The host allows this only when the widget identity, manifest request, your decision, " +
                    "and its current lifecycle state all permit it.",
                    "capability.enforcement", "Host enforcement summary").Classes("page-help"),
            UI.Text($"Technical ID: {capability.Id}",
                    "capability.technical-id", "Technical capability identifier")
                .Classes("diagnostic-line"),
        };
        if (!granted)
        {
            children.Add(UI.Text(
                $"Confirm granting this {requirement.ToLowerInvariant()} to " +
                $"{package.Name} from {package.Publisher}.",
                "capability.confirmation", "Grant confirmation").Classes("page-help"));
            children.Add(UI.Button("Allow access", "capability.grant", "capability.grant")
                .Disabled(!consentValid).Busy(busy).Classes("primary-button"));
        }
        children.Add(UI.Button(granted ? "Revoke access" : "Block access",
                "capability.deny", "capability.deny")
            .Disabled(!consentValid).Busy(busy).Classes("danger-button"));
        children.Add(UI.Button("Back", "back", "capability.back").Classes("secondary-button"));
        LinkVertical(children);
        var scope = UI.Stack("capability.decision", children.ToArray())
            .InputScope("capability.decision")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        return View(header, scope, granted ? "capability.deny" : "capability.grant",
            "capability.decision");
    }

    private static WidgetView MissingPermissionSelection(
        StackElement header, string message, string scopeId) => View(header,
        PageScope(scopeId,
            UI.Text(message, scopeId + ".error", "Permission selection unavailable")
                .Classes("diagnostic-error"),
            UI.Button("Back", "back", scopeId + ".back").Classes("secondary-button")),
        scopeId + ".back", scopeId);

    private async Task ChangeConsentAsync(
        ConsentDecision decision, CancellationToken cancellationToken)
    {
        PermissionPackage? package;
        DeclaredCapability? capability;
        bool consentValid;
        bool confirmationActive;
        lock (_stateLock)
        {
            package = SelectedPackageLocked();
            capability = package?.Capabilities.FirstOrDefault(item => item.Id == _selectedCapabilityId);
            consentValid = _consentValid;
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
                ConsentIdentity(package), capability.Id, decision, cancellationToken)
                .ConfigureAwait(false);
            lock (_stateLock)
            {
                _consent = updated;
                _busy = false;
                _error = false;
                _status = decision == ConsentDecision.Grant
                    ? $"Granted {CapabilityName(capability.Id)}"
                    : $"Denied {CapabilityName(capability.Id)}";
            }
        }
        catch (BrokerException exception)
        {
            lock (_stateLock)
            {
                if (exception.Code is "invalid_consent" or "unsafe_consent_store")
                    _consentValid = false;
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
            if (_page != SettingsPage.Permissions || !_permissionCatalogValid ||
                index < 0 || index >= _permissionPackages.Count) return;
            var package = _permissionPackages[index];
            _selectedPackageId = package.Id;
            _selectedPublisherId = package.AuthorityPublisher;
            _selectedCapabilityId = null;
            _capabilityPage = 0;
            _page = SettingsPage.PackageCapabilities;
        }
        Invalidate();
    }

    private void SelectCapability(int index)
    {
        lock (_stateLock)
        {
            var package = SelectedPackageLocked();
            if (_page != SettingsPage.PackageCapabilities || !_consentValid ||
                package is null || index < 0 || index >= package.Capabilities.Count)
                return;
            _selectedCapabilityId = package.Capabilities[index].Id;
            _page = SettingsPage.CapabilityDecision;
        }
        Invalidate();
    }

    private void ChangePermissionPackagePage(int delta)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.Permissions) return;
            _permissionPackagePage = Math.Clamp(_permissionPackagePage + delta, 0,
                LastPage(_permissionPackages.Count, PermissionPackagesPerPage));
        }
        Invalidate();
    }

    private void ChangeCapabilityPage(int delta)
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.PackageCapabilities) return;
            var package = SelectedPackageLocked();
            _capabilityPage = Math.Clamp(_capabilityPage + delta, 0,
                LastPage(package?.Capabilities.Count ?? 0, CapabilitiesPerPage));
        }
        Invalidate();
    }

    private PermissionPackage? SelectedPackageLocked() => _permissionPackages.FirstOrDefault(package =>
        package.Id == _selectedPackageId &&
        package.AuthorityPublisher == _selectedPublisherId);

    private static ConsentDecision? FindDecision(
        ConsentDocument consent, PermissionPackage package, string capabilityId) =>
        consent.Entries.FirstOrDefault(entry =>
            entry.PackageId == package.Id &&
            entry.PublisherId == package.AuthorityPublisher &&
            entry.CapabilityId == capabilityId)?.Decision;

    private static BrokerWidgetIdentity ConsentIdentity(PermissionPackage package) =>
        new(package.Id, package.AuthorityPublisher, "settings-consent");

    private static string ConsentKey(string packageId, string publisherId, string capabilityId) =>
        packageId + "\n" + publisherId + "\n" + capabilityId;

    private static int LastPage(int count, int pageSize) => Math.Max(0, (count - 1) / pageSize);

    private static string DecisionLabel(ConsentDecision? decision) => decision switch
    {
        ConsentDecision.Grant => "Granted by you",
        ConsentDecision.Deny => "Denied by you",
        _ => "Not decided — access is blocked",
    };

    private static string CapabilityName(string id) => id switch
    {
        PlatformCapabilities.AudioSessionsReadV1 => "Read audio sessions",
        PlatformCapabilities.AudioSessionsControlV1 => "Control audio sessions",
        PlatformCapabilities.AudioOutputReadV1 => "Read master output state",
        PlatformCapabilities.AudioOutputControlV1 => "Control master output",
        PlatformCapabilities.AudioDevicesReadV1 => "Read audio device names",
        PlatformCapabilities.AudioInputReadV1 => "Read microphone level",
        PlatformCapabilities.AudioInputControlV1 => "Control microphone level",
        PlatformCapabilities.NetworkReadV1 => "Read network status",
        PlatformCapabilities.NetworkSavedProfileSwitchV1 => "Switch saved network profile",
        PlatformCapabilities.NetworkWifiReadV1 => "Find nearby Wi-Fi networks",
        PlatformCapabilities.NetworkWifiConnectV1 => "Connect to visible Wi-Fi",
        PlatformCapabilities.NetworkWifiRadioReadV1 => "Read Wi-Fi radio state",
        PlatformCapabilities.NetworkWifiRadioControlV1 => "Turn Wi-Fi on or off",
        PlatformCapabilities.NetworkBluetoothReadV1 => "See Bluetooth devices",
        PlatformCapabilities.NetworkBluetoothRadioControlV1 => "Turn Bluetooth on or off",
        PlatformCapabilities.RecentActivityReadV1 => "See recently observed apps",
        PlatformCapabilities.RecentActivityActivateV1 => "Switch to a running app",
        _ => "Unsupported capability",
    };

    private static string CapabilityDescription(string id) => id switch
    {
        PlatformCapabilities.AudioSessionsReadV1 =>
            "See sanitized audio-session names, volume, mute, and activity state.",
        PlatformCapabilities.AudioSessionsControlV1 =>
            "Change volume or mute for an opaque audio-session ID while the widget is interactive.",
        PlatformCapabilities.AudioOutputReadV1 =>
            "See volume and mute for the current default multimedia output; no device identity.",
        PlatformCapabilities.AudioOutputControlV1 =>
            "Change master volume or mute for the current default multimedia output while interactive. It cannot switch devices.",
        PlatformCapabilities.AudioDevicesReadV1 =>
            "See sanitized names for active audio outputs and inputs and which devices are currently default. Raw endpoint IDs are never exposed.",
        PlatformCapabilities.AudioInputReadV1 =>
            "See volume and mute state for the current default multimedia microphone. It does not capture or inspect microphone audio.",
        PlatformCapabilities.AudioInputControlV1 =>
            "Change volume or mute for the current default multimedia microphone while the widget is interactive. It cannot record audio or switch devices.",
        PlatformCapabilities.NetworkReadV1 =>
            "See sanitized connectivity and saved-profile summaries without credentials.",
        PlatformCapabilities.NetworkSavedProfileSwitchV1 =>
            "Switch to an existing saved profile by opaque ID. It cannot create profiles or read passwords.",
        PlatformCapabilities.NetworkWifiReadV1 =>
            "Let Network Controls run a short scan and show nearby network names, signal strength, " +
            "security, and connection state. Windows precise-location permission must also be enabled; " +
            "the overlay cannot bypass that Windows setting.",
        PlatformCapabilities.NetworkWifiConnectV1 =>
            "Let Network Controls connect to a visible saved or open network while you are using it. " +
            "It cannot read saved passwords. Password entry and enterprise sign-in remain in Windows.",
        PlatformCapabilities.NetworkWifiRadioReadV1 =>
            "See whether Wi-Fi is on, off, unavailable, or disabled by a hardware switch. No adapter identity is exposed.",
        PlatformCapabilities.NetworkWifiRadioControlV1 =>
            "Turn the Wi-Fi software radio on or off while Network Controls is interactive. " +
            "It cannot override a hardware switch or Windows policy.",
        PlatformCapabilities.NetworkBluetoothReadV1 =>
            "See sanitized names and paired, connected, and nearby state for Bluetooth devices. " +
            "Widgets never receive Bluetooth addresses, Windows device IDs, or handles.",
        PlatformCapabilities.NetworkBluetoothRadioControlV1 =>
            "Turn the Bluetooth software radio on or off while Network Controls is interactive. " +
            "Windows permission, hardware switches, and device policy can block a change.",
        PlatformCapabilities.RecentActivityReadV1 =>
            "See a bounded list of privacy-filtered running applications observed after you allow access. " +
            "Widgets receive only display names, app kinds, state, and opaque IDs—never process IDs, paths, command lines, or window handles.",
        PlatformCapabilities.RecentActivityActivateV1 =>
            "Switch to a still-running observed application while the widget is interactive. " +
            "It cannot launch executables, reopen closed apps, or access their process and window identifiers.",
        _ => "This capability is not supported.",
    };

    private static void LinkVertical(List<WidgetElement> elements)
    {
        var buttonIndexes = elements.Select((element, index) => (element, index))
            .Where(item => item.element is ButtonElement).Select(item => item.index).ToArray();
        for (var position = 0; position < buttonIndexes.Length; position++)
        {
            var index = buttonIndexes[position];
            var button = (ButtonElement)elements[index];
            if (position > 0) button = button.FocusUp(((ButtonElement)elements[buttonIndexes[position - 1]]).Id);
            if (position + 1 < buttonIndexes.Length)
                button = button.FocusDown(((ButtonElement)elements[buttonIndexes[position + 1]]).Id);
            elements[index] = button;
        }
    }
}
