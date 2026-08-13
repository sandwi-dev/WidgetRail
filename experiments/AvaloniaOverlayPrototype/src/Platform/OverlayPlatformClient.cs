using System.Runtime.InteropServices;
using GameBarAlternative.AvaloniaPrototype.Input;
using GameBarAlternative.WidgetProtocol;
using Avalonia;

namespace GameBarAlternative.AvaloniaPrototype.Platform;

public sealed record OverlayPlatformInput(
    SemanticInput? Navigation = null,
    ControllerButton? Button = null,
    ControllerEventPhase Phase = ControllerEventPhase.Pressed,
    bool Connected = true);

public interface IOverlayPlatformClient : IDisposable
{
    event EventHandler? GuideToggleRequested;
    event EventHandler<OverlayPlatformInput>? InputReceived;
    bool HasGameInput { get; }
    bool RequiresLegacyGuidePolling { get; }
    void Attach(nint windowHandle);
    void SetWindowState(bool visible, bool focused);
    void Tick(bool foregroundConfirmed);
    PixelRect? ComputePlacement(PixelRect workArea, uint dpi, double desiredWidthDip, double desiredHeightDip);
}

internal sealed class OverlayPlatformClient : IOverlayPlatformClient
{
    private const uint AbiVersion = 1;
    private const ushort DpadUp = 0x0001;
    private const ushort DpadDown = 0x0002;
    private const ushort DpadLeft = 0x0004;
    private const ushort DpadRight = 0x0008;
    private const ushort Menu = 0x0010;
    private const ushort View = 0x0020;
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;
    private const ushort LeftBumper = 0x0100;
    private const ushort RightBumper = 0x0200;
    private const ushort A = 0x1000;
    private const ushort B = 0x2000;
    private const ushort X = 0x4000;
    private const ushort Y = 0x8000;

    private readonly nint library;
    private readonly object nativeGate = new();
    private readonly EventAvailable eventAvailable;
    private readonly Diagnostic diagnostic;
    private readonly Create create;
    private readonly Initialize initialize;
    private readonly Shutdown shutdown;
    private readonly Destroy destroy;
    private readonly BoolQuery hasGameInput;
    private readonly BoolQuery requiresLegacyPolling;
    private readonly NativeSetWindowState setWindowState;
    private readonly DrainEvent drainEvent;
    private readonly PollLegacyGuide pollLegacyGuide;
    private readonly PrimeController primeController;
    private readonly ReadController readController;
    private readonly SetOwnedWindows setOwnedWindows;
    private readonly NativeComputePlacement computePlacement;
    private nint handle;
    private bool attached;
    private bool wasConnected;
    private bool disposed;

