using System.ComponentModel;
using System.Runtime.InteropServices;
using WinUIEx.Messaging;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>One window-owned global key registration. New bindings reserve a distinct ID before replacing the old registration.</summary>
internal sealed partial class OverlayKeyboardShortcut : IDisposable
{
    private static int nextId = 1000;
    private readonly int hotkeyId;
    private const uint HotkeyMessage = 0x0312;
    private const uint NoRepeat = 0x4000;
    private readonly nint window;
    private readonly WindowMessageMonitor monitor;
    private bool disposed;
    private bool suppressed;
    private int admittedSince = Environment.TickCount;
    internal bool IsRegistered { get; private set; }
    internal int ErrorCode { get; private set; }
    internal OverlayShortcutSettings Shortcut { get; }
    internal Action<OverlayShortcutSettings>? CapturedWhileSuppressed { get; set; }
    internal void SetSuppressed(bool value)
    {
        suppressed = value;
        admittedSince = Environment.TickCount;
    }

    internal OverlayKeyboardShortcut(nint window, Action toggle, OverlayShortcutSettings? shortcut = null)
    {
        Shortcut = shortcut ?? new() { Enabled = true };
        if (!Shortcut.IsWellFormed()) throw new ArgumentException("Invalid keyboard shortcut.", nameof(shortcut));
        hotkeyId = Interlocked.Increment(ref nextId);
        if (hotkeyId > 0xBFFF) throw new InvalidOperationException("Keyboard registration identifiers are exhausted; restart WidgetRail.");
        this.window = window;
        monitor = new(window);
        monitor.WindowMessageReceived += (_, args) =>
        {
            if (disposed || !IsRegistered || args.Message.MessageId != HotkeyMessage ||
                (nint)args.Message.WParam != hotkeyId) return;
            args.Handled = true;
            args.Result = 0;
            var chord = unchecked((uint)(nint)args.Message.LParam);
            if ((chord & 0xFFFF) != (uint)Shortcut.Modifiers || (chord >> 16) != Shortcut.Key) return;
            if (unchecked(GetMessageTime() - admittedSince) < 0) return;
            // Never allow a managed exception to escape the native message callback.
            try
            {
                if (suppressed) CapturedWhileSuppressed?.Invoke(Shortcut);
                else toggle();
            }
            catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("keyboard-shortcut-toggle", error); }
        };
        IsRegistered = RegisterHotKey(window, hotkeyId, NoRepeat | (uint)Shortcut.Modifiers, (uint)Shortcut.Key) != 0;
        if (!IsRegistered)
        {
            ErrorCode = Marshal.GetLastPInvokeError();
            monitor.Dispose();
            Diagnostics.FrontendFailureLog.Current.Write("keyboard-shortcut-registration", new Win32Exception(ErrorCode),
                "Global keyboard shortcut unavailable; controller opening shortcut remains available.");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (IsRegistered)
        {
            if (UnregisterHotKey(window, hotkeyId) == 0)
                Diagnostics.FrontendFailureLog.Current.Write("keyboard-shortcut-unregister", new Win32Exception(Marshal.GetLastPInvokeError()));
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

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetMessageTime();
}
