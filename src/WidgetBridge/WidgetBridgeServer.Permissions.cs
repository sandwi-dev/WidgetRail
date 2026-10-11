using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private async Task HandlePermissionAsync(BridgeEnvelope message, CancellationToken token)
    {
        var registry = PermissionRequestRegistry.Shared;
        var pending = registry.Take();
        var (catalog, _) = _registry.CatalogSnapshot();
        var descriptor = pending is null ? null : catalog.Widgets.FirstOrDefault(w => w.InstanceId == pending.Widget.InstanceId);
        var configured = descriptor is null ? null : catalog.GetConfigured(descriptor.Id);
        var valid = pending is not null && configured is not null && configured.PackageId == pending.Widget.PackageId &&
            configured.PublisherId == pending.Widget.PublisherId && pending.CapabilityIds.All(configured.DeclaredCapabilities.Contains);
        if (!valid && pending is not null) await registry.CompleteAsync(pending.RequestId, WidgetPermissionOutcome.Unavailable, token).ConfigureAwait(false);
        if (message.Type == BridgeMessageTypes.TakePermission)
        {
            var prompt = valid ? new BridgePermissionPrompt(pending!.RequestId, descriptor!.Id, descriptor.Name,
                pending.CapabilityIds.Select(CapabilityPresentation.CapabilityName).ToArray()) : null;
            await ReplyAsync(message.Type, message.RequestId, new BridgePermissionTake(prompt), token).ConfigureAwait(false);
        }
        else if (message.Type == BridgeMessageTypes.CheckPermission)
        {
            var reference = BridgeJson.FromElement<WidgetPermissionTicket>(message.Payload);
            await ReplyAsync(message.Type, message.RequestId, new BridgePermissionCurrent(valid && pending!.RequestId == reference.RequestId && registry.IsCurrent(reference.RequestId)), token).ConfigureAwait(false);
        }
        else
        {
            var completion = BridgeJson.FromElement<BridgePermissionComplete>(message.Payload);
            var accepted = valid && pending!.RequestId == completion.RequestId &&
                await registry.CompleteAsync(completion.RequestId, completion.Outcome, token).ConfigureAwait(false);
            await ReplyAsync(message.Type, message.RequestId, new BridgePermissionCurrent(accepted), token).ConfigureAwait(false);
        }
    }
}
