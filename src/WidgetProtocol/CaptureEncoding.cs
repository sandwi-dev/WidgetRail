using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureVideoCodec>))]
public enum CaptureVideoCodec { Auto, H264, Hevc, Av1 }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureVideoPixelFormat>))]
public enum CaptureVideoPixelFormat { Auto, Yuv4208, Yuv42010, Yuv4448, Yuv44410 }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureVideoRateControl>))]
public enum CaptureVideoRateControl { Auto, ConstantBitrate, Quality, VariableBitrate }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureRecordingResolution>))]
public enum CaptureRecordingResolution { Auto, Source, Bounded }

/// <summary>Independent requested constraints. Auto never overrides another explicitly selected field.</summary>
public sealed record CaptureRecordingSelection
{
    public CaptureRecordingResolution Resolution { get; init; }
    public int? MaximumWidth { get; init; }
    public int? MaximumHeight { get; init; }
    public int? FramesPerSecond { get; init; }
    public CaptureVideoCodec Codec { get; init; }
    public CaptureVideoPixelFormat PixelFormat { get; init; }
    public CaptureVideoRateControl RateControl { get; init; }
    public int? Bitrate { get; init; }
    public int? Quality { get; init; }

    public bool IsWellFormed() => Enum.IsDefined(Resolution) && Enum.IsDefined(Codec) &&
        Enum.IsDefined(PixelFormat) && Enum.IsDefined(RateControl) &&
        (Resolution == CaptureRecordingResolution.Bounded
            ? MaximumWidth is >= 2 and <= 8192 && MaximumHeight is >= 2 and <= 8192 &&
                MaximumWidth % 2 == 0 && MaximumHeight % 2 == 0 &&
                (long)MaximumWidth * MaximumHeight <= CaptureLimits.MaximumPixels
            : MaximumWidth is null && MaximumHeight is null) &&
        (FramesPerSecond is null or 15 or 30 or 60) &&
        (Bitrate is null or >= 100_000 and <= 100_000_000) &&
        (Quality is null or >= 0 and <= 100) &&
        (Quality is null || RateControl is CaptureVideoRateControl.Auto or CaptureVideoRateControl.Quality) &&
        (RateControl != CaptureVideoRateControl.Quality || Bitrate is null) &&
        (Quality is null || Bitrate is null);

    /// <summary>Verify a host resolution without allowing an explicit choice to become a fallback.</summary>
    public bool Allows(CaptureRecordingConfiguration configuration)
    {
        if (!IsWellFormed() || configuration?.IsWellFormed() != true) return false;
        var value = configuration.Recording;
        var encoding = value.Encoding!;
        return (FramesPerSecond is null || FramesPerSecond == value.FramesPerSecond) &&
            (Codec == CaptureVideoCodec.Auto || Codec == encoding.Codec) &&
            (PixelFormat == CaptureVideoPixelFormat.Auto || PixelFormat == encoding.PixelFormat) &&
            (RateControl == CaptureVideoRateControl.Auto || RateControl == encoding.RateControl) &&
            (Bitrate is null || Bitrate == value.Bitrate) && (Quality is null || Quality == encoding.Quality) &&
            (Resolution != CaptureRecordingResolution.Bounded || value.MaximumWidth <= MaximumWidth && value.MaximumHeight <= MaximumHeight) &&
            (Resolution != CaptureRecordingResolution.Source ||
                value.MaximumWidth == (configuration.SourceWidth + 1) / 2 * 2 &&
                value.MaximumHeight == (configuration.SourceHeight + 1) / 2 * 2);
    }
}

/// <summary>Actual encoder configuration. Null on legacy options preserves the original native encoder policy.</summary>
public sealed record CaptureEncodingOptions(CaptureVideoCodec Codec, CaptureVideoPixelFormat PixelFormat,
    CaptureVideoRateControl RateControl, int? Quality = null)
{
    public bool IsWellFormed() => Enum.IsDefined(Codec) && Codec != CaptureVideoCodec.Auto &&
        Enum.IsDefined(PixelFormat) && PixelFormat != CaptureVideoPixelFormat.Auto &&
        Enum.IsDefined(RateControl) && RateControl != CaptureVideoRateControl.Auto &&
        (RateControl == CaptureVideoRateControl.Quality ? Quality is >= 0 and <= 100 : Quality is null);
}

/// <summary>Public adapter identity is opaque; native handles and adapter LUIDs never cross the widget boundary.</summary>
public sealed record CaptureEncodingAdapter(string Id, string Name, string EnvironmentId)
{
    public bool IsWellFormed() => CaptureLimits.ValidToken(Id) && CaptureLimits.ValidToken(EnvironmentId) &&
        Name is { Length: > 0 and <= 160 } && !Name.Any(char.IsControl);
}

/// <summary>Resolved values admitted for the selected source, independent of persisted Auto preferences.</summary>
public sealed record CaptureRecordingConfiguration(RecordingOptions Recording, CaptureEncodingAdapter Adapter,
    int SourceWidth, int SourceHeight)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EditingSupported { get; init; }
    public bool IsWellFormed() => Recording is not null && Recording.IsWellFormed() && Recording.Selection is null &&
        Recording.Encoding is not null && Adapter?.IsWellFormed() == true &&
        SourceWidth is > 0 and <= CaptureLimits.MaximumWidth && SourceHeight is > 0 and <= CaptureLimits.MaximumHeight &&
        (long)SourceWidth * SourceHeight <= CaptureLimits.MaximumPixels;

    public bool Satisfies(RecordingOptions requested) => requested?.IsWellFormed() == true && IsWellFormed() &&
        Recording.Audio == requested.Audio && Recording.Encoder == requested.Encoder &&
        (requested.Selection is { } selection ? selection.Allows(this) :
            Recording.MaximumWidth <= requested.MaximumWidth && Recording.MaximumHeight <= requested.MaximumHeight &&
            Recording.FramesPerSecond == requested.FramesPerSecond && Recording.Bitrate == requested.Bitrate &&
            (requested.Encoding is null || Recording.Encoding == requested.Encoding));
}
