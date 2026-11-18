using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureReplayPhase>))]
public enum CaptureReplayPhase { Off, Waiting, Starting, Recording, Paused, Stopping, Error }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureReplayIssue>))]
public enum CaptureReplayIssue
{
    None, HostUnavailable, TargetUnavailable, HardwareEncoderUnavailable, AudioUnavailable,
    KeyframesUnavailable, OutputOrderUnsupported, StorageUnavailable, BufferNotReady,
    SaveInProgress, SessionChanged, SettingsChanged, FeatureRemoved, PermissionRevoked, Cancelled, RecordingActive, ConfigurationUnavailable,
}
[JsonConverter(typeof(JsonStringEnumConverter<CaptureReplayOutcome>))]
public enum CaptureReplayOutcome { Accepted, Cancelled, Unavailable, Conflict, Busy, Failed }

/// <summary>Host recording preferences, independent of any widget's residency.</summary>
public sealed record CaptureReplaySettings
{
    public bool Enabled { get; init; }
    public bool AutoStart { get; init; }
    public int ReplayDurationSeconds { get; init; } = 60;
    public long MemoryBudgetBytes { get; init; } = 128L * 1024 * 1024;
    public RecordingOptions Recording { get; init; } = new(1280,720,30,6_000_000,
        Encoder:CaptureEncoderMode.Hardware,Audio:CaptureAudioMode.Application);
    public bool IncludeCursor { get; init; }
    public bool IsWellFormed()=>ReplayDurationSeconds is >=15 and <=120 && MemoryBudgetBytes is >=32L*1024*1024 and <=256L*1024*1024 &&
        Recording is not null && Recording.IsWellFormed() && Recording.Encoder==CaptureEncoderMode.Hardware &&
        Recording.Audio is CaptureAudioMode.None or CaptureAudioMode.Application && Recording.FramesPerSecond is 15 or 30 or 60 &&
        Recording.MaximumWidth<=8192 && Recording.MaximumHeight<=8192 && (long)Recording.MaximumWidth*Recording.MaximumHeight<=3840*2160 &&
        (Recording.Selection is not { Resolution: CaptureRecordingResolution.Bounded } selection ||
            (long)selection.MaximumWidth! * selection.MaximumHeight! <= 3840*2160);
    /// <summary>Manual-record duration is not used by replay. Keep its canonical value independent of replay length.</summary>
    public CaptureReplaySettings Normalize()=>this with{Recording=Recording with{MaximumDurationSeconds=5}};
}

public sealed record CaptureReplayStatus(long Revision,long SettingsRevision,CaptureReplaySettings Settings,CaptureReplayPhase Phase)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaptureRecordingConfiguration? Resolved { get; init; }
    public string? SessionId { get; init; }
    public string? TargetName { get; init; }
    public double BufferedDurationSeconds { get; init; }
    public long ResidentBytes { get; init; }
    public long DroppedFrames { get; init; }
    public bool Saving { get; init; }
    public CaptureReplayIssue Issue { get; init; }
    public bool IsWellFormed()=>Revision>=0 && SettingsRevision>=0 && Settings is not null && Settings.IsWellFormed() &&
        Enum.IsDefined(Phase) && Enum.IsDefined(Issue) && (SessionId is null || CaptureLimits.ValidToken(SessionId)) &&
        (Resolved is null || SessionId is not null && Resolved.IsWellFormed()) &&
        (TargetName is null || TargetName is {Length:>0 and <=240} && !TargetName.Any(char.IsControl)) &&
        double.IsFinite(BufferedDurationSeconds) && BufferedDurationSeconds>=0 && BufferedDurationSeconds<=Settings.ReplayDurationSeconds &&
        ResidentBytes>=0 && ResidentBytes<=Settings.MemoryBudgetBytes && DroppedFrames>=0 &&
        (Phase is not (CaptureReplayPhase.Recording or CaptureReplayPhase.Paused) || SessionId is not null);
}
public sealed record CaptureReplaySettingsUpdate(long ExpectedRevision,CaptureReplaySettings Settings)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EncodingDetails { get; init; }
    public bool IsWellFormed()=>ExpectedRevision>=0 && Settings is not null && Settings.IsWellFormed();
}
public sealed record CaptureReplaySessionRequest(string? ExpectedSessionId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EncodingDetails { get; init; }
    public bool IsWellFormed()=>ExpectedSessionId is null || CaptureLimits.ValidToken(ExpectedSessionId);
}
/// <summary>Explicit opt-in to extended encoding settings; empty legacy requests retain their original response shape.</summary>
public sealed record CaptureReplayReadRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EncodingDetails { get; init; }
}
public sealed record CaptureReplaySaveRequest(string ExpectedSessionId,string RequestId)
{
    public bool IsWellFormed()=>CaptureLimits.ValidToken(ExpectedSessionId) && CaptureLimits.ValidToken(RequestId);
}
public sealed record CaptureReplayControlResult(CaptureReplayOutcome Outcome,CaptureReplayStatus Status,CaptureReplayIssue Issue=CaptureReplayIssue.None)
{
    public bool IsWellFormed()=>Enum.IsDefined(Outcome) && Enum.IsDefined(Issue) && Status?.IsWellFormed()==true;
}
public sealed record CaptureReplaySaveResult(CaptureReplayOutcome Outcome,string RequestId,CaptureLibraryItem? Item=null,
    double ActualDurationSeconds=0,CaptureReplayIssue Issue=CaptureReplayIssue.None)
{
    public bool IsWellFormed()=>Enum.IsDefined(Outcome) && Enum.IsDefined(Issue) && CaptureLimits.ValidToken(RequestId) &&
        double.IsFinite(ActualDurationSeconds) && ActualDurationSeconds>=0 && ActualDurationSeconds<=120 &&
        (Outcome==CaptureReplayOutcome.Accepted
            ?Item is not null && CaptureLibraryLimits.Valid(Item) && Item.Media.ContentType=="video/mp4" && ActualDurationSeconds>0 &&
                Math.Abs(Item.Media.DurationSeconds-ActualDurationSeconds)<.001
            :Item is null && ActualDurationSeconds==0);
}
