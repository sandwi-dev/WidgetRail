using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Previews;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal static partial class NativeWindowCapture
{
    private static readonly CaptureEncoderDiscovery discovery = new(
        () => Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported(), ProbeProfile, ProbeApplicationAudio);
    internal static Task<CaptureCapabilities> GetCapabilitiesAsync()
    {
        RefreshEncodingEnvironment();
        return discovery.DiscoverAsync();
    }
    internal static Task<CaptureCapabilities> GetRecordingCapabilitiesAsync(CaptureRecordingQuery query)
    {
        if(query.Recording is {Selection:not null} or {Encoding:not null})return CaptureRecordingResolver.DiscoverAsync(query);
        RefreshEncodingEnvironment();
        return discovery.DiscoverAsync(query);
    }
    private static void RefreshEncodingEnvironment()
    {
        try { _=ReadEncodingEnvironment(); }
        // Legacy capture exports remain independently usable. Advanced selections
        // require the new inventory and do not take this compatibility fallback.
        catch(Exception error) when(error is EntryPointNotFoundException or DllNotFoundException or BadImageFormatException or System.Runtime.InteropServices.COMException) { }
    }
    internal static CaptureCapabilities ImageCapabilities() => Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported()
        ? new(true, (CaptureImageFormat[])[CaptureImageFormat.Png], Array.Empty<CaptureRecordingProfile>(),
            (CaptureAudioMode[])[CaptureAudioMode.None], true, CaptureLimits.MaximumRecordingSeconds)
        : CaptureCapabilities.Unavailable;
    private static unsafe bool ProbeApplicationAudio()
    {
        var result = new EncodingProbe { Size = (uint)sizeof(EncodingProbe), Version = 1 };
        return ProbeAudio(ref result) >= 0 && result.Supported == 1;
    }
    private static unsafe bool ProbeProfile(CaptureRecordingProfile profile, int bitrate, bool audio)
    {
        var options = new Options { Size = (uint)sizeof(Options), Version = 3, Kind = 2,
            MaximumWidth = (uint)profile.MaximumWidth, MaximumHeight = (uint)profile.MaximumHeight,
            FramesPerSecond = (uint)profile.FramesPerSecond, Bitrate = (uint)bitrate,
            DurationSeconds = 1, Encoder = (uint)profile.Encoder, MaximumBytes = CaptureLimits.MaximumAttachmentBytes,
            AudioMode = audio ? (uint)CaptureAudioMode.Application : 0 };
        var result = new EncodingProbe { Size = (uint)sizeof(EncodingProbe), Version = 1 };
        var available = ProbeEncoding(in options, ref result) >= 0 && result.Supported == 1 &&
            (result.Hardware == 1) == (profile.Encoder == CaptureEncoderMode.Hardware);
        if (result.ElapsedMilliseconds >= 500)
            Diagnostics.FrontendFailureLog.Current.Write("capture-discovery-slow", null,
                $"profile={profile.Id} audio={audio} supported={available} elapsedMs={result.ElapsedMilliseconds:F0}");
        return available;
    }
    internal static unsafe NativePreviewTarget ForegroundTarget()
    {
        var window = GetAncestor(GetForegroundWindow(), 2); // GA_ROOT: capture the application's top-level window.
        if (window == 0 || window == GetDesktopWindow() || window == GetShellWindow())
            throw new BrokerException("capture_target_unavailable", "No foreground application is available.");
        var identity = new NativePreviewTarget { Size = (uint)sizeof(NativePreviewTarget), Version = 1 };
        Marshal.ThrowExceptionForHR(PreviewNative.ReadIdentity((ulong)window, ref identity));
        if (identity.ProcessId == (uint)Environment.ProcessId)
            throw new BrokerException("capture_target_unavailable", "WidgetRail cannot be its own capture target.");
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
        internal uint AudioMode, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ConfiguredResult
    {
        internal uint Size, Version, Width, Height, Frames;
        internal int Error;
        internal double DurationSeconds;
        internal uint SourceWidth, SourceHeight, MissedFrames, Encoder;
        internal uint AudioMode, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EncodingProbe
    {
        internal uint Size, Version, Supported, Hardware;
        internal int Error;
        internal uint Reserved;
        internal double ElapsedMilliseconds;
    }
    private sealed record Control(CancellationToken Cancel, CancellationToken Stop);
    internal sealed record CapturedResult(uint Width, uint Height, uint Frames, double DurationSeconds, NativeWindowPreviewTarget Target)
    {
        internal CaptureMediaProperties? Properties { get; init; }
        internal CaptureRecordingConfiguration? ResolvedRecording { get; init; }
    }
    internal sealed record FixedCaptureTarget(NativeWindowPreviewTarget Target, nint Overlay);
    internal static async Task<CapturedResult> RunAsync(HostWindowCapture request, CancellationToken token, CancellationToken stop = default,
        FixedCaptureTarget? fixedTarget = null)
    {
        if (request.Options is { } options)
        {
            var available = options.Recording is null ? ImageCapabilities() : await GetRecordingCapabilitiesAsync(new(options.Recording)).WaitAsync(token).ConfigureAwait(false);
            var validation = available.Validate(options);
            if (!validation.Supported) throw new BrokerException(validation.ErrorCode!, "The requested capture settings are unavailable.");
        }
        return await Task.Run(() => Run(request, token, stop, fixedTarget), token).ConfigureAwait(false);
    }
    private static NativePreviewTarget SelectCaptureTarget(FixedCaptureTarget? fixedTarget)
    {
#if ENABLE_WIDGET_VALIDATION
        if (TryGetFixtureCaptureTarget(out var fixture, fixedTarget)) return fixture;
#endif
        return fixedTarget is null ? ForegroundTarget() : ToNative(fixedTarget.Target);
    }

    private static NativePreviewTarget ToNative(NativeWindowPreviewTarget target)
    {
        return NativePreviewTarget.From(new("", Convert.ToUInt64(target.Handle, 16), target.ProcessId,
            Convert.ToUInt64(target.ProcessCreated, 16), target.ClassName));
    }

    private static unsafe CapturedResult Run(HostWindowCapture request, CancellationToken token, CancellationToken stop, FixedCaptureTarget? fixedTarget)
    {
        token.ThrowIfCancellationRequested();
        if (fixedTarget is { Overlay: not 0 } && (request.Kind != WindowCaptureKind.Screenshot ||
            request.Options?.Preparation.DelaySeconds != 0))
            throw new InvalidOperationException("Invalid immediate screenshot request.");
        var target = SelectCaptureTarget(fixedTarget);
        var root = GCHandle.Alloc(new Control(token, stop));
        try
        {
            var captured = target.ToTarget(request.RequestId);
            var identity = new NativeWindowPreviewTarget(captured.Handle.ToString("X"), captured.ProcessId, captured.ProcessCreated.ToString("X16"), captured.ClassName);
            if (request.Options is { } settings)
            {
                var video = settings.Recording;
                ResolvedCaptureRecording? resolved=null;
                if(video is {Selection:not null} or {Encoding:not null})
                {
                    resolved=CaptureRecordingResolver.ResolveAsync(identity,new CaptureReplaySettings {Recording=video},token).GetAwaiter().GetResult();
                    if(resolved is null)throw new BrokerException("capture_encoding_unsupported","The requested encoder configuration is unavailable.");
                    video=resolved.Configuration.Recording;
                    var exact=new CaptureRecordingProfile("resolved",video.MaximumWidth,video.MaximumHeight,video.FramesPerSecond,
                        video.Bitrate,video.Bitrate,video.Encoder){Encoding=video.Encoding,Adapter=resolved.Configuration.Adapter};
                    var admission=new CaptureCapabilities(true,(CaptureImageFormat[])[CaptureImageFormat.Png],(CaptureRecordingProfile[])[exact],
                        video.Audio==CaptureAudioMode.None?(CaptureAudioMode[])[CaptureAudioMode.None]:(CaptureAudioMode[])[CaptureAudioMode.None,video.Audio],true,CaptureLimits.MaximumRecordingSeconds)
                        .Validate(settings with{Recording=video});
                    if(!admission.Supported)throw new BrokerException(admission.ErrorCode!,"The resolved recording exceeds the requested output budget.");
                }
                var options = new Options { Size = (uint)sizeof(Options), Version = 3, Kind = (uint)request.Kind,
                    MaximumWidth = (uint)(video?.MaximumWidth ?? settings.Screenshot?.MaximumWidth ?? 0),
                    MaximumHeight = (uint)(video?.MaximumHeight ?? settings.Screenshot?.MaximumHeight ?? 0),
                    FramesPerSecond = (uint)(video?.FramesPerSecond ?? 15), Bitrate = (uint)(video?.Bitrate ?? 3_000_000),
                    DurationSeconds = (uint)(video?.MaximumDurationSeconds ?? 5), IncludeCursor = settings.IncludeCursor ? 1u : 0,
                    Encoder = (uint)(video?.Encoder ?? CaptureEncoderMode.Software), MaximumBytes = (ulong)settings.MaximumBytes,
                    AudioMode = (uint)(video?.Audio ?? CaptureAudioMode.None) };
                var output = new ConfiguredResult { Size = (uint)sizeof(ConfiguredResult), Version = 3 };
                int code;
                if (fixedTarget is { Overlay: not 0 })
                    code = CaptureOverlayScreenshot(in target, fixedTarget.Overlay, request.OutputPath, in options,
                        (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Current, GCHandle.ToIntPtr(root), ref output);
                else if(resolved is null)
                    code = settings.OutputPurpose == CaptureOutputPurpose.LibraryRecording
                        ? CaptureLibraryRecording(in target, request.OutputPath, in options, 0, 0, 0,
                            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Current, GCHandle.ToIntPtr(root), ref output)
                        : CaptureConfigured(in target, request.OutputPath, in options,
                            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Current, GCHandle.ToIntPtr(root), ref output);
                else
                {
                    var encoding=video!.Encoding!;
                    var selected=new EncodingSelectionNative {Size=32,Version=1,Codec=(uint)encoding.Codec,PixelFormat=(uint)encoding.PixelFormat,
                        RateControl=(uint)encoding.RateControl,Quality=(uint)(encoding.Quality??75),AdapterLuid=resolved.NativeAdapterLuid};
                    code=settings.OutputPurpose == CaptureOutputPurpose.LibraryRecording
                        ? CaptureLibraryRecording(in target,request.OutputPath,in options,(nint)(&selected),
                            (uint)resolved.Configuration.SourceWidth,(uint)resolved.Configuration.SourceHeight,
                            (nint)(delegate* unmanaged[Stdcall]<nint,int>)&Current,GCHandle.ToIntPtr(root),ref output)
                        : CaptureConfiguredEncoding(in target,request.OutputPath,in options,in selected,
                            (uint)resolved.Configuration.SourceWidth,(uint)resolved.Configuration.SourceHeight,
                            (nint)(delegate* unmanaged[Stdcall]<nint,int>)&Current,GCHandle.ToIntPtr(root),ref output);
                }
                token.ThrowIfCancellationRequested();
                if (code == unchecked((int)0x80004004) && stop.IsCancellationRequested) throw new OperationCanceledException(stop);
                Marshal.ThrowExceptionForHR(code);
                return new(output.Width, output.Height, output.Frames, output.DurationSeconds, identity) {
                    ResolvedRecording = resolved?.Configuration,
                    Properties = new((int)output.SourceWidth, (int)output.SourceHeight, video?.FramesPerSecond ?? 0,
                        video is null ? 0 : (int)output.Frames, (int)output.MissedFrames,
                        video is null ? null : (CaptureEncoderMode)output.Encoder, (CaptureAudioMode)output.AudioMode) };
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
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailCaptureOverlayScreenshot", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CaptureOverlayScreenshot(in NativePreviewTarget target, nint overlay, string path, in Options options,
        nint authority, nint context, ref ConfiguredResult result);
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailCaptureWindowConfigured", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CaptureConfigured(in NativePreviewTarget target, string path, in Options options, nint authority, nint context, ref ConfiguredResult result);
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailCaptureWindowLibraryV1", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CaptureLibraryRecording(in NativePreviewTarget target, string path, in Options options,
        nint selection, uint expectedSourceWidth, uint expectedSourceHeight, nint authority, nint context, ref ConfiguredResult result);
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailProbeCaptureEncoding")]
    private static partial int ProbeEncoding(in Options options, ref EncodingProbe result);
    [LibraryImport("WinUiWindowPreviewNative.dll", EntryPoint = "WrailProbeCaptureAudio")]
    private static partial int ProbeAudio(ref EncodingProbe result);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll")] private static partial nint GetDesktopWindow();
    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
}
