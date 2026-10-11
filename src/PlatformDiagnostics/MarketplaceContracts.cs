namespace WidgetRail.PlatformDiagnostics;

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

public static class MarketplaceContract
{
    public static void ValidateCommand(MarketplaceCommand command)
    {
        if (command is null || command.Action is not ("status" or "refresh" or "inventory" or "download" or "install" or "cancel") ||
            command.PackageId is not null && !Token(command.PackageId, 128) ||
            command.OperationId is not null && !Hex(command.OperationId, 32) ||
            command.ReviewToken is not null && !Hex(command.ReviewToken, 64) ||
            command.Action == "download" && command.PackageId is null ||
            command.Action != "download" && command.PackageId is not null ||
            command.Action is "install" or "cancel" && command.OperationId is null ||
            command.Action is not ("install" or "cancel") && command.OperationId is not null ||
            command.Action == "install" && command.ReviewToken is null ||
            command.Action != "install" && (command.ReviewToken is not null || command.ApproveFullTrust))
            throw new PlatformDiagnosticsException("invalid_marketplace_command");
    }

    public static void ValidateSnapshot(MarketplaceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Revision < 0 || snapshot.Items is null || snapshot.Items.Count > 16 ||
            snapshot.Items.Any(item => item is null || !Token(item.Id, 128) || !Text(item.Name, 120) || !Text(item.Description, 1024) ||
                item.Screenshots is null || item.Screenshots.Count > 4 || item.Screenshots.Any(url =>
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                    uri.Host != "raw.githubusercontent.com" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || url.Length > 512) ||
                item.AvailableVersion is not null && (!Text(item.AvailableVersion, 64) || !Version.TryParse(item.AvailableVersion, out _)) ||
                item.InstalledVersion is not null && (!Text(item.InstalledVersion, 64) || !Version.TryParse(item.InstalledVersion, out _)) || item.DownloadBytes < 0) ||
            snapshot.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Items.Count)
            throw new PlatformDiagnosticsException("invalid_marketplace_snapshot");
        if (snapshot.Operation is not { } operation) return;
        if (!Hex(operation.Id, 32) || operation.Kind is not ("refresh" or "inventory" or "download" or "install") ||
            operation.State is not ("running" or "review" or "completed" or "cancelled" or "failed") ||
            operation.PackageId is not null && !Token(operation.PackageId, 128) || operation.DownloadedBytes < 0 || operation.TotalBytes < 0 ||
            operation.Message is not null && !Text(operation.Message, 512))
            throw new PlatformDiagnosticsException("invalid_marketplace_operation");
        if (operation.State == "review" && operation.Review is null) throw new PlatformDiagnosticsException("invalid_marketplace_review");
        if (operation.Review is not { } review) return;
        if (operation.State != "review" || review.PackageId != operation.PackageId) throw new PlatformDiagnosticsException("invalid_marketplace_review");
        if (!Hex(review.Token, 64) || !Token(review.PackageId, 128) || !Text(review.Name, 120) || !Text(review.Version, 64) ||
            review.InstalledVersion is not null && !Text(review.InstalledVersion, 64) || !Text(review.Compatibility, 512) ||
            !Permissions(review.RequiredPermissions) || !Permissions(review.OptionalPermissions) || !Permissions(review.AddedPermissions) ||
            !Hex(review.Sha256, 64)) throw new PlatformDiagnosticsException("invalid_marketplace_review");
    }

    private static bool Token(string? value, int maximum) => Text(value, maximum) && value!.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Hex(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
    private static bool Permissions(IReadOnlyList<string>? values) => values is not null && values.Count <= 64 && values.All(v => Text(v, 160));
}
