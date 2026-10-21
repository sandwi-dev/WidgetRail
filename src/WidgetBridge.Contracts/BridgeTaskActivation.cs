namespace WidgetRail.WidgetBridge;

// Host-only completion of a broker-issued effect. No client-supplied HWND.
internal sealed record BridgeTaskActivationRequest(string WidgetId, string RuntimeGeneration, long Sequence);
internal sealed record BridgeTaskActivationResponse(string Result, string Code, int ProcessId, string NativeTrace);

// Trusted host channel only. Widget-originated activation still requires its
// broker-issued effect ticket above and cannot supply a native target.
internal sealed record BridgeHostTaskActivationRequest(long Sequence, long InitiatedAt,
    ulong Handle, uint ProcessId, ulong ProcessCreated, string ClassName)
{
    internal void Validate()
    {
        if (Sequence <= 0 || InitiatedAt < 0 || Handle == 0 || ProcessId == 0 || ProcessCreated == 0 ||
            string.IsNullOrWhiteSpace(ClassName) || ClassName.Length >= 256 || ClassName.Contains('\0'))
            throw new BridgeProtocolException("Invalid host task activation identity.");
    }
}
