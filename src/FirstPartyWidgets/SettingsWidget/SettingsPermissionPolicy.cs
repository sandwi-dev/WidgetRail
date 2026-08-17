using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsDeclaredCapability(string Id, bool IsRequired);

internal sealed record SettingsUnknownDeclaration(
    string PackageId,
    string PackageName,
    string AuthorityPublisher,
    string CapabilityId,
    bool IsRequired);

internal sealed record SettingsHiddenConsentDecision(
    string PackageId,
    string PackageName,
    string PublisherId,
    string CapabilityId,
    ConsentDecision Decision);

internal sealed record SettingsPermissionPackage(
    string Id,
    string Publisher,
    string AuthorityPublisher,
    string Name,
    IReadOnlyList<SettingsDeclaredCapability> Capabilities,
    string? ContentDigest);

internal sealed record SettingsPermissionProjection(
    IReadOnlyList<SettingsPermissionPackage> Packages,
    ConsentDocument Consent,
    bool CatalogValid,
    bool ConsentValid,
    string? Diagnostic,
    int UnknownDeclarations,
    int HiddenConsentEntries,
    bool InactiveConsentClassificationAvailable,
    IReadOnlyList<SettingsUnknownDeclaration> UnknownDeclarationDetails,
    IReadOnlyList<SettingsHiddenConsentDecision> HiddenConsentDetails);

internal sealed class SettingsUnknownDeclarationAccumulator
{
    public int Count { get; private set; }
    public List<SettingsUnknownDeclaration> Details { get; } = [];

    public void Add(SettingsUnknownDeclaration item)
    {
        Count = checked(Count + 1);
        if (Details.Count < SettingsPermissionProjectionPolicy.MaximumPermissionDiagnostics)
            Details.Add(item);
    }
}

/// <summary>Pure manifest and consent projection for the Settings permission pages.</summary>
internal static class SettingsPermissionProjectionPolicy
{
    public const int MaximumPermissionPackages = 256;
    public const int MaximumPermissionDiagnostics = 16;

    public static SettingsPermissionPackage CreatePackage(
        WidgetManifest manifest,
        string authorityPublisher,
        SettingsUnknownDeclarationAccumulator unknownDeclarations,
        string? contentDigest = null)
    {
        var capabilities = new List<SettingsDeclaredCapability>();
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
        return new SettingsPermissionPackage(
            manifest.Id,
            manifest.Publisher,
            authorityPublisher,
            manifest.Name,
            capabilities.OrderByDescending(capability => capability.IsRequired)
                .ThenBy(capability => capability.Id, StringComparer.Ordinal)
                .ToArray(),
            contentDigest?.ToLowerInvariant());
    }

