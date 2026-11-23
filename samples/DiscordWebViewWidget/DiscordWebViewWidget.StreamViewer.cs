using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private WatchSlot? ViewerSlot => view.Value.ViewerSlot is { } number ? watches.FirstOrDefault(slot => slot.Number == number) : null;
    private bool ViewerSelected(WatchSlot slot) => navigation.Value.Route == Route.StreamViewer && view.Value.ViewerSlot == slot.Number;
    private bool ViewerWanted(WatchSlot slot) => IsActive && client?.MainViewVisible == true && ViewerSelected(slot);
    private void MainViewVisibilityChanged(WebSessionReference session)
    {
        if (ViewerSlot is { } slot && ViewerSelected(slot) && !slot.Pin.IsSelected &&
            slot.State.Value is { Closed:false, Ended:false, ReceiverFailure:null } selection &&
            selection.StreamSession==session && client?.Reference==session)
            RestartReceiver(slot);
    }
    private bool ReceiverWanted(WatchSlot slot) => slot.Pin.IsSelected || ViewerWanted(slot);
    private WatchSlot? AvailableWatchSlot(DiscordStream stream, bool forPin = false) =>
        watches.FirstOrDefault(slot => !slot.State.Value.Closed && slot.State.Value.StreamKey == stream.Key) ??
        watches.FirstOrDefault(slot => !ViewerSelected(slot) && (forPin || !slot.Pin.IsSelected) && (slot.State.Value.Closed || slot.State.Value.StreamKey is null)) ??
        watches.FirstOrDefault(slot => !slot.Pin.IsSelected && !ViewerSelected(slot));

    private static string ViewStreamAction(DiscordState data, DiscordStream stream, WebSessionReference? session) =>
        DomainAction("view-stream", session?.Id, data.Account, data.ChannelId, stream.UserId, stream.Key);
    private string RetryViewerAction(WatchSlot slot) => DomainAction("viewer-retry", client?.Reference?.Id, slot.State.Value.Revision,
        view.Value.ViewerVisit.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void OpenStreamViewer(DiscordState data, DiscordStream stream, WidgetActionEvent action)
    {
        if (AvailableWatchSlot(stream) is not { } slot)
        {
            PublishFeedback("All three screens are in use. Unpin a screen to watch another.");
            return;
        }
        var session = client?.Reference;
        if (session is null) return;
        var selection = slot.State.Value;
        var reuse = !selection.Closed && selection.StreamSession == session && selection.Account == data.Account &&
            selection.Channel == data.ChannelId && selection.StreamKey == stream.Key;
        if (!reuse) slot.State.Update(previous => new WatchState(data.Account, data.ChannelId, stream.Key, stream.Name,
            previous.Receiver, StreamSession: session) { OfferPin = false });
        PushRoute(Route.StreamViewer, action);
        var visit = navigation.Value;
        view.Update(previous => previous with { ViewerSlot = slot.Number, ViewerVisit = visit.Revision });
        visit.RouteCancellationToken.Register(() =>
        {
            var removed = view.Update(previous => previous.ViewerVisit == visit.Revision && previous.ViewerSlot == slot.Number
                ? (previous with { ViewerSlot = null }, true) : (previous, false)).Result;
            if (!removed || slot.Pin.IsSelected) return;
            slot.State.Update(previous => previous with { Closed = true, OfferPin = false });
            RestartReceiver(slot);
        });
        // The selected pin owns its pending connection even if this visit ends
        // before the first frame. Watch may retry a known failure explicitly.
        if (!reuse || selection.Receiver is null && (!slot.Pin.IsSelected || selection.ReceiverFailure is not null))
            RestartReceiver(slot);
    }

    private void ResumeViewer()
    {
        if (ViewerSlot is { } slot && !slot.Pin.IsSelected && ViewerWanted(slot) &&
            slot.State.Value is { StreamKey: not null, Receiver: null, ReceiverFailure: null })
            RestartReceiver(slot);
    }

    private WidgetElement StreamViewerPage(DiscordState? data, string backAction)
    {
        var slot = ViewerSlot;
        var selected = slot?.State.Value;
        var stream = selected is { Closed: false, Ended: false } && data?.Ready == true && data.Account == selected.Account &&
            data.ChannelId == selected.Channel && client?.Reference == selected.StreamSession
            ? data.Streams.FirstOrDefault(item => item.Key == selected.StreamKey) : null;
        var body = new List<WidgetElement>
        {
            UI.Row("discord.viewer.header",
                UI.IconButton(WidgetGlyph.Back, backAction, "discord.viewer.back", "Back to call").Classes("discord-control"),
                UI.Text(stream?.Name ?? "Shared screen", "discord.viewer.title").Classes("discord-title", "discord-copy")).Classes("discord-header")
        };
        if (stream is null || slot is null || selected is null)
            body.Add(Empty("discord.viewer.ended", "Screen unavailable", "The stream ended or the call changed.", DiscordIcons.Screen));
        else if (selected.Receiver is { } relay && relay.StreamKey == stream.Key && relay.Account == data!.Account && relay.Channel == data.ChannelId)
        {
            body.Add(UI.WebView(relay.Receiver.Reference, "discord.viewer.video", "Shared screen, fullscreen", BrowserInteractionMode.ActivateToInteract).Classes("discord-viewer-video"));
            body.Add(StreamAudioControls(data, stream, selected, "discord.viewer.audio"));
        }
        else if (selected.ReceiverFailure is { } failure)
        {
            body.Add(UI.Text(failure.Message, "discord.viewer.failure").Classes("discord-secondary"));
            body.Add(UI.Button("Retry", RetryViewerAction(slot), "discord.viewer.retry").Classes("discord-control"));
        }
        else body.Add(UI.Text("Connecting to stream…", "discord.viewer.connecting").Classes("discord-secondary"));
        return UI.Stack("discord.viewer", body.ToArray()).Classes("discord-page");
    }
}
