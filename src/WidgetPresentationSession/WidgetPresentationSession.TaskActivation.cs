using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed record WidgetTaskActivationResult(string Result, string Code, int ProcessId, string NativeTrace);

public sealed partial class WidgetPresentationSession
{
    private long hostTaskActivationSequence;

    public async Task<WidgetTaskActivationResult> ActivateHostTaskWindowAsync(WidgetHostWindowTarget target, long initiatedAt,
        CancellationToken token = default)
    {
        var request = new BridgeHostTaskActivationRequest(Interlocked.Increment(ref hostTaskActivationSequence), initiatedAt,
            target.Handle, target.ProcessId, target.ProcessCreated, target.ClassName);
        request.Validate();
        var response = await RequestAsync(BridgeMessageTypes.ActivateHostTaskWindow, request,
            BridgeMessageTypes.ActivateHostTaskWindow, token).ConfigureAwait(false);
        return ReadTaskActivationResult(response.Payload);
    }

    public async Task<WidgetTaskActivationResult> CompleteTaskActivationAsync(WidgetPresentationHostEffect effect, CancellationToken token = default)
    {
        if (effect.Kind != WidgetHostEffectKind.ActivateTaskWindow || !IsHostEffectAuthorityCurrent(effect.Authority))
            return new("Rejected", "stale-host-effect", 0, "");
        var response = await RequestAsync(BridgeMessageTypes.CompleteTaskActivation,
            new BridgeTaskActivationRequest(effect.Authority.WidgetId, effect.Authority.RuntimeGeneration, effect.Sequence),
            BridgeMessageTypes.CompleteTaskActivation, token).ConfigureAwait(false);
        return ReadTaskActivationResult(response.Payload);
    }

    private static WidgetTaskActivationResult ReadTaskActivationResult(System.Text.Json.JsonElement payload)
    {
        RequireObjectProperties(payload, "result", "code", "processId", "nativeTrace");
        var result = BridgeJson.FromElement<BridgeTaskActivationResponse>(payload);
        if (result.Result is not ("Rejected" or "Requested" or "Denied") || result.Code is not { Length: > 0 and <= 128 } ||
            result.ProcessId <= 0 || result.NativeTrace is not { Length: <= 4096 })
            throw new BridgeProtocolException("Invalid task activation completion response.");
        return new(result.Result, result.Code, result.ProcessId, result.NativeTrace);
    }
}
