using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Microsoft.UI.Dispatching;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI;

internal enum SystemTrayCommand : uint { Open = 1, Restart, RestartSilently, Quit }

/// <summary>Native notification-area icon; owns its icon and menus, never the borrowed window.</summary>
internal sealed partial class SystemTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x720;
    private const uint Add = 0, Delete = 2, SetVersion = 4;
    private const uint MessageFlag = 1, IconFlag = 2, TipFlag = 4, GuidFlag = 0x20, ShowTipFlag = 0x80;
    private readonly nint window;
    private readonly Guid identity;
    private readonly Action<SystemTrayCommand> dispatch;
    private readonly OwnedIcon icon;
    private readonly WindowMessageMonitor monitor;
    private readonly uint taskbarCreated;
    private readonly DispatcherQueueTimer registrationRetry;
    private int registrationAttempts;
    private bool disposed, showingMenu;

    internal SystemTrayIcon(nint window, string profile, Action<SystemTrayCommand> dispatch)
    {
        this.window = window;
        this.dispatch = dispatch;
        // Stable per profile across updates, without collisions with isolated profiles.
        identity = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            "WidgetRail.SystemTray|" + Path.GetFullPath(profile).TrimEnd('\\', '/').ToUpperInvariant())).AsSpan(0, 16));
        icon = new(LoadImageW(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
            1, 32, 32, 0x10)); // IMAGE_ICON, LR_LOADFROMFILE: caller owns the result.
        if (icon.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            icon.Dispose(); throw new Win32Exception(error);
        }
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        if (taskbarCreated == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            icon.Dispose(); throw new Win32Exception(error);
        }
        registrationRetry = DispatcherQueue.GetForCurrentThread().CreateTimer();
        registrationRetry.Interval = TimeSpan.FromSeconds(2);
        registrationRetry.Tick += RetryRegistration;
        monitor = new(window);
        monitor.WindowMessageReceived += (_, args) =>
        {
            if (disposed) return;
            try
            {
                if (taskbarCreated != 0 && args.Message.MessageId == taskbarCreated)
                { registrationAttempts = 0; TryRegister(); return; } // Explorer restarted; add our icon again.
                if (args.Message.MessageId != CallbackMessage) return;
                args.Handled = true; args.Result = 0;
                var notification = (uint)((nuint)args.Message.LParam & 0xffff);
                if (notification is 0x400 or 0x401) dispatch(SystemTrayCommand.Open); // NIN_SELECT / NIN_KEYSELECT
                else if (notification == 0x7b) // WM_CONTEXTMENU (mouse or keyboard)
                {
                    var position = (nuint)args.Message.WParam;
                    ShowMenu(unchecked((short)(position & 0xffff)), unchecked((short)((position >> 16) & 0xffff)));
                }
            }
            catch (Exception error) { Diagnostics.FrontendFailureLog.Current.Write("system-tray", error); }
        };
        TryRegister();
    }

    private void RetryRegistration(DispatcherQueueTimer sender, object args) => TryRegister();

    private void TryRegister()
    {
        if (disposed) return;
        try { Register(); registrationRetry.Stop(); registrationAttempts = 0; }
        catch (Exception error)
        {
            if (++registrationAttempts == 1) Diagnostics.FrontendFailureLog.Current.Write("system-tray-registration", error);
            // Startup at sign-in can precede Explorer's notification area.
            // Retry briefly, then wait for the next TaskbarCreated broadcast.
            if (registrationAttempts < 10) registrationRetry.Start();
            else registrationRetry.Stop();
        }
    }

    private unsafe NotifyIconData Data()
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData), Window = window, Id = 1,
            Flags = MessageFlag | IconFlag | TipFlag | GuidFlag | ShowTipFlag,
            Callback = CallbackMessage, Icon = icon.DangerousGetHandle(), Guid = identity, Version = 4,
        };
        const string tooltip = "WidgetRail";
        for (var i = 0; i < tooltip.Length; ++i) data.Tip[i] = tooltip[i];
        return data;
    }

    private void Register()
    {
        var data = Data();
        if (Shell_NotifyIconW(Add, ref data) == 0) throw new InvalidOperationException("Windows could not add the WidgetRail notification icon.");
        if (Shell_NotifyIconW(SetVersion, ref data) == 0)
        {
            Shell_NotifyIconW(Delete, ref data);
            throw new InvalidOperationException("Windows could not enable notification icon keyboard interaction.");
        }
    }

    private void ShowMenu(int x, int y)
    {
        if (showingMenu) return;
        showingMenu = true;
        try
        {
            using var menu = new OwnedMenu(CreatePopupMenu());
            if (menu.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            AddItem(SystemTrayCommand.Open, "Open WidgetRail");
            if (AppendMenuW(menu, 0x800, 0, null) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            AddItem(SystemTrayCommand.Restart, "Restart");
            AddItem(SystemTrayCommand.RestartSilently, "Restart silently");
            AddItem(SystemTrayCommand.Quit, "Quit");
            if (x == -1 && y == -1)
            {
                var identifier = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = window, Id = 1, Guid = identity };
                if (Shell_NotifyIconGetRect(ref identifier, out var bounds) >= 0) { x = bounds.Left; y = bounds.Bottom; }
                else if (GetCursorPos(out var point) != 0) { x = point.X; y = point.Y; }
            }
            // The foreground owner and trailing WM_NULL are required by the
            // shell's notification-area menu contract for outside-click dismissal.
            SetForegroundWindow(window);
            var selected = TrackPopupMenuEx(menu, 0x100 | 0x80 | 0x2 | 0x20, x, y, window, 0);
            PostMessageW(window, 0, 0, 0);
            if (!disposed)
            {
                if (selected is >= 1 and <= 4) dispatch((SystemTrayCommand)selected);
                else
                {
                    var data = Data();
                    Shell_NotifyIconW(3, ref data); // NIM_SETFOCUS: return keyboard focus to the notification area.
                }
            }

            void AddItem(SystemTrayCommand command, string text)
            {
                if (AppendMenuW(menu, 0, (nuint)command, text) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally { showingMenu = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        registrationRetry.Stop(); registrationRetry.Tick -= RetryRegistration;
        var data = Data();
        Shell_NotifyIconW(Delete, ref data);
        monitor.Dispose(); icon.Dispose();
    }

    // shellapi.h NOTIFYICONDATAW (976 bytes on x64); inline UTF-16 buffers
    // keep this ABI blittable for source-generated interop and trimmed builds.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id, Flags, Callback;
        internal nint Icon;
        internal fixed char Tip[128];
        internal uint State, StateMask;
        internal fixed char Info[256];
        internal uint Version;
        internal fixed char InfoTitle[64];
        internal uint InfoFlags;
        internal Guid Guid;
        internal nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier { internal uint Size; internal nint Window; internal uint Id; internal Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X, Y; }

    private sealed class OwnedIcon : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal OwnedIcon(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => DestroyIcon(handle) != 0;
    }
    private sealed class OwnedMenu : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal OwnedMenu(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => DestroyMenu(handle) != 0;
    }

    [LibraryImport("shell32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [LibraryImport("shell32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect bounds);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint RegisterWindowMessageW(string name);
    [LibraryImport("user32.dll", SetLastError = true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int AppendMenuW(OwnedMenu menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint TrackPopupMenuEx(OwnedMenu menu, uint flags, int x, int y, nint window, nint parameters);
    [LibraryImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetForegroundWindow(nint window);
    [LibraryImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DestroyIcon(nint icon);
    [LibraryImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DestroyMenu(nint menu);
}
