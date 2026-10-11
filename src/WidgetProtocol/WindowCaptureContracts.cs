using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WindowCaptureKind>))]
public enum WindowCaptureKind { Screenshot = 1, Video = 2 }
[JsonConverter(typeof(JsonStringEnumConverter<WindowCapturePhase>))]
public enum WindowCapturePhase { Queued, Preparing, Recording, Ready, Cancelled, Failed }

/// <summary>Capture the foreground application after the host confirmation and countdown.</summary>
[method: JsonConstructor]
public sealed record WindowCaptureRequest(WindowCaptureKind Kind)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CaptureImageResolution Resolution { get; init; } = CaptureImageResolution.Context;
    public WindowCaptureRequest(WindowCaptureKind kind, CaptureImageResolution resolution) : this(kind) => Resolution = resolution;
}
public sealed record WindowCaptureTicket(string RequestId);
public sealed record WindowCaptureStatus(string RequestId, WindowCapturePhase Phase,
    CaptureAttachment? Attachment = null, string? ErrorCode = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StopRequested { get; init; }
}
/// <summary>Display metadata for the exact captured window; never a target selector.</summary>
public sealed record CaptureApplicationContext(string ApplicationName, string WindowTitle)
{
    public bool IsWellFormed() => !string.IsNullOrWhiteSpace(ApplicationName) && ApplicationName.Length <= 120 &&
        !ApplicationName.Any(char.IsControl) && WindowTitle is not null && WindowTitle.Length <= 240 && !WindowTitle.Any(char.IsControl);
}
[JsonConverter(typeof(JsonStringEnumConverter<CaptureAttachmentOrigin>))]
public enum CaptureAttachmentOrigin { WindowCapture = 0, SavedLibrary = 1 }
/// <summary>Host-issued temporary media; contains no local path or native handle.</summary>
public sealed record CaptureAttachment(string Id, string ContentType, int Width, int Height,
    long ByteLength, double DurationSeconds, long ExpiresAtUnixMilliseconds)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CaptureAttachmentOrigin Origin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureApplicationContext? SourceApplication { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureMediaProperties? Properties { get; init; }
    public bool IsWellFormed() => Enum.IsDefined(Origin) && (SourceApplication is null || SourceApplication.IsWellFormed()) && CaptureLimits.ValidToken(Id) && ContentType is "image/png" or "video/mp4" &&
        Width is > 0 and <= CaptureLimits.MaximumWidth && Height is > 0 and <= CaptureLimits.MaximumHeight &&
        ((long)Width * Height <= CaptureLimits.MaximumPixels) &&
        ByteLength is > 0 and <= CaptureLimits.MaximumAttachmentBytes &&
        (Properties is null || Properties.IsWellFormed(ContentType == "video/mp4")) &&
        (ContentType != "video/mp4" || Properties is not null || Width <= 1280 && Height <= 720 && ByteLength <= CaptureLimits.MaximumContextBytes) && double.IsFinite(DurationSeconds) &&
        (ContentType == "image/png" ? DurationSeconds == 0 : DurationSeconds > 0 && DurationSeconds <= (Properties is null ? CaptureLimits.VideoSeconds : CaptureLimits.MaximumRecordingSeconds) + 0.25) &&
        ExpiresAtUnixMilliseconds > 0;
}
public sealed record CaptureAttachmentRequest(string AttachmentId);
public sealed record CaptureReadRequest(string AttachmentId, long Offset);
public sealed record CaptureReadChunk(long Offset, byte[] Bytes, bool EndOfFile);

public static class CaptureLimits
{
    public const int PreparationSeconds = 5;
    public const int VideoSeconds = 5;
    public const int FramesPerSecond = 15;
    public const int MaximumRecordingSeconds = 120;
    public const int MaximumWidth = 16384;
    public const int MaximumHeight = 16384;
    public const long MaximumPixels = 33554432;
    public const int MaximumContextBytes = 8 * 1024 * 1024;
    public const int MaximumAttachmentBytes = 128 * 1024 * 1024;
    public const int ChunkBytes = 32 * 1024;
    public const int MaximumPerWidget = 4;
    public const int MaximumTotal = 16;
    public const long MaximumTotalBytes = 256L * 1024 * 1024;
    public static bool ValidToken(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);
}
