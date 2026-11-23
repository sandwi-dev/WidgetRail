using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    internal static async Task UploadChecksAsync(Action<bool,string> check)
    {await UploadCancellationAsync(check);await SubmittedCancellationAsync(check);await ShareWhileSendingAsync(check);}
    private static async Task UploadCancellationAsync(Action<bool,string> check)
    {
        foreach(var ignoreCancellation in new[]{false,true})
        {
            await using var scenario=await Scenario.OpenAsync();
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            await scenario.Act("discord.message.attach");await Wait(()=>scenario.Find("discord.message.file.0") is not null);
            await scenario.Act("discord.message.draft","Keep this upload draft");
            scenario.UploadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);scenario.IgnoreUploadCancellation=ignoreCancellation;
            await scenario.Act("discord.message.send");
            try {await Wait(()=>scenario.Find("discord.message.status")?.Text=="Uploading files… 33%");}
            catch(OperationCanceledException error)
            {throw new InvalidOperationException($"Upload did not reach progress: uploads={scenario.Uploads}, sends={scenario.Session.Sends}, status={scenario.Find("discord.message.status")?.Text}, inbox={scenario.Find("discord.messages.notice")?.Text}",error);}
            var staleCancel=scenario.Find("discord.message.cancel")!.ActionId!;
            check(scenario.Find("discord.message.cancel")?.Text=="Cancel upload" && scenario.Session.Sends==0,
                "Upload reports admitted byte progress and exposes Cancel before posting a message");
            check(WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,"Uploading composer uses valid existing shared controls");
            await scenario.Act("discord.message.cancel");
            if(ignoreCancellation)
            {
                check(scenario.Find("discord.message.cancel")?.IsDisabled==true,"Cancellation stays visible while an uncooperative upload unwinds");
                scenario.UploadGate.TrySetResult();
            }
            await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Wait(()=>scenario.Notifications.Any(n=>n.Notification?.Body=="Upload cancelled. Your draft and files are kept."));
            check(scenario.Session.Sends==0 && scenario.Find("discord.message.draft")?.TextEntryValue=="Keep this upload draft" &&
                scenario.Find("discord.message.file.0") is not null && scenario.Released.IsEmpty,
                "Cancelled upload retains draft/file authority and never posts, including late completion ignoring cancellation");
            check(scenario.Find("discord.message.status") is null && scenario.Notifications.All(n=>n.Notification?.Retention==WidgetRail.WidgetProtocol.WidgetNotificationRetention.BannerOnly),
                "Upload cancellation uses expiring feedback while leaving the composer free of permanent outcome text");
            scenario.IgnoreUploadCancellation=false;scenario.UploadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await scenario.Act("discord.message.send");await Wait(()=>scenario.Find("discord.message.status")?.Text=="Uploading files… 33%");
            await scenario.Widget.OnActionAsync(new(staleCancel,"discord.message.cancel",InputScopeId:scenario.Snapshot().ActiveInputScopeId));
            check(scenario.Find("discord.message.cancel")?.IsDisabled!=true,"An old Cancel action cannot cancel a newer upload nonce");
            scenario.UploadGate.TrySetResult();await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(scenario.Session.Sends==1,"A fresh explicit retry posts exactly once after the cancelled upload");
        }

        await using var departed=await Scenario.OpenAsync();
        await departed.Act("discord.conversation.10");await departed.WaitForHistory();await departed.Act("discord.message.attach");
        await Wait(()=>departed.Find("discord.message.file.0") is not null);
        departed.UploadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);departed.IgnoreUploadCancellation=true;
        await departed.Act("discord.message.send");await Wait(()=>departed.Find("discord.message.status")?.Text=="Uploading files… 33%");
        departed.Session.Inner.State=departed.Session.Inner.State with{Account="2"};
        await departed.Widget.OnActionAsync(new("refresh","test"));await departed.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        departed.UploadGate.TrySetResult();await Wait(()=>departed.Find("discord.message.draft") is null);
        check(departed.Session.Sends==0,"Account replacement cancels the upload and cannot retarget its late completion");
    }

    private static async Task SubmittedCancellationAsync(Action<bool,string> check)
    {
        foreach(var lateAcknowledgement in new[]{false,true})
        {
            await using var scenario=await Scenario.OpenAsync();
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            await scenario.Act("discord.message.draft","Submitted content");
            scenario.Session.SendGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Session.IgnoreSendCancellation=lateAcknowledgement;
            await scenario.Act("discord.message.send");await Wait(()=>scenario.Session.Sends==1);
            check(scenario.Find("discord.message.cancel")?.Text=="Stop waiting","A submitted send labels cancellation as Stop waiting");
            await scenario.Act("discord.message.cancel");
            if(lateAcknowledgement)
            {
                await scenario.Act("discord.message.draft","A newer draft");scenario.Session.SendGate.TrySetResult();
                await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(scenario.Session.Sends==1 && scenario.Find("discord.message.draft")?.TextEntryValue=="A newer draft",
                    "A late confirmed send is reconciled after Stop waiting without discarding newer text");
            }
            else
            {
                await Wait(()=>scenario.Find("discord.message.status")?.Text?.StartsWith("Stopped waiting.",StringComparison.Ordinal)==true && scenario.Find("discord.message.cancel") is null);
                check(scenario.Find("discord.message.send")?.Text=="Check delivery","Cancellation after submission preserves an ambiguous nonce for reconciliation");
                scenario.Session.SendGate=null;await scenario.Act("discord.message.send");await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(scenario.Session.Sends==1 && scenario.Session.Reconciles==1,"Check delivery after cancellation never repeats the external POST");
            }
        }
    }
}
