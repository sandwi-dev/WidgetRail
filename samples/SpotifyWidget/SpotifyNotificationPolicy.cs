using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.SpotifyWidget;

/// <summary>Track identity and metadata are independent of position ticks and pause/resume.</summary>
public sealed class SpotifyNotificationPolicy
{
    private bool initialized;
    private string? track;
    private WidgetNotification? previous;
    public (WidgetNotification Content, bool UpdateOnly)? Observe(SpotifyPlaybackSummary? playback)
    {
        if (playback is not { IsAvailable: true, Item.Uri: { Length: > 0 } identity } || !playback.IsPlaying) return null;
        var changed = initialized && track != identity;
        var baseline = !initialized;
        initialized = true; track = identity;
        var item = playback.Item!;
        var duration = playback.DurationMilliseconds is > 0 and < 86_400_000 ? TimeSpan.FromMilliseconds(playback.DurationMilliseconds) : (TimeSpan?)null;
        var artwork = item.ArtworkUrl;
        var content = new WidgetNotification("now-playing", Bound(string.IsNullOrWhiteSpace(item.Title) ? "Now playing" : item.Title, 256))
        {
            CategoryId = "song-changes", Body = Bound(item.Subtitle, 2048),
            ArtworkUrl = artwork is { Length: <= 2048 } && Uri.TryCreate(artwork, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 ? artwork : null,
            Metadata = duration is { } time ? time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") : "",
            ShowWidgetIcon = false, Retention = WidgetNotificationRetention.BannerOnly,
            Interaction = WidgetNotificationInteraction.Informational, Visibility = WidgetNotificationVisibility.Always,
        };
        if (!changed && previous == content) return null;
        previous = content;
        return baseline ? null : (content, !changed);
    }
    public static WidgetActivity Activity(SpotifyPlaybackSummary? playback, SpotifyPlaybackSummary? localTransport, SpotifyLocalPlaybackSummary? local)
    {
        var localAudio = localTransport is { IsAvailable: true, IsPlaying: true } && local is { State: SpotifyLocalPlaybackState.Active, VolumePercent: > 0 };
        // An uncertain local transport is not evidence of playback on another device.
        var remote = localTransport is null && local?.State is not (SpotifyLocalPlaybackState.Active or SpotifyLocalPlaybackState.Starting) &&
            playback is { IsAvailable: true, IsPlaying: true };
        return new(LocalAudio: localAudio, RemotePlayback: remote);
    }
    private static string Bound(string text, int limit) => text.Length <= limit ? text : text[..(char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit)];
}
