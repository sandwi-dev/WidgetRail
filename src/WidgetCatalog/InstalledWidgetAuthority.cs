namespace WidgetRail.WidgetCatalog;

/// <summary>
/// Derives the host-side authority identity for one exact unsigned installed
/// package content tree. This is intentionally distinct from the publisher
/// label a manifest asserts. Rollback to the exact verified bytes may regain
/// prior consent, while replacement bytes cannot inherit consent or secrets.
/// A future signed-package path can return a verified signer authority instead.
/// </summary>
public static class InstalledWidgetAuthority
{
    public static string PublisherId(InstalledWidgetVersion installedVersion)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        if (installedVersion.ContentDigest.Length != 64 ||
            !installedVersion.ContentDigest.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "A verified package content digest is required.", nameof(installedVersion));
        return $"unsigned.{installedVersion.ContentDigest.ToLowerInvariant()}";
    }
}
