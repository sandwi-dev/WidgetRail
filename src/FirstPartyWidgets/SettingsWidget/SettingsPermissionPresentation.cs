using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PermissionPackage = WidgetRail.FirstPartyWidgets.Settings.SettingsPermissionPackage;
using DeclaredCapability = WidgetRail.FirstPartyWidgets.Settings.SettingsDeclaredCapability;
using UnknownDeclaration = WidgetRail.FirstPartyWidgets.Settings.SettingsUnknownDeclaration;
using HiddenConsentDecision = WidgetRail.FirstPartyWidgets.Settings.SettingsHiddenConsentDecision;

namespace WidgetRail.FirstPartyWidgets.Settings;

/// <summary>Pure snapshot-only composition for permission and consent Settings pages.</summary>
internal static class SettingsPermissionPresentation
{
    private const int MaximumDiagnosticComponentRunes = 120;

    public static WidgetView RenderPermissionPackages(
        StackElement header,
        bool busy,
        SettingsPermissionState state)
    {
        var projection = state.Projection;
        var packages = projection.Packages;
        var catalogValid = projection.CatalogValid;
        var inactiveClassificationAvailable = projection.InactiveConsentClassificationAvailable;
        var returnDiagnosticsFocus = state.DiagnosticsReturnFocus;
        var diagnostic = projection.Diagnostic;
        var selectedPackageId = state.SelectedPackageId;
        var unknown = projection.UnknownDeclarations;
        var hidden = projection.HiddenConsentEntries;
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
        SettingsPresentation.LinkVertical(children);
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
        return SettingsPresentation.View(header, scope, initial, "permissions.packages");
    }

    public static WidgetView RenderPermissionDiagnostics(
        StackElement header,
        SettingsPermissionState state)
    {
        var projection = state.Projection;
        var unknownDetails = projection.UnknownDeclarationDetails;
        var hiddenDetails = projection.HiddenConsentDetails;
        var unknown = projection.UnknownDeclarations;
        var hidden = projection.HiddenConsentEntries;
        var inactiveClassificationAvailable = projection.InactiveConsentClassificationAvailable;

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
                $"{undisplayed} more diagnostics are hidden by the {SettingsPermissionProjectionPolicy.MaximumPermissionDiagnostics}-item display limit.",
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

        SettingsPresentation.LinkVertical(children);
        var scope = UI.VerticalScroll("permission-diagnostics.page", children.ToArray())
            .InputScope("permission-diagnostics.page")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        return SettingsPresentation.View(header, scope, focusableIds[0], "permission-diagnostics.page");
    }

    public static WidgetView RenderPackageCapabilities(
        StackElement header,
        bool busy,
        SettingsPermissionState state)
    {
        var package = state.SelectedPackage;
        var consent = state.Projection.Consent;
        var consentValid = state.Projection.ConsentValid;
        var diagnostic = state.Projection.Diagnostic;
        var selectedCapabilityId = state.SelectedCapabilityId;
        if (package is null)
            return MissingPermissionSelection(header, "Installed package is no longer available.",
                "capabilities.package");
        var children = new List<WidgetElement>
        {
            UI.Text(package.Name, "capabilities.heading", "Selected package").Classes("page-heading"),
            UI.Text(package.ContentDigest is null
                    ? "Trust: Built-in"
                    : "Trust: Unsigned · publisher unverified",
                "capabilities.trust", "Package trust status")
                .Classes(package.ContentDigest is null ? "diagnostic-ok" : "diagnostic-error"),
            UI.Text(package.ContentDigest is null
                    ? $"Publisher: {package.Publisher}"
                    : $"Declared publisher (unverified): {package.Publisher}",
                "capabilities.publisher", "Package publisher claim")
                .Classes("page-help"),
            UI.Text(
                    "Required access is needed for a declared widget feature; blocking it can limit that feature. " +
                    "Optional access only enables extra functionality. Neither type is allowed automatically.",
                    "capabilities.help", "Required and optional access help")
                .Classes("page-help"),
        };
        if (package.ContentDigest is not null)
        {
            children.Add(UI.Text("Consent authority is bound to sealed content SHA-256:",
                "capabilities.digest-label", "Consent digest label").Classes("page-help"));
            children.Add(UI.CodeText(package.ContentDigest, "capabilities.digest",
                "Sealed content SHA-256 digest"));
        }
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
            var decision = SettingsPermissionPolicy.FindDecision(
                consent, package, capability.Id);
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
        SettingsPresentation.LinkVertical(children);
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
        return SettingsPresentation.View(header, scope, initial, "capabilities.package");
    }

