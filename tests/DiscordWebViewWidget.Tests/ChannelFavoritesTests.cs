using System.Text.Json;
using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ChannelFavoritesTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var saved=new WidgetTestPrivateState("""{"SchemaVersion":1,"Accounts":[{"Account":"1","Favorites":["30"],"Recent":["20"]},{"Account":"2","Favorites":["40"],"Recent":[]}]}""",1);
        await using(var h=await Harness.Create(saved))
        {
            check((await h.Keys()).SequenceEqual(new[]{"channel.30","channel.20","channel.10","channel.40"}),"Favorites then recent channels lead one nonduplicated picker list");
            check(h.Provider.Commands.IsEmpty&&h.Provider.MicrophoneRequests==0,"Reading saved channels never joins voice or requests microphone access");
            var before=saved.Revision;await h.Favorite("40",true);await Until(()=>saved.Revision>before);
            check((await h.Keys()).SequenceEqual(new[]{"channel.30","channel.40","channel.20","channel.10"}),"Favorite action persists channel identity and moves it above recent channels");
            await h.Action("search","Lobby");
            check((await h.Keys()).SequenceEqual(new[]{"channel.10","channel.40","channel.20","channel.30"}),"Search ranking remains prefix-first and is not overridden by favorites");
            await h.Action("clear-search");
            h.Provider.State=h.Provider.State with{Account="2"};await h.Refresh();
            check((await h.Keys()).SequenceEqual(new[]{"channel.40","channel.10","channel.20","channel.30"}),"Account changes show only that account's favorites and recent channels");
            h.Provider.State=h.Provider.State with{Account="1"};await h.Refresh();
            check((await h.Keys()).Take(2).SequenceEqual(new[]{"channel.30","channel.40"}),"Returning to an account retains its own saved channel identities");
            before=saved.Revision;await h.Favorite("30",false);await Until(()=>saved.Revision>before);
            check((await h.Keys()).First()=="channel.40","Removing a favorite leaves other favorites intact");
            h.Provider.State=h.Provider.State with{Channels=h.Provider.State.Channels.Where(c=>c.Id!="40").ToArray()};await h.Refresh();
            check(!(await h.Keys()).Contains("channel.40"),"Saved but unavailable channels cannot be displayed or joined");
        }
        await using(var h=await Harness.Create(saved))
            check((await h.Keys()).First()=="channel.40","Saved favorites survive a new widget lifetime");

        await using(var h=await Harness.Create(new WidgetTestPrivateState()))
        {
            h.Provider.State=h.Provider.State with{ChannelId="20",VoiceConnection="connecting"};await h.Refresh();
            check(h.Storage.Revision==0,"A pending connection does not create a recent channel");
            h.Provider.State=h.Provider.State with{VoiceConnection="connected"};await h.Refresh();
            await Until(()=>h.Storage.Revision>0);
            check(Read(h.Storage).Accounts!.Single().Recent.SequenceEqual(new[]{"20"}),"Only an observed connected channel becomes recent");
            var before=h.Storage.Revision;await h.Refresh();await h.Widget.WaitForPreferenceWriteAsync();
            check(h.Storage.Revision==before,"Unchanged connected status does not write preferences on every poll");
        }

        foreach(var json in new[]{"null","{\"SchemaVersion\":99}","{\"SchemaVersion\":1,\"Accounts\":\"broken\"}"})
        {
            var storage=new WidgetTestPrivateState(json,1);await using var h=await Harness.Create(storage);
            check((await h.Keys()).Length==4&&storage.Revision==1,"Malformed or newer preference data preserves the ordinary picker without overwriting saved state");
        }
        var normalized=DiscordWebViewWidget.NormalizeChannelPreferences(new(1,[new("1",["10","10","bad",null!],["20","20",""]),null!,new("bad",[],[])]));
        check(normalized.Accounts is{Length:1}&&normalized.Accounts[0].Favorites.SequenceEqual(new[]{"10"})&&normalized.Accounts[0].Recent.SequenceEqual(new[]{"20"}),
            "Preference normalization bounds identities and ignores invalid or duplicate entries");

        await using(var h=await Harness.Create(new WidgetTestPrivateState()))
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(h.Widget,"favorite-stale");
            using var rows=await host.AcquireAsync("discord.channels",0,4);
            var selected=rows.Range.Items.Single(row=>row.Key=="channel.30");
            h.Provider.State=h.Provider.State with{Account="2"};await h.Refresh();host.PublishSnapshot();
            rows.RouteAction(selected.Key,ControllerButton.Menu,contextActionOwnerId:selected.Root.Id,contextActionId:"favorite");
            await Task.Delay(30);await h.Widget.WaitForPreferenceWriteAsync();
            check(h.Storage.Revision==0,"A delayed favorite menu action cannot cross an account boundary");
        }
        await using(var h=await Harness.Create(new WidgetTestPrivateState()))
        {
            // Simulate another writer after the widget's read: revision-CAS must
            // preserve that write and expose an explicit retry, not overwrite it.
            h.Storage.SimulateExternalWriteJson("""{"SchemaVersion":1,"Accounts":[{"Account":"1","Favorites":["20"],"Recent":[]}]}""");
            await h.Favorite("30",true);await Until(()=>h.Notice.Contains("Could not save"));
            await h.Widget.WaitForChannelPreferencesAsync();
            check(Read(h.Storage).Accounts!.Single().Favorites.SequenceEqual(new[]{"20"}),"A revision conflict preserves an external preference update without blind retry");
        }
    }

    private static DiscordWebViewWidget.ChannelPreferenceStore Read(WidgetTestPrivateState value)=>JsonSerializer.Deserialize<DiscordWebViewWidget.ChannelPreferenceStore>(value.Json!)!;
    private sealed class Harness:IAsyncDisposable
    {
        internal readonly FakeSession Provider=new();
        internal readonly WidgetTestPrivateState Storage;
        internal DiscordWebViewWidget Widget=null!;
        private Harness(WidgetTestPrivateState storage){Storage=storage;}
        internal static async Task<Harness> Create(WidgetTestPrivateState storage)
        {
            var h=new Harness(storage);h.Provider.State=h.Provider.State with{ChannelId=null,Participants=[],Streams=[],Channels=[
                new("10","1","Lobby first","Guild"),new("20","1","Lobby second","Guild"),new("30","1","Side lobby","Guild"),new("40","1","Lobby fourth","Guild")]};
            h.Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(h.Provider,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().WithPrivateState(storage).Build());
            await WidgetTestHost.InitializeAsync(h.Widget);await WidgetTestHost.SetLifecycleStateAsync(h.Widget,WidgetLifecycleState.Interactive);
            await h.Widget.WaitForStateReadAsync();await h.Refresh();await h.Widget.WaitForChannelPreferencesAsync();return h;
        }
        internal string Notice=>Nodes(Widget.Render().CreateSnapshot("favorite-view",1).Root).FirstOrDefault(n=>n.Id=="discord.notice")?.Text??"";
        internal ValueTask Action(string action,string? text=null)=>Widget.OnActionAsync(new(action,"discord.channels.search"){CommittedText=text});
        internal async Task Refresh(){await Action("refresh");await Widget.WaitForStateReadAsync();await Widget.WaitForChannelPreferencesAsync();}
        internal async Task<string[]> Keys()
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(Widget,Guid.NewGuid().ToString("N"));
            var count=Nodes(Widget.Render().CreateSnapshot("favorite-count",1).Root).Single(n=>n.Id=="discord.channels").IndexedCollection!.Count;
            using var rows=await host.AcquireAsync("discord.channels",0,count);return rows.Range.Items.Select(row=>row.Key).ToArray();
        }
        internal async Task Favorite(string channel,bool add)
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(Widget,Guid.NewGuid().ToString("N"));
            var count=Nodes(Widget.Render().CreateSnapshot("favorite-count",1).Root).Single(n=>n.Id=="discord.channels").IndexedCollection!.Count;
            using var rows=await host.AcquireAsync("discord.channels",0,count);var row=rows.Range.Items.Single(r=>r.Key=="channel."+channel);
            rows.RouteAction(row.Key,ControllerButton.Menu,contextActionOwnerId:row.Root.Id,contextActionId:add?"favorite":"unfavorite");
            await Task.Delay(30);await Widget.WaitForPreferenceWriteAsync();
        }
        public ValueTask DisposeAsync()=>WidgetTestHost.DestroyAsync(Widget);
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Until(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!condition())await Task.Delay(10,timeout.Token);}
}
