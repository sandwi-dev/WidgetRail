using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayPlatformClient;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class BindingTests
{
    [TestMethod]
    public void AbiLayoutMatchesNativeStaticAssertions()
    {
        Assert.AreEqual(32, Marshal.SizeOf<PlatformEvent>());
        Assert.AreEqual(12, Marshal.SizeOf<RawControllerState>());
        Assert.AreEqual(8, Marshal.SizeOf<NavigationEvent>());
        Assert.AreEqual(84, Marshal.SizeOf<ControllerFrame>());
        Assert.AreEqual(48, Marshal.SizeOf<PlacementInput>());
        Assert.AreEqual(24, Marshal.SizeOf<Placement>());
        Assert.AreEqual(32, Marshal.SizeOf<PlatformCreateOptions>());
        Assert.AreEqual(16, Marshal.OffsetOf<PlatformEvent>(nameof(PlatformEvent.TimestampMilliseconds)).ToInt32());
        Assert.AreEqual(20, Marshal.OffsetOf<ControllerFrame>(nameof(ControllerFrame.State)).ToInt32());
        Assert.AreEqual(60, Marshal.OffsetOf<ControllerFrame>(nameof(ControllerFrame.StickNavigation)).ToInt32());
        Assert.AreEqual(80, Marshal.OffsetOf<ControllerFrame>(nameof(ControllerFrame.LastInputFamily)).ToInt32());
        Assert.AreEqual(8, Marshal.OffsetOf<PlatformCreateOptions>(nameof(PlatformCreateOptions.CallbackContext)).ToInt32());
    }

    [TestMethod]
    public void ImportsAndReverseCallbacksDeclareStdcall()
    {
        var imports = typeof(OverlayPlatformNative).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<LibraryImportAttribute>() is not null).ToArray();
        Assert.IsGreaterThan(0, imports.Length);
        foreach (var method in imports)
            CollectionAssert.Contains(method.GetCustomAttribute<UnmanagedCallConvAttribute>()!.CallConvs!, typeof(CallConvStdcall));
        var callbacks = typeof(OverlayPlatformSession).Assembly.GetType("WidgetRail.OverlayPlatformClient.PlatformCallbacks")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>()).Where(attribute => attribute is not null).ToArray();
        Assert.HasCount(2, callbacks);
        foreach (var callback in callbacks)
            CollectionAssert.Contains(callback!.CallConvs!, typeof(CallConvStdcall));
    }

    [TestMethod]
    public void OutputHeadersAndStateArgumentsAreInitialized()
    {
        var api = new FakeNative();
        using var session = new OverlayPlatformSession(api, new QueuedDispatcher(), () => { });
        session.SetWindowState(true, false);
        Assert.AreEqual((1U, 0U), api.WindowState);
        session.PrepareVisible();
        session.PrimeController(false, 901);
        var frame = session.ReadController(false, 902);
        Assert.AreEqual(902UL, api.LastNow);
        Assert.AreEqual(0U, api.LastForeground);
        Assert.AreEqual(1U, frame.Connected);
        Assert.AreEqual(3U, frame.RemainingFrames);
        Assert.AreEqual(ControllerFamily.PlayStation, frame.LastInputFamily);
        Assert.IsNull(session.ReadEvent(903));
        Assert.IsNull(session.PollLegacyGuide(904));
        Assert.IsNotNull(session.ComputePlacement(default));
        session.SetOwnedWindows(111, 222);
        Assert.AreEqual(((nuint)111, (nuint)222), api.OwnedWindows);
        Assert.IsTrue(session.ObserveForegroundTarget(333, true));
        Assert.AreEqual((nuint)333, session.RememberedForegroundTarget);
        Assert.AreEqual((nuint)333, session.ResolveForegroundTarget(444, true));
        Assert.AreEqual((NativeShortcutSource.Shared, (ushort)48), session.ReadNativeShortcut());
    }

    [TestMethod]
    public void VersionMismatchNeverCreatesOrInitializes()
    {
        var api = new FakeNative { Version = 99 };
        var error = Assert.Throws<PlatformException>(() => new OverlayPlatformSession(api, new QueuedDispatcher(), () => { }));
        Assert.AreEqual(PlatformStatus.InvalidVersion, error.Status);
        Assert.AreEqual(0, api.CreateCalls);
    }

    [TestMethod]
    public void InitializeFailureDestroysAndCancelsQueuedCallbacks()
    {
        var api = new FakeNative { InitStatus = PlatformStatus.ControllerIsolationUnavailable, SignalDuringInitialize = true };
        var queue = new QueuedDispatcher();
        var delivered = 0;
        Assert.Throws<PlatformException>(() => new OverlayPlatformSession(api, queue, () => delivered++));
        queue.Pump();
        Assert.AreEqual(1, api.DestroyCalls);
        Assert.AreEqual(0, delivered);
    }

    [TestMethod]
    public void CreateFailureReleasesReturnedOwnerWithoutInitializing()
    {
        var api = new FakeNative { CreateStatus = PlatformStatus.InvalidArgument };
        Assert.Throws<PlatformException>(() => new OverlayPlatformSession(api, new QueuedDispatcher(), () => { }));
        Assert.AreEqual(1, api.DestroyCalls);
        Assert.AreEqual(0, api.InitializeCalls);
    }

    [TestMethod]
    public void CreateFailureWithoutOwnerDoesNotDestroyInvalidHandle()
    {
        var api = new FakeNative { CreateStatus = PlatformStatus.AllocationFailed, CreatedHandle = 0 };
        Assert.Throws<PlatformException>(() => new OverlayPlatformSession(api, new QueuedDispatcher(), () => { }));
        Assert.AreEqual(0, api.DestroyCalls);
        Assert.AreEqual(0, api.InitializeCalls);
    }

    [TestMethod]
    public void CallbackDuringInitializeIsQueuedAndCoalesced()
    {
        var api = new FakeNative { SignalDuringInitialize = true };
        var queue = new QueuedDispatcher();
        var delivered = 0;
        using var session = new OverlayPlatformSession(api, queue, () => delivered++);
        api.Signal();
        api.Signal();
        Assert.AreEqual(0, delivered);
        Assert.AreEqual(1, queue.Count);
        GC.Collect(); GC.WaitForPendingFinalizers();
        queue.Pump();
        Assert.AreEqual(1, delivered);
    }

    [TestMethod]
    public void DiagnosticIsCopiedBeforeNativeMemoryChanges()
    {
        var api = new FakeNative();
        var queue = new QueuedDispatcher();
        var messages = new List<string>();
        using var session = new OverlayPlatformSession(api, queue, () => { }, messages.Add);
        api.Diagnose("Controller ready");
        queue.Pump();
        CollectionAssert.AreEqual(new[] { "Controller ready" }, messages);
    }

    [TestMethod]
    public void SignalDuringDispatchIsNotStranded()
    {
        var api = new FakeNative();
        var queue = new QueuedDispatcher();
        var delivered = 0;
        using var session = new OverlayPlatformSession(api, queue, () => { if (++delivered == 1) api.Signal(); });
        api.Signal();
        queue.Pump();
        Assert.AreEqual(2, delivered);
    }

    [TestMethod]
    public void RejectedDispatcherRetainsNotificationForRetry()
    {
        var api = new FakeNative();
        var queue = new QueuedDispatcher { Reject = true };
        var delivered = 0;
        using var session = new OverlayPlatformSession(api, queue, () => delivered++);
        api.Signal();
        queue.Reject = false;
        session.RetryPendingNotifications();
        queue.Pump();
        Assert.AreEqual(1, delivered);
    }

    [TestMethod]
    public void DisposalClosesAdmissionBeforeDestroyAndCancelsQueuedWork()
    {
        var api = new FakeNative { SignalDuringDestroy = true };
        var queue = new QueuedDispatcher();
        var delivered = 0;
        var session = new OverlayPlatformSession(api, queue, () => delivered++);
        api.Signal();
        session.Dispose();
        session.Dispose();
        queue.Pump();
        Assert.AreEqual(0, delivered);
        Assert.AreEqual(1, api.DestroyCalls);
        Assert.Throws<ObjectDisposedException>(() => session.ReadController(true, 1));
        session.RetryPendingNotifications();
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public async Task NativeCallAndDisposeCannotRace()
    {
        var api = new FakeNative();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        api.DuringRead = () => { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5))); };
        var session = new OverlayPlatformSession(api, new QueuedDispatcher(), () => { });
        var read = Task.Run(() => session.ReadController(true, 42));
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
        using var disposing = new ManualResetEventSlim();
        var dispose = Task.Run(() => { disposing.Set(); session.Dispose(); });
        Assert.IsTrue(disposing.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, api.DestroyCalls);
        release.Set();
        await Task.WhenAll(read, dispose);
        Assert.AreEqual(1, api.DestroyCalls);
    }

    [TestMethod]
    public async Task NativeDestroyDrainsInFlightCallbackBeforeContextIsReleased()
    {
        var api = new FakeNative();
        var queue = new QueuedDispatcher();
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var callbackReturned = new ManualResetEventSlim();
        using var destroyEntered = new ManualResetEventSlim();
        queue.BeforeEnqueue = () =>
        {
            callbackEntered.Set();
            Assert.IsTrue(releaseCallback.Wait(TimeSpan.FromSeconds(5)));
        };
        api.DuringDestroy = () =>
        {
            destroyEntered.Set();
            Assert.IsTrue(callbackReturned.Wait(TimeSpan.FromSeconds(5)));
        };
        var delivered = 0;
        var session = new OverlayPlatformSession(api, queue, () => delivered++);
        var signal = Task.Run(() => { api.Signal(); callbackReturned.Set(); });
        Assert.IsTrue(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
        var dispose = Task.Run(session.Dispose);
        Assert.IsTrue(destroyEntered.Wait(TimeSpan.FromSeconds(5)));
        releaseCallback.Set();
        await Task.WhenAll(signal, dispose);
        queue.Pump();
        Assert.AreEqual(0, delivered);
        Assert.AreEqual(1, api.DestroyCalls);
    }

    [TestMethod]
    public void ConsumerExceptionDoesNotEscapeDispatchOrStopFutureNotifications()
    {
        var api = new FakeNative();
        var queue = new QueuedDispatcher();
        using var session = new OverlayPlatformSession(api, queue, () => throw new InvalidOperationException());
        api.Signal(); queue.Pump();
        api.Signal(); queue.Pump();
        Assert.AreEqual(2L, session.CallbackFailures);
    }
}

