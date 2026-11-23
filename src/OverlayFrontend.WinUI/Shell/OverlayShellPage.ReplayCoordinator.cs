using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsAppLibraryProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private ReplayCaptureCoordinator? replayCoordinator;
    internal IReplayCaptureBackendFactory ReplayBackendFactory { get; set; } = new NativeReplayCaptureBackendFactory(CaptureRecordingResolver.ResolveAsync);
    private void InitializeReplayCapture()
    {
        if (retired || owner is null || replayCoordinator is not null) return;
        var session = owner.Session;
        var windows = TaskWindows;
        replayCoordinator = new(new ReplayHost(session), ReplayBackendFactory,
            (work, token) => work.Command?.Automatic == true
                ? ChooseAutomaticReplayAsync(windows, work.Command, token)
                : ChooseReplayOnDispatcherAsync(work, token),
            error => Diagnostics.FrontendFailureLog.Current.Write("replay-coordinator", error));
        replayCoordinator.RecordingStarted += (widgetId, sessionId, application) => DispatcherQueue.TryEnqueue(() =>
            _ = ShowReplayNoticeAsync(application, "replay-start." + sessionId, "Replay recording started", WidgetNotificationRetention.Pending, widgetId));
        session.ReplayStatusChanged += ReplayHintChanged;
        replayCoordinator.Start();
    }
    private readonly object replayHintGate = new();
    private (TrayReplayState Commands, CaptureReplayPhase Phase, CaptureReplayIssue Issue, bool Saving) lastReplayHints;
    private long lastReplayHintRevision = -1;
    private bool replayHintRefreshQueued;
    private void ReplayHintChanged(CaptureReplayStatus status)
    {
        lock (replayHintGate)
        {
            if (status.Revision <= lastReplayHintRevision) return;
            lastReplayHintRevision = status.Revision;
            var current = (TrayReplayState.From(status), status.Phase, status.Issue, status.Saving);
            if (current == lastReplayHints) return;
            lastReplayHints = current;
            if (replayHintRefreshQueued) return;
            replayHintRefreshQueued = true;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            lock (replayHintGate) replayHintRefreshQueued = false;
            if (!retired && (RadialOpen || favoriteFlyout is not null)) RefreshFavoriteHints();
        });
    }
    internal void SetReplaySuspended(bool suspended) => replayCoordinator?.SetSuspended(suspended);

    private sealed class ReplayHost(WidgetRail.WidgetPresentationSession.WidgetPresentationSession session) : IReplayCaptureHost
    {
        public async Task<HostReplayWork> PollAsync(HostReplayReport? report, CancellationToken token)
        {
            try { return await session.TakeReplayWorkAsync(report, token).ConfigureAwait(false); }
            catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException error) when (error.Code == "bridge_busy")
            { throw new ReplayHostBusyException(error); }
        }
        public Task<bool> IsCurrentAsync(string id, CancellationToken token) => session.IsReplayCommandCurrentAsync(id, token);
        public Task<bool> CompleteAsync(HostReplayCompletion result, CancellationToken token) => session.CompleteReplayCommandAsync(result, token);
        public Task SetPowerAsync(long revision, bool suspended, CancellationToken token) => session.SetReplayPowerStateAsync(revision, suspended, token);
    }
    private async Task<ReplayCaptureDecision> ChooseAutomaticReplayAsync(WindowsTaskWindowCatalog catalog, HostReplayCommand command, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (command.Approvals?.IsWellFormed() != true || command.Approvals.Applications.Count == 0) return new(false, Issue: CaptureReplayIssue.TargetUnavailable);
            var windows = await catalog.GetWindowsAsync(token, requireComplete: true).ConfigureAwait(false);
            var choice = WebCaptureSourcePolicy.AutomaticReplayTarget(windows, (uint)Environment.ProcessId, command.Approvals);
            return choice is not null ? new(true, choice.Target, choice.ApplicationIdentity, choice.ApplicationName) : new(false, Issue: CaptureReplayIssue.TargetUnavailable);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException) && !token.IsCancellationRequested)
        { return new(false, Issue: CaptureReplayIssue.TargetUnavailable); }
    }
    private Task<ReplayCaptureDecision> ChooseReplayOnDispatcherAsync(HostReplayWork work, CancellationToken token)
    {
        var completion = new TaskCompletionSource<ReplayCaptureDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(await ChooseReplayAsync(work, token)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        })) completion.TrySetResult(new(false));
        return completion.Task.WaitAsync(token);
    }
    private async Task ChooseReplayApprovalAsync(HostReplayApprovalPicker picker, string? widgetId)
    {
        activeApprovalPicker = picker.Id;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        approvalPickerLifetime = cancellation;
        var token = cancellation.Token;
        var selection = selectionVersion;
        var session = owner!.Session;
        bool Current() => !token.IsCancellationRequested && !retired && visible && foreground && interactive &&
            widgetId is not null && widgetId == activeWidget && selectionVersion == selection;
        ReplayApprovedApplication? selected = null;
        try
        {
            if (!Current() || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) return;
            var windows = await TaskWindows.GetWindowsAsync(token);
            if (!Current()) return;
            var choices = WebCaptureSourcePolicy.Candidates(windows, (uint)Environment.ProcessId)
                .Where(item => item.ApplicationIdentity is not null && !picker.Approved.Contains(item.ApplicationIdentity) && !OwnsBrowserUiProcess(item.Target.ProcessId))
                .DistinctBy(item => item.ApplicationIdentity, StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item.ApplicationName, StringComparer.CurrentCultureIgnoreCase).ToArray();
            if (choices.Length == 0)
            {
                await ShowHostChoiceAsync("Add approved application", "Open an application you want to approve, then try again.",
                    (HostDialogChoice[])[new("close", "Close")], token, directDecisionButtons: true);
                return;
            }
            var id = await ShowHostChoiceAsync("Add approved application", "Allow replay to start automatically for this application.",
                choices.Select(item => new HostDialogChoice(item.Id, item.ApplicationName)).ToArray(), token,
                configure: dialog => dialog.ConfigurePreferenceList(closeLabel: "Cancel"));
            if (Current() && choices.FirstOrDefault(item => item.Id == id) is { } chosen)
                selected = new(chosen.ApplicationIdentity!, chosen.ApplicationName);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("replay-approval-picker", error); }
        finally
        {
            try { if (!retired) await session.CompleteReplayApprovalAsync(picker.Id, Current() ? selected : null, lifetime.Token); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("replay-approval-completion", error); }
            approvalPickerLifetime = null;
            activeApprovalPicker = null;
        }
    }
    private async Task<ReplayCaptureDecision> ChooseReplayAsync(HostReplayWork work, CancellationToken token)
    {
        if (work.Command is not { } command || work.WidgetId is null || retired || !visible || !foreground ||
            HostChoiceActive || LocalInstallActive || SystemFilePickerOpen || owner is null) return new(false);
        bool OwnsSource() => command.RequestedFromTray ? !interactive && !PinnedInputActive :
            interactive && activeWidget == work.WidgetId;
        if (!OwnsSource()) return new(false);
        var selection = selectionVersion;
        var requestOwner = owner;
        bool Current() => !retired && visible && foreground && OwnsSource() &&
            selectionVersion == selection && ReferenceEquals(owner, requestOwner) && !token.IsCancellationRequested;
        if (!await requestOwner.Session.IsReplayCommandCurrentAsync(command.Id, token) || !Current()) return new(false);
        var recording = command.Settings.Recording;
        var details=$"Replay: up to {command.Settings.ReplayDurationSeconds} s\n"+
            CaptureRecordingSummary.Video(recording)+"\n"+CaptureRecordingSummary.Audio(recording)+
            "\n\nContinues with overlay hidden\nEnds: Stop replay, application exit or quitting WidgetRail";
        if (command.Kind == HostReplayCommandKind.ApproveSettings)
        {
            var selected = await ShowHostChoiceAsync("Enable replay recording?", details,
                (HostDialogChoice[])[new("enable", "Enable replay")], token, directDecisionButtons: true);
            return new(selected == "enable" && Current() && await requestOwner.Session.IsReplayCommandCurrentAsync(command.Id, token));
        }
        var windows = await TaskWindows.GetWindowsAsync(token);
        if (!Current()) return new(false);
        var choices = WebCaptureSourcePolicy.Candidates(windows, (uint)Environment.ProcessId)
            .Where(item => item.ApplicationIdentity is not null && !OwnsBrowserUiProcess(item.Target.ProcessId)).ToArray();
        if (choices.Length == 0) return new(false, Issue: CaptureReplayIssue.TargetUnavailable);
        var approved = command.Approvals;
        var focused = WebCaptureSourcePolicy.FocusedApprovedTarget(choices, approved, ForegroundBeforeOverlay?.Invoke());
        if (focused is not null) return new(true, focused.Target, focused.ApplicationIdentity, focused.ApplicationName);
        HostChoiceDialog? chooser = null;
        var rows = choices.Select(item => new HostDialogChoice(item.Id, item.Label,
            Value: approved.Contains(item.ApplicationIdentity) ? "Approved" : null)).ToArray();
        var result = await ShowHostChoiceAsync("Record an application", details, rows, token, configure: dialog =>
        {
            chooser = dialog;
            dialog.ConfigureChoiceActions("Record", "Record and add to approved", id =>
                approved.Applications.Count < ReplayApplicationApprovals.MaximumApplications &&
                choices.Any(item => item.Id == id && !approved.Contains(item.ApplicationIdentity)));
        });
        if (!Current() || !await requestOwner.Session.IsReplayCommandCurrentAsync(command.Id, token)) return new(false);
        var chosen = choices.FirstOrDefault(item => item.Id == result);
        return chosen is null ? new(false) : new(true, chosen.Target, chosen.ApplicationIdentity, chosen.ApplicationName,
            AddToApproved: chooser?.UsedSecondaryChoiceAction == true);
    }
}
