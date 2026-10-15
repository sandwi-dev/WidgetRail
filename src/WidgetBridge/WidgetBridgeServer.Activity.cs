using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;
public sealed partial class WidgetBridgeServer
{
    private long activitySequence;
    private async ValueTask<WidgetActivityReceipt> PublishWidgetActivityAsync(ConfiguredWidget configured, WidgetActivity activity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var publication = _registry.TryAdmitWidgetBackgroundPublication(configured.Id, configured.WorkerFingerprint);
        if (publication is null) return new(false);
        var descriptor = publication.Value.Descriptor;
        await SendEventAsync(BridgeMessageTypes.WidgetActivity, new BridgeWidgetActivity(descriptor.Id, descriptor.InstanceId,
            descriptor.RuntimeGeneration, Interlocked.Increment(ref activitySequence), Environment.TickCount64, activity, publication.Value.WorkerRun), token).ConfigureAwait(false);
        return new(true);
    }
}
