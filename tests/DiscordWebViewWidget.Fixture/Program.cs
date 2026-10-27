using System.Text.Json;
using System.Collections.Concurrent;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetRuntime;

// No Discord page, network, media capture or account credentials exist in this fixture.
var audioMode=args.Contains("--audio",StringComparer.Ordinal)||File.Exists(Path.Combine(AppContext.BaseDirectory,"fixture-audio.enabled"));
var voicePinMode=File.Exists(Path.Combine(AppContext.BaseDirectory,"fixture-voice-pin.enabled"));
var voiceControlsMode=File.Exists(Path.Combine(AppContext.BaseDirectory,"fixture-voice-controls.enabled"));
var fixtureArgs=args.Where(arg=>arg!="--audio").ToArray();
if(fixtureArgs is ["--snapshots",var destination])
{
    Directory.CreateDirectory(destination);
    var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(new SyntheticDiscordSession(audioMode,voicePinMode,voiceControlsMode)),new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetPinningCapabilities.Open,(_,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult(new WidgetPinReceipt(true));}).Build());
    await WidgetTestHost.InitializeAsync(widget);
    await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
    await widget.WaitForStateReadAsync();
    foreach(var section in new[]{"call","channels"})
    {
        if(section=="channels")await widget.OnActionAsync(new("server-picker","fixture"){FocusedElementId="discord.main.mute"});
        var snapshot=widget.Render().CreateSnapshot("discord.fixture",1);
        var errors=WidgetTestHost.ValidateWinUiPresentation(snapshot);
        if(errors.Count!=0)throw new InvalidDataException(string.Join("; ",errors));
        await File.WriteAllTextAsync(Path.Combine(destination,section+".json"),JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));
    }
    if(audioMode)
    {
        var picker=widget.Render().CreateSnapshot("discord.fixture",2);
        await widget.OnActionAsync(new(picker.Root.Shortcuts.Single(s=>s.Button==ControllerButton.B).ActionId,"fixture"));
        var before=widget.Render().CreateSnapshot("discord.fixture",2);
        var watch=Nodes(before.Root).Single(node=>node.Id=="discord.stream.2");
        await widget.OnActionAsync(new(watch.ActionId!,watch.Id,InputScopeId:before.ActiveInputScopeId));
        var pin=WidgetTestHost.CreatePinnedLayoutHost(widget,"discord.fixture",10);
        if(!await pin.SelectAsync("discord.stream"))throw new InvalidOperationException("Synthetic audio pin was not selected.");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while(!Nodes(widget.Render().CreateSnapshot("discord.fixture",11).Root)
            .Any(node=>node.Id=="discord.streams.audio.volume"))await Task.Delay(10,deadline.Token);
        var watched=widget.Render().CreateSnapshot("discord.fixture",12);
        var videoPin=watched.PinnedLayouts.Single(layout=>layout.Id=="discord.stream").Root!;
        if(!Nodes(videoPin).Any(node=>node.Id=="discord.pin.video")||Nodes(videoPin).Any(node=>node.Kind is ViewNodeKind.Button or ViewNodeKind.Slider))
            throw new InvalidDataException("The stream pin must contain video without audio controls.");
        var errors=WidgetTestHost.ValidateWinUiPresentation(watched);
        if(errors.Count!=0)throw new InvalidDataException(string.Join("; ",errors));
        await File.WriteAllTextAsync(Path.Combine(destination,"streams-audio.json"),JsonSerializer.Serialize(watched,new JsonSerializerOptions{WriteIndented=true}));
    }
    await WidgetTestHost.DestroyAsync(widget);
    return;
}
Environment.ExitCode=await WidgetApplicationBootstrap.RunAsync(fixtureArgs,()=>new DiscordWebViewWidget(new SyntheticDiscordSession(audioMode,voicePinMode,voiceControlsMode)));
static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));

