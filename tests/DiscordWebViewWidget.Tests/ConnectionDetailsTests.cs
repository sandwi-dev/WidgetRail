using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ConnectionDetailsTests
{
    private const string Dialog="discord.connection-dialog";
    internal static async Task RunAsync(Action<bool,string> check)
    {
        MeasurementPolicy(check);
        await DetailsLifecycle(check);
        await DelayedCallRetirement(check);
        await ReceiverRetirement(check);
    }

    private static void MeasurementPolicy(Action<bool,string> check)
    {
        double?[] history=[];
        for(var index=0;index<60;index++)history=DiscordConnectionMeasurementPolicy.AppendPing(history,index);
        check(history.Length==24&&history[0]==36&&history[^1]==59,"Connection history retains exactly the newest 24 samples");
        var pin=new DiscordPinMeasurement("connected",1920,1080,false,100,0);
        var incoming=new DiscordIncomingMeasurement(true,1920,1080,0,100,100,1000);
        check(DiscordConnectionMeasurementPolicy.Playback(pin,incoming,pin,incoming,2).Contains("idle or waiting"),
            "Unchanged live streams are idle/waiting rather than false receiver failures");
        check(DiscordConnectionMeasurementPolicy.Playback(pin,incoming with{FramesDecoded=160},pin,incoming,2).Contains("pin has not advanced"),
            "Advancing Discord decoding with a stationary pin is distinguished from idle content");
        check(DiscordConnectionMeasurementPolicy.Playback(pin with{TotalFrames=220,DroppedFrames=2},incoming,pin,incoming,2).Contains("59 FPS"),
            "Pin playback rate uses interval frame counters excluding dropped frames");
        check(DiscordConnectionMeasurementPolicy.Playback(pin with{Paused=true},incoming,pin,incoming,2)=="Pin playback paused",
            "A paused video element is reported explicitly");
        check(DiscordConnectionMeasurementPolicy.Playback(pin with{State="disconnected"},incoming,pin,incoming,2).Contains("reconnecting"),
            "Transient receiver disconnect is not declared terminal");
        check(DiscordConnectionMeasurementPolicy.Playback(pin with{TotalFrames=2},incoming,pin,incoming,2)=="Playback rate unavailable",
            "Counter reset does not produce a negative playback rate or false stall");
        check(DiscordConnectionMeasurementPolicy.Playback(null,incoming,pin,incoming,2).Contains("unavailable"),
            "Missing receiver measurement is not represented as zero FPS");
        foreach(var invalid in new object[]{new{phase="connected",pingMilliseconds=-1},new{phase="unknown"},new{phase="connected",pingMilliseconds="private"}})
        {
            try{DiscordCallMeasurement.Read(JsonSerializer.SerializeToElement(invalid));check(false,"Invalid connection measurement rejected");}
            catch(InvalidDataException){check(true,"Invalid connection measurement rejected");}
        }
    }

    private static async Task DetailsLifecycle(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        check(test.Session.Reads==0,"Call rendering does not read diagnostics before details are opened");
        var homeSection=test.Node("discord.section.content").Transition;
        await test.Open();
        check(homeSection is not null && test.Node("discord.section.content").Transition==homeSection,
            "Opening connection details preserves the underlying call section transition");
        check(test.Node(Dialog+".ping").Text=="Ping: 42 ms","Connection details presents current measured voice ping");
        check(test.Snapshot().Root.Kind==ViewNodeKind.ModalLayer&&test.Snapshot().InitialFocusId==Dialog+".summary"&&
            WidgetTestHost.ValidateWinUiPresentation(test.Snapshot()).Count==0,"Connection details uses a valid scoped native modal and reading focus");
        check(test.Snapshot().PinnedLayouts.All(pin=>!Nodes(pin.Root!).Any(node=>node.Id==Dialog+".summary")),
            "Connection measurements and controls never cover a pinned video");
        for(var index=0;index<28;index++)await test.RefreshDetails();
        check(test.Node(Dialog+".history").Graph!.Points.Count==24,"Repeated details reads stay within the graph history bound");
        test.Session.Measurement=JsonSerializer.SerializeToElement(new{phase="connected",pingMilliseconds=(double?)null,averagePingMilliseconds=(double?)null,quality=(string?)null});
        await test.RefreshDetails();check(test.Node(Dialog+".ping").Text=="Ping unavailable","Missing provider ping is shown as unavailable");
        await test.Close();var count=test.Session.Reads;await Task.Delay(2200);
        check(test.Session.Reads==count,"Closing details stops periodic measurement requests");
        test.Session.Measurement=JsonSerializer.SerializeToElement(new{phase="connected",pingMilliseconds=44,averagePingMilliseconds=45,quality="fine"});
        await test.Open();await WidgetTestHost.SetLifecycleStateAsync(test.Widget,WidgetLifecycleState.Background);
        count=test.Session.Reads;await Task.Delay(2200);check(test.Session.Reads==count,"Hidden overlay lifecycle stops details measurement work");
        await WidgetTestHost.SetLifecycleStateAsync(test.Widget,WidgetLifecycleState.Interactive);
        await Until(()=>test.Session.Reads>count);await test.Widget.WaitForConnectionDetailsAsync();
        check(test.Node(Dialog+".history").Graph!.Points.Count<=1,"Reopening the overlay starts a fresh measurement interval history");
        check(test.Session.Core.MicrophoneRequests==0&&!test.Session.Core.Commands.Any(command=>command.Method is "join" or "share" or "watch"),
            "Reading connection details performs no microphone join share or watch action");
    }

    private static async Task DelayedCallRetirement(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        test.Session.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        await test.ActControl("discord.main.connected");await Until(()=>test.Session.Reads==1);
        test.Session.Core.State=test.Session.Core.State with{Account="2"};await test.RefreshState();
        check(test.Snapshot().Root.Kind!=ViewNodeKind.ModalLayer,"An account change retires its connection details visit");
        test.Session.Gate.TrySetResult();await test.Widget.WaitForConnectionDetailsAsync();
        check(!Nodes(test.Snapshot().Root).Any(node=>node.Id==Dialog+".ping"),"Late old-account diagnostics cannot republish into the current view");
        test.Session.Gate=null;await test.Open();
        var staleClose=test.Snapshot().Root.ModalDismissal!.ActionId;
        await test.Close();await test.Open();
        await test.Act(staleClose);
        check(test.Snapshot().Root.Kind==ViewNodeKind.ModalLayer,"A stale details close action cannot close a later visit");
    }

    private static async Task ReceiverRetirement(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        await test.ActControl("discord.stream.30");await test.Pin.ReplaceSnapshotAsync();await test.Pin.SelectAsync("discord.stream");
        await Until(()=>test.Session.Receivers.Count==1);var receiver=test.Session.Receivers.Single();
        await Until(()=>receiver.Lease is not null);await test.Open();
        check(test.Node(Dialog+".incoming").Text!.Contains("1920 × 1080"),"Incoming details reads the selected stream's decoded dimensions");
        check(test.Node(Dialog+".pin").Text!.Contains("1920 × 1080"),"Pin dimensions are measured independently from Discord receiving");
        receiver.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads=receiver.Reads;await test.ActControl(Dialog+".refresh");await Until(()=>receiver.Reads>reads);
        await test.Pin.SelectAsync("discord.call");await Until(()=>receiver.Disposed);
        receiver.Gate.TrySetResult();await test.Widget.WaitForConnectionDetailsAsync();
        check(!Nodes(test.Snapshot().Root).Any(node=>node.Id==Dialog+".pin"),"A late retired receiver cannot publish pin measurements");
        await test.RefreshDetails();
        check(test.Node(Dialog+".playback").Text=="No active video pin","Removing the stream pin reports its actual absence");
        var watches=test.Session.Core.Commands.Count(command=>command.Method=="watch");
        await test.ActControl(Dialog+".retry");await test.Pin.ReplaceSnapshotAsync();await test.Pin.SelectAsync("discord.stream");
        await Until(()=>test.Session.Receivers.Count==2);
        check(test.Session.Core.Commands.Count(command=>command.Method=="watch")==watches+1,"Explicit Reconnect pin performs one existing Watch flow");
    }

    private sealed class Harness:IAsyncDisposable
    {
        internal Session Session=new();
        internal DiscordWebViewWidget Widget=null!;
        internal WidgetPinnedLayoutTestHost Pin=null!;
        internal static async Task<Harness> Create()
        {
            var test=new Harness();
            test.Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(test.Session,refreshInterval:TimeSpan.FromHours(1)),
                new WidgetTestHostServicesBuilder().WithHandler(WidgetPinningCapabilities.Open,(_,_)=>ValueTask.FromResult(new WidgetPinReceipt(true))).Build());
            await WidgetTestHost.InitializeAsync(test.Widget);await WidgetTestHost.SetLifecycleStateAsync(test.Widget,WidgetLifecycleState.Interactive);
            test.Pin=WidgetTestHost.CreatePinnedLayoutHost(test.Widget,"connection-details");
            await Until(()=>Nodes(test.Snapshot().Root).Any(node=>node.Id=="discord.main.connected"));return test;
        }
        internal ViewSnapshot Snapshot()=>Widget.Render().CreateSnapshot("connection.details",1);
        internal ViewNode Node(string id)=>Nodes(Snapshot().Root).Single(node=>node.Id==id);
        internal async Task ActControl(string id)=>await Act(Node(id).ActionId!);
        internal async Task Act(string action)
        {
            var snapshot=Snapshot();var node=Nodes(snapshot.Root).FirstOrDefault(node=>node.ActionId==action);
            await Widget.OnActionAsync(new(action,node?.Id??"test",InputScopeId:snapshot.ActiveInputScopeId){FocusedElementId=node?.Id});
        }
        internal async Task Open(){await ActControl("discord.main.connected");await Until(()=>Nodes(Snapshot().Root).Any(node=>node.Id==Dialog+".ping"));}
        internal async Task Close(){await Act(Snapshot().Root.ModalDismissal!.ActionId);}
        internal async Task RefreshDetails(){await ActControl(Dialog+".refresh");await Widget.WaitForConnectionDetailsAsync().WaitAsync(TimeSpan.FromSeconds(5));}
        internal async Task RefreshState(){await Act("refresh");await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));}
        public async ValueTask DisposeAsync()
        {
            Session.Gate?.TrySetResult();foreach(var receiver in Session.Receivers)receiver.Gate?.TrySetResult();
            await Pin.RevokeAsync();await WidgetTestHost.DestroyAsync(Widget);
        }
    }

    private sealed class Session:IDiscordSession
    {
        internal FakeSession Core=new();
        internal ConcurrentQueue<Receiver> Receivers=new();
        internal int Reads;
        internal TaskCompletionSource? Gate;
        internal JsonElement Measurement=JsonSerializer.SerializeToElement(new{phase="connected",pingMilliseconds=42,averagePingMilliseconds=45,quality="fine"});
        public WebSessionReference? Reference=>Core.Reference;
        public ValueTask<DiscordState> ReadAsync(CancellationToken token)=>Core.ReadAsync(token);
        public ValueTask<WebSessionPermissionResult> RequestNotificationsAsync(CancellationToken token)=>Core.RequestNotificationsAsync(token);
        public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)=>Core.RequestMicrophoneAsync(token);
        public async ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
        {
            if(method=="connectionDiagnostics")
            {Interlocked.Increment(ref Reads);var result=Measurement;if(Gate is {} gate)await gate.Task;return result;}
            if(method=="videoPlayback")return await Receivers.Last().ReadDiagnosticsAsync(JsonSerializer.SerializeToElement(args).GetProperty("lease").GetString()!,token);
            if(method=="incomingDiagnostics")return JsonSerializer.SerializeToElement(new{available=true,width=1920,height=1080,framesPerSecond=30,framesReceived=100,framesDecoded=100,bytesReceived=1000});
            return await Core.InvokeAsync(method,args,token);
        }
        public ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)
        {var receiver=new Receiver();Receivers.Enqueue(receiver);return ValueTask.FromResult<IDiscordReceiver>(receiver);}
        public ValueTask DisposeAsync()=>Core.DisposeAsync();
    }
    private sealed class Receiver:IDiscordReceiver
    {
        internal string? Lease;internal bool Disposed;internal int Reads;internal TaskCompletionSource? Gate;
        private readonly TaskCompletionSource<DiscordReceiverFailure> failure=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DiscordReceiverFailure> Failure=>failure.Task;
        public WebSessionReference Reference{get;}=new(Guid.NewGuid().ToString("N"));
        public void Bind(string lease)=>Lease=lease;
        public async ValueTask<JsonElement> ReadDiagnosticsAsync(string lease,CancellationToken token)
        {Interlocked.Increment(ref Reads);if(Gate is {} gate)await gate.Task;return JsonSerializer.SerializeToElement(new{available=true,lease,state="connected",width=1920,height=1080,paused=false,totalFrames=100,droppedFrames=0});}
        public ValueTask DisposeAsync(){Disposed=true;failure.TrySetCanceled();return ValueTask.CompletedTask;}
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Until(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,timeout.Token);}
}
