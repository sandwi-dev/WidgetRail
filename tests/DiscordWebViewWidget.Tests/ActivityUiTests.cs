using System.Collections.Concurrent;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ActivityUiTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var fake=new FakeSession();var pins=new ConcurrentQueue<string>();
        var avatar="https://cdn.discordapp.com/avatars/31/"+new string('a',32)+".png?size=64";
        fake.State=fake.State with{ActivityAvailability="ready",Activities=[
            new("32","Alpha","offline","playing","A game","",null,null,null,null,false,null,false),
            new("31","Zulu","online","playing","Hades II","On a run","20","1","Co-op","Test guild",true,null,false,AvatarUrl:avatar),
            new("30","Watch friend","online","voice","In voice chat","","10","1","Lobby","Test guild",false,"guild:1:10:30",true)]};
        fake.State.Validate();
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetPinningCapabilities.Open,(request,token)=>{pins.Enqueue(request.LayoutId);return ValueTask.FromResult(new WidgetPinReceipt(true));}).Build());
        await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        try
        {
            await Wait(()=>Nodes(widget.Render().CreateSnapshot("activity",1).Root).Any(node=>node.Id=="discord.activities"));
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"activity");
            using var rows=await host.AcquireAsync("discord.activities",0,3);
            check(rows.Range.Items.Select(item=>item.Key).SequenceEqual(new[]{"friend.30","friend.31","friend.32"}),
                "Home defaults to stable Watch then Join then read-only activity ordering");
            var watch=rows.Range.Items[0].Root;var join=rows.Range.Items[1].Root;var read=rows.Range.Items[2].Root;
            check(!watch.AccessibilityLabel!.Contains("In voice In voice")&&watch.AccessibilityLabel.Contains("Lobby")&&
                join.AccessibilityLabel!.Contains("Co-op")&&join.AccessibilityLabel.Contains("Test guild"),
                "Activity actions identify their voice destination without repeated voice text");
            check(read.ActionId is null&&read.AccessibilityLabel!.Contains("Offline")&&!read.AccessibilityLabel.Contains("Online"),
                "Offline activity stays read-only and is not labeled online");
            check(Nodes(join).Single(node=>node.Kind==ViewNodeKind.Image).ImageSource==avatar&&
                Nodes(read).Any(node=>node.PackageIcon?.AssetId=="discord.person"),
                "Avatars use host HTTPS images with semantic person fallback when absent");

            fake.CommandGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await widget.OnActionAsync(new(DeclaredActions.Resolve(widget.Render().CreateSnapshot("activity",1),"mute"),"discord.main.mute"));await Wait(()=>fake.Commands.Any(c=>c.Method=="mute"));
            rows.RouteAction("friend.30",ControllerButton.A);await Task.Delay(60);
            check(pins.IsEmpty,"Activity Watch preserves pending-command admission and cannot change pins while Leave or another command is pending");
            fake.CommandGate.SetResult();await Task.Delay(60);fake.CommandGate=null;
            rows.RouteAction("friend.31",ControllerButton.A);
            await Wait(()=>fake.Commands.Any(c=>c.Method=="joinActivity"));
            check(fake.MicrophoneRequests==1&&fake.Commands.Single(c=>c.Method=="joinActivity").Args.GetProperty("userId").GetString()=="31"&&
                !fake.Commands.Any(c=>c.Method=="join"),"Activity Join obtains microphone consent and uses provider friend/channel revalidation");
            await Task.Delay(60);
            rows.RouteAction("friend.30",ControllerButton.A);await Wait(()=>pins.Count==1);
            var pinned=WidgetTestHost.CreatePinnedLayoutHost(widget,"activity-pin",30);await pinned.SelectAsync("discord.stream");
            await Wait(()=>fake.Commands.Any(c=>c.Method=="watchActivity"));
            check(fake.Commands.Single(c=>c.Method=="watchActivity").Args.GetProperty("userId").GetString()=="30"&&
                !fake.Commands.Any(c=>c.Method=="watch"),"Activity Watch validates friendship and stream identity when the actual pin receiver starts");
            await Wait(()=>widget.Render().PinnedLayouts!.Any(layout=>layout.Id=="discord.stream"&&layout.Root is { } root&&
                Nodes(root).Any(node=>node.WebBrowser is not null)));
            await widget.OnActionAsync(new(DeclaredActions.Resolve(widget.Render().CreateSnapshot("activity",1),"unwatch"),"discord.streams.stop"));
            await pinned.ReplaceSnapshotAsync();
            check(pinned.SelectedLayoutId is null,"Stop watching revokes its pin before a later activity Watch");
            fake.FailReceivers=true;
            // Pin selection published a newer snapshot. Use a newly admitted
            // collection lease, just as the native host does after publication.
            using var retryHost=WidgetTestHost.CreateIndexedCollectionHost(widget,"activity-pin",100);
            using var retryRows=await retryHost.AcquireAsync("discord.activities",0,3);
            if(retryRows.RouteAction("friend.30",ControllerButton.A)!=WidgetOperationAdmission.Enqueued)
                throw new InvalidDataException("The current activity row did not admit retry.");
            await Wait(()=>pins.Count==2);
            await pinned.ReplaceSnapshotAsync();
            await pinned.SelectAsync("discord.stream");
            await Wait(()=>widget.Render().CreateSnapshot("activity",100).PinnedLayouts.Any(layout=>layout.Root is { } root&&Nodes(root).Any(node=>node.Text=="Screen unavailable")));
            check(true,"A failed incoming receiver shows retry guidance in its pin rather than Connecting forever");
        }
        finally{fake.CommandGate?.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
    private static async Task Wait(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));while(!condition())await Task.Delay(10,timeout.Token);}
}