internal sealed class SyntheticDiscordSession:IDiscordSession
{
    private readonly bool audioMode;
    private readonly bool voiceControlsMode;
    private DiscordVoiceSettings voiceSettings=new(true,true,false,[new("input-a","Microphone A"),new("input-b","Microphone B")],
        [new("output-a","Speakers A"),new("output-b","Speakers B")],"input-a","output-a",null);
    private readonly ConcurrentDictionary<string,string> leases=new(StringComparer.Ordinal);
    private DiscordState state=new(1,true,"1",new(true,true,true,true,false,false),
        [new("10","100","The lobby","Friends & games"),new("11","100","Co-op night","Friends & games"),
         new("12","100","Quiet corner","Friends & games"),new("21","200","Build planning","Path of Exile"),
         new("22","200","Trading lounge","Path of Exile"),new("31","300","Indie discoveries","After hours")],
        "10","The lobby",true,false,[new("1","You",true,false,false),new("2","Avery",false,false,true),
            new("3","Morgan",false,true,false),new("4","Sky",false,false,false)],
        [new("guild:100:10:2","2","Avery"),new("guild:100:10:4","4","Sky")],false,false,null,"closed",
        [new("2","Avery","online","streaming","Hades II","On a new run","10","100","The lobby","Friends & games",false,"guild:100:10:2",true),
         new("3","Morgan","idle","listening","Evening playlist","",null,null,null,null,false,null,false),
         new("4","Sky","online","voice","Co-op night","Friends & games","11","100","Co-op night","Friends & games",true,null,false),
         new("5","Reese","dnd","playing","Hollow Knight: Silksong","",null,null,null,null,false,null,false)],"ready"){VoiceConnection="connected"};
    internal SyntheticDiscordSession(bool audioMode=false,bool voicePinMode=false,bool voiceControlsMode=false)
    {
        this.audioMode=audioMode;
        this.voiceControlsMode=voiceControlsMode;
        if(voiceControlsMode)state=state with{Participants=state.Participants.Select(person=>person.Id==state.Account?person:person with{Audio=new(true,false,50)}).ToArray()};
        if(audioMode)state=state with{Streams=state.Streams.Select(stream=>stream with{Audio=new(true,false,100)}).ToArray()};
        if(voicePinMode)state=state with{ChannelName="A very long voice channel name for compact controller pin layout checks",Muted=true,Deafened=true};
    }
    // Navigation-only sentinel: deliberately never registered with the broker.
    // The account route can be entered/backed out without any page, network or login.
    public WebSessionReference? Reference=>new("00000000000000000000000000000000");
    public ValueTask<DiscordState> ReadAsync(CancellationToken token){token.ThrowIfCancellationRequested();return ValueTask.FromResult(state);}
    public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)=>ValueTask.FromResult(new WebSessionMicrophoneResult(WebSessionMicrophoneState.Granted));
    public ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var value=JsonSerializer.SerializeToElement(args);
        if(voiceControlsMode&&method is "voiceMute" or "voiceVolume")
        {
            if(value.GetProperty("account").GetString()!=state.Account||value.GetProperty("channelId").GetString()!=state.ChannelId)
                throw new InvalidDataException("Synthetic participant owner changed.");
            var person=state.Participants.Single(person=>person.Id==value.GetProperty("userId").GetString()&&person.Id!=state.Account);
            var audio=method=="voiceMute"?person.Audio! with{Muted=value.GetProperty("value").GetBoolean()}:person.Audio! with{Volume=value.GetProperty("value").GetDouble()};
            state=state with{Participants=state.Participants.Select(item=>item.Id==person.Id?item with{Audio=audio}:item).ToArray()};
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new{audio},DiscordWebSessionClient.Json));
        }
        if(voiceControlsMode&&method is "voiceSettings" or "voiceInputDevice" or "voiceOutputDevice")
        {
            if(value.GetProperty("account").GetString()!=state.Account)throw new InvalidDataException("Synthetic device account changed.");
            if(method=="voiceInputDevice")voiceSettings=voiceSettings with{InputDeviceId=value.GetProperty("value").GetString()};
            if(method=="voiceOutputDevice")voiceSettings=voiceSettings with{OutputDeviceId=value.GetProperty("value").GetString()};
            voiceSettings.Validate();return ValueTask.FromResult(JsonSerializer.SerializeToElement(voiceSettings,DiscordWebSessionClient.Json));
        }
        if(audioMode&&method is "watch" or "watchActivity")
        {
            var stream=state.Streams.Single(stream=>stream.Key==value.GetProperty("key").GetString());
            if(value.GetProperty("account").GetString()!=state.Account||value.GetProperty("channelId").GetString()!=state.ChannelId)
                throw new InvalidDataException("Synthetic watch owner changed.");
            var lease=value.GetProperty("lease").GetString()!;
            leases[lease]=stream.Key;
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new{lease,offer=new{type="offer",sdp="synthetic-audio-fixture"}}));
        }
        if(audioMode&&method is "streamMute" or "streamVolume")
        {
            var key=value.GetProperty("key").GetString();var user=value.GetProperty("userId").GetString();
            if(value.GetProperty("account").GetString()!=state.Account||value.GetProperty("channelId").GetString()!=state.ChannelId||
                !leases.TryGetValue(value.GetProperty("lease").GetString()!,out var watched)||watched!=key)
                throw new InvalidDataException("Synthetic audio owner changed.");
            var stream=state.Streams.Single(stream=>stream.Key==key&&stream.UserId==user&&stream.Audio is {Available:true});
            var audio=stream.Audio!;
            if(method=="streamMute")audio=audio with{Muted=value.GetProperty("value").GetBoolean()};
            else
            {
                var volume=value.GetProperty("value").GetDouble();
                if(!double.IsFinite(volume)||volume is <0 or >100)throw new InvalidDataException("Synthetic volume is invalid.");
                audio=audio with{Volume=volume};
            }
            state=state with{Streams=state.Streams.Select(item=>item.Key==key?item with{Audio=audio}:item).ToArray()};
            // Match the provider's observed-state acknowledgement. An empty
            // response exercises the uncertain-write error path, not success.
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new{audio}));
        }
        if(audioMode&&method=="answer" && (value.GetProperty("account").GetString()!=state.Account||
            value.GetProperty("channelId").GetString()!=state.ChannelId||!leases.ContainsKey(value.GetProperty("lease").GetString()!)||
            value.GetProperty("answer").GetProperty("type").GetString()!="answer"||
            value.GetProperty("answer").GetProperty("sdp").GetString()!="synthetic-audio-fixture"))
            throw new InvalidDataException("Synthetic answer owner changed.");
        if(audioMode&&method=="unwatch"&&value.TryGetProperty("lease",out var removed))leases.TryRemove(removed.GetString()!,out _);
        if(method=="mute")state=state with{Muted=value.GetProperty("value").GetBoolean()};
        if(method=="deafen")state=state with{Deafened=value.GetProperty("value").GetBoolean()};
        if(method=="leave")state=state with{ChannelId=null,Participants=[],Streams=[],Activities=[],VoiceConnection="disconnected"};
        if(method is "join" or "joinActivity")
        {
            var channel=state.Channels.Single(c=>c.Id==value.GetProperty("target").GetString());
            state=state with{ChannelId=channel.Id,ChannelName=channel.Name,VoiceConnection="connected",Participants=[new("1","You",state.Muted,state.Deafened,false)],Streams=[],
                Activities=state.Activities.Select(row=>row with{CanJoinVoice=row.ChannelId is not null&&row.ChannelId!=channel.Id,CanWatch=false}).ToArray()};
        }
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new{}));
    }
    public ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!audioMode)throw new NotSupportedException("Synthetic fixture has no media.");
        return ValueTask.FromResult<IDiscordReceiver>(new SyntheticAudioReceiver());
    }
    public ValueTask DisposeAsync(){leases.Clear();return ValueTask.CompletedTask;}
}

// A protocol-shaped receiver only. This session is never registered with the
// host, so no WebView, remote document, audio device or media stream is opened.
internal sealed class SyntheticAudioReceiver:IDiscordReceiver
{
    private readonly TaskCompletionSource<DiscordReceiverFailure> failure=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<DiscordReceiverFailure> Failure=>failure.Task;
    public WebSessionReference Reference{get;}=new(Guid.NewGuid().ToString("N"));
    public ValueTask<JsonElement> AcceptAsync(JsonElement offer,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!Guid.TryParseExact(offer.GetProperty("lease").GetString(),"N",out _)||
            offer.GetProperty("offer").GetProperty("type").GetString()!="offer"||
            offer.GetProperty("offer").GetProperty("sdp").GetString()!="synthetic-audio-fixture")
            throw new InvalidDataException("Invalid synthetic receiver offer.");
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new{lease=offer.GetProperty("lease").GetString(),
            answer=new{type="answer",sdp="synthetic-audio-fixture"}}));
    }
    public ValueTask DisposeAsync(){failure.TrySetCanceled();return ValueTask.CompletedTask;}
}
