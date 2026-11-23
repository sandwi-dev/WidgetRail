namespace WidgetRail.WidgetProtocol;

public sealed record MarketplaceCommand(string Action, string? PackageId = null,
    string? OperationId = null, string? ReviewToken = null, bool ApproveFullTrust = false,
    IReadOnlyList<string>? PackageIds = null, bool ForceRefresh = false, ThemeBrowseQuery? ThemeQuery = null,
    CatalogueThemeSelection? Theme = null);

public sealed record CatalogueThemeSelection(string Id, string Version, string Sha256);
public sealed record CatalogueThemeStatus(bool Installed, bool Applied, bool AtCapacity);

public sealed record ThemeBrowseQuery(string Search = "", int Page = 0);

public static class ThemeCataloguePublication
{
    public const string Origin = "https://raw.githubusercontent.com/sandwi-dev/WidgetRail/gh-pages/themes/v1/";
    public const string PreviewActionPrefix = "host.try-catalogue-theme.";
    public static IReadOnlyList<string> PreviewFiles { get; } = Array.AsReadOnly(new[]
        { "tray-480.png", "tray-960.png", "tray.png", "posters.png", "notification.png" });

    public static string PreviewKeyFor(CatalogueTheme theme) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{theme.Id}/{theme.Version}/{theme.Sha256}")));

    public static string? PreviewFileDigest(Uri uri)
    {
        var prefix = Origin + "previews/";
        if (!uri.IsAbsoluteUri || !uri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal) || uri.Fragment.Length != 0 ||
            uri.Query is not { Length: 72 } query || !query.StartsWith("?sha256=", StringComparison.Ordinal) ||
            !query[8..].All(char.IsAsciiHexDigit)) return null;
        var parts = uri.GetLeftPart(UriPartial.Path)[prefix.Length..].Split('/');
        return parts is [ { Length: 64 } package, { Length: 64 } scene, var file ] &&
            package.All(char.IsAsciiHexDigit) && scene.All(char.IsAsciiHexDigit) && PreviewFiles.Contains(file, StringComparer.Ordinal)
            ? query[8..].ToLowerInvariant() : null;
    }
}

public sealed record CatalogueTheme(string Id, string Version, string Name, string Publisher, string Creator,
    string Description, string[] Tags, DateOnly Released, string Sha256, int Bytes, string PreviewKey,
    Dictionary<string, string>? PreviewHashes = null);

public sealed record ThemeBrowseShelf(string Name, CatalogueTheme[] Items);

public sealed record ThemeBrowsePage(int Page, int Total, CatalogueTheme[] Items, CatalogueTheme? Featured,
    ThemeBrowseShelf[] Collections, bool Refreshing, bool Offline);

public sealed record MarketplaceItem(string Id, string Name, string Description,
    IReadOnlyList<string> Screenshots, string? AvailableVersion = null, string? InstalledVersion = null,
    long? DownloadBytes = null, bool IsPrerelease = false, bool AvailableVersionSaved = false, bool RetainedAvailability = false,
    IReadOnlyList<string>? SetupRequirements = null);

public sealed record MarketplaceReview(string Token, string PackageId, string Name, string Version,
    string? InstalledVersion, bool FullTrust, bool Compatible, string Compatibility,
    IReadOnlyList<string> RequiredPermissions, IReadOnlyList<string> OptionalPermissions,
    IReadOnlyList<string> AddedPermissions, string Sha256);

public sealed record MarketplaceOperation(string Id, string Kind, string State, string? PackageId,
    long DownloadedBytes = 0, long? TotalBytes = null, MarketplaceReview? Review = null, string? Message = null);

public sealed record MarketplaceUpdateItem(string PackageId, string Name, string Version, string State,
    MarketplaceReview? Review = null, string? Message = null);

public sealed record MarketplaceUpdateBatch(IReadOnlyList<MarketplaceUpdateItem> Items, string? ReviewToken = null);

public sealed record MarketplaceSnapshot(long Revision, IReadOnlyList<MarketplaceItem> Items,
    MarketplaceOperation? Operation = null, DateTimeOffset? CheckedAtUtc = null, MarketplaceUpdateBatch? UpdateBatch = null,
    ThemeBrowsePage? Themes = null, CatalogueThemeStatus? ThemeStatus = null)
{
    public static MarketplaceSnapshot Unavailable { get; } = new(0, []);
}
