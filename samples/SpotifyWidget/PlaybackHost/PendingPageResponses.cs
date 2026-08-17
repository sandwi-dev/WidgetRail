using WidgetRail.SpotifyPlayback;

namespace WidgetRail.SpotifyPlaybackHost;

internal sealed class PendingPageResponses
{
    private readonly Dictionary<string, string> _expected = new(StringComparer.Ordinal);

    internal int Count => _expected.Count;

    internal void Register(string requestId, string expectedType)
    {
        if (_expected.Count >= SpotifyPlaybackProtocol.MaximumPendingCommands ||
            !_expected.TryAdd(requestId, expectedType))
            throw new SpotifyPlaybackProtocolException(
                "too_many_pending_commands",
                "The Spotify playback host has too many pending commands.");
    }

    internal bool TryConsume(string requestId, string actualType)
    {
        if (!_expected.TryGetValue(requestId, out var expectedType) ||
            (actualType != "command_failed" && actualType != expectedType))
            return false;
        _expected.Remove(requestId);
        return true;
    }

    internal void Cancel(string requestId) => _expected.Remove(requestId);

    internal void Clear() => _expected.Clear();
}
