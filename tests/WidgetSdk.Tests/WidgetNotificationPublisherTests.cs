using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetNotificationPublisherTests
{
    internal static async Task Run()
    {
        await CoalescesAndKeepsUpdateOnlyAsync();
        await OlderReceiptCannotClearNewEventAsync();
        await MetadataDuringDeliveryDoesNotBecomeANewEventAsync();
        await FailureAndDestructionAreBoundedAsync();
        await WithdrawalOrdersExternalDeliveryAsync();
        await WithdrawalBeforeDebounceAsync();
    }

    private static async Task WithdrawalOrdersExternalDeliveryAsync()
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands=new List<WidgetNotificationCommand>();WidgetNotification? visible=null;
        var widget=await CreateAsync(TimeProvider.System,TimeSpan.Zero,async(command,_)=>
        {
            lock(commands)commands.Add(command);
            if(command.Notification?.Title=="Old call"){started.SetResult();await release.Task;} // Ignore cancellation on purpose.
            if(command.Notification is null || !command.UpdateOnly || visible is not null)visible=command.Notification;
            return new(true);
        });
        try
        {
            widget.Publisher.Publish(Notice("Old call"));await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            widget.Publisher.Dismiss();
            var late=widget.Publisher.Publish(Notice("Late caller metadata"),updateOnly:true);
            lock(commands)Check(commands.Count==1,"Withdrawal overlapped the unsettled Show.");
            release.SetResult();await late.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Check(visible is null && commands.Count==2 && commands[^1].Notification is null,
                "Late Show or metadata resurrected a withdrawn event.");
            await widget.Publisher.Publish(Notice("New call")).Completion;
            Check(visible?.Title=="New call"&&!commands[^1].UpdateOnly,"Withdrawal suppressed a genuinely new event.");
        }
        finally{release.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
        Check(!widget.Publisher.Dismiss().IsAccepted,"Destroyed publisher admitted withdrawal.");
    }

    private static async Task WithdrawalBeforeDebounceAsync()
    {
        var clock=new WidgetOperationTests.ManualTimeProvider();var commands=new List<WidgetNotificationCommand>();
        var widget=await CreateAsync(clock,TimeSpan.FromSeconds(1),(command,_)=>{commands.Add(command);return ValueTask.FromResult(new WidgetNotificationReceipt(true));});
        try
        {
            widget.Publisher.Publish(Notice("Cancelled invitation"));
            await Until(()=>clock.CreatedTimerCount>0);
            await widget.Publisher.Dismiss().Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Check(commands.Count==1&&commands[0].Notification is null,"Withdrawal waited for debounce or allowed an unshown event to appear.");
            clock.Advance(TimeSpan.FromSeconds(2));
            Check(commands.Count==1,"Retired debounce delivered after withdrawal.");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }

    private static WidgetNotification Notice(string title) => new("track", title) { CategoryId = "music" };

    private static async Task CoalescesAndKeepsUpdateOnlyAsync()
    {
        var clock = new WidgetOperationTests.ManualTimeProvider();
        var commands = new List<WidgetNotificationCommand>();
        var widget = await CreateAsync(clock, TimeSpan.FromMilliseconds(250), (command, _) =>
        { lock (commands) commands.Add(command); return ValueTask.FromResult(new WidgetNotificationReceipt(true)); });
        try
        {
            var first = widget.Publisher.Publish(Notice("First"));
            var latest = widget.Publisher.Publish(Notice("Latest metadata"), updateOnly: true);
            await Until(() => clock.CreatedTimerCount >= 2);
            lock (commands) Check(commands.Count == 0, "Debounce sent early.");
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await latest.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            lock (commands) Check(commands.Count == 1 && !commands[0].UpdateOnly && commands[0].Notification!.Title == "Latest metadata",
                "Metadata lost the pending new event or older content was sent.");
            var updated = widget.Publisher.Publish(Notice("Artwork ready"), updateOnly: true);
            await Until(() => clock.CreatedTimerCount >= 3);
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await updated.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            lock (commands) Check(commands.Count == 2 && commands[1].UpdateOnly, "Metadata resurrected a notification.");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }

        commands.Clear();
        var updateOnly = await CreateAsync(TimeProvider.System, TimeSpan.Zero, (command, _) =>
        { commands.Add(command); return ValueTask.FromResult(new WidgetNotificationReceipt(true)); });
        try
        {
            await updateOnly.Publisher.Publish(Notice("No new event"), updateOnly: true).Completion;
            Check(commands.Single().UpdateOnly, "An initial update-only request became Show.");
        }
        finally { await WidgetTestHost.DestroyAsync(updateOnly); }
    }

    private static async Task OlderReceiptCannotClearNewEventAsync()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new List<WidgetNotificationCommand>();
        var widget = await CreateAsync(TimeProvider.System, TimeSpan.Zero, async (command, _) =>
        {
            int count;
            lock (commands) { commands.Add(command); count = commands.Count; }
            if (count == 1) { started.SetResult(); await release.Task; } // Deliberately ignore cancellation.
            return new WidgetNotificationReceipt(true);
        });
        try
        {
            widget.Publisher.Publish(Notice("Old track"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            widget.Publisher.Publish(Notice("Intermediate track"));
            var latest = widget.Publisher.Publish(Notice("Newest track"));
            release.SetResult();
            await latest.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            lock (commands) Check(commands.Count == 2 && commands[^1].Notification!.Title == "Newest track" && !commands[^1].UpdateOnly,
                "An old receipt cleared the new event or obsolete tracks were delivered.");
        }
        finally { release.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
    }

    private static async Task MetadataDuringDeliveryDoesNotBecomeANewEventAsync()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new List<WidgetNotificationCommand>();
        var widget = await CreateAsync(TimeProvider.System, TimeSpan.Zero, async (command, _) =>
        {
            commands.Add(command);
            if (commands.Count == 1) { started.SetResult(); await release.Task; }
            return new WidgetNotificationReceipt(true);
        });
        try
        {
            widget.Publisher.Publish(Notice("Track"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var metadata = widget.Publisher.Publish(Notice("Track with artwork"), updateOnly: true);
            release.SetResult();
            await metadata.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Check(commands.Count == 2 && commands[1].UpdateOnly,
                "Metadata arriving during an accepted delivery became another new event.");
        }
        finally { release.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
    }

    private static async Task FailureAndDestructionAreBoundedAsync()
    {
        var calls = 0;
        var widget = await CreateAsync(TimeProvider.System, TimeSpan.Zero, (_, _) =>
        {
            if (++calls == 1) throw new WidgetCapabilityException("unavailable", "Unavailable");
            return ValueTask.FromResult(new WidgetNotificationReceipt(false));
        });
        try
        {
            await widget.Publisher.Publish(Notice("Failure")).Completion;
            Check(calls == 1, "Failed publication looped or was lost.");
            await widget.Publisher.Publish(Notice("Later observation"), updateOnly: true).Completion;
            Check(calls == 2, "An explicit later publication did not retry.");
            try { widget.Publisher.Publish(new("other", "Wrong key")); throw new InvalidOperationException("A foreign key was accepted."); }
            catch (ArgumentException) { }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
        Check(!widget.Publisher.Publish(Notice("Destroyed")).IsAccepted && calls == 2, "Destroyed owner sent a notification.");

        var clock = new WidgetOperationTests.ManualTimeProvider();
        calls = 0;
        var retiring = await CreateAsync(clock, TimeSpan.FromSeconds(1), (_, _) =>
        { calls++; return ValueTask.FromResult(new WidgetNotificationReceipt(true)); });
        retiring.Publisher.Publish(Notice("Pending"));
        await WidgetTestHost.DestroyAsync(retiring);
        clock.Advance(TimeSpan.FromHours(1));
        Check(calls == 0, "Owner destruction did not retire the pending timer.");
    }

    private static async Task<PublisherWidget> CreateAsync(TimeProvider clock, TimeSpan delay,
        Func<WidgetNotificationCommand, CancellationToken, ValueTask<WidgetNotificationReceipt>> handler)
    {
        var widget = new PublisherWidget(clock, delay);
        WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetNotificationCapabilities.Publish, handler).Build());
        await WidgetTestHost.InitializeAsync(widget);
        return widget;
    }

    private sealed class PublisherWidget : Widget
    {
        internal PublisherWidget(TimeProvider clock, TimeSpan delay) => Publisher =
            CreateNotificationPublisher("notification", "track", "music", delay, timeProvider: clock);
        internal WidgetNotificationPublisher Publisher { get; }
        public override WidgetView Render() => new(UI.Stack("root"));
    }

    private static async Task Until(Func<bool> predicate)
    {
        var deadline = Environment.TickCount64 + 3000;
        while (!predicate())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("Publication did not settle.");
            await Task.Delay(1);
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
