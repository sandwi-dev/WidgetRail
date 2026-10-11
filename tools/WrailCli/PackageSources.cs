using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.WrailCli;

internal static class ThemePackageDigest
{
    internal static string Compute(ThemePackageInspection inspection)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in inspection.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath + "\0"));
            hash.AppendData(BitConverter.GetBytes(file.Content.Length));
            hash.AppendData(file.Content);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
