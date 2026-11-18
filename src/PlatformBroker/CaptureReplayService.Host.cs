using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureReplayService
{
    public async Task<HostReplayWork> PollHostAsync(HostReplayReport? report = null, CancellationToken token = default)
    {
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DemandAlive(); lastHostContact = clock.GetUtcNow();
            var authorityReady = true;
            if (control is { } pending && (pending.Token.IsCancellationRequested || pending.Expires <= lastHostContact))
            { CancelPendingControl(); if (session is null) Idle(); }
            if (control is { } waiting)
            {
                var pendingIssue = await authorize(waiting.Command.Widget, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false);
                if (pendingIssue != CaptureReplayIssue.None)
                {
                    lock (gate) if (ReferenceEquals(control, waiting)) control = null;
                    if (session is null) Idle(pendingIssue);
                    waiting.Completion.TrySetResult(Result(CaptureReplayOutcome.Unavailable, pendingIssue));
                }
            }
            if (configuration.Owner is { } owner && (configuration.Settings.Enabled || session is not null))
            {
                var issue = await authorize(owner, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false);
                if (issue is not (CaptureReplayIssue.None or CaptureReplayIssue.HostUnavailable) && reload is not null)
                {
                    var observed = await reload(token).ConfigureAwait(false);
                    if (!observed.IsWellFormed()) throw new BrokerException("invalid_replay_settings", "Replay preferences are invalid.");
                    observed.Owner?.Validate();
                    if (observed.Revision > configuration.Revision)
                    {
                        // A reviewed update can replace the durable owner while
                        // this broker still holds its old runtime configuration.
                        // Retire old work; never retag a recorder or save lease.
                        CancelPendingControl(); AbortRecording();
                        configuration = observed; automaticStartBlocked = false;
                        nextAutomaticStart = lastHostContact.AddSeconds(1);
                        issue = configuration.Owner is { } replacement
                            ? await authorize(replacement, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false)
                            : issue;
                        Idle(issue);
                    }
                }
                if (issue == CaptureReplayIssue.HostUnavailable)
                { authorityReady = false; CancelPendingControl(); AbortRecording(); Idle(issue); }
                else if (issue != CaptureReplayIssue.None) await RevokeCoreAsync(issue, token).ConfigureAwait(false);
            }
            if (save is { } saving &&
                (await authorize(saving.Actor, PlatformCapabilities.CaptureLibrarySaveV1, token).ConfigureAwait(false) != CaptureReplayIssue.None ||
                 await authorize(saving.Actor, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) != CaptureReplayIssue.None))
                CancelPendingSave(saving.Actor);
            if (report is not null && session is { } active && report.SessionId == active.Id)
            {
                if (!Enum.IsDefined(report.Phase) || !Enum.IsDefined(report.Issue) || !double.IsFinite(report.BufferedDurationSeconds) ||
                    report.BufferedDurationSeconds < 0 || report.BufferedDurationSeconds > active.Settings.ReplayDurationSeconds ||
                    report.ResidentBytes < 0 || report.ResidentBytes > active.Settings.MemoryBudgetBytes || report.DroppedFrames < 0 ||
                    report.Phase is not (CaptureReplayPhase.Starting or CaptureReplayPhase.Recording or CaptureReplayPhase.Paused or CaptureReplayPhase.Stopping or CaptureReplayPhase.Off or CaptureReplayPhase.Error))
                    await RevokeCoreAsync(CaptureReplayIssue.HostUnavailable, token).ConfigureAwait(false);
                else
                {
                    active.Ended |= report.Ended;
                    if (report.Ended)
                    {
                        active.TerminalIssue = report.Issue;
                        if (report.Issue is not (CaptureReplayIssue.None or CaptureReplayIssue.TargetUnavailable or CaptureReplayIssue.Cancelled)) automaticStartBlocked = true;
                    }
                    Publish(Status with { Phase = active.StopRequested || active.Ended ? CaptureReplayPhase.Stopping : report.Phase,
                        BufferedDurationSeconds = report.BufferedDurationSeconds, ResidentBytes = report.ResidentBytes,
                        DroppedFrames = report.DroppedFrames, Saving = save is not null, Issue = report.Issue });
                    if (active.Ended && save is null)
                    {
                        session = null; active.Lifetime.Dispose(); nextAutomaticStart = lastHostContact.AddSeconds(2);
                        Idle(active.TerminalIssue);
                    }
                }
            }
            if (authorityReady && !hostSuspended && session is null && control is null && save is null && !automaticStartBlocked && configuration.Settings is { Enabled: true, AutoStart: true } &&
                configuration.Owner is { } approvedOwner && configuration.ApprovedApplication is { } app && lastHostContact >= nextAutomaticStart)
            {
                var command = new HostReplayCommand(Guid.NewGuid().ToString("N"), HostReplayCommandKind.Start, approvedOwner,
                    Guid.NewGuid().ToString("N"), configuration.Settings, configuration.Revision)
                { Automatic = true, ApprovedApplication = app };
                control = new(this, command, lifetime.Token, lastHostContact.AddSeconds(20));
                Publish(Status with { Phase = CaptureReplayPhase.Starting, SessionId = command.SessionId, Issue = CaptureReplayIssue.None });
            }
            var currentId = session?.Id ?? control?.Command.SessionId;
            var saveCommand = save is { Revoked: false } currentSave && !currentSave.Session.Lifetime.IsCancellationRequested ? currentSave.Command : null;
            return new(Status, saveCommand ?? control?.Command, currentId,
                session?.StopRequested == true && (save is null || save.Command is not null),
                report is not null && report.SessionId != currentId);
        }
        finally { mutations.Release(); }
    }

    public bool IsHostCommandCurrent(string id)
    {
        lock (gate) return !disposed && (control is { } pending && pending.Command.Id == id && !pending.Token.IsCancellationRequested &&
            pending.Expires > clock.GetUtcNow() || save is { Command: { } command } current && command.Id == id &&
            !Volatile.Read(ref current.Revoked) && !current.Session.Lifetime.IsCancellationRequested);
    }

    public async Task<bool> IsHostCommandCurrentAsync(string id, CancellationToken token = default)
    {
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsHostCommandCurrent(id)) return false;
            BrokerWidgetIdentity? actor;
            lock (gate) actor = control is { } pending && pending.Command.Id == id ? pending.Command.Widget : save?.Actor;
            if (actor is null) return false;
            if (await authorize(actor, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) != CaptureReplayIssue.None) return false;
            if (save?.Command?.Id == id && await authorize(actor, PlatformCapabilities.CaptureLibrarySaveV1, token).ConfigureAwait(false) != CaptureReplayIssue.None) return false;
            return IsHostCommandCurrent(id);
        }
        finally { mutations.Release(); }
    }

    public async Task<bool> CompleteHostAsync(HostReplayCompletion completion, CancellationToken token = default)
    {
        if (!CaptureLimits.ValidToken(completion.Id) || !Enum.IsDefined(completion.Outcome) || !Enum.IsDefined(completion.Issue))
            throw new BrokerException("invalid_payload", "Invalid replay completion.");
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DemandAlive();
            if (save is { Command: { } saving } output && saving.Id == completion.Id)
            {
                if (saving.SessionId != completion.SessionId || output.Session.Lifetime.IsCancellationRequested) return false;
                return output.Output.TrySetResult(completion) || output.Output.Task.IsCompletedSuccessfully && output.Output.Task.Result == completion;
            }
            if (control is not { } pending || pending.Command.Id != completion.Id || pending.Command.SessionId != completion.SessionId ||
                pending.Token.IsCancellationRequested || pending.Expires <= clock.GetUtcNow()) return false;
            var command = pending.Command;
            if (completion.Outcome != CaptureReplayOutcome.Accepted)
            {
                control = null; nextAutomaticStart = clock.GetUtcNow().AddSeconds(2);
                if (command.Automatic && completion.Issue is not (CaptureReplayIssue.TargetUnavailable or CaptureReplayIssue.Cancelled)) automaticStartBlocked = true;
                Idle(completion.Issue);
                pending.Completion.TrySetResult(Result(completion.Outcome, completion.Issue)); return true;
            }
            var allowed = await authorize(command.Widget, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false);
            if (allowed != CaptureReplayIssue.None || pending.Token.IsCancellationRequested || !ReferenceEquals(control, pending))
            { CancelPendingControl(); Idle(allowed); return false; }
            var starts = command.Kind == HostReplayCommandKind.Start;
            if (starts && (completion.Resolved is not null && !completion.Resolved.Satisfies(command.Settings.Recording) ||
                (command.Settings.Recording.Selection is not null || command.Settings.Recording.Encoding is not null) && completion.Resolved is null))
                throw new BrokerException("invalid_capture_configuration", "The resolved recording does not satisfy the requested settings.");
            if (starts && (completion.Target is null || !ValidTarget(completion.Target) || completion.ApprovedApplication is not { Length: 64 } key ||
                !key.All(char.IsAsciiHexDigit) || completion.ApplicationName is not { Length: > 0 and <= 120 } name || name.Any(char.IsControl) ||
                command.Automatic && !string.Equals(key, configuration.ApprovedApplication, StringComparison.OrdinalIgnoreCase)))
            { automaticStartBlocked = true; CancelPendingControl(); Idle(CaptureReplayIssue.TargetUnavailable); return false; }
            var sameFeature = SameFeature(configuration.Owner, command.Widget);
            var next = configuration with { Settings = command.Settings, Owner = command.Widget,
                ApprovedApplication = starts ? completion.ApprovedApplication : sameFeature ? configuration.ApprovedApplication : null,
                ApplicationName = starts ? completion.ApplicationName : sameFeature ? configuration.ApplicationName : null };
            lock (gate)
            {
                if (!ReferenceEquals(control, pending) || pending.Token.IsCancellationRequested) return false;
                pending.Committing = true;
            }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, pending.Token, lifetime.Token);
                await SaveConfigurationAsync(next, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pending.Token.IsCancellationRequested || token.IsCancellationRequested || lifetime.IsCancellationRequested)
            { control = null; Idle(CaptureReplayIssue.Cancelled); pending.Completion.TrySetResult(Result(CaptureReplayOutcome.Cancelled, CaptureReplayIssue.Cancelled)); return false; }
            catch (BrokerException error) when (error.Code == "replay_settings_conflict")
            { control = null; automaticStartBlocked = true; Idle(CaptureReplayIssue.SettingsChanged); pending.Completion.TrySetResult(Result(CaptureReplayOutcome.Conflict, CaptureReplayIssue.SettingsChanged)); return false; }
            catch
            { control = null; automaticStartBlocked = true; Idle(CaptureReplayIssue.StorageUnavailable); pending.Completion.TrySetResult(Result(CaptureReplayOutcome.Failed, CaptureReplayIssue.StorageUnavailable)); throw; }
            control = null; automaticStartBlocked = false;
            if (starts)
            {
                var application = completion.SourceApplication?.IsWellFormed() == true ? completion.SourceApplication :
                    new CaptureApplicationContext(completion.ApplicationName!, completion.ApplicationName!);
                session = new(command.SessionId!, command.Widget, command.Settings, completion.Target!, application);
                Publish(Status with { Phase = CaptureReplayPhase.Starting, SessionId = session.Id, Resolved = completion.Resolved, TargetName = application.ApplicationName,
                    BufferedDurationSeconds = 0, ResidentBytes = 0, DroppedFrames = 0, Saving = false, Issue = CaptureReplayIssue.None });
            }
            else Idle();
            pending.Completion.TrySetResult(Result(CaptureReplayOutcome.Accepted));
            return true;
        }
        finally { mutations.Release(); }
    }

    public async Task RevokeAsync(BrokerWidgetIdentity actor, CaptureReplayIssue issue = CaptureReplayIssue.PermissionRevoked)
    {
        CancelPendingSave(actor);
        await mutations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            if (SameFeature(configuration.Owner, actor) || SameFeature(control?.Command.Widget, actor)) await RevokeCoreAsync(issue, lifetime.Token).ConfigureAwait(false);
        }
        finally { mutations.Release(); }
    }
    public async Task SetHostPowerStateAsync(long revision, bool suspended, CancellationToken token = default)
    {
        if (revision <= 0) throw new BrokerException("invalid_payload", "Invalid host power revision.");
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            DemandAlive(); if (revision <= lastPowerRevision) return;
            lastPowerRevision = revision; hostSuspended = suspended;
            CancelPendingControl(); AbortRecording(); automaticStartBlocked = false;
            nextAutomaticStart = clock.GetUtcNow().AddSeconds(1); Idle();
        }
        finally { mutations.Release(); }
    }
    private async Task RevokeCoreAsync(CaptureReplayIssue issue, CancellationToken token)
    {
        var previous = configuration;
        automaticStartBlocked = true; CancelPendingControl();
        AbortRecording();
        if (configuration.Settings.Enabled)
            try { await SaveConfigurationAsync(configuration with { Settings = configuration.Settings with { Enabled = false } }, token).ConfigureAwait(false); }
            catch (BrokerException error) when (error.Code == "replay_settings_conflict")
            {
                // The reviewed commit can win between Poll's reload and this
                // revoke write. SaveConfiguration has adopted its newer CAS
                // revision. Do not disable that separately authorized owner.
                if (configuration.Revision > previous.Revision && configuration.Owner is { } replacement &&
                    replacement != previous.Owner && configuration.Settings.Enabled &&
                    await authorize(replacement, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) == CaptureReplayIssue.None)
                {
                    automaticStartBlocked = false; nextAutomaticStart = clock.GetUtcNow().AddSeconds(1);
                    Idle(); return;
                }
                if (configuration.Settings.Enabled)
                { configuration = configuration with { Settings = configuration.Settings with { Enabled = false } }; issue = CaptureReplayIssue.StorageUnavailable; }
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                if (configuration.Settings.Enabled)
                {
                    configuration = configuration with { Settings = configuration.Settings with { Enabled = false } };
                    issue = CaptureReplayIssue.StorageUnavailable;
                }
            }
        Idle(issue);
    }
    private void AbortRecording()
    {
        if (session is { } retiring)
        {
            session = null; retiring.Lifetime.Cancel();
            if (save is null) retiring.Lifetime.Dispose();
        }
        save?.Output.TrySetCanceled();
    }
    private static bool ValidTarget(NativeWindowPreviewTarget target) => target.ProcessId != 0 &&
        target.Handle is { Length: > 0 and <= 16 } && ulong.TryParse(target.Handle, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var handle) && handle != 0 &&
        target.ProcessCreated is { Length: > 0 and <= 16 } && ulong.TryParse(target.ProcessCreated, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var created) && created != 0 &&
        target.ClassName is { Length: > 0 and <= 255 } && !target.ClassName.Any(char.IsControl);
}
