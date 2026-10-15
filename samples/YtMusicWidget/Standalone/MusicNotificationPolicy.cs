using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

/// <summary>Playback events are frequent; only a ready track transition or changed metadata is meaningful.</summary>
public sealed class MusicNotificationPolicy
{
    private MusicItem? current;
    private WidgetNotification? previous;
    public (WidgetNotification Content, bool UpdateOnly)? Observe(MusicState state, bool enabled, int opacity = 100)
    {
        var song = state.Current;
        if (!enabled) { current = song; previous = null; return null; }
        if (song is null || !state.Player.Playing || state.Player.Buffering || state.Player.TrackId != song.Id) return null;
        var changed = current?.Id != song.Id;
        var duration = double.IsFinite(state.Player.Duration) && state.Player.Duration > 0 && state.Player.Duration < 86400
            ? TimeSpan.FromSeconds(state.Player.Duration) : (TimeSpan?)null;
        var content = new WidgetNotification("now-playing", string.IsNullOrWhiteSpace(song.Title) ? "Now playing" : Bound(song.Title, 256))
        {
            CategoryId = "song-changes",
            Body = Bound(song.Subtitle, 2048), ArtworkUrl = song.Artwork.Length <= 2048 && Uri.TryCreate(song.Artwork, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 ? song.Artwork : null,
            Metadata = duration is { } time ? time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") : "",
            ShowWidgetIcon = false, Retention = WidgetNotificationRetention.BannerOnly,
            Interaction = WidgetNotificationInteraction.Informational, Visibility = WidgetNotificationVisibility.Always,
            BackgroundOpacityPercent = Math.Clamp(opacity, 40, 100),
        };
        current = song;
        if (!changed && previous == content) return null;
        previous = content;
        return (content, !changed);
    }
    private static string Bound(string text, int limit) => text.Length <= limit ? text : text[..(char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit)];
}
