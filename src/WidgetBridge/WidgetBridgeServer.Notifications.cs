using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private long notificationSequence;
    private async ValueTask<WidgetNotificationReceipt> PublishWidgetNotificationAsync(ConfiguredWidget configured,
        WidgetNotificationCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var publication = _registry.TryAdmitWidgetNotification(configured.Id, configured.WorkerFingerprint);
        if (publication is null) return new(false);
        var descriptor = publication.Value.Descriptor;
        token.ThrowIfCancellationRequested();
        await SendEventAsync(BridgeMessageTypes.WidgetNotification,
            new BridgeWidgetNotification(descriptor.Id, descriptor.InstanceId, descriptor.RuntimeGeneration,
                Interlocked.Increment(ref notificationSequence), Environment.TickCount64, command), token).ConfigureAwait(false);
        return new(true);
    }
}
