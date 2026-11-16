using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class VoicePreviewTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await Controls(check);
        foreach(var change in new[]{"account","call","session","participant","reopen","leave-rejoin","new-observation"})await LateReply(change,check);
    }
    private static async Task Controls(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        using(var self=await test.Host.AcquireAsync("discord.participants",0,1))
            check(self.Range.Items[0].Root.FocusPresentation is null,"Self has no participant audio preview");
        using var lease=await test.Acquire();var row=lease.Range.Items.Single();var content=row.Root.FocusPresentation!;
        check(test.Host.CurrentSnapshot.Root.Kind!=ViewNodeKind.ModalLayer &&
            Nodes(test.Host.CurrentSnapshot.Root).Any(node=>node.Id=="discord.participants.preview"&&node.FocusPreview?.EnterButton==ControllerButton.X),
            "Participant audio is an interactive hover/focus preview with host-owned X entry");
        var mute=Node(content,".mute");var slider=Node(content,".volume");
        check(slider.Minimum==0&&slider.Maximum==100&&slider.Step==5&&slider.Value==55&&slider.SliderInteractionMode==SliderInteractionMode.ActivateToAdjust,
            "Participant preview keeps the bounded activated volume slider");
        check(test.Session.Commands.IsEmpty&&test.Session.Core.MicrophoneRequests==0,"Showing audio preview has no side effects");
        var reads=test.Session.Core.Reads;
        check(lease.RoutePreviewAction(row.Key,mute.Id,mute.ActionId!)==WidgetOperationAdmission.Enqueued,"Exact participant preview mute admitted");
        await Until(()=>test.Session.Core.Reads>reads);await test.Widget.WaitForStateReadAsync();
        using(var changed=await test.Acquire())
            check(Node(changed.Range.Items.Single().Root.FocusPresentation!,".mute").Text=="Unmute for me","Mute label follows the fresh provider observation");
        check(lease.RoutePreviewAction(row.Key,mute.Id,mute.ActionId!) is null,"Old participant preview state cannot toggle newer audio");
        using(var volumeLease=await test.Acquire())
        {
            row=volumeLease.Range.Items.Single();slider=Node(row.Root.FocusPresentation!,".volume");reads=test.Session.Core.Reads;
            check(volumeLease.RoutePreviewValueAction(row.Key,slider.Id,slider.ValueChangedActionId!,80)==WidgetOperationAdmission.Enqueued,"Preview volume uses typed numeric admission");
            await Until(()=>test.Session.Core.Reads>reads);await test.Widget.WaitForStateReadAsync();
        }
        using(var changed=await test.Acquire())check(Node(changed.Range.Items.Single().Root.FocusPresentation!,".volume").Value==80,"Fresh provider volume reaches preview");
        test.Session.Core.State=test.Session.Core.State with {Participants=test.Session.Core.State.Participants.Select(person=>person.Id=="30"?person with{Audio=new(false,false,null)}:person).ToArray()};
        await test.Refresh();using(var unavailable=await test.Acquire())
            check(!Nodes(unavailable.Range.Items.Single().Root.FocusPresentation!).Any(node=>node.Kind==ViewNodeKind.Slider),"Unavailable provider has explanation instead of fake controls");
    }
    private static async Task LateReply(string change,Action<bool,string> check)
    {
        await using var test=await Harness.Create();using var old=await test.Acquire();var row=old.Range.Items.Single();var slider=Node(row.Root.FocusPresentation!,".volume");
        test.Session.HoldMethod="voiceVolume";
        check(old.RoutePreviewValueAction(row.Key,slider.Id,slider.ValueChangedActionId!,20)==WidgetOperationAdmission.Enqueued,"Delayed preview value admitted: "+change);
        await test.Session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var original=test.Session.Core.State;
        switch(change)
        {
            case "account":test.Session.Core.State=original with{Account="2"};await test.Refresh();break;
            case "call":test.Session.Core.State=original with{ChannelId="20"};await test.Refresh();break;
            case "session":test.Session.Core.Reference=new(new string('c',32));await test.Refresh();break;
            case "participant":test.Session.Core.State=original with{Participants=[original.Participants[0]]};await test.Refresh();break;
            case "reopen":old.Dispose();break;
            case "leave-rejoin":test.Session.Core.State=original with{ChannelId=null};await test.Refresh();test.Session.Core.State=original;await test.Refresh();break;
            case "new-observation":test.Session.Core.State=original with{Participants=original.Participants.Select(person=>person.Id=="30"?person with{Audio=new(true,false,90)}:person).ToArray()};await test.Refresh();break;
        }
        var reads=test.Session.Core.Reads;test.Session.Gate.TrySetResult();
        if(change!="session") {await Until(()=>test.Session.Core.Reads>reads);await test.Widget.WaitForStateReadAsync();}
        else await Task.Delay(50);
        if(change is "reopen" or "leave-rejoin" or "new-observation")
        {
            using var fresh=await test.Acquire();
            check(Node(fresh.Range.Items.Single().Root.FocusPresentation!,".volume").Value==(change=="new-observation"?90:55),
                "Late acknowledgment cannot overwrite reopened/current audio: "+change);
        }
        check(test.Session.Commands.Count(command=>command.Method=="voiceVolume")==1,"Reconciliation never replays external volume write: "+change);
        test.Host.PublishSnapshot();
        check(old.RoutePreviewValueAction(row.Key,slider.Id,slider.ValueChangedActionId!,80) is null,"Retired preview rejects later input: "+change);
    }
    private sealed class Harness:IAsyncDisposable
    {
        internal VoiceControlTests.VoiceSession Session=new();internal DiscordWebViewWidget Widget=null!;internal WidgetIndexedCollectionTestHost Host=null!;
        internal static async Task<Harness> Create()
        {
            var test=new Harness();test.Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(test.Session,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(test.Widget);await WidgetTestHost.SetLifecycleStateAsync(test.Widget,WidgetLifecycleState.Interactive);await test.Widget.WaitForStateReadAsync();
            test.Host=WidgetTestHost.CreateIndexedCollectionHost(test.Widget,"voice-preview");return test;
        }
        internal async Task<WidgetIndexedCollectionTestLease> Acquire(){Host.PublishSnapshot();return await Host.AcquireAsync("discord.participants",1,1);}
        internal async Task Refresh(){await Widget.OnActionAsync(new("refresh","test"));await Widget.WaitForStateReadAsync();Host.PublishSnapshot();}
        public async ValueTask DisposeAsync(){Session.Gate.TrySetResult();Host.Dispose();await WidgetTestHost.DestroyAsync(Widget);}
    }
    private static ViewNode Node(ViewNode root,string suffix)=>Nodes(root).Single(node=>node.Id.EndsWith(suffix,StringComparison.Ordinal));
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Until(Func<bool> condition){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!condition())await Task.Delay(10,timeout.Token);}
}
