using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task ConversationPresentationAsync(Action<bool,string> check)
    {
        var session=new MessagesSession{LastMessage="100",OldestUnread="100",CatalogOrderVersion="o1",ConversationMetadata=row=>row with
        {
            UserId="2",Presence="idle",LastMessageTimestamp=DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            LastMessagePreview="**Hello** ||do not disclose",LastMessageAuthorName="Me",LastMessageIsOwn=true
        }};
        session.Inner.State=session.Inner.State with{ChannelId="10",VoiceConnection="connected"};
        await using var scenario=await Scenario.OpenAsync(session);
        var row=scenario.Find("discord.conversation.10")!;
        var nodes=Nodes(row).ToArray();
        check(nodes.Any(node=>node.Id.EndsWith(".detail")&&node.Text=="You: Hello [Spoiler]") &&
            !nodes.Any(node=>(node.Text??"").Contains("do not disclose")) && !row.AccessibilityLabel!.Contains("do not disclose"),
            "Conversation snippet uses existing markdown/spoiler parsing for visible and accessible text");
        check(nodes.Any(node=>node.Id.EndsWith(".presence")&&node.AccessibilityLabel=="Idle") &&
            nodes.Any(node=>node.Id.EndsWith(".unread")&&node.AccessibilityLabel=="Unread messages") &&
            nodes.Any(node=>node.Id.EndsWith(".call")&&node.Text=="In call") && nodes.Any(node=>node.Id.EndsWith(".time")),
            "Rich conversation row exposes truthful presence, unread, time and current-call metadata");
        check(nodes.Count(node=>node.ActionId is not null)==1 && nodes.All(node=>node.FocusPresentation is null),
            "Rich conversation metadata adds no controller stops or automatic previews");
        check(!session.Calls.Any(call=>call.Method=="messageHistory"),"Rich inbox requires no per-conversation history fetches");
        session.Inner.State=session.Inner.State with{VoiceConnection="connecting"};
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        check(!Nodes(scenario.Find("discord.conversation.10")!).Any(node=>node.Id.EndsWith(".call")),
            "Selecting a channel before voice connects does not claim an active call in the conversation row");
        var original=session.ConversationMetadata!(new("10","10","2","Friend","dm",null,"100",true,true));
        check(original.IsValid && !(original with{Presence="invisible"}).IsValid &&
            !(original with{UserId=null}).IsValid && !(original with{LastMessageTimestamp=long.MaxValue}).IsValid &&
            !(original with{LastMessagePreview=new string('x',241)}).IsValid,
            "Conversation metadata validation rejects unknown presence, group-person ambiguity and unbounded data");
        var previousQuery=scenario.Find("discord.messages.list")!.IndexedCollection!;
        session.ConversationMetadata=item=>item;
        session.CatalogVersion="v2";session.CatalogRevision++;
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>Nodes(scenario.Find("discord.conversation.10")!).All(node=>!node.Id.EndsWith(".presence")));
        row=scenario.Find("discord.conversation.10")!;
        check(Nodes(row).Any(node=>node.Text=="Direct message") && !Nodes(row).Any(node=>node.Id.EndsWith(".time")),
            "Missing resident metadata renders an honest fallback rather than stale status, snippet or date");
        var updatedQuery=scenario.Find("discord.messages.list")!.IndexedCollection!;
        check(updatedQuery.QueryGeneration==previousQuery.QueryGeneration&&updatedQuery.ContentRevision>previousQuery.ContentRevision,
            "Provider metadata refresh retains the conversation query and publishes new content");
        session.CatalogOrderVersion="o2";session.CatalogVersion="v3";session.CatalogRevision++;
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>scenario.Find("discord.messages.list")!.IndexedCollection!.QueryGeneration>updatedQuery.QueryGeneration);
        check(true,"An actual ordered-membership change still replaces the conversation query");
    }
}
