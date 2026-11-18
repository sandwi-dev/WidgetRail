using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task LiveTailViewportAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new MessagesSession{LiveUpdates=true,HistoryCount=20});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"messages.tail.rows");
        var viewport=Guid.NewGuid().ToString("N");long sequence=0;
        await Observe(true);
        await Append(120,40);
        await Wait(()=>scenario.Find("discord.message.history")!.IndexedCollection!.Count==40);
        var keys=await Keys();
        check(keys[0]=="120"&&keys[^1]=="159","A following native viewport automatically rolls the bounded 40-message window");
        check(scenario.Find("discord.message.latest") is null,"Following the live tail does not require Latest");
        await Observe(false);await Append(160,5);
        await Wait(()=>scenario.Find("discord.message.latest")!.Text=="New messages");
        check((await Keys()).SequenceEqual(keys),"Reading older messages keeps the retained window while live messages wait");
        await Observe(true);
        keys=await Keys();
        check(keys[0]=="125"&&keys[^1]=="164"&&scenario.Find("discord.message.latest") is null,
            "Returning to the end consumes deferred messages locally without pressing Latest");
        check(scenario.Session.Calls.Count(call=>call.Method=="messageHistory")==1&&scenario.Session.Sends==0,
            "Live tail advancement adds no history GETs or sends");

        async Task Observe(bool end)
        {
            rows.PublishSnapshot();
            await rows.ObserveViewportAsync("discord.message.history",new(viewport,++sequence,true,end));
        }
        async Task Append(int first,int count)
        {
            foreach(var id in Enumerable.Range(first,count))
            {
                var message=new DiscordMessage(id.ToString(),"10","2","Friend",null,"Live "+id,id,null,null,[],false);
                scenario.Session.Changes.Enqueue(new(++scenario.Session.MessageRevision,"upsert",message.Id,message));
            }
            await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        async Task<string[]> Keys()
        {
            rows.PublishSnapshot();using var range=await rows.AcquireAsync("discord.message.history",0,40);
            return range.Range.Items.Select(item=>item.Key).ToArray();
        }
    }
}
