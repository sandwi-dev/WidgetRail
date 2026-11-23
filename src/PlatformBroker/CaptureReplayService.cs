using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>One profile-owned recorder. Widget objects own pending gestures, never an admitted recording.</summary>
public sealed partial class CaptureReplayService : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim mutations = new(1, 1);
    private readonly CaptureLibraryStore library;
    private readonly Func<CaptureReplayConfiguration, long, CancellationToken, Task<CaptureReplayConfiguration>> persist;
    private readonly Func<BrokerWidgetIdentity, string, CancellationToken, Task<CaptureReplayIssue>> authorize;
    private readonly Func<CancellationToken, Task<CaptureReplayConfiguration>>? reload;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource lifetime = new();
    private CaptureReplayConfiguration configuration;
    private CaptureReplayStatus status;
    private PendingControl? control;
    private ActiveSession? session;
    private PendingSave? save;
    private DateTimeOffset lastHostContact;
    private static readonly TimeSpan AutomaticDiscoveryInterval = TimeSpan.FromSeconds(5);
    private DateTimeOffset nextAutomaticStart;
    private bool automaticStartBlocked;
    private bool hostSuspended;
    private long lastPowerRevision;
    private bool disposed;
    public event Action<CaptureReplayStatus>? Changed;

    private sealed class PendingControl(object owner, HostReplayCommand command, CancellationToken token, DateTimeOffset expires)
    {
        internal readonly object Owner = owner;
        internal readonly HostReplayCommand Command = command;
        internal readonly CancellationToken Token = token;
        internal readonly DateTimeOffset Expires = expires;
        internal bool Committing;
        internal readonly TaskCompletionSource<CaptureReplayControlResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ActiveSession(string id, BrokerWidgetIdentity owner, CaptureReplaySettings settings,
        NativeWindowPreviewTarget target, CaptureApplicationContext application)
    {
        internal readonly string Id = id;
        internal readonly BrokerWidgetIdentity Owner = owner;
        internal readonly CaptureReplaySettings Settings = settings;
        internal readonly NativeWindowPreviewTarget Target = target;
        internal readonly CaptureApplicationContext Application = application;
        internal long StartedAt { get; init; }
        internal readonly CancellationTokenSource Lifetime = new();
        internal bool StopRequested, Ended;
        internal CaptureReplayIssue TerminalIssue;
    }
    private sealed class PendingSave(ActiveSession session, BrokerWidgetIdentity actor, string requestId, long capturedAt)
    {
        internal readonly ActiveSession Session = session;
        internal readonly BrokerWidgetIdentity Actor = actor;
        internal readonly string RequestId = requestId;
        internal readonly long CapturedAt = capturedAt;
        internal readonly TaskCompletionSource<HostReplayCompletion> Output = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<CaptureReplaySaveResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HostReplayCommand? Command;
        internal Task? Work;
        internal bool Revoked;
    }
    private readonly Dictionary<string, CaptureReplaySaveResult> completedSaves = new(StringComparer.Ordinal);

    public CaptureReplayService(CaptureLibraryStore library, CaptureReplayConfiguration configuration,
        Func<CaptureReplayConfiguration, long, CancellationToken, Task<CaptureReplayConfiguration>> persist,
        Func<BrokerWidgetIdentity, string, CancellationToken, Task<CaptureReplayIssue>> authorize, TimeProvider? time = null)
        : this(library, configuration, persist, authorize, time, null) { }

    public CaptureReplayService(CaptureLibraryStore library, CaptureReplayConfiguration configuration,
        Func<CaptureReplayConfiguration, long, CancellationToken, Task<CaptureReplayConfiguration>> persist,
        Func<BrokerWidgetIdentity, string, CancellationToken, Task<CaptureReplayIssue>> authorize, TimeProvider? time,
        Func<CancellationToken, Task<CaptureReplayConfiguration>>? reload)
    {
        this.library = library; this.persist = persist; this.authorize = authorize; this.reload = reload; clock = time ?? TimeProvider.System;
        if (!configuration.IsWellFormed()) throw new ArgumentException("Invalid replay configuration.", nameof(configuration));
        configuration.Owner?.Validate(); this.configuration = configuration;
        status = new(0, configuration.Revision, configuration.Settings, configuration.Settings.Enabled ? CaptureReplayPhase.Waiting : CaptureReplayPhase.Off);
    }

    public CaptureReplayStatus Status { get { lock (gate) return status; } }
    public BrokerWidgetIdentity? Owner { get { lock (gate) return configuration.Owner; } }
    private CaptureReplayControlResult Result(CaptureReplayOutcome outcome, CaptureReplayIssue issue = CaptureReplayIssue.None) => new(outcome, Status, issue);
    private void Publish(CaptureReplayStatus next)
    {
        Action<CaptureReplayStatus>? handlers;
        lock (gate)
        {
            next = next with { Revision = status.Revision, SettingsRevision = configuration.Revision, Settings = configuration.Settings };
            if (next == status) return;
            status = next = next with { Revision = checked(status.Revision + 1) }; handlers = Changed;
        }
        if (handlers is null) return;
        foreach (Action<CaptureReplayStatus> handler in handlers.GetInvocationList())
            try { handler(next); } catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
    }
    private void Idle(CaptureReplayIssue issue = CaptureReplayIssue.None) => Publish(Status with
    {
        Phase = !configuration.Settings.Enabled ? CaptureReplayPhase.Off :
            issue is CaptureReplayIssue.None or CaptureReplayIssue.TargetUnavailable or CaptureReplayIssue.Cancelled ? CaptureReplayPhase.Waiting : CaptureReplayPhase.Error,
        SessionId = null, Resolved = null, TargetName = null, BufferedDurationSeconds = 0, ResidentBytes = 0,
        DroppedFrames = 0, Saving = false, Issue = issue,
    });
    private async Task SaveConfigurationAsync(CaptureReplayConfiguration next, CancellationToken token)
    {
        var previous = configuration;
        next = next with { Revision = checked(previous.Revision + 1), Settings = next.Settings.Normalize() };
        CaptureReplayConfiguration committed;
        try { committed = await persist(next, previous.Revision, token).ConfigureAwait(false); }
        catch (BrokerException error) when (error.Code == "replay_settings_conflict" && reload is not null)
        {
            // Catalog removal can clear durable intent while this host is running.
            // Adopt its newer revision without overwriting the external decision.
            var observed = await reload(token).ConfigureAwait(false);
            if (!observed.IsWellFormed() || observed.Revision <= previous.Revision)
                throw new BrokerException("invalid_replay_settings", "Replay preferences could not be reconciled.");
            observed.Owner?.Validate(); configuration = observed;
            throw;
        }
        if (committed != next || !committed.IsWellFormed()) throw new BrokerException("invalid_replay_settings", "Replay settings could not be committed.");
        configuration = committed;
    }
    private bool HostAvailable => !hostSuspended && lastHostContact != default && clock.GetUtcNow() - lastHostContact < TimeSpan.FromSeconds(10);
    private void DemandAlive() => ObjectDisposedException.ThrowIf(disposed, this);
    private static bool SameFeature(BrokerWidgetIdentity? first, BrokerWidgetIdentity second) =>
        first is not null && first.PackageId == second.PackageId && first.PublisherId == second.PublisherId && first.InstanceId == second.InstanceId;

    public async Task<CaptureReplayControlResult> ConfigureAsync(object owner, BrokerWidgetIdentity actor,
        CaptureReplaySettingsUpdate request, CancellationToken token = default)
    {
        actor.Validate(); if (!request.IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid replay settings.");
        PendingControl? pending = null;
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DemandAlive();
            if (request.ExpectedRevision != configuration.Revision) return Result(CaptureReplayOutcome.Conflict, CaptureReplayIssue.SettingsChanged);
            if (request.Settings.Normalize() == configuration.Settings) return Result(CaptureReplayOutcome.Accepted);
            if (session is not null && request.Settings.Normalize() with { Enabled = configuration.Settings.Enabled } != configuration.Settings)
                return Result(CaptureReplayOutcome.Busy, CaptureReplayIssue.RecordingActive);
            if (!request.Settings.Enabled)
            {
                return await DisableAsync(request.Settings).ConfigureAwait(false);
            }
            if (control is not null || session is not null) return Result(CaptureReplayOutcome.Busy, CaptureReplayIssue.RecordingActive);
            if (!HostAvailable) return Result(CaptureReplayOutcome.Unavailable, CaptureReplayIssue.HostUnavailable);
            pending = new(owner, new(Guid.NewGuid().ToString("N"), HostReplayCommandKind.ApproveSettings, actor, null,
                request.Settings.Normalize(), configuration.Revision), token, clock.GetUtcNow().AddSeconds(120));
            lock (gate) control = pending;
        }
        finally { mutations.Release(); }
        return await AwaitControlAsync(pending!, token).ConfigureAwait(false);
    }

    public Task<CaptureReplayControlResult> StartAsync(object owner, BrokerWidgetIdentity actor, CancellationToken token = default) =>
        StartCoreAsync(owner, actor, null, token);

    public Task<CaptureReplayControlResult> StartFromTrayAsync(object owner, BrokerWidgetIdentity actor, long expectedRevision, CancellationToken token) =>
        StartCoreAsync(owner, actor, expectedRevision, token);

    private async Task<CaptureReplayControlResult> StartCoreAsync(object owner, BrokerWidgetIdentity actor, long? expectedRevision, CancellationToken token)
    {
        actor.Validate(); PendingControl pending;
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DemandAlive();
            if (expectedRevision is { } revision)
            {
                if (Status.Revision != revision || !TrayReplayState.From(Status).CanStart)
                    return Result(CaptureReplayOutcome.Conflict, CaptureReplayIssue.SessionChanged);
                var permission = await authorize(actor, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false);
                if (permission != CaptureReplayIssue.None) return Result(CaptureReplayOutcome.Unavailable, permission);
            }
            if (!HostAvailable) return Result(CaptureReplayOutcome.Unavailable, CaptureReplayIssue.HostUnavailable);
            // A manual start owns the choice while an automatic scan is still looking.
            if (session is null && control is { Command.Automatic: true, Committing: false }) CancelPendingControl();
            if (control is not null || session is not null) return Result(CaptureReplayOutcome.Busy);
            pending = new(owner, new(Guid.NewGuid().ToString("N"), HostReplayCommandKind.Start, actor,
                Guid.NewGuid().ToString("N"), configuration.Settings with { Enabled = true }, configuration.Revision) { RequestedFromTray = expectedRevision.HasValue,
                    Approvals = SameFeature(configuration.Owner, actor) ? configuration.Approvals : ReplayApplicationApprovals.Empty },
                token, clock.GetUtcNow().AddSeconds(120));
            lock (gate) control = pending;
            Publish(Status with { Phase = CaptureReplayPhase.Starting, SessionId = pending.Command.SessionId, Issue = CaptureReplayIssue.None });
        }
        finally { mutations.Release(); }
        return await AwaitControlAsync(pending, token).ConfigureAwait(false);
    }
    private async Task<CaptureReplayControlResult> AwaitControlAsync(PendingControl pending, CancellationToken token)
    {
        using var cancellation = token.Register(() => CancelControl(pending));
        try { return await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(125), token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { CancelControl(pending); throw; }
        catch (TimeoutException) { CancelControl(pending); return Result(CaptureReplayOutcome.Cancelled, CaptureReplayIssue.Cancelled); }
    }
    public void CancelPending(object owner)
    {
        var picker = Volatile.Read(ref approvalChoice);
        if (picker is not null && ReferenceEquals(picker.Owner, owner)) picker.Completion.TrySetResult(null);
        PendingControl? pending;
        lock (gate)
        {
            if (control is null || !ReferenceEquals(control.Owner, owner)) return;
            pending = control;
        }
        CancelControl(pending);
    }
    private void CancelControl(PendingControl pending)
    {
        lock (gate)
        { if (!ReferenceEquals(control, pending) || pending.Committing) return; control = null; }
        if (session is null) Idle();
        pending.Completion.TrySetResult(Result(CaptureReplayOutcome.Cancelled, CaptureReplayIssue.Cancelled));
    }
    private void CancelPendingControl()
    {
        PendingControl? pending; lock (gate) { pending = control; control = null; }
        pending?.Completion.TrySetResult(Result(CaptureReplayOutcome.Cancelled, CaptureReplayIssue.Cancelled));
    }
    public void CancelPendingSave(BrokerWidgetIdentity actor)
    {
        lock (gate)
        {
            if (save is not { } pending || !SameFeature(pending.Actor, actor)) return;
            Volatile.Write(ref pending.Revoked, true); pending.Output.TrySetCanceled();
        }
    }

    public Task<CaptureReplayControlResult> StopAsync(string? expectedSessionId, CancellationToken token = default) => StopAsync(expectedSessionId, null, token);

    public async Task<CaptureReplayControlResult> StopAsync(string? expectedSessionId, long? expectedRevision, CancellationToken token)
    {
        if (!new CaptureReplaySessionRequest(expectedSessionId).IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid replay session.");
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DemandAlive();
            var currentId = session?.Id ?? (control?.Command.Automatic == false ? control.Command.SessionId : null);
            if (currentId != expectedSessionId || expectedRevision is { } seen && Status.Revision != seen)
                return Result(CaptureReplayOutcome.Conflict, CaptureReplayIssue.SessionChanged);
            return await DisableAsync(configuration.Settings with { Enabled = false }).ConfigureAwait(false);
        }
        finally { mutations.Release(); }
    }

    private async Task<CaptureReplayControlResult> DisableAsync(CaptureReplaySettings settings)
    {
        CancelPendingControl(); automaticStartBlocked = true;
        // Once admitted, stopping is host-owned even if the initiating widget disappears.
        if (session is { } active) active.StopRequested = true;
        var issue = CaptureReplayIssue.None;
        if (configuration.Settings != settings.Normalize())
            try { await SaveConfigurationAsync(configuration with { Settings = settings }, lifetime.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                // Storage failure must never leave acquisition enabled in this host session.
                configuration = configuration with { Settings = configuration.Settings with { Enabled = false } };
                issue = error is BrokerException { Code: "replay_settings_conflict" } ? CaptureReplayIssue.SettingsChanged : CaptureReplayIssue.StorageUnavailable;
            }
        if (session is not null) Publish(Status with { Phase = CaptureReplayPhase.Stopping, Issue = issue });
        else Idle(issue);
        return Result(issue == CaptureReplayIssue.None ? CaptureReplayOutcome.Accepted :
            issue == CaptureReplayIssue.SettingsChanged ? CaptureReplayOutcome.Conflict : CaptureReplayOutcome.Failed, issue);
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingSave; ActiveSession? retiring;
        await mutations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel(); CancelPendingControl();
            lock (gate) { retiring = session; session = null; pendingSave = save?.Work; }
            retiring?.Lifetime.Cancel();
            save?.Output.TrySetCanceled(lifetime.Token);
        }
        finally { mutations.Release(); }
        if (pendingSave is not null) try { await pendingSave.ConfigureAwait(false); } catch (OperationCanceledException) { }
        retiring?.Lifetime.Dispose(); lifetime.Dispose();
    }
}
