using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class VoicePinTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await BoundedParticipants(check);
        await SpeakingAndCallState(check);
        await StreamIndicators(check);
        await FocusAndConnectionLoss(check);
    }

    private static async Task BoundedParticipants(Action<bool,string> check)
    {
        foreach(var count in new[]{0,1,100})
        {
            var people=Enumerable.Range(1,count).Select(index=>new DiscordParticipant(index.ToString(),"Person "+index,
                false,false,index==1,index%2==0?null:"https://cdn.discordapp.com/avatars/"+index+"/"+new string('a',32)+".png?size=64")).ToArray();
            await using var test=await Harness.Create(people);
            var snapshot=test.Snapshot();var pin=test.Pin();var nodes=Nodes(pin.Root!).ToArray();
            var overlay=snapshot.PinnedLayouts.Single(layout=>layout.Id=="discord.people");
            var overlayRows=overlay.Root!.Children.Where(row=>row.Kind==ViewNodeKind.Row).ToArray();
            check(overlay.Surface.Appearance==WidgetSurfaceAppearance.Transparent&&overlay.Root.StyleClasses.Contains("discord-people-overlay")&&
                overlayRows.Select(row=>Nodes(row).Single(node=>node.Id.StartsWith("discord.people.name.",StringComparison.Ordinal)).Text).SequenceEqual(people.Take(4).Select(person=>person.Name))&&
                !Nodes(overlay.Root).Any(node=>node.IsFocusable),"Transparent overlay pairs names with passive avatars in provider order: "+count);
            check(overlayRows.Where(row=>Nodes(row).Any(node=>node.StyleClasses.Contains("discord-voice-speaking")))
                .Select(row=>Nodes(row).Single(node=>node.Id.StartsWith("discord.people.name.",StringComparison.Ordinal)).Text).SequenceEqual(people.Take(4).Where(person=>person.Speaking).Select(person=>person.Name)),
                "Transparent overlay outlines the matching speaker: "+count);
            check(pin.Root!.Id=="discord.pin.controls"&&pin.Surface.PreferredWidth==400&&pin.Surface.PreferredHeight==340&&
                pin.Surface.MinimumWidth==320&&pin.Surface.MinimumHeight==340,
                "Voice pin retains its stable root and compact preferred/minimum surface: "+count+" participants");
            var avatars=nodes.Where(node=>node.Id.StartsWith("discord.pin.avatar.",StringComparison.Ordinal)).ToArray();
            check(avatars.Length==Math.Min(count,4)&&avatars.All(node=>!node.IsFocusable)&&
                avatars.Count(node=>node.Kind==ViewNodeKind.Image)==people.Take(4).Count(person=>person.AvatarUrl is not null),
                "Voice pin requests at most four passive participant avatars: "+count+" participants");
            check(test.Node("discord.pin.state").Text==count+" in call"&&
                nodes.Where(node=>node.Id.StartsWith("discord.pin.name.",StringComparison.Ordinal)).Select(node=>node.Text)
                    .SequenceEqual(people.Take(4).Select(person=>person.Name))&&
                (nodes.FirstOrDefault(node=>node.Id=="discord.pin.more")?.Text??"")==(count>4?"+"+(count-4):""),
                "Voice pin names and overflow describe the same bounded participant set: "+count+" participants");
            check(nodes.Where(node=>node.IsFocusable).Select(node=>node.Id).SequenceEqual(new[]{"discord.pin.mute","discord.pin.deafen","discord.pin.leave"})&&
                nodes.SelectMany(node=>node.Shortcuts).All(shortcut=>shortcut.Button is not(ControllerButton.X or ControllerButton.Y)),
                "Voice pin has only its three call-control focus stops and does not claim host X/Y: "+count+" participants");
            check(WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0&&test.Session.Commands.IsEmpty&&test.Session.MicrophoneRequests==0,
                "Voice pin rendering validates without joining, changing audio, or prompting: "+count+" participants");
        }
    }

    private static async Task SpeakingAndCallState(Action<bool,string> check)
    {
        await using var test=await Harness.Create([
            new("1","You",false,false,true),new("2","Muted participant",true,false,true),new("3","Deafened participant",false,true,true),
            new("4","Outside visible set",false,false,true)]);
        var before=test.Pin();var ids=Nodes(before.Root!).Select(node=>node.Id).ToArray();
        check(test.Node("discord.pin.person.1").StyleClasses.Contains("discord-voice-speaking")&&
            !test.Node("discord.pin.person.2").StyleClasses.Contains("discord-voice-speaking")&&
            !test.Node("discord.pin.person.3").StyleClasses.Contains("discord-voice-speaking"),
            "Voice pin suppresses speaking rings for muted or deafened participants");
        check(!Nodes(before.Root!).Any(node=>node.Kind==ViewNodeKind.Text&&node.Text is "Speaking" or "Listening"),
            "Voice pin communicates speaking without extra visible status labels");
        test.Session.State=test.Session.State with{Participants=test.Session.State.Participants.Select(person=>person with{Speaking=!person.Speaking}).ToArray(),
            ChannelName="A changed channel title",Muted=false,Deafened=true};
        await test.Refresh();
        var after=test.Pin();
        check(ids.SequenceEqual(Nodes(after.Root!).Select(node=>node.Id))&&before.ActiveInputScopeId==after.ActiveInputScopeId&&
            !Nodes(after.Root!).Any(node=>node.StyleClasses.Contains("discord-voice-speaking")),
            "Speaking updates retain participant order, authored IDs and the pin input scope");
        check(test.Node("discord.pin.channel").Text=="A changed channel title"&&test.Node("discord.pin.mute").Text=="Mute"&&
            test.Node("discord.pin.mute").IsSelected!=true&&test.Node("discord.pin.deafen").Text=="Undeafen"&&test.Node("discord.pin.deafen").IsSelected==true,
            "Voice pin reflects observed channel, mute and deafen state");
        var main=Nodes(test.Snapshot().Root).ToArray();
        check(new[]{"mute","deafen","leave"}.All(control=>test.Node("discord.pin."+control).ActionId==main.Single(node=>node.Id=="discord.main."+control).ActionId),
            "Voice pin uses the exact same domain-bound call actions as the main widget");
    }

    private static async Task StreamIndicators(Action<bool,string> check)
    {
        await using var test=await Harness.Create([new("1","You",false,false,false),new("30","Watching",false,false,false),
            new("31","Streaming",false,false,false),new("32","In voice",false,false,false)]);
        test.Session.State=test.Session.State with{Sharing=true,Streams=[new("guild:1:10:30","30","Watching",Watching:true),
            new("guild:1:10:31","31","Streaming")]};
        await test.Refresh();
        var nodes=Nodes(test.Snapshot().PinnedLayouts.Single(pin=>pin.Id=="discord.people").Root!).ToArray();
        check(new[]{"1","30","31"}.All(id=>nodes.Any(node=>node.Id=="discord.people.live."+id))&&
            !nodes.Any(node=>node.Id=="discord.people.live.32"),"LIVE badges identify both local and remote broadcasters only");
        check(!nodes.Any(node=>node.Id.StartsWith("discord.people.watching.",StringComparison.Ordinal)),
            "Watching another person's stream does not imply that they watch yours");
        test.Session.State=test.Session.State with{OutgoingViewers=new("guild:1:10:1",["31","32"])};await test.Refresh();
        nodes=Nodes(test.Snapshot().PinnedLayouts.Single(pin=>pin.Id=="discord.people").Root!).ToArray();
        check(new[]{"31","32"}.All(id=>nodes.Any(node=>node.Id=="discord.people.watching."+id&&node.PackageIcon?.AssetId=="discord.watching"))&&
            !nodes.Any(node=>node.Id=="discord.people.watching.30"),"Eye marks viewers of your stream independently of their LIVE status");
        var voice=Nodes(test.Pin().Root!).ToArray();
        check(new[]{"31","32"}.All(id=>voice.Any(node=>node.Id=="discord.pin.watching."+id)),
            "Voice controls use the same outgoing viewer status");
        test.Session.State=test.Session.State with{Sharing=false,Streams=[],OutgoingViewers=null};await test.Refresh();
        nodes=Nodes(test.Snapshot().PinnedLayouts.Single(pin=>pin.Id=="discord.people").Root!).ToArray();
        check(!nodes.Any(node=>node.Id.StartsWith("discord.people.live.",StringComparison.Ordinal)||
            node.Id.StartsWith("discord.people.watching.",StringComparison.Ordinal)),"Stream-end updates remove LIVE and watching indicators");
    }

    private static async Task FocusAndConnectionLoss(Action<bool,string> check)
    {
        await using var test=await Harness.Create([new("1",new string('n',120),false,false,false)]);
        check(test.Pin().InitialFocusId=="discord.pin.mute","Voice pin initially focuses the available microphone control");
        test.Session.State=test.Session.State with{Capabilities=test.Session.State.Capabilities with{Mute=false}};await test.Refresh();
        check(test.Pin().InitialFocusId=="discord.pin.deafen"&&test.Node("discord.pin.mute").IsDisabled==true,
            "Voice pin initial focus skips an unavailable microphone control");
        test.Session.State=test.Session.State with{Capabilities=test.Session.State.Capabilities with{Deafen=false}};await test.Refresh();
        check(test.Pin().InitialFocusId=="discord.pin.leave"&&test.Node("discord.pin.deafen").IsDisabled==true,
            "Voice pin focuses Leave when mute and deafen are both unavailable");
        check(test.Pin().RestoreWhenAvailable&&test.Snapshot().PinnedLayouts.Single(p=>p.Id=="discord.people").RestoreWhenAvailable,
            "Both voice layouts opt into user-selected availability memory");
        var connected=test.Session.State;
        foreach(var disconnected in new[]{false,true})
        {
            test.Session.State=test.Session.State with{Ready=!disconnected,ChannelId=null,ChannelName="",Participants=[],Streams=[],VoiceConnection="disconnected"};await test.Refresh();
            var snapshot=test.Snapshot();
            check(!snapshot.PinnedLayouts.Any(p=>p.Id is "discord.call" or "discord.people")&&WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0,
                disconnected?"Lost Discord connection withdraws both voice pins":"Leaving voice withdraws both voice pins instead of retaining empty windows");
        }
        test.Session.State=connected with{ChannelId="another-channel"};await test.Refresh();
        check(test.Snapshot().PinnedLayouts.Count(p=>p.RestoreWhenAvailable)==2,
            "Joining a different voice channel republishes the same two remembered layout identities");
    }

    private sealed class Harness:IAsyncDisposable
    {
        internal readonly FakeSession Session;
        internal readonly DiscordWebViewWidget Widget;
        private Harness(FakeSession session,DiscordWebViewWidget widget){Session=session;Widget=widget;}
        internal static async Task<Harness> Create(DiscordParticipant[] participants)
        {
            var session=new FakeSession();session.State=session.State with{Participants=participants,VoiceConnection="connected"};
            var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(session,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));return new(session,widget);
        }
        internal ViewSnapshot Snapshot()=>Widget.Render().CreateSnapshot("voice.pin",1);
        internal PinnedPresentationLayout Pin()=>Snapshot().PinnedLayouts.Single(layout=>layout.Id=="discord.call");
        internal ViewNode Node(string id)=>Nodes(Pin().Root!).Single(node=>node.Id==id);
        internal async Task Refresh(){await Widget.OnActionAsync(new("refresh","voice.pin.test"));await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));}
        public ValueTask DisposeAsync()=>WidgetTestHost.DestroyAsync(Widget);
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
}
