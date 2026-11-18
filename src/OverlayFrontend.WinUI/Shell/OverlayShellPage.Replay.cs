using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly object replayNotificationOwner = new();
    internal CaptureReplayStatus? ReplayStatusForHost => owner?.Session.ReplayStatus;

    internal async Task SaveReplayFromHostAsync(string? expectedSessionId = null)
    {
        if (retired || owner is not { } current) return;
        try
        {
            var status = await current.Session.GetReplayStatusAsync(lifetime.Token);
            if (retired || !ReferenceEquals(owner, current)) return;
            var sessionId = expectedSessionId ?? status.SessionId;
            if (sessionId is null)
            {
                await ShowReplayNoticeAsync("Replay recording is off. Start recording in Capture Library first.");
                return;
            }
            var result = await current.Session.SaveReplayAsync(sessionId, lifetime.Token);
            if (retired || !ReferenceEquals(owner, current)) return;
            await ShowReplayNoticeAsync(result.Outcome == CaptureReplayOutcome.Accepted
                ? $"Saved {result.ActualDurationSeconds:0.#} seconds to Capture Library."
                : ReplayHostIssue(result.Issue));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Diagnostics.FrontendFailureLog.Current.Write("replay-save", error);
            if (!retired) await ShowReplayNoticeAsync("The replay could not be saved. Check recording status in Capture Library.");
        }
    }

    internal async Task StopReplayFromHostAsync(string? expectedSessionId)
    {
        if (retired || owner is not { } current) return;
        try
        {
            // A menu command carries its observed session, including null while
            // waiting. Never replace it with a newer recording's session ID.
            var result = await current.Session.StopReplayAsync(expectedSessionId, lifetime.Token);
            if (retired || !ReferenceEquals(owner, current)) return;
            await ShowReplayNoticeAsync(result.Outcome == CaptureReplayOutcome.Accepted
                ? result.Status.Phase == CaptureReplayPhase.Stopping ? "Stopping replay recording…" : "Replay recording stopped."
                : !result.Status.Settings.Enabled && result.Issue == CaptureReplayIssue.StorageUnavailable
                    ? (result.Status.Phase == CaptureReplayPhase.Stopping ? "Recording is stopping" : "Recording is off") +
                        ", but the off setting could not be saved. Check available disk space before restarting WidgetRail."
                    : ReplayHostIssue(result.Issue));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Diagnostics.FrontendFailureLog.Current.Write("replay-stop", error);
            if (!retired) await ShowReplayNoticeAsync("Recording could not be stopped. Check recording status in Capture Library.");
        }
    }

    private async Task ShowReplayNoticeAsync(string text)
    {
        if (retired || notificationCenter is not { } center) return;
        await center.Ready;
        if (retired || !ReferenceEquals(notificationCenter, center)) return;
        center.Publish("settings", replayNotificationOwner, new("replay-status", new WidgetNotification("replay-status", "Replay recording")
        {
            Body = text,
            Visibility = WidgetNotificationVisibility.Always,
            Interaction = WidgetNotificationInteraction.Informational,
        }), () => !retired && ReferenceEquals(notificationCenter, center));
    }

    private static string ReplayHostIssue(CaptureReplayIssue issue) => issue switch
    {
        CaptureReplayIssue.BufferNotReady => "There is not enough recorded video yet. Try again in a moment.",
        CaptureReplayIssue.SaveInProgress => "A replay is already being saved.",
        CaptureReplayIssue.SessionChanged => "The recording changed. Try again with the current recording.",
        CaptureReplayIssue.StorageUnavailable => "The replay could not be saved. Check the capture folder and available disk space.",
        CaptureReplayIssue.PermissionRevoked or CaptureReplayIssue.FeatureRemoved => "Recording access is unavailable. Open Capture Library to review it.",
        _ => "Replay recording is unavailable. Check recording status in Capture Library.",
    };
}
