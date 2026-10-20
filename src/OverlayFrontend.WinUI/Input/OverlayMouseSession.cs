using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Mouse-only admission for one overlay opening. The owned input window
/// paints nothing and never activates or captures input. Controller navigation
/// continues on the original surfaces, including their focus previews.</summary>
internal sealed partial class OverlayMouseSession : IDisposable
{
    // All accesses occur on the frontend dispatcher, including preview queries.
    private static readonly HashSet<nint> suppressedOwners = [];
    private const string GuardClassName = "WidgetRail.MouseGuard";
    private static ushort guardClass;
    private const uint GuardStyle = 0x08200080; // NOACTIVATE | TOOLWINDOW | NOREDIRECTIONBITMAP
    private const uint ShowWithoutActivation = 0x0050; // SHOWWINDOW | NOACTIVATE
    private readonly nint owner;
    private readonly DispatcherQueueTimer timer;
    private nint guard;
    private WindowMessageMonitor? monitor;
    private Point origin;
    private Rect bounds;
    private bool disposed;
    internal bool WaitingForMovement { get; private set; }
    internal static bool SuppressesHover(XamlRoot? root) => suppressedOwners.Count != 0 && root is not null && suppressedOwners.Contains(
        Microsoft.UI.Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId));

    internal OverlayMouseSession(nint owner, DispatcherQueue dispatcher)
    {
        this.owner = owner;
        timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += CheckMovement;
    }

    internal void Begin()
    {
        if (disposed || WaitingForMovement || GetCursorPos(out origin) == 0 || GetWindowRect(owner, out bounds) == 0) return;
        if (guard == 0)
        {
            EnsureGuardClass();
            guard = CreateWindowExW(GuardStyle, GuardClassName, "", 0x80000000 /* POPUP */, 0, 0, 0, 0, owner, 0, GetModuleHandleW(0), 0);
            if (guard == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            monitor = new(guard);
            monitor.WindowMessageReceived += OnMessage;
        }
        // Showing a native window can synchronously produce pointer-exit
        // notifications. Fence those before changing the native hit-test owner.
        WaitingForMovement = true;
        suppressedOwners.Add(owner);
        if (!PlaceGuard() || !WaitingForMovement) { End(); return; }
        SetCursor(0);
        timer.Start();
    }

    private bool PlaceGuard() => SetWindowPos(guard, -1 /* TOPMOST */, bounds.Left, bounds.Top,
        bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, ShowWithoutActivation) != 0;

    private void CheckMovement(DispatcherQueueTimer sender, object args)
    {
        if (!WaitingForMovement) return;
        if (GetCursorPos(out var position) == 0 || position.X != origin.X || position.Y != origin.Y ||
            IsWindowVisible(owner) == 0 || !PlatformInputPump.IsForegroundProcess()) { End(); return; }
        if (GetWindowRect(owner, out var current) == 0) { End(); return; }
        if (current.Left != bounds.Left || current.Top != bounds.Top || current.Right != bounds.Right || current.Bottom != bounds.Bottom)
        {
            bounds = current;
            if (!PlaceGuard()) End();
        }
    }

    private void OnMessage(object? sender, WindowMessageEventArgs args)
    {
        if (args.Message.MessageId == 0x0084) // WM_NCHITTEST
        { args.Handled = true; args.Result = 1; return; } // HTCLIENT
        if (!WaitingForMovement) return;
        if (args.Message.MessageId == 0x0020) // WM_SETCURSOR
        {
            SetCursor(0); args.Handled = true; args.Result = 1;
        }
        else if (args.Message.MessageId is >= 0x0200 and <= 0x020E)
        {
            args.Handled = true; args.Result = 0;
            CheckMovement(timer, EventArgs.Empty);
        }
        else if (args.Message.MessageId == 0x0021) // WM_MOUSEACTIVATE
        { args.Handled = true; args.Result = 3; } // MA_NOACTIVATE
    }

    internal void End()
    {
        timer.Stop();
        if (!WaitingForMovement) { RetireGuard(); return; }
        WaitingForMovement = false;
        suppressedOwners.Remove(owner);
        // Restore visibility before reopening mouse admission. No ShowCursor
        // counter, global hook, cursor relocation, or synthetic input is needed.
        SetCursor(LoadCursorW(0, 32512 /* IDC_ARROW */));
        ShowWindow(guard, 0);
        RetireGuard();
    }

    public void Dispose()
    {
        if (disposed) return;
        End(); disposed = true;
        timer.Tick -= CheckMovement;
        RetireGuard();
    }

    private void RetireGuard()
    {
        if (monitor is not null) { monitor.WindowMessageReceived -= OnMessage; monitor.Dispose(); monitor = null; }
        if (guard != 0) { DestroyWindow(guard); guard = 0; }
    }

    private static unsafe void EnsureGuardClass()
    {
        if (guardClass != 0) return;
        fixed (char* name = GuardClassName)
        {
            var registration = new WindowClass
            {
                Size = (uint)sizeof(WindowClass), Instance = GetModuleHandleW(0), Name = (nint)name,
                Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&GuardWindowProcedure,
            };
            guardClass = RegisterClassExW(in registration);
            if (guardClass == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
    // Process-lifetime class callback; no delegate can be collected while an HWND exists.
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint GuardWindowProcedure(nint window, uint message, nuint wparam, nint lparam) => DefWindowProcW(window, message, wparam, lparam);

    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowClass
    {
        internal uint Size, Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background, Menu, Name, SmallIcon;
    }
    [LibraryImport("user32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial ushort RegisterClassExW(in WindowClass registration);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint DefWindowProcW(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("kernel32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint GetModuleHandleW(nint moduleName);
    // owner is borrowed; only the guard HWND created by this object is destroyed.
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateWindowExW(uint extended, string className, string name, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int DestroyWindow(nint window);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetWindowRect(nint window, out Rect rect);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int IsWindowVisible(nint window);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ShowWindow(nint window, int show);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint SetCursor(nint cursor);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint LoadCursorW(nint instance, nint name);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetCursorPos(out Point point);
}
