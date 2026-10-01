using System.Runtime.InteropServices;
using WinUIEx;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal static partial class PinnedDisplayAreas
{
    internal static IReadOnlyList<PinnedMonitor> Read() => MonitorInfo.GetDisplayMonitors().Select(monitor =>
    {
        var rect = monitor.RectWork;
        var handle = MonitorFromPoint(new((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2)), 2);
        var scale = GetDpiForMonitor(handle, 0, out var dpiX, out _) == 0 && dpiX is >= 48 and <= 960 ? dpiX / 96d : 1;
        return new PinnedMonitor(monitor.Name, new((int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height), scale, monitor.IsPrimary);
    }).ToArray();

    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);
    [LibraryImport("shcore.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}
