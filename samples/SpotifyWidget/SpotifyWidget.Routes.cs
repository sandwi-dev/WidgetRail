using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal enum SpotifyRoute
{
    Queue,
    Playlists,
    PlaylistDetail,
    Devices,
    Setup,
    Search,
}

internal enum SpotifyActionKind
{
    Unknown,
    SetupOpen,
    SetupClose,
    SetupDone,
    SetupOpenDashboard,
    SetupCopyRedirect,
    SetupConfigureClient,
    Connect,
    Disconnect,
    Refresh,
    Playback,
    Seek,
    Navigate,
    PreviousSection,
    NextSection,
    PlaylistBack,
    PageRetry,
    Noop,
    LocalStart,
    LocalStop,
    DeviceSelect,
    QueuePlay,
    PlaylistOpen,
    PlaylistPlay,
    PlaylistTrack,
}

internal readonly record struct SpotifyActionIntent(
    SpotifyActionKind Kind,
    SpotifyDestination? Destination = null,
    SpotifyPlaybackOperation? PlaybackOperation = null,
    WidgetCollectionItemKey? ItemKey = null,
    int ItemIndex = -1,
    long? RequestedPositionMs = null);

/// <summary>
/// Closed value-only interpretation of authored Spotify actions and route
/// focus. It owns no widget state, provider, operation, or invalidation.
/// </summary>
internal static class SpotifyRouteActionPolicy
{
    internal static SpotifyActionIntent Classify(WidgetActionEvent action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var direct = action.ActionId switch
        {
            "spotify.setup.open" => new(SpotifyActionKind.SetupOpen),
            "spotify.setup.close" => new(SpotifyActionKind.SetupClose),
            "spotify.setup.done" => new(SpotifyActionKind.SetupDone),
            "spotify.setup.dashboard" => new(SpotifyActionKind.SetupOpenDashboard),
            "spotify.setup.copy-redirect" => new(SpotifyActionKind.SetupCopyRedirect),
            "spotify.setup.client-id" => new(SpotifyActionKind.SetupConfigureClient),
            "spotify.connect" or "spotify.connect.features" =>
                new(SpotifyActionKind.Connect),
            "spotify.disconnect" => new(SpotifyActionKind.Disconnect),
            "spotify.retry" or "spotify.refresh" => new(SpotifyActionKind.Refresh),
            "spotify.play-toggle" => new(SpotifyActionKind.Playback),
            "spotify.previous" => Playback(SpotifyPlaybackOperation.Previous),
            "spotify.next" => Playback(SpotifyPlaybackOperation.Next),
            "spotify.shuffle" => Playback(SpotifyPlaybackOperation.SetShuffle),
            "spotify.repeat" => Playback(SpotifyPlaybackOperation.SetRepeat),
            "spotify.nav.queue" => Navigate(SpotifyDestination.Queue),
            "spotify.nav.search" => Navigate(SpotifyDestination.Search),
            "spotify.nav.playlists" => Navigate(SpotifyDestination.Playlists),
            "spotify.nav.devices" => Navigate(SpotifyDestination.Devices),
            "spotify.nav.previous-section" => new(SpotifyActionKind.PreviousSection),
            "spotify.nav.next-section" => new(SpotifyActionKind.NextSection),
            "spotify.playlist.back" => new(SpotifyActionKind.PlaylistBack),
            "spotify.page.retry" => new(SpotifyActionKind.PageRetry),
            "spotify.page.noop" => new(SpotifyActionKind.Noop),
            "spotify.local.start" => new(SpotifyActionKind.LocalStart),
            "spotify.local.stop" => new(SpotifyActionKind.LocalStop),
            "spotify.playlist.play" => new(SpotifyActionKind.PlaylistPlay),
            _ => default,
        };
        if (direct.Kind != SpotifyActionKind.Unknown) return direct;

        if (action.ActionId == "spotify.seek" &&
            action.RequestedValue is { } requested && double.IsFinite(requested))
            return new(SpotifyActionKind.Seek,
                RequestedPositionMs: Math.Max(0, (long)Math.Round(requested)));
        if (TryParseIndex(action.ActionId, "spotify.device.select.", out var index))
            return new(SpotifyActionKind.DeviceSelect, ItemIndex: index);
        if (TryParseKey(action.ActionId, "spotify.queue.play.", out var queueKey))
            return new(SpotifyActionKind.QueuePlay, ItemKey: queueKey);
        if (TryParseKey(action.ActionId, "spotify.playlist.open.", out var playlistKey))
            return new(SpotifyActionKind.PlaylistOpen, ItemKey: playlistKey);
        if (TryParseKey(action.ActionId, "spotify.playlist.track.", out var trackKey))
            return new(SpotifyActionKind.PlaylistTrack, ItemKey: trackKey);
        return default;
    }

    internal static string Mode(string sourceElementId) =>
        sourceElementId.Contains(".compact.", StringComparison.Ordinal)
            ? "compact" : "wide";

    internal static string NavigationFocusId(SpotifyDestination destination, string mode) =>
        $"spotify.nav.{mode}.{destination.ToString().ToLowerInvariant()}";

    internal static SpotifyDestination Destination(SpotifyRoute route) => route switch
    {
        SpotifyRoute.Queue => SpotifyDestination.Queue,
        SpotifyRoute.Search => SpotifyDestination.Search,
        SpotifyRoute.Devices => SpotifyDestination.Devices,
        _ => SpotifyDestination.Playlists,
    };

    internal static bool CanSwitchSection(SpotifyRoute route, int depth) =>
        depth == 0 || route == SpotifyRoute.PlaylistDetail;

    internal static string FocusGroupId(SpotifyRoute route) => route switch
    {
        SpotifyRoute.Queue => "spotify.page.queue",
        SpotifyRoute.Search => "spotify.page.search",
        SpotifyRoute.Devices => "spotify.page.devices",
        _ => "spotify.page.playlists",
    };

    internal static bool DevicesNeedLoad(
        SpotifyDevicesSummary? devices,
        SpotifyLocalPlaybackSummary? localPlayback,
        DateTimeOffset? cachedAt,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        devices is null || localPlayback is null || cachedAt is not { } value ||
        now - value >= lifetime;

    private static SpotifyActionIntent Playback(SpotifyPlaybackOperation operation) =>
        new(SpotifyActionKind.Playback, PlaybackOperation: operation);

    private static SpotifyActionIntent Navigate(SpotifyDestination destination) =>
        new(SpotifyActionKind.Navigate, Destination: destination);

    private static bool TryParseIndex(string action, string prefix, out int index)
    {
        index = -1;
        return action.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(action[prefix.Length..], out index) && index >= 0;
    }

    private static bool TryParseKey(
        string action,
        string prefix,
        out WidgetCollectionItemKey key)
    {
        key = default;
        if (!action.StartsWith(prefix, StringComparison.Ordinal) ||
            action.Length == prefix.Length) return false;
        try
        {
            key = new(action[prefix.Length..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
