using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class SharingTests
{
    private static async Task EncoderPreferenceTests(Action<bool,string> check)
    {
        static ViewNode Selector(Harness h,string key="encoder")=>Nodes(h.Snapshot.Root).Single(n=>n.Id=="discord.share."+key);
        static string Choice(Harness h,string value,string key="encoder")=>Selector(h,key).SelectOptions!.Single(option=>option.Id==value).ActionId;
        static string Selected(Harness h,string key="encoder")=>Selector(h,key).SelectOptions!.Single(option=>option.IsSelected).Id;
        static DiscordWebViewWidget.ChannelAccountPreferences Saved(WidgetTestPrivateState storage)=>
            JsonSerializer.Deserialize<DiscordWebViewWidget.ChannelPreferenceStore>(storage.Json!)!.Accounts!.Single(account=>account.Account=="1");
        static async Task Ready(Harness h)
        {
            await h.Widget.WaitForStateReadAsync();await h.Widget.WaitForChannelPreferencesAsync();await h.Widget.WaitForPreferenceWriteAsync();
        }
        static async Task Select(Harness h,string value,string key="encoder")
        {await h.Act(Choice(h,value,key));await h.Widget.WaitForPreferenceWriteAsync();}

        var storage=new WidgetTestPrivateState("""{"SchemaVersion":1,"Accounts":[{"Account":"1","Favorites":["20"],"Recent":["30"],"ContentPreference":"detail","BandwidthPreference":"high-detail"}]}""",1);
        await using(var h=await Harness.CreateAsync(preferences:storage))
        {
            await Ready(h);
            check(Selected(h)=="discord"&&Selector(h).IsDisabled!=true&&storage.Revision==1,
                "Existing accounts retain Discord default encoding without a silent preference migration");
            await Select(h,"prefer-hardware");
            var saved=Saved(storage);
            check(Selected(h)=="prefer-hardware"&&saved.EncoderPreference=="prefer-hardware"&&saved.BandwidthPreference=="high-detail"&&
                saved.ContentPreference=="detail"&&saved.Favorites.SequenceEqual(new[]{"20"})&&saved.Recent.SequenceEqual(new[]{"30"})&&
                h.Provider.Commands.IsEmpty&&h.Capture.Prepares.IsEmpty,
                "Opting into hardware H.264 preserves bandwidth and other preferences without starting capture");
            await Select(h,"automatic","bandwidth");
            check(Saved(storage).EncoderPreference=="prefer-hardware"&&Saved(storage).BandwidthPreference=="automatic",
                "Changing bandwidth does not erase the independent encoder preference");
            await Select(h,"high-detail","bandwidth");
            var old=Choice(h,"discord");
            h.Provider.State=h.Provider.State with{Account="2"};await h.Refresh();
            var revision=storage.Revision;await h.Act(old);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selected(h)=="discord"&&storage.Revision==revision,"An encoder option cannot carry its preference into another account");
            h.Provider.State=h.Provider.State with{Account="1",ChannelId="20"};await h.Refresh();
            revision=storage.Revision;await h.Act(old);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selected(h)=="prefer-hardware"&&storage.Revision==revision,"A stale encoder selection cannot cross a call boundary");
            old=Choice(h,"discord");h.Provider.Reference=new(new string('d',32));await h.Refresh();
            revision=storage.Revision;await h.Act(old);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selected(h)=="prefer-hardware"&&storage.Revision==revision,"A stale encoder selection cannot cross a backend session boundary");
        }
        await using(var h=await Harness.CreateAsync(preferences:storage))
        {
            await Ready(h);check(Selected(h)=="prefer-hardware","Hardware encoder opt-in persists across widget lifetimes for the same account");
            var stale=Choice(h,"discord");
            h.Capture.PrepareDelivery=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await h.Act("share");await Wait(()=>h.Capture.Prepares.Count==1);
            var revision=storage.Revision;await h.Act(stale);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selector(h).IsDisabled==true&&Selected(h)=="prefer-hardware"&&storage.Revision==revision,
                "An outstanding native source chooser keeps its captured encoder intent and rejects stale selector actions");
            h.Capture.PrepareDelivery.SetResult();await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            var payload=h.Provider.Commands.Single(command=>command.Method=="share").Args;
            check(payload.GetProperty("encoderPreference").GetString()=="prefer-hardware"&&payload.GetProperty("bandwidthPreference").GetString()=="high-detail"&&
                h.Capture.Prepares.Single().Options.ContentPreference==WebCaptureContentPreference.Detail,
                "Sharing sends independent immutable encoder and bandwidth policies with unchanged capture content preference");
            await h.Widget.WaitForPreferenceWriteAsync();revision=storage.Revision;
            await h.Act(stale);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selector(h).IsDisabled==true&&storage.Revision==revision,"Prepared sharing cannot be reconfigured by an old encoder option");
            h.Provider.State=h.Provider.State with{Sharing=true,ShareQualityWarning="share_encoder_unconfirmed"};
            h.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await h.Refresh();
            check(Nodes(h.Snapshot.Root).Single(n=>n.Id=="discord.share.quality-warning").Text!.Contains("encoder preference")&&h.Capture.Stops.IsEmpty,
                "An unconfirmed encoder preference warns without ending a working stream");
        }
        await using(var h=await Harness.CreateAsync())
        {
            await Ready(h);await h.Act("share");await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            var payload=h.Provider.Commands.Single(command=>command.Method=="share").Args;
            check(payload.GetProperty("encoderPreference").GetString()=="discord"&&payload.GetProperty("bandwidthPreference").GetString()=="automatic",
                "A new account sends Discord default encoding and Automatic bandwidth without opting in");
        }
        var malformed=new WidgetTestPrivateState("""{"SchemaVersion":1,"Accounts":[{"Account":"1","Favorites":[],"Recent":[],"EncoderPreference":"force-hardware"}]}""",1);
        await using(var h=await Harness.CreateAsync(preferences:malformed))
        {
            await Ready(h);check(Selected(h)=="discord"&&malformed.Revision==1,
                "Unknown saved encoder values fall back to Discord default without rewriting saved data");
        }
        var valid=new FakeSession().State with{ShareQualityWarning="share_encoder_unconfirmed"};valid.Validate();
        var rejected=false;try{(valid with{ShareQualityWarning="share_encoder_other"}).Validate();}catch(InvalidDataException){rejected=true;}
        check(rejected,"Encoder warning validation admits only the recognized provider code");

        foreach(var (key,chosen,original) in new[]{("encoder","prefer-hardware","discord"),("bandwidth","high-detail","automatic")})
        {
            var delayedStorage=new WidgetTestPrivateState();
            var inner=new WidgetTestHostServicesBuilder().WithPrivateState(delayedStorage).Build();
            var builder=new WidgetTestHostServicesBuilder();
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writes=0;
            // Forward original request/response objects to the ordinary private
            // state fixture, retaining its real runtime types and revision CAS.
            // Only delivery of a write is delayed; no widget test hook or wire
            // DTO reconstruction changes the behavior being tested.
            foreach(var member in new[]{"read","write","clear"})
            {
                var operation=new WidgetCapabilityOperation<object,object>("storage.private-state.v1","private-state."+member);
                builder.WithHandler(operation,async(request,token)=>
                {
                    if(operation.OperationId=="private-state.write")
                    {
                        Interlocked.Increment(ref writes);entered.TrySetResult();
                        await release.Task.WaitAsync(token);
                    }
                    return await inner.Capabilities.InvokeAsync(operation,request,token);
                });
            }
            await using var h=new Harness();
            h.Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(h.Provider,h.Clock,TimeSpan.FromHours(1)),builder.Build());
            try
            {
                await WidgetTestHost.InitializeAsync(h.Widget);await WidgetTestHost.SetLifecycleStateAsync(h.Widget,WidgetLifecycleState.Interactive);await Ready(h);
                var action=Choice(h,chosen,key);await h.Act(action);await entered.Task.WaitAsync(TimeSpan.FromSeconds(6));
                check(Selector(h,key).IsDisabled!=true&&Selected(h,key)==original,
                    $"Pending {key} persistence keeps the initiating Select enabled and retains its last confirmed value");
                await h.Act(action);await h.Act(Choice(h,original,key));
                check(writes==1&&delayedStorage.Revision==0,$"Pending {key} duplicate and alternate choices do not start another write");
                release.TrySetResult();await h.Widget.WaitForPreferenceWriteAsync();
                check(writes==1&&delayedStorage.Revision==1&&Selected(h,key)==chosen&&Selector(h,key).IsDisabled!=true,
                    $"Confirmed {key} persistence updates the same enabled selector without queued duplicate work");
            }
            finally{release.TrySetResult();}
        }
    }
}