    public static SettingsPermissionProjection Compose(
        IReadOnlyList<SettingsPermissionPackage> packages,
        ConsentDocument consent,
        bool catalogValid,
        bool catalogComplete,
        bool consentValid,
        string? catalogDiagnostic,
        string? consentDiagnostic,
        SettingsUnknownDeclarationAccumulator unknownDeclarations)
    {
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
            .Select(entry => new SettingsHiddenConsentDecision(
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
        return new SettingsPermissionProjection(
            packages,
            consent,
            CatalogValid: catalogValid,
            ConsentValid: consentValid,
            Diagnostic: diagnostic.Length == 0 ? null : diagnostic,
            UnknownDeclarations: unknownCount,
            HiddenConsentEntries: hiddenConsentEntries.Length,
            InactiveConsentClassificationAvailable: inactiveClassificationAvailable,
            UnknownDeclarationDetails: unknownDetails,
            HiddenConsentDetails: hiddenConsentDetails);
    }

    public static string ConsentKey(
        string packageId,
        string publisherId,
        string capabilityId) =>
        packageId + "\n" + publisherId + "\n" + capabilityId;
}

internal sealed record SettingsPermissionState(
    SettingsPermissionProjection Projection,
    string? SelectedPackageId,
    string? SelectedPublisherId,
    string? SelectedCapabilityId,
    SettingsPage PackageCapabilitiesReturnPage,
    bool DiagnosticsReturnFocus)
{
    public static SettingsPermissionState Empty { get; } = new(
        new SettingsPermissionProjection(
            [],
            ConsentDocument.Empty,
            CatalogValid: true,
            ConsentValid: true,
            Diagnostic: null,
            UnknownDeclarations: 0,
            HiddenConsentEntries: 0,
            InactiveConsentClassificationAvailable: true,
            UnknownDeclarationDetails: [],
            HiddenConsentDetails: []),
        SelectedPackageId: null,
        SelectedPublisherId: null,
        SelectedCapabilityId: null,
        PackageCapabilitiesReturnPage: SettingsPage.Permissions,
        DiagnosticsReturnFocus: false);

    public SettingsPermissionPackage? SelectedPackage =>
        Projection.Packages.FirstOrDefault(package =>
            string.Equals(package.Id, SelectedPackageId, StringComparison.Ordinal) &&
            string.Equals(
                package.AuthorityPublisher,
                SelectedPublisherId,
                StringComparison.Ordinal));
}

internal readonly record struct SettingsPermissionTransition(
    SettingsPermissionState State,
    SettingsPage Page);

/// <summary>
/// Value-only permission selection, stale-projection, and consent action policy.
/// Consent-store I/O and committed Settings state remain owned by the widget.
/// </summary>
internal static class SettingsPermissionPolicy
{
    public static SettingsPermissionTransition Reconcile(
        SettingsPermissionState current,
        SettingsPermissionProjection projection,
        SettingsPage page)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(projection);
        var state = current with { Projection = projection };
        var selected = state.SelectedPackage;
        if (selected is null)
        {
            state = state with
            {
                SelectedPackageId = null,
                SelectedPublisherId = null,
                SelectedCapabilityId = null,
            };
            if (page is SettingsPage.PackageCapabilities or SettingsPage.CapabilityDecision)
                page = state.PackageCapabilitiesReturnPage;
        }
        else if (state.SelectedCapabilityId is not null &&
            !selected.Capabilities.Any(capability => string.Equals(
                capability.Id, state.SelectedCapabilityId, StringComparison.Ordinal)))
        {
            state = state with { SelectedCapabilityId = null };
            if (page == SettingsPage.CapabilityDecision)
                page = SettingsPage.PackageCapabilities;
        }
        return new(state, page);
    }

    public static bool TrySelectPackage(
        SettingsPermissionState state,
        int index,
        out SettingsPermissionTransition transition)
    {
        var packages = state.Projection.Packages;
        if (!state.Projection.CatalogValid || index < 0 || index >= packages.Count)
        {
            transition = default;
            return false;
        }
        var package = packages[index];
        transition = new(
            state with
            {
                SelectedPackageId = package.Id,
                SelectedPublisherId = package.AuthorityPublisher,
                SelectedCapabilityId = null,
                PackageCapabilitiesReturnPage = SettingsPage.Permissions,
                DiagnosticsReturnFocus = false,
            },
            SettingsPage.PackageCapabilities);
        return true;
    }

    public static bool TryOpenInstalledPackage(
        SettingsPermissionState state,
        bool installedCatalogValid,
        string? packageId,
        out SettingsPermissionTransition transition)
    {
        var package = installedCatalogValid && state.Projection.CatalogValid && packageId is not null
            ? state.Projection.Packages.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, packageId, StringComparison.Ordinal))
            : null;
        if (package is null)
        {
            transition = default;
            return false;
        }
        transition = new(
            state with
            {
                SelectedPackageId = package.Id,
                SelectedPublisherId = package.AuthorityPublisher,
                SelectedCapabilityId = null,
                PackageCapabilitiesReturnPage = SettingsPage.InstalledWidgetDetails,
                DiagnosticsReturnFocus = false,
            },
            SettingsPage.PackageCapabilities);
        return true;
    }

    public static bool TryOpenDiagnostics(
        SettingsPermissionState state,
        out SettingsPermissionTransition transition)
    {
        var projection = state.Projection;
        if (projection.UnknownDeclarations == 0 &&
            projection.HiddenConsentEntries == 0 &&
            projection.InactiveConsentClassificationAvailable)
        {
            transition = default;
            return false;
        }
        transition = new(
            state with { DiagnosticsReturnFocus = true },
            SettingsPage.PermissionDiagnostics);
        return true;
    }

    public static bool TrySelectCapability(
        SettingsPermissionState state,
        int index,
        out SettingsPermissionTransition transition)
    {
        var package = state.SelectedPackage;
        if (!state.Projection.ConsentValid || package is null ||
            index < 0 || index >= package.Capabilities.Count)
        {
            transition = default;
            return false;
        }
        transition = new(
            state with { SelectedCapabilityId = package.Capabilities[index].Id },
            SettingsPage.CapabilityDecision);
        return true;
    }

    public static ConsentDecision? FindDecision(
        ConsentDocument consent,
        SettingsPermissionPackage package,
        string capabilityId) =>
        consent.Entries.FirstOrDefault(entry =>
            string.Equals(entry.PackageId, package.Id, StringComparison.Ordinal) &&
            string.Equals(entry.PublisherId, package.AuthorityPublisher, StringComparison.Ordinal) &&
            string.Equals(entry.CapabilityId, capabilityId, StringComparison.Ordinal))?.Decision;

    public static BrokerWidgetIdentity ConsentIdentity(SettingsPermissionPackage package) =>
        new(package.Id, package.AuthorityPublisher, "settings-consent");
}
