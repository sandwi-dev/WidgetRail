using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class DiscordSharingTests
{
    private static async Task ColdSharingAsync(Action<bool,string> check)
    {
        await using(var cold=await Cold())
        {
            check(await cold.Widget.IsIntentAvailableAsync(cold.Request),"A genuinely initial Discord read does not exclude its installed file-share handler");
            using var callback=new CancellationTokenSource();
            var accepting=cold.Widget.OnIntentAsync(cold.Request,callback.Token).AsTask();
            check(await accepting.WaitAsync(TimeSpan.FromSeconds(1))==WidgetIntentResult.Accepted,"Cold sharing accepts custody without waiting for browser readiness inside the intent callback");
            callback.Cancel();
            check(cold.Find("discord.messages.cancel-share") is not null && cold.Find("discord.message.send") is null && cold.Find("discord.messages.search") is null,
                "Unbound incoming files expose Cancel while the first account snapshot is pending");
            await Task.Delay(2100);
            check(cold.Accepts==1 && cold.Session.Mutations==0 && cold.Reads==0 && cold.Released.IsEmpty,
                "Accepted file custody survives beyond the intent RPC deadline without upload or source-offer reuse");
            cold.Session.ReadGate!.TrySetResult();await cold.Until(()=>cold.Find("discord.conversation.10") is not null);
            check(cold.Find("discord.messages.notice")?.Text=="Choose a recipient for 2 shared file(s)." && cold.Find("discord.message.send") is null,
                "First verified readiness binds the pending files to recipient review without sending");
            await cold.ChangeAccount("2");await cold.Until(()=>cold.Released.Count==2);
            check(cold.Find("discord.messages.cancel-share") is null && cold.Session.Mutations==0,
                "Account replacement after first binding retires the cold share rather than carrying files to another account");
        }
        await using(var repeated=await Cold())
        {
            await repeated.Widget.OnIntentAsync(repeated.Request);
            repeated.Files=[new(new string('e',32),"next-capture.png","image/png",10,long.MaxValue)];
            check(await repeated.Widget.OnIntentAsync(repeated.Request)==WidgetIntentResult.Accepted &&
                repeated.Find("discord.incoming.file.2") is not null && repeated.Released.IsEmpty,
                "Repeated sharing during cold initialization preserves both receipts and keeps recipient review pending");
            repeated.Session.ReadGate!.SetResult();await repeated.Until(()=>repeated.Find("discord.conversation.10") is not null);
            check(repeated.Find("discord.messages.notice")?.Text?.Contains("3 shared file")==true && repeated.Session.Mutations==0,
                "Only the current readiness wait binds the combined cold share without choosing a recipient or sending");
            await repeated.Act("discord.messages.cancel-share");await repeated.Until(()=>repeated.Released.Count==3);
            check(repeated.Released.Distinct().Count()==3,"Cancelling repeated cold intake releases each owned grant once");
        }
        await using(var race=await Cold())
        {
            race.AcceptGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var accepting=race.Widget.OnIntentAsync(race.Request).AsTask();await race.Until(()=>race.Accepts==1);
            race.Session.ReadGate!.TrySetResult();await race.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            race.AcceptGate.TrySetResult();check(await accepting==WidgetIntentResult.Accepted,"Read completion during file acceptance still adopts the authorized share");
            await race.Until(()=>race.Find("discord.conversation.10") is not null);
            check(race.Find("discord.messages.pending") is null && race.Session.Mutations==0,"Already-ready account state does not leave an accepted cold share waiting forever");
        }
        await using(var activated=new SharingFixture())
        {
            activated.Session.FailRead=true;
            await WidgetTestHost.InitializeAsync(activated.Widget);await activated.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(!await activated.Widget.IsIntentAvailableAsync(activated.Request),"An initial Background read failure does not pretend to be a ready account");
            activated.Session.FailRead=false;activated.Session.ReadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await WidgetTestHost.SetLifecycleStateAsync(activated.Widget,WidgetLifecycleState.Interactive);
            check(await activated.Widget.OnIntentAsync(activated.Request)==WidgetIntentResult.Accepted,
                "Static handler activation reuses the normal permitted read before accepting a pending cold share");
            activated.Session.ReadGate.TrySetResult();await activated.Until(()=>activated.Find("discord.conversation.10") is not null);
            check(activated.Session.Mutations==0 && activated.Reads==0,"Activated cold intake reaches recipient review without uploading or sending");
        }
        foreach(var beforeReceive in new[]{false,true})
        {
            await using var hydrating=await Cold();
            hydrating.Session.Inner.State=hydrating.Session.Inner.State with{Ready=false,Account=""};
            if(beforeReceive){hydrating.Session.ReadGate!.TrySetResult();await hydrating.Widget.WaitForStateReadAsync();}
            check(await hydrating.Widget.IsIntentAvailableAsync(hydrating.Request) && await hydrating.Widget.OnIntentAsync(hydrating.Request)==WidgetIntentResult.Accepted,
                "Initial disconnected account remains unknown whether published before or after receipt: "+beforeReceive);
            hydrating.Session.ReadGate!.TrySetResult();await hydrating.Widget.WaitForStateReadAsync();
            check(hydrating.Find("discord.messages.cancel-share") is not null && hydrating.Find("discord.account.open") is {IsDisabled:not true} && hydrating.Released.IsEmpty,
                "Initial hydration or fresh sign-out keeps cancellable local files and the existing Open Discord path");
            hydrating.Session.Inner.State=hydrating.Session.Inner.State with{Ready=true,Account="1"};
            await hydrating.Widget.OnActionAsync(new("refresh","test"));await hydrating.Until(()=>hydrating.Find("discord.conversation.10") is not null);
            check(hydrating.Accepts==1 && hydrating.Session.Mutations==0 && hydrating.Reads==0,"Later verified account binds the same receipt without reaccepting or uploading");
        }
        foreach(var outcome in new[]{"cancel","back","background","error","cannot-send","session-change"})
        {
            await using var cold=await Cold();
            check(await cold.Widget.OnIntentAsync(cold.Request)==WidgetIntentResult.Accepted,"Pending cold share is admitted before outcome: "+outcome);
            if(outcome=="cancel")await cold.Act("discord.messages.cancel-share");
            if(outcome=="back")await cold.Act("discord.messages.back");
            if(outcome=="background")await WidgetTestHost.SetLifecycleStateAsync(cold.Widget,WidgetLifecycleState.Background);
            if(outcome=="error")cold.Session.FailRead=true;
            if(outcome=="cannot-send")cold.Session.CanSend=false;
            if(outcome=="session-change")cold.Session.Inner.Reference=new(new string('d',32));
            cold.Session.ReadGate!.TrySetResult();await cold.Until(()=>cold.Released.Count==2);
            await cold.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(cold.Accepts==1 && cold.Find("discord.conversation.10") is null && cold.Find("discord.message.send") is null && cold.Session.Mutations==0,
                "Pending share retires exactly its accepted files without late recipient adoption: "+outcome);
        }
        await using(var replaced=await Cold())
        {
            replaced.Session.ReferenceUnavailable=true;
            replaced.Session.Inner.State=replaced.Session.Inner.State with{Ready=false,Account=""};
            check(await replaced.Widget.OnIntentAsync(replaced.Request)==WidgetIntentResult.Accepted,"An unopened backend can accept unbound file custody");
            replaced.Session.Inner.Reference=new(new string('a',32));replaced.Session.ReferenceUnavailable=false;replaced.Session.ReadGate!.TrySetResult();
            await replaced.Widget.WaitForStateReadAsync();
            replaced.Session.Inner.Reference=new(new string('b',32));replaced.Session.Inner.State=replaced.Session.Inner.State with{Ready=true,Account="1"};
            await replaced.Widget.OnActionAsync(new("refresh","test"));await replaced.Until(()=>replaced.Released.Count==2);
            check(replaced.Session.ConversationReads==0 && replaced.Session.Mutations==0,
                "Replacing the first observed backend session during hydration cannot adopt pending files into its successor");
        }
        foreach(var unavailable in new[]{"signedout","cannot-send"})
        {
            await using var fixture=new SharingFixture();
            if(unavailable=="cannot-send")fixture.Session.CanSend=false;
            await fixture.Start();
            if(unavailable=="signedout")
            {
                fixture.Session.Inner.State=fixture.Session.Inner.State with{Ready=false,Account=""};
                await fixture.Widget.OnActionAsync(new("refresh","test"));await fixture.Widget.WaitForStateReadAsync();
            }
            check(!await fixture.Widget.IsIntentAvailableAsync(fixture.Request) && await fixture.Widget.OnIntentAsync(fixture.Request)==WidgetIntentResult.Rejected && fixture.Accepts==0,
                "Known provider unavailability rejects incoming file custody: "+unavailable);
        }
        await using(var known=new SharingFixture())
        {
            known.Session.CanSend=false;await known.Start();known.Session.FailRead=true;
            await known.Widget.OnActionAsync(new("refresh","test"));await known.Widget.WaitForStateReadAsync();
            known.Session.FailRead=false;known.Session.ReadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await known.Widget.OnActionAsync(new("refresh","test"));
            check(!await known.Widget.IsIntentAvailableAsync(known.Request),"A later retry after known account unavailability is not mistaken for a never-observed cold account");
        }
        var destroyed=await Cold();var stopped=false;
        try
        {
            _=await destroyed.Widget.OnIntentAsync(destroyed.Request);
            await WidgetTestHost.DestroyAsync(destroyed.Widget).AsTask().WaitAsync(TimeSpan.FromSeconds(5));stopped=true;
            destroyed.Session.ReadGate!.TrySetResult();
            check(destroyed.Session.ConversationReads==0 && destroyed.Session.Mutations==0,
                "Worker destruction cancels pending account binding without a late recipient query or external mutation");
        }
        finally{if(!stopped)await destroyed.DisposeAsync();}

        await using(var reduced=await Cold())
        {
            await reduced.Widget.OnIntentAsync(reduced.Request);
            await reduced.Act("discord.incoming.file.0");await reduced.Until(()=>reduced.Released.Count==1);
            reduced.Session.ReadGate!.TrySetResult();await reduced.Until(()=>reduced.Find("discord.conversation.10") is not null);
            check(reduced.Find("discord.incoming.file.0")?.Text=="clip.mp4 · Remove" && reduced.Find("discord.incoming.file.1") is null,
                "Removing one cold incoming file retains account initialization for the remaining grant");
            await reduced.Act("discord.messages.cancel-share");await reduced.Until(()=>reduced.Released.Count==2);
            check(reduced.Released.Distinct().Count()==2,"Cold removal followed by Cancel releases each original grant exactly once");
        }
        foreach(var boundary in new[]{"session","account"})
        {
            await using var changed=await Cold();await changed.Widget.OnIntentAsync(changed.Request);
            if(boundary=="session")changed.Session.Inner.Reference=new(new string('d',32));
            await changed.Act("discord.incoming.file.0");await changed.Until(()=>changed.Released.Count>=1);
            changed.Session.ReadGate!.TrySetResult();
            if(boundary=="account")
            {
                await changed.Until(()=>changed.Find("discord.conversation.10") is not null);
                await changed.ChangeAccount("2");
            }
            await changed.Until(()=>changed.Released.Count==2);
            check(changed.Released.Distinct().Count()==2 && changed.Find("discord.incoming.file.0") is null && changed.Session.Mutations==0,
                "Cold removal keeps the first session/account boundary and releases the remaining grant once on replacement: "+boundary);
        }
        await using(var detour=await Cold())
        {
            check(await detour.Widget.OnIntentAsync(detour.Request)==WidgetIntentResult.Accepted,"Cold share accepts file custody before browser detour");
            var notification=WidgetIntentRequest.Create(WidgetIntentContracts.WebNotification,JsonSerializer.SerializeToElement(new
                {sessionId=detour.Session.Reference!.Id,tag="105"}));
            check(await detour.Widget.OnIntentAsync(notification)==WidgetIntentResult.Rejected && detour.Find("discord.account.browser") is not null,
                "Notification during cold recipient initialization uses the existing browser fallback");
            detour.Session.ReadGate!.TrySetResult();await detour.Widget.WaitForStateReadAsync();
            await detour.Until(()=>detour.Session.ConversationReads>0);
            await detour.Act("discord.account.back");
            await detour.Until(()=>detour.Find("discord.conversation.10") is not null);
            check(detour.Find("discord.incoming.file.1") is not null && detour.Released.IsEmpty && detour.Session.Mutations==0 && detour.Reads==0,
                "Back from a cold browser detour resumes recipient selection with exact accepted custody and no send");
            await detour.Act("discord.messages.cancel-share");await detour.Until(()=>detour.Released.Count==2);
            check(detour.Released.Distinct().Count()==2,"Explicit Cancel after a cold browser detour releases each accepted grant once");
        }

        static async Task<SharingFixture> Cold()
        {
            var fixture=new SharingFixture();fixture.Session.ReadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await WidgetTestHost.InitializeAsync(fixture.Widget);await WidgetTestHost.SetLifecycleStateAsync(fixture.Widget,WidgetLifecycleState.Interactive);
            await fixture.Session.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));return fixture;
        }
    }
}
