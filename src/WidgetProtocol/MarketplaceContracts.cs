namespace WidgetRail.WidgetProtocol;

public sealed record MarketplaceCommand(string Action, string? PackageId = null,
    string? OperationId = null, string? ReviewToken = null, bool ApproveFullTrust = false);

public sealed record MarketplaceItem(string Id, string Name, string Description,
    IReadOnlyList<string> Screenshots, string? AvailableVersion = null, string? InstalledVersion = null,
    long? DownloadBytes = null, bool IsPrerelease = false, bool AvailableVersionSaved = false);

public sealed record MarketplaceReview(string Token, string PackageId, string Name, string Version,
    string? InstalledVersion, bool FullTrust, bool Compatible, string Compatibility,
    IReadOnlyList<string> RequiredPermissions, IReadOnlyList<string> OptionalPermissions,
    IReadOnlyList<string> AddedPermissions, string Sha256);

public sealed record MarketplaceOperation(string Id, string Kind, string State, string? PackageId,
    long DownloadedBytes = 0, long? TotalBytes = null, MarketplaceReview? Review = null, string? Message = null);

public sealed record MarketplaceSnapshot(long Revision, IReadOnlyList<MarketplaceItem> Items,
    MarketplaceOperation? Operation = null, DateTimeOffset? CheckedAtUtc = null)
{
    public static MarketplaceSnapshot Unavailable { get; } = new(0, []);
}
