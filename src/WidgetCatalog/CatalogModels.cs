using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetCatalog;

public sealed record WidgetCatalogOptions
{
    public int MaximumArchiveEntries { get; init; } = 512;
    public long MaximumEntryBytes { get; init; } = 16 * 1024 * 1024;
    public long MaximumTotalBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumPathLength { get; init; } = 240;

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

public sealed record WidgetVersionChange(string Id, Version PreviousVersion, Version SelectedVersion);

public sealed record WidgetUninstallResult(
    string Id,
    IReadOnlyList<Version> RemovedVersions,
    bool CleanupPending = false);

public sealed class WidgetPackageException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
