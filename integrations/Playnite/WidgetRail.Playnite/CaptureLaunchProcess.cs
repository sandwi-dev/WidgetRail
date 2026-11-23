using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;

namespace WidgetRail.Playnite;

internal static class CaptureLaunchProcess
{
    internal static Process? Resolve(int pid, string? installDirectory)
    {
        var reported = Process.GetProcessById(pid);
        Process? player = null;
        try
        {
            if (!IsUnityHelper(reported.ProcessName))
            {
                var direct = reported;
                reported = null;
                return direct;
            }
            if (string.IsNullOrWhiteSpace(installDirectory) || !Path.IsPathRooted(installDirectory)) return null;
            using var info = new ManagementObject("Win32_Process.Handle='" + pid.ToString(CultureInfo.InvariantCulture) + "'");
            var parent = Convert.ToInt32(info["ParentProcessId"], CultureInfo.InvariantCulture);
            if (parent <= 0) return null;
            player = Process.GetProcessById(parent);
            // A 32-bit Playnite process cannot enumerate modules in a 64-bit Unity player.
            // WMI returns the path for these two exact PIDs without opening their module lists.
            using var parentInfo = new ManagementObject("Win32_Process.Handle='" + parent.ToString(CultureInfo.InvariantCulture) + "'");
            var helperPath = info["ExecutablePath"] as string;
            var playerPath = parentInfo["ExecutablePath"] as string;
            if (!IsPlayer(installDirectory!,helperPath,playerPath) || player.StartTime.ToUniversalTime() > reported.StartTime.ToUniversalTime() ||
                reported.HasExited || player.HasExited) return null;
            var resolved = player;
            player = null;
            return resolved;
        }
        finally { player?.Dispose(); reported?.Dispose(); }
    }

    private static bool IsUnityHelper(string name) => name.Equals("UnityCrashHandler64",StringComparison.OrdinalIgnoreCase) ||
        name.Equals("UnityCrashHandler32",StringComparison.OrdinalIgnoreCase);

    internal static bool IsPlayer(string root,string? helper,string? player)
    {
        if (helper == null || player == null || !IsUnityHelper(Path.GetFileNameWithoutExtension(helper)) ||
            !player.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) return false;
        var folder = Path.GetDirectoryName(Path.GetFullPath(player))!;
        var installed = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return (folder + Path.DirectorySeparatorChar).StartsWith(installed,StringComparison.OrdinalIgnoreCase) &&
            folder.Equals(Path.GetDirectoryName(Path.GetFullPath(helper)),StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(folder,"UnityPlayer.dll")) &&
            Directory.Exists(Path.Combine(folder,Path.GetFileNameWithoutExtension(player) + "_Data"));
    }
}
