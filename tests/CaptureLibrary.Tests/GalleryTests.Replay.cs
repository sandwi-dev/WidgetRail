using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task ReplayUnstartedHomeKeepsOneStart()
    {
        foreach(var outcome in new[]{CaptureReplayOutcome.Cancelled,CaptureReplayOutcome.Unavailable,CaptureReplayOutcome.Failed})
        {
            await using var fixture=new GalleryFixture{ReplayStartOutcome=outcome};
            await fixture.Start();
            await fixture.Until(()=>fixture.ReplayReads>=1&&fixture.Node("capture-library.replay.start") is not null);
            await fixture.Action("capture-library.replay.start");
            await fixture.Until(()=>fixture.ReplayStarts==1&&fixture.Nodes.Any(node=>node.Id=="capture-library.replay.start"&&node.IsDisabled!=true));
            Check(ViewSnapshotValidator.Validate(fixture.View()).Count==0,"An unstarted replay must publish a valid home view: "+outcome);
            Check(fixture.Nodes.Count(node=>node.Id=="capture-library.replay.start")==1&&!fixture.Replay.Settings.Enabled,
                "An unstarted replay must retain one Start command without enabling recording");
            Check((fixture.Node("capture-library.replay.notice") is null)==(outcome==CaptureReplayOutcome.Cancelled),
                "User cancellation is quiet; unavailable/failed starts retain actionable feedback");
            fixture.ReplayStartOutcome=CaptureReplayOutcome.Accepted;
            await fixture.Action("capture-library.replay.start");
            await fixture.Until(()=>fixture.ReplayStarts==2&&fixture.Node("capture-library.replay.stop") is not null);
            Check(ViewSnapshotValidator.Validate(fixture.View()).Count==0&&fixture.Node("capture-library.replay.start") is null,
                "Retry must replace the single Start with the active replay controls");
        }
    }

    internal static async Task ReplayUsesHostStatusAndSessionFences()
    {
        await using var fixture=new GalleryFixture();await fixture.Start();
        await fixture.Until(()=>fixture.ReplayReads>=1 && fixture.Node("capture-library.replay.start") is not null);
        Check(fixture.ReplayStarts==0&&fixture.ReplayReads==1,"Opening Capture Library must observe without starting recording or polling");
        await fixture.Action("capture-library.replay.start");await fixture.Until(()=>fixture.Node("capture-library.replay.stop") is not null);
        var stoppedSession=fixture.Node("capture-library.replay.stop")!.ActionId!;
        fixture.NotifyReplay(fixture.Replay with{Revision=10,SessionId=new string('b',32),TargetName="New game",BufferedDurationSeconds=5});
        await fixture.Until(()=>fixture.Node("capture-library.replay.status")?.Text?.Contains("New game")==true);
        await fixture.Action(stoppedSession);
        Check(fixture.ReplayStops==0,"A stale Stop must not affect a replacement recording");
        fixture.NotifyReplay(fixture.Replay with{Revision=9,TargetName="Old event"});
        await Task.Yield();
        Check(fixture.Node("capture-library.replay.status")?.Text?.Contains("New game")==true,"Delayed replay event replaced newer status");
        fixture.Replay=fixture.Replay with{Revision=10,TargetName="New game"};
        fixture.ReplaySaveGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save=fixture.Node("capture-library.replay.save")!.ActionId!;
        var query=fixture.Collection!.IndexedCollection!;
        await fixture.Action(save);await fixture.Until(()=>fixture.ReplaySaves==1);await fixture.Action(save);
        Check(fixture.ReplaySaves==1&&fixture.Collection!.IndexedCollection==query&&fixture.Node("capture-library.search")?.IsBusy!=true,
            "Saving is single-flight without disabling or replacing the gallery");
        fixture.ReplaySaveGate.SetResult();await fixture.Until(()=>fixture.LastFeedback?.StartsWith("Saved 5 seconds")==true);
        Check(fixture.Node("capture-library.replay.notice") is null&&
            fixture.Node("capture-library.replay.buffer")?.Text?.StartsWith("Saved 5s · ")==true&&
            fixture.Node("capture-library.replay.panel") is not null,"Saved feedback must fit in the recording deck without an extra notice row");
        var notifications=fixture.Notifications.Count;
        for(var render=0;render<3;render++)fixture.View();
        Check(fixture.Notifications.Count==notifications&&fixture.Notifications.Last() is
            {Retention:WidgetNotificationRetention.BannerOnly,Interaction:WidgetNotificationInteraction.Informational,Visibility:WidgetNotificationVisibility.Always},
            "Action feedback must use an informational host banner and not republish during rendering");
        var subscriptions=fixture.ReplaySubscriptions;
        await WidgetTestHost.SetLifecycleStateAsync(fixture.Widget,WidgetLifecycleState.Background);
        Check(fixture.ReplayStops==0,"Widget backgrounding stopped a host-owned recording");
        await WidgetTestHost.SetLifecycleStateAsync(fixture.Widget,WidgetLifecycleState.Interactive);
        await fixture.Until(()=>fixture.ReplaySubscriptions==subscriptions+1);
        await fixture.Action(fixture.Node("capture-library.replay.stop")!.ActionId!);
        await fixture.Until(()=>fixture.ReplayStops==1&&fixture.Node("capture-library.replay.start") is not null);
        Check(!fixture.Replay.Settings.Enabled,"Explicit Stop did not disable recording");
    }

    internal static async Task ReplaySettingsPreserveHostRevision()
    {
        await using var fixture=new GalleryFixture();await fixture.Start();await fixture.Action("capture-library.settings");
        await fixture.Until(()=>fixture.Node("capture-library.recording.replay.resolution") is not null&&!fixture.Busy);
        Check(fixture.Node("capture-library.recording.replay.resolution")!.SelectOptions.Single(option=>option.IsSelected).Label.Contains("1280",StringComparison.Ordinal),
            "Replay default must show its actual720p setting, not the larger1080p qualification envelope");
        Check(fixture.Node("capture-library.replay.audio")!.SelectOptions.Single(option=>option.IsSelected) is {IsDisabled:true} unavailableAudio &&
            unavailableAudio.Label.Contains("unavailable",StringComparison.Ordinal),
            "Unsupported saved application audio stays visible and cannot masquerade as an available choice");
        var oldMemory=fixture.Node("capture-library.replay.memory")!.SelectOptions.Single(option=>option.Label=="64 MiB").ActionId;
        await fixture.Action(fixture.Node("capture-library.replay.duration")!.SelectOptions.Single(option=>option.Label=="30 seconds").ActionId);
        await fixture.Until(()=>fixture.ReplayConfigurations==1&&fixture.Node("capture-library.replay.duration")!.SelectOptions.Any(option=>option.Label=="30 seconds"&&option.IsSelected));
        await fixture.Action(oldMemory);Check(fixture.ReplayConfigurations==1,"Stale settings control overwrote latest host preferences");
        Check(fixture.Replay.Settings.ReplayDurationSeconds==30&&!fixture.Replay.Settings.Enabled&&fixture.ReplayStarts==0,
            "Editing replay preferences must not silently start capture");
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count==0,"Replay settings declaration invalid");
    }

    internal static async Task ReplayStoppingRemainsVisible()
    {
        await using var fixture=new GalleryFixture();await fixture.Start();
        await fixture.Until(()=>fixture.ReplayReads==1);await fixture.Action("capture-library.replay.start");
        await fixture.Until(()=>fixture.ReplayStarts==1);
        fixture.NotifyReplay(fixture.Replay with{Revision=10,Settings=fixture.Replay.Settings with{Enabled=false},Phase=CaptureReplayPhase.Stopping,Saving=true});
        await fixture.Until(()=>fixture.Node("capture-library.replay.status")?.Text=="Stopping recording…");
        Check(fixture.Node("capture-library.replay.stop")?.IsDisabled==true && fixture.Node("capture-library.replay.start") is null &&
            fixture.Node("capture-library.more")!.SelectOptions.All(option=>option.ActionId!="capture-library.replay.start") &&
            fixture.Node("capture-library.count")?.Text?.Contains("Replay off")==false,
            "Stopping/saving remains visible after desired Enabled becomes false; a replacement Start must not be offered yet");
        fixture.NotifyReplay(fixture.Replay with{Revision=11,Phase=CaptureReplayPhase.Off,SessionId=null,Saving=false});
        await fixture.Until(()=>fixture.ReplayReads>=1 && fixture.Node("capture-library.replay.start") is not null);
        Check(fixture.Node("capture-library.replay.status") is null,"Completed Stop should release the active recording row");
    }
}
