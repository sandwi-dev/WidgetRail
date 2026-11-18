using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal static partial class CaptureRecordingResolver
{
    internal static async Task<ResolvedCaptureRecording?> ResolveAsync(NativeWindowPreviewTarget target,
        CaptureReplaySettings requested,CancellationToken token)
    {
        var recording=requested.Recording;
        if(recording.Selection is null && recording.Encoding is null)return null;
        for(var attempt=0;attempt<2;++attempt)
        {
            token.ThrowIfCancellationRequested();
            var environment=await Task.Run(NativeWindowCapture.ReadEncodingEnvironment,token).ConfigureAwait(false);
            if(!await NativeWindowCapture.EncodingCaptureAvailableAsync().WaitAsync(token).ConfigureAwait(false))
                throw new ReplayBackendException(CaptureReplayIssue.ConfigurationUnavailable);
            var source=await Task.Run(()=>NativeWindowCapture.ReadEncodingSourceSize(target),token).ConfigureAwait(false);
            var device=environment.Devices.Single(value=>value.Luid==environment.DefaultAdapter);
            foreach(var profile in Candidates(recording,device.Adapter,source.Width,source.Height))
            {
                if(!AllowedByAutomaticPolicy(recording,profile))continue;
                if(!await NativeWindowCapture.VerifyEncodingAsync(profile,device,recording.Audio==CaptureAudioMode.Application).WaitAsync(token).ConfigureAwait(false))continue;
                if(NativeWindowCapture.ReadEncodingEnvironment().Id!=environment.Id)break;
                var resolved=recording with {Selection=null,Encoding=profile.Encoding,MaximumWidth=profile.MaximumWidth,MaximumHeight=profile.MaximumHeight,
                    FramesPerSecond=profile.FramesPerSecond,Bitrate=profile.MaximumBitrate};
                var configuration=new CaptureRecordingConfiguration(resolved,device.Adapter,source.Width,source.Height) {EditingSupported=profile.EditingSupported};
                if(!configuration.Satisfies(recording))throw new ReplayBackendException(CaptureReplayIssue.HardwareEncoderUnavailable);
                return new(configuration,device.Luid);
            }
            if(NativeWindowCapture.ReadEncodingEnvironment().Id==environment.Id)break;
        }
        throw new ReplayBackendException(CaptureReplayIssue.ConfigurationUnavailable);
    }

    internal static async Task<CaptureCapabilities> DiscoverAsync(CaptureRecordingQuery query)
    {
        if(!query.IsWellFormed() || query.Recording is not {} requested)throw new ArgumentException("Explicit selection query required.");
        var environment=await Task.Run(NativeWindowCapture.ReadEncodingEnvironment).ConfigureAwait(false);
        if(!await NativeWindowCapture.EncodingCaptureAvailableAsync().ConfigureAwait(false))return CaptureCapabilities.Unavailable;
        var device=environment.Devices.Single(value=>value.Luid==environment.DefaultAdapter);
        var selection=requested.Selection;
        var width=selection?.Resolution==CaptureRecordingResolution.Bounded ? selection.MaximumWidth!.Value :
            selection is null ? requested.MaximumWidth :1920;
        var height=selection?.Resolution==CaptureRecordingResolution.Bounded ? selection.MaximumHeight!.Value :
            selection is null ? requested.MaximumHeight :1080;
        var frameRates=selection is {FramesPerSecond:null} ? query.DiscoverAlternatives?new[]{30,15,60}:new[]{30} :new[]{selection?.FramesPerSecond??requested.FramesPerSecond};
        var requests=new List<(RecordingOptions Options,int Width,int Height)>();
        if(query.DiscoverAlternatives && selection==new CaptureRecordingSelection())
        {
            // A small facet survey, not a Cartesian benchmark. Exact changed
            // constraints still get their own cached probe before admission.
            foreach(var (w,h,fps) in new[]{(1920,1080,30),(1920,1080,60),(1280,720,15),(2560,1440,30),(2560,1440,60),(3840,2160,30)})
                requests.Add((requested with{Selection=selection with{Resolution=CaptureRecordingResolution.Bounded,MaximumWidth=w,MaximumHeight=h,FramesPerSecond=fps}},w,h));
            requests.Add((requested with{Selection=selection with{Resolution=CaptureRecordingResolution.Bounded,MaximumWidth=1920,MaximumHeight=1080,
                FramesPerSecond=30,Codec=CaptureVideoCodec.Hevc,PixelFormat=CaptureVideoPixelFormat.Yuv42010}},1920,1080));
            requests.Add((requested with{Selection=selection with{Resolution=CaptureRecordingResolution.Bounded,MaximumWidth=1920,MaximumHeight=1080,
                FramesPerSecond=30,Codec=CaptureVideoCodec.Hevc,PixelFormat=CaptureVideoPixelFormat.Yuv4448}},1920,1080));
            foreach(var rate in new[]{CaptureVideoRateControl.VariableBitrate,CaptureVideoRateControl.Quality})
                requests.Add((requested with{Selection=selection with{Resolution=CaptureRecordingResolution.Bounded,MaximumWidth=1920,MaximumHeight=1080,
                    FramesPerSecond=30,Codec=CaptureVideoCodec.H264,PixelFormat=CaptureVideoPixelFormat.Yuv4208,RateControl=rate,
                    Quality=rate==CaptureVideoRateControl.Quality?75:null}},1920,1080));
        }
        else foreach(var rate in frameRates)
            requests.Add((selection is null ? requested : requested with {Selection=selection with {FramesPerSecond=rate}},width,height));
        var profiles=new List<CaptureRecordingProfile>();
        foreach(var (candidateRequest,w,h) in requests)
        {
            foreach(var profile in Candidates(candidateRequest,device.Adapter,w,h))
            {
                if(!query.DiscoverAlternatives && !AllowedByAutomaticPolicy(requested,profile))continue;
                if(await NativeWindowCapture.VerifyEncodingAsync(profile,device,requested.Audio==CaptureAudioMode.Application).ConfigureAwait(false))profiles.Add(profile);
            }
        }
        var applicationAudio=profiles.Count>0 && (query.DiscoverAlternatives || requested.Audio==CaptureAudioMode.Application) &&
            await NativeWindowCapture.EncodingApplicationAudioAsync().ConfigureAwait(false);
        // A changed driver/device must never be represented by old successful
        // completions, even when the expensive work was already in flight.
        if(NativeWindowCapture.ReadEncodingEnvironment().Id!=environment.Id)
            return new(true,(CaptureImageFormat[])[CaptureImageFormat.Png],Array.Empty<CaptureRecordingProfile>(),(CaptureAudioMode[])[CaptureAudioMode.None],true,CaptureLimits.MaximumRecordingSeconds);
        return new(true,(CaptureImageFormat[])[CaptureImageFormat.Png],profiles,
            applicationAudio ? (CaptureAudioMode[])[CaptureAudioMode.None,CaptureAudioMode.Application] : (CaptureAudioMode[])[CaptureAudioMode.None],
            true,CaptureLimits.MaximumRecordingSeconds);
    }
}
