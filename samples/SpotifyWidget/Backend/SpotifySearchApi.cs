using System.Globalization;
using WidgetRail.Samples.SpotifyWidget;

namespace WidgetRail.WindowsSpotifyProvider;

internal sealed class SpotifySearchApi(ISpotifyAuthorizedRequestSender sender)
{
    internal async Task<SpotifySearchPage> SearchAsync(SpotifyIntegrationIdentity identity,
        string query, SpotifySearchKind kind, int offset, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200 || query.Any(char.IsControl) ||
            !Enum.IsDefined(kind) || offset < 0 || offset >= 1000 || limit is < 1 or > 10)
            throw new SpotifyProviderException("invalid_request", "Enter a search of up to 200 characters.");
        var type = kind.ToString().ToLowerInvariant();
        var uri = new Uri("https://api.spotify.com/v1/search?q=" + Uri.EscapeDataString(query.Trim()) +
            "&type=" + type + "&offset=" + offset.ToString(CultureInfo.InvariantCulture) +
            "&limit=" + limit.ToString(CultureInfo.InvariantCulture));
        // Reuse the connected session's baseline grant; search adds no OAuth scope.
        var response = await sender.SendAsync(identity,
            new(HttpMethod.Get, uri, WindowsSpotifyPlatformBackend.PlaybackReadScope), cancellationToken)
            .ConfigureAwait(false);
        SpotifyResponseParser.EnsureSuccess(response);
        return SpotifyResponseParser.ParseSearchPage(response.Body, kind, offset, limit);
    }
}
