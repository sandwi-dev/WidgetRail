namespace WidgetRail.Samples.SpotifyWidget;

public enum SpotifySearchKind { Track, Album, Artist, Playlist }

public sealed record SpotifySearchItem(
    SpotifySearchKind Kind, string Id, string Title, string Subtitle,
    string? ArtworkUrl, string Uri, string SpotifyUrl, bool IsPlayable);

public sealed record SpotifySearchPage(
    IReadOnlyList<SpotifySearchItem> Items, int Offset, int Limit, int Total,
    bool HasAuthoritativeWindow = true);
