using System.Diagnostics;
using Vortice.XInput;

namespace GameBarAlternative.AvaloniaPrototype.Input;

internal readonly record struct ControllerSnapshot(
    bool Connected,
    int DeviceSlot,
    float LeftX,
    float LeftY,
    bool DpadUp,
    bool DpadDown,
    bool DpadLeft,
    bool DpadRight,
    bool A,
    bool B);

internal interface IControllerStateSource
{
    ControllerSnapshot Read();
}

internal interface IControllerInputAdapter : IDisposable
{
    event EventHandler<SemanticInputEventArgs>? InputReceived;

    void Start();

    void SetActive(bool active);

    void ResetHeldState();
}

internal sealed class XInputStateSource : IControllerStateSource
{
    public ControllerSnapshot Read()
    {
        for (uint slot = 0; slot < 4; slot++)
        {
            if (!XInput.GetState(slot, out var state))
            {
                continue;
            }

            var gamepad = state.Gamepad;
            var buttons = gamepad.Buttons;
            return new ControllerSnapshot(
                true,
                (int)slot,
                NormalizeAxis(gamepad.LeftThumbX),
                NormalizeAxis(gamepad.LeftThumbY),
                buttons.HasFlag(GamepadButtons.DPadUp),
                buttons.HasFlag(GamepadButtons.DPadDown),
                buttons.HasFlag(GamepadButtons.DPadLeft),
                buttons.HasFlag(GamepadButtons.DPadRight),
                buttons.HasFlag(GamepadButtons.A),
                buttons.HasFlag(GamepadButtons.B));
        }

        return default;
    }

    private static float NormalizeAxis(short value) => value < 0 ? value / 32768f : value / 32767f;
}

internal sealed class ControllerStateProcessor
{
    internal static readonly TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(350);
    internal static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(100);
    internal const float DeadZone = 0.28f;

    private int activeSlot = -1;
    private SemanticInput? heldDirection;
    private TimeSpan nextRepeatAt;
    private bool aWasDown;
    private bool bWasDown;
    private bool awaitingNeutral;

    public IReadOnlyList<SemanticInput> Process(ControllerSnapshot snapshot, TimeSpan now)
    {
        if (!snapshot.Connected)
        {
            Reset(requireNeutral: true);
            return [];
        }

        if (snapshot.DeviceSlot != activeSlot)
        {
            Reset(requireNeutral: true);
            activeSlot = snapshot.DeviceSlot;
        }

        if (awaitingNeutral)
        {
            if (ResolveDirection(snapshot) is not null || snapshot.A || snapshot.B)
            {
                return [];
            }

            awaitingNeutral = false;
        }

        var emitted = new List<SemanticInput>(3);
        var direction = ResolveDirection(snapshot);
        if (direction != heldDirection)
        {
            heldDirection = direction;
            nextRepeatAt = now + InitialRepeatDelay;
            if (direction is { } pressed)
            {
                emitted.Add(pressed);
            }
        }
        else if (direction is { } repeated && now >= nextRepeatAt)
        {
            emitted.Add(repeated);
            nextRepeatAt = now + RepeatInterval;
        }

        if (snapshot.A && !aWasDown)
        {
            emitted.Add(SemanticInput.Activate);
        }

        if (snapshot.B && !bWasDown)
        {
            emitted.Add(SemanticInput.Back);
        }

        aWasDown = snapshot.A;
        bWasDown = snapshot.B;
        return emitted;
    }

    public void Reset(bool requireNeutral = false)
    {
        activeSlot = -1;
        heldDirection = null;
        nextRepeatAt = default;
        aWasDown = false;
        bWasDown = false;
        awaitingNeutral = requireNeutral;
    }

    private static SemanticInput? ResolveDirection(ControllerSnapshot snapshot)
    {
        if (snapshot.DpadUp) return SemanticInput.Up;
        if (snapshot.DpadDown) return SemanticInput.Down;
        if (snapshot.DpadLeft) return SemanticInput.Left;
        if (snapshot.DpadRight) return SemanticInput.Right;

        var x = Math.Abs(snapshot.LeftX) >= DeadZone ? snapshot.LeftX : 0;
        var y = Math.Abs(snapshot.LeftY) >= DeadZone ? snapshot.LeftY : 0;
        if (x == 0 && y == 0) return null;
        if (Math.Abs(x) > Math.Abs(y)) return x < 0 ? SemanticInput.Left : SemanticInput.Right;
        return y < 0 ? SemanticInput.Down : SemanticInput.Up;
    }
}

internal sealed class XInputControllerAdapter(IControllerStateSource stateSource) : IControllerInputAdapter
{
    private readonly ControllerStateProcessor processor = new();
    private readonly object gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private CancellationTokenSource? lifetime;
    private bool active;

    public event EventHandler<SemanticInputEventArgs>? InputReceived;

    public void Start()
    {
        lock (gate)
        {
            if (lifetime is not null) return;
            lifetime = new CancellationTokenSource();
            _ = PollAsync(lifetime.Token);
        }
    }

    public void SetActive(bool value)
    {
        lock (gate)
        {
            active = value;
            if (!value) processor.Reset(requireNeutral: true);
        }
    }

    public void ResetHeldState()
    {
        lock (gate) processor.Reset(requireNeutral: true);
    }

    public void Dispose()
    {
        lock (gate)
        {
            active = false;
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            processor.Reset(requireNeutral: true);
        }
    }

    private async Task PollAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                IReadOnlyList<SemanticInput> emitted;
                lock (gate)
                {
                    if (!active) continue;
                    emitted = processor.Process(stateSource.Read(), clock.Elapsed);
                }

                foreach (var input in emitted)
                {
                    InputReceived?.Invoke(this, new SemanticInputEventArgs(input, SemanticInputSource.Controller));
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
