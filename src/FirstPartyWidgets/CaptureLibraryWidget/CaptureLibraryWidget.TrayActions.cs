using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private string? permissionAction;

    protected override async ValueTask PrepareTrayActionAsync(string actionId, CancellationToken token)
    {
        string[] permissions = actionId switch
        {
            "screenshot" or "record-clip" => [WidgetCaptureCapabilities.Permission, WidgetCaptureLibraryCapabilities.SavePermission],
            "save-replay" => [WidgetCaptureCapabilities.Permission, WidgetCaptureLibraryCapabilities.ReadPermission, WidgetCaptureLibraryCapabilities.SavePermission],
            _ => [WidgetCaptureCapabilities.Permission, WidgetCaptureLibraryCapabilities.ReadPermission]
        };
        var access = await HostServices.Permissions.CheckAsync(permissions, token);
        permissionAction = access.Any(item => item.State != WidgetPermissionState.Granted) ? actionId : null;
        if (permissionAction is not null) return;
        if (actionId is "record-clip" or "screenshot")
        {
            await LoadPreferencesAsync(token, showFailure: false);
            return;
        }
        try { ApplyReplay(await HostServices.Capture.Replay.GetAsync(token)); }
        catch (WidgetCapabilityException) { model.Update(state => state with { Replay = null }); }
    }

    private IReadOnlyList<WidgetQuickAction> CaptureTrayActions(State state)
    {
        var actions = new List<WidgetQuickAction>(5);
        if (!preferencesLoaded || state.Preferences is not null)
        {
            Add("screenshot", "Take screenshot", new("capture.save-screenshot"));
            Add("record-clip", "Record clip", new("capture.record-clip"));
        }
        if (permissionAction is { } blocked)
        {
            var label = blocked switch
            {
                "screenshot" => "Take screenshot", "record-clip" => "Record clip",
                "start-replay" => "Start replay", "save-replay" => "Save replay", _ => "Stop replay"
            };
            actions.Add(WidgetQuickAction.ForTray(label, new TrayActionOffer(blocked, "permission") { NeedsPermission = true }));
        }
        var replay = TrayReplayState.From(state.Replay);
        if (replay.CommandFor("capture.replay.start") is { } start) Add("start-replay", "Start replay", start, "save-replay");
        if (replay.CommandFor("capture.replay.save") is { } save) Add("save-replay", "Save replay", save);
        if (replay.CommandFor("capture.replay.stop") is { } stop) Add("stop-replay", "Stop replay", stop);
        return actions;

        void Add(string id, string label, TrayCaptureCommand command, string? fallbackFor = null)
        {
            if (id != permissionAction)
                actions.Add(WidgetQuickAction.ForTray(label, new TrayActionOffer(id, command.SessionId ?? id, fallbackFor)
                    { CaptureCommand = command }, new(WidgetCaptureCapabilities.Permission, command.Operation)));
        }
    }

    protected override async ValueTask<TrayActionResult> OnTrayActionAsync(TrayActionOffer offer, CancellationToken token)
    {
        if (offer.CaptureCommand is not { } command || !CaptureTrayActions(model.Value).Any(item => item.TrayOffer == offer))
            return default;
        try
        {
            switch (command.Operation)
            {
                case "capture.save-screenshot":
                case "capture.record-clip":
                    await LoadPreferencesAsync(token);
                    if (model.Value.Preferences is not { } preferences) return default;
                    var kind = command.Operation == "capture.save-screenshot" ? WindowCaptureKind.Screenshot : WindowCaptureKind.Video;
                    await HostServices.Capture.CaptureToLibraryAsync(preferences.Request(kind), token);
                    return TrayActionResult.Success;
                case "capture.replay.start":
                case "capture.replay.stop":
                    var control = command.Operation == "capture.replay.start"
                        ? await HostServices.Capture.Replay.StartAsync(token)
                        : await HostServices.Capture.Replay.StopAsync(command.SessionId, token);
                    ApplyReplay(control.Status);
                    if (control.Outcome is not (CaptureReplayOutcome.Accepted or CaptureReplayOutcome.Cancelled))
                        return TrayActionResult.Unavailable(ReplayIssue(control.Issue));
                    return TrayActionResult.Success;
                case "capture.replay.save" when command.SessionId is { } sessionId:
                    var saved = await HostServices.Capture.Replay.SaveAsync(sessionId, token);

                    if (saved.Outcome != CaptureReplayOutcome.Accepted)
                        return TrayActionResult.Unavailable(ReplayIssue(saved.Issue));
                    PublishFeedback(WidgetRail.Internal.CaptureReplayFeedback.Saved(saved));
                    return TrayActionResult.Success;
                default:
                    return default;
            }
        }
        catch (WidgetCapabilityException error)
        {
            return TrayActionResult.Unavailable(Explain(error.ErrorCode));
        }
    }
}
