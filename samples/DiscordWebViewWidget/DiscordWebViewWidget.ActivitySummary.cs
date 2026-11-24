using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.Integrations;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record SummaryAction(string Kind, string Account, string Session, string? Call,
        string User, string? Destination, string? Stream);
    private readonly object summaryGate = new();
    private Dictionary<SummaryAction, string> summaryTokens = [];
    private (string? Session, string Account) summaryOwner;
    private string summaryVisit = Guid.NewGuid().ToString("N");
    private string? queuedSummary;
    private long summarySequence;
    private long retrySummaryAfter;

    private DiscordActivitySummary BuildActivitySummary(DiscordState data, WebSessionReference? session)
    {
        lock (summaryGate)
        {
            if (summaryOwner != (session?.Id, data.Account))
            {
                summaryOwner = (session?.Id, data.Account);
                summaryVisit = Guid.NewGuid().ToString("N");
                summaryTokens.Clear();
            }
            var nextTokens = new Dictionary<SummaryAction, string>();
            string? Token(string? kind, string user, string? destination, string? stream)
            {
                if (kind is null || session is null) return null;
                var action = new SummaryAction(kind, data.Account, session.Id, data.ChannelId, user, destination, stream);
                return nextTokens[action] = summaryTokens.GetValueOrDefault(action) ?? Guid.NewGuid().ToString("N");
            }
            DiscordActivityPerson[] people;
            string mode, title;
            int count;
            bool truncated = false;
            if (!data.Ready || session is null)
            {
                mode = "setup";
                title = data.EffectiveConnectionState == "signed-out" ? "Sign in from the Discord widget" : "Connecting to Discord";
                people = []; count = 0;
            }
            else if (data.ChannelId is not null)
            {
                mode = "call"; title = data.ChannelName; count = data.Participants.Length;
                people = data.Participants.Select(person => (person, stream: data.Streams.FirstOrDefault(stream => stream.UserId == person.Id)))
                    .OrderByDescending(item => item.stream is not null || data.Sharing && item.person.Id == data.Account)
                    .Take(6).Select(item =>
                    {
                        var person = item.person;
                        var watch = item.stream is not null && data.Capabilities.Watch && person.Id != data.Account;
                        return new DiscordActivityPerson(person.Id, person.Name, ParticipantStatus(person), person.AvatarUrl,
                            item.stream is not null || data.Sharing && person.Id == data.Account, watch ? DiscordSummaryAction.WatchAndPin : null,
                            Token(watch ? "watch" : null, person.Id, data.ChannelId, item.stream?.Key));
                    }).ToArray();
            }
            else
            {
                mode = data.ActivityAvailability == "ready" ? "friends" : "unavailable";
                title = mode == "friends" ? "Friends active" : "Activity unavailable";
                count = data.Activities.Length; truncated = data.ActivitiesTruncated;
                people = OrderActivities(data).Take(6).Select(person =>
                {
                    var detail = ActivityDescription(person).Split('\n')[0];
                    return new DiscordActivityPerson(person.UserId, person.Name, detail, person.AvatarUrl,
                        person.Kind == "streaming", person.CanJoinVoice ? DiscordSummaryAction.JoinVoice : null,
                        Token(person.CanJoinVoice ? "join" : null, person.UserId, person.ChannelId, null));
                }).ToArray();
            }
            summaryTokens = nextTokens;
            return new(summaryVisit, mode, title, count, truncated, people)
            { ConnectionFailed = data.EffectiveConnectionState == "unavailable" };
        }
    }

    private void QueueActivitySummary(DiscordState? data)
    {
        lock (summaryGate)
        {
            if (data is null) { summaryTokens.Clear(); summaryOwner = default; }
            if (Environment.TickCount64 < retrySummaryAfter) return;
            var summary = data is null ? new DiscordActivitySummary(summaryVisit, "unavailable", "Discord unavailable", 0, false, []) { ConnectionFailed = true }
                : BuildActivitySummary(data, client?.Reference);
            var value = JsonSerializer.SerializeToElement(summary);
            var json = value.GetRawText();
            if (json == queuedSummary) return;
            var revision = ++summarySequence;
            queuedSummary = json;
            var delivery = Operations.RunLatest("discord.summary.publish", async work =>
            {
                try
                {
                    await HostServices.Capabilities.InvokeAsync(WidgetSummaryCapabilities.Publish,
                        new(DiscordActivityIntegration.Audience, revision, value), work.CancellationToken);
                }
                catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or NotSupportedException or InvalidOperationException)
                {
                    lock (summaryGate)
                    {
                        if (revision != summarySequence) return;
                        queuedSummary = null;
                        summaryTokens.Clear();
                        retrySummaryAfter = Environment.TickCount64 + 30_000;
                    }
                }
            }, WidgetOperationLifetime.Widget);
            if (!delivery.IsAccepted) queuedSummary = null;
        }
    }

    private async ValueTask<WidgetIntentResult> PerformActivitySummaryActionAsync(string reference, string reader, bool wantsPin, bool background, CancellationToken token)
    {
        if ((!background && !IsActive) || Operations.IsBusy("discord.command")) return WidgetIntentResult.Rejected;
        await state.Refresh().Completion.WaitAsync(token);
        try
        {
            var consent = await HostServices.Capabilities.InvokeAsync(WidgetSummaryCapabilities.CheckReader, new(reader), token);
            if (consent?.Acknowledged != true) return WidgetIntentResult.Rejected;
        }
        catch (WidgetCapabilityException) { return WidgetIntentResult.Rejected; }
        SummaryAction? action;
        lock (summaryGate) action = summaryTokens.FirstOrDefault(pair => pair.Value == reference).Key;
        var data = CurrentState();
        if ((!background && !IsActive) || data is null || action is null || wantsPin != (action.Kind == "watch") || data.Account != action.Account ||
            client?.Reference?.Id != action.Session || data.ChannelId != action.Call || Operations.IsBusy("discord.command"))
            return WidgetIntentResult.Rejected;
        if (action.Kind == "watch" && data.Capabilities.Watch &&
            data.Streams.FirstOrDefault(s => s.Key == action.Stream && s.UserId == action.User) is { } stream)
        {
            var slot = AvailableWatchSlot(stream, forPin: true);
            if (slot is null) return WidgetIntentResult.Rejected;
            var receipt = await HostServices.Capabilities.InvokeAsync(WidgetSummaryCapabilities.SelectPin,
                new(reader, reference, slot.Pin.Id), token);
            if (!receipt.Accepted || (!background && !IsActive) || CurrentState() is not { } latest ||
                latest.Account != action.Account || latest.ChannelId != action.Call || client?.Reference?.Id != action.Session ||
                !latest.Streams.Any(s => s.Key == action.Stream && s.UserId == action.User) ||
                AvailableWatchSlot(stream, forPin: true) != slot) return WidgetIntentResult.Rejected;
            var selection = slot.State.Value;
            if (!(slot.Pin.IsSelected && selection.StreamSession == client?.Reference && selection.Receiver is { } live &&
                live.Account == latest.Account && live.Channel == latest.ChannelId && live.StreamKey == stream.Key))
                PrepareWatch(slot, latest, stream, null);
            if (!background && !messages.Value.IncomingFiles) OpenCallSection();
        }
        else if (action.Kind == "join" && data.Activities.FirstOrDefault(a => a.UserId == action.User) is { CanJoinVoice: true } friend &&
            friend.ChannelId == action.Destination && friend.ChannelId is not null)
        {
            if (!background && !messages.Value.IncomingFiles) OpenCallSection();
            if (!await JoinVoiceAsync(data.Account, friend.ChannelId, token, friend.UserId,
                background ? new(reader, reference, client!.Reference!) : null)) return WidgetIntentResult.Rejected;
        }
        else return WidgetIntentResult.Rejected;
        return WidgetIntentResult.Accepted;
    }
}
