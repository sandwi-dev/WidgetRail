using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameBarAlternative.FirstPartyWidgets.Settings;

public sealed partial class SettingsWidget
{
    private const int MaximumPermissionPackages = 256;
    private const int MaximumPermissionDiagnostics = 16;
    private const int MaximumDiagnosticComponentRunes = 120;
    private const int MaximumBundledDirectories = 64;
    private const int MaximumManifestBytes = 1024 * 1024;

    private sealed record DeclaredCapability(string Id, bool IsRequired);
    private sealed record UnknownDeclaration(
        string PackageId,
        string PackageName,
        string AuthorityPublisher,
        string CapabilityId,
        bool IsRequired);
    private sealed record HiddenConsentDecision(
        string PackageId,
        string PackageName,
        string PublisherId,
        string CapabilityId,
        ConsentDecision Decision);
    private sealed class UnknownDeclarationAccumulator
    {
        public int Count { get; private set; }
        public List<UnknownDeclaration> Details { get; } = [];

        public void Add(UnknownDeclaration item)
        {
            Count = checked(Count + 1);
            if (Details.Count < MaximumPermissionDiagnostics) Details.Add(item);
        }
    }
    private sealed record PermissionPackage(
        string Id,
        string Publisher,
        string AuthorityPublisher,
        string Name,
        IReadOnlyList<DeclaredCapability> Capabilities);

    private IReadOnlyList<PermissionPackage> _permissionPackages = [];
    private ConsentDocument _consent = ConsentDocument.Empty;
    private string? _selectedPackageId;
    private string? _selectedPublisherId;
    private string? _selectedCapabilityId;
    private bool _permissionCatalogValid = true;
    private bool _consentValid = true;
    private string? _permissionDiagnostic;
    private int _unknownDeclarations;
    private int _hiddenConsentEntries;
    private bool _inactiveConsentClassificationAvailable = true;
    private bool _permissionDiagnosticsReturnFocus;
    private IReadOnlyList<UnknownDeclaration> _unknownDeclarationDetails = [];
    private IReadOnlyList<HiddenConsentDecision> _hiddenConsentDetails = [];

