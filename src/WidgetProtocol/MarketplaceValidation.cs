namespace WidgetRail.WidgetProtocol;

/// <summary>Shared wire bounds for the private Settings channel and scoped marketplace service.</summary>
public static class MarketplaceValidation
{
    public static string? CommandError(MarketplaceCommand command)
    {
        if (command is null || command.Action is not ("status" or "refresh" or "inventory" or "download" or "install" or "cancel" or "download-updates" or "install-updates") ||
            command.PackageId is not null && !Token(command.PackageId,128) || command.OperationId is not null && !Hex(command.OperationId,32) ||
            command.ReviewToken is not null && !Hex(command.ReviewToken,64) || command.Action == "download" && command.PackageId is null ||
            command.Action != "download" && command.PackageId is not null || command.Action is "install" or "install-updates" or "cancel" && command.OperationId is null ||
            command.Action is not ("install" or "install-updates" or "cancel") && command.OperationId is not null || command.Action is "install" or "install-updates" && command.ReviewToken is null ||
            command.Action is not ("install" or "install-updates") && (command.ReviewToken is not null || command.ApproveFullTrust)) return "invalid_marketplace_command";
        if (command.ForceRefresh && command.Action != "refresh") return "invalid_marketplace_command";
        if (command.Action == "download-updates")
        {
            if (command.PackageIds is not { Count: > 0 and <= 16 } ids || ids.Any(id => !Token(id,128)) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) return "invalid_marketplace_command";
        }
        else if (command.PackageIds is not null) return "invalid_marketplace_command";
        return null;
    }
    public static string? SnapshotError(MarketplaceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Revision < 0 || snapshot.Items is null || snapshot.Items.Count > 32 ||
            snapshot.Items.Any(item => item is null || !Token(item.Id,128) || !Text(item.Name,120) || !Text(item.Description,1024) ||
                item.Screenshots is null || item.Screenshots.Count > 4 || item.Screenshots.Any(url => !Uri.TryCreate(url,UriKind.Absolute,out var uri) ||
                    uri.Scheme != "https" || uri.Host != "raw.githubusercontent.com" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || url.Length > 512) ||
                item.AvailableVersion is not null && (!Text(item.AvailableVersion,64) || !Version.TryParse(item.AvailableVersion,out _)) ||
                item.RetainedAvailability && item.AvailableVersion is null ||
                item.SetupRequirements is { } requirements && (requirements.Count > 8 || requirements.Any(value => !Text(value,320))) ||
                item.InstalledVersion is not null && (!Text(item.InstalledVersion,64) || !Version.TryParse(item.InstalledVersion,out _)) || item.DownloadBytes < 0) ||
            snapshot.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Items.Count) return "invalid_marketplace_snapshot";
        if (snapshot.UpdateBatch is { } batch)
        {
            if (batch.Items is not { Count: > 0 and <= 16 } entries ||
                entries.Any(item => item is null || !Token(item.PackageId,128) || !Text(item.Name,120) ||
                    !Text(item.Version,64) || !Version.TryParse(item.Version,out _) ||
                    item.State is not ("queued" or "downloading" or "review" or "installing" or "completed" or "failed" or "cancelled") ||
                    item.Message is not null && !Text(item.Message,512) ||
                    (item.State == "review" ? item.Review is null || !item.Review.Compatible || item.Review.Version != item.Version ||
                        item.Review.InstalledVersion is null || ReviewError(item.Review,item.PackageId) is not null : item.Review is not null)) ||
                entries.Select(item => item.PackageId).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                return "invalid_marketplace_update_batch";
            if (batch.ReviewToken is not null && (!Hex(batch.ReviewToken,64) || !entries.Any(item => item.State == "review") ||
                snapshot.Operation is not { Kind: "download-updates", State: "review" })) return "invalid_marketplace_update_batch";
        }
        if (snapshot.Operation is not { } operation) return snapshot.UpdateBatch is null ? null : "invalid_marketplace_update_batch";
        if (!Hex(operation.Id,32) || operation.Kind is not ("refresh" or "inventory" or "download" or "install" or "download-updates" or "install-updates") ||
            operation.State is not ("running" or "review" or "completed" or "cancelled" or "failed") ||
            operation.PackageId is not null && !Token(operation.PackageId,128) || operation.DownloadedBytes < 0 || operation.TotalBytes < 0 ||
            operation.Message is not null && !Text(operation.Message,512)) return "invalid_marketplace_operation";
        if (operation.Kind is "download-updates" or "install-updates")
        {
            if (snapshot.UpdateBatch is null || operation.Review is not null ||
                operation.State == "review" && snapshot.UpdateBatch.ReviewToken is null) return "invalid_marketplace_update_batch";
            return null;
        }
        if (operation.State == "review" && operation.Review is null) return "invalid_marketplace_review";
        if (operation.Review is not { } review) return null;
        return operation.State != "review" ? "invalid_marketplace_review" : ReviewError(review, operation.PackageId);
    }
    private static string? ReviewError(MarketplaceReview review, string? packageId)
    {
        if (review.PackageId != packageId || !Hex(review.Token,64) || !Token(review.PackageId,128) ||
            !Text(review.Name,120) || !Text(review.Version,64) || review.InstalledVersion is not null && !Text(review.InstalledVersion,64) ||
            !Text(review.Compatibility,512) || !Permissions(review.RequiredPermissions) || !Permissions(review.OptionalPermissions) ||
            !Permissions(review.AddedPermissions) || !Hex(review.Sha256,64)) return "invalid_marketplace_review";
        return null;
    }
    private static bool Token(string? value,int max) => Text(value,max) && value!.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    private static bool Text(string? value,int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    private static bool Hex(string? value,int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
    private static bool Permissions(IReadOnlyList<string>? values) => values is not null && values.Count <= 64 && values.All(v => Text(v,160));
}
