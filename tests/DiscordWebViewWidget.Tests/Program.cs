using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

var passed=ActivityContractTests.Run();
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
static async Task Wait(Func<bool> condition)
{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!condition())await Task.Delay(10,timeout.Token);}
static ViewSnapshot Snapshot(DiscordWebViewWidget widget,long sequence=1)
{
    try{return widget.Render().CreateSnapshot("discord.test",sequence);}
    catch(ProtocolValidationException error){throw new InvalidDataException(string.Join("; ",error.Errors),error);}
}
static async Task Act(DiscordWebViewWidget widget,string action,string? text=null)
{
    var snapshot=Snapshot(widget);action=DeclaredActions.Resolve(snapshot,action);var declared=Nodes(snapshot.Root).FirstOrDefault(node=>node.ActionId==action);
    await widget.OnActionAsync(new(action,declared?.Id??"discord.test.action",InputScopeId:snapshot.ActiveInputScopeId)
        {CommittedText=text,FocusedElementId=declared?.Id??"discord.account.open"});
}

var fake=new FakeSession();fake.State=fake.State with {NotificationPermission="default"};var pins=new ConcurrentQueue<string>();
var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),
    new WidgetTestHostServicesBuilder().WithHandler(WidgetPinningCapabilities.Open,(request,token)=>{token.ThrowIfCancellationRequested();pins.Enqueue(request.LayoutId);return ValueTask.FromResult(new WidgetPinReceipt(true));}).Build());
