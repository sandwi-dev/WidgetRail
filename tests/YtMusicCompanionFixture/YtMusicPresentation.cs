using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget;

internal static class YtMusicPresentation
{
    private static readonly WidgetSurfaceHints StandardSurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 760,
        PreferredHeight = 440,
        MinimumWidth = 480,
        MinimumHeight = 340,
    };

    private static readonly IReadOnlyList<WidgetQuickAction> ConnectedQuickActions =
    [
        new(ControllerButton.LeftBumper, "previous", "Previous track",
            LoopbackControlAuthority()),
        new(ControllerButton.X, "toggle-playback", "Play or pause",
            LoopbackControlAuthority()),
        new(ControllerButton.RightBumper, "next", "Next track",
            LoopbackControlAuthority()),
    ];

    internal static WidgetView Compose(
        YtMusicPresentationState presentation,
        YtMusicPlaybackSnapshot snapshot)
    {
        var connection = presentation.ConnectionState;
        var pendingCommands = presentation.PendingOptimistic
            .Select(item => item.Command)
            .ToHashSet();
        if (connection is
            YtMusicWidgetConnectionState.Connecting or
            YtMusicWidgetConnectionState.Pairing)
        {
            var children = new List<WidgetElement>
            {
                Header(presentation.Status, connection),
                UI.Text(connection == YtMusicWidgetConnectionState.Pairing
                        ? "Approve the code in YTMDesktop2. The widget will continue automatically."
                        : "Checking the local companion API and loading now playing…",
                    "loading-detail", "YT Music connection in progress")
                    .Classes("loading-detail"),
            };
            if (!string.IsNullOrWhiteSpace(presentation.PairingCode))
            {
                children.Add(UI.Text(
                        presentation.PairingCode,
                        "pairing-code",
                        $"Pairing code {presentation.PairingCode}")
                    .Classes("pairing-code"));
            }
            return new WidgetView(
                UI.Stack("ytmusic-root", children.ToArray())
                    .Classes("ytmusic-widget", "is-loading"),
                Surface: StandardSurface);
        }

        if (connection != YtMusicWidgetConnectionState.Connected)
        {
            var primaryId = connection == YtMusicWidgetConnectionState.Error
                ? "retry"
                : YtMusicActionPolicy.ConnectId;
            var primaryLabel = connection == YtMusicWidgetConnectionState.Error
                ? "Retry"
                : "Connect";
            return new WidgetView(
                UI.Stack("ytmusic-root",
                    Header(presentation.Status, connection),
                    UI.Text(
                        "YTMDesktop2 owns playback; this widget talks only to its loopback API.",
                        "connection-help", "Connection help")
                        .Classes("connection-help"),
                    UI.Row("connection-actions",
                        UI.Button(primaryLabel, YtMusicActionPolicy.ConnectId, primaryId)
                            .FocusRight("pair")
                            .Classes("primary-action"),
                        UI.Button("Pair device", YtMusicActionPolicy.PairId, "pair")
                            .FocusLeft(primaryId)
                            .Classes("connection-action"))
                    .Classes("connection-actions"))
                .Classes("ytmusic-widget", "is-disconnected"),
                InitialFocusId: primaryId,
                Surface: StandardSurface);
        }

        if (!presentation.HasCurrentPlayback)
        {
            return new WidgetView(
                UI.Stack("ytmusic-root",
                    Header(presentation.Status, connection),
                    UI.EmptyState(
                        "No track playing",
                        "Start playback in YTMDesktop2, then refresh now playing.",
                        "ytmusic-idle",
                        new ComponentAction(
                            "Refresh", YtMusicActionPolicy.RefreshId,
                            WidgetGlyph.Refresh),
                        WidgetGlyph.Music))
                    .Classes("ytmusic-widget", "is-connected", "is-idle"),
                InitialFocusId: "ytmusic-idle.action",
                Surface: StandardSurface);
        }

        var duration = snapshot.DurationSeconds > 0 ? snapshot.DurationSeconds : 1;
        var position = Math.Clamp(snapshot.PositionSeconds, 0, duration);
        var playLabel = snapshot.IsPlaying ? "Pause" : "Play";
        var shuffleEnabled = snapshot.IsShuffleEnabled is true;
        var repeatMode = snapshot.RepeatMode ?? YtMusicRepeatMode.Off;
        var ratingBusy = pendingCommands.Contains(YtMusicCommand.Like) ||
                         pendingCommands.Contains(YtMusicCommand.Dislike);
        WidgetElement artwork = string.IsNullOrWhiteSpace(snapshot.ArtworkUrl)
            ? UI.Icon(WidgetGlyph.Music, "artwork-placeholder", "No album artwork")
                .Classes("artwork-placeholder")
            : UI.Image(snapshot.ArtworkUrl, "album-artwork",
                    $"Album artwork for {snapshot.Title}", ImageFit.Cover)
                .Classes("artwork-image");
        return new WidgetView(
            UI.VerticalScroll("ytmusic-root",
                Header(presentation.Status, connection),
                UI.Row("media-layout",
                    UI.Stack("artwork-frame", artwork).Classes("artwork-frame"),
                    UI.Stack("media-details",
                        UI.Stack("track-details",
                            UI.Text(snapshot.Title, "track-title",
                                    $"Track {snapshot.Title}")
                                .Classes("track-title"),
                            UI.Text(snapshot.Artist, "track-artist",
                                    $"Artist {snapshot.Artist}")
                                .Classes("track-artist"),
                            UI.Text(snapshot.Album, "track-album",
                                    string.IsNullOrWhiteSpace(snapshot.Album)
                                        ? "No album"
                                        : $"Album {snapshot.Album}")
                                .Classes("track-album"))
                            .Classes("track-details"),
                        UI.Row("progress-row",
                            UI.Text(FormatTime(position), "position-text",
                                    "Current playback position")
                                .Classes("time"),
                            UI.Progress(position, duration, "track-progress",
                                    $"{FormatTime(position)} of {FormatTime(snapshot.DurationSeconds)}")
                                .Classes("track-progress"),
                            UI.Text(FormatTime(snapshot.DurationSeconds),
                                    "duration-text", "Track duration")
                                .Classes("time"))
                            .Classes("progress-row"),
                        UI.Row("primary-actions",
                            UI.Button("", "previous", "previous")
                                .Icon(WidgetGlyph.Previous, "Previous track")
                                .FocusRight("play-pause").FocusDown("shuffle")
                                .Classes("transport-action"),
                            UI.Button("", "toggle-playback", "play-pause")
                                .Icon(snapshot.IsPlaying ? WidgetGlyph.Pause : WidgetGlyph.Play,
                                    playLabel)
                                .FocusLeft("previous").FocusRight("next").FocusDown("like")
                                .Classes("play-action",
                                    snapshot.IsPlaying ? "is-playing" : "is-paused"),
                            UI.Button("", "next", "next")
                                .Icon(WidgetGlyph.Next, "Next track")
                                .FocusLeft("play-pause").FocusRight("refresh").FocusDown("dislike")
                                .Classes("transport-action"),
                            UI.Button("", YtMusicActionPolicy.RefreshId, "refresh")
                                .Icon(WidgetGlyph.Refresh, "Refresh now playing")
                                .FocusLeft("next").FocusDown("repeat")
                                .Classes("refresh-action")),
                        UI.Row("secondary-actions",
                            UI.Button("", "shuffle", "shuffle")
                                .Icon(WidgetGlyph.Shuffle,
                                    shuffleEnabled ? "Turn shuffle off" : "Turn shuffle on")
                                .Selected(shuffleEnabled)
                                .Busy(pendingCommands.Contains(YtMusicCommand.Shuffle))
                                .FocusUp("previous").FocusRight("like")
                                .Classes("secondary-action",
                                    shuffleEnabled ? "is-active" : "is-inactive"),
                            UI.Button("", "like", "like")
                                .Icon(WidgetGlyph.Like,
                                    snapshot.IsLiked ? "Unlike track" : "Like track")
                                .Selected(snapshot.IsLiked).Busy(ratingBusy)
                                .FocusUp("play-pause").FocusLeft("shuffle").FocusRight("dislike")
                                .Classes("secondary-action",
                                    snapshot.IsLiked ? "is-active" : "is-inactive"),
                            UI.Button("", "dislike", "dislike")
                                .Icon(WidgetGlyph.Dislike,
                                    snapshot.IsDisliked ? "Remove dislike" : "Dislike track")
                                .Selected(snapshot.IsDisliked).Busy(ratingBusy)
                                .FocusUp("next").FocusLeft("like").FocusRight("repeat")
                                .Classes("secondary-action",
                                    snapshot.IsDisliked ? "is-active" : "is-inactive"),
                            UI.Button("", "repeat", "repeat")
                                .Icon(repeatMode == YtMusicRepeatMode.One
                                        ? WidgetGlyph.RepeatOne
                                        : WidgetGlyph.Repeat,
                                    RepeatAccessibilityLabel(repeatMode))
                                .Selected(repeatMode != YtMusicRepeatMode.Off)
                                .Busy(pendingCommands.Contains(YtMusicCommand.Repeat))
                                .FocusUp("refresh").FocusLeft("dislike")
                                .Classes("secondary-action",
                                    repeatMode == YtMusicRepeatMode.Off
                                        ? "is-inactive"
                                        : "is-active",
                                    $"repeat-{repeatMode.ToString().ToLowerInvariant()}")))
                        .Classes("media-details"))
                    .Classes("media-layout"))
                .Shortcut(ControllerButton.LeftBumper, "previous", label: "Previous track")
                .Shortcut(ControllerButton.X, "toggle-playback", label: "Play or pause")
                .Shortcut(ControllerButton.RightBumper, "next", label: "Next track")
                .Shortcut(ControllerButton.Y, YtMusicActionPolicy.RefreshId, label: "Refresh")
                .Classes("ytmusic-widget", "is-connected"),
            InitialFocusId: "play-pause",
            QuickActions: ConnectedQuickActions,
            Surface: StandardSurface);
    }

    internal static string FormatTime(double seconds)
    {
        var safeSeconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var value = TimeSpan.FromSeconds(safeSeconds);
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static StackElement Header(
        string status,
        YtMusicWidgetConnectionState connection) =>
        UI.Stack("header",
            UI.Text("YT MUSIC", "widget-title", "YouTube Music")
                .Classes("widget-title"),
            UI.Text(status, "connection-status", status).Classes(
                "connection-status",
                connection == YtMusicWidgetConnectionState.Connected
                    ? "is-connected"
                    : "is-disconnected"));

    private static string RepeatAccessibilityLabel(
        YtMusicRepeatMode mode) => mode switch
        {
            YtMusicRepeatMode.All => "Repeat all · change repeat mode",
            YtMusicRepeatMode.One => "Repeat one · change repeat mode",
            _ => "Repeat off · change repeat mode",
        };

    private static WidgetQuickActionCapability LoopbackControlAuthority() => new(
        WidgetLoopbackCapabilities.CapabilityId(YtmDesktopApiClient.CompanionPort),
        WidgetLoopbackCapabilities.PostJsonOperation);
}
