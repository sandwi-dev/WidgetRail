using WidgetRail.WidgetProtocol;

internal static partial class DiscordSharingTests
{
    private static async Task RepeatedSharingAsync(Action<bool,string> check)
    {
        await using(var fixture=new SharingFixture())
        {
            fixture.Files=fixture.Files.Select((file,index)=>file with{ContentIdentity=(index+1).ToString("x32")}).ToArray();
            var original=fixture.Files;
            await fixture.Start();await fixture.Widget.OnIntentAsync(fixture.Request);
            await fixture.Until(()=>fixture.Find("discord.conversation.10") is not null);
            await fixture.Act("discord.conversation.10");await fixture.Act("discord.message.draft","Keep my first draft");
            var cancelled=new WidgetFileReference(new string('e',32),"new.png","image/png",10,long.MaxValue){ContentIdentity=new string('3',32)};
            fixture.Files=[cancelled];
            check(await fixture.Widget.OnIntentAsync(fixture.Request)==WidgetIntentResult.Accepted &&
                fixture.Find("discord.messages.search") is not null && fixture.Find("discord.message.draft") is null,
                "Every external Share opens recipient selection even with an existing text/attachment draft");
            check(fixture.Find("discord.incoming.file.0")?.Text=="new.png · Remove" && fixture.Find("discord.messages.cancel-share") is not null,
                "Incoming files and Cancel remain fixed and visible before recipient choice");
            await fixture.Act("discord.messages.cancel-share");await fixture.Until(()=>fixture.Released.Contains(cancelled.Id));
            await fixture.Act("discord.conversation.10");
            check(fixture.Find("discord.message.draft")?.TextEntryValue=="Keep my first draft" && fixture.Find("discord.message.file.1") is not null &&
                original.All(file=>!fixture.Released.Contains(file.Id)),"Cancelling recipient selection preserves the existing recipient text and files");
            var backed=cancelled with{Id=new string('7',32)};
            fixture.Files=[backed];await fixture.Widget.OnIntentAsync(fixture.Request);await fixture.Act("discord.messages.back");
            await fixture.Until(()=>fixture.Released.Contains(backed.Id));
            await fixture.Act("discord.messages.open");await fixture.Act("discord.conversation.10");
            check(fixture.Find("discord.message.draft")?.TextEntryValue=="Keep my first draft" && fixture.Find("discord.message.file.1") is not null,
                "Back from recipient selection cancels only its incoming batch and preserves the prior draft");

            var second=new WidgetFileReference(new string('f',32),"other.png","image/png",11,long.MaxValue){ContentIdentity=new string('4',32)};
            fixture.Files=[second];await fixture.Widget.OnIntentAsync(fixture.Request);await fixture.Act("discord.conversation.11");
            check(fixture.Find("discord.message.draft")?.TextEntryValue=="" && fixture.Find("discord.message.file.0")?.Text=="other.png · Remove" &&
                fixture.Find("discord.message.file.1") is null,"Choosing another recipient carries only incoming files, not another conversation's draft");
            await fixture.Act("discord.message.draft","Second conversation text");await fixture.Act("discord.messages.back");await fixture.Act("discord.conversation.10");
            check(fixture.Find("discord.message.draft")?.TextEntryValue=="Keep my first draft" && fixture.Find("discord.message.file.1") is not null,
                "Back and reopen restores the exact first conversation draft");
            var duplicate=original[0] with{Id=new string('8',32)};
            var sameLabel=original[0] with{Id=new string('9',32),ContentIdentity=new string('5',32)};
            fixture.Files=[duplicate,sameLabel];await fixture.Widget.OnIntentAsync(fixture.Request);await fixture.Act("discord.conversation.10");
            await fixture.Until(()=>fixture.Released.Contains(duplicate.Id));
            check(fixture.Find("discord.message.file.2") is not null && fixture.Find("discord.message.file.3") is null &&
                fixture.Find("discord.message.draft")?.TextEntryValue=="Keep my first draft",
                "Same-recipient choice appends unique content and deduplicates a new grant of the same original without losing text");
            check(!fixture.Released.Contains(sameLabel.Id) && !fixture.Released.Contains(original[0].Id) &&
                fixture.Released.Count(id=>id==duplicate.Id)==1,"Equal name and size do not collapse distinct files; only the superseded duplicate grant is released");
            await fixture.Act("discord.message.clear-files");
            await fixture.Until(()=>original.All(file=>fixture.Released.Contains(file.Id)) && fixture.Released.Contains(sameLabel.Id));
            check(fixture.Find("discord.message.file.0") is null && fixture.Find("discord.message.draft")?.TextEntryValue=="Keep my first draft",
                "Fixed Clear attachments removes files while preserving unsent text");
            await fixture.Act("discord.messages.back");await fixture.Act("discord.conversation.11");
            check(fixture.Find("discord.message.file.0")?.Text=="other.png · Remove" && fixture.Find("discord.message.draft")?.TextEntryValue=="Second conversation text",
                "Clearing one conversation never changes another recipient's retained draft");
            await fixture.ChangeAccount("2");await fixture.Until(()=>fixture.Released.Contains(second.Id));
            check(fixture.Released.GroupBy(id=>id).All(group=>group.Count()==1) && fixture.Session.Mutations==0 && fixture.Reads==0,
                "Account retirement releases retained drafts exactly once without any implicit read, upload or send");
        }
        await using(var limit=new SharingFixture())
        {
            limit.Files=Enumerable.Range(1,11).Select(index=>new WidgetFileReference(index.ToString("x32"),"file"+index+".txt","text/plain",1,long.MaxValue)).ToArray();
            await limit.Start();check(await limit.Widget.OnIntentAsync(limit.Request)==WidgetIntentResult.Accepted,
                "A provider attachment limit is handled inside the receiver rather than as destination-unavailable");
            await limit.Until(()=>limit.Find("discord.conversation.10") is not null);await limit.Act("discord.conversation.10");
            check(limit.Find("discord.messages.notice")?.Text?.Contains("allows 10")==true && limit.Find("discord.message.draft") is null,
                "Over-limit incoming files stay removable with a specific actionable provider limit");
            await limit.Act("discord.incoming.file.10");await limit.Act("discord.conversation.10");
            check(limit.Find("discord.message.file.9") is not null && limit.Find("discord.message.clear-files") is not null,
                "Removing the excess file allows recipient selection and leaves all attachment controls fixed");
        }
        await using(var stale=new SharingFixture())
        {
            await stale.Start();await stale.Act("discord.messages.open");await stale.Until(()=>stale.Find("discord.conversation.10") is not null);
            await stale.Act("discord.conversation.10");stale.AcceptGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var receiving=stale.Widget.OnIntentAsync(stale.Request).AsTask();await stale.Until(()=>stale.Accepts==1);
            await stale.Act("discord.messages.back");stale.AcceptGate.SetResult();
            check(await receiving==WidgetIntentResult.Rejected,"A delayed acceptance cannot reopen recipient selection after the user navigates away");
            await stale.Until(()=>stale.Released.Count==2);
        }
        await using(var edited=new SharingFixture())
        {
            await edited.Start();await edited.Act("discord.messages.open");await edited.Until(()=>edited.Find("discord.conversation.10") is not null);
            await edited.Act("discord.conversation.10");edited.AcceptGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var receiving=edited.Widget.OnIntentAsync(edited.Request).AsTask();await edited.Until(()=>edited.Accepts==1);
            await edited.Act("discord.message.draft","Edited while acceptance was pending");edited.AcceptGate.SetResult();
            check(await receiving==WidgetIntentResult.Accepted,"An in-scope draft edit does not turn incoming sharing into a generic destination error");
            await edited.Act("discord.conversation.10");
            check(edited.Find("discord.message.draft")?.TextEntryValue=="Edited while acceptance was pending" && edited.Find("discord.message.file.1") is not null,
                "Recipient choice preserves text edited during file acceptance");
        }
        await using(var size=new SharingFixture())
        {
            size.Files=[size.Files[0] with{ByteLength=2048},size.Files[1]];
            await size.Start();await size.Widget.OnIntentAsync(size.Request);await size.Until(()=>size.Find("discord.conversation.10") is not null);
            await size.Act("discord.conversation.10");
            check(size.Find("discord.messages.notice")?.Text?.Contains("achievement.png exceeds")==true && size.Find("discord.incoming.file.0") is not null,
                "Account size limits identify the oversized file while keeping removal and cancellation available");
            await size.Act("discord.incoming.file.0");await size.Act("discord.conversation.10");
            check(size.Find("discord.message.file.0")?.Text=="clip.mp4 · Remove","Removing an oversized attachment allows the remaining share to continue");
        }
    }
}
