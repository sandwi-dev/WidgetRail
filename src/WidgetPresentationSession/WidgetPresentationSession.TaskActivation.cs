using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed record WidgetTaskActivationResult(string Result, string Code, int ProcessId, string NativeTrace);

public sealed partial class WidgetPresentationSession
{
    public async Task<WidgetTaskActivationResult> CompleteTaskActivationAsync(WidgetPresentationHostEffect effect, CancellationToken token = default)
    {
        if (effect.Kind != WidgetHostEffectKind.ActivateTaskWindow || !IsHostEffectAuthorityCurrent(effect.Authority))
            return new("Rejected", "stale-host-effect", 0, "");
        var response = await RequestAsync(BridgeMessageTypes.CompleteTaskActivation,
            new BridgeTaskActivationRequest(effect.Authority.WidgetId, effect.Authority.RuntimeGeneration, effect.Sequence),
            BridgeMessageTypes.CompleteTaskActivation, token).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "result", "code", "processId", "nativeTrace");
        var result = BridgeJson.FromElement<BridgeTaskActivationResponse>(response.Payload);
        if (result.Result is not ("Rejected" or "Requested" or "Denied") || result.Code is not { Length: > 0 and <= 128 } ||
            result.ProcessId <= 0 || result.NativeTrace is not { Length: <= 4096 })
            throw new BridgeProtocolException("Invalid task activation completion response.");
        return new(result.Result, result.Code, result.ProcessId, result.NativeTrace);
    }
}
