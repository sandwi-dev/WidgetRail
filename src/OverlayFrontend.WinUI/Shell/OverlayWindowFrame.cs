using System.Runtime.InteropServices;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Maintain native overlay layers and suppress DWM outlines without activating windows.</summary>
internal static partial class OverlayWindowFrame
{
#if ENABLE_WIDGET_VALIDATION
    internal static void DemoteForValidation(nint window)=>SetWindowPos(window,-2,0,0,0,0,0x213);
    internal static long ZOrderWritesForValidation { get; private set; }
    internal static long FrameRefreshesForValidation { get; private set; }
#endif
    [System.Diagnostics.Conditional("ENABLE_WIDGET_VALIDATION")]
    private static void RecordOrderWrite()
    {
#if ENABLE_WIDGET_VALIDATION
        ++ZOrderWritesForValidation;
#endif
    }
    [System.Diagnostics.Conditional("ENABLE_WIDGET_VALIDATION")]
    private static void RecordFrameRefresh()
    {
#if ENABLE_WIDGET_VALIDATION
        ++FrameRefreshesForValidation;
#endif
    }

    internal static bool IsAbove(nint window, nint other)
    {
        if(window==0||other==0||window==other)return false;
        // Top-level HWND order can change outside our dispatcher. Bound the walk
        // instead of relying on adjacency or caching an order another app can change.
        var current=other;
        for(var remaining=4096;remaining>0;remaining--)
        {
            current=GetWindow(current,3); // GW_HWNDPREV
            if(current==window)return true;
            if(current==0||current==other)return false;
        }
        return false;
    }

    internal static void EnsureAbove(nint window,nint below)
    {
        if(window!=below&&!IsAbove(window,below))PlaceAbove(window,below);
    }
    internal static void EnsureBelow(nint window,nint above)
    {
        if(window!=above&&!IsAbove(above,window))PlaceBelow(window,above);
    }

    internal static void PlaceAbove(nint window, nint below)
    {
        var previous = GetWindow(below, 3); // GW_HWNDPREV
        if (previous == window) return;
        // Insert directly above the sibling, preserving every other window's
        // relative order. HWND_TOP when there is no predecessor. Never activate.
        RecordOrderWrite();
        if (SetWindowPos(window, previous, 0, 0, 0, 0, 0x213) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static void PlaceBelow(nint window, nint above)
    {
        // NOACTIVATE | NOMOVE | NOSIZE | NOOWNERZORDER. Change stacking only,
        // without recreating/reparenting native media or transferring focus.
        if (GetWindow(window, 3) == above) return; // GW_HWNDPREV
        RecordOrderWrite();
        if (SetWindowPos(window, above, 0, 0, 0, 0, 0x213) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static void KeepTopmost(nint window)
    {
        // Do not repeatedly raise an already-topmost passive window over other
        // applications' menus/dialogs. Promotion is only needed after demotion.
        if ((GetWindowLongPtrW(window, -20) & 8) != 0) return;
        RecordOrderWrite();
        if (SetWindowPos(window, -1, 0, 0, 0, 0, 0x213) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int IsWindowVisible(nint window);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindow(nint window, uint command);

    internal static void SetExtendedStyleWithoutActivation(nint hwnd, nint style)
    {
        const int extendedStyleIndex = -20;
        if (GetWindowLongPtrW(hwnd, extendedStyleIndex) == style) return;
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtrW(hwnd, extendedStyleIndex, style) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        // A style refresh must not acquire foreground or reorder a passive pin.
        // FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER | NOACTIVATE | NOOWNERZORDER.
        RecordFrameRefresh();
        if (SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x237) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static void SuppressBorder(nint hwnd, bool suppressNativeCorners = false)
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
            else
            {
                RecordFrameRefresh();
                if (SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x37) == 0)
                    System.Diagnostics.Trace.TraceWarning("Overlay nonclient frame refresh failed: {0}", Marshal.GetLastPInvokeError());
            }
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        if(suppressNativeCorners)
        {
            // DWM's rounded native contour can remain visible around a fully
            // transparent gutter. These peers draw their own rounded content.
            const uint cornerPreferenceAttribute=33; // DWMWA_WINDOW_CORNER_PREFERENCE
            var squareWindow=1u; // DWMWCP_DONOTROUND
            var cornerResult=DwmSetWindowAttribute(hwnd,cornerPreferenceAttribute,in squareWindow,sizeof(uint));
            if(cornerResult<0)System.Diagnostics.Trace.TraceWarning("Overlay native corner policy failed: 0x{0:X8}",cornerResult);
        }
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
