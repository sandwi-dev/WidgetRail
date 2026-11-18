using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static readonly CompiledWidgetIntentContract notificationIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.WebNotification);
    private sealed record MessageNotificationTarget(string Account,string MessageId,DiscordConversation Conversation);

    private static bool IsMessageNotification(WidgetIntentRequest request) => request.IsWellFormed() &&
        request.ContractId==notificationIntent.Id && request.Version==notificationIntent.Version &&
        request.SchemaDigest==notificationIntent.SchemaDigest && notificationIntent.Accepts(request.Payload);

    private bool CanOpenMessageNotification(WidgetIntentRequest request) => IsMessageNotification(request) && IsActive &&
        client?.Reference is {} session && request.Payload.GetProperty("sessionId").GetString()==session.Id;

    private async ValueTask<WidgetIntentResult> OpenMessageNotificationAsync(WidgetIntentRequest request,CancellationToken token)
    {
        if(!CanOpenMessageNotification(request) || client?.Reference is not {} session)
            return WidgetIntentResult.Rejected;
        var visit=navigation.Value;
        WidgetIntentResult BrowserFallback()
        {
            if(CanOpenMessageNotification(request) && client?.Reference==session && navigation.Value==visit)
            {
                // The backend is otherwise parked without a visible BrowserSlot.
                // Present its ordinary route before the host invokes the original
                // source-bound notification callback. Keep the underlying draft,
                // recipient picker and send ownership intact for Back.
                if(visit.Route!=Route.Account)navigation.Push(Route.Account,"discord.account.open");
                ObserveMessages();
            }
            return WidgetIntentResult.Rejected;
        }
        if(CurrentState() is not {Ready:true,Messaging.CanRead:true} data || messages.Value.IncomingFiles || Operations.IsBusy("discord.message.send"))
            return BrowserFallback();
        var tag=request.Payload.GetProperty("tag").GetString();
        try
        {
            // Exact bounded MESSAGE_CREATE association in the already-resident
            // provider. No history lookup, title matching or read acknowledgment.
            var json=await client.InvokeAsync("messageNotificationTarget",new{account=data.Account,tag},token);
            token.ThrowIfCancellationRequested();
            if(json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)return BrowserFallback();
            var target=json.Deserialize<MessageNotificationTarget>(MessageJson);
            if(target is null || target.Account!=data.Account || target.MessageId!=tag || !DiscordMessageValues.Id(target.MessageId) ||
                target.Conversation is not {IsValid:true,ChannelId:not null} || target.Conversation.Kind is not ("dm" or "group") ||
                !MessageAccountCurrent(data.Account,session) || navigation.Value!=visit || !CanOpenMessageNotification(request))
                return BrowserFallback();
            var query=new ConversationQuery(data.Account,session);
            messages.Update(value=>value with{Query=query});
            await OpenConversationAsync(query,target.Conversation,new("notification.open","discord.messages.open"),token,target.MessageId);
            return MessageAccountCurrent(data.Account,session) && navigation.Value.Route==Route.Conversation &&
                messages.Value.InitialReveal?.MessageId==target.MessageId && messages.Value.Selection?.Channel==target.Conversation.ChannelId
                ? WidgetIntentResult.Accepted : BrowserFallback();
        }
        catch(Exception) when(!token.IsCancellationRequested)
        {
            // Unmapped/retired/provider failures retain the existing website
            // activation fallback. Never fabricate a new notification or draft.
            return BrowserFallback();
        }
    }
}
