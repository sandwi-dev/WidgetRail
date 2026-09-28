using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.WindowsDisplayProvider;

/// <summary>Read-only monitor connection identity. Saved physical identity is resolved by the bridge.</summary>
public sealed record WindowDisplayContext(string Id, string Name, IReadOnlyList<string> DevicePaths)
{
    public static WindowDisplayContext Unavailable { get; } = new(string.Empty, "Display unavailable", []);

    [SupportedOSPlatform("windows")]
    public static WindowDisplayContext Read(nint window)
    {
        try { return WindowsDisplayNative.ReadWindowContext(window); }
        catch (Exception error) when (error is Win32Exception or WidgetRail.PlatformBroker.BrokerException or IOException or UnauthorizedAccessException)
        { return Unavailable; }
    }

    internal static WindowDisplayContext FromTargets(IEnumerable<(string Path, string Name)> source)
    {
        var targets = source.Select(value => (Path: value.Path.ToUpperInvariant(), value.Name))
            .DistinctBy(value => value.Path).OrderBy(value => value.Path, StringComparer.Ordinal).ToArray();
        if (targets.Length is 0 or > 16 || targets.Any(value => string.IsNullOrWhiteSpace(value.Path) || value.Path.Length > 256 || value.Path.Any(char.IsControl)))
            return Unavailable;
        // Same connection-token construction as native DisplayScaleContext.h.
        var connection = string.Concat(targets.Select(value => value.Path + "\n"));
        var id = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(connection))).ToLowerInvariant();
        var name = (targets.Length > 1 ? "Duplicated displays: " : "") + string.Join(", ", targets.Select(value => value.Name));
        name = new string(name.Take(128).Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
        return new(id, name, Array.AsReadOnly(targets.Select(value => value.Path).ToArray()));
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsDisplayNative
{
    internal static WindowDisplayContext ReadWindowContext(nint window)
    {
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        if (monitor == 0 || GetMonitorInfoW(monitor, &info) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var device = ReadString(info.Device, 32);
        var (paths, _) = Query(2); // Active display paths only, including clones.
        var targets = new List<(string Path, string Name)>();
        foreach (var path in paths)
        {
            var source = new SourceName { Type = 1, Size = (uint)sizeof(SourceName), Adapter = path.Source.Adapter, Id = path.Source.Id };
            Check(DisplayConfigGetDeviceInfo(&source));
            if (!string.Equals(ReadString(source.Device, 32), device, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Name(path.Target);
            targets.Add((target.DevicePath, target.Name));
        }
        return WindowDisplayContext.FromTargets(targets);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public int Left, Top, Right, Bottom, WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;
        public fixed char Device[32];
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SourceName
    {
        public uint Type, Size;
        public NativeLuid Adapter;
        public uint Id;
        public fixed char Device[32];
    }
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetMonitorInfoW(nint monitor, MonitorInfo* info);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DisplayConfigGetDeviceInfo(SourceName* request);
}
