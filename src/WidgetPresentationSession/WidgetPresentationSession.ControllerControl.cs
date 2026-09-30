using System.Text.Json;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private readonly SemaphoreSlim _controllerControlExchange = new(1, 1);

    /// <summary>
    /// Reports the native controller adapter's actual status and reads the next
    /// persisted preference. The host must apply that preference before reporting
    /// again. Cancellation never replays a report already admitted to the bridge.
    /// </summary>
    public async Task<ControllerSettings> ExchangeControllerControlAsync(
        ControllerControlStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!Enum.IsDefined(status.State)) throw new ArgumentOutOfRangeException(nameof(status));
        if (!_options.ExclusiveControllerControl)
            throw new InvalidOperationException("Controller control was not advertised by this host.");
        await _controllerControlExchange.WaitAsync(cancellationToken).ConfigureAwait(false);
        // Keep the lane until the actual response arrives, even if this caller
        // stops waiting. The broker attributes each report to the preceding reply.
        var exchange = ExchangeControllerControlCoreAsync(status);
        _ = exchange.ContinueWith(completed =>
        {
            _ = completed.Exception;
            _controllerControlExchange.Release();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await exchange.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ControllerSettings> ExchangeControllerControlCoreAsync(ControllerControlStatus status)
    {
        var response = await RequestAsync(BridgeMessageTypes.ControllerControl, status,
            BridgeMessageTypes.ControllerControl, CancellationToken.None).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "exclusiveControl", "revision", "openShortcut", "holdDpadToScroll");
        // Deserialization defaults must not silently turn malformed data into an
        // instruction to disable control or change the opening shortcut.
        if (response.Payload.EnumerateObject().Count() != 4 ||
            response.Payload.GetProperty("exclusiveControl").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            response.Payload.GetProperty("holdDpadToScroll").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            response.Payload.GetProperty("revision").ValueKind != JsonValueKind.Number ||
            !response.Payload.GetProperty("revision").TryGetInt64(out var revision) || revision < 0 ||
            response.Payload.GetProperty("openShortcut").ValueKind != JsonValueKind.String ||
            response.Payload.GetProperty("openShortcut").GetString() is not ("guide" or "viewMenu"))
            throw new BridgeProtocolException("WidgetBridge returned invalid controller preferences.");
        return BridgeJson.FromElement<ControllerSettings>(response.Payload);
    }
}