    internal static IReadOnlyDictionary<string, int> ManagedAbiLayoutSizes { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(PlatformEvent)] = Marshal.SizeOf<PlatformEvent>(),
            [nameof(RawControllerState)] = Marshal.SizeOf<RawControllerState>(),
            [nameof(NavigationEvent)] = Marshal.SizeOf<NavigationEvent>(),
            [nameof(ControllerFrame)] = Marshal.SizeOf<ControllerFrame>(),
            [nameof(PlacementInput)] = Marshal.SizeOf<PlacementInput>(),
            [nameof(Placement)] = Marshal.SizeOf<Placement>(),
            [nameof(CreateOptions)] = Marshal.SizeOf<CreateOptions>(),
        };

    public OverlayPlatformClient(string libraryPath)
    {
        library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        eventAvailable = OnEventAvailable;
        diagnostic = OnDiagnostic;
        create = Load<Create>("GbaOverlayPlatformCreate");
        initialize = Load<Initialize>("GbaOverlayPlatformInitialize");
        shutdown = Load<Shutdown>("GbaOverlayPlatformShutdown");
        destroy = Load<Destroy>("GbaOverlayPlatformDestroy");
        hasGameInput = Load<BoolQuery>("GbaOverlayPlatformHasGameInput");
        requiresLegacyPolling = Load<BoolQuery>("GbaOverlayPlatformRequiresLegacyGuidePolling");
        setWindowState = Load<NativeSetWindowState>("GbaOverlayPlatformSetWindowState");
        drainEvent = Load<DrainEvent>("GbaOverlayPlatformDrainEvent");
        pollLegacyGuide = Load<PollLegacyGuide>("GbaOverlayPlatformPollLegacyGuide");
        primeController = Load<PrimeController>("GbaOverlayPlatformPrimeController");
        readController = Load<ReadController>("GbaOverlayPlatformReadController");
        setOwnedWindows = Load<SetOwnedWindows>("GbaOverlayPlatformSetOwnedWindows");
        computePlacement = Load<NativeComputePlacement>("GbaOverlayPlatformComputePlacement");
        var options = new CreateOptions
        {
            StructSize = (uint)Marshal.SizeOf<CreateOptions>(),
            AbiVersion = AbiVersion,
            EventAvailable = eventAvailable,
            Diagnostic = diagnostic,
        };
        ThrowIfFailed(create(ref options, out handle), "create");
        try { ThrowIfFailed(initialize(handle), "initialize"); }
        catch { destroy(handle); handle = 0; NativeLibrary.Free(library); throw; }
    }

    public event EventHandler? GuideToggleRequested;
    public event EventHandler<OverlayPlatformInput>? InputReceived;

    public bool HasGameInput => handle != 0 && hasGameInput(handle) != 0;
    public bool RequiresLegacyGuidePolling => handle != 0 && requiresLegacyPolling(handle) != 0;

    public void Attach(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (windowHandle == 0) throw new ArgumentOutOfRangeException(nameof(windowHandle));
        ThrowIfFailed(setOwnedWindows(handle, (nuint)windowHandle, 0), "set owned window");
        attached = true;
    }

    public void SetWindowState(bool visible, bool focused)
    {
        if (disposed || !attached) return;
        ThrowIfFailed(setWindowState(handle, visible ? 1u : 0u, focused ? 1u : 0u), "set window state");
        if (visible && focused)
            ThrowIfFailed(primeController(handle, 1, NowMilliseconds()), "prime controller");
    }

    public void Tick(bool foregroundConfirmed)
    {
        if (disposed || !attached) return;
        DrainEvents();
        if (RequiresLegacyGuidePolling)
        {
            var legacyEvent = NewEvent();
            ThrowIfFailed(pollLegacyGuide(handle, NowMilliseconds(), ref legacyEvent, out var hasEvent), "poll legacy Guide");
            if (hasEvent != 0) DispatchEvent(legacyEvent);
        }

        var frame = NewControllerFrame();
        ThrowIfFailed(readController(handle, foregroundConfirmed ? 1u : 0u, NowMilliseconds(), ref frame), "read controller");
        var connected = frame.Connected != 0;
        if (connected != wasConnected)
        {
            wasConnected = connected;
            InputReceived?.Invoke(this, new OverlayPlatformInput(Connected: connected));
        }
        if (!connected || frame.Primed == 0) return;

        DispatchNavigation(frame.StickNavigation);
        DispatchNavigation(frame.DpadNavigation);
        DispatchButtons(frame.PressedButtons, ControllerEventPhase.Pressed);
        DispatchButtons(frame.ReleasedButtons, ControllerEventPhase.Released);
        if (frame.LeftTriggerPressed != 0) DispatchButton(ControllerButton.LeftTrigger, ControllerEventPhase.Pressed);
        if (frame.LeftTriggerReleased != 0) DispatchButton(ControllerButton.LeftTrigger, ControllerEventPhase.Released);
        if (frame.RightTriggerPressed != 0) DispatchButton(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
        if (frame.RightTriggerReleased != 0) DispatchButton(ControllerButton.RightTrigger, ControllerEventPhase.Released);
    }

    public PixelRect? ComputePlacement(
        PixelRect workArea,
        uint dpi,
        double desiredWidthDip,
        double desiredHeightDip)
    {
        var input = new PlacementInput
        {
            StructSize = (uint)Marshal.SizeOf<PlacementInput>(),
            AbiVersion = AbiVersion,
            WorkLeft = workArea.X,
            WorkTop = workArea.Y,
            WorkRight = workArea.Right,
            WorkBottom = workArea.Bottom,
            Dpi = dpi,
            DesiredWidthDip = (float)desiredWidthDip,
            DesiredHeightDip = (float)desiredHeightDip,
            SideMarginDip = 24,
            TopMarginDip = 24,
            BottomMarginDip = 32,
        };
        var output = new Placement
        {
            StructSize = (uint)Marshal.SizeOf<Placement>(),
            AbiVersion = AbiVersion,
        };
        ThrowIfFailed(computePlacement(ref input, ref output, out var hasPlacement), "compute placement");
        return hasPlacement == 0 ? null : new PixelRect(output.X, output.Y, output.Width, output.Height);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (nativeGate)
        {
            if (handle != 0)
            {
                shutdown(handle);
                destroy(handle);
                handle = 0;
            }
        }
        NativeLibrary.Free(library);
    }

    private void DrainEvents()
    {
        lock (nativeGate)
        {
            while (!disposed && handle != 0)
            {
                var platformEvent = NewEvent();
                ThrowIfFailed(drainEvent(handle, NowMilliseconds(), ref platformEvent, out var hasEvent), "drain event");
                if (hasEvent == 0) return;
                DispatchEvent(platformEvent);
            }
        }
    }

    private void DispatchEvent(PlatformEvent platformEvent)
    {
        if (platformEvent.Kind == 1) GuideToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DispatchNavigation(NavigationEvent navigation)
    {
        var semantic = navigation.Direction switch
        {
            1u => SemanticInput.Left,
            2u => SemanticInput.Right,
            3u => SemanticInput.Up,
            4u => SemanticInput.Down,
            _ => (SemanticInput?)null,
        };
        if (semantic is { } input)
            InputReceived?.Invoke(this, new OverlayPlatformInput(input, Phase: navigation.Phase == 2
                ? ControllerEventPhase.Repeated
                : ControllerEventPhase.Pressed));
    }

    private void DispatchButtons(ushort buttons, ControllerEventPhase phase)
    {
        Dispatch(A, ControllerButton.A);
        Dispatch(B, ControllerButton.B);
        Dispatch(X, ControllerButton.X);
        Dispatch(Y, ControllerButton.Y);
        Dispatch(LeftBumper, ControllerButton.LeftBumper);
        Dispatch(RightBumper, ControllerButton.RightBumper);
        Dispatch(LeftThumb, ControllerButton.LeftStick);
        Dispatch(RightThumb, ControllerButton.RightStick);
        Dispatch(Menu, ControllerButton.Menu);
        Dispatch(View, ControllerButton.View);
        void Dispatch(ushort mask, ControllerButton button)
        {
            if ((buttons & mask) != 0) DispatchButton(button, phase);
        }
    }

    private void DispatchButton(ControllerButton button, ControllerEventPhase phase) =>
        InputReceived?.Invoke(this, new OverlayPlatformInput(Button: button, Phase: phase));

    private void OnEventAvailable(nint context) =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (disposed) return;
            try { DrainEvents(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        });
    private static void OnDiagnostic(nint context, nint message) { }
    private T Load<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private static ulong NowMilliseconds() => unchecked((ulong)Environment.TickCount64);
    private static PlatformEvent NewEvent() => new()
    {
        StructSize = (uint)Marshal.SizeOf<PlatformEvent>(),
        AbiVersion = AbiVersion,
    };
    private static ControllerFrame NewControllerFrame() => new()
    {
        StructSize = (uint)Marshal.SizeOf<ControllerFrame>(),
        AbiVersion = AbiVersion,
    };
    private static void ThrowIfFailed(uint status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"OverlayPlatformInterop {operation} failed ({status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PlatformEvent
    {
        public uint StructSize, AbiVersion, Kind, GuideSource;
        public ulong TimestampMilliseconds;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawControllerState
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftThumbX, LeftThumbY, RightThumbX, RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NavigationEvent { public uint Direction, Phase; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ControllerFrame
    {
        public uint StructSize, AbiVersion, Connected, ForegroundExclusive, ReadPath;
        public RawControllerState State;
        public ushort PressedButtons, ReleasedButtons;
        public uint LeftTriggerPressed, LeftTriggerReleased, RightTriggerPressed, RightTriggerReleased;
        public uint RecoveryChordPressed, Primed;
        public NavigationEvent StickNavigation, DpadNavigation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateOptions
    {
        public uint StructSize, AbiVersion;
        public nint CallbackContext;
        [MarshalAs(UnmanagedType.FunctionPtr)] public EventAvailable EventAvailable;
        [MarshalAs(UnmanagedType.FunctionPtr)] public Diagnostic Diagnostic;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PlacementInput
    {
        public uint StructSize, AbiVersion;
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Dpi;
        public float DesiredWidthDip, DesiredHeightDip, SideMarginDip, TopMarginDip, BottomMarginDip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public uint StructSize, AbiVersion;
        public int X, Y, Width, Height;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EventAvailable(nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Diagnostic(nint context, nint message);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Create(ref CreateOptions options, out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Initialize(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Shutdown(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Destroy(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint BoolQuery(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint NativeSetWindowState(nint handle, uint visible, uint focused);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint DrainEvent(nint handle, ulong now, ref PlatformEvent platformEvent, out uint hasEvent);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint PollLegacyGuide(nint handle, ulong now, ref PlatformEvent platformEvent, out uint hasEvent);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint PrimeController(nint handle, uint foreground, ulong now);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReadController(nint handle, uint foreground, ulong now, ref ControllerFrame frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint SetOwnedWindows(nint handle, nuint overlay, nuint backdrop);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint NativeComputePlacement(ref PlacementInput input, ref Placement output, out uint hasPlacement);
}
