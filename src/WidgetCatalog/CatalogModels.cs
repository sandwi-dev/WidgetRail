using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetCatalog;

public sealed record WidgetCatalogOptions
{
    public int MaximumArchiveEntries { get; init; } = 512;
    public long MaximumEntryBytes { get; init; } = 16 * 1024 * 1024;
    public long MaximumTotalBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumPathLength { get; init; } = 240;
    public int MaximumInstalledWidgetIds { get; init; } = 256;
    public int MaximumVersionsPerWidget { get; init; } = 8;
    public int MaximumInstalledVersions { get; init; } = 512;
    public int MaximumInstalledEntries { get; init; } = 32_768;
    public long MaximumInstalledBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public TimeSpan MaximumDiscoveryDuration { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaximumArchiveEntries is < 2 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumArchiveEntries));
        if (MaximumEntryBytes is < 1 or > 1024L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumEntryBytes));
        if (MaximumTotalBytes < MaximumEntryBytes || MaximumTotalBytes > 4L * 1024 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumTotalBytes));
        if (MaximumPathLength is < 32 or > 1_024)
            throw new ArgumentOutOfRangeException(nameof(MaximumPathLength));
        if (MaximumInstalledWidgetIds is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumInstalledWidgetIds));
        if (MaximumVersionsPerWidget is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(MaximumVersionsPerWidget));
        if (MaximumInstalledVersions is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumInstalledVersions));
        if (MaximumInstalledEntries < checked(MaximumArchiveEntries + 1) ||
            MaximumInstalledEntries > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(MaximumInstalledEntries));
        if (MaximumInstalledBytes < MaximumTotalBytes ||
            MaximumInstalledBytes > 256L * 1024 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumInstalledBytes));
        if (MaximumDiscoveryDuration < TimeSpan.FromSeconds(1) ||
            MaximumDiscoveryDuration > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(MaximumDiscoveryDuration));
    }
}

public sealed record InstalledWidgetVersion(
    string Id,
    Version Version,
    string InstallPath,
    WidgetManifest Manifest,
    string ContentDigest)
{
    internal IReadOnlyDictionary<string, string> VerifiedGbssDigests { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, VerifiedPackageFile> VerifiedFiles { get; init; } =
        new Dictionary<string, VerifiedPackageFile>(StringComparer.Ordinal);
    internal WidgetCatalogOptions VerificationOptions { get; init; } = new();
    internal int VerifiedEntryCount { get; init; }
    internal long VerifiedTotalBytes { get; init; }
}

public sealed record CatalogWidget(
    string Id,
    string Name,
    bool Enabled,
    int Order,
    InstalledWidgetVersion ActiveVersion,
    IReadOnlyList<InstalledWidgetVersion> Versions);

public sealed record WidgetCatalogSnapshot(IReadOnlyList<CatalogWidget> Widgets);

public sealed record WidgetPackageInspection(
    string Id,
    Version Version,
    WidgetManifest Manifest,
    int EntryCount,
    long TotalUncompressedBytes);

/// <summary>
/// An explicit caller decision for a package that runs as an ordinary
/// current-user process. Omission always rejects full-trust installation or
/// enablement and has no effect on sandboxed packages.
/// </summary>
public enum WidgetPackageTrustApproval
{
    None,
    FullTrustCurrentUser,
}

public sealed record WidgetVersionChange(string Id, Version PreviousVersion, Version SelectedVersion);

public sealed record WidgetUninstallResult(
    string Id,
    IReadOnlyList<Version> RemovedVersions,
    bool CleanupPending = false);

internal sealed record WidgetUninstallInspection(
    string Id,
    string Name,
    string PublisherId,
    Version ActiveVersion,
    int VersionCount,
    bool Enabled,
    string ConfirmationToken);

/// <summary>
/// A directory-name-only recovery candidate. No manifest or package content
/// from this version was trusted to create this record.
/// </summary>
public sealed record WidgetCatalogRepairCandidate(
    string Id,
    Version Version,
    bool WidgetEnabled,
    bool Selected)
{
    public bool CanRemove => !Selected;
}

public sealed record WidgetCatalogHealthSnapshot(
    string? FailureCode,
    IReadOnlyList<WidgetCatalogRepairCandidate> Candidates)
{
    public bool IsWithinDirectoryLimits => FailureCode is null;
}

public sealed record WidgetVersionRemovalResult(
    string Id,
    Version Version,
    bool CleanupPending = false);

public sealed class WidgetPackageException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
