using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.Integrations;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SummaryActionTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var session = new FakeSession();
        var messages = new ConcurrentQueue<DiscordActivitySummary>();
        var pins = new ConcurrentQueue<string>();
        bool allow = true;
        var services = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetSummaryCapabilities.Publish, (request, _) =>
            {
                if (request.Value?.Deserialize<DiscordActivitySummary>() is { } summary) messages.Enqueue(summary);
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            })
            .WithHandler(WidgetSummaryCapabilities.CheckReader, (_, _) => ValueTask.FromResult(new WidgetCapabilityAcknowledgement(allow)))
            .WithHandler(WidgetSummaryCapabilities.SelectPin,(request, _) =>
            {
                pins.Enqueue(request.LayoutId);
                return ValueTask.FromResult(new WidgetPinReceipt(true));
            }).Build();
        var widget = WidgetTestHost.Attach(new DiscordWebViewWidget(session, refreshInterval:TimeSpan.FromHours(1)), services);
        WidgetIntentRequest Request(DiscordActivityPerson person) => WidgetIntentRequest.Create(WidgetIntentContracts.SummaryAction,
            JsonSerializer.SerializeToElement(new { token = person.Token, reader = new string('b', 32), pin = person.Action == DiscordSummaryAction.WatchAndPin }));
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        try
        {
            await widget.WaitForStateReadAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            while (messages.IsEmpty) await Task.Delay(10, deadline.Token);
            var call = messages.Last();
            var stream = Request(call.People.Single(person => person.Action == DiscordSummaryAction.WatchAndPin));
            check(call.Mode == "call" && call.People[0].Streaming && call.People[0].Detail == "Speaking",
                "Playnite summary leads with the streamer and preserves their voice state");
            allow = false;
            check(await widget.OnIntentAsync(stream) == WidgetIntentResult.Rejected && pins.IsEmpty,
                "Revoked reader cannot pin an otherwise current Discord stream");
            allow = true;
            check(await widget.OnIntentAsync(stream) == WidgetIntentResult.Accepted && pins.TryPeek(out var slot) && slot == "discord.stream",
                "A current summary action uses Discord’s stream pin chooser");
            session.State = session.State with { ChannelId = null, ChannelName = "", Participants = [], Streams = [], ActivityAvailability = "ready",
                Activities = [new("31","Maya","online","voice","","","20","1","Game Night","Guild",true,null,false)] };
            await widget.OnActionAsync(new("refresh","test"));
            await widget.WaitForStateReadAsync();
            check(await widget.OnIntentAsync(stream) == WidgetIntentResult.Rejected && pins.Count == 1, "Ended stream cannot be pinned from an old Playnite panel");
            while (messages.Last().Mode != "friends") await Task.Delay(10, deadline.Token);
            var join = Request(messages.Last().People.Single());
            var friends = session.State.Activities;
            session.State = session.State with { Activities = [] };
            check(await widget.OnIntentAsync(join) == WidgetIntentResult.Rejected && session.MicrophoneRequests == 0,
                "A friend leaving between display and activation never reaches microphone consent");
            session.State = session.State with { Activities = friends };
            await widget.OnActionAsync(new("refresh","test"));
            await widget.WaitForStateReadAsync();
            while (messages.Last().People.Length != 1 || Request(messages.Last().People[0]).Matches(join))
                await Task.Delay(10, deadline.Token);
            var fresh = Request(messages.Last().People[0]);
            check(await widget.OnIntentAsync(fresh) == WidgetIntentResult.Accepted, "The returning friend receives a fresh Join voice action");
            while (!session.Commands.Any(c => c.Method == "joinActivity")) await Task.Delay(10, deadline.Token);
            check(session.MicrophoneRequests == 1, "Summary Join voice uses Discord's existing microphone admission");
            await widget.WaitForStateReadAsync();
            session.State = session.State with
            {
                Ready = false, Account = "", ConnectionState = "signed-out", ChannelId = null, ChannelName = "",
                Participants = [], Streams = [], Activities = [], VoiceConnection = "disconnected"
            };
            await widget.OnActionAsync(new("refresh","test"));
            while (messages.Last().Mode != "setup") await Task.Delay(10, deadline.Token);
            var signedOut = messages.Last();
            check(signedOut.Title == "Sign in from the Discord widget" && signedOut.People.Length == 0 && signedOut.Session != call.Session,
                "Sign-out replaces the activity summary and retires the previous account's rows");
            check(await widget.OnIntentAsync(fresh) == WidgetIntentResult.Rejected && session.MicrophoneRequests == 1,
                "A join action from before sign-out cannot ask for microphone access");
            var start = WidgetIntentRequest.Create(DiscordActivityIntegration.Open, DiscordActivityIntegration.OpenPayload,
                WidgetIntentPresentation.StayInSource);
            session.State = session.State with { ConnectionState = "unavailable" };
            await widget.OnActionAsync(new("refresh","test"));
            while (!messages.Last().ConnectionFailed) await Task.Delay(10, deadline.Token);
            check(await widget.OnIntentAsync(start) == WidgetIntentResult.Rejected,
                "A failed background connection can be retried and reports its actual failure");
            session.State = session.State with { ConnectionState = "signed-out" };
            check(await widget.OnIntentAsync(start) == WidgetIntentResult.Accepted,
                "Retry refreshes the failed connection and accepts a recovered sign-in state");
            while (messages.Last().ConnectionFailed) await Task.Delay(10, deadline.Token);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }
}
