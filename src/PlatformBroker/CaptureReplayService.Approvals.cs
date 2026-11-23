using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed record HostReplayApprovalPicker(string Id, BrokerWidgetIdentity Widget, ReplayApplicationApprovals Approved);

public sealed partial class CaptureReplayService
{
    private sealed record ApprovalChoice(object Owner, HostReplayApprovalPicker Request)
    {
        internal TaskCompletionSource<ReplayApprovedApplication?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private ApprovalChoice? approvalChoice;
    internal HostReplayApprovalPicker? PendingApproval => Volatile.Read(ref approvalChoice) is { } choice && !choice.Completion.Task.IsCompleted ? choice.Request : null;

    public async Task<ReplayApprovalList> ReadApprovalsAsync(BrokerWidgetIdentity actor, CancellationToken token)
    {
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await DemandApprovalAccessAsync(actor, token).ConfigureAwait(false);
            return new(configuration.Revision, configuration.Approvals);
        }
        finally { mutations.Release(); }
    }

    private async Task DemandApprovalAccessAsync(BrokerWidgetIdentity actor, CancellationToken token)
    {
        DemandAlive();
        actor.Validate();
        if (configuration.Owner is not null && !SameFeature(configuration.Owner, actor) ||
            await authorize(actor, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) != CaptureReplayIssue.None)
            throw new BrokerException("permission_denied", "Replay approvals require access to the owning capture feature.");
    }

    public async Task<CaptureReplayControlResult> ChooseApprovalAsync(object owner, BrokerWidgetIdentity actor, long revision, CancellationToken token)
    {
        ApprovalChoice choice;
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await DemandApprovalAccessAsync(actor, token).ConfigureAwait(false);
            if (configuration.Revision != revision) return Result(CaptureReplayOutcome.Conflict, CaptureReplayIssue.SettingsChanged);
            if (configuration.Approvals.Applications.Count >= ReplayApplicationApprovals.MaximumApplications)
                throw new BrokerException("replay_approvals_full", "Remove an approved application before adding another.");
            choice = new(owner, new(Guid.NewGuid().ToString("N"), actor, configuration.Approvals));
            if (Interlocked.CompareExchange(ref approvalChoice, choice, null) is not null)
                return Result(CaptureReplayOutcome.Busy);
        }
        finally { mutations.Release(); }
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var selected = await choice.Completion.Task.WaitAsync(TimeSpan.FromSeconds(120), cancellation.Token).ConfigureAwait(false);
            if (selected is null) return Result(CaptureReplayOutcome.Cancelled, CaptureReplayIssue.Cancelled);
            return await EditApprovalsAsync(actor, revision, approvals => approvals.Add(selected), cancellation.Token).ConfigureAwait(false);
        }
        catch (TimeoutException) { return Result(CaptureReplayOutcome.Cancelled, CaptureReplayIssue.Cancelled); }
        finally { Interlocked.CompareExchange(ref approvalChoice, null, choice); }
    }

    internal bool CompleteApprovalPicker(string id, ReplayApprovedApplication? selected)
    {
        var choice = Volatile.Read(ref approvalChoice);
        return choice?.Request.Id == id && (selected is null || selected.IsWellFormed()) && choice.Completion.TrySetResult(selected);
    }

    public Task<CaptureReplayControlResult> RemoveApprovalAsync(BrokerWidgetIdentity actor, ReplayApprovalRemoval request, CancellationToken token)
    {
        if (request.ApplicationId is not { Length: 64 } || !request.ApplicationId.All(char.IsAsciiHexDigit))
            throw new BrokerException("invalid_payload", "Invalid approved application identifier.");
        return EditApprovalsAsync(actor, request.ExpectedRevision, approvals =>
            new(approvals.Applications.Where(item => !string.Equals(item.Id, request.ApplicationId, StringComparison.OrdinalIgnoreCase)).ToArray()), token);
    }

    private async Task<CaptureReplayControlResult> EditApprovalsAsync(BrokerWidgetIdentity actor, long revision,
        Func<ReplayApplicationApprovals, ReplayApplicationApprovals> edit, CancellationToken token)
    {
        await mutations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await DemandApprovalAccessAsync(actor, token).ConfigureAwait(false);
            if (configuration.Revision != revision) return Result(CaptureReplayOutcome.Conflict, CaptureReplayIssue.SettingsChanged);
            var next = edit(configuration.Approvals);
            if (next == configuration.Approvals) return Result(CaptureReplayOutcome.Accepted);
            var previous = configuration;
            try { await SaveConfigurationAsync(previous with { Approvals = next, Owner = actor }, token).ConfigureAwait(false); }
            catch (BrokerException error) when (error.Code == "replay_settings_conflict")
            {
                // SaveConfigurationAsync adopted the durable revision. An external change of
                // owner or recording settings also retires work admitted under the old grant.
                if (previous.Owner != configuration.Owner || previous.Settings != configuration.Settings)
                {
                    CancelPendingControl();
                    AbortRecording();
                    Idle();
                }
                else
                {
                    if (control?.Command.Automatic == true) CancelPendingControl();
                    Publish(Status);
                }
                return Result(CaptureReplayOutcome.Conflict, CaptureReplayIssue.SettingsChanged);
            }
            if (control?.Command.Automatic == true) CancelPendingControl();
            Publish(Status);
            return Result(CaptureReplayOutcome.Accepted);
        }
        finally { mutations.Release(); }
    }
}
