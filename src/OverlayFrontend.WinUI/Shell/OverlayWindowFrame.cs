using System.Runtime.InteropServices;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Suppress Windows 11's DWM outline on transparent overlay windows.</summary>
internal static partial class OverlayWindowFrame
{
    internal static void SetExtendedStyleWithoutActivation(nint hwnd, nint style)
    {
        const int extendedStyleIndex = -20;
        if (GetWindowLongPtrW(hwnd, extendedStyleIndex) == style) return;
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtrW(hwnd, extendedStyleIndex, style) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        // A style refresh must not acquire foreground or reorder a passive pin.
        // FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER | NOACTIVATE | NOOWNERZORDER.
        if (SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x237) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static void SuppressBorder(nint hwnd)
    {
        // OverlappedPresenter retains WS_DLGFRAME on a nonresizable window even
        // when SetBorderAndTitleBar(false, false) is used. Remove the native
        // nonclient frame after showing; DWM border color alone cannot hide it.
        const int styleIndex = -16;
        var style = GetWindowLongPtrW(hwnd, styleIndex);
        var borderless = style & ~(nint)0x00c00000; // WS_CAPTION = WS_BORDER | WS_DLGFRAME
        if (style != borderless)
        {
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLongPtrW(hwnd, styleIndex, borderless) == 0 && Marshal.GetLastPInvokeError() != 0)
                System.Diagnostics.Trace.TraceWarning("Overlay nonclient frame removal failed: {0}", Marshal.GetLastPInvokeError());
            else if (SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x37) == 0)
                System.Diagnostics.Trace.TraceWarning("Overlay nonclient frame refresh failed: {0}", Marshal.GetLastPInvokeError());
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        const uint borderColorAttribute = 34; // DWMWA_BORDER_COLOR
        const uint noBorder = 0xfffffffe; // DWMWA_COLOR_NONE, distinct from a transparent COLORREF
        var color = noBorder;
        var result = DwmSetWindowAttribute(hwnd, borderColorAttribute, in color, sizeof(uint));
        if (result < 0) System.Diagnostics.Trace.TraceWarning("Overlay DWM border policy failed: 0x{0:X8}", result);
    }

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, in uint value, uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
