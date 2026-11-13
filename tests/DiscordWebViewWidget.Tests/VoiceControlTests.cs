using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class VoiceControlTests
{
    private const string Dialog="discord.voice-dialog";
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await ConnectionStatus(check);
        await ParticipantControls(check);
        await DeviceControls(check);
        await StaleDialogs(check);
        await DelayedDeviceReads(check);
        await DeviceWriteReconciliation(check);
    }

    private static async Task ConnectionStatus(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        foreach(var (phase,label) in new[]{("connecting","Connecting to voice…"),("connected","Voice connected"),
            ("reconnecting","Reconnecting to voice…"),("disconnected","Voice disconnected"),("unavailable","Voice status unavailable")})
        {
            test.Session.Core.State=test.Session.Core.State with{VoiceConnection=phase};await test.Refresh();
            check(test.Node("discord.main.connected").Text==label,"Voice heading reports observed RTC phase: "+phase);
            check(test.Node("discord.main.connected").StyleClasses.Contains(phase=="connected"?"discord-connected":"discord-connecting"),
                "Only an observed connected voice call uses connected emphasis: "+phase);
        }
        check(test.Session.Commands.IsEmpty&&test.Session.Core.MicrophoneRequests==0,"Reading connection status has no call or permission side effects");
    }

    private static async Task ParticipantControls(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        using(var host=WidgetTestHost.CreateIndexedCollectionHost(test.Widget,"voice-self"))
        using(var rows=await host.AcquireAsync("discord.participants",0,1))
            check(Nodes(rows.Range.Items.Single().Root).All(node=>node.ActionId!="voice-audio"),"Self participant does not expose other-person voice controls");
        var original=test.Session.Core.State;
        await test.OpenParticipant();
        var snapshot=test.Snapshot();var slider=test.Node(Dialog+".volume");
        check(snapshot.Root.Kind==ViewNodeKind.ModalLayer&&snapshot.ActiveInputScopeId=="discord.participant-dialog.scope"&&
            snapshot.InitialFocusId==Dialog+".mute","Selecting a participant opens a native modal with initial local-mute focus");
        check(slider.Kind==ViewNodeKind.Slider&&slider.Minimum==0&&slider.Maximum==100&&slider.Step==5&&slider.Value==55&&
            slider.SliderInteractionMode==SliderInteractionMode.ActivateToAdjust,"Participant voice volume uses a bounded host-adjusted slider");
        check(WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0&&
            snapshot.PinnedLayouts.All(pin=>pin.Root?.Kind!=ViewNodeKind.ModalLayer),"Voice modal validates and does not leak into either pin");
        var mute=test.Node(Dialog+".mute").ActionId!;
        await test.ActControl(Dialog+".mute");
        check(test.Node(Dialog+".mute").Text=="Unmute for me"&&test.Session.Commands.Last().Method=="voiceMute",
            "Participant mute uses the voice-specific acknowledged command");
        var count=test.Session.Commands.Count;await test.Act(mute);
        check(test.Session.Commands.Count==count,"An old participant mute action cannot toggle a newer observed state");
        await test.ActControl(Dialog+".volume",35);
        check(test.Node(Dialog+".volume.value").Text=="35%"&&test.Node(Dialog+".mute").Text=="Unmute for me",
            "Voice-volume acknowledgment updates immediately without clearing local mute");
        check(test.Session.Core.State.Muted==original.Muted&&test.Session.Core.State.Deafened==original.Deafened&&
            test.Session.Core.State.Streams.SequenceEqual(original.Streams),"Participant audio changes neither self audio nor stream audio");
        test.Session.Core.State=test.Session.Core.State with{Deafened=true};await test.Refresh();
        check(test.Node(Dialog+".deafened").Text!.Contains("Undeafen"),"Participant controls explain silence while self-deafened");
        await test.Close();
        check(test.Snapshot().FocusGroupEntryRequest is {RestoreViewport:true,IndexedItem:{CollectionId:"discord.participants",ItemKey:"participant.30",Index:1}},
            "Closing participant audio restores the exact indexed participant and viewport through SDK navigation");
        test.Session.Core.State=test.Session.Core.State with{Participants=test.Session.Core.State.Participants.Select(person=>person with{Audio=null}).ToArray()};await test.Refresh();
        await test.OpenParticipant();
        check(test.Find(Dialog+".volume") is null&&test.Node(Dialog+".unavailable").Text!.Contains("unavailable"),
            "Missing participant audio support presents a clear message without fake controls");
    }

    private static async Task DeviceControls(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        await test.OpenSettings();var snapshot=test.Snapshot();
        check(snapshot.Root.Kind==ViewNodeKind.ModalLayer&&snapshot.ActiveInputScopeId=="discord.voice-settings-dialog.scope"&&
            WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0,"Menu Voice settings uses a native modal and existing host controls");
        check(test.Node(Dialog+".input").SelectOptions!.Count==2&&test.Node(Dialog+".output").SelectOptions!.Count==2&&
            test.Find(Dialog+".input-volume") is null,"Available devices render as selects while unsupported WebRTC input gain has no misleading slider");
        check(test.Node(Dialog+".input-volume.unavailable").Text!.Contains("unavailable")&&test.Session.Core.MicrophoneRequests==0,
            "Device settings do not request microphone access or imply input gain works");
        var oldOutput=test.Node(Dialog+".output").SelectOptions![1].ActionId;
        await test.Act(test.Node(Dialog+".input").SelectOptions![1].ActionId);
        check(test.Session.Settings.InputDeviceId=="mic-b"&&test.Node(Dialog+".input").SelectOptions![1].IsSelected,
            "Native microphone selection publishes the acknowledged provider device");
        await test.Act(oldOutput);
        check(test.Session.Settings.OutputDeviceId=="speaker-b"&&test.Session.Commands.Last().Method=="voiceOutputDevice",
            "Output choices remain bound to their own observed device list and selection");
        var stale=test.Node(Dialog+".input").SelectOptions![0].ActionId;
        test.Session.Settings=test.Session.Settings with{InputDevices=[new("mic-b","Second microphone")],InputDeviceId="mic-b"};
        await test.ActControl(Dialog+".refresh");await test.Widget.WaitForVoiceSettingsAsync();
        var count=test.Session.Commands.Count;await test.Act(stale);
        check(test.Session.Commands.Count==count&&test.Node(Dialog+".input").SelectOptions!.Count==1,
            "Refreshing devices invalidates removed native Select choices without retargeting them");
        await test.Close();check(test.Snapshot().Root.Kind!=ViewNodeKind.ModalLayer,"B closes voice settings through the matching navigator scope");
        test.Session.Settings=new(false,false,false,[],[],null,null,null);await test.OpenSettings();
        check(test.Find(Dialog+".input") is null&&test.Find(Dialog+".output") is null&&
            test.Find(Dialog+".input.unavailable") is not null&&test.Find(Dialog+".open") is not null,
            "Unavailable device APIs preserve explicit Open Discord and Refresh recovery");
        await test.ActControl(Dialog+".open");
        check(test.Find("discord.account") is not null&&test.Snapshot().Root.Kind!=ViewNodeKind.ModalLayer,
            "Open Discord leaves the device modal and uses the existing account page");
    }

    private static async Task StaleDialogs(Action<bool,string> check)
    {
        foreach(var change in new[]{"account","call","session","participant","reopen"})
        {
            await using var test=await Harness.Create();await test.OpenParticipant();
            var oldAction=test.Node(Dialog+".volume").ValueChangedActionId!;
            test.Session.HoldMethod="voiceVolume";
            var pending=test.Act(oldAction,20).AsTask();await test.Session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch(change)
            {
                case "account":test.Session.Core.State=test.Session.Core.State with{Account="2"};await test.Refresh();break;
                case "call":test.Session.Core.State=test.Session.Core.State with{ChannelId="20"};await test.Refresh();break;
                case "session":test.Session.Core.Reference=new(new string('c',32));await test.Refresh();break;
                case "participant":test.Session.Core.State=test.Session.Core.State with{Participants=[test.Session.Core.State.Participants[0]]};await test.Refresh();break;
                case "reopen":await test.Close();await test.OpenParticipant();break;
            }
            test.Session.Gate.TrySetResult();await pending.WaitAsync(TimeSpan.FromSeconds(5));
            check(change=="reopen"?test.Node(Dialog+".volume.value").Text=="55%":test.Snapshot().Root.Kind!=ViewNodeKind.ModalLayer,
                "Late participant acknowledgment cannot replace a newer view after "+change);
            var count=test.Session.Commands.Count;await test.Act(oldAction,80);
            check(test.Session.Commands.Count==count,"Stale participant action is rejected after "+change);
        }
    }

    private static async Task DelayedDeviceReads(Action<bool,string> check)
    {
        await using var test=await Harness.Create();
        test.Session.HoldMethod="voiceSettings";
        await test.OpenSettings(wait:false);await test.Session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        check(test.Node(Dialog+".status").Text=="Loading voice settings…","Voice settings expose the SDK resource loading state");
        var staleClose=test.DismissAction();await test.Close();
        test.Session.Settings=new(false,false,false,[],[],null,null,null);
        await test.OpenSettings(wait:false);test.Session.Gate.TrySetResult();
        await test.Widget.WaitForVoiceSettingsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(test.Find(Dialog+".input") is null&&test.Find(Dialog+".input.unavailable") is not null,
            "A device read that ignores cancellation cannot populate a later dialog visit");
        await test.Act(staleClose);
        check(test.Snapshot().Root.Kind==ViewNodeKind.ModalLayer,"A delayed old dialog dismiss cannot close the new visit");
    }

    private static async Task DeviceWriteReconciliation(Action<bool,string> check)
    {
        await using var test=await Harness.Create();await test.OpenSettings();
        test.Session.HoldMethod="voiceInputDevice";
        var pending=test.Act(test.Node(Dialog+".input").SelectOptions![1].ActionId).AsTask();
        await test.Session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The external write happened, but its full-state reply is delayed.
        // A later read observes another device change before that reply arrives.
        test.Session.Settings=test.Session.Settings with{OutputDeviceId="speaker-b"};
        await test.ActControl(Dialog+".refresh");await test.Widget.WaitForVoiceSettingsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        test.Session.Gate.TrySetResult();await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await test.Widget.WaitForVoiceSettingsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(test.Node(Dialog+".output").SelectOptions![1].IsSelected&&test.Node(Dialog+".input").SelectOptions![1].IsSelected,
            "A late device write acknowledgment cannot replace newer device inventory or selection");
        check(test.Session.Commands.Count(command=>command.Method=="voiceInputDevice")==1&&
            test.Session.Commands.Count(command=>command.Method=="voiceSettings")==3,
            "Concurrent device observation reconciles with one read and never replays the external write");
    }

    private sealed class Harness:IAsyncDisposable
    {
        internal VoiceSession Session=new();internal DiscordWebViewWidget Widget=null!;
        internal static async Task<Harness> Create()
        {
            var test=new Harness();test.Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(test.Session,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.InitializeAsync(test.Widget);await WidgetTestHost.SetLifecycleStateAsync(test.Widget,WidgetLifecycleState.Interactive);
            await test.Widget.WaitForStateReadAsync();return test;
        }
        internal ViewSnapshot Snapshot()=>Widget.Render().CreateSnapshot("voice.controls",1);
        internal ViewNode? Find(string id)=>Nodes(Snapshot().Root).FirstOrDefault(node=>node.Id==id);
        internal ViewNode Node(string id)=>Find(id)??throw new Exception("Missing node: "+id);
        internal ValueTask Act(string action,double? value=null)
        {var snapshot=Snapshot();return Widget.OnActionAsync(new(action,"voice-test",InputScopeId:snapshot.ActiveInputScopeId){RequestedValue=value});}
        internal ValueTask ActControl(string id,double? value=null){var node=Node(id);return Act(node.ValueChangedActionId??node.ActionId??throw new Exception("No action on "+id),value);}
        internal string DismissAction()=>Nodes(Snapshot().Root).SelectMany(node=>node.Shortcuts).Single(shortcut=>shortcut.Button==ControllerButton.B).ActionId;
        internal ValueTask Close()=>Act(DismissAction());
        internal async Task OpenSettings(bool wait=true)
        {
            await Act(Snapshot().Root.ContextActions.Single(action=>action.Label=="Voice settings").ActionId);
            if(wait)await Widget.WaitForVoiceSettingsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal async Task OpenParticipant()
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(Widget,"voice-participant."+Guid.NewGuid().ToString("N"));
            using var rows=await host.AcquireAsync("discord.participants",1,1);rows.RouteAction("participant.30",ControllerButton.A);
            await Until(()=>Snapshot().Root.Kind==ViewNodeKind.ModalLayer);
        }
        internal async Task Refresh(){await Act("refresh");await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));}
        public async ValueTask DisposeAsync(){Session.Gate.TrySetResult();await WidgetTestHost.DestroyAsync(Widget);}
    }

    private sealed class VoiceSession:IDiscordSession
    {
        internal readonly FakeSession Core=new();
        internal readonly ConcurrentQueue<FakeSession.Command> Commands=new();
        internal string? HoldMethod;
        internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DiscordVoiceSettings Settings=new(true,true,false,[new("mic-a","Microphone A"),new("mic-b","Microphone B")],
            [new("speaker-a","Speakers A"),new("speaker-b","Speakers B")],"mic-a","speaker-a",null);
        internal VoiceSession()=>Core.State=Core.State with{Participants=Core.State.Participants.Select(person=>person.Id=="30"?person with{Audio=new(true,false,55)}:person).ToArray()};
        public WebSessionReference? Reference=>Core.Reference;
        public ValueTask<DiscordState> ReadAsync(CancellationToken token)=>Core.ReadAsync(token);
        public ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)=>Core.OpenReceiverAsync(token);
        public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)=>Core.RequestMicrophoneAsync(token);
        public ValueTask DisposeAsync()=>Core.DisposeAsync();
        public async ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();var input=JsonSerializer.SerializeToElement(args);Commands.Enqueue(new(method,input));
            if(input.GetProperty("account").GetString()!=Core.State.Account)throw new InvalidDataException("Wrong account");
            JsonElement result;
            if(method is "voiceMute" or "voiceVolume")
            {
                if(input.GetProperty("channelId").GetString()!=Core.State.ChannelId)throw new InvalidDataException("Wrong call");
                var user=input.GetProperty("userId").GetString();var person=Core.State.Participants.Single(person=>person.Id==user);
                var audio=method=="voiceMute"?person.Audio! with{Muted=input.GetProperty("value").GetBoolean()}:person.Audio! with{Volume=input.GetProperty("value").GetDouble()};
                result=JsonSerializer.SerializeToElement(new{audio},DiscordWebSessionClient.Json);
                if(HoldMethod!=method)Core.State=Core.State with{Participants=Core.State.Participants.Select(item=>item.Id==user?item with{Audio=audio}:item).ToArray()};
            }
            else
            {
                Settings=method switch{"voiceInputDevice"=>Settings with{InputDeviceId=input.GetProperty("value").GetString()},
                    "voiceOutputDevice"=>Settings with{OutputDeviceId=input.GetProperty("value").GetString()},
                    "voiceInputVolume"=>Settings with{InputVolume=input.GetProperty("value").GetDouble()},_=>Settings};
                result=JsonSerializer.SerializeToElement(Settings,DiscordWebSessionClient.Json);
            }
            if(method==HoldMethod){HoldMethod=null;Started.TrySetResult();await Gate.Task;}
            return result;
        }
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Until(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!condition())await Task.Delay(10,timeout.Token);}
}
