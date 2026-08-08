namespace GameBarAlternative.SpotifyPlaybackHost;

internal static class SpotifyPlaybackPageValidator
{
    internal static bool IsTrustedSource(string? source) =>
        string.Equals(source, SpotifyPlaybackPage.TopLevelUri, StringComparison.Ordinal);

    internal static SpotifyLocalPlaybackState ValidatePlaybackState(
        SpotifyLocalPlaybackState value)
    {
        if (value.PositionMilliseconds is < 0 or > SpotifyPlaybackProtocol.MaximumSeekMilliseconds ||
            value.DurationMilliseconds is < 0 or > SpotifyPlaybackProtocol.MaximumSeekMilliseconds ||
            value.RepeatMode is < 0 or > 2 || value.Disallows is null)
            throw Invalid();

        if (value.CurrentTrack is not { } track) return value;
        ValidateRequiredText(track.Name, 512);
        ValidateRequiredText(track.Type, 32);
        ValidateRequiredText(track.MediaType, 32);
        ValidateOptionalText(track.AlbumName, 512);
        ValidateSpotifyUri(track.Uri);
        ValidateSpotifyId(track.Id);
        ValidateArtworkUrl(track.ArtworkUrl);
        if (track.Artists is null || track.Artists.Count > 16)
            throw Invalid();
        foreach (var artist in track.Artists) ValidateRequiredText(artist, 256);
        return value;
    }

    private static void ValidateRequiredText(string? value, int maximum)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum || value.Any(char.IsControl))
            throw Invalid();
    }

    private static void ValidateOptionalText(string? value, int maximum)
    {
        if (value is not null && (value.Length > maximum || value.Any(char.IsControl)))
            throw Invalid();
    }

    private static void ValidateSpotifyUri(string? value)
    {
        if (value is null) return;
        if (value.Length is 0 or > 512 || value.Any(char.IsControl) ||
            !value.StartsWith("spotify:", StringComparison.Ordinal))
            throw Invalid();
    }

    private static void ValidateSpotifyId(string? value)
    {
        if (value is null) return;
        if (value.Length is 0 or > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw Invalid();
    }

    private static void ValidateArtworkUrl(string? value)
    {
        if (value is null) return;
        if (value.Length is 0 or > 1_024 || value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
            !IsSpotifyImageHost(uri.IdnHost))
            throw Invalid();
    }

    private static bool IsSpotifyImageHost(string host) =>
        IsHostOrSubdomain(host, "scdn.co") ||
        IsHostOrSubdomain(host, "spotifycdn.com") ||
        IsHostOrSubdomain(host, "spotify.com");

    private static bool IsHostOrSubdomain(string host, string suffix) =>
        string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith('.' + suffix, StringComparison.OrdinalIgnoreCase);

    private static SpotifyPlaybackProtocolException Invalid() => new(
        "invalid_page_event", "The Spotify SDK playback event is invalid.");
}
