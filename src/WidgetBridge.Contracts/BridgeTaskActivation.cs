namespace WidgetRail.WidgetBridge;

// Host-only completion of a broker-issued effect. No client-supplied HWND.
internal sealed record BridgeTaskActivationRequest(string WidgetId, string RuntimeGeneration, long Sequence);
internal sealed record BridgeTaskActivationResponse(string Result, string Code, int ProcessId, string NativeTrace);
