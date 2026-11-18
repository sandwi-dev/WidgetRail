using System.Numerics;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private MessageSelection? liveMessageSelection;
    private WidgetCursorMutationScope? liveMessageScope;
    private long liveWatchEpoch, liveMessageRevision;
    private bool liveRecoveryPending, liveRecoveryBlocked;
    private bool liveFreshLoadExpected;
    private WidgetOperationHandle? messageObservation;

    private void ResetMessageUpdates(bool freshLoadExpected=true)
    {
        Operations.Cancel("discord.message.changes");
        liveMessageSelection=null;liveMessageScope=null;liveWatchEpoch=liveMessageRevision=0;
        liveRecoveryPending=liveRecoveryBlocked=false;
        liveFreshLoadExpected=freshLoadExpected;
    }

    private void ReconcileMessageUpdates(DiscordMessagingState state)
    {
        if(navigation.Value.Route!=Route.Conversation || messages.Value.Selection is not {} selected ||
            !MessageSelectionCurrent(selected) || state.ObservedChannelId!=selected.Channel || state.WatchEpoch==0)return;
        if(liveMessageSelection!=selected || liveWatchEpoch!=state.WatchEpoch)
        {
            Operations.Cancel("discord.message.changes");
            liveMessageSelection=selected;liveMessageScope=history.MutationScope;
            liveWatchEpoch=state.WatchEpoch;liveMessageRevision=state.ObservedSinceRevision;
            liveRecoveryPending=!liveFreshLoadExpected && history.Snapshot.Status==WidgetPagedResourceStatus.Ready && history.Snapshot.Items.Count>0;
            liveFreshLoadExpected=false;
            liveRecoveryBlocked=false;
        }
        if(liveRecoveryBlocked || !liveRecoveryPending && state.MessageRevision<=liveMessageRevision)return;
        var scope=liveMessageScope!;var epoch=liveWatchEpoch;
        Operations.RunSingleFlight("discord.message.changes",async context=>
        {
            bool Current()=>context.IsCurrent && MessageSelectionCurrent(selected) && liveMessageSelection==selected &&
                ReferenceEquals(liveMessageScope,scope) && liveWatchEpoch==epoch && navigation.Value.Route==Route.Conversation;
            try
            {
                if(liveRecoveryPending)
                {
                    liveRecoveryPending=false;
                    if(!await RecoverWindow())return;
                }
                // The provider journal is 128 entries; three bounded local reads
                // drain one observed burst. Later bursts use the existing invalidation.
                for(var pageIndex=0;pageIndex<3 && Current();pageIndex++)
                {
                    var after=liveMessageRevision;
                    var json=await client!.InvokeAsync("messageChanges",new{account=selected.Account,channelId=selected.Channel,
                        watchEpoch=epoch,afterRevision=after,limit=50},context.CancellationToken);
                    var batch=json.Deserialize<DiscordMessageChanges>(MessageJson)??throw DiscordMessageValues.Invalid();
                    batch.Validate(selected.Account,selected.Channel,epoch,after);
                    if(!Current())return;
                    if(batch.Context.ResetRequired)
                    {
                        if(!await RecoverWindow())return;
                        continue;
                    }
                    var follows=messageRows.ViewportState.FollowsEnd;
                    var last=history.Snapshot.Items.LastOrDefault();
                    var newTail=batch.Items.Any(change=>change.Message is {} message&&(last is null||MessageLiveOptions.Compare(message,last)>0));
                    var result=history.ApplyMutations(scope,batch.Items.Select(change=>new WidgetCursorMutation<DiscordMessage>(
                        new(change.Id),change.Message,change.Kind=="remove")).ToArray(),allowTailEviction:follows);
                    if(!result.Applied)
                    {
                        if(result.RequiresRefresh){liveRecoveryBlocked=true;messages.Update(value=>value with{NewMessages=true});}
                        return;
                    }
                    liveMessageRevision=batch.Context.Revision;
                    messages.Update(value=>value.Selection==selected?value with{NewMessages=result.HasUnseenItems || history.Snapshot.After is not null||!follows&&(value.NewMessages||newTail)}:value);
                    if(!batch.Context.HasMore)return;
                }
            }
            catch(Exception) when(!context.CancellationToken.IsCancellationRequested)
            {
                // No HTTP retry loop. A later existing provider invalidation/state
                // read can retry this idempotent local revision read.
                if(Current())messages.Update(value=>value with{NewMessages=true});
            }

            async Task<bool> RecoverWindow()
            {
                // Recovery is exceptional, bounded, and preserves the retained
                // reading interval. A failed/partial response never means delete.
                var retained=history.Snapshot;
                if(retained.Items.Count==0)
                {
                    liveRecoveryBlocked=true;
                    if(Current())messages.Update(value=>value with{NewMessages=true});
                    return false;
                }
                liveRecoveryBlocked=true;
                var keys=retained.Items.Select(item=>item.Id).ToArray();
                var json=await client!.InvokeAsync("messageRecover",new{account=selected.Account,channelId=selected.Channel,
                    watchEpoch=epoch,ids=keys},context.CancellationToken);
                var recovered=json.Deserialize<DiscordMessageRecovery>(MessageJson)??throw DiscordMessageValues.Invalid();
                recovered.Validate(selected.Account,selected.Channel,epoch,keys);
                if(!Current())return false;
                var result=history.ApplyMutations(scope,recovered.Items.Select(change=>new WidgetCursorMutation<DiscordMessage>(
                    new(change.Id),change.Message,change.Kind=="remove")).ToArray(),boundaries:new(retained.Before,new(recovered.Context.After)));
                if(!result.Applied)return false;
                liveMessageRevision=recovered.Context.Revision;
                liveRecoveryBlocked=!recovered.Context.Complete;
                messages.Update(value=>value.Selection==selected?value with{NewMessages=true}:value);
                return !liveRecoveryBlocked;
            }
        });
    }

    private void ApplySentMessage(MessageSelection selected,DiscordMessage message)
    {
        if(!MessageSelectionCurrent(selected))return;
        var follows=messageRows.ViewportState.FollowsEnd;
        var result=history.ApplyMutations(history.MutationScope,[new(new(message.Id),message)],allowTailEviction:follows);
        messages.Update(value=>value.Selection==selected?value with{NewMessages=result.HasUnseenItems||!follows}:value);
    }

    private static WidgetCursorLiveOptions<DiscordMessage> MessageLiveOptions { get; } = new(
        (left,right)=>BigInteger.Parse(left.Id,System.Globalization.CultureInfo.InvariantCulture)
            .CompareTo(BigInteger.Parse(right.Id,System.Globalization.CultureInfo.InvariantCulture)),
        item=>new(item.Id));
}