    private async Task<string?> ReloadPermissionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionPackage> packages = [];
        var catalogValid = true;
        var catalogComplete = true;
        string? catalogDiagnostic = null;
        var unknownDeclarations = new UnknownDeclarationAccumulator();
        try
        {
            var catalog = await _widgetCatalog.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            var discovered = new Dictionary<string, PermissionPackage>(StringComparer.Ordinal);
            if (_bundledWidgetRoot is not null)
            {
                foreach (var manifest in DiscoverBundledManifests(_bundledWidgetRoot))
                {
                    var package = CreatePermissionPackage(
                        manifest, manifest.Publisher, unknownDeclarations);
                    if (package.Capabilities.Count != 0)
                        discovered.TryAdd(package.Id, package);
                }
            }
            foreach (var widget in catalog.Widgets.Take(MaximumPermissionPackages))
            {
                var manifest = widget.ActiveVersion.Manifest;
                var package = CreatePermissionPackage(
                    manifest,
                    InstalledWidgetAuthority.PublisherId(widget.ActiveVersion),
                    unknownDeclarations);
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
            {
                catalogComplete = false;
                catalogDiagnostic = $"Installed package list is limited to {MaximumPermissionPackages} entries";
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

        var inactiveClassificationAvailable = catalogValid && catalogComplete && consentValid;
        var declaredKeys = packages
            .SelectMany(package => package.Capabilities.Select(capability =>
                ConsentKey(package.Id, package.AuthorityPublisher, capability.Id)))
            .ToHashSet(StringComparer.Ordinal);
        var packageNames = packages.ToDictionary(
            package => ConsentKey(package.Id, package.AuthorityPublisher, string.Empty),
            package => package.Name,
            StringComparer.Ordinal);
        var hiddenConsentEntries = inactiveClassificationAvailable
            ? consent.Entries
                .Where(entry => !declaredKeys.Contains(ConsentKey(
                    entry.PackageId, entry.PublisherId, entry.CapabilityId)))
                .OrderBy(entry => entry.PackageId, StringComparer.Ordinal)
                .ThenBy(entry => entry.CapabilityId, StringComparer.Ordinal)
                .ThenBy(entry => entry.PublisherId, StringComparer.Ordinal)
                .ToArray()
            : [];
        var unknownCount = catalogValid ? unknownDeclarations.Count : 0;
        var unknownDetails = catalogValid
            ? unknownDeclarations.Details
                .OrderBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.CapabilityId, StringComparer.Ordinal)
                .Take(MaximumPermissionDiagnostics)
                .ToArray()
            : [];
        var remainingDiagnosticBudget = Math.Max(
            0, MaximumPermissionDiagnostics - unknownDetails.Length);
        var hiddenConsentDetails = hiddenConsentEntries
            .Take(remainingDiagnosticBudget)
            .Select(entry => new HiddenConsentDecision(
                entry.PackageId,
                packageNames.TryGetValue(
                    ConsentKey(entry.PackageId, entry.PublisherId, string.Empty), out var packageName)
                    ? packageName
                    : entry.PackageId,
                entry.PublisherId,
                entry.CapabilityId,
                entry.Decision))
            .ToArray();
        var diagnostic = string.Join("; ", new[] { catalogDiagnostic, consentDiagnostic }
            .Where(value => value is not null));
        lock (_stateLock)
        {
            _permissionPackages = packages;
            _consent = consent;
            _permissionCatalogValid = catalogValid;
            _consentValid = consentValid;
            _permissionDiagnostic = diagnostic.Length == 0 ? null : diagnostic;
            _unknownDeclarations = unknownCount;
            _hiddenConsentEntries = hiddenConsentEntries.Length;
            _inactiveConsentClassificationAvailable = inactiveClassificationAvailable;
            _unknownDeclarationDetails = unknownDetails;
            _hiddenConsentDetails = hiddenConsentDetails;
            var selected = SelectedPackageLocked();
            if (selected is null)
            {
                _selectedPackageId = null;
                _selectedPublisherId = null;
                _selectedCapabilityId = null;
                if (_page is SettingsPage.PackageCapabilities or SettingsPage.CapabilityDecision)
                    _page = SettingsPage.Permissions;
            }
            else
            {
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
        string authorityPublisher,
        UnknownDeclarationAccumulator unknownDeclarations)
    {
        var capabilities = new List<DeclaredCapability>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in manifest.Permissions)
        {
            if (PlatformCapabilities.TryGet(id, out _) && seen.Add(id))
                capabilities.Add(new(id, IsRequired: true));
            else if (!PlatformCapabilities.TryGet(id, out _))
                unknownDeclarations.Add(new(
                    manifest.Id, manifest.Name, authorityPublisher, id, IsRequired: true));
        }
        foreach (var id in manifest.OptionalPermissions)
        {
            if (PlatformCapabilities.TryGet(id, out _) && seen.Add(id))
                capabilities.Add(new(id, IsRequired: false));
            else if (!PlatformCapabilities.TryGet(id, out _))
                unknownDeclarations.Add(new(
                    manifest.Id, manifest.Name, authorityPublisher, id, IsRequired: false));
        }
        return new PermissionPackage(
            manifest.Id,
            manifest.Publisher,
            authorityPublisher,
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
        bool catalogValid;
        bool inactiveClassificationAvailable;
        bool returnDiagnosticsFocus;
        string? diagnostic;
        string? selectedPackageId;
        int unknown;
        int hidden;
        lock (_stateLock)
        {
            packages = _permissionPackages;
            catalogValid = _permissionCatalogValid;
            inactiveClassificationAvailable = _inactiveConsentClassificationAvailable;
            returnDiagnosticsFocus = _permissionDiagnosticsReturnFocus;
            diagnostic = _permissionDiagnostic;
            selectedPackageId = _selectedPackageId;
            unknown = _unknownDeclarations;
            hidden = _hiddenConsentEntries;
        }
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
        };
        var showDiagnosticsReview = unknown != 0 || hidden != 0 ||
                                    !inactiveClassificationAvailable;
        if (showDiagnosticsReview)
        {
            var label = inactiveClassificationAvailable
                ? $"Review unsupported or inactive access · {unknown} requests · {hidden} saved decisions"
                : $"Review unsupported or inactive access · {unknown} requests · inactive classification unavailable";
            children.Add(UI.Button(label, "open.permission-diagnostics",
                    "permissions.diagnostics.open")
                .Busy(busy).Classes("setting-row"));
        }
        for (var index = 0; index < packages.Count; index++)
        {
            var package = packages[index];
            var required = package.Capabilities.Count(capability => capability.IsRequired);
            var optional = package.Capabilities.Count - required;
            var label = $"{package.Name} · {required} required · {optional} optional";
            var button = UI.Button(label, $"permission.select.{index}", $"permission.item.{index}")
                .Disabled(!catalogValid).Busy(busy).Classes("setting-row");
            children.Add(button);
        }
        LinkVertical(children);
        var scope = UI.VerticalScroll("permissions.packages", children.ToArray())
            .InputScope("permissions.packages")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        var selectedIndex = packages
            .Select((package, index) => (package, index))
            .FirstOrDefault(item => string.Equals(
                item.package.Id, selectedPackageId, StringComparison.Ordinal)).index;
        var hasSelectedPackage = selectedPackageId is not null &&
            packages.Any(package => string.Equals(
                package.Id, selectedPackageId, StringComparison.Ordinal));
        var initial = showDiagnosticsReview && returnDiagnosticsFocus
            ? "permissions.diagnostics.open"
            : packages.Count != 0
                ? $"permission.item.{(hasSelectedPackage ? selectedIndex : 0)}"
                : showDiagnosticsReview ? "permissions.diagnostics.open" : null;
        return View(header, scope, initial, "permissions.packages");
    }

    private WidgetView RenderPermissionDiagnostics(StackElement header)
    {
        IReadOnlyList<UnknownDeclaration> unknownDetails;
        IReadOnlyList<HiddenConsentDecision> hiddenDetails;
        int unknown;
        int hidden;
        bool inactiveClassificationAvailable;
        lock (_stateLock)
        {
            unknownDetails = _unknownDeclarationDetails;
            hiddenDetails = _hiddenConsentDetails;
            unknown = _unknownDeclarations;
            hidden = _hiddenConsentEntries;
            inactiveClassificationAvailable = _inactiveConsentClassificationAvailable;
        }

        var children = new List<WidgetElement>
        {
            UI.Text("Unsupported or inactive access", "permission-diagnostics.heading",
                    "Unsupported or inactive widget access")
                .Classes("page-heading"),
            UI.Text(
                    "These rows are read-only. Unsupported requests are ignored; inactive saved decisions do not grant access to the current package identity.",
                    "permission-diagnostics.help", "Permission diagnostic help")
                .Classes("page-help"),
        };
        var focusableIds = new List<string>();
        if (!inactiveClassificationAvailable)
        {
            const string unavailableId = "permission-diagnostics.classification-unavailable";
            children.Add(ReadOnlyDiagnosticRow(
                "Inactive saved-decision classification is unavailable until the complete package catalog and consent document are valid.",
                unavailableId));
            focusableIds.Add(unavailableId);
        }

        foreach (var item in unknownDetails)
        {
            var requirement = item.IsRequired ? "required" : "optional";
            var packageName = SafeDiagnosticComponent(item.PackageName);
            var capabilityId = SafeDiagnosticComponent(item.CapabilityId);
            var authority = AuthorityDiagnosticLabel(item.AuthorityPublisher);
            var label = $"Unsupported {requirement} request · {packageName} · {capabilityId} · {authority}";
            var id = PermissionDiagnosticId("unknown", item.PackageId,
                item.AuthorityPublisher, item.CapabilityId, requirement);
            children.Add(ReadOnlyDiagnosticRow(label, id));
            focusableIds.Add(id);
        }
        foreach (var item in hiddenDetails)
        {
            var packageName = SafeDiagnosticComponent(item.PackageName);
            var capabilityId = SafeDiagnosticComponent(item.CapabilityId);
            var capabilityName = SafeDiagnosticComponent(CapabilityName(item.CapabilityId));
            var authority = AuthorityDiagnosticLabel(item.PublisherId);
            var label = $"Inactive {item.Decision} decision · {packageName} · {capabilityName} ({capabilityId}) · {authority}";
            var id = PermissionDiagnosticId("inactive", item.PackageId,
                item.PublisherId, item.CapabilityId, item.Decision.ToString());
            children.Add(ReadOnlyDiagnosticRow(label, id));
            focusableIds.Add(id);
        }

        var displayed = unknownDetails.Count + hiddenDetails.Count;
        var undisplayed = Math.Max(0, checked(unknown + hidden - displayed));
        if (undisplayed != 0)
        {
            const string moreId = "permission-diagnostics.more";
            children.Add(ReadOnlyDiagnosticRow(
                $"{undisplayed} more diagnostics are hidden by the {MaximumPermissionDiagnostics}-item display limit.",
                moreId));
            focusableIds.Add(moreId);
        }
        if (focusableIds.Count == 0)
        {
            const string emptyId = "permission-diagnostics.empty";
            children.Add(ReadOnlyDiagnosticRow(
                "No unsupported requests or inactive saved decisions were found.", emptyId));
            focusableIds.Add(emptyId);
        }

        LinkVertical(children);
        var scope = UI.VerticalScroll("permission-diagnostics.page", children.ToArray())
            .InputScope("permission-diagnostics.page")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        return View(header, scope, focusableIds[0], "permission-diagnostics.page");
    }

    private WidgetView RenderPackageCapabilities(StackElement header, bool busy)
    {
        PermissionPackage? package;
        ConsentDocument consent;
        bool consentValid;
        string? diagnostic;
        string? selectedCapabilityId;
        lock (_stateLock)
        {
            package = SelectedPackageLocked();
            consent = _consent;
            consentValid = _consentValid;
            diagnostic = _permissionDiagnostic;
            selectedCapabilityId = _selectedCapabilityId;
        }
        if (package is null)
            return MissingPermissionSelection(header, "Installed package is no longer available.",
                "capabilities.package");
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
        };
        if (!consentValid)
        {
            children.Add(UI.Text(
                    diagnostic ?? "Consent data is unavailable; changes are disabled.",
                    "capabilities.diagnostic", "Consent diagnostic")
                .Classes("diagnostic-error"));
        }
        for (var index = 0; index < package.Capabilities.Count; index++)
        {
            var capability = package.Capabilities[index];
            var decision = FindDecision(consent, package, capability.Id);
            var label = $"{CapabilityName(capability.Id)} · " +
                        $"{(capability.IsRequired ? "Required" : "Optional")} · {DecisionLabel(decision)}";
            var button = UI.Button(label, $"capability.select.{index}", $"capability.item.{index}")
                .Disabled(!consentValid).Busy(busy)
                .Selected(decision == ConsentDecision.Grant).Classes("setting-row");
            children.Add(button);
        }
        if (package.Capabilities.Count == 0)
            children.Add(UI.Text("This package declares no supported capabilities.",
                "capabilities.empty", "No supported capabilities").Classes("page-help"));
        LinkVertical(children);
        var scope = UI.VerticalScroll("capabilities.package", children.ToArray())
            .InputScope("capabilities.package")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        var selectedIndex = package.Capabilities
            .Select((capability, index) => (capability, index))
            .FirstOrDefault(item => string.Equals(
                item.capability.Id, selectedCapabilityId, StringComparison.Ordinal)).index;
        var hasSelectedCapability = selectedCapabilityId is not null &&
            package.Capabilities.Any(capability => string.Equals(
                capability.Id, selectedCapabilityId, StringComparison.Ordinal));
        var initial = package.Capabilities.Count == 0
            ? null
            : $"capability.item.{(hasSelectedCapability ? selectedIndex : 0)}";
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
        LinkVertical(children);
        var scope = UI.VerticalScroll("capability.decision", children.ToArray())
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
                .Classes("diagnostic-error")),
        null, scopeId);

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
            _permissionDiagnosticsReturnFocus = false;
            _page = SettingsPage.PackageCapabilities;
        }
        Invalidate();
    }

    private void OpenPermissionDiagnostics()
    {
        lock (_stateLock)
        {
            if (_page != SettingsPage.Permissions ||
                (_unknownDeclarations == 0 && _hiddenConsentEntries == 0 &&
                 _inactiveConsentClassificationAvailable))
                return;
            _permissionDiagnosticsReturnFocus = true;
            _page = SettingsPage.PermissionDiagnostics;
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

    private static ButtonElement ReadOnlyDiagnosticRow(string label, string id)
    {
        var safeLabel = SafeDiagnosticText(label);
        return UI.Button(safeLabel, "permission-diagnostics.read-only", id)
            .Disabled(true)
            .Classes("setting-row", "permission-diagnostic-row") with
        {
            AccessibilityLabel = safeLabel,
        };
    }

    private static string AuthorityDiagnosticLabel(string publisherId)
    {
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(publisherId)))[..12]
            .ToLowerInvariant();
        return $"authority {SafeDiagnosticComponent(publisherId)} · {fingerprint}";
    }

    private static string PermissionDiagnosticId(string kind, params string[] components)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", components)));
        return $"permission-diagnostics.{kind}.{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string SafeDiagnosticText(string value) =>
        SafeDiagnosticComponent(value, maxRunes: 640);

    private static string SafeDiagnosticComponent(
        string? value,
        int maxRunes = MaximumDiagnosticComponentRunes)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unnamed";
        var builder = new StringBuilder(Math.Min(value.Length, maxRunes + 1));
        var pendingSpace = false;
        var appended = 0;
        var truncated = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.Control or
                UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator)
            {
                pendingSpace = builder.Length != 0;
                continue;
            }
            if (appended >= maxRunes)
            {
                truncated = true;
                break;
            }
            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }
            builder.Append(rune.ToString());
            appended++;
        }
        if (truncated) builder.Append('…');
        return builder.Length == 0 ? "Unnamed" : builder.ToString();
    }

    private static string DecisionLabel(ConsentDecision? decision) => decision switch
    {
        ConsentDecision.Grant => "Granted by you",
        ConsentDecision.Deny => "Denied by you",
        _ => "Not decided — access is blocked",
    };

    private static string CapabilityName(string id)
    {
        if (PlatformCapabilities.TryGetLoopbackPort(id, out var port))
            return $"Access local app on port {port}";
        return id switch
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
        PlatformCapabilities.NetworkBluetoothPairV1 => "Pair Bluetooth devices",
        PlatformCapabilities.NetworkBluetoothManageV1 => "Open Bluetooth device settings",
        PlatformCapabilities.RecentActivityReadV1 => "See recently observed apps",
        PlatformCapabilities.AppLibraryReadV1 => "See installed apps",
        PlatformCapabilities.AppLibraryLaunchV1 => "Launch installed apps",
        PlatformCapabilities.MediaSessionsReadV1 => "See Windows media sessions",
        PlatformCapabilities.MediaSessionsControlV1 => "Control media playback",
        PlatformCapabilities.PrivateSecretsV1 => "Store private connection secrets",
        _ => "Unsupported capability",
        };
    }

    private static string CapabilityDescription(string id)
    {
        if (PlatformCapabilities.TryGetLoopbackPort(id, out var port))
            return $"Exchange bounded JSON with only 127.0.0.1:{port}. GET works while the " +
                "widget is visible; POST requires an interactive widget or a host-issued dashboard " +
                "gesture. The widget cannot choose another host or port, use DNS, follow redirects, " +
                "configure a proxy, or open raw sockets.";
        return id switch
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
        PlatformCapabilities.NetworkBluetoothPairV1 =>
            "Pair one currently visible device by its opaque ID while Network Controls is interactive. " +
            "Windows owns the pairing ceremony; the widget never receives an address or native device ID and cannot claim the device is connected.",
        PlatformCapabilities.NetworkBluetoothManageV1 =>
            "Open the Windows Bluetooth device settings page after an explicit controller action. " +
            "No device identifier is placed in the settings URI and Windows remains responsible for connect, disconnect, removal, and profile-specific setup.",
        PlatformCapabilities.RecentActivityReadV1 =>
            "See a bounded list of privacy-filtered running applications observed after you allow access. " +
            "Widgets receive only display names, app kinds, state, and opaque IDs—never process IDs, paths, command lines, or window handles.",
        PlatformCapabilities.AppLibraryReadV1 =>
            "See a bounded catalog of Start Menu application names, conservative kinds, and opaque IDs. " +
            "Widgets never receive paths, shortcuts, command lines, package identities, AUMIDs, or launch authority.",
        PlatformCapabilities.AppLibraryLaunchV1 =>
            "Launch one selected Start Menu registration by its opaque ID while the widget is interactive. " +
            "The trusted host rechecks the exact registration before asking Windows to open it; widgets cannot " +
            "supply paths, arguments, working directories, elevation, or foreground-window commands.",
        PlatformCapabilities.MediaSessionsReadV1 =>
            "See sanitized app, title, artist, playback, progress, and supported-control state from " +
            "Windows media sessions. Widgets never receive package IDs, process IDs, paths, handles, or native objects.",
        PlatformCapabilities.MediaSessionsControlV1 =>
            "Use only the play, pause, previous, and next actions that Windows reports as supported " +
            "while the widget is interactive. It cannot automate an app or access its account.",
        PlatformCapabilities.PrivateSecretsV1 =>
            "Create, replace, inspect metadata for, or delete package-scoped secrets in Windows " +
            "Credential Manager. Stored values are never returned to widget code; an exact-port " +
            "loopback request can ask the trusted host to inject one as a Bearer value. Secrets " +
            "remain available to authenticated updates from the same package publisher.",
        _ => "This capability is not supported.",
        };
    }

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
