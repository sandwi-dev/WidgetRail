namespace WidgetRail.WidgetProtocol;

public static class WidgetCatalogIconValidation
{
    public static bool IsValid(WidgetCatalogIconSource? source) =>
        source is not null &&
        source.PackageId is { Length: > 0 and <= 128 } &&
        WidgetManifestValidator.IsPackageIdentity(source.PackageId) &&
        source.Version is { Length: > 0 and <= 128 } &&
        source.Version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+') &&
        (source.ContentDigest is null || source.ContentDigest is { Length: 64 } && source.ContentDigest.All(Uri.IsHexDigit));

    internal static int TextLength(WidgetPackageIcon? icon) => icon?.Catalog is { } source
        ? (source.PackageId?.Length ?? 0) + (source.Version?.Length ?? 0) + (source.ContentDigest?.Length ?? 0) : 0;
}
