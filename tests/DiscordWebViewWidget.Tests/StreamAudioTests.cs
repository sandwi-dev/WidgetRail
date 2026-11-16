using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class StreamAudioTests
{
    private const string Main="discord.streams.audio", Pin="discord.stream.pin.audio";
    internal static async Task RunAsync(Action<bool,string> check)
    {
        Model(check);
        await PresentationAndWrites(check);
        await PinContinuity(check);
        await BrowserRange(check);
        await ProviderSnap(check);
        await StaleOwners(check);
        await MutedIntent(check);
        await QueuedVolume(check);
        await DelayedRead(check);
        await LatestUnrelatedFields(check);
    }

    private static void Model(Action<bool,string> check)
    {
        var original=new FakeSession().State;
        original.Validate();
        var state=original with{Streams=[original.Streams[0] with{Audio=new(true,true,37.5)}]};
        state.Validate();
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var restored=JsonSerializer.Deserialize<DiscordState>(JsonSerializer.Serialize(state,options),options)!;
        restored.Validate();
        check(restored.Streams[0].Audio==new DiscordStreamAudio(true,true,37.5),
            "Stream audio state preserves observed mute and fractional volume through JSON roundtrip");
        foreach(var audio in new DiscordStreamAudio?[]{null,new(false,false,null),new(true,true,100)})
            (original with{Streams=[original.Streams[0] with{Audio=audio}]}).Validate();
        check(true,"Legacy absent audio, unavailable null volume and displayed browser boundary remain valid provider states");
        foreach(var value in new[]{-.01,100.01,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            try{(original with{Streams=[original.Streams[0] with{Audio=new(true,false,value)}]}).Validate();throw new Exception("Invalid stream volume accepted: "+value);}
            catch(InvalidDataException){ }
        }
        check(true,"Provider volume contract rejects out-of-range and non-finite numbers instead of silently clamping");
    }

    private static async Task PresentationAndWrites(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        check(!test.AllNodes().Any(n=>n.Id is Main+".mute" or Main+".volume" or Pin+".mute" or Pin+".volume"),
            "Stream audio controls are absent before a stream is watched");
        test.Session.Core.ReceiverGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        await test.Watch(wait:false);
        await Until(()=>test.Session.Core.ReceiverRequests==1);
        check(test.Find(Main+".mute") is null&&test.Find(Pin+".volume") is null,
            "Pin selection alone does not expose audio controls before its receiver is active");
        test.Session.Core.ReceiverGate.SetResult();
        await Until(()=>test.Find(Main+".volume") is not null);
        var snapshot=test.Snapshot();
        var volume=test.Node(Main+".volume");
        check(volume.Kind==ViewNodeKind.Slider&&volume.Minimum==0&&volume.Maximum==100&&volume.Step==5&&
            volume.Value==55&&volume.SliderInteractionMode==SliderInteractionMode.ActivateToAdjust,
            "Watched stream exposes bounded observed volume with host-owned controller adjustment");
        check(test.Node(Main+".value") is {Kind:ViewNodeKind.Text,Text:"55%"},
            "Observed stream percentage is visible text in the main controls");
        check(!test.AllNodes().Any(node=>node.Id?.StartsWith(Pin,StringComparison.Ordinal)==true),
            "Pinned stream has no audio footer or duplicated controls");
        check(WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0&&
            SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).PinnedLayouts.Count==snapshot.PinnedLayouts.Count,
            "Stream audio declarations satisfy protocol roundtrip and WinUI contracts");

        test.Session.Core.State=test.Session.Core.State with{Deafened=true};await test.Refresh();
        check(test.Node(Main+".deafened") is {Kind:ViewNodeKind.Text} mainWarning&&mainWarning.Text!.Contains("Undeafen"),
            "Deafened call state and volume are visible text rather than accessibility-only labels");
        var receiver=test.Session.Core.Receivers.Single();
        var voice=test.Session.Core.State;
        var reads=test.Session.Core.Reads;
        test.Session.Core.CommandGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var write=test.Act(Main+".volume",35).AsTask();
        await Until(()=>test.Session.AudioCommands.Count==1);
        check(!write.IsCompleted&&test.Node(Main+".volume").Value==55,
            "Pending stream-volume write is awaited and does not optimistically repaint observed volume");
        test.Session.Core.CommandGate.SetResult();await write;test.Session.Core.CommandGate=null;
        check(test.Node(Main+".volume").Value==35&&test.Session.Core.Reads==reads&&test.Node(Main+".value").Text=="35%",
            "Confirmed audio acknowledgement updates controls without waiting for a broad state read");
        var command=test.Session.AudioCommands.Single();
        check(command.Method=="streamVolume"&&command.Args.GetProperty("account").GetString()=="1"&&
            command.Args.GetProperty("channelId").GetString()=="10"&&command.Args.GetProperty("key").GetString()=="guild:1:10:30"&&
            command.Args.GetProperty("userId").GetString()=="30"&&command.Args.GetProperty("lease").GetString()==receiver.Lease&&
            command.Args.GetProperty("value").GetDouble()==35,
            "Audio write carries the exact account, call, stream, participant, receiver lease and desired volume");
        await test.Act(Main+".mute");
        check(test.Node(Main+".mute").AccessibilityLabel=="Unmute stream"&&test.Node(Main+".mute").IsSelected==true&&
            test.Session.AudioCommands.Last().Args.GetProperty("value").GetBoolean(),
            "Main mute sends an explicit desired stream state and reflects the observation");
        check(test.Session.Core.State.Muted==voice.Muted&&test.Session.Core.State.Deafened==voice.Deafened&&
            test.Session.Core.MicrophoneRequests==0&&test.Session.Core.Receivers.Count==1&&!receiver.Disposed&&
            !test.Session.Core.Commands.Any(c=>c.Method is "mute" or "deafen" or "join" or "leave" or "unwatch" or "stopShare"),
            "Stream audio leaves voice state, microphone consent and the active receiver subscription unchanged");

        test.Session.ApplyWrites=false;await test.Act(Main+".volume",10);
        check(test.Node(Main+".volume").Value==35,"Successful command acknowledgement alone does not substitute for observed provider volume");
        test.Session.SetAudio(new(true,true,70));await test.Refresh();
        check(test.Node(Main+".volume").Value==70,"External provider audio changes reach the existing controls on refresh");
        test.Session.ApplyWrites=true;
        var before=test.Session.AudioCommands.Count;var declared=test.Node(Main+".volume");
        foreach(var invalid in new double?[]{null,-1,101,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            await test.Replay(declared,invalid);
        check(test.Session.AudioCommands.Count==before,"Missing, non-finite and out-of-range requested volumes do not reach the provider");
        foreach(var bound in new[]{0d,100d})await test.Act(Main+".volume",bound);
        check(test.Session.AudioCommands.TakeLast(2).Select(c=>c.Args.GetProperty("value").GetDouble()).SequenceEqual(new[]{0d,100d}),
            "Both advertised volume boundaries are accepted without conversion loss");
        var oldMute=test.Node(Main+".mute");var oldVolume=test.Node(Main+".volume");
        test.Session.SetAudio(new(false,false,null));await test.Refresh();before=test.Session.AudioCommands.Count;
        await test.Replay(oldMute);await test.Replay(oldVolume,45);
        check(test.Find(Main+".mute") is null&&test.Find(Pin+".volume") is null&&test.Session.AudioCommands.Count==before,
            "Unavailable audio removes main controls and rejects their stale actions");
        test.Session.SetAudio(new(true,false,55));await test.Refresh();
        await test.PinHost!.RevokeAsync();
        await Until(()=>test.Session.Core.Receivers.Single().Disposed);
        check(test.Find(Main+".mute") is null&&test.Find(Pin+".volume") is null,
            "Revoking the stream pin retires its audio controls with the receiver");
    }

    private static async Task PinContinuity(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        test.Session.Core.ReceiverGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        await test.Watch(wait:false);
        var initial=test.Snapshot().PinnedLayouts.Single(layout=>layout.Id=="discord.stream");
        var initialRoot=initial.Root??throw new InvalidOperationException("Missing initial stream pin root");
        test.Session.Core.ReceiverGate.SetResult();
        await Until(()=>test.Session.Core.Receivers.Count==1&&Nodes(test.Snapshot().PinnedLayouts.Single(layout=>layout.Id=="discord.stream").Root!)
            .Any(node=>node.WebBrowser?.HostedSession is not null));
        var first=test.Snapshot().PinnedLayouts.Single(layout=>layout.Id=="discord.stream");
        var firstRoot=first.Root??throw new InvalidOperationException("Missing active stream pin root");
        var firstVideo=Nodes(firstRoot).Single(node=>node.WebBrowser?.HostedSession is not null);
        var receiver=test.Session.Core.Receivers.Single();
        check(initialRoot.Id==firstRoot.Id&&initialRoot.Kind==firstRoot.Kind&&initial.ActiveInputScopeId==first.ActiveInputScopeId,
            "Stream pin retains its root and input scope when waiting status becomes a receiver");
        foreach(var available in new[]{false,true})
        {
            test.Session.SetAudio(new(available,false,available?55d:null));await test.Refresh();
            var published=await test.PinHost!.ReplaceSnapshotAsync();
            var current=published.PinnedLayouts.Single(layout=>layout.Id=="discord.stream");
            var currentRoot=current.Root??throw new InvalidOperationException("Missing refreshed stream pin root");
            var video=Nodes(currentRoot).Single(node=>node.WebBrowser?.HostedSession is not null);
            check(currentRoot.Id==firstRoot.Id&&currentRoot.Kind==firstRoot.Kind&&currentRoot.InputScopeId==firstRoot.InputScopeId&&
                current.ActiveInputScopeId==first.ActiveInputScopeId&&video.Id==firstVideo.Id&&video.Kind==firstVideo.Kind&&
                video.WebBrowser!.HostedSession==firstVideo.WebBrowser!.HostedSession&&
                !Nodes(currentRoot).Any(node=>node.Id?.StartsWith(Pin,StringComparison.Ordinal)==true)&&WidgetTestHost.ValidateWinUiPresentation(published).Count==0,
                $"Audio availability {available} preserves video-only pin, input scope and hosted video identity");
        }
        check(test.Session.Core.Receivers.Count==1&&ReferenceEquals(receiver,test.Session.Core.Receivers.Single())&&!receiver.Disposed&&
            test.Session.Core.Commands.Count(command=>command.Method=="watch")==1&&!test.Session.Core.Commands.Any(command=>command.Method=="unwatch"),
            "Audio availability changes never restart, replace or unsubscribe the receiver");
    }

    private static async Task BrowserRange(Action<bool,string> check)
    {
        await using var test=await Harness.Create(new(true,true,100));await test.Watch();
        check(test.Node(Main+".mute").AccessibilityLabel=="Unmute stream"&&test.Node(Main+".volume").Value==100&&
            test.Find(Main+".reset") is null&&test.Find(Pin+".reset") is null&&test.Find(Pin+".status") is null,
            "Displayed browser boundary uses ordinary mute and volume controls without reset or special status UI");
        check(WidgetTestHost.ValidateWinUiPresentation(test.Snapshot()).Count==0&&test.Session.AudioCommands.IsEmpty,
            "Rendering provider-projected 100% does not emit a normalization write");
        var controls=test.Node(Main+".controls");
        check(controls.Kind==ViewNodeKind.Row&&controls.Children.Count==3&&
            test.Node(Main+".mute").Kind==ViewNodeKind.Button&&string.IsNullOrEmpty(test.Node(Main+".mute").Text)&&
            test.Node(Main+".value") is {Kind:ViewNodeKind.Text,Text:"100%"},
            "Main audio is one compact icon, slider and visible percentage row");
        await test.Act(Main+".mute");
        check(test.Session.Core.State.Streams[0].Audio is {Muted:false,Volume:100}&&
            test.Session.AudioCommands.All(command=>command.Method=="streamMute"),
            "Stream mute does not issue a volume-normalization command");
        await test.Act(Main+".volume",65);
        check(test.Node(Main+".value").Text=="65%",
            "A genuine slider adjustment displays its acknowledged percentage");
    }

    private static async Task StaleOwners(Action<bool,string> check)
    {
        foreach(var change in new[]{"account","call","session","stream user","stream selection","receiver lease"})
        {
            await using var test=await Harness.Create();await test.Watch();
            var mute=test.Node(Main+".mute");var volume=test.Node(Main+".volume");
            switch(change)
            {
                case "account":test.Session.Core.State=test.Session.Core.State with{Account="2"};await test.Refresh();break;
                case "call":test.Session.Core.State=test.Session.Core.State with{ChannelId="20"};await test.Refresh();break;
                case "session":test.Session.Core.Reference=new(new string('b',32));await test.Refresh();break;
                case "stream user":test.Session.Core.State=test.Session.Core.State with{Streams=[test.Session.Core.State.Streams[0] with{UserId="31"}]};await test.Refresh();break;
                case "stream selection":
                    test.Session.Core.State=test.Session.Core.State with{Streams=[..test.Session.Core.State.Streams,new("guild:1:10:31","31","Second",new(true,false,55))]};
                    await test.Refresh();
                    await test.Act("discord.streams.stop");
                    await test.Watch("31");break;
                case "receiver lease":await test.Watch();break;
            }
            var before=test.Session.AudioCommands.Count;
            await test.Replay(mute);await test.Replay(volume,25);
            check(test.Session.AudioCommands.Count==before,$"Audio actions cannot retarget after {change} changes");
        }
    }

    private static async Task ProviderSnap(Action<bool,string> check)
    {
        await using var test=await Harness.Create(new(true,false,54.2));await test.Watch();
        test.Session.ApplyWrites=false;
        await test.Act(Main+".volume",55); // Provider confirms its snapped observation, not the requested 55.
        var slider=test.Node(Main+".volume");
        check(slider.Value==55&&test.Session.Core.State.Streams[0].Audio!.Volume==54.2&&
            test.Node(Main+".value").Text=="54%"&&slider.AccessibilityValue=="54%",
            "Provider-snapped volume keeps observed labels while aligning the controller thumb to its step");
        check(SliderMath.Increment(slider.Value!.Value,0,100,5)==60&&SliderMath.Decrement(slider.Value.Value,0,100,5)==50,
            "A settled 54.2% acknowledgement permits deliberate Right to 60 and Left to 50");
        foreach(var observed in new[]{.1,99.9})
        {
            test.Session.SetAudio(new(true,false,observed));await test.Refresh();
            var value=test.Node(Main+".volume").Value!.Value;
            check(value==observed&&(observed<1?SliderMath.Decrement(value,0,100,5)==0:SliderMath.Increment(value,0,100,5)==100),
                $"Observed {observed}% does not strand the nearest real endpoint behind thumb rounding");
        }
    }

    private static async Task MutedIntent(Action<bool,string> check)
    {
        await using var test=await Harness.Create();await test.Watch();
        var oldMute=test.Node(Main+".mute");var volume=test.Node(Main+".volume");
        test.Session.SetAudio(new(true,true,90));await test.Refresh();await test.Replay(oldMute);
        check(test.Session.AudioCommands.IsEmpty,"External stream mute changes invalidate the prior desired mute action");
        await test.Act(Main+".mute");
        check(!test.Session.AudioCommands.Last().Args.GetProperty("value").GetBoolean(),
            "Fresh Unmute stream sends false rather than toggling an unknown provider state");
        await test.Replay(volume,25);
        check(test.Node(Main+".volume").Value==25,
            "Absolute volume intent remains usable across observed volume changes in the same stream lease");
    }

    private static async Task QueuedVolume(Action<bool,string> check)
    {
        await using var test=await Harness.Create();await test.Watch();
        var snapshot=test.Widget.RenderSnapshot(test.PinHost!.CurrentSnapshot.WidgetInstanceId,500);
        test.Session.Core.CommandGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<bool> Send(double volume)=>await test.Widget.OnControllerInputAsync(new(ControllerButton.DPadRight,
            ControllerEventPhase.Repeated,ControllerInputContext.OpenWidget,Main+".volume",
            ActiveInputScopeId:snapshot.ActiveInputScopeId,SnapshotSequence:snapshot.Sequence,RequestedValue:volume));
        check(await Send(10),"Native-style absolute stream-volume input enters the SDK action queue");
        await Until(()=>test.Session.AudioCommands.Count==1);
        foreach(var value in new[]{20d,35d,80d})if(!await Send(value))throw new Exception("Stream slider input was rejected");
        test.Session.Core.CommandGate.SetResult();
        await Until(()=>test.Session.AudioCommands.Count>=2&&test.Find(Main+".volume")?.Value==80);
        check(test.Session.AudioCommands.Select(c=>c.Args.GetProperty("value").GetDouble()).SequenceEqual(new[]{10d,80d})&&
            test.Session.AudioConcurrency.All(count=>count==1),
            "SDK serialization and slider coalescing preserve the final volume without overlapping external writes");
    }

    private static async Task DelayedRead(Action<bool,string> check)
    {
        await using var test=await Harness.Create();await test.Watch();
        test.Session.HoldNextRead();
        await test.Widget.OnActionAsync(new("refresh","test.poll"));
        await test.Session.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var change=test.Act(Main+".volume",85).AsTask();
        await Until(()=>test.Session.AudioCommands.Count==1);
        await change.WaitAsync(TimeSpan.FromSeconds(5));
        check(!test.Session.ReadGate!.Task.IsCompleted&&test.Node(Main+".volume").Value==85,
            "Confirmed audio is visible before a broad pre-mutation poll completes");
        test.Session.ReadGate.SetResult();await test.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(test.Node(Main+".volume").Value==85,
            "A delayed broad poll cannot overwrite the acknowledged partial audio publication");
    }

    private static async Task LatestUnrelatedFields(Action<bool,string> check)
    {
        await using var test=await Harness.Create();await test.Watch();
        test.Session.Core.CommandGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var change=test.Act(Main+".volume",40).AsTask();await Until(()=>test.Session.AudioCommands.Count==1);
        test.Session.Core.State=test.Session.Core.State with{ChannelName="Updated lobby",Muted=false,
            Participants=[..test.Session.Core.State.Participants,new("31","Newest participant",false,false,false)]};
        await test.Refresh();test.Session.Core.CommandGate.SetResult();await change;
        check(test.Node("discord.main.channel").Text=="Updated lobby"&&test.Node("discord.main.state").Text=="3 in call"&&
            test.Node("discord.main.mute").Text=="Mute"&&test.Node(Main+".volume").Value==40,
            "Audio acknowledgement merges into the latest snapshot without reverting unrelated call observations");
    }

    private sealed class Harness: IAsyncDisposable
    {
        internal readonly AudioSession Session;
        internal readonly DiscordWebViewWidget Widget;
        internal WidgetPinnedLayoutTestHost? PinHost;
        private Harness(DiscordStreamAudio audio)
        {
            Session=new(audio);
            Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(Session,refreshInterval:TimeSpan.FromHours(1)),
                new WidgetTestHostServicesBuilder().WithHandler(WidgetPinningCapabilities.Open,(_,token)=>
                {token.ThrowIfCancellationRequested();return ValueTask.FromResult(new WidgetPinReceipt(true));}).Build());
        }
        internal static async Task<Harness> Create(DiscordStreamAudio? audio=null)
        {
            var test=new Harness(audio??new(true,false,55));
            await WidgetTestHost.InitializeAsync(test.Widget);await WidgetTestHost.SetLifecycleStateAsync(test.Widget,WidgetLifecycleState.Interactive);
            await Until(()=>test.Find("discord.stream.30") is not null);return test;
        }
        internal ViewSnapshot Snapshot()=>Widget.Render().CreateSnapshot("stream.audio",1);
        internal IEnumerable<ViewNode> AllNodes(){var snapshot=Snapshot();return Nodes(snapshot.Root).Concat(snapshot.PinnedLayouts.Where(p=>p.Root is not null).SelectMany(p=>Nodes(p.Root!)));}
        internal ViewNode? Find(string id)=>AllNodes().FirstOrDefault(n=>n.Id==id);
        internal ViewNode Node(string id)=>Find(id)??throw new InvalidOperationException("Missing stream audio control: "+id);
        internal ValueTask Act(string id,double? value=null)=>Replay(Node(id),value);
        internal ValueTask Replay(ViewNode node,double? value=null)=>Widget.OnActionAsync(new(node.ValueChangedActionId??node.ActionId??throw new InvalidOperationException("Not an action"),node.Id,
            RequestedValue:value,InputScopeId:Snapshot().ActiveInputScopeId){FocusedElementId=node.Id});
        internal async Task Refresh(){await Widget.OnActionAsync(new("refresh","test"));await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));}
        internal async Task Watch(string user="30",bool wait=true)
        {
            var receivers=Session.Core.Receivers.Count;
            await Act("discord.stream."+user);
            if(PinHost is null){PinHost=WidgetTestHost.CreatePinnedLayoutHost(Widget,"stream.audio."+Guid.NewGuid().ToString("N"),100);if(!await PinHost.SelectAsync("discord.stream"))throw new Exception("Pin rejected");}
            if(wait)await Until(()=>Session.Core.Receivers.Count>receivers&&Find(Main+".mute") is not null);
        }
        public async ValueTask DisposeAsync()
        {
            Session.Core.CommandGate?.TrySetResult();Session.Core.ReceiverGate?.TrySetResult();Session.ReadGate?.TrySetResult();
            await WidgetTestHost.DestroyAsync(Widget);
        }
    }

    // Audio mutation is deliberately opt-in and local to this test suite; the
    // shared fake's defaults and all existing call/receiver tests are unchanged.
    private sealed class AudioSession: IDiscordSession
    {
        internal readonly FakeSession Core=new();
        internal bool ApplyWrites=true;
        internal readonly ConcurrentQueue<int> AudioConcurrency=new();
        private int inFlight;
        private bool holdNextRead;
        internal TaskCompletionSource? ReadGate;
        internal readonly TaskCompletionSource ReadStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal AudioSession(DiscordStreamAudio audio)=>SetAudio(audio);
        internal ConcurrentQueue<FakeSession.Command> AudioCommands=>new(Core.Commands.Where(c=>c.Method is "streamMute" or "streamVolume"));
        public WebSessionReference? Reference=>Core.Reference;
        internal void SetAudio(DiscordStreamAudio audio)=>Core.State=Core.State with{Streams=Core.State.Streams.Select(s=>s with{Audio=audio}).ToArray()};
        internal void HoldNextRead(){ReadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);holdNextRead=true;}
        public async ValueTask<DiscordState> ReadAsync(CancellationToken token)
        {
            var captured=await Core.ReadAsync(token);
            if(holdNextRead){holdNextRead=false;ReadStarted.TrySetResult();await ReadGate!.Task;}
            return captured;
        }
        public ValueTask<WebSessionPermissionResult> RequestNotificationsAsync(CancellationToken token)=>Core.RequestNotificationsAsync(token);
        public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)=>Core.RequestMicrophoneAsync(token);
        public ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)=>Core.OpenReceiverAsync(token);
        public async ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
        {
            var audio=method is "streamMute" or "streamVolume";
            if(audio)AudioConcurrency.Enqueue(Interlocked.Increment(ref inFlight));
            try
            {
                var result=await Core.InvokeAsync(method,args,token);
                if(audio&&ApplyWrites)
                {
                    var input=JsonSerializer.SerializeToElement(args);
                    if(Core.State.Account==input.GetProperty("account").GetString()&&Core.State.ChannelId==input.GetProperty("channelId").GetString())
                        Core.State=Core.State with{Streams=Core.State.Streams.Select(s=>s.Key==input.GetProperty("key").GetString()&&s.UserId==input.GetProperty("userId").GetString()&&s.Audio is {} current
                            ?s with{Audio=method=="streamMute"?current with{Muted=input.GetProperty("value").GetBoolean()}:current with{Volume=input.GetProperty("value").GetDouble()}}:s).ToArray()};
                }
                if(audio)
                {
                    var input=JsonSerializer.SerializeToElement(args);
                    var observed=Core.State.Streams.Single(s=>s.Key==input.GetProperty("key").GetString()).Audio;
                    return JsonSerializer.SerializeToElement(new{audio=observed},new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                return result;
            }
            finally{if(audio)Interlocked.Decrement(ref inFlight);}
        }
        public ValueTask DisposeAsync()=>Core.DisposeAsync();
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Until(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!condition())await Task.Delay(10,timeout.Token);}
}
