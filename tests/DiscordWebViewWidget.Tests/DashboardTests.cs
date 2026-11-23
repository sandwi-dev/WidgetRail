using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class DashboardTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var fake=new FakeSession();
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"discord.dashboard");
            var snapshot=host.PublishSnapshot();
            check(snapshot.QuickActions.Select(action=>action.Label).SequenceEqual(new[]{"Deafen","Unmute","Leave call"}),
                "Discord dashboard declares state-aware call controls through the shared quick-action contract");
            check(WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0,
                "Discord dashboard actions satisfy protocol and native presentation admission");
            var stale=snapshot.QuickActions.Single(action=>action.Button==ControllerButton.X).ActionId;

            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Background);
            // The production dashboard promotes its selected widget to Visible
            // before dispatch; Background itself grants no action admission.
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Visible);
            snapshot=host.PublishSnapshot();
            fake.CommandGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            check(await widget.OnControllerInputAsync(new(ControllerButton.X,ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction,Sequence:1,SnapshotSequence:snapshot.Sequence)),
                "Dashboard mute is admitted with the host's Visible lifecycle without opening the Discord page");
            await Wait(()=>fake.Commands.Count==1);
            check(fake.Commands.Single().Method=="mute" && !fake.Commands.Single().Args.GetProperty("value").GetBoolean(),
                "Dashboard mute uses the existing desired-state command against the current call");
            check(!await widget.OnControllerInputAsync(new(ControllerButton.X,ControllerEventPhase.Repeated,
                ControllerInputContext.DashboardQuickAction,SnapshotSequence:snapshot.Sequence)),
                "Holding a dashboard toggle never repeats it");
            check(host.PublishSnapshot().QuickActions.Count==0,"In-flight call commands withdraw dashboard actions");
            fake.CommandGate.TrySetResult();fake.CommandGate=null;
            await Wait(()=>widget.Render().QuickActions?.Count==3);

            fake.State=fake.State with{Muted=false,Deafened=true};await Refresh();
            snapshot=host.PublishSnapshot();
            check(snapshot.QuickActions.Select(action=>action.Label).SequenceEqual(new[]{"Undeafen","Mute","Leave call"}),
                "Externally changed call state updates dashboard labels");
            await widget.OnActionAsync(new(stale,"dashboard-card"));
            check(fake.Commands.Count==1,"A delayed dashboard toggle cannot invert externally changed mute state");
            stale=snapshot.QuickActions.Single(action=>action.Button==ControllerButton.RightBumper).ActionId;
            fake.State=fake.State with{Account="2",ChannelId="20"};await Refresh();host.PublishSnapshot();
            await widget.OnActionAsync(new(stale,"dashboard-card"));
            check(fake.Commands.Count==1,"A delayed dashboard leave cannot disconnect a replacement account/call");
            var current = host.PublishSnapshot();
            var mute = current.QuickActions.Single(action => action.TrayOffer?.Id == "mute");
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            check((await WidgetTestHost.InvokeTrayActionAsync(widget, current, mute)).Handled, "Tray mute works with a resident background call");
            await Wait(() => fake.Commands.Count == 2);
            check(fake.Commands.Last().Args.GetProperty("channelId").GetString() == "20" && !widget.IsActive, "Tray mute targets only the offered call");
            var retry = host.PublishSnapshot();
            fake.CommandGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.FailCommands = true;
            var failure = WidgetTestHost.InvokeTrayActionAsync(widget, retry, retry.QuickActions.Single(item => item.TrayOffer?.Id == "mute")).AsTask();
            await Wait(() => fake.Commands.Count == 3);
            check(!failure.IsCompleted, "Tray mute waits for its Discord command reply");
            fake.CommandGate.SetResult();
            var rejected = await failure.WaitAsync(TimeSpan.FromSeconds(5));
            check(!rejected.Handled && rejected.Reason?.Contains("could not confirm", StringComparison.Ordinal) == true,
                "A refused Discord mute reports a single user-facing outcome");
            fake.FailCommands = false;
            fake.CommandGate = null;
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

            fake.State=fake.State with{Capabilities=fake.State.Capabilities with{Mute=false,Deafen=false}};await Refresh();
            check(host.PublishSnapshot().QuickActions is [{Label:"Leave call"}],"Unsupported dashboard controls are not advertised");
            fake.State=fake.State with{ChannelId=null};await Refresh();
            snapshot = host.PublishSnapshot();
            check(snapshot.QuickActions.Count == 0 && snapshot.UnavailableTrayActions?["mute"] == "Join a voice call to use Discord voice controls.",
                "Leaving a call replaces the mute offer with an explanation");
            fake.State=fake.State with{Ready=false};await Refresh();
            snapshot = host.PublishSnapshot();
            check(snapshot.QuickActions.Count == 0 && snapshot.UnavailableTrayActions?["deafen"] == "Connect to Discord to use voice controls.",
                "A disconnected account replaces the previous call explanation");
        }
        finally
        {
            fake.CommandGate?.TrySetResult();
            await WidgetTestHost.DestroyAsync(widget);
        }
        async Task Refresh()
        {
            await widget.OnActionAsync(new("refresh","dashboard.test"));
            await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        static async Task Wait(Func<bool> condition)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(!condition())await Task.Delay(10,timeout.Token);
        }
    }
}
