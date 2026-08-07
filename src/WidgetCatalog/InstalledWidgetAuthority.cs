using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetCatalog;

/// <summary>
/// Derives the host-side authority identity for one exact unsigned installed
/// package version. This is intentionally distinct from the publisher label a
/// manifest asserts: an immutable version may regain its own prior consent on
/// rollback, but a different unsigned version cannot inherit it.
/// </summary>
public static class InstalledWidgetAuthority
{
    public static string PublisherId(WidgetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = WidgetManifestValidator.Validate(manifest);
        if (errors.Count != 0)
            throw new ArgumentException("A valid manifest is required.", nameof(manifest));
        var canonical = string.Join('\n', manifest.Publisher, manifest.Id, manifest.Version);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"unsigned.{Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant()}";
    }
}
