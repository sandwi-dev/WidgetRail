using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task LibraryCaptureHandsOffWithoutReview()
    {
        CaptureSessionRequest? image = null, clip = null;
        await using var fixture = new GalleryFixture((builder, _) => builder
            .WithHandler(WidgetCaptureCapabilities.SaveScreenshot, (request, _) =>
            {
                image = request;
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            })
            .WithHandler(WidgetCaptureCapabilities.RecordClip, (request, _) =>
            {
                clip = request;
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            }));
        await fixture.Start();
        await fixture.Action("capture-library.photo");
        await fixture.Until(() => image is not null && !fixture.Busy);
        Check(image is { Screenshot: not null, Recording: null, Preparation.DelaySeconds: 0 } && Preview(fixture) is null,
            "A fresh gallery screenshot should be immediate and stay in the gallery");
        await fixture.Action("capture-library.video");
        await fixture.Until(() => clip is not null && !fixture.Busy);
        Check(clip is { Recording: not null, Screenshot: null, Preparation.DelaySeconds: 5, OutputPurpose: CaptureOutputPurpose.LibraryRecording,
            MaximumBytes: CaptureLimits.MaximumLibraryRecordingBytes }, "Clip did not use the library recording budget");
        Check(fixture.View().Root.Transition?.Key == "home" && fixture.Node("capture-library.save") is null,
            "A completed handoff opened a review page");
    }

    internal static async Task ScreenshotPreferences()
    {
        var state = new WidgetTestPrivateState();
        CaptureSessionRequest? request = null;
        GalleryFixture Open() => new((builder, _) => builder.WithHandler(WidgetCaptureCapabilities.SaveScreenshot, (value, _) =>
        {
            request = value;
            return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
        }), state);
        async Task TrayScreenshot(GalleryFixture fixture, int seconds)
        {
            request = null;
            var snapshot = fixture.View();
            var offer = snapshot.QuickActions.Single(item => item.TrayOffer?.Id == "screenshot");
            Check((await WidgetTestHost.InvokeTrayActionAsync(fixture.Widget, snapshot, offer)).Handled &&
                request?.Preparation.DelaySeconds == seconds, "Tray screenshot used the wrong delay");
        }
        await using (var initial = Open())
        {
            await initial.Start();
            await TrayScreenshot(initial, 0);
            await initial.Action("capture-library.settings"); await initial.Action("capture-library.section.Preparation.open");
            await initial.Until(() => initial.Node("capture-library.setting.image.delay") is not null && !initial.Busy);
            await initial.Action("capture-library.option.countdown.off");
            await initial.Until(() => state.Revision == 1 && !initial.Busy);
            Check(initial.Node("capture-library.setting.image.delay")!.SelectOptions.Single(o => o.IsSelected).Label == "Immediately",
                "Changing countdown changed screenshot delay");
            await initial.Action("capture-library.option.image.delay.10");
            await initial.Until(() => state.Revision == 2 && !initial.Busy);
            Check(initial.Node("capture-library.setting.delay")!.SelectOptions.Single(o => o.IsSelected).Label == "5 seconds",
                "Changing screenshot delay changed clip delay");
        }
        await using (var restarted = Open())
        {
            await restarted.Start();
            await TrayScreenshot(restarted, 10);
        }
        state.SimulateExternalWriteJson("""{"Preparation":{"DelaySeconds":5,"ShowCountdown":false}}""");
        await using var previousProfile = Open();
        await previousProfile.Start();
        await TrayScreenshot(previousProfile, 5);
    }

    internal static async Task ApprovalManagementWhileRecording()
    {
        var reads = 0;
        var removes = 0;
        var game = new ReplayApprovedApplication(new string('a', 64), "Fixture game");
        var approvals = new ReplayApplicationApprovals([game]);
        await using var fixture = new GalleryFixture((builder, instance) => builder
            .WithHandler(WidgetCaptureReplayCapabilities.Approvals, (_, _) =>
            {
                reads++;
                return ValueTask.FromResult(new ReplayApprovalList(instance.Replay.SettingsRevision, approvals));
            })
            .WithHandler(WidgetCaptureReplayCapabilities.RemoveApproval, (request, _) =>
            {
                Check(request.ApplicationId == game.Id && request.ExpectedRevision == instance.Replay.SettingsRevision, "Removal lost its revision");
                removes++;
                approvals = ReplayApplicationApprovals.Empty;
                var next = instance.Replay with { Revision = instance.Replay.Revision + 1, SettingsRevision = request.ExpectedRevision + 1 };
                instance.NotifyReplay(next);
                return ValueTask.FromResult(new CaptureReplayControlResult(CaptureReplayOutcome.Accepted, next));
            }));
        fixture.Replay = new(2, 1, new() { Enabled = true }, CaptureReplayPhase.Recording)
            { SessionId = new string('b', 32), TargetName = game.Name };
        await fixture.Start();
        await fixture.Action("capture-library.settings"); await fixture.Action("capture-library.section.Replay.open");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.approvals.open") is { IsDisabled: not true });
        Check(reads == 0, "Opening general settings queried the list");
        await fixture.Action("capture-library.approvals.open");
        var button = "capture-library.approvals." + game.Id;
        await fixture.Until(() => fixture.Node(button) is { IsDisabled: not true });
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0, "Invalid approvals modal");
        var action = fixture.Node(button)!.ActionId!;
        fixture.NotifyReplay(fixture.Replay with { Revision = 3, BufferedDurationSeconds = 5 });
        await fixture.Action(action);
        await fixture.Until(() => fixture.Node("capture-library.approvals.empty") is not null);
        await fixture.Action(action);
        Check(removes == 1 && reads == 2 && fixture.Replay.Phase == CaptureReplayPhase.Recording,
            "Stale removal or buffer updates changed the approvals");
    }
}
