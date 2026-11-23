using System.Collections.Concurrent;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class CoordinatorTests
{
    internal static async Task SaveThenStop()
    {
        await using var f = new ReplayFixture();
        var factory = new Factory(); var errors = new ConcurrentQueue<Exception>();
        var host = new Host(f.Service);
        await using var coordinator = new ReplayCaptureCoordinator(host, factory, (_, _) => Task.FromResult(Decision()), errors.Enqueue);
        coordinator.Start();
        await Wait(() => host.Polls > 0);
        var started = await f.Service.StartAsync(new object(), ReplayFixture.Actor).WaitAsync(TimeSpan.FromSeconds(5));
        PolicyTests.Check(started.Outcome == CaptureReplayOutcome.Accepted, "Coordinator failed to admit source");
        await Wait(() => f.Service.Status.Phase == CaptureReplayPhase.Recording);
        var session = started.Status.SessionId!;
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, new(session, Guid.NewGuid().ToString("N")));
        await Wait(() => factory.Backend.SavePath is not null);
        await f.Service.StopAsync(session);
        await Wait(() => factory.Backend.Stopped);
        var renewals = factory.Backend.Renewals;
        await Wait(() => factory.Backend.Renewals > renewals);
        PolicyTests.Check(factory.Backend.Events.ToArray() is ["save", "stop"], "Stop overtook accepted native save");
        await factory.Backend.CompleteSave();
        PolicyTests.Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Outcome == CaptureReplayOutcome.Accepted, "Coordinator discarded drained output");
        await Wait(() => factory.Backend.Disposed);
        PolicyTests.Check(errors.IsEmpty && factory.Starts == 1, "Graceful save generated errors or new worker");
    }

    internal static async Task StartNotices()
    {
        foreach (var automatic in new[] { false, true })
        {
            await using var f = new ReplayFixture(enabled: automatic, automatic: automatic);
            var host = new Host(f.Service);
            var factory = new Factory();
            factory.Backend.Phase = CaptureReplayPhase.Starting;
            var notices = new ConcurrentQueue<(string Session, string App)>();
            await using var coordinator = new ReplayCaptureCoordinator(host, factory, (_, _) => Task.FromResult(Decision()));
            coordinator.RecordingStarted += (_, session, name) => notices.Enqueue((session, name));
            coordinator.Start();
            await Wait(() => host.Polls > 0);
            if (!automatic) await f.Service.StartAsync(new object(), ReplayFixture.Actor).WaitAsync(TimeSpan.FromSeconds(5));
            await Wait(() => factory.Backend.Renewals >= 2);
            PolicyTests.Check(notices.IsEmpty, "A pending recorder announced a start.");
            factory.Backend.Phase = CaptureReplayPhase.Recording;
            await Wait(() => notices.Count == 1);
            var id = f.Service.Status.SessionId!;
            factory.Backend.Phase = CaptureReplayPhase.Paused;
            await Wait(() => f.Service.Status.Phase == CaptureReplayPhase.Paused);
            factory.Backend.Phase = CaptureReplayPhase.Recording;
            await Wait(() => f.Service.Status.Phase == CaptureReplayPhase.Recording);
            PolicyTests.Check(notices.ToArray() is [var started] && started == (id, "Fixture game"),
                "The notification lost its application or repeated on resume.");
        }
    }

    internal static async Task CancelAndSuspend()
    {
        await using var f = new ReplayFixture(); var factory = new Factory();
        var chooser = new TaskCompletionSource<ReplayCaptureDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new ConcurrentQueue<Exception>();
        var host = new Host(f.Service);
        await using var coordinator = new ReplayCaptureCoordinator(host, factory, async (_, token) =>
        { entered.TrySetResult(); return await chooser.Task.WaitAsync(token); }, errors.Enqueue);
        coordinator.Start(); await Wait(() => host.Polls > 0);
        using var cancellation = new CancellationTokenSource();
        var start = f.Service.StartAsync(new object(), ReplayFixture.Actor, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        try { await start; } catch (OperationCanceledException) { }
        chooser.TrySetResult(Decision()); await Task.Delay(800);
        PolicyTests.Check(factory.Starts == 0 && f.Service.Status.SessionId is null, "Cancelled chooser launched native recording");
        var accepted = await f.Service.StartAsync(new object(), ReplayFixture.Actor).WaitAsync(TimeSpan.FromSeconds(5));
        PolicyTests.Check(accepted.Outcome == CaptureReplayOutcome.Accepted, "Second current source failed");
        await Wait(() => factory.Backend.Renewals > 0);
        coordinator.SetSuspended(true);
        await Wait(() => factory.Backend.Disposed && f.Service.Status.SessionId is null);
        coordinator.SetSuspended(false); await Task.Delay(800);
        PolicyTests.Check(factory.Starts == 1 && f.Service.Status.Settings.Enabled && errors.IsEmpty,
            "Manual resume restarted acquisition, erased intent or emitted teardown errors");
    }
    private static ReplayCaptureDecision Decision() => new(true, new("123", 456, "789", "FixtureWindow"), ReplayFixture.Application, "Fixture game");
    internal static async Task TransportFailure()
    {
        await using var f = new ReplayFixture(); var host = new Host(f.Service); var factory = new Factory();
        var errors = new ConcurrentQueue<Exception>();
        await using var coordinator = new ReplayCaptureCoordinator(host, factory, (_, _) => Task.FromResult(Decision()), errors.Enqueue);
        coordinator.Start(); await Wait(() => host.Polls > 0);
        PolicyTests.Check((await f.Service.StartAsync(new object(), ReplayFixture.Actor).WaitAsync(TimeSpan.FromSeconds(5))).Outcome == CaptureReplayOutcome.Accepted,
            "Transport failure fixture did not start");
        await Wait(() => factory.Backend.Renewals > 0); host.Fail = true;
        await Wait(() => factory.Backend.Disposed);
        await coordinator.DisposeAsync();
        PolicyTests.Check(!errors.IsEmpty && factory.Backend.Disposed, "Lost broker left recorder alive or unreported");
    }
    internal static async Task BusyThenReady()
    {
        await using var f = new ReplayFixture(); var host = new Host(f.Service); var factory = new Factory(); var clock = new Clock();
        var errors = new ConcurrentQueue<Exception>();
        await using var coordinator = new ReplayCaptureCoordinator(host, factory, (_, _) => Task.FromResult(Decision()), errors.Enqueue, clock);
        coordinator.Start(); await Wait(() => host.Polls > 0);
        await f.Service.StartAsync(new object(), ReplayFixture.Actor).WaitAsync(TimeSpan.FromSeconds(5));
        await Wait(() => factory.Backend.Renewals > 0); host.Busy = true; await Wait(() => host.BusyPolls > 0);
        var renewals = factory.Backend.Renewals; var busy = host.BusyPolls; clock.Advance(TimeSpan.FromSeconds(9));
        await Wait(() => host.BusyPolls > busy);
        PolicyTests.Check(!factory.Backend.Disposed && factory.Backend.Renewals == renewals, "Capacity refusal either killed healthy recording or renewed stale authority");
        host.Busy = false; await Wait(() => factory.Backend.Renewals > renewals);
        PolicyTests.Check(factory.Starts == 1 && !factory.Backend.Disposed && errors.IsEmpty, "Fresh broker acknowledgment did not retain the same recorder");
    }
    internal static async Task BusyExpires()
    {
        await using var f = new ReplayFixture(); var host = new Host(f.Service); var factory = new Factory(); var clock = new Clock();
        var errors = new ConcurrentQueue<Exception>();
        await using var coordinator = new ReplayCaptureCoordinator(host, factory, (_, _) => Task.FromResult(Decision()), errors.Enqueue, clock);
        coordinator.Start(); await Wait(() => host.Polls > 0);
        await f.Service.StartAsync(new object(), ReplayFixture.Actor).WaitAsync(TimeSpan.FromSeconds(5));
        await Wait(() => factory.Backend.Renewals > 0); host.Busy = true; await Wait(() => host.BusyPolls > 0);
        var renewals = factory.Backend.Renewals; var busy = host.BusyPolls; clock.Advance(ReplayBackendAuthority.LeaseDuration - TimeSpan.FromSeconds(1));
        await Wait(() => host.BusyPolls > busy); PolicyTests.Check(!factory.Backend.Disposed, "Capacity refusal shortened existing native authority");
        clock.Advance(TimeSpan.FromSeconds(1)); await Wait(() => factory.Backend.Disposed);
        PolicyTests.Check(factory.Backend.Renewals == renewals && !errors.IsEmpty, "Capacity retries extended native authority");
        busy = host.BusyPolls; await Wait(() => host.BusyPolls > busy); // A retirement report must survive another refusal.
        host.Busy = false; await Wait(() => f.Service.Status.SessionId is null);
        PolicyTests.Check(factory.Starts == 1 && f.Service.Status.Issue == CaptureReplayIssue.HostUnavailable, "Recovery lost retirement report or revived the expired worker");
    }
    internal static async Task OneEncodingWhileCommitPending()
    {
        await using var f = new ReplayFixture(); var host = new Host(f.Service); var factory = new Factory();
        var inspecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishInspection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.InspectBarrier = async token => { inspecting.TrySetResult(); await finishInspection.Task.WaitAsync(token); };
        await using var coordinator = new ReplayCaptureCoordinator(host, factory, (_, _) => Task.FromResult(Decision()));
        coordinator.Start(); await Wait(() => host.Polls > 0);
        var start = await f.Service.StartAsync(new object(), ReplayFixture.Actor).WaitAsync(TimeSpan.FromSeconds(5));
        await Wait(() => f.Service.Status.BufferedDurationSeconds > 0);
        var save = f.Service.SaveAsync(ReplayFixture.Actor, new(start.Status.SessionId!, Guid.NewGuid().ToString("N")));
        await Wait(() => factory.Backend.SavePath is not null); await factory.Backend.CompleteSave();
        await inspecting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var polls = host.Polls; await Wait(() => host.Polls >= polls + 3);
            PolicyTests.Check(factory.Backend.Events.Count(item => item == "save") == 1,
                "The same host command encoded its staging path again after native completion");
        }
        finally { finishInspection.TrySetResult(); }
        PolicyTests.Check((await save.WaitAsync(TimeSpan.FromSeconds(5))).Outcome == CaptureReplayOutcome.Accepted,
            "Retaining completed execution prevented library commit");
    }
    private static async Task Wait(Func<bool> condition, int timeout = 5000)
    {
        using var stop = new CancellationTokenSource(timeout);
        while (!condition()) await Task.Delay(10, stop.Token);
    }
    private sealed class Host(CaptureReplayService service) : IReplayCaptureHost
    {
        internal int Polls, BusyPolls; internal volatile bool Fail, Busy;
        public async Task<HostReplayWork> PollAsync(HostReplayReport? report, CancellationToken token)
        { if (Fail) throw new IOException("Injected closed bridge"); if (Busy) { Interlocked.Increment(ref BusyPolls); throw new ReplayHostBusyException(); }
            var result = await service.PollHostAsync(report, token); Interlocked.Increment(ref Polls); return result; }
        public Task<bool> IsCurrentAsync(string id, CancellationToken token) => service.IsHostCommandCurrentAsync(id, token);
        public Task<bool> CompleteAsync(HostReplayCompletion result, CancellationToken token) => service.CompleteHostAsync(result, token);
        public Task SetPowerAsync(long revision, bool suspended, CancellationToken token) => service.SetHostPowerStateAsync(revision, suspended, token);
    }
    private sealed class Factory : IReplayCaptureBackendFactory
    {
        internal readonly Backend Backend = new(); internal int Starts;
        public Task<IReplayCaptureBackend> StartAsync(NativeWindowPreviewTarget target, CaptureReplaySettings settings, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Interlocked.Increment(ref Starts); return Task.FromResult<IReplayCaptureBackend>(Backend); }
    }
    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
    }
    private sealed class Backend : IReplayCaptureBackend
    {
        internal string? SavePath; internal volatile bool Stopped, Disposed, Ended;
        internal volatile CaptureReplayPhase Phase = CaptureReplayPhase.Recording;
        internal int Renewals; internal readonly ConcurrentQueue<string> Events = new();
        private readonly TaskCompletionSource<ReplayBackendSaveResult> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ReplayBackendState> ReadStateAsync(CancellationToken token) => Task.FromResult(new ReplayBackendState(
            Stopped ? CaptureReplayPhase.Stopping : Phase, 2, 1024, 0, Ended: Ended));
        public void RenewAuthority() { if (Disposed) throw new ObjectDisposedException(nameof(Backend)); Interlocked.Increment(ref Renewals); }
        public Task<ReplayBackendSaveResult> SaveAsync(string path, int seconds, CancellationToken token)
        { Events.Enqueue("save"); SavePath = path; return saved.Task.WaitAsync(token); }
        public Task StopAsync(CancellationToken token) { Events.Enqueue("stop"); Stopped = true; return saved.Task.WaitAsync(token); }
        internal async Task CompleteSave()
        {
            await File.WriteAllBytesAsync(SavePath!, ReplayFixture.Bytes); Ended = Stopped;
            saved.TrySetResult(new(1280, 720, 2, ReplayFixture.Bytes.Length, new(1280, 720, 30, 60, 0, CaptureEncoderMode.Hardware, CaptureAudioMode.Application)));
        }
        public ValueTask DisposeAsync() { Disposed = true; saved.TrySetCanceled(); return ValueTask.CompletedTask; }
    }
}
