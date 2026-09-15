using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.WindowsDisplayProvider;

public static class DisplayScaleIdentity
{
    public static string? Resolve(IReadOnlyList<string> devicePaths)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var connected = new WindowsDisplayNative().ConnectedPaths()
                .Select(target => target.Identity).DistinctBy(target => target.DevicePath, StringComparer.OrdinalIgnoreCase).ToArray();
            return Resolve(devicePaths, connected);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or
            WidgetRail.PlatformBroker.BrokerException or IOException or UnauthorizedAccessException)
        { return null; }
    }

    internal static string? Resolve(IReadOnlyList<string> devicePaths, IReadOnlyList<DisplayIdentity> connected)
    {
        if (devicePaths.Count is 0 or > 16 || devicePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != devicePaths.Count)
            return null;
        var inventory = connected.DistinctBy(target => target.DevicePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var keys = new List<string>();
        foreach (var path in devicePaths)
        {
            var target = inventory.SingleOrDefault(item => item.DevicePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (target is null) return null;
            // A serial duplicated by connected devices cannot identify a physical monitor.
            // Keep those monitors' sizing separate by connection instead.
            var unique = target.HardwareKey is not null && inventory.Count(item => item.HardwareKey == target.HardwareKey) == 1;
            keys.Add(unique ? "hardware:" + target.HardwareKey : "path:" + target.DevicePath.ToUpperInvariant());
        }
        keys.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', keys)))).ToLowerInvariant();
    }
}
