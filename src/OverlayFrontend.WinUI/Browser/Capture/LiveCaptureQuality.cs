using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser.Capture;

/// <summary>Bounded local transport options, not provider entitlement or guaranteed delivered FPS.
/// Width is a source-aspect envelope; a nominal 720p ultrawide stream can be 2560x720.</summary>
internal sealed record LiveCaptureQuality(string Id, string Label, uint MaximumWidth, uint MaximumHeight, uint FrameRate)
{
    internal const uint MaximumVideoBytes=5120u*1440u*4u;
    internal WebCaptureContentPreference ContentPreference { get; init; }
    internal WebCaptureStreamOptions Options=>new((int)MaximumHeight,(int)FrameRate){ContentPreference=ContentPreference};
    internal string ContentHint=>ContentPreference==WebCaptureContentPreference.Motion?"motion":"detail";
    internal static IReadOnlyList<LiveCaptureQuality> Supported { get; } = Array.AsReadOnly(
        (from size in new[]{(Width:1706u,Height:480u),(Width:2560u,Height:720u),(Width:3840u,Height:1080u),(Width:5120u,Height:1440u)}
         from rate in new[]{15u,30u,60u}
         select new LiveCaptureQuality($"{size.Height}p{rate}",$"{size.Height}p · {rate} FPS",size.Width,size.Height,rate)).ToArray());
    internal static LiveCaptureQuality Default => FromId("720p30");
    internal static IReadOnlyList<LiveCaptureQuality> Allowed(IReadOnlyList<WebCaptureStreamOptions>? permitted)=>
        Supported.Where(quality=>(permitted??WebCaptureStreamOptions.LegacyQualities).Any(quality.Options.IsSameQuality)).ToArray();
    internal static LiveCaptureQuality FromId(string id) => Supported.SingleOrDefault(p=>p.Id==id)
        ?? throw new ArgumentException("Unsupported capture quality.",nameof(id));
    internal static LiveCaptureQuality FromOptions(WebCaptureStreamOptions options)=>options.IsWellFormed()
        ?FromId($"{options.Height}p{options.FrameRate}") with{ContentPreference=options.ContentPreference}
        :throw new ArgumentException("Unsupported capture options.",nameof(options));
    internal bool IsSupported => Enum.IsDefined(ContentPreference)&&Supported.Any(quality=>quality.Id==Id&&quality.MaximumWidth==MaximumWidth&&quality.MaximumHeight==MaximumHeight&&quality.FrameRate==FrameRate);
    public override string ToString()=>Label;
}
