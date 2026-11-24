using System.Runtime.InteropServices;
using WinUIEx;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal static partial class PinnedDisplayAreas
{
    internal static IReadOnlyList<PinnedMonitor> Read(nint pin = 0)
    {
        var pinMonitor = pin == 0 ? 0 : MonitorFromWindow(pin, 0);
        var pinDpi = pinMonitor == 0 ? 0 : GetDpiForWindow(pin);
        return MonitorInfo.GetDisplayMonitors().Select(monitor =>
        {
            var rect = monitor.RectWork;
            var handle = MonitorFromPoint(new((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2)), 2);
            var scale = handle != 0 && handle == pinMonitor && pinDpi is >= 48 and <= 960
                ? pinDpi / 96d
                : GetScaleFactorForMonitor(handle, out var percent) == 0 && percent is >= 50 and <= 1000 ? percent / 100d : 1;
            return new PinnedMonitor(monitor.Name, new((int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height), scale, monitor.IsPrimary);
        }).ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("shcore.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetScaleFactorForMonitor(nint monitor, out int scalePercent);
}
