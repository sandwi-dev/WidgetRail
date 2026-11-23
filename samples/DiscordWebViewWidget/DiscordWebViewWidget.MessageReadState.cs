using System.Numerics;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record ReadVisit(MessageSelection Selection,long NavigationRevision,CancellationToken Lifetime,
        string? OpeningWatermark,bool Historical,string? Requested=null,string? Attempted=null);
    private readonly object readVisitGate=new();
    private ReadVisit? readVisit;

    private void BeginReadVisit(MessageSelection selected,string? watermark,bool historical)
    {
        Operations.Cancel("discord.message.read");
        var route=navigation.Value;
        lock(readVisitGate)readVisit=new(selected,route.Revision,route.RouteCancellationToken,watermark,historical);
    }

    private void ObserveMessageReadState(string channel,WidgetCollectionViewportState viewport)
    {
        if(viewport.ActiveViews==0 || viewport.Recovering || !IsActive || navigation.Value.Route!=Route.Conversation ||
            CurrentState()?.Messaging is not {CanMarkRead:true} ||
            history.Snapshot is not {Status:WidgetPagedResourceStatus.Ready} page)return;
        ReadVisit requested;
        lock(readVisitGate)
        {
            if(readVisit is not {} visit || visit.Lifetime.IsCancellationRequested || visit.Selection.Channel!=channel ||
                navigation.Value.Revision!=visit.NavigationRevision || !MessageSelectionCurrent(visit.Selection))return;
            // A normal entry consumes only the watermark captured at opening.
            // Historical notification/reply visits acknowledge only at the real
            // newest end, never just at the end of a bounded middle page.
            var target=visit.Requested is null&&!visit.Historical?visit.OpeningWatermark:null;
            if(viewport.ObservedEndItemKey is {} last && !page.HasAfter && IncomingReadTarget(page.Items,last.Value) is {} incoming)
                target=target is null||Newer(incoming,target)?incoming:target;
            if(target is null || visit.Requested is {} prior&&!Newer(target,prior))return;
            requested=visit with{Requested=target};readVisit=requested;
        }
        var started=false;
        var admission=Operations.RunLatest("discord.message.read",async operation=>
        {
            started=true;
            using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(operation.CancellationToken,requested.Lifetime);
            var token=lifetime.Token;
            var submitted=false;
            bool Current()=>operation.IsCurrent&&!token.IsCancellationRequested&&IsActive&&
                navigation.Value.Route==Route.Conversation&&navigation.Value.Revision==requested.NavigationRevision&&
                MessageSelectionCurrent(requested.Selection)&&messageRows.ViewportState is {ActiveViews:>0,Recovering:false};
            try
            {
                // Latest-wins before dispatch; Discord owns network batching.
                await Task.Delay(TimeSpan.FromMilliseconds(250),time,token);
                // A retained visible frame may arrive before Discord has finished
                // establishing its channel watch. Await that existing operation
                // rather than requiring another identical viewport notification.
                if(messageObservation is {} observation)await observation.Completion.WaitAsync(token);
                if(!Current())return;
                var epoch=liveWatchEpoch;
                if(epoch<=0 || liveMessageSelection!=requested.Selection)return;
                lock(readVisitGate)
                {
                    if(!ReferenceEquals(readVisit,requested)||!operation.IsCurrent||token.IsCancellationRequested)return;
                    // Pending coalesced requests are not external effects. Keep
                    // the last dispatched watermark separately so cancellation
                    // cannot turn an unsent, superseded request into a read.
                    submitted=true;
                    requested=requested with{Attempted=requested.Requested};readVisit=requested;
                }
                var result=await client!.InvokeAsync("messageMarkRead",new{account=requested.Selection.Account,channelId=channel,
                    messageId=requested.Requested,watchEpoch=epoch},token);
                if(!Current())return;
                if(result.GetProperty("account").GetString()!=requested.Selection.Account ||
                    result.GetProperty("channelId").GetString()!=channel || result.GetProperty("messageId").GetString()!=requested.Requested ||
                    result.GetProperty("status").GetString() is not("applied" or "requested" or "already-read"))
                    throw new InvalidDataException("Invalid read acknowledgment.");
                // The provider read store is authoritative for local unread UI.
                // No optimistic widget mutation and no claim of server delivery.
                state.Refresh();
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { }
            catch(Exception)
            {
                if(Current())PublishFeedback("Could not mark this conversation as read. Reopen it to try again.");
                // Retain the attempted watermark: polling cannot blindly retry
                // an external effect whose delivery may already have happened.
            }
            finally
            {
                if(!submitted)lock(readVisitGate)
                    if(ReferenceEquals(readVisit,requested))readVisit=requested with{Requested=requested.Attempted};
            }
        },WidgetOperationLifetime.Active);
        // Rejected admission and cancellation of a queued latest-wins request
        // never execute the body/finally. They cannot reserve an unsent read.
        _=admission.Completion.ContinueWith(_=>
        {
            if(started)return;
            lock(readVisitGate)
                if(ReferenceEquals(readVisit,requested))readVisit=requested with{Requested=requested.Attempted};
        },CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }

    internal Task WaitForMessageReadAsync(CancellationToken token=default)=>Operations.WhenIdleAsync("discord.message.read",token);

    private static string? IncomingReadTarget(IReadOnlyList<DiscordMessage> items,string observedId)
    {
        // A confirmed send can appear ahead of Discord's own read-state head.
        // Use the newest incoming row at or before the observed message. Rows
        // arriving after this viewport observation still need their own observation.
        for(var i=items.Count-1;i>=0;i--)
        {
            if(items[i].Id!=observedId)continue;
            while(i>=0 && items[i].IsOwn)i--;
            return i>=0?items[i].Id:null;
        }
        return null;
    }

    private static bool Newer(string candidate,string previous)=>BigInteger.Parse(candidate)>BigInteger.Parse(previous);
}
