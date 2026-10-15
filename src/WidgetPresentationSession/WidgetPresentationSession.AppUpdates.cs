using System.Text.Json;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public event EventHandler<AppUpdateSnapshot>? AppUpdateChanged;
    public async Task<AppUpdateSnapshot> StartAppUpdateCheckAsync(CancellationToken token = default)
    {
        var response = await RequestAsync(BridgeMessageTypes.StartAppUpdateCheck, new BridgeEmptyPayload(), BridgeMessageTypes.AppUpdateChanged, token).ConfigureAwait(false);
        var snapshot = BridgeJson.FromElement<AppUpdateSnapshot>(response.Payload);
        AppUpdateContract.ValidateSnapshot(snapshot);
        return snapshot;
    }
    private void HandleAppUpdateChanged(JsonElement payload)
    {
        var snapshot = BridgeJson.FromElement<AppUpdateSnapshot>(payload);
        AppUpdateContract.ValidateSnapshot(snapshot);
        AppUpdateChanged?.Invoke(this, snapshot);
    }
}
