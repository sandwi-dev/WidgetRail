using System.Text.Json;
using WidgetRail.Samples.SpotifyWidget;

namespace WidgetRail.WindowsSpotifyProvider;

/// <summary>Track library operations using Spotify's current unified library API.</summary>
internal sealed class SpotifyLibraryApi(ISpotifyAuthorizedRequestSender sender)
{
    private readonly ISpotifyAuthorizedRequestSender _sender =
        sender ?? throw new ArgumentNullException(nameof(sender));

    internal async Task<IReadOnlyList<bool>> GetSavedItemsAsync(
        SpotifyIntegrationIdentity identity, IReadOnlyList<string> uris,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (uris is null || uris.Count is < 1 or > SpotifyApplicationContract.MaximumLibraryItems)
            throw new SpotifyProviderException("invalid_request", "Select between 1 and 40 Spotify tracks.");
        // Snapshot before awaiting; caller mutations cannot change response alignment.
        var snapshot = uris.ToArray();
        foreach (var uri in snapshot) ValidateTrackUri(uri);
        var response = await _sender.SendAsync(identity,
            new SpotifyAuthorizedRequest(HttpMethod.Get,
                LibraryUri("/contains", snapshot), WindowsSpotifyPlatformBackend.LibraryReadScope),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyResponseParser.EnsureSuccess(response);
        return ParseSavedItems(response.Body, snapshot.Length);
    }

    internal async Task SetSavedItemAsync(
        SpotifyIntegrationIdentity identity, string uri, bool saved,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTrackUri(uri);
        var response = await _sender.SendAsync(identity,
            new SpotifyAuthorizedRequest(saved ? HttpMethod.Put : HttpMethod.Delete,
                LibraryUri(string.Empty, [uri]), WindowsSpotifyPlatformBackend.LibraryModifyScope),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SpotifyResponseParser.EnsureSuccess(response, allowNoContent: true);
    }

    private static Uri LibraryUri(string suffix, IReadOnlyList<string> uris) =>
        new("https://api.spotify.com/v1/me/library" + suffix +
            "?uris=" + Uri.EscapeDataString(string.Join(',', uris)));

    private static void ValidateTrackUri(string uri)
    {
        const string prefix = "spotify:track:";
        if (uri is null || uri.Length != prefix.Length + 22 ||
            !uri.StartsWith(prefix, StringComparison.Ordinal) ||
            uri.AsSpan(prefix.Length).ContainsAnyExcept(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"))
            throw new SpotifyProviderException("invalid_request", "Select a Spotify track to save to your library.");
    }

    private static IReadOnlyList<bool> ParseSavedItems(string body, int count)
    {
        // At most 40 booleans. Bound before allocating a JSON document.
        if (body is null || body.Length > 4096)
            throw InvalidResponse();
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != count)
                throw InvalidResponse();
            var result = new bool[count];
            var index = 0;
            foreach (var value in root.EnumerateArray())
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw InvalidResponse();
                result[index++] = value.GetBoolean();
            }
            return Array.AsReadOnly(result);
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static SpotifyProviderException InvalidResponse() =>
        new("invalid_response", "Spotify returned invalid saved-track information. Try again later.");
}