internal sealed class QueuedDispatcher : IPlatformDispatcher
{
    private readonly Queue<Action> actions = new();
    public bool Reject { get; set; }
    public Action? BeforeEnqueue { get; set; }
    public int Count => actions.Count;
    public bool TryEnqueue(Action action)
    {
        if (Reject) return false;
        BeforeEnqueue?.Invoke();
        actions.Enqueue(action);
        return true;
    }
    public void Pump()
    {
        for (var i = 0; i < 1000 && actions.TryDequeue(out var action); i++) action();
        Assert.AreEqual(0, actions.Count);
    }
}

internal sealed unsafe class FakeNative : IOverlayPlatformNative
{
    private PlatformCreateOptions options;
    public uint Version { get; set; } = 4;
    public PlatformStatus InitStatus { get; set; }
    public PlatformStatus CreateStatus { get; set; }
    public nint CreatedHandle { get; set; } = 123;
    public bool SignalDuringInitialize { get; set; }
    public bool SignalDuringDestroy { get; set; }
    public int CreateCalls { get; private set; }
    public int InitializeCalls { get; private set; }
    public int DestroyCalls { get; private set; }
    public (uint, uint) WindowState { get; private set; }
    public (nuint, nuint) OwnedWindows { get; private set; }
    public ulong LastNow { get; private set; }
    public uint LastForeground { get; private set; }
    public nuint Foreground { get; private set; }
    public Action? DuringRead { get; set; }
    public Action? DuringDestroy { get; set; }
    public uint GetAbiVersion() => Version;
    public PlatformStatus Create(in PlatformCreateOptions value, out nint handle)
    {
        Assert.AreEqual(32U, value.StructSize);
        Assert.AreEqual(4U, value.AbiVersion);
        options = value; CreateCalls++; handle = CreatedHandle; return CreateStatus;
    }
    public PlatformStatus Initialize(nint handle) { InitializeCalls++; if (SignalDuringInitialize) Signal(); return InitStatus; }
    public void Shutdown(nint handle) { }
    public void Destroy(nint handle) { DuringDestroy?.Invoke(); if (SignalDuringDestroy) Signal(); DestroyCalls++; }
    public uint HasGameInput(nint handle) => 1;
    public uint RequiresLegacyGuidePolling(nint handle) => 0;
    public PlatformStatus SetWindowState(nint handle, uint visible, uint focused) { WindowState = (visible, focused); return PlatformStatus.Ok; }
    public PlatformStatus PrepareVisible(nint handle) => PlatformStatus.Ok;
    public PlatformStatus DrainEvent(nint handle, ulong now, ref PlatformEvent value, out uint present)
    {
        Assert.AreEqual(32U, value.StructSize); Assert.AreEqual(4U, value.AbiVersion);
        present = 0; return PlatformStatus.Ok;
    }
    public PlatformStatus PollLegacyGuide(nint handle, ulong now, ref PlatformEvent value, out uint present) => DrainEvent(handle, now, ref value, out present);
    public PlatformStatus PrimeController(nint handle, uint foreground, ulong now) { LastNow = now; LastForeground = foreground; return PlatformStatus.Ok; }
    public PlatformStatus ReadController(nint handle, uint foreground, ulong now, ref ControllerFrame frame)
    {
        Assert.AreEqual(84U, frame.StructSize); Assert.AreEqual(4U, frame.AbiVersion);
        DuringRead?.Invoke();
        LastNow = now; LastForeground = foreground;
        frame.Connected = 1; frame.RemainingFrames = 3; frame.LastInputFamily = ControllerFamily.PlayStation;
        return PlatformStatus.Ok;
    }
    public PlatformStatus SetOwnedWindows(nint handle, nuint overlay, nuint backdrop) { OwnedWindows = (overlay, backdrop); return PlatformStatus.Ok; }
    public uint ObserveForegroundTarget(nint handle, nuint candidate, uint valid) { if (valid != 0) Foreground = candidate; return valid; }
    public nuint RememberedForegroundTarget(nint handle) => Foreground;
    public nuint ResolveForegroundTarget(nint handle, nuint fallback, uint valid) => valid != 0 ? Foreground : fallback;
    public PlatformStatus ComputePlacement(in PlacementInput input, ref Placement output, out uint present)
    {
        Assert.AreEqual(48U, input.StructSize); Assert.AreEqual(4U, input.AbiVersion);
        Assert.AreEqual(24U, output.StructSize); Assert.AreEqual(4U, output.AbiVersion);
        output.Width = 100; present = 1; return PlatformStatus.Ok;
    }
    public NativeShortcutSource NativeShortcutButtons(nint handle, out ushort buttons) { buttons = 48; return NativeShortcutSource.Shared; }
    public void Signal() => ((delegate* unmanaged[Stdcall]<nint, void>)options.EventAvailable)(options.CallbackContext);
    public void Diagnose(string value)
    {
        var buffer = (value + '\0').ToCharArray();
        fixed (char* text = buffer)
            ((delegate* unmanaged[Stdcall]<nint, char*, void>)options.Diagnostic)(options.CallbackContext, text);
        Array.Fill(buffer, 'x');
    }
}
