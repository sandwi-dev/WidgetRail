namespace WidgetRail.OverlayPlatformClient;

/// <summary>
/// Serialized platform lifetime. Construction does not enable isolation or alter
/// driver policy. The application must dispose this owner before dispatcher exit.
/// </summary>
public sealed class OverlayPlatformSession : IDisposable
{
    private readonly object gate = new();
    private readonly IOverlayPlatformNative native;
    private readonly PlatformCallbacks callbacks;
    private readonly PlatformSafeHandle handle;
    private bool disposed;

    public OverlayPlatformSession(
        IOverlayPlatformNative native,
        IPlatformDispatcher dispatcher,
        Action eventAvailable,
        Action<string>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(eventAvailable);
        this.native = native;
        if (native.GetAbiVersion() != PlatformAbi.Version)
            throw new PlatformException("GetAbiVersion", PlatformStatus.InvalidVersion);

        callbacks = new PlatformCallbacks(dispatcher, eventAvailable, diagnostic);
        nint value = 0;
        PlatformSafeHandle? created = null;
        try
        {
            var options = callbacks.CreateOptions();
            var status = native.Create(in options, out value);
            if (value != 0 && value != -1) created = new PlatformSafeHandle(value, native, callbacks);
            Check(status, "Create");
            if (created is null) throw new PlatformException("Create", PlatformStatus.AllocationFailed);
            handle = created;
            Check(native.Initialize(value), "Initialize");
        }
        catch
        {
            callbacks.CloseAdmission();
            if (created is not null) created.Dispose();
            else callbacks.Dispose();
            throw;
        }
    }

    public long CallbackFailures => callbacks.Failures;
    public void RetryPendingNotifications() => callbacks.RetryDispatch();

    public bool HasGameInput => Invoke(h => native.HasGameInput(h) != 0);
    public bool RequiresLegacyGuidePolling => Invoke(h => native.RequiresLegacyGuidePolling(h) != 0);
    /// <summary>Read-only driver readiness. Query on the control plane, not per input frame.</summary>
    public PlatformControllerPrerequisites ControllerPrerequisites => Invoke(_ =>
        (PlatformControllerPrerequisites)native.ControllerPrerequisites());
    public PlatformControllerControlState ControllerControlState => Invoke(h =>
        (PlatformControllerControlState)native.ControllerControlState(h));

    /// <summary>
    /// Applies one explicit isolation request. Expected setup/recovery failures
    /// return false so the owner can report the native state without tearing down
    /// ordinary input. Other ABI errors throw; this method never retries.
    /// </summary>
    public bool SetExclusiveControl(bool enabled) => Invoke(h =>
    {
        var status = native.SetExclusiveControl(h, Flag(enabled));
        if (status == PlatformStatus.ControllerIsolationUnavailable) return false;
        Check(status, "SetExclusiveControl");
        return true;
    });

    public void PrepareVisible() => Invoke(h => Check(native.PrepareVisible(h), "PrepareVisible"));
    public void SetWindowState(bool visible, bool focused) => Invoke(h =>
        Check(native.SetWindowState(h, Flag(visible), Flag(focused)), "SetWindowState"));
    public void PrimeController(bool foregroundConfirmed, ulong nowMilliseconds) => Invoke(h =>
        Check(native.PrimeController(h, Flag(foregroundConfirmed), nowMilliseconds), "PrimeController"));

    public ControllerFrame ReadController(bool foregroundConfirmed, ulong nowMilliseconds) => Invoke(h =>
    {
        var frame = ControllerFrame.Create();
        Check(native.ReadController(h, Flag(foregroundConfirmed), nowMilliseconds, ref frame), "ReadController");
        return frame;
    });

    /// <summary>Reads one event. The caller controls a bounded drain and rescheduling.</summary>
    public PlatformEvent? ReadEvent(ulong nowMilliseconds) => Invoke<PlatformEvent?>(h =>
    {
        var value = PlatformEvent.Create();
        Check(native.DrainEvent(h, nowMilliseconds, ref value, out var present), "DrainEvent");
        return present != 0 ? value : null;
    });

    public PlatformEvent? PollLegacyGuide(ulong nowMilliseconds) => Invoke<PlatformEvent?>(h =>
    {
        var value = PlatformEvent.Create();
        Check(native.PollLegacyGuide(h, nowMilliseconds, ref value, out var present), "PollLegacyGuide");
        return present != 0 ? value : null;
    });

    public void SetOwnedWindows(nuint overlay, nuint backdrop = 0) => Invoke(h =>
        Check(native.SetOwnedWindows(h, overlay, backdrop), "SetOwnedWindows"));
    /// <summary>
    /// Makes one foreground acquisition attempt for the registered overlay window.
    /// Call on its owning UI thread after showing it. False means Windows did not
    /// confirm foreground ownership; no persistent activation retry is scheduled.
    /// The frontend remains responsible for restoring focus inside its UI tree.
    /// </summary>
    public bool AcquireForeground() => Invoke(h =>
    {
        Check(native.AcquireForeground(h, out var confirmed), "AcquireForeground");
        return confirmed != 0;
    });
    public bool ObserveForegroundTarget(nuint candidate, bool valid) => Invoke(h =>
        native.ObserveForegroundTarget(h, candidate, Flag(valid)) != 0);
    public nuint RememberedForegroundTarget => Invoke(native.RememberedForegroundTarget);
    public nuint ResolveForegroundTarget(nuint fallback, bool rememberedValid) => Invoke(h =>
        native.ResolveForegroundTarget(h, fallback, Flag(rememberedValid)));
    public (NativeShortcutSource Source, ushort Buttons) ReadNativeShortcut() => Invoke(h =>
    {
        var source = native.NativeShortcutButtons(h, out var buttons);
        return (source, buttons);
    });

    public void SetViewMenuShortcut(bool enabled) => Invoke(h =>
        Check(native.SetViewMenuShortcut(h, Flag(enabled)), "SetViewMenuShortcut"));
    public (bool Pressed, bool Consumed) PollViewMenuShortcut() => Invoke(h =>
    {
        Check(native.PollViewMenuShortcut(h, out var pressed, out var consumed), "PollViewMenuShortcut");
        return (pressed != 0, consumed != 0);
    });

    public Placement? ComputePlacement(PlacementInput input) => Invoke<Placement?>(_ =>
    {
        input.StructSize = 48;
        input.AbiVersion = PlatformAbi.Version;
        var output = Placement.Create();
        Check(native.ComputePlacement(in input, ref output, out var present), "ComputePlacement");
        return present != 0 ? output : null;
    });

    private T Invoke<T>(Func<nint, T> action)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var added = false;
            try
            {
                handle.DangerousAddRef(ref added);
                return action(handle.DangerousGetHandle());
            }
            finally
            {
                if (added) handle.DangerousRelease();
            }
        }
    }

    private void Invoke(Action<nint> action) => Invoke(h => { action(h); return 0; });
    private static uint Flag(bool value) => value ? 1U : 0U;
    private static void Check(PlatformStatus status, string operation)
    {
        if (status != PlatformStatus.Ok) throw new PlatformException(operation, status);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            callbacks.CloseAdmission();
            // Destroy performs the native Shutdown. Keeping one release path
            // also covers initialization failures and SafeHandle finalization.
            handle.Dispose();
        }
    }
}
