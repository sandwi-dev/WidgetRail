using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Previews;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal static partial class NativeWindowCapture
{
    internal static unsafe NativePreviewTarget ForegroundTarget()
    {
        var window = GetAncestor(GetForegroundWindow(), 2); // GA_ROOT: capture the application's top-level window.
        if (window == 0 || window == GetDesktopWindow() || window == GetShellWindow())
            throw new InvalidOperationException("No foreground application is available.");
        var identity = new NativePreviewTarget { Size = (uint)sizeof(NativePreviewTarget), Version = 1 };
        Marshal.ThrowExceptionForHR(PreviewNative.ReadIdentity((ulong)window, ref identity));
        if (identity.ProcessId == (uint)Environment.ProcessId)
            throw new InvalidOperationException("WidgetRail cannot be its own capture target.");
        return identity;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Result
    {
        internal uint Size, Version, Width, Height, Frames;
        internal int Error;
        internal double DurationSeconds;
    }
    internal sealed record CapturedResult(uint Width, uint Height, uint Frames, double DurationSeconds, NativeWindowPreviewTarget Target);
    internal static Task<CapturedResult> RunAsync(HostWindowCapture request, CancellationToken token) => Task.Run(() => Run(request, token), token);
    private static unsafe CapturedResult Run(HostWindowCapture request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var target = ForegroundTarget();
        var root = GCHandle.Alloc(token);
        try
        {
            var result = new Result { Size = (uint)sizeof(Result), Version = 1 };
            var status = Capture(in target, request.OutputPath, (uint)request.Kind,
                (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Current, GCHandle.ToIntPtr(root), ref result);
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(status);
            var captured = target.ToTarget(request.RequestId);
            return new(result.Width, result.Height, result.Frames, result.DurationSeconds,
                new(captured.Handle.ToString("X"), captured.ProcessId, captured.ProcessCreated.ToString("X16"), captured.ClassName));
        }
        finally { root.Free(); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Current(nint context)
    {
        try { return GCHandle.FromIntPtr(context).Target is CancellationToken token && !token.IsCancellationRequested ? 1 : 0; }
        catch { return 0; }
    }
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailCaptureWindow", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int Capture(in NativePreviewTarget target, string path, uint kind, nint authority, nint context, ref Result result);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll")] private static partial nint GetDesktopWindow();
    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
}
