using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task LiveMessagesAsync(Action<bool,string> check)
    {
        await LiveRecoveryAsync(check);
        await using var scenario=await Scenario.OpenAsync(new MessagesSession{LiveUpdates=true,SentId="102"});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        var session=scenario.Session;
        var initialGets=session.Calls.Count(call=>call.Method=="messageHistory");
        var added=new DiscordMessage("101","10","2","Friend",null,"Arrived live",2,null,null,[],false);
        session.Changes.Enqueue(new(++session.MessageRevision,"upsert",added.Id,added));
        await Refresh();await Wait(()=>scenario.Find("discord.message.history")?.IndexedCollection?.Count==2);
        check((await Texts()).Contains("Arrived live"),"Gateway create enters native history without Latest");
        check(session.Calls.Count(call=>call.Method=="messageHistory")==initialGets,
            "A new message uses local changes without a history request");
        session.Changes.Enqueue(new(++session.MessageRevision,"upsert",added.Id,added with{Content="Edited live"}));
        session.Changes.Enqueue(new(++session.MessageRevision,"remove","100",null));
        await Refresh();await Wait(()=>scenario.Find("discord.message.history")?.IndexedCollection?.Count==1);
        check((await Texts()).Contains("Edited live") && !(await Texts()).Contains("Hello from history"),
            "Gateway edit/delete update retained rows without reconstructing a stale history page");
        var calls=session.Calls.Count(call=>call.Method=="messageChanges");
        await Refresh();check(session.Calls.Count(call=>call.Method=="messageChanges")==calls,
            "An unchanged state snapshot makes no duplicate delta read");
        check(session.Calls.Count(call=>call.Method=="messageHistory")==initialGets && session.Sends==0,
            "Live reconciliation performs no external history request or send");
        await scenario.Act("discord.message.draft","Explicit send");await scenario.Act("discord.message.send");
        await Wait(()=>scenario.Find("discord.message.status")?.Text=="Sent");
        await Wait(()=>scenario.Find("discord.message.history")?.IndexedCollection?.Count==2);
        check((await Texts()).Contains("Explicit send"),"Own successful send appears immediately without a history GET");
        var nonce=session.Calls.Last(call=>call.Method=="messageSend").Request.GetProperty("nonce").GetString();
        var own=new DiscordMessage("102","10",session.Inner.State.Account,"Friend",null,"Explicit send",1,null,nonce,[],true);
        session.Changes.Enqueue(new(++session.MessageRevision,"upsert",own.Id,own));
        await Refresh();
        check(scenario.Find("discord.message.history")?.IndexedCollection?.Count==2,
            "Send acknowledgment and later gateway event do not duplicate a message occurrence");
        check(WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,
            "Live history preserves valid shared indexed presentation");

        async Task Refresh()
        {
            await scenario.Widget.OnActionAsync(new("refresh","test"));
            await scenario.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        async Task<string[]> Texts()
        {
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"messages.live.rows");
            rows.PublishSnapshot();var count=scenario.Find("discord.message.history")!.IndexedCollection!.Count;
            using var range=await rows.AcquireAsync("discord.message.history",0,(int)count);
            return range.Range.Items.SelectMany(item=>Nodes(item.Root)).Select(node=>node.Text??"").ToArray();
        }
    }

    private static async Task LiveRecoveryAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new MessagesSession{LiveUpdates=true});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        var session=scenario.Session;
        session.RecoveryFloor=session.MessageRevision=1;
        await Refresh();await Wait(()=>session.Calls.Any(call=>call.Method=="messageRecover"));
        await Wait(()=>scenario.Find("discord.message.history")?.CollectionBoundaries?.After==true);
        check(await ContainsText("Recovered current text"),"A real event gap reconciles the retained interval in place");
        var recovery=session.Calls.Single(call=>call.Method=="messageRecover").Request;
        check(recovery.GetProperty("ids").EnumerateArray().Select(value=>value.GetString()).SequenceEqual(["100"]) &&
            session.Calls.Count(call=>call.Method=="messageHistory")==1,
            "Gap recovery supplies exact retained identities without resetting to latest history");
        await Refresh();
        check(session.Calls.Count(call=>call.Method=="messageRecover")==1,
            "Unchanged observations do not repeat the recovery fetch");

        session.RecoveryFloor=session.MessageRevision=2;session.RecoveryComplete=false;
        await Refresh();await Wait(()=>session.Calls.Count(call=>call.Method=="messageRecover")==2);
        check(await ContainsText("Recovered current text"),"Incomplete recovery keeps rows whose absence is not authoritative");
        await Refresh();check(session.Calls.Count(call=>call.Method=="messageRecover")==2,
            "Uncertain recovery keeps old paint instead of starting an HTTP retry loop");

        await scenario.Act("discord.message.latest");await scenario.WaitForHistory();
        session.RecoveryComplete=true;session.RecoveryGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RecoveryFloor=session.MessageRevision=3;
        await Refresh();await Wait(()=>session.Calls.Count(call=>call.Method=="messageRecover")==3);
        await scenario.Act("discord.messages.back");await scenario.Act("discord.conversation.20");await scenario.WaitForHistory();
        session.RecoveryGate.SetResult();await Task.Yield();
        check(await ContainsText("Hello from history") && !await ContainsText("Recovered current text"),
            "A delayed recovery cannot replace another conversation after navigation");

        async Task Refresh()
        {
            await scenario.Widget.OnActionAsync(new("refresh","test"));
            await scenario.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        async Task<bool> ContainsText(string text)
        {
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"messages.recovery.rows");
            rows.PublishSnapshot();using var range=await rows.AcquireAsync("discord.message.history",0,1);
            return range.Range.Items.SelectMany(item=>Nodes(item.Root)).Any(node=>node.Text==text);
        }
    }
}
