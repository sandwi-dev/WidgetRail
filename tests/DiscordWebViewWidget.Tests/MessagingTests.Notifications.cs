using System.Text.Json;
using WidgetRail.WidgetProtocol;

internal static partial class MessagingTests
{
    private static bool IsInitialHistoryRead((string Method,JsonElement Request) call) => call.Method=="messageHistory" &&
        (!call.Request.TryGetProperty("before",out var before)||before.ValueKind==JsonValueKind.Null) &&
        (!call.Request.TryGetProperty("after",out var after)||after.ValueKind==JsonValueKind.Null);
    private static WidgetIntentRequest NotificationRequest(MessagesSession session,string id="105") =>
        WidgetIntentRequest.Create(WidgetIntentContracts.WebNotification,JsonSerializer.SerializeToElement(new
        {sessionId=session.Reference!.Id,tag=id}));

    private static async Task NotificationNavigationAsync(Action<bool,string> check)
    {
        await using(var scenario=await Scenario.OpenAsync(new(){HistoryCount=12}))
        {
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            await scenario.Act("discord.message.draft","Keep my draft");
            await scenario.Act("discord.message.attach");await Wait(()=>scenario.Find("discord.message.file.0") is not null);
            var reads=scenario.Session.Calls.Count(IsInitialHistoryRead);
            check(await scenario.Widget.OnIntentAsync(NotificationRequest(scenario.Session))==WidgetIntentResult.Accepted,
                "Exact notification association opens a native conversation");
            await scenario.WaitForHistory();
            check(scenario.Find("discord.message.title")?.Text=="Friend 20" &&
                scenario.Snapshot().ScrollRevealRequest?.IndexedItem is {ItemKey:"105",Index:5} &&
                scenario.Find("discord.message.draft")?.TextEntryValue=="" && scenario.Find("discord.message.file.0") is null,
                "Notification reveals the exact message and never carries another recipient's draft");
            var calls=scenario.Session.Calls.Where(IsInitialHistoryRead).ToArray();
            check(calls.Length==reads+1 && calls[^1].Request.GetProperty("around").GetString()=="105" && scenario.Session.Sends==0,
                "Notification cold entry uses one bounded around read without sending or acknowledging");
            check(await scenario.Widget.OnIntentAsync(NotificationRequest(scenario.Session,"106"))==WidgetIntentResult.Accepted,
                "Another notification in retained history is accepted");
            check(scenario.Session.Calls.Count(IsInitialHistoryRead)==reads+1 &&
                scenario.Snapshot().ScrollRevealRequest?.IndexedItem?.ItemKey=="106",
                "Retained notification target is revealed without another history request");
            await scenario.Act("discord.message.latest");
            await Wait(()=>scenario.Session.Calls.Count(IsInitialHistoryRead)==reads+2);
            await scenario.WaitForHistory();
            check(scenario.Session.Calls.Last(IsInitialHistoryRead).Request.GetProperty("around").ValueKind==JsonValueKind.Null &&
                scenario.Snapshot().ScrollRevealRequest?.IndexedItem?.ItemKey=="111",
                "Jump to latest leaves the notification anchor and explicitly reveals the current tail");
            await scenario.Act("discord.messages.back");await Wait(()=>scenario.Find("discord.conversation.10") is not null);
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            check(scenario.Find("discord.message.draft")?.TextEntryValue=="Keep my draft" &&
                scenario.Find("discord.message.file.0")?.Text=="pending-capture.png · Remove" && !scenario.Released.Contains(scenario.File.Id),
                "Returning after notification navigation restores the original draft and exact file custody");
        }
        foreach(var direction in new[]{"older","newer"})
        {
            await using var scenario=await Scenario.OpenAsync(new(){HistoryCount=20,PagedNotificationHistory=true});
            check(await scenario.Widget.OnIntentAsync(NotificationRequest(scenario.Session))==WidgetIntentResult.Accepted,
                "Notification opens the bounded middle history window before explicit paging: "+direction);
            await scenario.WaitForHistory();
            check(scenario.Snapshot().ScrollRevealRequest?.IndexedItem?.ItemKey=="105","Notification target is initially declared before paging: "+direction);
            var viewport=Guid.NewGuid().ToString("N");
            for(var page=0;page<2;page++)
            {
                using var rows=WidgetRail.WidgetSdk.WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"notification.boundary."+page);
                rows.PublishSnapshot();
                using var prior=await rows.AcquireAsync("discord.message.history",0,scenario.Find("discord.message.history")!.IndexedCollection!.Count);
                var priorKeys=prior.Range.Items.Select(item=>item.Key).ToArray();
                await rows.ObserveViewportAsync("discord.message.history",new(viewport,page+1,true,false)
                    {Boundary=direction=="older"?IndexedCollectionBoundary.Before:IndexedCollectionBoundary.After,PhysicalAtEnd=false});
                await Wait(()=>scenario.Find("discord.message.history")?.IndexedCollection?.Count==40);
                rows.PublishSnapshot();using var current=await rows.AcquireAsync("discord.message.history",0,40);
                check(!current.Range.Items.Select(item=>item.Key).SequenceEqual(priorKeys),"Boundary demand commits a different reading window: "+direction+" "+page);
            }
            check(scenario.Find("discord.message.history")?.IndexedCollection?.Count==40 &&
                scenario.Snapshot().ScrollRevealRequest is null && scenario.Find("discord.message.status") is null,
                "Explicit paging retires the notification reveal and missing-target notice when its row leaves the bounded window: "+direction);
            check(scenario.Session.Calls.Count(IsInitialHistoryRead)==1 && scenario.Session.Sends==0 &&
                scenario.Session.Calls.Count(call=>call.Method=="messageHistory")<=7,
                "Notification paging uses bounded adjacent preparation without an implicit refresh: "+direction);
        }
        await using(var scenario=await Scenario.OpenAsync(new(){MissingNotificationTarget=true}))
        {
            check(await scenario.Widget.OnIntentAsync(NotificationRequest(scenario.Session))==WidgetIntentResult.Rejected &&
                scenario.Find("discord.account.browser") is not null && !scenario.Session.Calls.Any(call=>call.Method=="messageHistory"),
                "Unmapped notification presents its existing browser source without opening guessed history");
            await scenario.Act("discord.account.back");
            scenario.Session.MissingNotificationTarget=false;
            await scenario.Widget.OnIntentAsync(IncomingRequest());
            var before=scenario.Session.Calls.Count;
            check(await scenario.Widget.OnIntentAsync(NotificationRequest(scenario.Session))==WidgetIntentResult.Rejected &&
                scenario.Find("discord.account.browser") is not null &&
                !scenario.Session.Calls.Skip(before).Any(call=>call.Method is "messageHistory" or "messageNotificationTarget"),
                "Notification during recipient selection presents the browser without resolving or sending files");
            await scenario.Act("discord.account.back");
            check(scenario.Find("discord.incoming.file.0") is not null && !scenario.Released.Contains(scenario.IncomingFile.Id),
                "Back from notification browser restores the unresolved incoming share and its file custody");
        }
        await using(var scenario=await Scenario.OpenAsync())
        {
            var request=NotificationRequest(scenario.Session);
            scenario.Session.NotificationGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending=scenario.Widget.OnIntentAsync(request).AsTask();
            await Wait(()=>scenario.Session.Calls.Any(call=>call.Method=="messageNotificationTarget"));
            await scenario.Act("discord.messages.back");
            scenario.Session.NotificationGate.SetResult();
            check(await pending==WidgetIntentResult.Rejected && !scenario.Session.Calls.Any(call=>call.Method=="messageHistory"),
                "Late notification mapping cannot replace newer user navigation");
            scenario.Session.Inner.Reference=new(new string('b',32));
            check(await scenario.Widget.OnIntentAsync(request)==WidgetIntentResult.Rejected,
                "Notification from an old browser session cannot open a new account session");
        }
    }
}
