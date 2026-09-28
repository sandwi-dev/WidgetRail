using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Explicit test backend. Never loads the platform DLL or touches hardware.</summary>
internal sealed class ReplayNativePlatform : IOverlayPlatformNative
{
    private readonly Queue<ControllerFrame> frames = new();
    private readonly Queue<PlatformEvent> signals = new();
    public void Move(NavigationDirection direction)
    {
        var frame = ControllerFrame.Create();
        frame.Connected = 1;
        frame.DpadNavigation = new() { Direction = direction, Phase = NavigationPhase.Pressed };
        frames.Enqueue(frame);
    }
    public void Activate()
    {
        var frame = ControllerFrame.Create();
        frame.Connected = 1;
        frame.PressedButtons = 0x1000;
        frames.Enqueue(frame);
    }
    public void Toggle()
    {
        var value = PlatformEvent.Create();
        value.Kind = PlatformEventKind.GuideToggleRequested;
        value.GuideSource = GuideSource.LegacyCompatibility;
        signals.Enqueue(value);
    }
    public uint GetAbiVersion() => PlatformAbi.Version;
    public PlatformStatus Create(in PlatformCreateOptions options, out nint handle) { handle = 1; return PlatformStatus.Ok; }
    public PlatformStatus Initialize(nint handle) => PlatformStatus.Ok;
    public void Shutdown(nint handle) { frames.Clear(); signals.Clear(); }
    public void Destroy(nint handle) => Shutdown(handle);
    public uint HasGameInput(nint handle) => 0;
    public uint RequiresLegacyGuidePolling(nint handle) => 1;
    public PlatformStatus SetWindowState(nint handle, uint visible, uint focused) => PlatformStatus.Ok;
    public PlatformStatus PrepareVisible(nint handle) => PlatformStatus.Ok;
    public PlatformStatus DrainEvent(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent)
    { hasEvent = 0; return PlatformStatus.Ok; }
    public PlatformStatus PollLegacyGuide(nint handle, ulong nowMilliseconds, ref PlatformEvent value, out uint hasEvent)
    { hasEvent = signals.TryDequeue(out var signal) ? 1U : 0U; if (hasEvent != 0) value = signal; return PlatformStatus.Ok; }
    public PlatformStatus PrimeController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds) => PlatformStatus.Ok;
    public PlatformStatus ReadController(nint handle, uint foregroundConfirmed, ulong nowMilliseconds, ref ControllerFrame frame)
    {
        if (frames.TryDequeue(out var next)) frame = next;
        else frame.Connected = 1;
        return PlatformStatus.Ok;
    }
    public PlatformStatus SetOwnedWindows(nint handle, nuint overlay, nuint backdrop) => PlatformStatus.Ok;
    public uint ObserveForegroundTarget(nint handle, nuint candidate, uint candidateIsValid) => 0;
    public nuint RememberedForegroundTarget(nint handle) => 0;
    public nuint ResolveForegroundTarget(nint handle, nuint fallback, uint rememberedTargetIsValid) => fallback;
    public PlatformStatus ComputePlacement(in PlacementInput input, ref Placement output, out uint hasPlacement)
    { hasPlacement = 0; return PlatformStatus.Ok; }
    public NativeShortcutSource NativeShortcutButtons(nint handle, out ushort buttons)
    { buttons = 0; return NativeShortcutSource.Unavailable; }
}
