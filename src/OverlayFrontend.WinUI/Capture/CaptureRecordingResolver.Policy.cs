using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal static partial class CaptureRecordingResolver
{
    internal const long AutomaticPixelBudget=2560L*1440;
    // This is deliberately a conservative ordering, not a claim that codec
    // generation predicts gaming performance. Promotion requires measured
    // consumer compatibility and overhead qualification.
    internal static IEnumerable<CaptureRecordingProfile> Candidates(RecordingOptions requested,
        CaptureEncodingAdapter adapter,int sourceWidth,int sourceHeight)
    {
        if (!requested.IsWellFormed() || !adapter.IsWellFormed() || sourceWidth<=0 || sourceHeight<=0 ||
            requested.Selection is null && requested.Encoding is null)
            yield break;
        var selection=requested.Selection;
        var native=selection?.Resolution==CaptureRecordingResolution.Source;
        var boundWidth=selection is null ? requested.MaximumWidth :
            selection.Resolution==CaptureRecordingResolution.Bounded ? selection.MaximumWidth!.Value : native ? sourceWidth :1920;
        var boundHeight=selection is null ? requested.MaximumHeight :
            selection.Resolution==CaptureRecordingResolution.Bounded ? selection.MaximumHeight!.Value : native ? sourceHeight :1080;
        var geometries=selection?.Resolution==CaptureRecordingResolution.Auto?
            new[]{AreaFit(sourceWidth,sourceHeight,8192),AreaFit(sourceWidth,sourceHeight,3840),AreaFit(sourceWidth,sourceHeight,1920)}.Distinct():
            new[]{native?((sourceWidth+1)/2*2,(sourceHeight+1)/2*2):Fit(sourceWidth,sourceHeight,boundWidth,boundHeight)};
        var fps=selection is null ? requested.FramesPerSecond : selection.FramesPerSecond??30;
        var formats=selection is null ? new[] {requested.Encoding!.PixelFormat} :
            new[] {selection.PixelFormat==CaptureVideoPixelFormat.Auto ? CaptureVideoPixelFormat.Yuv4208 : selection.PixelFormat};
        var codecs=selection is null ? new[] {requested.Encoding!.Codec} : selection.Codec==CaptureVideoCodec.Auto ?
            new[] {CaptureVideoCodec.H264,CaptureVideoCodec.Hevc} : new[] {selection.Codec};
        var rate=selection is null ? requested.Encoding!.RateControl : selection.RateControl==CaptureVideoRateControl.Auto ?
            selection.Quality is null ? CaptureVideoRateControl.ConstantBitrate : CaptureVideoRateControl.Quality : selection.RateControl;
        foreach(var dimensions in geometries)
        foreach(var codec in codecs)
        foreach(var format in formats)
        {
            if(dimensions.Item1>8192 || dimensions.Item2>8192 || (long)dimensions.Item1*dimensions.Item2>3840*2160)continue;
            if(!ConsumerCompatible(codec,format))continue;
            // The measured 1440p30 baseline is 12 Mbps. Preserve that quality
            // target on smaller sources; larger explicit formats scale by area.
            var bitrate=selection is null?requested.Bitrate:selection.Bitrate??Math.Clamp((int)(12_000_000d*
                Math.Max(1,(long)dimensions.Item1*dimensions.Item2/(double)AutomaticPixelBudget)*fps/30),3_000_000,40_000_000);
            var quality=rate==CaptureVideoRateControl.Quality ? selection?.Quality??requested.Encoding?.Quality??75 : (int?)null;
            var encoding=new CaptureEncodingOptions(codec,format,rate,quality);
            var key=$"{adapter.Id}:{adapter.EnvironmentId}:{dimensions.Item1}:{dimensions.Item2}:{fps}:{bitrate}:{encoding}";
            yield return new("encoding-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0,16)),
                dimensions.Item1,dimensions.Item2,fps,bitrate,bitrate,CaptureEncoderMode.Hardware)
                { Encoding=encoding,Adapter=adapter,EditingSupported=codec==CaptureVideoCodec.H264 && format==CaptureVideoPixelFormat.Yuv4208 };
        }
    }

    internal static bool ConsumerCompatible(CaptureVideoCodec codec,CaptureVideoPixelFormat format) =>
        // Native encode/mux/decode and production thumbnail/player paths are
        // qualified. HEVC remains explicit because the existing editor is AVC-only.
        codec==CaptureVideoCodec.H264 && format==CaptureVideoPixelFormat.Yuv4208 ||
        codec==CaptureVideoCodec.Hevc && format is CaptureVideoPixelFormat.Yuv4208 or CaptureVideoPixelFormat.Yuv42010;

    internal static bool AllowedByAutomaticPolicy(RecordingOptions requested,CaptureRecordingProfile profile)=>
        requested.Selection?.Codec!=CaptureVideoCodec.Auto || profile.EditingSupported==true;

    private static (int,int) Fit(int sourceWidth,int sourceHeight,int maximumWidth,int maximumHeight)
    {
        var scale=Math.Min(1d,Math.Min(maximumWidth/(double)sourceWidth,maximumHeight/(double)sourceHeight));
        return (Math.Max(2,(int)(sourceWidth*scale)/2*2),Math.Max(2,(int)(sourceHeight*scale)/2*2));
    }
    private static (int,int) AreaFit(int width,int height,int axisLimit)
    {
        var paddedWidth=(width+1)/2*2;var paddedHeight=(height+1)/2*2;
        var scale=Math.Min(1,Math.Min(Math.Sqrt(AutomaticPixelBudget/(double)((long)paddedWidth*paddedHeight)),
            axisLimit/(double)Math.Max(paddedWidth,paddedHeight)));
        return scale>=1?(paddedWidth,paddedHeight):
            (Math.Max(2,(int)(width*scale)/2*2),Math.Max(2,(int)(height*scale)/2*2));
    }
}
