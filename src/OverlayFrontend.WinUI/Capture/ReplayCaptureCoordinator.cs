using System.Diagnostics;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal sealed record ReplayCaptureDecision(bool Approved, NativeWindowPreviewTarget? Target = null,
    string? ApplicationIdentity = null, string? ApplicationName = null, CaptureReplayIssue Issue = CaptureReplayIssue.Cancelled);

internal sealed class ReplayHostBusyException(Exception? inner = null) : Exception("The replay work queue is temporarily full.", inner);

internal interface IReplayCaptureHost
{
    Task<HostReplayWork> PollAsync(HostReplayReport? report, CancellationToken token);
    Task<bool> IsCurrentAsync(string commandId, CancellationToken token);
    Task<bool> CompleteAsync(HostReplayCompletion result, CancellationToken token);
    Task SetPowerAsync(long revision, bool suspended, CancellationToken token);
}

/// <summary>Host executor only. Policy/persistence remain in the broker; chooser callbacks alone enter the UI thread.</summary>
internal sealed class ReplayCaptureCoordinator(IReplayCaptureHost host, IReplayCaptureBackendFactory factory,
    Func<HostReplayWork, CancellationToken, Task<ReplayCaptureDecision>> choose, Action<Exception>? reportFailure = null,
    TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private Hosted? current;
    private HostReplayReport? retiredReport;
    private Execution? execution;
    private Task? running, retiring;
    private int suspended, disposed;
    private long powerRevision, sentPowerRevision;
    private bool reportedHostFailure;

    private sealed class Hosted(string id, IReplayCaptureBackend backend, CancellationToken parent, long admittedAt)
    {
        internal readonly string Id = id;
        internal readonly IReplayCaptureBackend Backend = backend;
        internal readonly CancellationTokenSource Lifetime = CancellationTokenSource.CreateLinkedTokenSource(parent);
        internal Task? Stop;
        internal bool StopRequested;
        internal CaptureReplayIssue Failure;
        internal long AuthorityAcknowledgedAt = admittedAt;
    }
    private sealed class Execution(HostReplayCommand command, CancellationToken parent)
    {
        internal readonly HostReplayCommand Command = command;
        internal readonly CancellationTokenSource Lifetime = CancellationTokenSource.CreateLinkedTokenSource(parent);
        internal Task Work = Task.CompletedTask;
    }
    internal void Start()
    {
        lock (gate) running ??= Task.Run(RunAsync);
    }
    internal void SetSuspended(bool value)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (Interlocked.Exchange(ref suspended, value ? 1 : 0) == (value ? 1 : 0)) return;
        Interlocked.Exchange(ref powerRevision, Stopwatch.GetTimestamp());
        if (!value) return;
        Hosted? previous; Execution? pending;
        lock (gate) { previous = current; pending = execution; }
        Cancel(pending?.Lifetime);
        if (previous is not null)
        {
            Cancel(previous.Lifetime);
            var retirement = RetireAsync(previous);
            lock (gate) retiring = retirement;
        }
    }
    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                try { await TickAsync(lifetime.Token).ConfigureAwait(false); reportedHostFailure = false; }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (ReplayHostBusyException error)
                {
                    Hosted? previous; lock (gate) previous = current;
                    // A capacity refusal is not a renewed grant. Keep the last
                    // admitted recorder only inside its unchanged native lease.
                    if (previous is not null && (previous.Failure != CaptureReplayIssue.None ||
                        clock.GetElapsedTime(Interlocked.Read(ref previous.AuthorityAcknowledgedAt)) >= ReplayBackendAuthority.LeaseDuration))
                    {
                        if (!reportedHostFailure) { reportedHostFailure = true; reportFailure?.Invoke(error); }
                        await ObserveCleanupAsync(RetireAsync(previous)).ConfigureAwait(false);
                    }
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
                {
                    if (!reportedHostFailure) { reportedHostFailure = true; reportFailure?.Invoke(error); }
                    Hosted? previous; lock (gate) previous = current;
                    if (previous is not null) await ObserveCleanupAsync(RetireAsync(previous)).ConfigureAwait(false);
                }
                if (!await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false)) break;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private async Task TickAsync(CancellationToken token)
    {
        var power = Interlocked.Read(ref powerRevision);
        if (power > sentPowerRevision)
        { await host.SetPowerAsync(power, Volatile.Read(ref suspended) != 0, token).ConfigureAwait(false); sentPowerRevision = power; }
        Hosted? active; Execution? pending; HostReplayReport? report; HostReplayReport? retirement;
        lock (gate) { active = current; pending = execution; report = retirement = retiredReport; }
        ReplayBackendState? native = null;
        if (active is not null)
        {
            try { native = await active.Backend.ReadStateAsync(token).ConfigureAwait(false); }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { native = new(CaptureReplayPhase.Error, 0, 0, 0, CaptureReplayIssue.HostUnavailable, Ended: true); }
            report = new(active.Id, native.Phase, native.BufferedDurationSeconds, native.ResidentBytes, native.DroppedFrames,
                active.Failure != CaptureReplayIssue.None ? active.Failure : native.Issue, native.Ended || active.Failure != CaptureReplayIssue.None);
        }
        var work = await host.PollAsync(report, token).ConfigureAwait(false);
        lock (gate) if (ReferenceEquals(retiredReport, retirement)) retiredReport = null;
        if (active is not null && (work.AbortRequested || work.SessionId != active.Id || Volatile.Read(ref suspended) != 0))
        { await RetireAsync(active).ConfigureAwait(false); active = null; }
        if (active is not null && native?.Ended != true)
            try { active.Backend.RenewAuthority(); Interlocked.Exchange(ref active.AuthorityAcknowledgedAt, clock.GetTimestamp()); }
            catch (ReplayBackendException error) { active.Failure = error.Issue; }

        if (pending is not null && pending.Work.IsCompleted)
        {
            // A native result can be acknowledged before inspection/index commit
            // withdraws the host command. Keep this execution until that boundary;
            // seeing the same level-triggered command again is not a new save.
            var retained = work.Command?.Id == pending.Command.Id;
            try { await pending.Work.ConfigureAwait(false); }
            catch { retained = false; throw; }
            finally
            {
                if (!retained)
                {
                    lock (gate) if (ReferenceEquals(execution, pending)) execution = null;
                    pending.Lifetime.Dispose(); pending = null;
                }
            }
        }
        if (pending is not null && work.Command?.Id != pending.Command.Id && !AlreadyAdmitted(work, pending.Command))
            Cancel(pending.Lifetime);
        if (pending is null && work.Command is { } command && Volatile.Read(ref suspended) == 0 && retiring is not { IsCompleted: false })
        {
            var next = new Execution(command, token);
            lock (gate) execution = next;
            // Save admission must happen synchronously before a simultaneous Stop.
            next.Work = command.Kind == HostReplayCommandKind.Save ? BeginSave(next, active) : RunControlAsync(next, work);
        }
        if (active is not null && work.StopRequested && !active.StopRequested)
        { active.StopRequested = true; active.Stop = StopAsync(active); }
    }
    private static bool AlreadyAdmitted(HostReplayWork work, HostReplayCommand command) => command.Kind switch
    {
        HostReplayCommandKind.Start => work.SessionId == command.SessionId,
        HostReplayCommandKind.ApproveSettings => work.Status.SettingsRevision == command.SettingsRevision + 1 && work.Status.Settings == command.Settings,
        HostReplayCommandKind.Save => work.SessionId == command.SessionId && !work.Status.Saving,
        _ => false,
    };
    private async Task RunControlAsync(Execution operation, HostReplayWork work)
    {
        var command = operation.Command; var token = operation.Lifetime.Token;
        IReplayCaptureBackend? backend = null;
        try
        {
            if (!await host.IsCurrentAsync(command.Id, token).ConfigureAwait(false)) return;
            var decision = await choose(work, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!decision.Approved)
            { await CompleteAsync(new(command.Id, decision.Issue == CaptureReplayIssue.Cancelled ? CaptureReplayOutcome.Cancelled : CaptureReplayOutcome.Unavailable,
                decision.Issue) { SessionId = command.SessionId }).ConfigureAwait(false); return; }
            if (!await host.IsCurrentAsync(command.Id, token).ConfigureAwait(false)) return;
            if (command.Kind == HostReplayCommandKind.ApproveSettings)
            { await CompleteAsync(new(command.Id, CaptureReplayOutcome.Accepted)).ConfigureAwait(false); return; }
            if (decision.Target is null || decision.ApplicationIdentity is null || decision.ApplicationName is null ||
                command.Automatic && !string.Equals(decision.ApplicationIdentity, command.ApprovedApplication, StringComparison.OrdinalIgnoreCase))
                throw new ReplayBackendException(CaptureReplayIssue.TargetUnavailable);
            backend = await factory.StartAsync(decision.Target, command.Settings, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!await host.IsCurrentAsync(command.Id, token).ConfigureAwait(false)) return;
            if (!await CompleteAsync(new(command.Id, CaptureReplayOutcome.Accepted) { SessionId = command.SessionId,
                Target = decision.Target, ApprovedApplication = decision.ApplicationIdentity, ApplicationName = decision.ApplicationName,
                Resolved = backend.ResolvedConfiguration }).ConfigureAwait(false)) return;
            var admitted = new Hosted(command.SessionId!, backend, lifetime.Token, clock.GetTimestamp());
            lock (gate)
            {
                if (current is not null || Volatile.Read(ref suspended) != 0 || lifetime.IsCancellationRequested) { admitted.Lifetime.Dispose(); return; }
                current = admitted; backend = null;
            }
            try { admitted.Backend.RenewAuthority(); Interlocked.Exchange(ref admitted.AuthorityAcknowledgedAt, clock.GetTimestamp()); }
            catch (ReplayBackendException error) { admitted.Failure = error.Issue; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var issue = error is ReplayBackendException native ? native.Issue : CaptureReplayIssue.HostUnavailable;
            await CompleteAsync(new(command.Id, CaptureReplayOutcome.Unavailable, issue) { SessionId = command.SessionId }).ConfigureAwait(false);
        }
        finally { if (backend is not null) await backend.DisposeAsync().ConfigureAwait(false); }
    }
    private Task BeginSave(Execution operation, Hosted? active)
    {
        try
        {
            if (active is null || active.Id != operation.Command.SessionId || operation.Command.OutputPath is null)
                throw new ReplayBackendException(CaptureReplayIssue.SessionChanged);
            var save = active.Backend.SaveAsync(operation.Command.OutputPath, operation.Command.Settings.ReplayDurationSeconds, operation.Lifetime.Token);
            return FinishSaveAsync(operation, save);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { return FinishSaveAsync(operation, Task.FromException<ReplayBackendSaveResult>(error)); }
    }
    private async Task FinishSaveAsync(Execution operation, Task<ReplayBackendSaveResult> saving)
    {
        var command = operation.Command; var acknowledged = false; var successful = false;
        try
        {
            var result = await saving.ConfigureAwait(false);
            successful = true;
            acknowledged = await CompleteAsync(new(command.Id, CaptureReplayOutcome.Accepted) { SessionId = command.SessionId,
                Width = result.Width, Height = result.Height, ByteLength = result.ByteLength, ActualDurationSeconds = result.ActualDurationSeconds,
                Properties = result.Properties }).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var cancelled = operation.Lifetime.IsCancellationRequested || lifetime.IsCancellationRequested;
            var issue = cancelled ? CaptureReplayIssue.Cancelled : error is ReplayBackendException native ? native.Issue : CaptureReplayIssue.HostUnavailable;
            await CompleteAsync(new(command.Id, cancelled ? CaptureReplayOutcome.Cancelled : CaptureReplayOutcome.Failed, issue) { SessionId = command.SessionId }).ConfigureAwait(false);
        }
        finally
        {
            if ((!acknowledged || !successful) && command.OutputPath is { } output)
                try { File.Delete(output); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    private async Task<bool> CompleteAsync(HostReplayCompletion completion)
    {
        if (lifetime.IsCancellationRequested) return false;
        try { return await host.CompleteAsync(completion, lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return false; }
    }
    private async Task StopAsync(Hosted active)
    {
        try { await active.Backend.StopAsync(active.Lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { if (!active.Lifetime.IsCancellationRequested) active.Failure = error is ReplayBackendException native ? native.Issue : CaptureReplayIssue.HostUnavailable; }
    }
    private async Task RetireAsync(Hosted expected)
    {
        Execution? pending;
        lock (gate)
        {
            if (!ReferenceEquals(current, expected)) return;
            current = null; pending = execution?.Command.SessionId == expected.Id ? execution : null;
            retiredReport = new(expected.Id, CaptureReplayPhase.Error, 0, 0, 0, CaptureReplayIssue.HostUnavailable, Ended: true);
        }
        Cancel(pending?.Lifetime);
        Cancel(expected.Lifetime);
        try { await expected.Backend.DisposeAsync().ConfigureAwait(false); if (expected.Stop is not null) await expected.Stop.ConfigureAwait(false); }
        finally { expected.Lifetime.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); Hosted? active; Execution? pending; Task? background;
        lock (gate) { active = current; pending = execution; background = running; }
        Cancel(pending?.Lifetime);
        if (active is not null) await ObserveCleanupAsync(RetireAsync(active)).ConfigureAwait(false);
        if (background is not null) await ObserveCleanupAsync(background).ConfigureAwait(false);
        if (pending is not null) { try { await ObserveCleanupAsync(pending.Work).ConfigureAwait(false); } finally { pending.Lifetime.Dispose(); } }
        if (retiring is not null) await ObserveCleanupAsync(retiring).ConfigureAwait(false);
        lifetime.Dispose();
    }
    private async Task ObserveCleanupAsync(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception error) when (lifetime.IsCancellationRequested && error is OperationCanceledException or ObjectDisposedException) { }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { reportFailure?.Invoke(error); }
    }
    private static void Cancel(CancellationTokenSource? source)
    { try { source?.Cancel(); } catch (ObjectDisposedException) { } }
}
