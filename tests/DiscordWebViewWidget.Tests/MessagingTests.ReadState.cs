using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    internal static async Task ReadStateAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new MessagesSession{LiveUpdates=true,CanMarkRead=true,LastMessage="100"});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        check(!Reads().Any(),"Loading a conversation alone does not acknowledge unseen content");
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"read-viewport");
        var viewport=Guid.NewGuid().ToString("N");long sequence=0;
        await Observe(false);
        await scenario.Widget.WaitForMessageReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(Reads().Single().Request.GetProperty("messageId").GetString()=="100",
            "A normally opened visible chat acknowledges its captured opening watermark");
        await Observe(false);await scenario.Widget.WaitForMessageReadAsync();
        check(Reads().Count()==1,"Repeated viewport feedback does not resend an acknowledged attempt");
        var live=new DiscordMessage("101","10","2","Friend",null,"New while reading",2,null,null,[],false);
        scenario.Session.Changes.Enqueue(new(++scenario.Session.MessageRevision,"upsert",live.Id,live));
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>scenario.Find("discord.message.history")!.IndexedCollection!.Count==2);
        await scenario.Widget.WaitForMessageReadAsync();
        check(Reads().Count()==1,"New messages remain unread until their tail is observed");
        await Observe(true);await scenario.Widget.WaitForMessageReadAsync();
        check(Reads().Last().Request.GetProperty("messageId").GetString()=="101",
            "Observed newest history advances the read watermark");
        await scenario.Act("discord.call.open");await scenario.Act("discord.messages.open");await scenario.Act("discord.conversation.10");
        await Wait(()=>scenario.Find("discord.message.history")?.IndexedCollection?.Count>0);
        var before=Reads().Count();await Observe(false);
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Background);
        await scenario.Widget.WaitForMessageReadAsync();
        check(Reads().Count()==before,"Hiding the overlay before acknowledgment dispatch cancels that request");
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Interactive);
        await Observe(false);await scenario.Widget.WaitForMessageReadAsync();
        check(Reads().Count()==before+1,"A request cancelled before dispatch can be retried on actual visible reentry");

        await ReadStateNavigationAsync(check);
        await CoalescedReadCancellationAsync(check);
        await ConfirmedSendReadAsync(check);
        await OwnOnlyReadAsync(check);
        await CurrentReadFailureAsync(check);
        await HiddenChatWithPinAsync(check);
        await DelayedReadWatchAsync(check);

        IEnumerable<(string Method,System.Text.Json.JsonElement Request)> Reads()=>scenario.Session.Calls.Where(call=>call.Method=="messageMarkRead");
        async Task Observe(bool end)
        {rows.PublishSnapshot();await rows.ObserveViewportAsync("discord.message.history",new(viewport,++sequence,true,end));}
    }

    private static async Task ReadStateNavigationAsync(Action<bool,string> check)
    {
        await using(var historical=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,HistoryCount=12,LastMessage="111"}))
        {
            await historical.Widget.OnIntentAsync(NotificationRequest(historical.Session));
            await historical.WaitForHistory();
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(historical.Widget,"historical-read");
            var viewport=Guid.NewGuid().ToString("N");
            rows.PublishSnapshot();
            await rows.ObserveViewportAsync("discord.message.history",new(viewport,1,true,true));
            await historical.Widget.WaitForMessageReadAsync();
            check(!historical.Session.Calls.Any(call=>call.Method=="messageMarkRead"),
                "Historical notification entry cannot acknowledge at the end of a middle history page");
            await historical.Act("discord.message.latest");await historical.WaitForHistory();
            await Wait(()=>historical.Snapshot().ScrollRevealRequest?.IndexedItem?.ItemKey=="111");
            rows.PublishSnapshot();
            await rows.ObserveViewportAsync("discord.message.history",new(viewport,2,true,true));
            await historical.Widget.WaitForMessageReadAsync();
            check(historical.Session.Calls.Single(call=>call.Method=="messageMarkRead").Request.GetProperty("messageId").GetString()=="111",
                "Explicit Latest followed by observed newest history can advance a historical visit");
        }
        await using(var departed=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,LastMessage="100"}))
        {
            await departed.Act("discord.conversation.10");await departed.WaitForHistory();
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(departed.Widget,"departed-read");
            rows.PublishSnapshot();
            await rows.ObserveViewportAsync("discord.message.history",new(Guid.NewGuid().ToString("N"),1,true,false));
            await departed.Act("discord.call.open");
            await departed.Widget.WaitForMessageReadAsync();
            check(!departed.Session.Calls.Any(call=>call.Method=="messageMarkRead"),
                "Switching to Call before dispatch cancels the old conversation acknowledgment");
        }
        await using(var late=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,LastMessage="100",ReadGate=new(TaskCreationOptions.RunContinuationsAsynchronously)}))
        {
            await late.Act("discord.conversation.10");await late.WaitForHistory();
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(late.Widget,"late-read");
            var viewport=Guid.NewGuid().ToString("N");
            rows.PublishSnapshot();
            await rows.ObserveViewportAsync("discord.message.history",new(viewport,1,true,false));
            await Wait(()=>late.Session.Calls.Any(call=>call.Method=="messageMarkRead"));
            var hiding=WidgetTestHost.SetLifecycleStateAsync(late.Widget,WidgetLifecycleState.Background).AsTask();
            late.Session.ReadGate!.SetResult();
            await hiding.WaitAsync(TimeSpan.FromSeconds(5));
            await late.Widget.WaitForMessageReadAsync();
            await WidgetTestHost.SetLifecycleStateAsync(late.Widget,WidgetLifecycleState.Interactive);
            rows.PublishSnapshot();
            await rows.ObserveViewportAsync("discord.message.history",new(viewport,2,true,false));
            await late.Widget.WaitForMessageReadAsync();
            check(late.Session.Calls.Count(call=>call.Method=="messageMarkRead")==1,
                "A dispatched acknowledgment is not replayed after an ambiguous late completion and visible reentry");
            check(late.Notifications.IsEmpty,"Late read completion cannot publish feedback into a new presentation");
        }
    }

    private static async Task CoalescedReadCancellationAsync(Action<bool,string> check)
    {
        var clock=new ReadDelayClock();
        await using var scenario=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,LastMessage="100"},clock);
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"coalesced-read");
        var viewport=Guid.NewGuid().ToString("N");
        rows.PublishSnapshot();
        await rows.ObserveViewportAsync("discord.message.history",new(viewport,1,true,false));
        await Wait(()=>clock.Pending==1);
        var live=new DiscordMessage("101","10","2","Friend",null,"New tail",2,null,null,[],false);
        scenario.Session.Changes.Enqueue(new(++scenario.Session.MessageRevision,"upsert",live.Id,live));
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>scenario.Find("discord.message.history")!.IndexedCollection!.Count==2);
        rows.PublishSnapshot();
        await rows.ObserveViewportAsync("discord.message.history",new(viewport,2,true,true));
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Background);
        await scenario.Widget.WaitForMessageReadAsync();
        check(!scenario.Session.Calls.Any(call=>call.Method=="messageMarkRead"),
            "Hiding during coalescing cancels both unsent watermarks");
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Interactive);
        rows.PublishSnapshot();
        await rows.ObserveViewportAsync("discord.message.history",new(viewport,3,true,false));
        await Wait(()=>clock.Pending==1);
        clock.Fire();await scenario.Widget.WaitForMessageReadAsync();
        check(scenario.Session.Calls.Single(call=>call.Method=="messageMarkRead").Request.GetProperty("messageId").GetString()=="100",
            "A superseded unsent watermark cannot suppress the opening read on visible reentry");
    }

    private static async Task ConfirmedSendReadAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,HistoryCount=15,
            OldestUnread="105",LastMessage="114",SentId="124",LatestStartAfterSend=110,RejectReadBeyond="123"});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"sent-read-watermarks");
        var viewport=Guid.NewGuid().ToString("N");long sequence=0;
        async Task Observe(bool end)
        {
            rows.PublishSnapshot();await rows.ObserveViewportAsync("discord.message.history",new(viewport,++sequence,true,end));
            await scenario.Widget.WaitForMessageReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        string[] Watermarks()=>scenario.Session.Calls.Where(call=>call.Method=="messageMarkRead")
            .Select(call=>call.Request.GetProperty("messageId").GetString()!).ToArray();
        await Observe(false);
        await scenario.Act("discord.message.draft","From older history");await scenario.Act("discord.message.send");
        await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Wait(()=>scenario.Snapshot().ScrollRevealRequest?.IndexedItem?.ItemKey=="124");
        await Observe(true);await Observe(true);
        check(Watermarks().SequenceEqual(new[]{"114","123"}),
            "Sending from older history acknowledges the incoming row preceding the visible outgoing tail exactly once ("+string.Join(",",Watermarks())+")");
        check(!scenario.Notifications.Any(n=>n.Notification?.Body?.Contains("Could not mark",StringComparison.Ordinal)==true),
            "A confirmed outbound message ahead of Discord's read-state head does not produce a read-error toast");
        scenario.Session.RejectReadBeyond=null;
        var reply=new DiscordMessage("125","10","2","Friend",null,"Reply after outgoing",2,null,null,[],false);
        scenario.Session.Changes.Enqueue(new(++scenario.Session.MessageRevision,"upsert",reply.Id,reply));
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>scenario.Find("discord.message.history")?.IndexedOccurrenceKeys?.LastOrDefault()=="125");
        await Observe(false);
        check(Watermarks().SequenceEqual(new[]{"114","123"}),"A new peer reply past the observed outgoing message stays unread");
        await Observe(true);
        check(Watermarks().SequenceEqual(new[]{"114","123","125"}),"A later visible incoming reply advances its own read marker");
    }

    private static async Task OwnOnlyReadAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,OwnHistory=true,
            LastMessage="100",SentId="101"});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"own-read-watermark");
        var viewport=Guid.NewGuid().ToString("N");
        rows.PublishSnapshot();await rows.ObserveViewportAsync("discord.message.history",new(viewport,1,true,true));
        await scenario.Widget.WaitForMessageReadAsync();
        check(scenario.Session.Calls.Where(call=>call.Method=="messageMarkRead").Single().Request.GetProperty("messageId").GetString()=="100",
            "Opening a conversation acknowledges the exact provider head even when the head is self-authored");
        await scenario.Act("discord.message.draft","Only my own messages");await scenario.Act("discord.message.send");
        await scenario.Widget.WaitForMessageSendAsync();
        await Wait(()=>scenario.Snapshot().ScrollRevealRequest?.IndexedItem?.ItemKey=="101");
        rows.PublishSnapshot();await rows.ObserveViewportAsync("discord.message.history",new(viewport,2,true,true));
        await scenario.Widget.WaitForMessageReadAsync();
        check(scenario.Session.Calls.Count(call=>call.Method=="messageMarkRead")==1,
            "A newly sent own-only tail does not manufacture an incoming read target");
    }

    private static async Task CurrentReadFailureAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,LastMessage="100",RejectReadBeyond="99"});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"current-read-error");
        var viewport=Guid.NewGuid().ToString("N");
        for(var sequence=1;sequence<=2;sequence++)
        {
            rows.PublishSnapshot();await rows.ObserveViewportAsync("discord.message.history",new(viewport,sequence,true,true));
            await scenario.Widget.WaitForMessageReadAsync();
        }
        await Wait(()=>scenario.Notifications.Any(n=>n.Notification?.Body?.Contains("Could not mark",StringComparison.Ordinal)==true));
        check(scenario.Session.Calls.Count(call=>call.Method=="messageMarkRead")==1,
            "A genuine rejection on a current incoming target remains visible without automatic replay");
    }

    private static async Task HiddenChatWithPinAsync(Action<bool,string> check)
    {
        var clock=new ReadDelayClock();
        await using var scenario=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,LastMessage="100"},clock);
        var pin=WidgetTestHost.CreatePinnedLayoutHost(scenario.Widget,"pinned-call-read");
        check(await pin.SelectAsync("discord.call"),"A voice pin is selected before opening the chat");
        await scenario.Act("discord.conversation.10");
        await scenario.WaitForHistory();
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"pinned-call-chat");
        var viewport=Guid.NewGuid().ToString("N");
        rows.PublishSnapshot();
        await rows.ObserveViewportAsync("discord.message.history",new(viewport,1,true,true));
        await Wait(()=>clock.Pending==1);
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Visible);
        await rows.ObserveViewportAsync("discord.message.history",new(viewport,2,false,false));
        clock.Fire();
        await scenario.Widget.WaitForMessageReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(scenario.Widget.IsActive && pin.SelectedLayoutId=="discord.call" &&
            !scenario.Session.Calls.Any(call=>call.Method=="messageMarkRead"),
            "A visible voice pin cannot acknowledge a chat whose viewport closed before dispatch");

        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Interactive);
        await rows.ObserveViewportAsync("discord.message.history",new(viewport,3,true,true));
        await Wait(()=>clock.Pending==1);
        clock.Fire();
        await scenario.Widget.WaitForMessageReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(scenario.Session.Calls.Single(call=>call.Method=="messageMarkRead").Request.GetProperty("messageId").GetString()=="100",
            "Showing the chat again submits its previously unsent read exactly once");
    }

    private static async Task DelayedReadWatchAsync(Action<bool,string> check)
    {
        var clock=new ReadDelayClock();
        var observed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scenario=await Scenario.OpenAsync(new(){LiveUpdates=true,CanMarkRead=true,LastMessage="100"},clock);
        await scenario.Act("discord.conversation.10");await Wait(()=>scenario.Session.HistoryReplies==1);await scenario.WaitForHistory();
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Background);
        scenario.Session.ObservationGate=observed;
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Interactive);
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"watch-read");
        rows.PublishSnapshot();
        await rows.ObserveViewportAsync("discord.message.history",new(Guid.NewGuid().ToString("N"),1,true,false));
        await Wait(()=>clock.Pending==1);clock.Fire();
        check(!scenario.Session.Calls.Any(call=>call.Method=="messageMarkRead"),
            "A retained chat does not acknowledge before its provider watch is reestablished");
        observed.SetResult();
        await scenario.Widget.WaitForMessageReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(scenario.Session.Calls.Single(call=>call.Method=="messageMarkRead").Request.GetProperty("messageId").GetString()=="100",
            "Delayed watch readiness consumes the existing visible observation without requiring another layout event");
    }

    // Only the read coalescing delay uses this clock; no wall-clock race is needed
    // to put two requests into the unsent state before lifecycle cancellation.
    private sealed class ReadDelayClock:TimeProvider
    {
        private readonly object gate=new();
        private readonly HashSet<ReadTimer> timers=[];
        internal int Pending {get{lock(gate)return timers.Count;}}
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {
            if(dueTime!=TimeSpan.FromMilliseconds(250))return TimeProvider.System.CreateTimer(callback,state,dueTime,period);
            if(period!=Timeout.InfiniteTimeSpan)throw new NotSupportedException("Only one-shot delays are expected.");
            var timer=new ReadTimer(this,callback,state);lock(gate)timers.Add(timer);return timer;
        }
        internal void Fire()
        {
            ReadTimer[] due;lock(gate){due=timers.ToArray();timers.Clear();}
            foreach(var timer in due)timer.Callback(timer.State);
        }
        private sealed class ReadTimer(ReadDelayClock owner,TimerCallback callback,object? state):ITimer
        {
            internal readonly TimerCallback Callback=callback;
            internal readonly object? State=state;
            public bool Change(TimeSpan dueTime,TimeSpan period)=>throw new NotSupportedException();
            public void Dispose(){lock(owner.gate)owner.timers.Remove(this);}
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
}
