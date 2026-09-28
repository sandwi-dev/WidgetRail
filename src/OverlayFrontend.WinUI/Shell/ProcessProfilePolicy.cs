using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>One settings profile owns one renderer, independent of executable/package version.</summary>
internal static class ProcessProfilePolicy
{
    internal static string ForSettingsRoot(string settingsRoot, string defaultSettingsRoot)
    {
        var profile = Normalize(settingsRoot);
        if (profile == Normalize(defaultSettingsRoot)) return "production";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
    }
    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("A process profile requires an absolute settings root.");
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('/', '\\');
        if (canonical.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) canonical = "\\\\" + canonical[8..];
        else if (canonical.StartsWith("\\\\?\\", StringComparison.Ordinal)) canonical = canonical[4..];
        return canonical.ToUpperInvariant();
    }
}
