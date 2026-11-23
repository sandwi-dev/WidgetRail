using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    internal async Task<CaptureReplayIssue> AuthorizeReplayFeatureAsync(BrokerWidgetIdentity actor, string capability, CancellationToken token)
    {
        if (capability is not (PlatformCapabilities.WindowCaptureV1 or PlatformCapabilities.CaptureLibrarySaveV1))
            return CaptureReplayIssue.PermissionRevoked;
        var (catalog, _) = _registry.CatalogSnapshot();
        var descriptor = catalog.Widgets.FirstOrDefault(widget => widget.InstanceId == actor.InstanceId);
        if (descriptor is null) return catalog.IsComplete ? CaptureReplayIssue.FeatureRemoved : CaptureReplayIssue.HostUnavailable;
        var configured = catalog.GetConfigured(descriptor.Id);
        if (configured.PackageId != actor.PackageId || configured.PublisherId != actor.PublisherId || !configured.DeclaredCapabilities.Contains(capability))
            return CaptureReplayIssue.FeatureRemoved;
        if (_consentStore is null) return CaptureReplayIssue.PermissionRevoked;
        try { return await _consentStore.GetDecisionAsync(actor, capability, token).ConfigureAwait(false) == ConsentDecision.Grant ? CaptureReplayIssue.None : CaptureReplayIssue.PermissionRevoked; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException) && !token.IsCancellationRequested)
        { return CaptureReplayIssue.PermissionRevoked; }
    }

    private async Task HandleReplayAsync(BridgeEnvelope request, CancellationToken token)
    {
        var replay = _platformBackend?.CaptureReplay;
        if (replay is null)
        {
            var unavailable = new CaptureReplayStatus(0, 0, new(), CaptureReplayPhase.Off) { Issue = CaptureReplayIssue.HostUnavailable };
            if (request.Type == BridgeMessageTypes.GetReplayStatus) { await ReplyAsync(request.Type, request.RequestId, unavailable, token).ConfigureAwait(false); return; }
            if (request.Type == BridgeMessageTypes.TakeReplayWork) { await ReplyAsync(request.Type, request.RequestId, new HostReplayWork(unavailable, null, null, false, true), token).ConfigureAwait(false); return; }
            throw new BrokerException("platform_unavailable", "Replay capture is unavailable.");
        }
        switch (request.Type)
        {
            case BridgeMessageTypes.ReplayPowerState:
                var power = BridgeJson.FromElement<BridgeReplayPower>(request.Payload);
                await replay.SetHostPowerStateAsync(power.Revision, power.Suspended, token).ConfigureAwait(false);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(true), token).ConfigureAwait(false); break;
            case BridgeMessageTypes.GetReplayStatus:
                await ReplyAsync(request.Type, request.RequestId, replay.Status, token).ConfigureAwait(false); break;
            case BridgeMessageTypes.TakeReplayWork:
                var poll = BridgeJson.FromElement<BridgeReplayPoll>(request.Payload);
                var work = await replay.PollHostAsync(poll.Report, token).ConfigureAwait(false);
                if (work.Command is { } command)
                {
                    var (catalog, _) = _registry.CatalogSnapshot();
                    work = work with { WidgetId = catalog.Widgets.FirstOrDefault(widget => widget.InstanceId == command.Widget.InstanceId)?.Id };
                }
                await ReplyAsync(request.Type, request.RequestId, work, token).ConfigureAwait(false); break;
            case BridgeMessageTypes.CheckReplayCommand:
                var check = BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(await replay.IsHostCommandCurrentAsync(check.RequestId, token).ConfigureAwait(false)), token).ConfigureAwait(false); break;
            case BridgeMessageTypes.CompleteReplayCommand:
                var completion = BridgeJson.FromElement<HostReplayCompletion>(request.Payload);
                if (completion.Target is { } target)
                    completion = completion with { SourceApplication = await ResolveCaptureApplicationAsync(target, token).ConfigureAwait(false) };
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(await replay.CompleteHostAsync(completion, token).ConfigureAwait(false)), token).ConfigureAwait(false); break;
            case BridgeMessageTypes.StopReplay:
                var stop = BridgeJson.FromElement<BridgeReplayStop>(request.Payload);
                await ReplyAsync(request.Type, request.RequestId, await replay.StopAsync(stop.ExpectedSessionId, stop.ExpectedRevision, token).ConfigureAwait(false), token).ConfigureAwait(false); break;
            case BridgeMessageTypes.SaveReplay:
                var save = BridgeJson.FromElement<CaptureReplaySaveRequest>(request.Payload);
                var actor = replay.Owner;
                var result = actor is null ? new CaptureReplaySaveResult(CaptureReplayOutcome.Unavailable, save.RequestId, Issue: CaptureReplayIssue.BufferNotReady) :
                    await replay.SaveAsync(actor, save, token).ConfigureAwait(false);
                await ReplyAsync(request.Type, request.RequestId, result, token).ConfigureAwait(false); break;
        }
    }
}
