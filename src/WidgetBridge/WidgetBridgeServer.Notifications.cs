using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private long notificationSequence;
    private async ValueTask<WidgetNotificationReceipt> PublishWidgetNotificationAsync(ConfiguredWidget configured,
        WidgetNotificationCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var publication = _registry.TryAdmitWidgetBackgroundPublication(configured.Id, configured.WorkerFingerprint);
        if (publication is null) return new(false);
        var descriptor = publication.Value.Descriptor;
        if (!_registry.RegisterNotificationIntent(configured.Id, configured.WorkerFingerprint, publication.Value.WorkerRun, command, out var intentToken)) return new(false);
        try
        {
            token.ThrowIfCancellationRequested();
            await SendEventAsync(BridgeMessageTypes.WidgetNotification,
                new BridgeWidgetNotification(descriptor.Id, descriptor.InstanceId, descriptor.RuntimeGeneration,
                    Interlocked.Increment(ref notificationSequence), Environment.TickCount64, command, intentToken), token).ConfigureAwait(false);
        }
        catch
        {
            if (intentToken is not null) _registry.ReleaseNotificationIntent(new(configured.Id, intentToken));
            throw;
        }
        return new(true);
    }
}
