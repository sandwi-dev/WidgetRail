using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ConnectionStateTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var session=new FakeSession();
        session.State=session.State with{Ready=false,Account="",ConnectionState="initializing",ChannelId=null,Participants=[],Streams=[]};
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(session,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            foreach(var (state,title) in new[]{("initializing","Connecting to Discord…"),("signed-out","Sign in to Discord"),
                ("reconnecting","Reconnecting to Discord…"),("unavailable","Discord is unavailable")})
            {
                session.State=session.State with{ConnectionState=state};
                await widget.OnActionAsync(new("refresh","discord.refresh"));
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while(!Nodes(Snapshot().Root).Any(node=>node.Text==title))await Task.Delay(10,timeout.Token);
                check(!Nodes(Snapshot().Root).Any(node=>node.Text=="Sign in to Discord") || state=="signed-out",
                    "Startup presentation only asks for sign-in with explicit provider evidence: "+state);
            }
            session.State=session.State with{Ready=true,ConnectionState="signed-out"};
            try {session.State.Validate();throw new Exception("Contradictory state was accepted");}
            catch(InvalidDataException){check(true,"Contradictory signed-out and ready state is rejected");}
            session.State=session.State with{Ready=false,ConnectionState=null};session.State.Validate();
            check(session.State.EffectiveConnectionState=="initializing","Legacy non-ready snapshots default to loading, never signed out");
            check(session.Commands.IsEmpty&&session.MicrophoneRequests==0,"Connection presentation does not invoke account or call mutations");
        }
        finally {await WidgetTestHost.DestroyAsync(widget);}
        ViewSnapshot Snapshot()=>widget.Render().CreateSnapshot("connection.test",1);
        static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
    }
}
