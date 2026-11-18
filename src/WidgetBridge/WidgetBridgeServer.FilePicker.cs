using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private async Task HandleFilePickerAsync(BridgeEnvelope message, CancellationToken token)
    {
        var pending = FilePickerRequests.Pending;
        var (catalog, _) = _registry.CatalogSnapshot();
        var descriptor = pending is null ? null : catalog.Widgets.FirstOrDefault(widget => widget.InstanceId == pending.Widget.InstanceId);
        var configured = descriptor is null ? null : catalog.GetConfigured(descriptor.Id);
        var valid = pending is not null && configured is not null && configured.PackageId == pending.Widget.PackageId &&
            configured.PublisherId == pending.Widget.PublisherId && FilePickerRequests.IsCurrent(pending.Id);
        if (!valid && pending is not null) FilePickerRequests.Complete(pending.Id, []);
        if (message.Type == BridgeMessageTypes.TakeFilePicker)
        {
            var prompt = valid ? new BridgeFilePickerPrompt(pending!.Id, descriptor!.Id, pending.Title, pending.Multiple) : null;
            await ReplyAsync(message.Type, message.RequestId, new BridgeFilePickerTake(prompt), token).ConfigureAwait(false);
        }
        else if (message.Type == BridgeMessageTypes.CheckFilePicker)
        {
            var reference = BridgeJson.FromElement<BridgeFilePickerReference>(message.Payload);
            await ReplyAsync(message.Type, message.RequestId, new BridgeFilePickerCurrent(valid && pending!.Id == reference.RequestId), token).ConfigureAwait(false);
        }
        else
        {
            var completion = BridgeJson.FromElement<BridgeFilePickerCompletion>(message.Payload);
            var accepted = valid && pending!.Id == completion.RequestId && FilePickerRequests.Complete(completion.RequestId, completion.Paths);
            await ReplyAsync(message.Type, message.RequestId, new BridgeFilePickerCurrent(accepted), token).ConfigureAwait(false);
        }
    }
}
