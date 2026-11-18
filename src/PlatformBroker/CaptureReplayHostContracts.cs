using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Durable host intent. Native window/process identities are never persisted.</summary>
public sealed record CaptureReplayConfiguration(long Revision, CaptureReplaySettings Settings,
    BrokerWidgetIdentity? Owner = null, string? ApprovedApplication = null, string? ApplicationName = null)
{
    public static CaptureReplayConfiguration Default { get; } = new(0, new());
    public bool IsWellFormed() => Revision is >= 0 and <= 9_007_199_254_740_990L && Settings?.IsWellFormed() == true &&
        (!Settings.Enabled || Owner is not null) &&
        (ApprovedApplication is null && ApplicationName is null || ApprovedApplication is { Length: 64 } &&
            ApprovedApplication.All(char.IsAsciiHexDigit) && ApplicationName is { Length: > 0 and <= 120 } && !ApplicationName.Any(char.IsControl));
}

public enum HostReplayCommandKind { ApproveSettings, Start, Save }

/// <summary>Trusted frontend work; the output path is reserved by the library, never supplied by a widget.</summary>
public sealed record HostReplayCommand(string Id, HostReplayCommandKind Kind, BrokerWidgetIdentity Widget,
    string? SessionId, CaptureReplaySettings Settings, long SettingsRevision)
{
    public bool Automatic { get; init; }
    public string? ApprovedApplication { get; init; }
    public string? OutputPath { get; init; }
    public string? SaveRequestId { get; init; }
}

public sealed record HostReplayReport(string SessionId, CaptureReplayPhase Phase,
    double BufferedDurationSeconds, long ResidentBytes, long DroppedFrames,
    CaptureReplayIssue Issue = CaptureReplayIssue.None, bool Ended = false);

public sealed record HostReplayCompletion(string Id, CaptureReplayOutcome Outcome, CaptureReplayIssue Issue = CaptureReplayIssue.None)
{
    public string? SessionId { get; init; }
    public NativeWindowPreviewTarget? Target { get; init; }
    public string? ApprovedApplication { get; init; }
    public string? ApplicationName { get; init; }
    public CaptureApplicationContext? SourceApplication { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long ByteLength { get; init; }
    public double ActualDurationSeconds { get; init; }
    public CaptureMediaProperties? Properties { get; init; }
    public CaptureRecordingConfiguration? Resolved { get; init; }
}

/// <summary>A renewable host lease. Stop acquires no new frames; an accepted save may drain. Abort revokes all work.</summary>
public sealed record HostReplayWork(CaptureReplayStatus Status, HostReplayCommand? Command,
    string? SessionId, bool StopRequested, bool AbortRequested)
{
    public string? WidgetId { get; init; }
}
