using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    internal static async Task PrivateCallsAsync(Action<bool,string> check)
    {
        await CallDockStateAsync(check);
        await PrivateCallNotificationsAsync(check);
        var invite=new DiscordCallInvitation(new string('b',32),"20","Synthetic friend");
        static DiscordCalls Ready(params DiscordCallInvitation[] incoming)=>new(true,true,true,true,incoming,[]);
        static MessagesSession Session(DiscordCalls calls)
        {
            var session=new MessagesSession();session.Inner.State=session.Inner.State with{ChannelId=null,Participants=[],Streams=[],VoiceConnection="disconnected",Calls=calls};return session;
        }
        await using(var scenario=await Scenario.OpenAsync(Session(Ready(invite))))
        {
            var session=scenario.Session;
            check(scenario.Find("discord.incoming.answer."+invite.Id)?.Text=="Answer" && scenario.Find("discord.incoming.decline."+invite.Id)?.Text=="Decline",
                "Incoming private calls offer native Answer and Decline controls");
            check(WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,"Incoming call controls satisfy the shared presentation contract");
            check(session.Calls.All(call=>!call.Method.StartsWith("call",StringComparison.Ordinal)),"Reading and rendering incoming calls has no call side effect");
            session.CallEffect=(method,args)=>{session.Inner.State=session.Inner.State with{Calls=Ready()};return Task.CompletedTask;};
            await scenario.Act("discord.incoming.decline."+invite.Id);
            await Wait(()=>scenario.Find("discord.calls.incoming") is null);
            var decline=session.Calls.Single(call=>call.Method=="callDecline").Request;
            check(session.Inner.MicrophoneRequests==0 && decline.GetProperty("invitationId").GetString()==invite.Id &&
                decline.GetProperty("channelId").GetString()=="20","Decline retains invitation authority and does not request microphone access");
        }
        await using(var scenario=await Scenario.OpenAsync(Session(Ready())))
        {
            var session=scenario.Session;await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            check(scenario.Find("discord.message.call")?.Text=="Call","An existing conversation exposes an explicit Call action");
            var callShortcut=scenario.Snapshot().Root.Shortcuts.SingleOrDefault(item=>item.Button==ControllerButton.LeftStick);
            check(callShortcut is {ActionId:"message-focus-call"} && scenario.Find("discord.message.call.hint")?.ControllerPrompt==ControllerPrompt.LeftStickPress,
                "Conversation L3 advertises focus on the call button with a native controller glyph");
            await scenario.Widget.OnActionAsync(new("message-focus-call","test"));
            check(scenario.Snapshot().FocusGroupEntryRequest?.TargetId=="discord.message.call" &&
                session.Inner.MicrophoneRequests==0 && session.Calls.All(call=>call.Method!="callStart"),
                "L3 requests header call focus without microphone permission or ringing");
            check(scenario.Find("discord.call.open")?.Text=="Servers" &&
                scenario.Snapshot().Root.Shortcuts.Any(item=>item.Button==ControllerButton.LeftBumper&&item.Label=="Servers"),
                "Primary section and bumper hint consistently say Servers");
            session.Inner.Microphone=WebSessionMicrophoneState.Denied;
            await scenario.Act("discord.message.call");await Wait(()=>session.Inner.MicrophoneRequests==1 && scenario.Find("discord.message.call")?.IsDisabled!=true);
            check(session.Calls.All(call=>call.Method!="callStart"),"Denied microphone consent never starts a private call");
            session.Inner.Microphone=WebSessionMicrophoneState.Granted;session.Inner.CommandGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await scenario.Act("discord.message.call");await Wait(()=>session.Calls.Any(call=>call.Method=="callStart"));
            await scenario.Act("discord.message.call");
            check(session.Calls.Count(call=>call.Method=="callStart")==1 && scenario.Find("discord.message.call")?.IsDisabled==true,
                "Private call dispatch shares the existing single-flight command guard");
            check(!scenario.Snapshot().Root.Shortcuts.Any(item=>item.Button==ControllerButton.LeftStick) && scenario.Find("discord.message.call.hint") is null,
                "Busy call action does not advertise an unavailable focus shortcut");
            var start=session.Calls.Single(call=>call.Method=="callStart").Request;
            check(start.GetProperty("previousChannelId").ValueKind==JsonValueKind.Null && start.GetProperty("channelId").GetString()=="10" &&
                Guid.TryParseExact(start.GetProperty("commandId").GetString(),"N",out _),"Outgoing dispatch binds its account, previous call, target and unique command identity");
            session.Inner.CommandGate.SetResult();await Wait(()=>scenario.Find("discord.message.call")?.IsDisabled!=true);
            var stale=scenario.Find("discord.message.call")!;await scenario.Act("discord.conversation.20");await scenario.WaitForHistory();
            await scenario.Widget.OnActionAsync(new(stale.ActionId!,stale.Id));
            check(session.Calls.Count(call=>call.Method=="callStart")==1,"A stale Call action cannot ring the newly selected conversation");
        }
        await using(var scenario=await Scenario.OpenAsync(Session(Ready())))
        {
            var session=scenario.Session;await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            await scenario.Widget.OnActionAsync(new("message-focus-call","test"));
            session.Inner.Microphone=WebSessionMicrophoneState.Granted;
            session.CallEffect=(method,args)=>{session.Inner.State=session.Inner.State with{ChannelId="10",VoiceConnection="connected",Calls=Ready() with{Current=new("10","Synthetic friend","active")}};return Task.CompletedTask;};
            await scenario.Act("discord.message.call");await Wait(()=>scenario.Find("discord.call.dock.leave") is not null);
            check(scenario.Find("discord.message.call") is null && scenario.Snapshot().FocusGroupEntryRequest?.TargetId!="discord.message.call" &&
                WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,
                "Starting a call after L3 retires the removed header target without publishing an invalid focus request");
        }
        foreach(var replacement in new[]{"invitation","account","session","current-call"})
        {
            await using var scenario=await Scenario.OpenAsync(Session(Ready(invite)));var session=scenario.Session;
            session.Inner.MicrophoneGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await scenario.Act("discord.incoming.answer."+invite.Id);await Wait(()=>session.Inner.MicrophoneRequests==1);
            session.Inner.State=replacement switch
            {
                "invitation"=>session.Inner.State with{Calls=Ready(invite with{Id=new string('c',32)})},
                "account"=>session.Inner.State with{Account="2"},
                "current-call"=>session.Inner.State with{ChannelId="30"},
                _=>session.Inner.State
            };
            if(replacement=="session")session.Inner.Reference=new(new string('d',32));
            await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
            session.Inner.MicrophoneGate.SetResult();
            await Wait(()=>Nodes(scenario.Snapshot().Root).Any(node=>node.Id.StartsWith("discord.incoming.answer.",StringComparison.Ordinal)&&node.IsDisabled!=true));
            // Allow the existing command's final refresh to finish before observing dispatch counts.
            await scenario.Widget.WaitForStateReadAsync();
            check(session.Calls.All(call=>call.Method!="callAnswer"),"Microphone completion cannot answer after replacement of "+replacement);
        }
        await using(var scenario=await Scenario.OpenAsync(Session(Ready(invite))))
        {
            var session=scenario.Session;
            session.CallEffect=(method,args)=>{session.Inner.State=session.Inner.State with{ChannelId="20",VoiceConnection="connected",Calls=Ready() with{Current=new("20","Synthetic friend","active")}};return Task.CompletedTask;};
            await scenario.Act("discord.incoming.answer."+invite.Id);await Wait(()=>scenario.Find("discord.main.leave") is not null);
            check(session.Inner.MicrophoneRequests==1 && session.Calls.Count(call=>call.Method=="callAnswer")==1 &&
                session.Calls.All(call=>call.Method!="callStart"),"Answer uses microphone consent then joins the observed invitation and shows the existing call controls");
            check(WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,"Answered private call reuses the existing voice/share/pin interface");
            session.Inner.State=session.Inner.State with{Calls=Ready() with{Current=new("20","Synthetic friend","unavailable")}};
            await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
            check(Nodes(scenario.Snapshot().Root).Any(node=>node.AccessibilityLabel?.StartsWith("Call unavailable",StringComparison.Ordinal)==true),
                "An unavailable private call is not presented as a connected voice session");
        }
        var invalid=Ready(invite,invite);
        check(!invalid.IsValid(null) && !Ready(invite).IsValid("20") && !(Ready() with{Current=new("20","Call","ringing-guess")}).IsValid("20"),
            "Private call validation rejects duplicate invitations, already-joined invitations and unknown phases");
    }
}
