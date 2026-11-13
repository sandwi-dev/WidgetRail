using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class UnifiedLayoutTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await CallAndPicker(check);
    }

    private static async Task CallAndPicker(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        var call=test.Snapshot();
        var callNodes=Nodes(call.Root).ToArray();
        check(callNodes.Any(n=>n.Id=="discord.main.mute")&&callNodes.Any(n=>n.Id=="discord.share.start")&&
            callNodes.Any(n=>n.Id=="discord.stream.30")&&callNodes.Any(n=>n.Id=="discord.activities"),
            "Unified call places voice, sharing, incoming screens and activity on one page");
        check(!callNodes.Any(n=>n.Id=="discord.navigation.shell")&&
            !callNodes.SelectMany(n=>n.Shortcuts).Any(s=>s.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper),
            "Unified call has no obsolete navigation shell or tab-cycling shortcuts");
        var shortcuts=callNodes.SelectMany(n=>n.Shortcuts).ToArray();
        check(shortcuts.Single(s=>s.Button==ControllerButton.X).ActionId==test.Node("discord.main.mute").ActionId&&
            shortcuts.Single(s=>s.Button==ControllerButton.Y).ActionId==test.Node("discord.share.start").ActionId,
            "Call X and Y use the same immutable domain intents as the visible mute and sharing controls");
        check(!callNodes.Any(n=>n.Kind==ViewNodeKind.Text&&n.Text is "Speaking" or "Listening")&&
            !callNodes.Any(n=>n.Text?.Contains("Pinned video stays unobstructed",StringComparison.Ordinal)==true),
            "Call presentation omits speaking/listening labels and explanatory pin filler");
        check(WidgetTestHost.ValidateWinUiPresentation(call).Count==0,
            "Unified call declarations retain valid WinUI presentation and protocol bounds");
        if(test.Find("discord.participants") is {IndexedCollection: { } participantSource})
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(test.Widget,"unified.participants");
            using var people=await host.AcquireAsync("discord.participants",0,participantSource.Count);
            check(!people.Range.Items.SelectMany(row=>Nodes(row.Root)).Any(n=>n.Kind==ViewNodeKind.Text&&n.Text is "Speaking" or "Listening"),
                "Lazy participant rows also omit visible speaking/listening labels");
        }
        var pin=call.PinnedLayouts.Single(p=>p.Id=="discord.stream");
        var callPin=call.PinnedLayouts.Single(p=>p.Id=="discord.call");

        check(shortcuts.Single(s=>s.Button==ControllerButton.LeftStick).ActionId=="server-picker",
            "L3 opens the same server picker used by the visible call destination control");
        await test.Replay("server-picker","discord.main.mute");
        check(test.Find("discord.channels.server") is not null&&!test.Shortcuts().Any(s=>s.Button is ControllerButton.X or ControllerButton.Y)&&
            test.Snapshot().ActiveInputScopeId!=call.ActiveInputScopeId,
            "The pushed picker owns its own input scope and cannot run call X/Y shortcuts");
        var back=test.Shortcuts().Single(s=>s.Button==ControllerButton.B);
        await test.Replay(back.ActionId,"unified.test.back");
        check(test.Find("discord.main.mute") is not null&&test.Snapshot().InitialFocusId=="discord.main.mute"&&test.Session.Commands.IsEmpty,
            "Back from server selection restores the originating call control without leaving voice");

        test.Session.State=test.Session.State with{Sharing=true};await test.Refresh();
        check(!test.Shortcuts().Any(s=>s.Button==ControllerButton.Y)&&test.Shortcuts().Any(s=>s.Button==ControllerButton.X),
            "An active outgoing stream removes the start-sharing shortcut instead of turning Y into Stop sharing");
        test.Session.State=test.Session.State with{Sharing=false};await test.Refresh();

        await test.Act("discord.account.open");
        check(test.Find("discord.account.browser") is not null&&!test.Shortcuts().Any(s=>s.Button is ControllerButton.X or ControllerButton.Y),
            "Full Discord account route does not retain native call X/Y shortcuts");
        await test.Act("discord.account.back");
        check(test.Snapshot().InitialFocusId=="discord.account.open",
            "Returning from full Discord retains navigator-owned origin focus");

        test.Session.State=test.Session.State with{ChannelId=null,ChannelName="",Participants=[],Streams=[]};
        await test.Refresh();
        var idle=test.Snapshot();
        check(test.Find("discord.channels.server") is not null&&test.Find("discord.channels") is not null&&
            test.Find("discord.main.mute") is null&&test.Find("discord.activities") is not null,
            "Leaving voice automatically replaces the call area with the server/channel picker and preserves activity");
        check(!test.Shortcuts().Any(s=>s.Button is ControllerButton.X or ControllerButton.Y)&&
            test.Session.Commands.IsEmpty&&test.Session.MicrophoneRequests==0,
            "Idle picker has no call shortcuts and rendering never joins, captures, or prompts");
        check(WidgetTestHost.ValidateWinUiPresentation(idle).Count==0,
            "Idle server picker satisfies protocol and native control contracts");
        var idlePin=idle.PinnedLayouts.Single(p=>p.Id=="discord.stream");
        var idleCallPin=idle.PinnedLayouts.Single(p=>p.Id=="discord.call");
        check(pin.Root!.Id==idlePin.Root!.Id&&pin.Root.Kind==idlePin.Root.Kind&&pin.ActiveInputScopeId==idlePin.ActiveInputScopeId&&
            callPin.Root!.Id==idleCallPin.Root!.Id&&callPin.Root.Kind==idleCallPin.Root.Kind,
            "Call/idle layout transitions retain the existing pinned layout root identities");
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));

    private sealed class Harness:IAsyncDisposable
    {
        internal readonly FakeSession Session;
        internal readonly DiscordWebViewWidget Widget;
        private Harness(FakeSession session,DiscordWebViewWidget widget){Session=session;Widget=widget;}
        internal static async Task<Harness> Create(DiscordState? state=null)
        {
            var session=new FakeSession();
            if(state is not null)session.State=state;
            else session.State=session.State with{ActivityAvailability="ready",Activities=[
                new("31","Friend","online","playing","Silksong","",null,null,null,null,false,null,false)]};
            var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(session,refreshInterval:TimeSpan.FromHours(1)),
                new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return new(session,widget);
        }
        internal ViewSnapshot Snapshot()=>Widget.Render().CreateSnapshot("unified.layout",1);
        internal ViewNode? Find(string id)=>Nodes(Snapshot().Root).FirstOrDefault(n=>n.Id==id);
        internal ViewNode Node(string id)=>Find(id)??throw new InvalidDataException("Missing unified layout node: "+id);
        internal IEnumerable<ControllerShortcut> Shortcuts()=>Nodes(Snapshot().Root).SelectMany(n=>n.Shortcuts);
        internal ValueTask Act(string id)=>Replay(Node(id).ActionId??throw new InvalidDataException("Missing action: "+id),id);
        internal ValueTask Replay(string action,string source,string? text=null)=>Widget.OnActionAsync(new(action,source,InputScopeId:Snapshot().ActiveInputScopeId)
            {CommittedText=text,FocusedElementId=source});
        internal async Task Refresh(){await Replay("refresh","unified.test");await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));}
        public ValueTask DisposeAsync()=>WidgetTestHost.DestroyAsync(Widget);
    }
}
