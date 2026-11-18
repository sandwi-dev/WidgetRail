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
        replayCoordinator.Start();
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
            var observed = NativeWindowCapture.ForegroundTarget().ToTarget(command.Id);
            var foreground = new NativeWindowPreviewTarget(observed.Handle.ToString("X"), observed.ProcessId,
                observed.ProcessCreated.ToString("X16"), observed.ClassName);
            var windows = await catalog.GetWindowsAsync(token).ConfigureAwait(false);
            var window = windows.FirstOrDefault(item => !item.IsMinimized && item.PreviewTarget == foreground &&
                string.Equals(item.ApplicationIdentity, command.ApprovedApplication, StringComparison.OrdinalIgnoreCase));
            var choice = WebCaptureSourcePolicy.Candidates(windows, (uint)Environment.ProcessId).FirstOrDefault(item => item.Target == window?.PreviewTarget);
            return window?.ApplicationIdentity is { } identity && choice is not null
                ? new(true, choice.Target, identity, choice.ApplicationName) : new(false, Issue: CaptureReplayIssue.TargetUnavailable);
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
    private async Task<ReplayCaptureDecision> ChooseReplayAsync(HostReplayWork work, CancellationToken token)
    {
        if (work.Command is not { } command || work.WidgetId is null || retired || !visible || !foreground || !interactive ||
            activeWidget != work.WidgetId || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen || owner is null) return new(false);
        var selection = selectionVersion;
        var requestOwner = owner;
        bool Current() => !retired && visible && foreground && interactive && activeWidget == work.WidgetId &&
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
        var identities = windows.Where(window => window.ApplicationIdentity is not null && window.PreviewTarget is not null)
            .ToDictionary(window => window.PreviewTarget!, window => window.ApplicationIdentity!);
        var choices = WebCaptureSourcePolicy.Candidates(windows, (uint)Environment.ProcessId)
            .Where(item => identities.ContainsKey(item.Target) && !OwnsBrowserUiProcess(item.Target.ProcessId)).ToArray();
        if (choices.Length == 0) return new(false, Issue: CaptureReplayIssue.TargetUnavailable);
        var result = await ShowHostChoiceAsync("Record an application", details,
            choices.Select(item => new HostDialogChoice(item.Id, item.Label)).ToArray(), token,
            configure: dialog => dialog.ConfigurePreferenceList(closeLabel: "Cancel"));
        if (!Current() || !await requestOwner.Session.IsReplayCommandCurrentAsync(command.Id, token)) return new(false);
        var chosen = choices.FirstOrDefault(item => item.Id == result);
        return chosen is null ? new(false) : new(true, chosen.Target, identities[chosen.Target], chosen.ApplicationName);
    }
}
