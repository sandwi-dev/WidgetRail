using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task PrivateCallNotificationsAsync(Action<bool,string> check)
    {
        var manifest=ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"discord-manifest.json")));
        var manifestErrors=WidgetManifestValidator.Validate(manifest);
        check(manifestErrors.Count==0,"Discord declares a valid incoming-call notification category and navigation intent: "+string.Join("; ",manifestErrors));
        var contract=CompiledWidgetIntentContract.Create(manifest.Intents!.Handles.Single(item=>item.Id==DiscordWebViewWidget.OpenCallContract.Id));
        var requestContract=CompiledWidgetIntentContract.Create(manifest.Intents.Requests.Single(item=>item.Id==DiscordWebViewWidget.OpenCallContract.Id));
        var session=new MessagesSession();
        session.Inner.State=session.Inner.State with{ChannelId=null,Participants=[],Streams=[],VoiceConnection="disconnected",Calls=new(true,true,true,true,[],[])};
        await using var scenario=await Scenario.OpenAsync(session,new HoldPollingClock());
        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Background);
        var invite=new DiscordCallInvitation(new string('b',32),"20","Synthetic incoming caller");
        session.Inner.State=session.Inner.State with{Calls=new(true,true,true,true,[invite],[])};
        session.Inner.NotifyChanged();
        await Wait(()=>Notices().Any(command=>command.Notification is not null));
        var shown=Notices().Single().Notification!;
        check(shown.CategoryId=="calls" && shown.Retention==WidgetNotificationRetention.Pending && shown.Visibility==WidgetNotificationVisibility.SourceHidden,
            "A hidden widget consumes provider invalidation and publishes one host-owned call alert without the polling timer");
        var intent=shown.OpenIntent!;
        check(intent.IsWellFormed() && intent.SchemaDigest==contract.SchemaDigest && contract.Accepts(intent.Payload),
            "Call alert navigation matches the exact declared manifest contract");
        check(intent.SchemaDigest==requestContract.SchemaDigest && requestContract.Accepts(intent.Payload),
            "The notification source also declares its exact outgoing intent so the bridge can admit the alert");
        check(session.Inner.MicrophoneRequests==0 && session.Inner.NotificationRequests==0 && session.Calls.All(call=>!call.Method.StartsWith("call",StringComparison.Ordinal)),
            "Background call detection grants no permissions and performs no call action");
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        check(Notices().Count(command=>command.Notification is not null&&!command.UpdateOnly)==1,"Repeated ringing snapshots do not produce repeated banners");
        session.Inner.State=session.Inner.State with{Calls=new(true,true,true,true,[invite with{Name="Updated caller name"}],[])};
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>Notices().Any(command=>command.UpdateOnly));
        check(Notices().Last().Notification?.Body=="Updated caller name","Caller metadata updates the existing alert without a new event");
        var second=invite with{Id=new string('c',32),ChannelId="30",Name="Second incoming caller"};
        session.Inner.State=session.Inner.State with{Calls=new(true,true,true,true,[invite,second],[])};
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>Notices().Last().Notification?.Body==second.Name);
        check(!Notices().Last().UpdateOnly && Notices().Last().Notification?.Title=="Incoming calls",
            "A second invitation is a new alert event rather than quiet metadata for the earlier caller");

        await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Interactive);
        await scenario.Act("discord.account.open");
        check(await scenario.Widget.OnIntentAsync(intent)==WidgetIntentResult.Accepted,"Opening a current call alert leaves the account page for native call controls");
        check(scenario.Snapshot().FocusGroupEntryRequest?.TargetId=="discord.incoming.answer."+invite.Id &&
            WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,"Call-alert navigation requests focus through the supported exact-target API");
        check(session.Inner.MicrophoneRequests==0 && session.Calls.All(call=>!call.Method.StartsWith("call",StringComparison.Ordinal)),
            "Opening a call alert is navigation only, never automatic Answer");
        session.Inner.State=session.Inner.State with{Calls=new(true,true,true,true,[],[])};
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>Notices().Last().Notification is null);
        check(scenario.Snapshot().FocusGroupEntryRequest is null,"Ending an invitation withdraws its alert and focus preference");
        check(await scenario.Widget.OnIntentAsync(intent)==WidgetIntentResult.Rejected,"A dismissed invitation cannot navigate through its stale alert");

        session.Inner.State=session.Inner.State with{Calls=new(true,true,true,true,[invite with{Id=new string('c',32)}],[])};
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>Notices().Last().Notification is not null);
        var accountIntent=Notices().Last().Notification!.OpenIntent!;
        session.Inner.State=session.Inner.State with{Account="2",Calls=new(true,true,true,true,[],[])};
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync();
        await Wait(()=>Notices().Last().Notification is null);
        check(await scenario.Widget.OnIntentAsync(accountIntent)==WidgetIntentResult.Rejected,"Account replacement withdraws the prior call alert and rejects its navigation");

        WidgetNotificationCommand[] Notices()=>scenario.Notifications.Where(command=>command.Key=="discord-incoming-call").ToArray();
    }

    // Hold only the existing periodic fallback. Native backend invalidations
    // remain the sole way for the hidden invitation above to become observable.
    private sealed class HoldPollingClock:TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)=>
            dueTime>=TimeSpan.FromSeconds(5)?new HeldTimer():TimeProvider.System.CreateTimer(callback,state,dueTime,period);
        private sealed class HeldTimer:ITimer
        {
            public bool Change(TimeSpan dueTime,TimeSpan period)=>true;
            public void Dispose(){}
            public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
        }
    }
}
