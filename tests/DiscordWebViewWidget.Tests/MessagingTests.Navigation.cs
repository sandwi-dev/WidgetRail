using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task IntegratedNavigationAsync(Action<bool,string> check)
    {
        await UnreadDestinationAsync(check);
        await using var scenario=await Scenario.OpenAsync();
        var messagesSection=scenario.Find("discord.section.content")!.Transition;
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        check(scenario.Find("discord.section.content")?.Transition==messagesSection,
            "Opening a conversation keeps the Messages section identity and does not start a whole-section animation");
        await scenario.Act("discord.message.draft","Unsent draft");
        var snapshot=scenario.Snapshot();
        check(scenario.Find("discord.sections") is not null && scenario.Find("discord.messages.list") is not null &&
            scenario.Find("discord.message.history") is not null && scenario.Find("discord.call.dock.mute") is not null,
            "Conversation shares permanent destinations, the wide inbox and live call controls");
        check(WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0,"Integrated conversation declaration satisfies native presentation constraints");
        check(scenario.Find("discord.messages.refresh") is null && scenario.Find("discord.messages.search")?.TextEntryPlaceholder=="Search",
            "Conversation list uses one short Search control and the shared top-level Refresh");
        check(scenario.Find("discord.messages.list")?.Focus?.Right=="discord.message.draft" &&
            scenario.Find("discord.message.draft")?.Focus?.Left=="discord.messages.list" &&
            scenario.Find("discord.message.history")?.Focus?.Left=="discord.messages.list" &&
            scenario.Find("discord.messages.list")?.IndexedPreferredEntryKey=="10",
            "Panel exits target the composer and selected conversation rather than spatial or previously hovered rows");
        check(scenario.Find("discord.section.content")?.Transition is {Kind:WidgetTransitionKind.Content},
            "Section reading position is owned by the host transition region");
        check(scenario.Find("discord.messages.open")?.Focus?.Right=="discord.refresh" &&
            scenario.Find("discord.refresh")?.Focus is {Left:"discord.messages.open",Right:"discord.account.open"} &&
            scenario.Find("discord.account.open")?.Focus is {Left:"discord.refresh",Right:"discord.call.open"},
            "Header focus can leave the tab group and reach both toolbar actions");
        var before=scenario.Session.Calls.Count;
        await scenario.Act("discord.messages.open");
        check(scenario.Session.Calls.Count==before,"Selecting the current Messages destination does not reload its inbox or history");
        await scenario.Act("discord.call.open");
        check(scenario.Find("discord.section.content")?.Transition?.Key!=messagesSection!.Key,
            "Call still has a distinct section destination");
        check(scenario.Find("discord.main.mute") is not null && scenario.Find("discord.message.history") is null,
            "Call destination restores the existing call interface");
        await scenario.Act("discord.messages.open");
        check(scenario.Find("discord.message.draft")?.TextEntryValue=="Unsent draft" && scenario.Find("discord.message.history") is not null,
            "Returning to Messages restores the selected conversation and unsent draft");
        await scenario.Act("discord.messages.back");
        check(scenario.Snapshot().InitialFocusId=="discord.messages.list" && scenario.Find("discord.messages.list")?.IndexedPreferredEntryKey=="10",
            "Back after a section round trip restores the retained conversation's inbox row");
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        var intermediateListPage=0;var observing=0;
        void ObserveSwitch(object? sender,WidgetInvalidatedEventArgs args)
        {
            if(Interlocked.Exchange(ref observing,1)!=0)return;
            try { if(scenario.Find("discord.message.workspace") is null)Interlocked.Increment(ref intermediateListPage); }
            finally { Volatile.Write(ref observing,0); }
        }
        scenario.Widget.Invalidated+=ObserveSwitch;
        try { await scenario.Act("discord.conversation.20");await scenario.WaitForHistory(); }
        finally { scenario.Widget.Invalidated-=ObserveSwitch; }
        check(intermediateListPage==0,"Changing chats never publishes an intermediate conversation-list page that discards the chat workspace");
        check(scenario.Find("discord.section.content")?.Transition==messagesSection,
            "Switching selected conversations updates the chat without changing the section animation identity");
        await scenario.Act("discord.messages.back");
        check(scenario.Find("discord.message.history") is null && scenario.Find("discord.messages.search") is not null,
            "Switching conversation through the sidebar does not stack previous conversations into Back history");
        check(!scenario.Session.Calls.Any(call=>call.Method is "join" or "leave" or "share" or "watch" or "messageSend"),
            "Section and conversation navigation does not mutate voice sharing or send messages");
        scenario.Session.Inner.State=scenario.Session.Inner.State with{ChannelId=null,ChannelName="",Participants=[],Streams=[]};
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        check(scenario.Find("discord.call.dock") is null && scenario.Find("discord.call.open")?.Text=="Servers" &&
            scenario.Snapshot().Root.Shortcuts.Any(item=>item.Button==ControllerButton.LeftBumper&&item.Label=="Servers"),
            "Outside a call, Messages has no call bar and the section and guide say Servers");
    }

    private static async Task UnreadDestinationAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new(){OldestUnread="100",HasUnread=true});
        await scenario.Act("discord.call.open");
        check(scenario.Find("discord.messages.open")?.Text=="Messages •" &&
            !scenario.Session.Calls.Any(call=>call.Method=="messageHistory"),
            "Provider unread state decorates the primary destination without fetching history");
        scenario.Session.HasUnread=false;
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        check(scenario.Find("discord.messages.open")?.Text=="Messages",
            "An authoritative all-read state clears the dot despite a stale cached inbox page");
    }
}
