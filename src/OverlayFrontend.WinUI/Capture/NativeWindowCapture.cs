using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Previews;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal static partial class NativeWindowCapture
{
    internal static CaptureCapabilities Capabilities() => Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported()
        ? new(true, (CaptureImageFormat[])[CaptureImageFormat.Png],
            (CaptureRecordingProfile[])[new("720p15-software", 1280, 720, 15, 1_000_000, 6_000_000, CaptureEncoderMode.Software)],
            (CaptureAudioMode[])[CaptureAudioMode.None], true, CaptureLimits.MaximumRecordingSeconds)
        : CaptureCapabilities.Unavailable;
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
    [StructLayout(LayoutKind.Sequential)]
    private struct Options
    {
        internal uint Size, Version, Kind, MaximumWidth, MaximumHeight, FramesPerSecond, Bitrate, DurationSeconds, IncludeCursor, Encoder;
        internal ulong MaximumBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ConfiguredResult
    {
        internal uint Size, Version, Width, Height, Frames;
        internal int Error;
        internal double DurationSeconds;
        internal uint SourceWidth, SourceHeight, MissedFrames, Encoder;
    }
    private sealed record Control(CancellationToken Cancel, CancellationToken Stop);
    internal sealed record CapturedResult(uint Width, uint Height, uint Frames, double DurationSeconds, NativeWindowPreviewTarget Target)
    {
        internal CaptureMediaProperties? Properties { get; init; }
    }
    internal static Task<CapturedResult> RunAsync(HostWindowCapture request, CancellationToken token, CancellationToken stop = default) => Task.Run(() => Run(request, token, stop), token);
    private static unsafe CapturedResult Run(HostWindowCapture request, CancellationToken token, CancellationToken stop)
    {
        token.ThrowIfCancellationRequested();
        var target = ForegroundTarget();
        var root = GCHandle.Alloc(new Control(token, stop));
        try
        {
            var captured = target.ToTarget(request.RequestId);
            var identity = new NativeWindowPreviewTarget(captured.Handle.ToString("X"), captured.ProcessId, captured.ProcessCreated.ToString("X16"), captured.ClassName);
            if (request.Options is { } settings)
            {
                var validation = Capabilities().Validate(settings);
                if (!validation.Supported) throw new BrokerException(validation.ErrorCode!, "The requested capture settings are no longer available.");
                var video = settings.Recording;
                var options = new Options { Size = (uint)sizeof(Options), Version = 2, Kind = (uint)request.Kind,
                    MaximumWidth = (uint)(video?.MaximumWidth ?? settings.Screenshot?.MaximumWidth ?? 0),
                    MaximumHeight = (uint)(video?.MaximumHeight ?? settings.Screenshot?.MaximumHeight ?? 0),
                    FramesPerSecond = (uint)(video?.FramesPerSecond ?? 15), Bitrate = (uint)(video?.Bitrate ?? 3_000_000),
                    DurationSeconds = (uint)(video?.MaximumDurationSeconds ?? 5), IncludeCursor = settings.IncludeCursor ? 1u : 0,
                    Encoder = (uint)(video?.Encoder ?? CaptureEncoderMode.Software), MaximumBytes = (ulong)settings.MaximumBytes };
                var output = new ConfiguredResult { Size = (uint)sizeof(ConfiguredResult), Version = 2 };
                var code = CaptureConfigured(in target, request.OutputPath, in options,
                    (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Current, GCHandle.ToIntPtr(root), ref output);
                token.ThrowIfCancellationRequested();
                if (code == unchecked((int)0x80004004) && stop.IsCancellationRequested) throw new OperationCanceledException(stop);
                Marshal.ThrowExceptionForHR(code);
                return new(output.Width, output.Height, output.Frames, output.DurationSeconds, identity) {
                    Properties = new((int)output.SourceWidth, (int)output.SourceHeight, video?.FramesPerSecond ?? 0,
                        video is null ? 0 : (int)output.Frames, (int)output.MissedFrames,
                        video is null ? null : (CaptureEncoderMode)output.Encoder, CaptureAudioMode.None) };
            }
            var result = new Result { Size = (uint)sizeof(Result), Version = 1 };
            var status = Capture(in target, request.OutputPath, request.Kind == WindowCaptureKind.Screenshot && request.Resolution == CaptureImageResolution.Original ? 3u : (uint)request.Kind,
                (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Current, GCHandle.ToIntPtr(root), ref result);
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(status);
            return new(result.Width, result.Height, result.Frames, result.DurationSeconds, identity);
        }
        finally { root.Free(); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Current(nint context)
    {
        try { return GCHandle.FromIntPtr(context).Target is Control state && !state.Cancel.IsCancellationRequested ? state.Stop.IsCancellationRequested ? 2 : 1 : 0; }
        catch { return 0; }
    }
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailCaptureWindow", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int Capture(in NativePreviewTarget target, string path, uint kind, nint authority, nint context, ref Result result);
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailCaptureWindowConfigured", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CaptureConfigured(in NativePreviewTarget target, string path, in Options options, nint authority, nint context, ref ConfiguredResult result);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll")] private static partial nint GetDesktopWindow();
    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
}
