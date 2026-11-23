using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    internal static WidgetIntentContract OpenCallContract { get; }=new("community.widgetrail.discord.call.open",1,
        JsonDocument.Parse("""{"type":"object","additionalProperties":false,"properties":{"sessionId":{"type":"string","minLength":32,"maxLength":32},"account":{"type":"string","minLength":1,"maxLength":32},"invitationId":{"type":"string","minLength":32,"maxLength":32}},"required":["sessionId","account","invitationId"]}""").RootElement.Clone());
    private static readonly CompiledWidgetIntentContract openCallContract=CompiledWidgetIntentContract.Create(OpenCallContract);
    private readonly WidgetNotificationPublisher callNotifications;
    private readonly object callNotificationGate=new();
    private sealed record CallAlert(string Account,WebSessionReference Session,DiscordCallInvitation Invitation,int Count,string Membership);
    private sealed record CallAlertFocus(WidgetNavigationSnapshot<Route> Visit,CallAlert Alert,FocusGroupEntryRequest Request);
    private sealed record CallUi(CallAlertFocus? Focus=null);
    private readonly WidgetModel<CallUi> callUi;
    private CallAlert? announcedCall;

    private void ReconcileCallAlert(DiscordState? data)
    {
        lock(callNotificationGate)
        {
            var session=client?.Reference;
            var calls=data is {Ready:true,Calls.Available:true}?data.Calls.Incoming:[];
            var previous=announcedCall;
            var sameOwner=previous is not null&&data?.Account==previous.Account&&session==previous.Session;
            var oldIds=sameOwner?previous!.Membership.Split('.'):[];
            var fresh=calls.FirstOrDefault(call=>!oldIds.Contains(call.Id,StringComparer.Ordinal));
            var selected=fresh??calls.FirstOrDefault(call=>sameOwner&&call.Id==previous!.Invitation.Id)??calls.FirstOrDefault();
            CallAlert? next=selected is null||session is null?null:new(data!.Account,session,selected,calls.Length,CallMembership(calls));
            if(next==previous)return;
            announcedCall=next;
            if(next is null){callNotifications.Dismiss();return;}
            var sameEvent=sameOwner&&fresh is null&&previous!.Invitation.Id==next.Invitation.Id;
            callNotifications.Publish(new("discord-incoming-call",next.Count>1?"Incoming calls":"Incoming call")
            {
                CategoryId="calls",Body=next.Invitation.Name,Metadata=next.Count>1?$"{next.Count} calls":"",
                Visibility=WidgetNotificationVisibility.SourceHidden,DurationMilliseconds=6000,
                OpenIntent=WidgetIntentRequest.Create(OpenCallContract,JsonSerializer.SerializeToElement(new
                    {sessionId=next.Session.Id,account=next.Account,invitationId=next.Invitation.Id}),WidgetIntentPresentation.OpenWidget)
            },updateOnly:sameEvent);
        }
    }

    private static string CallMembership(IEnumerable<DiscordCallInvitation> calls)=>string.Join('.',calls.Select(call=>call.Id).Order(StringComparer.Ordinal));

    private static bool IsCallNotification(WidgetIntentRequest request)=>request.IsWellFormed()&&request.ContractId==openCallContract.Id&&
        request.Version==openCallContract.Version&&request.SchemaDigest==openCallContract.SchemaDigest&&openCallContract.Accepts(request.Payload);

    private bool CallNotificationOwnerCurrent(WidgetIntentRequest request)=>IsActive&&client?.Reference?.Id==request.Payload.GetProperty("sessionId").GetString()&&
        CurrentState()?.Account==request.Payload.GetProperty("account").GetString();

    private async ValueTask<WidgetIntentResult> OpenCallNotificationAsync(WidgetIntentRequest request,CancellationToken token)
    {
        if(!CallNotificationOwnerCurrent(request))return WidgetIntentResult.Rejected;
        var visit=navigation.Value;
        await state.Refresh().Completion.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if(!CallNotificationOwnerCurrent(request)||!ReferenceEquals(visit,navigation.Value)||CurrentState()?.Calls is not {Available:true} calls)
            return WidgetIntentResult.Rejected;
        var invitation=calls.Incoming.FirstOrDefault(call=>call.Id==request.Payload.GetProperty("invitationId").GetString());
        if(invitation is null)return WidgetIntentResult.Rejected;
        // Navigation only: never answer, grant microphone access, or discard an
        // incoming file offer/draft when the user opens a call notification.
        navigation.NavigateRoot(Route.Home);ObserveMessages();
        var alert=new CallAlert(CurrentState()!.Account,client!.Reference!,invitation,calls.Incoming.Length,CallMembership(calls.Incoming));
        var actionable=!Operations.IsBusy("discord.command")&&(calls.CanAnswer||calls.CanDecline);
        var target=actionable?(calls.CanAnswer?"discord.incoming.answer.":"discord.incoming.decline.")+invitation.Id:"discord.account.open";
        callUi.Update(_=>new(new(navigation.Value,alert,CreateFocusRequest().FocusElement(actionable?"discord.calls.incoming":"discord.shell",target))));
        return WidgetIntentResult.Accepted;
    }

    private WidgetView ApplyCallNotificationFocus(WidgetView view,DiscordState? data)
    {
        var focus=callUi.Value.Focus;
        return IsActive&&focus is not null&&ReferenceEquals(focus.Visit,navigation.Value)&&client?.Reference==focus.Alert.Session&&
            data?.Account==focus.Alert.Account&&data.Calls?.Incoming.Any(call=>call.Id==focus.Alert.Invitation.Id)==true
            ?view with{FocusGroupEntryRequest=focus.Request}:view;
    }
}
