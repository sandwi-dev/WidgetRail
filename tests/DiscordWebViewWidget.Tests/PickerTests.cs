using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using System.Text.Json;

internal static class PickerTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await RankedResults(check);
        await SelectionAuthority(check);
        await LargeServerCatalog(check);
        await ConfirmedJoinNavigation(check);
        await IndexedPickerReturn(check);
    }

    private static async Task RankedResults(Action<bool,string> check)
    {
        await using var test=await Harness.Create([
            new("10","1","Lobby","Lobby squad"),new("11","2","Side lobby","Lobby squad"),
            new("12","3","Lobby room","ZLobby")]);
        check(test.Node("discord.channels.server").SelectOptions!.Count==4&&test.Fake.Commands.IsEmpty&&test.Fake.MicrophoneRequests==0,
            "Idle picker exposes every server without joining or prompting");
        await test.Act("search","lobby");
        using(var host=WidgetTestHost.CreateIndexedCollectionHost(test.Widget,"picker-ranks"))
        using(var rows=await host.AcquireAsync("discord.channels",0,6))
        {
            var field=test.Node("discord.channels.search");
            var suggestions=await test.Widget.OnSuggestTextAsync(new(field.Id,field.TextEntrySuggestions!.Key,
                Guid.NewGuid().ToString("N"),1,"lobby",6,120),default);
            check(rows.Range.Items.Select(row=>row.Key).SequenceEqual(suggestions.Items.Select(item=>item.Id)),
                "Server and channel search results share the keyboard catalog's rank order");
            check(suggestions.Items.Count(item=>item.Id.StartsWith("server.",StringComparison.Ordinal))==3&&
                suggestions.Items.Count(item=>item.Id.StartsWith("channel.",StringComparison.Ordinal))==3&&
                suggestions.Items.Take(4).All(item=>item.Label.StartsWith("Lobby",StringComparison.OrdinalIgnoreCase)),
                "Merged search includes servers and voice channels with prefix matches before substring matches");
            check(suggestions.Items.Count(item=>item.Label=="Lobby squad")==2,
                "Equal server names retain distinct server identities in suggestions");
            rows.RouteAction("server.2",ControllerButton.A);
            await Until(()=>test.Node("discord.channels.search").TextEntryValue=="");
        }
        using(var host=WidgetTestHost.CreateIndexedCollectionHost(test.Widget,"picker-server-open"))
        using(var rows=await host.AcquireAsync("discord.channels",0,1))
            check(rows.Range.Items.Single().Key=="channel.11"&&test.Fake.Commands.IsEmpty&&test.Fake.MicrophoneRequests==0,
                "Selecting a server result opens only its channels without joining voice");
        await test.Act("search","Lobby");
        check(test.Node("discord.channels").IndexedCollection!.Count==6,
            "Search spans the whole account even after selecting one server");
    }

    private static async Task SelectionAuthority(Action<bool,string> check)
    {
        await using var test=await Harness.Create([new("10","1","One","Same"),new("20","2","Two","Same")]);
        var initial=test.Node("discord.channels.server").SelectOptions!;
        await test.Act(initial.Single(option=>option.Id=="server.1").ActionId);
        check(await test.OnlyKey()=="channel.10","Same-name server selection uses server IDs rather than display text");
        await test.Act(initial.Single(option=>option.Id=="server.2").ActionId);
        check(await test.OnlyKey()=="channel.20","Second same-name server selects its own channel set");

        var staleAccount=initial.Single(option=>option.Id=="server.1").ActionId;
        test.Fake.State=test.Fake.State with{Account="2"};await test.Refresh();
        await test.Act(staleAccount);
        check(test.Node("discord.channels").IndexedCollection!.Count==2,
            "A delayed server selection cannot cross an account boundary");
        var staleSession=test.Node("discord.channels.server").SelectOptions!.Single(option=>option.Id=="server.1").ActionId;
        test.Fake.Reference=new(new string('b',32));
        var reconnecting=test.Widget.Render().CreateSnapshot("picker.session-replacement",1);
        check(reconnecting.InitialFocusId=="discord.refresh"&&WidgetTestHost.ValidateWinUiPresentation(reconnecting).Count==0,
            "A replaced backend keeps a valid native focus target before its new catalog arrives");
        await test.Refresh();await test.Act(staleSession);
        check(test.Node("discord.channels").IndexedCollection!.Count==2,
            "A delayed server selection cannot cross a backend session boundary");
        var staleCatalog=test.Node("discord.channels.server").SelectOptions!.Single(option=>option.Id=="server.1").ActionId;
        test.Fake.State=test.Fake.State with{Channels=[new("10","1","Renamed","Same"),new("20","2","Two","Same")]};
        await test.Refresh();await test.Act(staleCatalog);
        check(test.Node("discord.channels").IndexedCollection!.Count==2,
            "A replaced catalog invalidates a stale native dropdown selection");

        using var host=WidgetTestHost.CreateIndexedCollectionHost(test.Widget,"stale-picker-row");
        using var oldRows=await host.AcquireAsync("discord.channels",0,1);
        test.Fake.State=test.Fake.State with{Channels=[new("10","1","Replacement","Same")]};await test.Refresh();
        oldRows.RouteAction("channel.10",ControllerButton.A);await Task.Delay(40);
        check(test.Fake.Commands.IsEmpty&&test.Fake.MicrophoneRequests==0,
            "An old indexed channel lease cannot join after its catalog is replaced");
    }

    private static async Task LargeServerCatalog(Action<bool,string> check)
    {
        await using var test=await Harness.Create(Enumerable.Range(1,200)
            .Select(index=>new DiscordChannel(index.ToString(),index.ToString(),"Channel "+index,"Server "+index.ToString("D3"))).ToArray());
        var browse=test.Node("discord.channels.server");
        check(browse.Kind==ViewNodeKind.Button,"Large server catalogs use the indexed picker rather than truncate a bounded dropdown");
        await test.Act(browse.ActionId!);
        check(test.Node("discord.channels").IndexedCollection!.Count==200,"All servers remain available beyond the dropdown option limit");
        using(var host=WidgetTestHost.CreateIndexedCollectionHost(test.Widget,"large-server-picker"))
        using(var rows=await host.AcquireAsync("discord.channels",199,1))
        {
            check(rows.Range.Items.Single().Key=="server.200","A deep server is lazily realized with its stable identity");
            rows.RouteAction("server.200",ControllerButton.A);
            await Until(()=>test.Node("discord.channels").IndexedCollection!.Count==1);
        }
        check(await test.OnlyKey()=="channel.200"&&test.Fake.Commands.IsEmpty,
            "Opening a deep server reveals its channel without a provider mutation");
        await test.Act("search","Channel");
        check(test.Node("discord.channels").IndexedCollection!.Count==200,
            "Ranked results remain complete beyond the keyboard completion limit");
    }

    private static async Task ConfirmedJoinNavigation(Action<bool,string> check)
    {
        foreach(var scenario in new[]{"confirmed","unconfirmed","back","reopened","account-page","session-changed","account-changed","consent-cancelled"})
        {
            var session=new ConfirmingJoinSession();
            if(scenario=="unconfirmed")session.ObserveJoin=false;
            if(scenario=="consent-cancelled")session.Inner.Microphone=WebSessionMicrophoneState.Cancelled;
            var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(session,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync();
            try
            {
                await Action("server-picker","discord.main.servers");
                using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"confirmed-join."+scenario);
                using var rows=await host.AcquireAsync("discord.channels",1,1);
                var beforeReads=session.Inner.Reads;
                rows.RouteAction("channel.20",ControllerButton.A);
                await Until(()=>session.Inner.MicrophoneRequests>0);
                if(scenario=="consent-cancelled")
                {
                    await widget.WaitForCallCommandAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    check(session.Inner.Commands.IsEmpty&&Find("discord.picker.page") is not null,
                        "Cancelled microphone consent neither joins nor closes the server picker");
                    continue;
                }
                await Until(()=>session.Inner.Commands.Any(command=>command.Method=="join"));
                if(scenario is "back" or "reopened")
                {
                    await Action(Find("discord.picker.back")!.ActionId!,"discord.picker.back");
                    if(scenario=="reopened")await Action("server-picker","discord.main.servers");
                }
                else if(scenario=="account-page")await Action("account","discord.account.open");
                else if(scenario=="session-changed")session.Inner.Reference=new(new string('b',32));
                else if(scenario=="account-changed")session.Inner.State=session.Inner.State with{Account="2"};
                session.Completion.TrySetResult();
                // Await the actual SDK operation lane, including observation
                // and navigation, rather than sampling a negative assertion.
                await widget.WaitForCallCommandAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(session.Inner.Reads==beforeReads+1,"Join observes provider state once before its navigation decision: "+scenario);
                var expectedPicker=scenario is "unconfirmed" or "reopened" or "session-changed" or "account-changed";
                var snapshot=Snapshot();
                check((Find("discord.picker.page") is not null)==expectedPicker&&
                    (Find("discord.account") is not null)==(scenario=="account-page")&&
                    WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0,
                    scenario switch
                    {
                        "confirmed"=>"Confirmed Join returns from its original picker to the active call",
                        "unconfirmed"=>"A provider response without observed channel confirmation leaves the picker open",
                        "back"=>"A late Join does not navigate after the user backs out of its picker",
                        "reopened"=>"A late Join cannot close a newer visit to the same picker scope",
                        "account-page"=>"A late Join does not displace the Discord account page",
                        "session-changed"=>"A Join from an old backend cannot close the new backend's picker",
                        _=>"A Join from an old account cannot close the current account's picker"
                    });
            }
            finally{session.Completion.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
            ViewSnapshot Snapshot()=>widget.Render().CreateSnapshot("picker.join-navigation",1);
            ViewNode? Find(string id)=>Nodes(Snapshot().Root).FirstOrDefault(node=>node.Id==id);
            ValueTask Action(string action,string source)=>widget.OnActionAsync(new(action,source,InputScopeId:Snapshot().ActiveInputScopeId)
                {FocusedElementId=source});
        }
    }

    private sealed class ConfirmingJoinSession:IDiscordSession
    {
        internal readonly FakeSession Inner=new();
        internal readonly TaskCompletionSource Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ObserveJoin=true;
        internal ConfirmingJoinSession()=>Inner.State=Inner.State with{Channels=[new("10","1","Lobby","Server"),new("20","1","Second","Server")],Streams=[]};
        public WebSessionReference? Reference=>Inner.Reference;
        public ValueTask<DiscordState> ReadAsync(CancellationToken token)=>Inner.ReadAsync(token);
        public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)=>Inner.RequestMicrophoneAsync(token);
        public ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)=>Inner.OpenReceiverAsync(token);
        public async ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
        {
            var result=await Inner.InvokeAsync(method,args,token);
            if(method=="join")
            {
                await Completion.Task.WaitAsync(token);
                if(ObserveJoin)
                {
                    var target=JsonSerializer.SerializeToElement(args).GetProperty("target").GetString();
                    var channel=Inner.State.Channels.Single(item=>item.Id==target);
                    Inner.State=Inner.State with{ChannelId=channel.Id,ChannelName=channel.Name};
                }
            }
            return result;
        }
        public ValueTask DisposeAsync()=>Inner.DisposeAsync();
    }

    private static async Task IndexedPickerReturn(Action<bool,string> check)
    {
        foreach(var scenario in new[]{"participant","participant-removed","activity","activity-compact","activity-removed","call-ended"})
        {
            var fake=new FakeSession();fake.State=fake.State with{ActivityAvailability="ready",Activities=[
                new("31","Friend","online","playing","Game","",null,null,null,null,false,null,false)]};
            var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync();
            try
            {
                var participant=scenario.StartsWith("participant",StringComparison.Ordinal)||scenario=="call-ended";
                var collection=participant?"discord.participants":scenario=="activity-compact"?"discord.activities.compact":"discord.activities";
                var key=participant?"participant.30":"friend.31";var index=participant?1:0;
                using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"picker-return."+scenario);
                using var row=await host.AcquireAsync(collection,index,1);
                check(row.RouteAction(key,ControllerButton.LeftStick)==WidgetOperationAdmission.Enqueued,
                    "L3 is admitted through the real indexed occurrence for "+scenario);
                await Until(()=>Find("discord.picker.page") is not null);
                if(scenario=="participant-removed")fake.State=fake.State with{Participants=[]};
                else if(scenario=="activity-removed")fake.State=fake.State with{Activities=[]};
                else if(scenario=="call-ended")fake.State=fake.State with{ChannelId=null,Participants=[],Streams=[]};
                if(scenario is "participant-removed" or "activity-removed" or "call-ended")
                {await Action("refresh","picker-return.test");await widget.WaitForStateReadAsync();}
                await Action(Find("discord.picker.back")!.ActionId!,"discord.picker.back");
                var result=Snapshot();
                if(scenario is "participant-removed" or "activity-removed" or "call-ended")
                    check(result.FocusGroupEntryRequest is null&&WidgetTestHost.ValidateWinUiPresentation(result).Count==0,
                        "Removed indexed origin falls back without a stale return-focus request: "+scenario);
                else
                    check(result.FocusGroupEntryRequest is {RestoreViewport:true,IndexedItem: { } item}&&
                        item.CollectionId==collection&&item.ItemKey==key&&item.Index==index&&
                        WidgetTestHost.ValidateWinUiPresentation(result).Count==0,
                        "Picker Back restores the captured indexed occurrence and viewport: "+scenario);
            }
            finally{await WidgetTestHost.DestroyAsync(widget);}
            ViewSnapshot Snapshot()=>widget.Render().CreateSnapshot("picker.indexed-return",1);
            ViewNode? Find(string id)=>Nodes(Snapshot().Root).FirstOrDefault(node=>node.Id==id);
            ValueTask Action(string action,string source)=>widget.OnActionAsync(new(action,source,InputScopeId:Snapshot().ActiveInputScopeId)
                {FocusedElementId=source});
        }
    }

    private sealed class Harness: IAsyncDisposable
    {
        internal readonly FakeSession Fake;
        internal readonly DiscordWebViewWidget Widget;
        private Harness(FakeSession fake,DiscordWebViewWidget widget){Fake=fake;Widget=widget;}
        internal static async Task<Harness> Create(DiscordChannel[] channels)
        {
            var fake=new FakeSession();fake.State=fake.State with{ChannelId=null,Participants=[],Streams=[],Channels=channels};
            var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync();return new(fake,widget);
        }
        internal ViewNode Node(string id)=>Nodes(Widget.Render().CreateSnapshot("picker",1).Root).Single(node=>node.Id==id);
        internal ValueTask Act(string action,string? text=null)=>Widget.OnActionAsync(new(action,"discord.channels.server"){CommittedText=text});
        internal async Task Refresh(){await Act("refresh");await Widget.WaitForStateReadAsync();}
        internal async Task<string> OnlyKey()
        {using var host=WidgetTestHost.CreateIndexedCollectionHost(Widget,Guid.NewGuid().ToString("N"));using var rows=await host.AcquireAsync("discord.channels",0,1);return rows.Range.Items.Single().Key;}
        public ValueTask DisposeAsync()=>WidgetTestHost.DestroyAsync(Widget);
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Until(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,timeout.Token);}
}
