using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed class NativeWebNotification(NativeWebNotifications owner, ulong id, string origin, string tag, string title, string body, uint flags)
{
    internal string Origin { get; } = origin;
    internal string Tag { get; } = tag;
    internal string Title { get; } = title;
    internal string Body { get; } = body;
    internal bool Renotify => (flags & 2) != 0;
    internal event Action? CloseRequested;
    internal void Withdraw() => CloseRequested?.Invoke();
    internal void ReportShown() => owner.Command(id, 0);
    internal void ReportClicked() => owner.Command(id, 1);
    internal void ReportClosed() => owner.Command(id, 2);
    internal void Release() { CloseRequested = null; owner.Release(id); }
}

/// <summary>Native COM owns notification/args lifetimes; managed code receives only copied metadata and IDs.</summary>
internal sealed partial class NativeWebNotifications : IDisposable
{
    private ulong handle;
    private readonly NotificationCallback callback;
    private readonly Action<NativeWebNotification> received;
    private readonly Dictionary<ulong, NativeWebNotification> notices = [];

    internal NativeWebNotifications(CoreWebView2 core, Action<NativeWebNotification> received)
    {
        this.received = received;
        callback = OnEvent;
        // Borrow the WinRT core only for this call. The documented
        // ICoreWebView2Interop2::GetComICoreWebView2 gives native code its own
        // COM reference; no reflection or private projection-cache disposal.
        var native = (WinRT.IWinRTObject)(object)core;
        Marshal.ThrowExceptionForHR(Subscribe(native.NativeObject.ThisPtr, Marshal.GetFunctionPointerForDelegate(callback), out handle));
        GC.KeepAlive(core);
    }
    private void OnEvent(ulong id, uint kind, nint origin, nint tag, nint title, nint body, uint flags)
    {
        try
        {
            if (handle == 0) return;
            if (kind == 2) { if (notices.TryGetValue(id, out var item)) item.Withdraw(); return; }
            if (kind != 1) return;
            var notification = new NativeWebNotification(this, id, Read(origin), Read(tag), Read(title), Read(body), flags);
            notices.Add(id, notification);
            try { received(notification); }
            catch { notification.Release(); throw; }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Diagnostics.FrontendFailureLog.Current.Write("web-notification", null, $"Notification handling failed: {error.GetType().Name}");
        }
    }
    private static string Read(nint text) => text == 0 ? "" : Marshal.PtrToStringUni(text) ?? "";
    internal void Command(ulong id, uint command)
    {
        if (handle == 0 || !notices.ContainsKey(id)) throw new ObjectDisposedException(nameof(NativeWebNotification));
        Marshal.ThrowExceptionForHR(SendCommand(handle, id, command));
    }
    internal void Release(ulong id)
    {
        if (!notices.Remove(id) || handle == 0) return;
        Marshal.ThrowExceptionForHR(SendCommand(handle, id, 3));
    }
    public void Dispose()
    {
        if (handle == 0) return;
        Marshal.ThrowExceptionForHR(Unsubscribe(handle));
        handle = 0; notices.Clear(); GC.KeepAlive(callback);
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void NotificationCallback(ulong id, uint kind, nint origin, nint tag, nint title, nint body, uint flags);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailWebNotificationsSubscribe")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static partial int Subscribe(nint core, nint callback, out ulong handle);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailWebNotificationCommand")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static partial int SendCommand(ulong handle, ulong id, uint command);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailWebNotificationsUnsubscribe")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static partial int Unsubscribe(ulong handle);
}
