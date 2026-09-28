namespace WidgetRail.OverlayPlatformClient;

/// <summary>
/// ABI seam for deterministic tests. Implementations must match native lifetime:
/// Destroy drains callbacks before returning and must not throw.
/// </summary>
public interface IOverlayPlatformNative
{
    uint GetAbiVersion();
    PlatformStatus Create(in PlatformCreateOptions options, out nint handle);
    PlatformStatus Initialize(nint handle);
    void Shutdown(nint handle);
    void Destroy(nint handle);
    uint HasGameInput(nint handle);
    uint RequiresLegacyGuidePolling(nint handle);
    PlatformStatus SetWindowState(nint handle, uint visible, uint focused);
    PlatformStatus PrepareVisible(nint handle);
    PlatformStatus DrainEvent(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent);
    PlatformStatus PollLegacyGuide(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent);
    PlatformStatus PrimeController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds);
    PlatformStatus ReadController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds, ref ControllerFrame frame);
    PlatformStatus SetOwnedWindows(nint handle, nuint overlay, nuint backdrop);
    PlatformStatus AcquireForeground(nint handle, out uint confirmed);
    uint ObserveForegroundTarget(nint handle, nuint candidate, uint candidateIsValid);
    nuint RememberedForegroundTarget(nint handle);
    nuint ResolveForegroundTarget(nint handle, nuint fallback, uint rememberedTargetIsValid);
    PlatformStatus ComputePlacement(in PlacementInput input, ref Placement output, out uint hasPlacement);
    NativeShortcutSource NativeShortcutButtons(nint handle, out ushort buttons);
}

/// <summary>
/// Queues work to the frontend's serialized owner. Must never execute inline or
/// wait for the dispatched work; native shutdown waits for callbacks to return.
/// </summary>
public interface IPlatformDispatcher
{
    bool TryEnqueue(Action action);
}
