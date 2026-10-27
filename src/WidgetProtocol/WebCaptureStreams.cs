using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WebCaptureContentPreference>))]
public enum WebCaptureContentPreference { Detail, Motion }

/// <summary>Supported local capture settings. Height is a ceiling; source aspect ratio is preserved.</summary>
public sealed record WebCaptureStreamOptions(int Height = 720, int FrameRate = 30)
{
    /// <summary>Browser encoding preference; not a promise of resolution or frame-rate delivery.</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public WebCaptureContentPreference ContentPreference { get; init; }
    public bool IsWellFormed() => Height is 480 or 720 or 1080 or 1440 && FrameRate is 15 or 30 or 60 && Enum.IsDefined(ContentPreference);
    /// <summary>Compares the resolution/FPS mode independently of the user's content preference.</summary>
    public bool IsSameQuality(WebCaptureStreamOptions? other) => other is not null && Height==other.Height && FrameRate==other.FrameRate;
    /// <summary>Compatibility choices for preparation without provider restrictions.</summary>
    public static IReadOnlyList<WebCaptureStreamOptions> LegacyQualities { get; } =
        Array.AsReadOnly(new WebCaptureStreamOptions[]{new(480,15),new(480,30),new(720,15),new(720,30)});
}

/// <summary>The destination is an owned backend session. The user chooses the source and audio in the host.</summary>
public sealed record WebCaptureStreamRequest(WebSessionReference Session, WebCaptureStreamOptions Options)
{
    public const int MaximumAllowedQualities = 12;
    private IReadOnlyList<WebCaptureStreamOptions>? allowedQualities;
    /// <summary>Provider-approved choices, intersected with host support. Null retains legacy choices.</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WebCaptureStreamOptions>? AllowedQualities
    {
        get=>allowedQualities;
        init=>allowedQualities=value is null?null:Array.AsReadOnly(value.ToArray());
    }
    public bool IsWellFormed() => Session?.IsWellFormed()==true && Options?.IsWellFormed()==true &&
        (AllowedQualities is null ? WebCaptureStreamOptions.LegacyQualities.Any(Options.IsSameQuality) :
            AllowedQualities.Count is >0 and <=MaximumAllowedQualities &&
            AllowedQualities.All(quality=>quality?.IsWellFormed()==true) &&
            AllowedQualities.DistinctBy(quality=>(quality.Height,quality.FrameRate)).Count()==AllowedQualities.Count && AllowedQualities.Any(Options.IsSameQuality));
}

/// <summary>One source selection, bound to one worker-owned browser session. Never contains a native handle.</summary>
public sealed record WebCaptureStreamTicket(WebSessionReference Session, string Id)
{
    public bool IsWellFormed() => Session?.IsWellFormed()==true && Id is {Length:32} && Id.All(char.IsAsciiHexDigit);
}

[JsonConverter(typeof(JsonStringEnumConverter<WebCaptureStreamPhase>))]
public enum WebCaptureStreamPhase { Unavailable, Cancelled, Prepared, Capturing, Stopped, Failed }

/// <summary>Local capture state, not confirmation that a remote service is broadcasting.</summary>
public sealed record WebCaptureStreamStatus(WebCaptureStreamPhase Phase)
{
    public WebCaptureStreamTicket? Ticket { get; init; }
    public WebCaptureStreamOptions? Quality { get; init; }
    public string? ApplicationName { get; init; }
    public bool ApplicationAudio { get; init; }
    public string? Reason { get; init; }
    public bool IsWellFormed() => Enum.IsDefined(Phase) &&
        (Ticket is null || Ticket.IsWellFormed()) && (Quality is null || Quality.IsWellFormed()) &&
        (ApplicationName is null || ApplicationName is {Length:>0 and <=120} && !ApplicationName.Any(char.IsControl)) &&
        (Reason is null || Reason is {Length:>0 and <=64} && Reason.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_')) &&
        (Phase is not (WebCaptureStreamPhase.Prepared or WebCaptureStreamPhase.Capturing) ||
            Ticket is not null && Quality is not null && ApplicationName is not null && Reason is null);
}
