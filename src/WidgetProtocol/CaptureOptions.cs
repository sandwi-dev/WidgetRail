using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureImageFormat>))]
public enum CaptureImageFormat { Png, Jpeg }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureEncoderMode>))]
public enum CaptureEncoderMode { Software = 1, Hardware = 2 }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureAudioMode>))]
public enum CaptureAudioMode { None, Application, System, Microphone }

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
    public bool IsWellFormed() => MaximumWidth is >= 2 and <= 8192 && MaximumHeight is >= 2 and <= 8192 &&
        MaximumWidth % 2 == 0 && MaximumHeight % 2 == 0 && (long)MaximumWidth * MaximumHeight <= CaptureLimits.MaximumPixels &&
        FramesPerSecond is >= 1 and <= 120 && Bitrate is >= 100_000 and <= 100_000_000 &&
        MaximumDurationSeconds is >= 1 and <= CaptureLimits.MaximumRecordingSeconds && Enum.IsDefined(Encoder) && Enum.IsDefined(Audio);
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
    public long MaximumBytes { get; init; } = CaptureLimits.MaximumAttachmentBytes;
    public bool IsWellFormed() => Preparation is not null && Preparation.IsWellFormed() &&
        MaximumBytes is >= 1024 and <= CaptureLimits.MaximumAttachmentBytes &&
        (Screenshot is not null && Recording is null && Screenshot.IsWellFormed() ||
            Recording is not null && Screenshot is null && Recording.IsWellFormed());
}

/// <summary>A verified encoding envelope. Widgets may request smaller even dimensions and a bitrate within the bounds.</summary>
public sealed record CaptureRecordingProfile(string Id, int MaximumWidth, int MaximumHeight,
    int FramesPerSecond, int MinimumBitrate, int MaximumBitrate, CaptureEncoderMode Encoder)
{
    public bool IsWellFormed() => Id is { Length: > 0 and <= 64 } && Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
        MinimumBitrate <= MaximumBitrate && new RecordingOptions(MaximumWidth, MaximumHeight, FramesPerSecond, MinimumBitrate, Encoder: Encoder).IsWellFormed() &&
        new RecordingOptions(MaximumWidth, MaximumHeight, FramesPerSecond, MaximumBitrate, Encoder: Encoder).IsWellFormed();
    public bool Supports(RecordingOptions value) => value.MaximumWidth <= MaximumWidth && value.MaximumHeight <= MaximumHeight &&
        value.FramesPerSecond == FramesPerSecond && value.Bitrate >= MinimumBitrate && value.Bitrate <= MaximumBitrate && value.Encoder == Encoder;
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
            if (!RecordingProfiles.Any(p => p.Supports(video))) return new(false, "capture_encoding_unsupported");
            // Reserve container overhead before admission. Runtime enforces the actual bound as well.
            if ((long)video.Bitrate * video.MaximumDurationSeconds / 8 + 1024 * 1024 > request.MaximumBytes)
                return new(false, "capture_output_budget_too_small");
        }
        return new(true);
    }
}

public sealed record CaptureConfigurationValidation(bool Supported, string? ErrorCode = null);

/// <summary>Actual encoded output, independent of the widget that requested or retained it.</summary>
public sealed record CaptureMediaProperties(int SourceWidth, int SourceHeight, int RequestedFramesPerSecond = 0,
    int EncodedFrames = 0, int MissedFrames = 0, CaptureEncoderMode? Encoder = null, CaptureAudioMode Audio = CaptureAudioMode.None)
{
    public bool IsWellFormed(bool video) => SourceWidth is > 0 and <= CaptureLimits.MaximumWidth &&
        SourceHeight is > 0 and <= CaptureLimits.MaximumHeight && (long)SourceWidth * SourceHeight <= CaptureLimits.MaximumPixels && Enum.IsDefined(Audio) &&
        (video ? RequestedFramesPerSecond is >= 1 and <= 120 && EncodedFrames is > 0 and <= 14400 && MissedFrames is >= 0 and <= 14400 &&
            Encoder is { } encoder && Enum.IsDefined(encoder) : RequestedFramesPerSecond == 0 && EncodedFrames == 0 && MissedFrames == 0 && Encoder is null && Audio == CaptureAudioMode.None);
}
