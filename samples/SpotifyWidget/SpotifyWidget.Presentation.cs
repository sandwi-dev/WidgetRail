using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

public sealed partial class SpotifyWidget
{
    public override WidgetView Render()
    {
        var presentation = CapturePresentationState();
        if (presentation.ShowSetup)
            return RenderSetup(presentation.Status, presentation.SetupViewGeneration);
        var header = Header(presentation.Status, presentation.ViewState);
        return presentation.ViewState switch
        {
            SpotifyWidgetViewState.Unconfigured => RenderUnconfigured(header),
            SpotifyWidgetViewState.Disconnected => RenderDisconnected(header, false),
            SpotifyWidgetViewState.Authorizing => RenderAuthorizing(header),
            SpotifyWidgetViewState.PermissionDenied => RenderPermissionDenied(header),
            SpotifyWidgetViewState.ServiceUnavailable => RenderError(
                header, "Spotify service unavailable",
                "The trusted Spotify provider is not available. Try again after the host recovers."),
            SpotifyWidgetViewState.Error => RenderError(
                header, "Spotify could not be loaded",
                "The provider returned an unexpected error. Retry without leaving the overlay."),
            SpotifyWidgetViewState.Ready => RenderConnected(header, presentation),
            _ => RenderLoading(header),
        };
    }

    private static StackElement Header(string status, SpotifyWidgetViewState state) =>
        UI.Stack("spotify.header",
                UI.Text("SPOTIFY", "spotify.eyebrow", "Spotify").Classes("spotify-eyebrow"),
                UI.Row("spotify.heading",
                        UI.Text("Music", "spotify.title", "Spotify music").Classes("spotify-title"),
                        UI.Text(status, "spotify.status", status).Classes("spotify-status",
                            state is SpotifyWidgetViewState.Error or
                                SpotifyWidgetViewState.PermissionDenied
                                ? "is-error" : state == SpotifyWidgetViewState.Ready
                                    ? "is-live" : "is-neutral"))
                    .Classes("spotify-heading"))
            .Classes("spotify-header");

    private static WidgetView RenderLoading(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Loading Spotify", "Checking your local account state…"))
            .InputScope(InputScope).Classes("spotify-widget"),
        Surface: StandardSurface);

