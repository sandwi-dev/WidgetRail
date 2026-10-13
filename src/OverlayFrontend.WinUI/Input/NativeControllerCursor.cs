using System.Runtime.InteropServices;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal sealed partial class NativeControllerCursor : IDisposable
{
    private nuint window;
    internal void Show(int x, int y, uint dpi, ControllerPointerAppearance appearance)
    {
        var scale = dpi / 96d;
        var result = Draw(ref window, x, y, Math.Clamp((int)Math.Round(ControllerPointerAppearance.Diameter * scale), 8, 128),
            Math.Max(1, (int)Math.Round(ControllerPointerAppearance.Border * scale)), appearance.Fill, appearance.Outline, 1);
        Marshal.ThrowExceptionForHR(result);
    }
    internal void Hide() { if (window != 0) _ = Draw(ref window, 0, 0, 16, 2, 0, 0, 0); }
    public void Dispose() { if (window != 0) Destroy(window); window = 0; }
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformDrawControllerCursor")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static partial int Draw(ref nuint window, int x, int y, int size, int border, uint fill, uint outline, uint visible);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformDestroyControllerCursor")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static partial void Destroy(nuint window);
}
