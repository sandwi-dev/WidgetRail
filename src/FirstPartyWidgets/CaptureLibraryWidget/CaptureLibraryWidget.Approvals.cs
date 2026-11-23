using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private const string ApprovalsPrefix = "capture-library.approvals.";
    private readonly WidgetQueryResource<long, ReplayApprovalList> approvedApplications;
    private volatile bool showApprovals;

    private bool HandleApprovalAction(string action, State state)
    {
        if (!action.StartsWith(ApprovalsPrefix, StringComparison.Ordinal)) return false;
        var command = action[ApprovalsPrefix.Length..];
        if (command == "close")
        {
            showApprovals = false;
            approvedApplications.Reset();
            Invalidate();
            return true;
        }
        if (!state.Settings || showSources || state.Replay is not { } replay) return true;
        if (state.Busy || CurrentRecordingDraft(true, state) is not null) return true;
        if (command == "open" || showApprovals && command == "retry")
        {
            showApprovals = true;
            Work(async token =>
            {
                await RequirePermissionsAsync([WidgetCaptureCapabilities.Permission], token);
                if (showApprovals && model.Value.Replay is { } current)
                    await approvedApplications.EnsureLoaded(current.SettingsRevision, token, forceRefresh: true).Completion;
            });
            Invalidate();
            return true;
        }
        if (!showApprovals || approvedApplications.GetSnapshot(replay.SettingsRevision).Value is not { } list ||
            list.Revision != replay.SettingsRevision) return true;
        var add = command == $"add.{list.Revision}";
        var removal = list.Approvals.Applications.FirstOrDefault(app => command == $"remove.{list.Revision}.{app.Id}");
        if (!add && removal is null) return true;
        Work(async token =>
        {
            var result = add
                ? await HostServices.Capture.Replay.ChooseApprovedApplicationAsync(list.Revision, token)
                : await HostServices.Capture.Replay.RemoveApprovedApplicationAsync(removal!.Id, list.Revision, token);
            token.ThrowIfCancellationRequested();
            ApplyReplay(result.Status);
            if (result.Outcome is not (CaptureReplayOutcome.Accepted or CaptureReplayOutcome.Cancelled))
                PublishFeedback(ReplaySettingsIssue(result.Issue));
        });
        return true;
    }

    private WidgetModal ApprovalModal(State state)
    {
        var snapshot = approvedApplications.GetSnapshot(state.Replay?.SettingsRevision ?? -1);
        var rows = new List<WidgetElement>();
        if (state.Replay?.TargetName is { } target)
            rows.Add(UI.Text($"Current replay: {target}. Removing an approval takes effect on future starts.", ApprovalsPrefix + "current"));
        if (snapshot.Value is { } list && list.Revision == state.Replay?.SettingsRevision)
        {
            rows.Add(UI.Button("Add application", ApprovalsPrefix + $"add.{list.Revision}", ApprovalsPrefix + "add")
                .Disabled(state.Busy || list.Approvals.Applications.Count >= ReplayApplicationApprovals.MaximumApplications));
            if (list.Approvals.Applications.Count == 0)
                rows.Add(UI.Text("No applications approved.", ApprovalsPrefix + "empty"));
            foreach (var app in list.Approvals.Applications.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                rows.Add(UI.Button("Remove " + app.Name, ApprovalsPrefix + $"remove.{list.Revision}.{app.Id}", ApprovalsPrefix + app.Id)
                    .Disabled(state.Busy));
        }
        else if (snapshot.Error is { } || !state.Busy && (state.Message is not null || state.Replay is null))
        {
            rows.Add(UI.Text(snapshot.Error?.Message ?? state.Message ?? "Approved applications are unavailable.", ApprovalsPrefix + "error"));
            rows.Add(UI.Button("Try again", ApprovalsPrefix + "retry", ApprovalsPrefix + "retry"));
        }
        else rows.Add(UI.Text("Loading approved applications…", ApprovalsPrefix + "loading"));
        return new("capture-library-approvals", "Approved applications", UI.Stack(ApprovalsPrefix + "list", rows.ToArray()).Classes("capture-approval-list"),
            "capture-library-approvals.close", ApprovalsPrefix + "close");
    }
}
