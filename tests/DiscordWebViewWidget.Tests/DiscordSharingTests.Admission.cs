using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class DiscordSharingTests
{
    private static async Task RecipientAdmissionAsync(Action<bool,string> check)
    {
        await using(var fixture=new SharingFixture())
        {
            await fixture.Start();await fixture.Widget.OnIntentAsync(fixture.Request);
            await fixture.Until(()=>fixture.Find("discord.conversation.10") is not null);
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget,"recipient-admission");rows.PublishSnapshot();
            using var old=await rows.AcquireAsync("discord.messages.list",0,1);
            fixture.Request=WidgetIntentRequest.Create(WidgetIntentContracts.Files,JsonSerializer.SerializeToElement(new{offerId=new string('d',32)}));
            check(await fixture.Widget.OnIntentAsync(fixture.Request)==WidgetIntentResult.Accepted,"A new offer is accepted while the prior recipient action is queued");
            rows.PublishSnapshot();
            check(old.RouteAction("10",ControllerButton.A) is null,"An earlier offer's recipient lease loses action authority");
            check(fixture.Find("discord.messages.cancel-share") is not null && fixture.Find("discord.message.draft") is null &&
                fixture.Find("discord.incoming.file.1") is not null && fixture.Released.IsEmpty,
                "Queued recipient action from an older offer cannot consume a new share even when its granted files are identical");
            await fixture.Act("discord.conversation.10");
            check(fixture.Find("discord.message.file.1") is not null && fixture.Session.Mutations==0,"Only a recipient action declared for the current offer adopts its files without sending");
        }
        foreach(var outcome in new[]{"complete","cancel","account"})
        {
            await using var fixture=new SharingFixture();fixture.Session.FriendOnly=true;
            fixture.Session.OpenGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await fixture.Start();await fixture.Widget.OnIntentAsync(fixture.Request);
            await fixture.Until(()=>fixture.Find("discord.conversation.10") is not null);
            var opening=fixture.Act("discord.conversation.10");await fixture.Until(()=>fixture.Session.OpenRequests==1);
            fixture.Request=WidgetIntentRequest.Create(WidgetIntentContracts.Files,JsonSerializer.SerializeToElement(new{offerId=new string('d',32)}));
            fixture.Files=[new(new string('e',32),"second.png","image/png",10,long.MaxValue)];
            check(await fixture.Widget.OnIntentAsync(fixture.Request)==WidgetIntentResult.Accepted,"New incoming files are accepted while an older friend-open is awaited: "+outcome);
            if(outcome=="cancel")await fixture.Act("discord.messages.cancel-share");
            if(outcome=="account")await fixture.ChangeAccount("2");
            fixture.Session.OpenGate.TrySetResult();await opening;
            check(fixture.Find("discord.message.draft") is null && fixture.Session.Mutations==0 && fixture.Reads==0,
                "Late friend-open cannot navigate or assign incoming files after its selection was replaced: "+outcome);
            if(outcome=="complete")
            {
                check(fixture.Find("discord.incoming.file.2") is not null && fixture.Released.IsEmpty,"The new incoming offer keeps all three grants after the stale friend-open completes");
                await fixture.Act("discord.conversation.10");
                check(fixture.Find("discord.message.file.2") is not null && fixture.Session.OpenRequests==2,"A fresh explicit recipient selection adopts the combined incoming files");
            }
            else
            {
                await fixture.Until(()=>fixture.Released.Count==3);
                check(fixture.Released.Distinct().Count()==3,"Cancelled or account-retired pending files release exactly once: "+outcome);
            }
        }
    }
}
