using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.WidgetBridge;

internal static class InstalledWidgetInstanceIdentity
{
    internal static string Derive(string packageId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{packageId}@{version}"));
        return $"installed.{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }
}
