using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<BridgeFilePickerTake> TakeFilePickerAsync(CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeFilePickerTake>((await RequestAsync(BridgeMessageTypes.TakeFilePicker, new BridgeEmptyPayload(),
            BridgeMessageTypes.TakeFilePicker, token).ConfigureAwait(false)).Payload);
    public async Task<bool> IsFilePickerCurrentAsync(string id, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeFilePickerCurrent>((await RequestAsync(BridgeMessageTypes.CheckFilePicker,
            new BridgeFilePickerReference(id), BridgeMessageTypes.CheckFilePicker, token).ConfigureAwait(false)).Payload).Current;
    public async Task CompleteFilePickerAsync(string id, IReadOnlyList<string> paths, CancellationToken token = default) =>
        _ = await RequestAsync(BridgeMessageTypes.CompleteFilePicker, new BridgeFilePickerCompletion(id, paths),
            BridgeMessageTypes.CompleteFilePicker, token).ConfigureAwait(false);
    public async Task CompleteWindowPickerAsync(string id, string? match, CancellationToken token = default) =>
        _ = await RequestAsync(BridgeMessageTypes.CompleteFilePicker, new BridgeFilePickerCompletion(id, []) { WindowMatch = match },
            BridgeMessageTypes.CompleteFilePicker, token).ConfigureAwait(false);
}
