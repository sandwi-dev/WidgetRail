using System.Collections.Concurrent;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class DomainIntentTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        foreach(var (name,change) in new (string,Action<FakeSession>)[]
        {
            ("account",fake=>fake.State=fake.State with{Account="2"}),
            ("call",fake=>fake.State=fake.State with{ChannelId="20"}),
            ("session",fake=>fake.Reference=new(new string('b',32)))
        })
        {
            var fake=new FakeSession();var widget=Attach(fake);
            await Ready(widget);
            try
            {
                var snapshot=Snapshot(widget);
                var actions=new[]{"mute","deafen","leave","pin-call"}.Select(method=>DeclaredActions.Resolve(snapshot,method)).ToArray();
                var pinMute=Nodes(snapshot.PinnedLayouts.Single(pin=>pin.Id=="discord.call").Root!).Single(node=>node.Id=="discord.pin.mute").ActionId;
                check(pinMute==actions[0],"Main and pinned call controls bind the same immutable intent");
                change(fake);await Refresh(widget);
                foreach(var action in actions)await widget.OnActionAsync(new(action,"test.stale"));
                check(fake.Commands.IsEmpty,$"Delayed call actions cannot retarget after {name} changes");
            }
            finally{await WidgetTestHost.DestroyAsync(widget);}
        }
        foreach(var method in new[]{"mute","deafen"})
        {
            var fake=new FakeSession();var widget=Attach(fake);await Ready(widget);
            try
            {
                var old=DeclaredActions.Resolve(Snapshot(widget),method);
                fake.State=method=="mute"?fake.State with{Muted=!fake.State.Muted}:fake.State with{Deafened=!fake.State.Deafened};
                await Refresh(widget);await widget.OnActionAsync(new(old,"test.stale"));
                check(fake.Commands.IsEmpty,$"External {method} changes cannot invert an earlier button intent");
                await widget.OnActionAsync(new(DeclaredActions.Resolve(Snapshot(widget),method),"test.current"));
                await Wait(()=>fake.Commands.Count==1);
                check(fake.Commands.Single().Args.GetProperty("value").GetBoolean()==(method=="mute"?!fake.State.Muted:!fake.State.Deafened),
                    $"Fresh {method} action sends the desired state declared on the control");
            }
            finally{await WidgetTestHost.DestroyAsync(widget);}
        }
        {
            var fake=new FakeSession();var pins=new ConcurrentQueue<string>();
            fake.State=fake.State with{Streams=[new("guild:1:10:30","30","First"),new("guild:1:10:31","31","Second")]};
            var widget=Attach(fake,pins);await Ready(widget);
            try
            {
                var watch=DeclaredActions.Resolve(Snapshot(widget),"watch.30");
                await widget.OnActionAsync(new(watch,"discord.stream.30"));
                var stop=DeclaredActions.Resolve(Snapshot(widget),"unwatch");
                await widget.OnActionAsync(new(DeclaredActions.Resolve(Snapshot(widget),"watch.31"),"discord.stream.31"));
                var secondStop=DeclaredActions.Resolve(Snapshot(widget),"unwatch");
                await widget.OnActionAsync(new(stop,"discord.streams.stop"));
                check(DeclaredActions.Resolve(Snapshot(widget),"unwatch")==secondStop,"A delayed Stop watching cannot clear a different selected stream");
                fake.State=fake.State with{Account="2"};await Refresh(widget);
                var requests=pins.Count;await widget.OnActionAsync(new(watch,"discord.stream.30"));
                check(pins.Count==requests,"A delayed Watch cannot start a receiver in a different account");
            }
            finally{await WidgetTestHost.DestroyAsync(widget);}
        }
        {
            var fake=new FakeSession{ReceiverGate=new(TaskCreationOptions.RunContinuationsAsynchronously)};
            var widget=Attach(fake);await Ready(widget);
            try
            {
                await widget.OnActionAsync(new(DeclaredActions.Resolve(Snapshot(widget),"watch.30"),"discord.stream.30"));
                var pin=WidgetTestHost.CreatePinnedLayoutHost(widget,"session.receiver",100);await pin.SelectAsync("discord.stream");
                await Wait(()=>fake.ReceiverRequests==1);
                fake.Reference=new(new string('b',32));fake.ReceiverGate.SetResult();
                await Wait(()=>fake.Receivers.Count==1&&fake.Receivers.Single().Disposed);
                check(!fake.Commands.Any(command=>command.Method is "watch" or "answer"),
                    "Backend replacement during receiver creation cannot watch through the new session");
                await Refresh(widget);
                check(!Nodes(Snapshot(widget).Root).Any(node=>node.Id=="discord.streams.stop"),
                    "Session retirement clears the old stream selection instead of leaving Connecting forever");
            }
            finally{fake.ReceiverGate.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
        }
        {
            var fake=new FakeSession{MicrophoneGate=new(TaskCreationOptions.RunContinuationsAsynchronously)};
            var widget=Attach(fake);await Ready(widget);
            try
            {
                await widget.OnActionAsync(new("server-picker","test"){FocusedElementId="discord.main.mute"});
                using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"session.intent");
                using var rows=await host.AcquireAsync("discord.channels",19,1);
                rows.RouteAction("channel.20",ControllerButton.A);await Wait(()=>fake.MicrophoneRequests==1);
                fake.Reference=new(new string('c',32));fake.MicrophoneGate.SetResult();
                await Wait(()=>!Nodes(Snapshot(widget).Root).Any(node=>node.Id=="discord.main.state"&&node.Text=="Updating…"));
                check(fake.Commands.IsEmpty,"Replacing a backend during microphone consent cannot join using the retired session's grant");
            }
            finally{fake.MicrophoneGate.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
        }
    }
    private static DiscordWebViewWidget Attach(FakeSession fake,ConcurrentQueue<string>? pins=null)=>WidgetTestHost.Attach(
        new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetPinningCapabilities.Open,(request,token)=>{pins?.Enqueue(request.LayoutId);return ValueTask.FromResult(new WidgetPinReceipt(true));}).Build());
    private static async Task Ready(DiscordWebViewWidget widget)
    {await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Wait(()=>Nodes(Snapshot(widget).Root).Any(node=>node.Id=="discord.main.mute"));}
    private static async Task Refresh(DiscordWebViewWidget widget)
    {await widget.OnActionAsync(new("refresh","test"));await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));}
    private static ViewSnapshot Snapshot(DiscordWebViewWidget widget)
    {
        try{return widget.Render().CreateSnapshot("intent",1);}
        catch(ProtocolValidationException error){throw new InvalidDataException(string.Join("; ",error.Errors),error);}
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Wait(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,timeout.Token);}
}
