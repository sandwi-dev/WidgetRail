using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

/// <summary>Describe requested choices before a source exists; never present Auto fallback fields as resolved settings.</summary>
internal static class CaptureRecordingSummary
{
    internal static string Video(RecordingOptions recording)
    {
        var requested=recording.Selection;
        var resolution=requested?.Resolution switch
        {
            CaptureRecordingResolution.Auto=>"Auto size",
            CaptureRecordingResolution.Source=>"Source size at start",
            CaptureRecordingResolution.Bounded=>$"Up to {requested.MaximumWidth} × {requested.MaximumHeight}",
            _=>$"Up to {recording.MaximumWidth} × {recording.MaximumHeight}",
        };
        var rate=requested is null?$"{recording.FramesPerSecond} fps":requested.FramesPerSecond is {} fps?$"{fps} fps":"Auto fps";
        var codec=(requested?.Codec??recording.Encoding?.Codec??CaptureVideoCodec.H264) switch
        {
            CaptureVideoCodec.Av1=>"AV1",CaptureVideoCodec.Hevc=>"HEVC",CaptureVideoCodec.H264=>"H.264",_=>"Auto codec",
        };
        return $"Video: {resolution} · {rate}\nEncoding: {codec} · {recording.Encoder}";
    }

    internal static string Audio(RecordingOptions recording)=>recording.Audio==CaptureAudioMode.Application
        ?"Audio: Application · Microphone off":"Audio: Off";
}
