using System.Text.Json;
using WidgetRail.WidgetProtocol;

internal static partial class MessagingTests
{
    private static WidgetIntentRequest IncomingRequest()=>WidgetIntentRequest.Create(WidgetIntentContracts.Files,
        JsonSerializer.SerializeToElement(new{offerId=new string('c',32)}));

    private static async Task ShareWhileSendingAsync(Action<bool,string> check)
    {
        await using(var scenario=await Scenario.OpenAsync())
        {
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            await scenario.Act("discord.message.draft","Message already in flight");
            scenario.Session.SendGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await scenario.Act("discord.message.send");await Wait(()=>scenario.Session.Sends==1);
            check(await scenario.Widget.OnIntentAsync(IncomingRequest())==WidgetIntentResult.Accepted &&
                scenario.Find("discord.messages.search") is not null && scenario.Find("discord.incoming.file.0") is not null,
                "A new external share stages separately while an existing send continues");
            await scenario.Act("discord.conversation.20");
            check(scenario.Find("discord.messages.notice")?.Text?.Contains("still sending")==true && scenario.Find("discord.message.draft") is null,
                "Recipient choice during an in-flight send shows a truthful busy state without mutating that send");
            await scenario.Act("discord.messages.cancel-share");await Wait(()=>scenario.Released.Contains(scenario.IncomingFile.Id));
            scenario.Session.SendGate.SetResult();
            await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await scenario.Act("discord.conversation.10");
            check(scenario.Session.Sends==1 && scenario.Find("discord.message.draft")?.TextEntryValue=="" &&
                scenario.Released.Count(id=>id==scenario.IncomingFile.Id)==1,
                "Cancelling the incoming choice leaves the original exactly-once send and acknowledgment intact");
        }
        await using(var scenario=await Scenario.OpenAsync())
        {
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            await scenario.Act("discord.message.attach");await Wait(()=>scenario.Find("discord.message.file.0") is not null);
            await scenario.Act("discord.message.draft","Unconfirmed original");scenario.Session.Ambiguous=true;
            await scenario.Act("discord.message.send");await Wait(()=>scenario.Find("discord.message.send")?.Text=="Check delivery");
            await Wait(()=>scenario.Find("discord.message.status")?.Text?.Contains("not been confirmed")==true);
            check(await scenario.Widget.OnIntentAsync(IncomingRequest())==WidgetIntentResult.Accepted,
                "An uncertain prior delivery does not exclude Discord as a destination for a new share");
            await scenario.Act("discord.conversation.10");
            check(scenario.Find("discord.messages.notice")?.Text?.Contains("unconfirmed send")==true && scenario.Find("discord.incoming.file.0") is not null,
                "New files cannot silently mutate the unresolved nonce in the same conversation");
            await scenario.Act("discord.conversation.20");
            check(scenario.Find("discord.message.file.0")?.Text=="incoming.png · Remove" && scenario.Find("discord.message.draft")?.TextEntryValue=="" &&
                !scenario.Released.Contains(scenario.File.Id),"Another recipient receives only incoming files while original uncertain custody stays held");
            await scenario.Act("discord.messages.back");await scenario.Act("discord.conversation.10");
            check(scenario.Find("discord.message.send")?.Text=="Check delivery" && scenario.Find("discord.message.draft")?.TextEntryValue=="Unconfirmed original" &&
                scenario.Find("discord.message.file.0")?.Text=="pending-capture.png · Remove","Original uncertain content/files restore with their exact reconciliation action");
        }
    }
}
