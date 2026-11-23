namespace WidgetRail.WidgetProtocol;

/// <summary>Assignable even when the widget has no running worker. IDs are local to this package.</summary>
public sealed record TrayActionDefinition(string Id, string Label, string? FallbackId = null)
{
    /// <summary>An explicit press may prepare this action in a background worker.
    /// Assignment, hint rendering and availability checks never start a worker.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool WakeOnInvoke { get; init; }

    /// <summary>Optional host operation whose current state can select the hint before background preparation.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusOperation { get; init; }
}

/// <summary>Identifies the current target and command meaning. Change TargetKey when either changes.</summary>
public sealed record TrayActionOffer(string Id, string TargetKey, string? AvailableAsFallbackFor = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TrayMediaCommand? MediaCommand { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TrayCaptureCommand? CaptureCommand { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool TrayOnly { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool NeedsPermission { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? EmbeddedSessionId { get; init; }
}

/// <summary>Exact arguments for the one media transport operation authorized by this gesture.</summary>
public sealed record TrayMediaCommand(string SessionId, string Command);

/// <summary>The parts of recorder state that change an assignable replay command.</summary>
public readonly record struct TrayReplayState(bool CanStart, bool CanSave, bool CanStop, string? SessionId, long? IdleRevision)
{
    public static TrayReplayState From(CaptureReplayStatus? status)
    {
        if (status is null || status.Issue is CaptureReplayIssue.FeatureRemoved or CaptureReplayIssue.HostUnavailable)
            return default;
        var start = status.SessionId is null && (status.Phase == CaptureReplayPhase.Off && !status.Settings.Enabled ||
            status.Phase == CaptureReplayPhase.Waiting);
        var save = status.SessionId is not null && !status.Saving && status.BufferedDurationSeconds > 0 &&
            status.Phase is CaptureReplayPhase.Recording or CaptureReplayPhase.Paused;
        var stop = (status.SessionId is not null || status.Settings.Enabled) && status.Phase != CaptureReplayPhase.Stopping;
        return new(start, save, stop, status.SessionId, status.SessionId is null && (start || stop) ? status.Revision : null);
    }

    public TrayCaptureCommand? CommandFor(string? operation) => operation switch
    {
        "capture.replay.start" when CanStart => new(operation, Revision: IdleRevision),
        "capture.replay.save" when CanSave => new(operation, SessionId),
        "capture.replay.stop" when CanStop => new(operation, SessionId, IdleRevision),
        _ => null
    };
}

/// <summary>The capture operation and recorder state authorized by a single press.</summary>
public sealed record TrayCaptureCommand(string Operation, string? SessionId = null, long? Revision = null)
{
    public bool IsWellFormed() => Operation switch
    {
        "capture.save-screenshot" or "capture.record-clip" => SessionId is null && Revision is null,
        "capture.replay.start" => SessionId is null && Revision >= 0,
        "capture.replay.stop" => SessionId is null ? Revision >= 0 : CaptureLimits.ValidToken(SessionId) && Revision is null,
        "capture.replay.save" => CaptureLimits.ValidToken(SessionId) && Revision is null,
        _ => false
    };
}

/// <summary>Final outcome for one tray invocation. A rejection may carry a short user-facing explanation.</summary>
public readonly record struct TrayActionResult([property: System.Text.Json.Serialization.JsonRequired] bool Handled, string? Reason = null)
{
    public static TrayActionResult Success => new(true);
    public static TrayActionResult Unavailable(string reason) => TrayActionContract.ValidReason(reason)
        ? new(false, reason) : throw new ArgumentException("Invalid tray action explanation.", nameof(reason));
    public bool IsWellFormed() => Reason is null || !Handled && TrayActionContract.ValidReason(Reason);
}

public static class TrayActionContract
{
    public static bool AllowsFallback(IReadOnlyList<WidgetQuickAction> actions, TrayActionOffer offer, string? primary) =>
        primary is null || offer.AvailableAsFallbackFor == primary && !actions.Any(action => action.TrayOffer?.Id == primary);
    public const int MaximumDefinitions = 16;
    public static bool ValidReason(string? reason) => reason is { Length: > 0 and <= 160 } &&
        !string.IsNullOrWhiteSpace(reason) && !reason.Any(char.IsControl);
    public static bool ValidId(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    public static bool ValidDefinitions(IReadOnlyList<TrayActionDefinition>? items)
    {
        if (items is null || items.Count > MaximumDefinitions) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
            if (item is null || !ValidId(item.Id) || !ids.Add(item.Id) ||
                item.StatusOperation is not null && (!item.WakeOnInvoke || item.StatusOperation is not
                    ("capture.replay.start" or "capture.replay.save" or "capture.replay.stop")) ||
                string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > 80 || item.Label.Any(char.IsControl))
                return false;
        // A fallback is a separately executable local definition; never chain fallbacks.
        return items.All(item => item.FallbackId is null || items.Any(target =>
            target.Id == item.FallbackId && target.Id != item.Id && target.FallbackId is null));
    }

    public static bool ValidOffer(TrayActionOffer? offer) => offer is not null && ValidId(offer.Id) &&
        !string.IsNullOrWhiteSpace(offer.TargetKey) && offer.TargetKey.Length <= 256 && !offer.TargetKey.Any(char.IsControl) &&
        (offer.AvailableAsFallbackFor is null || ValidId(offer.AvailableAsFallbackFor) && offer.AvailableAsFallbackFor != offer.Id) &&
        (offer.EmbeddedSessionId is null || ValidId(offer.EmbeddedSessionId)) &&
        (!offer.NeedsPermission || offer.TrayOnly && offer.MediaCommand is null && offer.CaptureCommand is null) &&
        (offer.CaptureCommand is null || offer.MediaCommand is null && offer.CaptureCommand.IsWellFormed()) &&
        (offer.MediaCommand is null || offer.MediaCommand.SessionId is { Length: > 0 and <= 128 } session &&
            !session.Any(char.IsControl) && offer.MediaCommand.Command is "Play" or "Pause" or "Previous" or "Next");
}
