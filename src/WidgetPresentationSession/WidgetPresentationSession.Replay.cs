using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private CaptureReplayStatus? replayStatus;
    /// <summary>Latest observed host recorder state. This does not start a widget or recorder.</summary>
    public CaptureReplayStatus? ReplayStatus { get { using (_gate.Enter()) return replayStatus; } }
    /// <summary>Raised on the transport thread; marshal to the host UI dispatcher before presentation.</summary>
    public event Action<CaptureReplayStatus>? ReplayStatusChanged;
    private CaptureReplayStatus ObserveReplayStatus(CaptureReplayStatus status)
    {
        if (!status.IsWellFormed()) throw new InvalidDataException("Invalid replay state.");
        Action<CaptureReplayStatus>? notify;
        using (_gate.Enter())
        {
            if (_disposed || replayStatus is { } previous && previous.Revision >= status.Revision) return status;
            replayStatus = status; notify = ReplayStatusChanged;
        }
        notify?.Invoke(status); return status;
    }
    public async Task<CaptureReplayStatus> GetReplayStatusAsync(CancellationToken cancellationToken = default) =>
        ObserveReplayStatus(BridgeJson.FromElement<CaptureReplayStatus>((await RequestAsync(BridgeMessageTypes.GetReplayStatus,
            new BridgeEmptyPayload(), BridgeMessageTypes.GetReplayStatus, cancellationToken).ConfigureAwait(false)).Payload));
    public async Task<HostReplayWork> TakeReplayWorkAsync(HostReplayReport? report = null, CancellationToken cancellationToken = default)
    {
        var work = BridgeJson.FromElement<HostReplayWork>((await RequestAsync(BridgeMessageTypes.TakeReplayWork,
            new BridgeReplayPoll(report), BridgeMessageTypes.TakeReplayWork, cancellationToken).ConfigureAwait(false)).Payload);
        ObserveReplayStatus(work.Status); return work;
    }
    public async Task<bool> IsReplayCommandCurrentAsync(string id, CancellationToken cancellationToken = default) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CheckReplayCommand,
            new BridgeCaptureReference(id), BridgeMessageTypes.CheckReplayCommand, cancellationToken).ConfigureAwait(false)).Payload).Current;
    public async Task<bool> CompleteReplayApprovalAsync(string id, ReplayApprovedApplication? selected, CancellationToken token) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CompleteReplayApproval,
            new BridgeReplayApprovalChoice(id, selected), BridgeMessageTypes.CompleteReplayApproval, token).ConfigureAwait(false)).Payload).Current;
    public async Task SetReplayPowerStateAsync(long revision, bool suspended, CancellationToken cancellationToken = default) =>
        _ = await RequestAsync(BridgeMessageTypes.ReplayPowerState, new BridgeReplayPower(revision, suspended),
            BridgeMessageTypes.ReplayPowerState, cancellationToken).ConfigureAwait(false);
    public async Task<bool> CompleteReplayCommandAsync(HostReplayCompletion completion, CancellationToken cancellationToken = default) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CompleteReplayCommand,
            completion, BridgeMessageTypes.CompleteReplayCommand, cancellationToken).ConfigureAwait(false)).Payload).Current;
    public async Task<CaptureReplaySaveResult> SaveReplayAsync(string expectedSessionId, CancellationToken cancellationToken = default)
    {
        var request = new CaptureReplaySaveRequest(expectedSessionId, Guid.NewGuid().ToString("N"));
        if (!request.IsWellFormed()) throw new ArgumentException("Invalid replay session.", nameof(expectedSessionId));
        var result = BridgeJson.FromElement<CaptureReplaySaveResult>((await RequestAsync(BridgeMessageTypes.SaveReplay,
            request, BridgeMessageTypes.SaveReplay, cancellationToken).ConfigureAwait(false)).Payload);
        return result.IsWellFormed() && result.RequestId == request.RequestId ? result : throw new InvalidDataException("Invalid replay save result.");
    }
    public Task<CaptureReplayControlResult> StopReplayAsync(string? expectedSessionId, CancellationToken cancellationToken = default) =>
        StopReplayAsync(expectedSessionId, null, cancellationToken);

    public async Task<CaptureReplayControlResult> StopReplayAsync(string? expectedSessionId, long? expectedRevision, CancellationToken cancellationToken)
    {
        var request = new CaptureReplaySessionRequest(expectedSessionId);
        if (!request.IsWellFormed()) throw new ArgumentException("Invalid replay session.", nameof(expectedSessionId));
        var result = BridgeJson.FromElement<CaptureReplayControlResult>((await RequestAsync(BridgeMessageTypes.StopReplay,
            new BridgeReplayStop(expectedSessionId, expectedRevision), BridgeMessageTypes.StopReplay, cancellationToken).ConfigureAwait(false)).Payload);
        if (!result.IsWellFormed()) throw new InvalidDataException("Invalid replay stop result.");
        ObserveReplayStatus(result.Status); return result;
    }
}