    public static WidgetView RenderCapabilityDecision(
        StackElement header,
        bool busy,
        SettingsPermissionState permissionState)
    {
        var package = permissionState.SelectedPackage;
        var capability = package?.Capabilities.FirstOrDefault(item =>
            string.Equals(
                item.Id,
                permissionState.SelectedCapabilityId,
                StringComparison.Ordinal));
        var consent = permissionState.Projection.Consent;
        var consentValid = permissionState.Projection.ConsentValid;
        if (package is null || capability is null || !PlatformCapabilities.TryGet(capability.Id, out _))
            return MissingPermissionSelection(header, "Capability is no longer declared by this package.",
                "capability.decision");
        var decision = SettingsPermissionPolicy.FindDecision(
            consent, package, capability.Id);
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
            var recipient = package.ContentDigest is null
                ? $"{package.Name} from {package.Publisher}"
                : $"unsigned {package.Name}; declared publisher {package.Publisher} is unverified, " +
                  $"and this decision is bound to SHA-256 {SettingsPresentation.ShortContentDigest(package.ContentDigest)}…";
            children.Add(UI.Text(
                $"Confirm granting this {requirement.ToLowerInvariant()} to " +
                $"{recipient}.",
                "capability.confirmation", "Grant confirmation").Classes("page-help"));
            children.Add(UI.Button("Allow access", "capability.grant", "capability.grant")
                .Disabled(!consentValid).Busy(busy).Classes("primary-button"));
        }
        children.Add(UI.Button(granted ? "Revoke access" : "Block access",
                "capability.deny", "capability.deny")
            .Disabled(!consentValid).Busy(busy).Classes("danger-button"));
        SettingsPresentation.LinkVertical(children);
        var scope = UI.VerticalScroll("capability.decision", children.ToArray())
            .InputScope("capability.decision")
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");
        return SettingsPresentation.View(header, scope, granted ? "capability.deny" : "capability.grant",
            "capability.decision");
    }

    private static WidgetView MissingPermissionSelection(
        StackElement header, string message, string scopeId) => SettingsPresentation.View(header,
        SettingsPresentation.PageScope(scopeId,
            UI.Text(message, scopeId + ".error", "Permission selection unavailable")
                .Classes("diagnostic-error")),
        null, scopeId);

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

    public static string CapabilityName(string id)
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
        PlatformCapabilities.NetworkDetailsReadV1 => "Read current connection details",
        PlatformCapabilities.NetworkSavedProfileSwitchV1 => "Switch saved network profile",
        PlatformCapabilities.NetworkWifiReadV1 => "Find nearby Wi-Fi networks",
        PlatformCapabilities.NetworkWifiConnectV1 => "Connect to visible Wi-Fi",
        PlatformCapabilities.NetworkWifiRadioReadV1 => "Read Wi-Fi radio state",
        PlatformCapabilities.NetworkWifiRadioControlV1 => "Turn Wi-Fi on or off",
        PlatformCapabilities.NetworkBluetoothReadV1 => "See Bluetooth devices",
        PlatformCapabilities.NetworkBluetoothRadioControlV1 => "Turn Bluetooth on or off",
        PlatformCapabilities.NetworkBluetoothPairV1 => "Pair Bluetooth devices",
        PlatformCapabilities.NetworkBluetoothUnpairV1 => "Remove Bluetooth pairings",
        PlatformCapabilities.NetworkBluetoothManageV1 => "Open Bluetooth device settings",
        PlatformCapabilities.RecentActivityReadV1 => "See recently observed apps",
        PlatformCapabilities.AppLibraryReadV1 => "See installed apps",
        PlatformCapabilities.AppRunningReadV1 => "See visible running apps",
        PlatformCapabilities.AppRunningRegisterV1 => "Remember running apps",
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
        PlatformCapabilities.NetworkDetailsReadV1 =>
            "See bounded current IP, default-gateway, and DNS display values for one preferred " +
            "connection. It does not expose adapter IDs, MAC addresses, routes, traffic, Wi-Fi " +
            "identity, or public location.",
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
        PlatformCapabilities.NetworkBluetoothUnpairV1 =>
            "Remove one currently paired device after explicit confirmation while Network Controls is interactive. " +
            "The widget receives only an opaque ID; Windows owns the pairing and the refreshed device list determines success.",
        PlatformCapabilities.NetworkBluetoothManageV1 =>
            "Open the Windows Bluetooth device settings page after an explicit controller action. " +
            "No device identifier is placed in the settings URI and Windows remains responsible for connect, disconnect, and profile-specific setup.",
        PlatformCapabilities.RecentActivityReadV1 =>
            "See a bounded list of privacy-filtered running applications observed after you allow access. " +
            "Widgets receive only display names, app kinds, state, and opaque IDs—never process IDs, paths, command lines, or window handles.",
        PlatformCapabilities.AppLibraryReadV1 =>
            "See a bounded catalog of Start Menu application names, conservative kinds, and opaque IDs. " +
            "Widgets never receive paths, shortcuts, command lines, package identities, AUMIDs, or launch authority.",
        PlatformCapabilities.AppRunningReadV1 =>
            "On request, see visible applications that exactly match the installed catalog. " +
            "Widgets receive only names, kinds, sources, and opaque saved IDs—never process or window identity.",
        PlatformCapabilities.AppRunningRegisterV1 =>
            "After an explicit action, remember one exact visible application for this widget package. " +
            "The trusted provider privately stores and later rechecks its executable path and Windows file identity; " +
            "widgets receive only an opaque saved ID and cannot supply paths, arguments, working directories, or elevation. " +
            "Forgetting removes only this package's portable record; installed catalog entries are unchanged.",
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

}
