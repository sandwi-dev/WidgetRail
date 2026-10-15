namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private async Task PumpAppUpdateEventsAsync(CancellationToken token)
    {
        try
        {
            await foreach (var snapshot in _appUpdates.Changes.Reader.ReadAllAsync(token).ConfigureAwait(false))
                await SendEventAsync(BridgeMessageTypes.AppUpdateChanged, snapshot, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (IOException) { /* The owning request loop observes a closed transport and owns teardown. */ }
    }
}
