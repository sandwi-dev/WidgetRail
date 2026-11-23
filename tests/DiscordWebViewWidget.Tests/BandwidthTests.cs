using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class SharingTests
{
    private static async Task BandwidthTests(Action<bool,string> check)
    {
        var validState=new FakeSession().State with{ShareQualityWarning="share_bandwidth_unconfirmed"};
        validState.Validate();
        var rejected=false;
        try{(validState with{ShareQualityWarning="share_bandwidth_unknown"}).Validate();}catch(InvalidDataException){rejected=true;}
        check(rejected,"Bandwidth warnings admit the known provider code without accepting arbitrary warning values");
        static ViewNode Selector(Harness h)=>Nodes(h.Snapshot.Root).Single(n=>n.Id=="discord.share.bandwidth");
        static string Choice(Harness h,string id)=>Selector(h).SelectOptions!.Single(option=>option.Id==id).ActionId;
        static string Selected(Harness h)=>Selector(h).SelectOptions!.Single(option=>option.IsSelected).Id;
        static DiscordWebViewWidget.ChannelPreferenceStore Read(WidgetTestPrivateState storage)=>
            JsonSerializer.Deserialize<DiscordWebViewWidget.ChannelPreferenceStore>(storage.Json!)!;
        static async Task Ready(Harness h)
        {
            await h.Widget.WaitForStateReadAsync();
            await h.Widget.WaitForChannelPreferencesAsync();
            await h.Widget.WaitForPreferenceWriteAsync();
        }

        var storage=new WidgetTestPrivateState("""{"SchemaVersion":1,"Accounts":[{"Account":"1","Favorites":["20"],"Recent":["30"],"ContentPreference":"detail"},{"Account":"2","Favorites":[],"Recent":[],"BandwidthPreference":"automatic"}]}""",1);
        await using(var h=await Harness.CreateAsync(preferences:storage))
        {
            await Ready(h);
            check(Selected(h)=="automatic"&&Selector(h).IsDisabled!=true,
                "Existing Discord preferences default to Automatic bandwidth without changing the capture chooser");
            await h.Act(Choice(h,"high-detail"));await h.Widget.WaitForPreferenceWriteAsync();
            var saved=Read(storage).Accounts!.Single(account=>account.Account=="1");
            check(Selected(h)=="high-detail"&&saved.BandwidthPreference=="high-detail"&&saved.Favorites.SequenceEqual(new[]{"20"})&&
                saved.Recent.SequenceEqual(new[]{"30"})&&saved.ContentPreference=="detail"&&h.Provider.Commands.IsEmpty&&h.Capture.Prepares.IsEmpty,
                "Higher-detail bandwidth persists without starting a share or overwriting other preferences");
            var stale=Choice(h,"automatic");
            h.Provider.State=h.Provider.State with{Account="2"};await h.Refresh();
            var revision=storage.Revision;await h.Act(stale);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selected(h)=="automatic"&&storage.Revision==revision,
                "A delayed bandwidth selection cannot cross an account boundary");
            h.Provider.State=h.Provider.State with{Account="1",ChannelId="20"};await h.Refresh();
            revision=storage.Revision;await h.Act(stale);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selected(h)=="high-detail"&&storage.Revision==revision,
                "A delayed bandwidth selection cannot cross a call boundary");
        }
        await using(var h=await Harness.CreateAsync(preferences:storage))
        {
            await Ready(h);
            check(Selected(h)=="high-detail","Bandwidth preference survives a new widget lifetime for its account");
            var stale=Choice(h,"automatic");
            await h.Act("share");await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            await h.Widget.WaitForPreferenceWriteAsync();
            check(h.Provider.Commands.Single(command=>command.Method=="share").Args.GetProperty("bandwidthPreference").GetString()=="high-detail"&&
                h.Capture.Prepares.Single().Options.ContentPreference==WebCaptureContentPreference.Detail,
                "The immutable share request sends the saved bandwidth while preserving the independent content preference");
            var revision=storage.Revision;await h.Act(stale);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selector(h).IsDisabled==true&&storage.Revision==revision&&Selected(h)=="high-detail",
                "Bandwidth cannot mutate a prepared or active share through an old menu action");
            h.Provider.State=h.Provider.State with{Sharing=true,ShareQualityWarning="share_bandwidth_unconfirmed"};
            h.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await h.Refresh();
            check(Nodes(h.Snapshot.Root).Single(n=>n.Id=="discord.share.quality-warning").Text!.Contains("selected bandwidth")&&h.Capture.Stops.IsEmpty,
                "Unconfirmed bandwidth remains a readable warning while a working stream stays active");
        }
        await using(var h=await Harness.CreateAsync())
        {
            await Ready(h);await h.Act("share");await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            check(h.Provider.Commands.Single(command=>command.Method=="share").Args.GetProperty("bandwidthPreference").GetString()=="automatic",
                "A new account explicitly preserves Discord's automatic encoder bandwidth policy");
        }
        await using(var h=await Harness.CreateAsync())
        {
            await Ready(h);var stale=Choice(h,"high-detail");
            h.Capture.PrepareDelivery=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await h.Act("share");await Wait(()=>h.Capture.Prepares.Count==1);
            await h.Act(stale);await h.Widget.WaitForPreferenceWriteAsync();
            check(Selector(h).IsDisabled==true&&Selected(h)=="automatic","The source chooser cannot race a stale bandwidth selection");
            h.Capture.PrepareDelivery.SetResult();await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            check(h.Provider.Commands.Single(command=>command.Method=="share").Args.GetProperty("bandwidthPreference").GetString()=="automatic",
                "A source selection retains the bandwidth captured for its own share attempt");
        }
        var malformed=new WidgetTestPrivateState("""{"SchemaVersion":1,"Accounts":[{"Account":"1","Favorites":[],"Recent":[],"BandwidthPreference":"999-mbps"}]}""",1);
        await using(var h=await Harness.CreateAsync(preferences:malformed))
        {
            await Ready(h);check(Selected(h)=="automatic"&&malformed.Revision==1,
                "Unrecognized saved bandwidth normalizes to Automatic without rewriting the preference file");
        }
        var broken=new WidgetTestPrivateState("""{"SchemaVersion":99} """,1);
        await using(var h=await Harness.CreateAsync(preferences:broken))
        {
            await Ready(h);check(Selector(h).IsDisabled==true&&Selected(h)=="automatic"&&broken.Revision==1,
                "Unreadable preference schemas disable changes without crashing or overwriting saved data");
        }
    }
}
