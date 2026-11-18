using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private async ValueTask LoadMessageBoundaryAsync(string channel,WidgetCollectionBoundaryDemand demand,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!IsActive || navigation.Value.Route!=Route.Conversation || messages.Value.Selection is not {} selected ||
            selected.Channel!=channel || !MessageSelectionCurrent(selected) || history.IsBusy)return;
        var page=history.Snapshot;
        if(page.Error is not null || !(demand.Direction==WidgetCursorDirection.Before?page.HasBefore:page.HasAfter))return;
        messages.Update(value=>value with{InitialReveal=null,HistoryAnchor=null});
        await history.Prefetch(demand,"discord.message.history").Completion.WaitAsync(token);
    }
    private ValueTask MessageViewportChanged(string channel,WidgetCollectionViewportState viewport,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!viewport.FollowsEnd||viewport.Recovering||!IsActive||navigation.Value.Route!=Route.Conversation||
            messages.Value.Selection is not {} selected||selected.Channel!=channel||!MessageSelectionCurrent(selected))return ValueTask.CompletedTask;
        // Consume any bounded tail deferred while reading. The host can recover
        // the exact old anchor if a new native scroll races this observation.
        var result=history.ApplyMutations(history.MutationScope,[],allowTailEviction:true);
        if(result.Applied)messages.Update(value=>value.Selection==selected?value with{NewMessages=result.HasUnseenItems||history.Snapshot.After is not null}:value);
        return ValueTask.CompletedTask;
    }

    // Host calls this only for an evicted visible anchor in the same semantic
    // conversation. Ordinary gateway updates remain local and never use this GET.
    private async ValueTask<WidgetViewportRecoveryResult> RecoverMessageViewportAsync(string channel,WidgetViewportRecovery request,CancellationToken token)
    {
        if(messages.Value.Selection is not { } selected || selected.Channel!=channel || !MessageSelectionCurrent(selected) ||
            !DiscordMessageValues.Id(request.AnchorKey.Value))return WidgetViewportRecoveryResult.NotFound;
        using var scope=history.CaptureRecovery();
        var json=await client!.InvokeAsync("messageHistory",new{account=selected.Account,channelId=selected.Channel,
            around=request.AnchorKey.Value,limit=40},token);
        token.ThrowIfCancellationRequested();
        var result=json.Deserialize<DiscordMessagePage>(MessageJson)??throw DiscordMessageValues.Invalid();
        result.Validate(selected.Account,selected.Channel,null,40,around:request.AnchorKey.Value);
        if(!MessageSelectionCurrent(selected))return WidgetViewportRecoveryResult.NotFound;
        var page=new WidgetCursorPage<DiscordMessage>(result.Items,
            result.Context.NextBefore is {} before?new(before):null,
            result.Context.NextAfter is {} after?new(after):null);
        return MessageSelectionCurrent(selected)&&request.TryCommit(history,scope,page)
            ?WidgetViewportRecoveryResult.Recovered:WidgetViewportRecoveryResult.NotFound;
    }
}
