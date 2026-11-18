using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureImageFormat>))]
public enum CaptureImageFormat { Png, Jpeg }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureEncoderMode>))]
public enum CaptureEncoderMode { Software = 1, Hardware = 2 }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureAudioMode>))]
public enum CaptureAudioMode { None, Application, System, Microphone }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureOutputPurpose>))]
public enum CaptureOutputPurpose { TemporaryAttachment = 0, LibraryRecording = 1 }

/// <summary>Null dimensions preserve source pixels. A pair of dimensions fits without upscaling.</summary>
public sealed record ScreenshotOptions(int? MaximumWidth = null, int? MaximumHeight = null,
    CaptureImageFormat Format = CaptureImageFormat.Png, int Quality = 90)
{
    public bool IsWellFormed() => Enum.IsDefined(Format) && Quality is >= 1 and <= 100 &&
        (MaximumWidth is null && MaximumHeight is null || MaximumWidth is > 0 and <= CaptureLimits.MaximumWidth &&
            MaximumHeight is > 0 and <= CaptureLimits.MaximumHeight && (long)MaximumWidth * MaximumHeight <= CaptureLimits.MaximumPixels);
}

/// <summary>Explicit encoding settings. The requested dimensions are an aspect-preserving bound, not a stretch.</summary>
public sealed record RecordingOptions(int MaximumWidth = 1280, int MaximumHeight = 720, int FramesPerSecond = 15,
    int Bitrate = 3_000_000, int MaximumDurationSeconds = 5, CaptureEncoderMode Encoder = CaptureEncoderMode.Software,
    CaptureAudioMode Audio = CaptureAudioMode.None)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureRecordingSelection? Selection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureEncodingOptions? Encoding { get; init; }
    public bool IsWellFormed() => MaximumWidth is >= 2 and <= 8192 && MaximumHeight is >= 2 and <= 8192 &&
        MaximumWidth % 2 == 0 && MaximumHeight % 2 == 0 && (long)MaximumWidth * MaximumHeight <= CaptureLimits.MaximumPixels &&
        FramesPerSecond is >= 1 and <= 120 && Bitrate is >= 100_000 and <= 100_000_000 &&
        MaximumDurationSeconds is >= 1 and <= CaptureLimits.MaximumRecordingSeconds && Enum.IsDefined(Encoder) && Enum.IsDefined(Audio) &&
        (Selection is null || Selection.IsWellFormed()) && (Encoding is null || Encoding.IsWellFormed()) &&
        (Selection is null || Encoding is null) && (Selection is null && Encoding is null || Encoder == CaptureEncoderMode.Hardware);
}

/// <summary>Host-owned confirmation/hide/countdown workflow, independent of encoded media settings.</summary>
public sealed record CapturePreparation(int DelaySeconds = 5, bool ShowCountdown = true)
{
    public bool IsWellFormed() => DelaySeconds is >= 0 and <= 30;
}

public sealed record CaptureSessionRequest
{
    public ScreenshotOptions? Screenshot { get; init; }
    public RecordingOptions? Recording { get; init; }
    public CapturePreparation Preparation { get; init; } = new();
    public bool IncludeCursor { get; init; }
    /// <summary>Library recordings require capture and library-save permission and remain drafts until saved.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CaptureOutputPurpose OutputPurpose { get; init; }
    public long MaximumBytes { get; init; } = CaptureLimits.MaximumAttachmentBytes;
    public bool IsWellFormed() => Preparation is not null && Preparation.IsWellFormed() &&
        Enum.IsDefined(OutputPurpose) &&
        (OutputPurpose != CaptureOutputPurpose.LibraryRecording || Recording is not null) &&
        MaximumBytes >= 1024 && MaximumBytes <= (OutputPurpose == CaptureOutputPurpose.LibraryRecording
            ? CaptureLimits.MaximumLibraryRecordingBytes : CaptureLimits.MaximumAttachmentBytes) &&
        (Screenshot is not null && Recording is null && Screenshot.IsWellFormed() ||
            Recording is not null && Screenshot is null && Recording.IsWellFormed());
}

/// <summary>A verified encoding envelope. Widgets may request smaller even dimensions and a bitrate within the bounds.</summary>
public sealed record CaptureRecordingProfile(string Id, int MaximumWidth, int MaximumHeight,
    int FramesPerSecond, int MinimumBitrate, int MaximumBitrate, CaptureEncoderMode Encoder)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureEncodingOptions? Encoding { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureEncodingAdapter? Adapter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EditingSupported { get; init; }
    public bool IsWellFormed() => Id is { Length: > 0 and <= 64 } && Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
        (Encoding is null && Adapter is null && EditingSupported is null || Encoding?.IsWellFormed() == true && Adapter?.IsWellFormed() == true && Encoder == CaptureEncoderMode.Hardware) &&
        MinimumBitrate <= MaximumBitrate && new RecordingOptions(MaximumWidth, MaximumHeight, FramesPerSecond, MinimumBitrate, Encoder: Encoder).IsWellFormed() &&
        new RecordingOptions(MaximumWidth, MaximumHeight, FramesPerSecond, MaximumBitrate, Encoder: Encoder).IsWellFormed();
    public bool Supports(RecordingOptions value) => value.MaximumWidth <= MaximumWidth && value.MaximumHeight <= MaximumHeight &&
        value.FramesPerSecond == FramesPerSecond && value.Bitrate >= MinimumBitrate && value.Bitrate <= MaximumBitrate && value.Encoder == Encoder &&
        value.Selection is null && value.Encoding == Encoding;
    public bool SupportsSelection(CaptureRecordingSelection value) => value.IsWellFormed() && Encoding is { } encoding &&
        (value.FramesPerSecond is null || value.FramesPerSecond==FramesPerSecond) &&
        (value.Codec==CaptureVideoCodec.Auto || value.Codec==encoding.Codec) &&
        (value.PixelFormat==CaptureVideoPixelFormat.Auto || value.PixelFormat==encoding.PixelFormat) &&
        (value.RateControl==CaptureVideoRateControl.Auto || value.RateControl==encoding.RateControl) &&
        (value.Quality is null || value.Quality==encoding.Quality) &&
        (value.Bitrate is null || value.Bitrate>=MinimumBitrate && value.Bitrate<=MaximumBitrate) &&
        (value.Resolution!=CaptureRecordingResolution.Bounded || MaximumWidth<=value.MaximumWidth && MaximumHeight<=value.MaximumHeight);
}

