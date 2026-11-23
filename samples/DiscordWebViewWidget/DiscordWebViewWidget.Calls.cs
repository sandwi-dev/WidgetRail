using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static string PrivateCallAction(string method,DiscordState data,WebSessionReference? session,string channel,string? invitation=null)=>
        DomainAction(method,session?.Id,data.Account,data.ChannelId,channel,invitation);

    private bool TryPrivateCallAction(WidgetActionEvent action,CancellationToken token)
    {
        var data=CurrentState();var session=client?.Reference;
        if(data?.Calls is not {Available:true} calls || session is null)return false;
        foreach(var invitation in calls.Incoming)
        {
            foreach(var method in new[]{"callAnswer","callDecline"})
            {
                if(action.ActionId!=PrivateCallAction(method,data,session,invitation.ChannelId,invitation.Id))continue;
                if(method=="callAnswer"?calls.CanAnswer:calls.CanDecline)
                    PrivateCallCommand(method,data,session,invitation.ChannelId,invitation.Id,token);
                return true;
            }
        }
        if(navigation.Value.Route==Route.Conversation && messages.Value.Selection is {} selected && MessageSelectionCurrent(selected) &&
            action.ActionId==PrivateCallAction("callStart",data,session,selected.Channel))
        {
            if(calls.CanStart && data.ChannelId!=selected.Channel)PrivateCallCommand("callStart",data,session,selected.Channel,null,token);
            return true;
        }
        return false;
    }

    private void PrivateCallCommand(string method,DiscordState before,WebSessionReference session,string target,string? invitation,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(Operations.IsBusy("discord.command"))return;
        var origin=navigation.Value;
        bool Current(DiscordState? data)=>client?.Reference==session && data is {Ready:true,Calls.Available:true} &&
            data.Account==before.Account && data.ChannelId==before.ChannelId &&
            (method=="callStart"?data.Calls.CanStart && messages.Value.Selection?.Channel==target && ReferenceEquals(origin,navigation.Value):
                data.Calls.Incoming.Any(call=>call.Id==invitation&&call.ChannelId==target) &&
                (method=="callAnswer"?data.Calls.CanAnswer:data.Calls.CanDecline));
        Operations.RunSingleFlight("discord.command",async operation=>
        {
            try
            {
                if(!Current(CurrentState()))return;
                if(method!="callDecline")
                {
                    var permission=await client!.RequestMicrophoneAsync(operation.CancellationToken);
                    if(permission.State!=WebSessionMicrophoneState.Granted)
                    {
                        if(operation.IsCurrent && Current(CurrentState()) && permission.State!=WebSessionMicrophoneState.Cancelled)
                            PublishFeedback("Microphone access was not granted. The call was not joined.");
                        return;
                    }
                }
                if(!operation.IsCurrent || !Current(CurrentState()))return;
                await client!.InvokeAsync(method,new{account=before.Account,channelId=target,previousChannelId=before.ChannelId,
                    invitationId=invitation,commandId=Guid.NewGuid().ToString("N")},operation.CancellationToken);
            }
            catch(Exception)when(!operation.CancellationToken.IsCancellationRequested)
            {
                // Dispatch can succeed before a reply is lost. Refresh the
                // client's actual state; never repeat a ring automatically.
                if(operation.IsCurrent && client?.Reference==session)
                    PublishFeedback("Discord could not confirm the call action. Check the current call before trying again.");
            }
            finally
            {
                if(LifecycleState!=WidgetLifecycleState.Destroying)await state.Refresh().Completion;
            }
            if(method=="callAnswer" && operation.IsCurrent && client?.Reference==session &&
                CurrentState() is {} joined && joined.Account==before.Account && joined.ChannelId==target && ReferenceEquals(origin,navigation.Value))
                OpenCallSection();
        },WidgetOperationLifetime.Widget);
    }

    private WidgetElement IncomingCalls(DiscordState data,bool busy)
    {
        var calls=data.Calls!;
        return UI.VerticalScroll("discord.calls.incoming",calls.Incoming.Select(call=>
            UI.Row("discord.incoming."+call.Id,
                UI.Stack("discord.incoming.copy."+call.Id,
                    UI.Text("Incoming call","discord.incoming.label."+call.Id).Classes("discord-secondary"),
                    UI.Text(call.Name,"discord.incoming.name."+call.Id).Classes("discord-row-title")).Classes("discord-copy"),
                UI.Button(data.ChannelId is null?"Answer":"Switch & answer",PrivateCallAction("callAnswer",data,client?.Reference,call.ChannelId,call.Id),"discord.incoming.answer."+call.Id)
                    .Icon(DiscordIcons.Voice).Disabled(busy||!calls.CanAnswer).Classes("discord-control","discord-primary"),
                UI.Button("Decline",PrivateCallAction("callDecline",data,client?.Reference,call.ChannelId,call.Id),"discord.incoming.decline."+call.Id)
                    .Icon(DiscordIcons.Leave).Disabled(busy||!calls.CanDecline).Classes("discord-control","discord-leave"))
                .Classes("discord-actions","discord-call-panel")).ToArray()).Classes("discord-incoming-calls");
    }

    private ButtonElement? ConversationCallButton(DiscordState data,MessageSelection selected)
    {
        if(data.Calls is not {Available:true} calls || data.ChannelId==selected.Channel)return null;
        var invitation=calls.Incoming.FirstOrDefault(call=>call.ChannelId==selected.Channel);
        if(invitation is null&&!calls.CanStart)return null;
        var label=invitation is not null?(data.ChannelId is null?"Answer":"Switch & answer"):
            calls.ActiveChannels.Contains(selected.Channel)?(data.ChannelId is null?"Join call":"Switch & join"):data.ChannelId is null?"Call":"Switch & call";
        return UI.Button(label,PrivateCallAction(invitation is null?"callStart":"callAnswer",data,client?.Reference,selected.Channel,invitation?.Id),"discord.message.call")
            .Icon(DiscordIcons.Voice).Disabled(Operations.IsBusy("discord.command")||invitation is not null&&!calls.CanAnswer).Classes("discord-control","discord-primary");
    }
}
