using System.ComponentModel;
using System.Runtime.InteropServices;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Window-owned global F1 fallback; independent of controller settings and widget lifetime.</summary>
internal sealed partial class OverlayKeyboardShortcut : IDisposable
{
    private const int HotkeyId = 1;
    private const uint HotkeyMessage = 0x0312;
    private const uint NoRepeat = 0x4000;
    private const uint F1 = 0x70;
    private readonly nint window;
    private readonly WindowMessageMonitor monitor;
    private bool disposed;
    internal bool IsRegistered { get; private set; }

    internal OverlayKeyboardShortcut(nint window, Action toggle)
    {
        this.window = window;
        monitor = new(window);
        monitor.WindowMessageReceived += (_, args) =>
        {
            if (disposed || !IsRegistered || args.Message.MessageId != HotkeyMessage ||
                (nint)args.Message.WParam != HotkeyId) return;
            args.Handled = true;
            args.Result = 0;
            // Never allow a managed exception to escape the native message callback.
            try { toggle(); }
            catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("f1-toggle", error); }
        };
        IsRegistered = RegisterHotKey(window, HotkeyId, NoRepeat, F1) != 0;
        if (!IsRegistered)
        {
            var error = Marshal.GetLastPInvokeError();
            monitor.Dispose();
            Diagnostics.FrontendFailureLog.Current.Write("f1-registration", new Win32Exception(error),
                "Global F1 shortcut unavailable; controller opening shortcut remains available.");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (IsRegistered)
        {
            if (UnregisterHotKey(window, HotkeyId) == 0)
                Diagnostics.FrontendFailureLog.Current.Write("f1-unregister", new Win32Exception(Marshal.GetLastPInvokeError()));
            IsRegistered = false;
            monitor.Dispose();
        }
    }

    // HWND is borrowed from MainWindow; this owner releases only its hotkey registration.
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int UnregisterHotKey(nint window, int id);
}