public sealed record CaptureCapabilities(bool Available, IReadOnlyList<CaptureImageFormat> ImageFormats,
    IReadOnlyList<CaptureRecordingProfile> RecordingProfiles, IReadOnlyList<CaptureAudioMode> AudioModes,
    bool CursorSupported, int MaximumRecordingSeconds)
{
    public static CaptureCapabilities Unavailable => new(false, [], [], [], false, 0);
    public bool IsWellFormed() => ImageFormats is not null && ImageFormats.Count <= 2 && ImageFormats.All(Enum.IsDefined) &&
        ImageFormats.Distinct().Count() == ImageFormats.Count && RecordingProfiles is not null && RecordingProfiles.Count <= 16 &&
        RecordingProfiles.All(p => p is not null && p.IsWellFormed()) && RecordingProfiles.Select(p => p.Id).Distinct().Count() == RecordingProfiles.Count &&
        AudioModes is not null && AudioModes.Count <= 4 && AudioModes.All(Enum.IsDefined) && AudioModes.Distinct().Count() == AudioModes.Count &&
        MaximumRecordingSeconds is >= 0 and <= CaptureLimits.MaximumRecordingSeconds &&
        (RecordingProfiles.Count == 0 || MaximumRecordingSeconds > 0 && AudioModes.Contains(CaptureAudioMode.None));

    public CaptureConfigurationValidation Validate(CaptureSessionRequest request)
    {
        if (request is null || !request.IsWellFormed()) return new(false, "invalid_capture_options");
        if (!Available) return new(false, "capture_unavailable");
        if (request.IncludeCursor && !CursorSupported) return new(false, "capture_cursor_unsupported");
        if (request.Screenshot is { } image && !ImageFormats.Contains(image.Format)) return new(false, "capture_image_format_unsupported");
        if (request.Recording is { } video)
        {
            if (!AudioModes.Contains(video.Audio)) return new(false, "capture_audio_unsupported");
            if (video.MaximumDurationSeconds > MaximumRecordingSeconds) return new(false, "capture_duration_unsupported");
            var supported = RecordingProfiles.Where(p => video.Selection is {} selection ? p.SupportsSelection(selection) : p.Supports(video)).ToArray();
            if (supported.Length==0) return new(false, "capture_encoding_unsupported");
            // Requested Auto/source constraints have no resolved bitrate yet.
            // The trusted source-admission path validates the exact resolved
            // configuration before acquisition; never estimate from fallback
            // legacy fields or an unrelated catalog profile.
            if(video.Selection is not null)return new(true);
            // Reserve container overhead before admission. Runtime enforces the actual bound as well.
            // Quality-driven rates have no promised bitrate ceiling. The native
            // recorder rejects output exceeding the byte limit, including finalization overhead.
            var totalBitrate = (long)video.Bitrate + (video.Audio == CaptureAudioMode.None ? 0 : CaptureLimits.AudioBitrate);
            if(video.Encoding?.RateControl==CaptureVideoRateControl.Quality) return new(true);
            if (totalBitrate * video.MaximumDurationSeconds / 8 + 1024 * 1024 > request.MaximumBytes)
                return new(false, "capture_output_budget_too_small");
        }
        return new(true);
    }
}

public sealed record CaptureConfigurationValidation(bool Supported, string? ErrorCode = null);

/// <summary>Discover a recording profile without surveying unrelated formats. Null selects the recommended silent profile.</summary>
public sealed record CaptureRecordingQuery(RecordingOptions? Recording = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DiscoverAlternatives { get; init; }
    public bool IsWellFormed() => (Recording is null || Recording.IsWellFormed()) && (!DiscoverAlternatives || Recording?.Selection is not null);
    // Duration affects admission, not encoder availability. Keep shared discovery keys stable.
    public CaptureRecordingQuery Normalize() => this with { Recording=Recording is { } recording ? recording with { MaximumDurationSeconds = 5 } : null };
}

/// <summary>Actual encoded output, independent of the widget that requested or retained it.</summary>
public sealed record CaptureMediaProperties(int SourceWidth, int SourceHeight, int RequestedFramesPerSecond = 0,
    int EncodedFrames = 0, int MissedFrames = 0, CaptureEncoderMode? Encoder = null, CaptureAudioMode Audio = CaptureAudioMode.None)
{
    public bool IsWellFormed(bool video) => SourceWidth is > 0 and <= CaptureLimits.MaximumWidth &&
        SourceHeight is > 0 and <= CaptureLimits.MaximumHeight && (long)SourceWidth * SourceHeight <= CaptureLimits.MaximumPixels && Enum.IsDefined(Audio) &&
        (video ? RequestedFramesPerSecond is >= 1 and <= 120 && EncodedFrames is > 0 and <= 14400 && MissedFrames is >= 0 and <= 14400 &&
            Encoder is { } encoder && Enum.IsDefined(encoder) : RequestedFramesPerSecond == 0 && EncodedFrames == 0 && MissedFrames == 0 && Encoder is null && Audio == CaptureAudioMode.None);
}