await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
try
{
    await Wait(()=>Nodes(Snapshot(widget).Root).Any(node=>node.Id=="discord.main.mute"));
    Check(fake.Commands.IsEmpty&&fake.MicrophoneRequests==0,"Startup reads state without joining, sharing, or prompting for microphone");
    Check(WidgetTestHost.ValidateWinUiPresentation(Snapshot(widget)).Count==0,"Unified call declarations satisfy protocol and WinUI contracts");
    var enable = Nodes(Snapshot(widget).Root).Single(node => node.Id == "discord.notifications.enable").ActionId!;
    Check(fake.NotificationRequests == 0, "Rendering the call page never prompts for notification permission");
    await Act(widget, enable);
    await Wait(() => !Nodes(Snapshot(widget).Root).Any(node => node.Id == "discord.notifications.enable"));
    await Act(widget, enable);
    Check(fake.NotificationRequests == 1 && fake.MicrophoneRequests == 0, "Explicit notification setup disappears after grant and rejects a stale click");
    await Act(widget,"server-picker");
    var all=Snapshot(widget);
    Check(Nodes(all.Root).Single(node=>node.Id=="discord.channels").IndexedCollection!.Count==1000,"All 1,000 loaded channels remain available without eager row rendering");
    using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"channels"))
    {
        using var deep=await host.AcquireAsync("discord.channels",999,1);
        Check(deep.Range.Items.Single().Key=="channel.1000","Deep channel range resolves through the shared indexed source");
        Check(deep.RouteAction("channel.1000",ControllerButton.A)==WidgetOperationAdmission.Enqueued,"Deep channel row admits its captured action");
        await Wait(()=>fake.Commands.Any(command=>command.Method=="join"));
        Check(fake.MicrophoneRequests==1&&fake.Commands.Single(command=>command.Method=="join").Args.GetProperty("target").GetString()=="1000","Native Join asks for microphone then uses the captured channel identity");
    }
    await Wait(()=>!Nodes(Snapshot(widget).Root).Any(node=>node.Text=="Updating Discord…"));
    fake.Microphone=WebSessionMicrophoneState.Denied;
    var commandsBefore=fake.Commands.Count;var microphoneBefore=fake.MicrophoneRequests;
    using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"denied",10))
    {
        using var row=await host.AcquireAsync("discord.channels",998,1);
        row.RouteAction("channel.999",ControllerButton.A);
        await Wait(()=>fake.MicrophoneRequests>microphoneBefore);
        await Wait(()=>Nodes(Snapshot(widget).Root).Any(node=>node.Id=="discord.notice"&&node.Text!.Contains("not granted")));
    }
    Check(fake.Commands.Count==commandsBefore,"Denied microphone admission cannot join the channel");
    fake.Microphone=WebSessionMicrophoneState.Granted;
    fake.MicrophoneGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
    microphoneBefore=fake.MicrophoneRequests;
    using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"changed-call",20))
    {
        using var row=await host.AcquireAsync("discord.channels",997,1);
        row.RouteAction("channel.998",ControllerButton.A);
        await Wait(()=>fake.MicrophoneRequests>microphoneBefore);
        fake.State=fake.State with{ChannelId="20"};var reads=fake.Reads;
        await Act(widget,"refresh");await Wait(()=>fake.Reads>reads);
        await Wait(()=>Nodes(Snapshot(widget).Root).Single(node=>node.Id=="discord.channels").IndexedCollection!.ContentRevision>0);
        fake.MicrophoneGate.SetResult();await Task.Delay(60);
    }
    Check(fake.Commands.Count==commandsBefore,"A call change during microphone consent invalidates the pending Join intent");
    fake.MicrophoneGate=null;
    await Act(widget,"search","Channel 1000");
    Check(Nodes(Snapshot(widget).Root).Single(node=>node.Id=="discord.channels").IndexedCollection!.Count==1,"Channel search filters the complete catalog");
    await Act(widget,"clear-search");
    Check(Nodes(Snapshot(widget).Root).Single(node=>node.Id=="discord.channels").IndexedCollection!.Count==1000,"Clear search restores every loaded channel");
    await Act(widget,Snapshot(widget).Root.Shortcuts.Single(shortcut=>shortcut.Button==ControllerButton.B).ActionId);
    fake.CommandGate=new(TaskCreationOptions.RunContinuationsAsynchronously);fake.FailCommands=true;
    var commandCount=fake.Commands.Count;
    await Act(widget,"mute");await Wait(()=>fake.Commands.Count>commandCount);
    await Act(widget,"mute");
    Check(fake.Commands.Count==commandCount+1,"Repeated activation during a pending mutation does not enqueue another provider command");
    Check(Nodes(Snapshot(widget).Root).Single(node=>node.Id=="discord.main.mute").IsDisabled!=true,"Pending mutation preserves the native control's focus eligibility");
    fake.CommandGate.SetResult();
    await Wait(()=>Nodes(Snapshot(widget).Root).Any(node=>node.Id=="discord.notice"&&node.Text!.Contains("could not confirm")));
    Check(fake.Commands.Count==commandCount+1,"Uncertain provider mutation reports failure without automatic replay");
    fake.CommandGate=null;fake.FailCommands=false;
    await Act(widget,"account");var account=Snapshot(widget);
    Check(account.ActiveInputScopeId=="discord.account.scope"&&Nodes(account.Root).Any(node=>node.Id=="discord.account.browser"),"Account page presents the existing host session in a navigator-owned scope");
    await Act(widget,Nodes(account.Root).Single(node=>node.Id=="discord.account.back").ActionId!);
    Check(Snapshot(widget).InitialFocusId=="discord.account.open","Back from account restores the originating native control");

    await Act(widget,"watch.30");
    Check(pins.TryDequeue(out var layout)&&layout=="discord.stream"&&!fake.Commands.Any(command=>command.Method=="watch"),"Watch requests a pin directly and waits for host selection before acquiring media");
    var pin=WidgetTestHost.CreatePinnedLayoutHost(widget,"pin",100);
    Check(await pin.SelectAsync("discord.stream"),"Host selection activates the declared stream layout");
    await Wait(()=>fake.Receivers.Count==1&&Nodes(Snapshot(widget).PinnedLayouts.Single(p=>p.Id=="discord.stream").Root!).Any(node=>node.WebBrowser is not null));
    Check(fake.Commands.Any(command=>command.Method=="watch" && command.Args.GetProperty("videoView").GetString()==fake.Receivers.Single().Reference.Id),"Selected stream attaches to the exact direct video view");
    var receiver=fake.Receivers.Single();
    await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Background);
    Check(!receiver.Disposed&&!fake.Commands.Any(command=>command.Method is "leave" or "stopShare"),"Overlay background preserves call and selected incoming receiver");
    await pin.RevokeAsync();await Wait(()=>receiver.Disposed);
    Check(fake.Commands.Any(command=>command.Method=="unwatch"),"Pin revocation closes its receiver and releases the exact provider lease");
    await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
    await Act(widget,"watch.30");
    fake.ReceiverGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
    await pin.ReplaceSnapshotAsync();await pin.SelectAsync("discord.stream");
    await Wait(()=>fake.ReceiverRequests==2);
    fake.State=fake.State with{Account="2",ChannelId="40",Streams=[]};var readCount=fake.Reads;
    await Act(widget,"refresh");await Wait(()=>fake.Reads>readCount);
    await Wait(()=>Nodes(Snapshot(widget).Root).Any(node=>node.Id=="discord.notice"&&node.Text!.Contains("call changed")));
    fake.ReceiverGate.SetResult();await Wait(()=>fake.Receivers.Count==2&&fake.Receivers.Last().Disposed);
    Check(!Nodes(Snapshot(widget).PinnedLayouts.Single(p=>p.Id=="discord.stream").Root!).Any(node=>node.WebBrowser is not null),"Late receiver after account change is disposed without being published");
}
finally{fake.MicrophoneGate?.TrySetResult();fake.ReceiverGate?.TrySetResult();fake.CommandGate?.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
Check(fake.Disposed,"Widget teardown disposes its backend session");
await TransportChecks(Check);
await SharingTests.RunAsync(Check);
await ActivityUiTests.RunAsync(Check);
await ReceiverHealthTests.RunAsync(Check);
await ReceiverLifecycleTests.RunAsync(Check);
await DomainIntentTests.RunAsync(Check);
await ChannelSuggestionTests.RunAsync(Check);
await StreamAudioTests.RunAsync(Check);
await UnifiedLayoutTests.RunAsync(Check);
await PickerTests.RunAsync(Check);
await ChannelFavoritesTests.RunAsync(Check);
await VoicePinTests.RunAsync(Check);
await VoiceControlTests.RunAsync(Check);
await ProviderInvalidationTests.RunAsync(Check);
await ConnectionDetailsTests.RunAsync(Check);
Console.WriteLine($"Discord WebView widget: {passed} checks passed.");

static async Task TransportChecks(Action<bool,string> check)
{
    var messages=new ConcurrentQueue<string>();var opens=0;var catalogs=0;var close=0;
    var version=Guid.NewGuid().ToString("N");var account="1";var wrongCatalog=false;
    var source=new FakeSession().State with{Channels=[new("10","1","First","Guild"),new("20","1","Second","Guild") ]};
    var probe=WidgetTestHost.Attach(new TransportProbe(),new WidgetTestHostServicesBuilder().WithHandler(WidgetWebSessionCapabilities.Command,async(command,token)=>
    {
        if(command.Operation==WebSessionOperation.OpenBackend)
        {opens++;check(command.Definition?.PageAdapter?.Mode==WebSessionPageAdapterMode.SessionBackend,"Production transport uses the explicit background backend contract");return new(){Session=new(new string('b',32))};}
        if(command.Operation==WebSessionOperation.OpenBackendVideo)
        {
            check(command.Definition?.Source is {Kind:WebSessionSourceKind.BackendVideo} video && video.Location==new string('b',32),"Direct playback opens only for its own backend");
            return new(){Session=new(new string('c',32))};
        }
        if(command.Operation==WebSessionOperation.Close){close++;return new();}
        if(command.Operation==WebSessionOperation.ReadMessages){await Task.Delay(2,token);return new(){MessagesBase64=messages.TryDequeue(out var message)?[message]:[]};}
        if(command.Operation==WebSessionOperation.PostMessage)
        {
            using var document=JsonDocument.Parse(Convert.FromBase64String(command.MessageBase64!));var request=document.RootElement;
            if(request.GetProperty("kind").GetString()!="request")return new();
            var id=request.GetProperty("id").GetString();var method=request.GetProperty("method").GetString();object result;
            if(method=="snapshot")
                result=new{context=new{source.Revision,source.Ready,account,source.Capabilities,source.ChannelId,source.ChannelName,source.Muted,source.Deafened,
                    source.Sharing,source.SharePending,source.ShareError,source.RelayState,channelVersion=version,channelCount=source.Channels.Length},
                    items=source.Participants.Select(value=>(object)new{kind="participant",value}).Concat(source.Streams.Select(value=>(object)new{kind="stream",value})).ToArray()};
            else if(method=="channels")
            {catalogs++;result=new{context=new{account=wrongCatalog?"wrong":account,channelVersion=version},items=source.Channels};}
            else throw new InvalidOperationException("Unexpected test method");
            messages.Enqueue(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{widgetRail=1,kind="response",id,payload=result},new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }
        return new();
    }).Build());
    await using(var client=new DiscordWebSessionClient(probe.Sessions))
    {
        var first=await client.ReadAsync(default);
        await using(var receiver=await client.OpenReceiverAsync(default)){receiver.Bind(new string('a',32));}
        var second=await client.ReadAsync(default);
        check(opens==1&&catalogs==1&&ReferenceEquals(first.Channels,second.Channels),"Production reads reuse an unchanged account-bound channel catalog");
        check(second.Participants.Length==2&&second.Streams.Length==1,"Role-tagged status items reconstruct every participant and stream");
        account="2";version=Guid.NewGuid().ToString("N");wrongCatalog=true;
        try{await client.ReadAsync(default);throw new Exception("Mixed-account catalog accepted");}
        catch(InvalidDataException){check(true,"Account or catalog mismatch rejects the whole snapshot");}
        wrongCatalog=false;
        var recovered=await client.ReadAsync(default);
        check(recovered.Account=="2"&&recovered.Channels.Length==2&&catalogs==3,"A rejected catalog cannot poison the next authoritative read");
        await Task.WhenAll(client.ReadAsync(default).AsTask(),client.ReadAsync(default).AsTask());
        check(opens==1&&catalogs==3,"Concurrent status reads retain one backend and do not duplicate catalog fetches");
    }
    check(close==2,"Production transport closes each owned source and playback session exactly once");
    await WidgetTestHost.DestroyAsync(probe);
}

sealed class TransportProbe:Widget
{
    internal WidgetWebSessionService Sessions=>HostServices.WebSessions;
    public override WidgetView Render()=>new(UI.Text("Transport probe","transport.probe"));
}

sealed class FakeSession:IDiscordSession
{
    public event Action<WebSessionReference>? Invalidated;
    internal void NotifyChanged() { if(Reference is { } session)Invalidated?.Invoke(session); }
    internal sealed record Command(string Method,JsonElement Args);
    internal readonly ConcurrentQueue<Command> Commands=new();
    internal readonly ConcurrentQueue<FakeReceiver> Receivers=new();
    internal int Reads,MicrophoneRequests,ReceiverRequests;
    internal bool Disposed;
    internal TaskCompletionSource? MicrophoneGate,ReceiverGate,CommandGate,ShareGate;
    internal bool FailCommands,FailReceivers;
    internal bool IgnoreShareCancellation;
    internal TaskCompletionSource? DiagnosticsGate;
    internal JsonElement Diagnostics=JsonSerializer.SerializeToElement(new{source=new{width=2560,height=1440},
        outbound=new[]{new{width=1920,height=1080,framesPerSecond=59.5}},advertised=new[]{new{width=1280,height=720,frameRate=60}}});
    internal WebSessionMicrophoneState Microphone=WebSessionMicrophoneState.Granted;
    internal DiscordState State=new(1,true,"1",new(true,true,true,true,true,true),
        Enumerable.Range(1,1000).Select(index=>new DiscordChannel(index.ToString(),"1","Channel "+index,"Test guild")).ToArray(),
        "10","Lobby",true,false,[new("1","Self",true,false,false),new("30","Other",false,false,true)],
        [new("guild:1:10:30","30","Other")],false,false,null,"closed");
    public WebSessionReference Reference{get;set;}=new(new string('a',32));
    public ValueTask<DiscordState> ReadAsync(CancellationToken token){token.ThrowIfCancellationRequested();Interlocked.Increment(ref Reads);return ValueTask.FromResult(State);}
    internal int NotificationRequests;
    public ValueTask<WebSessionPermissionResult> RequestNotificationsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Increment(ref NotificationRequests);
        State = State with { NotificationPermission = "granted" };
        return ValueTask.FromResult(new WebSessionPermissionResult(WebSessionPermissionState.Granted));
    }
    public async ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)
    {Interlocked.Increment(ref MicrophoneRequests);if(MicrophoneGate is { } gate)await gate.Task.WaitAsync(token);return new(Microphone);}
    public async ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var payload=JsonSerializer.SerializeToElement(args);Commands.Enqueue(new(method,payload));
        if(method is "watch" or "watchActivity" && (!payload.TryGetProperty("key",out var key)||!State.Streams.Any(stream=>stream.Key==key.GetString())))
            throw new InvalidDataException("Provider stream key is missing or stale.");
        if(CommandGate is { } gate)await gate.Task.WaitAsync(token);
        if(method=="share"&&ShareGate is { } shareGate)
        {if(IgnoreShareCancellation)await shareGate.Task;else await shareGate.Task.WaitAsync(token);}
        if(FailCommands)throw new IOException("Synthetic uncertain command outcome");
        if(method=="streamDiagnostics")
        {
            // Deliberately ignore cancellation: publication must fence stale provider reads.
            if(DiagnosticsGate is { } diagnosticsGate)await diagnosticsGate.Task;
            return Diagnostics;
        }
        return method is "watch" or "watchActivity"?JsonSerializer.SerializeToElement(new{lease=payload.GetProperty("lease").GetString(),videoView=payload.GetProperty("videoView").GetString()}):
            method=="share"?JsonSerializer.SerializeToElement(new{requested=true}):JsonSerializer.SerializeToElement(new{});
    }
    public async ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)
    {
        Interlocked.Increment(ref ReceiverRequests);
        // Deliberately ignores cancellation to exercise publication and cleanup fencing.
        if(ReceiverGate is { } gate)await gate.Task;
        if(FailReceivers)throw new IOException("Synthetic receiver unavailable");
        var result=new FakeReceiver();Receivers.Enqueue(result);return result;
    }
    public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
}
sealed class FakeReceiver:IDiscordReceiver
{
    internal readonly TaskCompletionSource<DiscordReceiverFailure> FailureSource=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string? Lease;
    internal bool KeepFailurePendingOnDispose;
    public Task<DiscordReceiverFailure> Failure=>FailureSource.Task;
    internal bool Disposed;
    public WebSessionReference Reference{get;}=new(Guid.NewGuid().ToString("N"));
    public void Bind(string lease)=>Lease=lease;
    public ValueTask DisposeAsync(){Disposed=true;if(!KeepFailurePendingOnDispose)FailureSource.TrySetCanceled();return ValueTask.CompletedTask;}
}