    private static WidgetView RenderUnconfigured(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Settings, "Client ID required",
                    "Add your own Spotify developer Client ID. No client secret belongs in this widget.",
                    UI.Button("Setup instructions", "spotify.setup.open", "spotify.setup.open")
                        .Icon(WidgetGlyph.Settings, "Open Spotify setup instructions")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.setup.open", Surface: StandardSurface);

    private static WidgetView RenderDisconnected(StackElement header, bool reconnect) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, reconnect ? "Reconnect Spotify" : "Connect Spotify",
                    "Spotify opens a browser and uses PKCE. Your credentials stay in the trusted host.",
                    UI.Row("spotify.connect-actions",
                            UI.Button(reconnect ? "Reconnect" : "Connect", "spotify.connect",
                                    "spotify.connect")
                                .Icon(WidgetGlyph.Play, "Connect Spotify account")
                                .FocusRight("spotify.setup.open")
                                .Classes("spotify-primary", "spotify-responsive-action"),
                            UI.Button("Setup", "spotify.setup.open", "spotify.setup.open")
                                .Icon(WidgetGlyph.Settings, "Open setup instructions")
                                .FocusLeft("spotify.connect")
                                .Classes("spotify-secondary", "spotify-responsive-action"))
                        .Classes("spotify-connect-actions")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.connect", Surface: StandardSurface);

    private static WidgetView RenderAuthorizing(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Finish in your browser",
                    "The overlay is waiting for Spotify. Canceling or closing the browser will leave you disconnected."))
            .InputScope(InputScope).Classes("spotify-widget"),
        Surface: StandardSurface);

    private static WidgetView RenderPermissionDenied(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Settings, "Spotify permission is off",
                    "Allow the declared Spotify capabilities in Settings, then retry.",
                    UI.Button("Retry", "spotify.retry", "spotify.retry")
                        .Icon(WidgetGlyph.Refresh, "Retry Spotify")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.retry", Surface: StandardSurface);

    private static WidgetView RenderError(StackElement header, string title, string detail) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, title, detail,
                    UI.Button("Try again", "spotify.retry", "spotify.retry")
                        .Icon(WidgetGlyph.Refresh, "Try Spotify again")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.retry", Surface: StandardSurface);

    private static WidgetView RenderIdle(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Nothing playing",
                    "Start playback in Spotify on one of your devices, then refresh here.",
                    UI.Row("spotify.idle-actions",
                            UI.Button("Refresh", "spotify.refresh", "spotify.refresh")
                                .Icon(WidgetGlyph.Refresh, "Refresh Spotify playback")
                                .FocusRight("spotify.disconnect")
                                .Classes("spotify-primary", "spotify-responsive-action"),
                            UI.Button("Disconnect", "spotify.disconnect", "spotify.disconnect")
                                .FocusLeft("spotify.refresh")
                                .Classes("spotify-secondary", "spotify-responsive-action"))
                        .Classes("spotify-connect-actions")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.refresh", Surface: StandardSurface);

    private static WidgetView RenderSetup(string status, long setupViewGeneration)
    {
        var instructions = UI.Stack("spotify.setup-card",
                        UI.Text("Spotify setup", "spotify.setup-title",
                                "Spotify developer app setup")
                            .Classes("spotify-state-title", "spotify-setup-title"),
                        // Keep the only focus target at the top of the scroll
                        // surface. Placing it after the instructions makes the
                        // renderer correctly reveal that focused descendant,
                        // which opens the page already scrolled past its title.
                        UI.Button("Check configuration", "spotify.setup.done",
                                "spotify.setup.done")
                            .Classes("spotify-primary"),
                        UI.Text("1. Create an app in the Spotify developer dashboard.",
                                "spotify.setup-step-1").Classes("spotify-setup-step"),
                        UI.Text($"2. Add this exact redirect URI: {WidgetSpotifyService.ExactRedirectUri}",
                                "spotify.setup-step-2").Classes("spotify-setup-step"),
                        UI.Text("3. Save only the public Client ID; never enter a Client Secret.",
                                "spotify.setup-step-3").Classes("spotify-setup-step"),
                        UI.CodeText("dotnet run --project .\\tools\\GbarCli\\GbarCli.csproj -- config set org.gbar.samples.spotify client-id YOUR_CLIENT_ID --publisher org.gbar.samples",
                                "spotify.setup-command", "Client ID configuration command")
                            .AddClasses("spotify-setup-command"))
                    .Classes("spotify-setup-card");
        var setupScroll = UI.VerticalScroll(
                $"spotify.setup-scroll.{setupViewGeneration}", instructions)
            .InputScope(SetupScope)
            .Shortcut(ControllerButton.B, "spotify.setup.close")
            .Classes("spotify-setup-scroll");
        var root = UI.Stack("spotify.setup-root",
                Header(status, SpotifyWidgetViewState.Unconfigured),
                setupScroll)
            .Classes("spotify-widget", "spotify-setup");
        return new WidgetView(root, "spotify.setup.done", ActiveInputScopeId: SetupScope,
            Surface: StandardSurface);
    }

    private static WidgetView RenderConnected(
        StackElement header,
        SpotifyPresentationState presentation)
    {
        var playback = presentation.Playback;
        var pending = presentation.PendingOperation;
        var destination = presentation.Destination;
        var playlists = presentation.Playlists;
        var playlistDetail = presentation.PlaylistDetail;
        var wide = UI.Row("spotify.shell.wide",
                NavigationRail(destination, "wide"),
                UI.Stack("spotify.player.wide", PlayerPanel(playback, pending, "wide"))
                    .Classes("spotify-player-pane"),
                UI.Stack("spotify.context.wide",
                        DestinationPage(destination, presentation.Queue, playlists,
                            playlistDetail, presentation.Devices, presentation.LocalPlayback,
                            presentation.PageLoading, presentation.PageError, "wide"))
                    .Classes("spotify-context-pane"))
            .Classes("spotify-shell", "spotify-shell-wide")
            .VisibleWhen(ResponsiveVisibility.ExpandedOnly);
        var compact = UI.Stack("spotify.shell.compact",
                NavigationTabs(destination, "compact"),
                UI.Stack("spotify.context.compact",
                        destination == SpotifyDestination.Player
                            ? UI.VerticalScroll("spotify.player.compact.scroll",
                                    PlayerPanel(playback, pending, "compact"))
                                .Classes("spotify-compact-player-scroll")
                            : DestinationPage(destination, presentation.Queue, playlists,
                                playlistDetail, presentation.Devices,
                                presentation.LocalPlayback, presentation.PageLoading,
                                presentation.PageError, "compact"))
                    .Classes("spotify-compact-pane"))
            .Classes("spotify-shell", "spotify-shell-compact")
            .VisibleWhen(ResponsiveVisibility.CompactOnly);

        var content = new List<WidgetElement> { header };
        if (presentation.RefreshWarning is { } warning)
            content.Add(UI.Alert(
                    warning.ConsecutiveFailures ==
                        SpotifyRefreshFailurePolicy.MaximumTrackedFailures
                            ? "Spotify updates still delayed"
                            : "Spotify update delayed",
                    $"{warning.Detail} Diagnostic: {warning.DiagnosticCode}.",
                    AlertTone.Warning,
                    "spotify.refresh-warning",
                    new ComponentAction("Retry now", "spotify.refresh", WidgetGlyph.Refresh))
                .Classes("spotify-refresh-warning"));
        content.Add(wide);
        content.Add(compact);
        var root = UI.Stack("spotify.root", content.ToArray())
            .InputScope(InputScope)
            .Classes("spotify-widget", "is-ready");
        if (playlistDetail is not null && destination == SpotifyDestination.Playlists)
            root = root.Shortcut(ControllerButton.B, "spotify.playlist.back");
        if (playback is { IsAvailable: true })
        {
            var disallowed = playback.DisallowedActions;
            var toggleBlocked = playback.IsPlaying ? disallowed.Pausing : disallowed.Resuming;
            if (!disallowed.SkippingPrevious)
                root = root.Shortcut(ControllerButton.LeftBumper, "spotify.previous");
            if (!toggleBlocked)
                root = root.Shortcut(ControllerButton.X, "spotify.play-toggle");
            if (!disallowed.SkippingNext)
                root = root.Shortcut(ControllerButton.RightBumper, "spotify.next");
        }
        root = root.Shortcut(ControllerButton.Y, "spotify.refresh");

        var quickActions = new List<WidgetQuickAction>();
        if (playback is { IsAvailable: true })
        {
            var blocked = playback.DisallowedActions;
            if (!blocked.SkippingPrevious)
                quickActions.Add(new(ControllerButton.LeftBumper, "spotify.previous",
                    "Previous track", PlaybackControlAuthority));
            if (!(playback.IsPlaying ? blocked.Pausing : blocked.Resuming))
                quickActions.Add(new(ControllerButton.X, "spotify.play-toggle",
                    playback.IsPlaying ? "Pause" : "Play", PlaybackControlAuthority));
            if (!blocked.SkippingNext)
                quickActions.Add(new(ControllerButton.RightBumper, "spotify.next",
                    "Next track", PlaybackControlAuthority));
        }
        var requestedPageFocus = destination == SpotifyDestination.Playlists
            ? playlistDetail is null
                ? playlists.RequestedFocusId
                : playlistDetail.Items.RequestedFocusId
            : null;
        var requestedInitialFocus = requestedPageFocus ?? presentation.ReadyInitialFocusId;
        if (destination == SpotifyDestination.Playlists && playlistDetail is not null &&
            playlistDetail.Items.Page is null)
        {
            var mode = playlistDetail.Selection.Mode;
            requestedInitialFocus = playlistDetail.Items.Error is null
                ? NavId(SpotifyDestination.Playlists, mode)
                : $"spotify.page.error.{mode}.action";
        }
        var resolvedInitialFocus = requestedInitialFocus == "spotify.play-toggle" &&
            playback is not { IsAvailable: true, Item: not null }
                ? "spotify.player.empty.wide.action"
                : requestedInitialFocus;
        return new WidgetView(root, resolvedInitialFocus, quickActions,
            ActiveInputScopeId: InputScope, Surface: StandardSurface);
    }

    private static WidgetElement NavigationRail(SpotifyDestination selected, string mode) =>
        UI.Stack($"spotify.navigation.{mode}",
                NavigationButton(SpotifyDestination.Player, selected, mode, WidgetGlyph.Music),
                NavigationButton(SpotifyDestination.Queue, selected, mode, WidgetGlyph.Next),
                NavigationButton(SpotifyDestination.Playlists, selected, mode, WidgetGlyph.Music),
                NavigationButton(SpotifyDestination.Devices, selected, mode, WidgetGlyph.Connection))
            .Classes("spotify-navigation", "spotify-navigation-rail");

    private static WidgetElement NavigationTabs(SpotifyDestination selected, string mode) =>
        UI.SegmentedTabs($"spotify.navigation.{mode}", NavId(selected, mode),
                new(NavId(SpotifyDestination.Player, mode), "Player", "spotify.nav.player"),
                new(NavId(SpotifyDestination.Queue, mode), "Queue", "spotify.nav.queue"),
                new(NavId(SpotifyDestination.Playlists, mode), "Playlists", "spotify.nav.playlists"),
                new(NavId(SpotifyDestination.Devices, mode), "Devices", "spotify.nav.devices"))
            .Classes("spotify-navigation-tabs");

    private static ButtonElement NavigationButton(
        SpotifyDestination destination,
        SpotifyDestination selected,
        string mode,
        WidgetGlyph glyph) =>
        UI.Button(DestinationLabel(destination), $"spotify.nav.{DestinationToken(destination)}",
                NavId(destination, mode))
            .Icon(glyph, $"Open {DestinationLabel(destination)}")
            .Selected(destination == selected)
            .Classes("spotify-nav-button");

    private static WidgetElement PlayerPanel(
        WidgetSpotifyPlaybackSummary? playback,
        WidgetSpotifyPlaybackOperation? pending,
        string mode)
    {
        if (playback is not { IsAvailable: true, Item: not null })
            return UI.EmptyState("Nothing playing",
                    "Choose a playlist or start Spotify on a device.",
                    $"spotify.player.empty.{mode}",
                    new ComponentAction("Refresh", "spotify.refresh", WidgetGlyph.Refresh),
                    WidgetGlyph.Music)
                .Classes("spotify-player-card");

        var item = playback.Item;
        var duration = Math.Max(1, playback.DurationMilliseconds);
        var position = Math.Clamp(playback.ProgressMilliseconds, 0, duration);
        var disallowed = playback.DisallowedActions;
        var toggleBlocked = playback.IsPlaying ? disallowed.Pausing : disallowed.Resuming;
        // Preserve the original wide-player IDs so focus restoration and
        // controller gestures survive the 0.2 navigation-shell upgrade.
        var legacy = mode == "wide";
        var prefix = legacy ? "spotify" : $"spotify.player.{mode}";
        var artworkId = legacy ? "spotify.artwork" : $"{prefix}.artwork";
        var placeholderId = legacy ? "spotify.artwork-placeholder" : $"{prefix}.artwork-placeholder";
        var frameId = legacy ? "spotify.artwork-frame" : $"{prefix}.artwork-frame";
        var detailsId = legacy ? "spotify.details" : $"{prefix}.details";
        var titleId = legacy ? "spotify.track-title" : $"{prefix}.title";
        var subtitleId = legacy ? "spotify.track-subtitle" : $"{prefix}.subtitle";
        var contextId = legacy ? "spotify.context" : $"{prefix}.context";
        var controlsId = legacy ? "spotify.primary-controls" : $"{prefix}.controls";
        var attributionId = legacy ? "spotify.attribution" : $"{prefix}.attribution";
        var previousId = $"{prefix}.previous";
        var toggleId = $"{prefix}.play-toggle";
        var nextId = $"{prefix}.next";
        var shuffleId = $"{prefix}.shuffle";
        var repeatId = $"{prefix}.repeat";
        var seekId = $"{prefix}.seek";
        var seekSliderId = $"{seekId}.slider";
        WidgetElement artwork = string.IsNullOrWhiteSpace(item.ArtworkUrl)
            ? UI.Icon(WidgetGlyph.Music, placeholderId, "No artwork")
                .Classes("spotify-artwork-placeholder")
            : UI.Image(item.ArtworkUrl, artworkId, $"Artwork for {item.Title}",
                    ImageFit.Cover)
                .Classes("spotify-artwork");
        var previous = UI.IconButton(WidgetGlyph.Previous, "spotify.previous",
                previousId, "Previous track", size: IconButtonSize.Medium)
            .Disabled(disallowed.SkippingPrevious)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Previous)
            .FocusLeft(shuffleId).FocusUp(seekSliderId).FocusRight(toggleId)
            .Classes("spotify-transport");
        var toggle = UI.IconButton(playback.IsPlaying ? WidgetGlyph.Pause : WidgetGlyph.Play,
                "spotify.play-toggle", toggleId,
                playback.IsPlaying ? "Pause" : "Play", IconButtonVariant.Primary,
                IconButtonSize.Large)
            .Disabled(toggleBlocked)
            .Busy(pending is WidgetSpotifyPlaybackOperation.Play or
                WidgetSpotifyPlaybackOperation.Pause)
            .FocusLeft(previousId).FocusUp(seekSliderId).FocusRight(nextId)
            .Classes("spotify-play");
        var next = UI.IconButton(WidgetGlyph.Next, "spotify.next", nextId,
                "Next track", size: IconButtonSize.Medium)
            .Disabled(disallowed.SkippingNext)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Next)
            .FocusLeft(toggleId).FocusUp(seekSliderId).FocusRight(repeatId)
            .Classes("spotify-transport");
        var shuffle = UI.IconButton(WidgetGlyph.Shuffle, "spotify.shuffle",
                shuffleId, "Toggle shuffle", size: IconButtonSize.Small)
            .Selected(playback.ShuffleState)
            .Disabled(disallowed.TogglingShuffle)
            .FocusUp(seekSliderId).FocusRight(previousId)
            .Classes("spotify-secondary-action");
        var repeat = UI.IconButton(WidgetGlyph.Repeat, "spotify.repeat",
                repeatId, $"Repeat {playback.RepeatState.ToString().ToLowerInvariant()}",
                size: IconButtonSize.Small)
            .Selected(playback.RepeatState != WidgetSpotifyRepeatState.Off)
            .Disabled(RepeatUnavailable(playback))
            .FocusUp(seekSliderId).FocusLeft(nextId)
            .Classes("spotify-secondary-action");
        var seek = UI.Scrubber(TimeSpan.FromMilliseconds(position),
                TimeSpan.FromMilliseconds(duration), TimeSpan.FromSeconds(5), "spotify.seek",
                seekId, "Spotify playback position")
            .Disabled(disallowed.Seeking)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Seek)
            .FocusDown(toggleId)
            .RequireControllerActivation()
            .AddClasses("spotify-scrubber");

        return UI.Stack($"{prefix}.card",
                UI.Stack(frameId, artwork).Classes("spotify-artwork-frame"),
                UI.Stack(detailsId,
                        UI.Text(item.Title, titleId, item.Title)
                            .Classes("spotify-track-title"),
                        UI.Text(item.Subtitle, subtitleId, item.Subtitle)
                            .Classes("spotify-track-subtitle"),
                        UI.Text(item.ContextName ?? "Spotify", contextId,
                                item.ContextName ?? "Spotify")
                            .Classes("spotify-context"))
                    .Classes("spotify-details"),
                seek,
                UI.Row(controlsId, shuffle, previous, toggle, next, repeat)
                    .Classes("spotify-primary-controls"),
                UI.Text(playback.Attribution, attributionId, "Powered by Spotify")
                    .Classes("spotify-attribution"))
            .Classes("spotify-player-card");
    }

    private static WidgetElement DestinationPage(
        SpotifyDestination destination,
        WidgetSpotifyQueueSummary? queue,
        WidgetPagedResourceSnapshot<WidgetSpotifyPlaylistSummary> playlists,
        SpotifyPlaylistDetailPresentation? playlistDetail,
        WidgetSpotifyDevicesSummary? devices,
        WidgetSpotifyLocalPlaybackSummary? localPlayback,
        bool loading,
        string? error,
        string mode) =>
        destination switch
        {
            SpotifyDestination.Player => PlayerOverview(mode),
            SpotifyDestination.Queue => QueuePage(queue, loading, error, mode),
            SpotifyDestination.Playlists => PlaylistsPage(playlists, playlistDetail, mode),
            SpotifyDestination.Devices => DevicesPage(devices, localPlayback,
                loading, error, mode),
            _ => PlayerOverview(mode),
        };

    private static WidgetElement PlayerOverview(string mode) =>
        UI.Stack($"spotify.player-overview.{mode}",
                UI.SectionHeader("Now playing", $"spotify.player-overview.header.{mode}",
                    "SPOTIFY", "Playback stays available while you browse."),
                UI.Button("Refresh playback", "spotify.refresh", $"spotify.refresh.{mode}")
                    .Icon(WidgetGlyph.Refresh, "Refresh playback")
                    .Classes("spotify-page-action"),
                UI.Button("Disconnect account", "spotify.disconnect",
                        $"spotify.disconnect.{mode}")
                    .Classes("spotify-page-action", "is-quiet"))
            .Classes("spotify-page", "spotify-player-overview");

    private static WidgetElement QueuePage(
        WidgetSpotifyQueueSummary? queue, bool loading, string? error, string mode)
    {
        if (loading && queue is null) return LoadingPage("Loading queue", mode);
        if (error is not null) return PageFailure("Queue unavailable", error, mode);
        if (queue is null || queue.Items.Count == 0)
            return UI.EmptyState("Queue is empty", "Spotify has no upcoming items.",
                $"spotify.queue.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Next).Classes("spotify-page");
        var rows = queue.Items.Select((item, index) => MediaRow(item,
            $"spotify.queue.play.{index}", $"spotify.queue.item.{mode}.{index}"))
            .ToArray();
        return UI.Stack($"spotify.queue.page.{mode}",
                UI.SectionHeader("Up next", $"spotify.queue.header.{mode}", "QUEUE",
                    queue.IsTruncated ? "Showing Spotify's next items." :
                        $"{queue.Items.Count} upcoming items"),
                UI.VerticalScroll($"spotify.queue.scroll.{mode}", rows)
                    .Classes("spotify-page-scroll"))
            .Classes("spotify-page");
    }

    private static WidgetElement PlaylistsPage(
        WidgetPagedResourceSnapshot<WidgetSpotifyPlaylistSummary> playlists,
        SpotifyPlaylistDetailPresentation? playlistDetail,
        string mode)
    {
        if (playlistDetail is not null)
            return PlaylistDetail(playlistDetail.Selection.Playlist,
                playlistDetail.Items, mode);
        if (playlists.Status == WidgetPagedResourceStatus.Loading && playlists.Page is null)
            return LoadingPage("Loading playlists", mode);
        if (playlists.Error is { } playlistError && playlists.Page is null)
            return PageFailure("Playlists unavailable", playlistError.Message, mode);
        var page = playlists.Page;
        if (page is null || page.Items.Count == 0 && page.Total == 0)
            return UI.EmptyState("No playlists", "Your Spotify library has no playlists.",
                $"spotify.playlists.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Music).Classes("spotify-page");
        var rows = page.Items.Select((playlist, index) => UI.MediaTile(
                playlist.Name, $"{playlist.ItemCount} items",
                $"spotify.playlist.open.{page.Offset + index}",
                $"spotify.playlist.item.{mode}.{page.Offset + index}", playlist.OwnerName,
                playlist.Description, Artwork(playlist.ArtworkUrl, playlist.Name),
                $"Open playlist {playlist.Name}"))
            .Select(row => row.Classes("spotify-media-row"))
            .ToList<WidgetElement>();
        if (rows.Count == 0)
            rows.Add(SparsePagePlaceholder("playlist", mode));
        var first = page.Offset + 1;
        var last = page.Offset + page.Items.Count;
        var range = page.Items.Count == 0
            ? $"{first}–{Math.Min(page.Offset + page.Limit, page.Total)} unavailable"
            : $"{first}–{last} of {page.Total} playlists";
        var scroll = PaginateSnapshot(
            UI.VerticalScroll($"spotify.playlists.scroll.{mode}", rows.ToArray()),
            playlists, "spotify.playlists");
        var content = new List<WidgetElement>
        {
            UI.SectionHeader("Your playlists", $"spotify.playlists.header.{mode}",
                "LIBRARY", range),
            scroll.Classes("spotify-page-scroll"),
        };
        if (playlists.Error is { } retainedError)
            content.Add(RetainedPageError(retainedError.Message, mode));
        return UI.Stack($"spotify.playlists.page.{mode}", content.ToArray())
            .Classes("spotify-page");
    }

    private static WidgetElement PlaylistDetail(
        WidgetSpotifyPlaylistSummary playlist,
        WidgetPagedResourceSnapshot<WidgetSpotifyMediaItemSummary> items,
        string mode)
    {
        if (items.Status == WidgetPagedResourceStatus.Loading && items.Page is null)
            return LoadingPage($"Loading {playlist.Name}", mode);
        if (items.Error is { } itemError && items.Page is null)
            return PageFailure("Playlist unavailable", itemError.Message, mode);
        var page = items.Page;
        var rows = (page?.Items ?? []).Select((item, index) => MediaRow(item,
            $"spotify.playlist.track.{page!.Offset + index}",
            $"spotify.playlist.track.{mode}.{page.Offset + index}"))
            .ToList<WidgetElement>();
        if (page is { Total: > 0 } && rows.Count == 0)
            rows.Add(SparsePagePlaceholder("track", mode));
        var scroll = PaginateSnapshot(
            UI.VerticalScroll($"spotify.playlist.detail.scroll.{mode}", rows.ToArray()),
            items, "spotify.playlist.items");
        var loading = items.Status is WidgetPagedResourceStatus.Loading or
            WidgetPagedResourceStatus.Refreshing or
            WidgetPagedResourceStatus.LoadingAdjacent;
        var content = new List<WidgetElement>
        {
            UI.SectionHeader(playlist.Name, $"spotify.playlist.detail.header.{mode}",
                    "PLAYLIST", playlist.Description,
                    UI.Button("Play", "spotify.playlist.play",
                            $"spotify.playlist.play.{mode}")
                        .Icon(WidgetGlyph.Play, $"Play {playlist.Name}")
                        .Busy(loading).Classes("spotify-page-action",
                            "spotify-playlist-header-action"))
                .Classes("spotify-playlist-header"),
            scroll.Classes("spotify-page-scroll"),
        };
        if (items.Error is { } retainedError)
            content.Add(RetainedPageError(retainedError.Message, mode));
        return UI.Stack($"spotify.playlist.detail.{mode}", content.ToArray())
            .Classes("spotify-page", "spotify-playlist-detail");
    }

    private static ScrollElement PaginateSnapshot<TItem>(
        ScrollElement scroll,
        WidgetPagedResourceSnapshot<TItem> snapshot,
        string operationKey) where TItem : notnull
    {
        if (snapshot.Status == WidgetPagedResourceStatus.Error) return scroll;
        var previous = snapshot.HasPrevious ? $"{operationKey}.page.previous" : null;
        var next = snapshot.HasNext ? $"{operationKey}.page.next" : null;
        return previous is null && next is null
            ? scroll
            : scroll.Paginate(previous, next, 1);
    }

    private static ButtonElement SparsePagePlaceholder(string itemKind, string mode) =>
        UI.Button($"No available {itemKind}s on this page", "spotify.page.noop",
                $"spotify.page.sparse.{itemKind}.{mode}")
            .Classes("spotify-media-row", "is-quiet");

    private static WidgetElement RetainedPageError(string error, string mode) =>
        UI.Alert("More items unavailable", error, AlertTone.Warning,
                $"spotify.page.retained-error.{mode}",
                new ComponentAction("Try again", "spotify.page.retry", WidgetGlyph.Refresh))
            .Classes("spotify-page-inline-error");

    private static WidgetElement DevicesPage(
        WidgetSpotifyDevicesSummary? devices,
        WidgetSpotifyLocalPlaybackSummary? local,
        bool loading,
        string? error,
        string mode)
    {
        if (loading && devices is null) return LoadingPage("Loading devices", mode);
        if (error is not null) return PageFailure("Devices unavailable", error, mode);
        var rows = new List<WidgetElement>();
        if (local is not null)
        {
            var localActive = local.State == WidgetSpotifyLocalPlaybackState.Active;
            var localStarting = local.State == WidgetSpotifyLocalPlaybackState.Starting;
            var localNeedsReconnect = local.State ==
                WidgetSpotifyLocalPlaybackState.ReauthorizationRequired;
            rows.Add(UI.SettingsRow("This overlay",
                    new ComponentAction(localActive ? "Stop" : localStarting ? "Starting…" :
                        localNeedsReconnect ? "Reconnect" : "Play here",
                        localActive ? "spotify.local.stop" : localNeedsReconnect
                            ? "spotify.connect.features" : "spotify.local.start",
                        localActive ? WidgetGlyph.Pause : localNeedsReconnect
                            ? WidgetGlyph.Refresh : WidgetGlyph.Play),
                    $"spotify.local.{mode}",
                    local.DisplayMessage ?? "Web Playback SDK audio stays in the trusted host.",
                    local.DeviceName,
                    LocalStateLabel(local.State),
                    LocalStateTone(local.State),
                    isDisabled: local.State is WidgetSpotifyLocalPlaybackState.PremiumRequired or
                        WidgetSpotifyLocalPlaybackState.Unavailable || localStarting,
                    isBusy: loading || localStarting,
                    glyph: WidgetGlyph.Music).Classes("spotify-device-row"));
        }
        if (devices is not null)
        {
            rows.AddRange(devices.Devices.Select((device, index) => (device, index))
                .Where(entry => !entry.device.IsLocalHost)
                .Select(entry => UI.ChoiceRow(
                        entry.device.Name, $"spotify.device.select.{entry.index}",
                        $"spotify.device.{mode}.{entry.index}", entry.device.IsActive,
                        entry.device.IsRestricted, false, WidgetGlyph.Connection,
                        $"{entry.device.Name}, {entry.device.Type}" )
                    .Classes("spotify-device-row")));
        }
        if (rows.Count == 0)
            return UI.EmptyState("No Spotify devices", "Open Spotify on another device or play here.",
                $"spotify.devices.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Connection).Classes("spotify-page");
        return UI.Stack($"spotify.devices.page.{mode}",
                UI.SectionHeader("Playback devices", $"spotify.devices.header.{mode}",
                    "DEVICES", "Move playback without exposing Spotify device IDs."),
                UI.VerticalScroll($"spotify.devices.scroll.{mode}", rows.ToArray())
                    .Classes("spotify-page-scroll"))
            .Classes("spotify-page");
    }

    private static WidgetElement LoadingPage(string label, string mode) =>
        UI.Stack($"spotify.page.loading.{mode}",
                UI.LoadingIndicator($"spotify.page.loading.indicator.{mode}", label),
                UI.Text(label, $"spotify.page.loading.label.{mode}", label))
            .Classes("spotify-page", "spotify-page-loading");

    private static WidgetElement PageFailure(string title, string error, string mode) =>
        UI.Alert(title, error, AlertTone.Warning, $"spotify.page.error.{mode}",
                new ComponentAction(
                    error.Contains("Reconnect", StringComparison.OrdinalIgnoreCase)
                        ? "Reconnect" : "Try again",
                    error.Contains("Reconnect", StringComparison.OrdinalIgnoreCase)
                        ? "spotify.connect.features" : "spotify.page.retry",
                    WidgetGlyph.Refresh))
            .Classes("spotify-page", "spotify-page-error");

    private static ActionSurfaceElement MediaRow(
        WidgetSpotifyMediaItemSummary item,
        string action,
        string id) => UI.MediaTile(item.Title, FormatTime(item.DurationMilliseconds),
            action, id, item.Subtitle, null, Artwork(item.ArtworkUrl, item.Title),
            $"Play {item.Title} by {item.Subtitle}")
        .Disabled(!item.IsPlayable)
        .Classes("spotify-media-row");

    private static TileArtwork Artwork(string? url, string label) =>
        string.IsNullOrWhiteSpace(url)
            ? TileArtwork.FromGlyph(WidgetGlyph.Music, label)
            : TileArtwork.FromHttps(url, label);

    private static string LocalStateLabel(WidgetSpotifyLocalPlaybackState state) => state switch
    {
        WidgetSpotifyLocalPlaybackState.Active => "Playing here",
        WidgetSpotifyLocalPlaybackState.Ready => "Ready",
        WidgetSpotifyLocalPlaybackState.Starting => "Starting",
        WidgetSpotifyLocalPlaybackState.PremiumRequired => "Premium required",
        WidgetSpotifyLocalPlaybackState.ReauthorizationRequired => "Reconnect required",
        WidgetSpotifyLocalPlaybackState.Unavailable => "Unavailable",
        WidgetSpotifyLocalPlaybackState.Error => "Error",
        _ => "Stopped",
    };

    private static StatusTone LocalStateTone(WidgetSpotifyLocalPlaybackState state) => state switch
    {
        WidgetSpotifyLocalPlaybackState.Active or WidgetSpotifyLocalPlaybackState.Ready =>
            StatusTone.Success,
        WidgetSpotifyLocalPlaybackState.Starting => StatusTone.Info,
        WidgetSpotifyLocalPlaybackState.PremiumRequired or
            WidgetSpotifyLocalPlaybackState.ReauthorizationRequired => StatusTone.Warning,
        WidgetSpotifyLocalPlaybackState.Error => StatusTone.Danger,
        _ => StatusTone.Neutral,
    };

    private static string NavId(SpotifyDestination destination, string mode) =>
        $"spotify.nav.{mode}.{DestinationToken(destination)}";

    private static string DestinationToken(SpotifyDestination destination) =>
        destination.ToString().ToLowerInvariant();

    private static string DestinationLabel(SpotifyDestination destination) => destination switch
    {
        SpotifyDestination.Player => "Player",
        SpotifyDestination.Queue => "Queue",
        SpotifyDestination.Playlists => "Playlists",
        SpotifyDestination.Devices => "Devices",
        _ => "Spotify",
    };

    private static WidgetElement StateCard(
        WidgetGlyph glyph,
        string title,
        string detail,
        WidgetElement? action = null)
    {
        var children = new List<WidgetElement>
        {
            UI.Icon(glyph, "spotify.state-icon", title).Classes("spotify-state-icon"),
            UI.Text(title, "spotify.state-title", title).Classes("spotify-state-title"),
            UI.Text(detail, "spotify.state-detail", detail).Classes("spotify-state-detail"),
        };
        if (action is not null) children.Add(action);
        var card = UI.Stack("spotify.state-card", children.ToArray())
            .Classes("spotify-state-card");
        return UI.VerticalScroll("spotify.state-scroll", card)
            .Classes("spotify-state-scroll");
    }

    private static bool RepeatUnavailable(WidgetSpotifyPlaybackSummary playback) =>
        playback.RepeatState switch
        {
            WidgetSpotifyRepeatState.Off => playback.DisallowedActions.TogglingRepeatContext,
            WidgetSpotifyRepeatState.Context => playback.DisallowedActions.TogglingRepeatTrack,
            _ => false,
        };

    public static string FormatTime(long milliseconds)
    {
        var totalSeconds = Math.Max(0, milliseconds / 1_000);
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }
}
