namespace WidgetRail.WidgetCatalog;

/// <summary>
/// Derives the host-side authority identity for one exact unsigned installed
/// package content tree. This is intentionally distinct from the publisher
/// label a manifest asserts. Rollback to the exact verified bytes may regain
/// prior consent. Reviewed updates explicitly copy matching consent decisions;
/// replacement bytes never inherit consent by manifest identity alone, or secrets.
/// A future signed-package path can return a verified signer authority instead.
/// </summary>
public static class InstalledWidgetAuthority
{
    public static string InstanceId(string packageId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId); ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{packageId}@{version}"));
        return $"installed.{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }
    internal static WidgetRail.PlatformBroker.BrokerWidgetIdentity Identity(InstalledWidgetVersion version) =>
        new(version.Id, PublisherId(version), InstanceId(version.Id, version.Version.ToString()));
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
