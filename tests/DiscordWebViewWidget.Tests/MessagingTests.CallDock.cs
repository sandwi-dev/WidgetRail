using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task CallDockStateAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync();
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        var session=scenario.Session;
        var people=Enumerable.Range(1,7).Select(index=>new DiscordParticipant(index.ToString(),"Person "+index,
            index==2,index==3,true)).ToArray();
        var stableScope=scenario.Snapshot().ActiveInputScopeId;
        foreach(var count in new[]{0,1,7,2})
        {
            session.Inner.State=session.Inner.State with{Participants=people.Take(count).Reverse().ToArray(),VoiceConnection="connected"};
            await Refresh();
            var dock=scenario.Find("discord.call.dock")!;
            var nodes=Nodes(dock).ToArray();
            var avatars=nodes.Where(node=>node.Id.StartsWith("discord.call.dock.person.",StringComparison.Ordinal)).ToArray();
            check(avatars.Select(node=>node.Id).SequenceEqual(session.Inner.State.Participants.Take(4).Select(person=>"discord.call.dock.person."+person.Id))&&
                (scenario.Find("discord.call.dock.more")?.Text??"")==(count>4?"+"+(count-4):""),
                "Call dock preserves provider order and truthful bounded overflow after count changes: "+count);
            check(nodes.Count(node=>node.IsFocusable)==5&&avatars.All(node=>!node.IsFocusable)&&
                scenario.Snapshot().ActiveInputScopeId==stableScope,
                "Participant updates retain five call-dock actions and the chat input scope: "+count);
            check(avatars.Where(node=>node.StyleClasses.Contains("discord-voice-speaking")).Select(node=>node.Id)
                .SequenceEqual(session.Inner.State.Participants.Take(4).Where(person=>!person.Muted&&!person.Deafened).Select(person=>"discord.call.dock.person."+person.Id)),
                "Dock speaking indication excludes muted/deafened participants: "+count);
            check(WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,
                "Dynamic call-dock declaration remains valid: "+count);
        }
        session.Inner.State=session.Inner.State with{VoiceConnection="reconnecting"};await Refresh();
        var name=scenario.Find("discord.call.dock.name")!.Text!;
        var label=scenario.Find("discord.call.dock.open")!.AccessibilityLabel!;
        check(name.StartsWith("Reconnecting to voice…",StringComparison.Ordinal)&&
            label.Split("Reconnecting to voice…",StringSplitOptions.None).Length==2,
            "Reconnect status is visible and announced exactly once by the dock summary");
        await scenario.Act("discord.call.dock.open");
        check(scenario.Find("discord.call.page") is not null&&session.Calls.All(call=>call.Method is not("callStart" or "callAnswer" or "leave")),
            "Participant/overflow summary opens the existing call screen without call side effects");
        await scenario.Act("discord.messages.open");await scenario.WaitForHistory();
        session.Inner.State=session.Inner.State with{ChannelId=null,Participants=[],Streams=[],VoiceConnection="disconnected"};await Refresh();
        check(scenario.Find("discord.call.dock") is null&&scenario.Find("discord.message.draft") is not null,
            "Disconnect removes the dock while preserving the current conversation and composer");
        async Task Refresh(){await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();}
    }
}
