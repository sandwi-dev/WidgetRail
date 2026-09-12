using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal static class SpotifyPresentation
{
    // Two authored 82-DIP rows plus the section header and gaps fit the
    // 340-DIP minimum pinned surface. A third row would require clipping.
    private const int PinnedUpNextMaximumItems = 2;
    internal const string InputScope = "spotify.window";
    private static readonly WidgetIcon SpotifyFullLogo = WidgetIcon.PackageSvg(
        "spotify.brand.full-green", WidgetPackageIconColorMode.OriginalColor,
        WidgetGlyph.Music);
    internal const string CompactPinnedLayoutId = "spotify.pinned.compact";
    internal const string UpNextPinnedLayoutId = "spotify.pinned.up-next";
    internal const string CompactPinnedLayoutName = "Compact now playing";
    internal const string UpNextPinnedLayoutName = "Now playing + up next";
    internal const string CompactPinnedScope = "spotify.pinned.compact.scope";
    internal const string UpNextPinnedScope = "spotify.pinned.up-next.scope";
    private static readonly WidgetSurfaceHints StandardSurface = new()
    {
        Mode = WidgetSurfaceMode.Adaptive,
        PreferredWidth = 980,
        PreferredHeight = 700,
        MinimumWidth = 620,
        MinimumHeight = 400,
    };
    internal static readonly WidgetSurfaceHints CompactPinnedSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 360,
        PreferredHeight = 360,
        MinimumWidth = 320,
        MinimumHeight = 300,
    };
    internal static readonly WidgetSurfaceHints UpNextPinnedSurface = new()
    {
        Mode = WidgetSurfaceMode.Wide,
        PreferredWidth = 760,
        PreferredHeight = 420,
        MinimumWidth = 640,
        MinimumHeight = 340,
    };

    internal static WidgetView Render(
        SpotifyPresentationState presentation,
        PinnedLayoutHandle compactPinnedLayout,
        PinnedLayoutHandle upNextPinnedLayout)
    {
        var view = presentation.Navigation.Route == SpotifyRoute.Setup &&
            presentation.ViewState != SpotifyWidgetViewState.Ready
            ? RenderSetup(
                presentation.Status, presentation.SetupViewGeneration,
                presentation.SetupBusy)
            : presentation.ViewState switch
            {
                SpotifyWidgetViewState.Unconfigured => RenderUnconfigured(
                    Header(presentation.Status, presentation.ViewState)),
                SpotifyWidgetViewState.Disconnected => RenderDisconnected(
                    Header(presentation.Status, presentation.ViewState), false),
                SpotifyWidgetViewState.Authorizing => RenderAuthorizing(
                    Header(presentation.Status, presentation.ViewState)),
                SpotifyWidgetViewState.PermissionDenied => RenderPermissionDenied(
                    Header(presentation.Status, presentation.ViewState)),
                SpotifyWidgetViewState.ServiceUnavailable => RenderError(
                    Header(presentation.Status, presentation.ViewState),
                    "Spotify service unavailable",
                    "The trusted Spotify provider is not available. Try again after the host recovers."),
                SpotifyWidgetViewState.Error => RenderError(
                    Header(presentation.Status, presentation.ViewState),
                    "Spotify could not be loaded",
                    "The provider returned an unexpected error. Retry without leaving the overlay."),
                SpotifyWidgetViewState.Ready => RenderConnected(presentation),
                _ => RenderLoading(Header(presentation.Status, presentation.ViewState)),
            };
        return view with
        {
            PinnedLayouts =
            [
                CreatePinnedLayout(presentation, compactPinnedLayout,
                    includeUpNext: false),
                CreatePinnedLayout(presentation, upNextPinnedLayout,
                    includeUpNext: true),
            ],
        };
    }

    private static PinnedPresentationLayout CreatePinnedLayout(
        SpotifyPresentationState presentation,
        PinnedLayoutHandle layout,
        bool includeUpNext)
    {
        var mode = includeUpNext ? "pinned-up-next" : "pinned-compact";
        var inputScope = layout.ActiveInputScopeId!;
        var content = new List<WidgetElement>();
        string? initialFocusId;
        if (presentation.ViewState == SpotifyWidgetViewState.Ready)
        {
            var playerFocusId = presentation.Playback is
                { IsAvailable: true, Item: not null }
                    ? $"spotify.player.{mode}.play-toggle"
                    : $"spotify.player.empty.{mode}.action";
            var nextItemId = includeUpNext && presentation.Queue.Items.Count != 0
                ? SpotifyCollectionIdentity.FocusId(
                    "spotify.queue.item", mode, presentation.Queue.Items[0].Key)
                : null;
            var player = PlayerPanel(
                presentation.Playback,
                presentation.PendingOperation,
                mode,
                pinned: true,
                adjacentFocusId: nextItemId);
            if (includeUpNext)
            {
                content.Add(UI.Row($"spotify.{mode}.shell",
                        player.AddClasses("spotify-pinned-player-up-next"), PinnedUpNext(
                            presentation.Queue, mode, playerFocusId))
                    .Classes("spotify-pinned-shell"));
            }
            else
            {
                content.Add(player);
            }
            initialFocusId = playerFocusId;
        }
        else
        {
            var state = PinnedState(presentation.ViewState, mode);
            content.Add(state.Element);
            initialFocusId = state.InitialFocusId;
        }

        var root = UI.Stack($"spotify.{mode}.root", content.ToArray())
            .InputScope(inputScope)
            .Classes("spotify-pinned-root",
                includeUpNext ? "spotify-pinned-up-next" : "spotify-pinned-now-playing");
        root = ApplyPlaybackShortcuts(root, presentation.Playback)
            .Shortcut(ControllerButton.Y, "spotify.refresh", label: "Refresh");
        return layout.Present(root, initialFocusId);
    }

    private static (WidgetElement Element, string? InitialFocusId) PinnedState(
        SpotifyWidgetViewState state,
        string mode) => state switch
        {
            SpotifyWidgetViewState.Unconfigured => (
                StateCard(WidgetGlyph.Settings, "Client ID required",
                    "Open Spotify setup in the full widget.",
                    UI.Button("Open setup", "spotify.setup.open",
                            $"spotify.{mode}.setup")
                        .Icon(WidgetGlyph.Settings, "Open Spotify setup")
                        .Classes("spotify-primary")),
                $"spotify.{mode}.setup"),
            SpotifyWidgetViewState.Disconnected => (
                StateCard(WidgetGlyph.Music, "Connect Spotify",
                    "Connect your account to show playback here.",
                    UI.Button("Connect", "spotify.connect",
                            $"spotify.{mode}.connect")
                        .Icon(WidgetGlyph.Play, "Connect Spotify account")
                        .Classes("spotify-primary")),
                $"spotify.{mode}.connect"),
            SpotifyWidgetViewState.PermissionDenied => (
                StateCard(WidgetGlyph.Settings, "Spotify permission is off",
                    "Allow the declared capabilities in Settings, then retry.",
                    UI.Button("Retry", "spotify.retry", $"spotify.{mode}.retry")
                        .Icon(WidgetGlyph.Refresh, "Retry Spotify")
                        .Classes("spotify-primary")),
                $"spotify.{mode}.retry"),
            SpotifyWidgetViewState.ServiceUnavailable or SpotifyWidgetViewState.Error => (
                StateCard(WidgetGlyph.Music, "Spotify unavailable",
                    "Spotify could not load. Retry from this pinned view.",
                    UI.Button("Try again", "spotify.retry", $"spotify.{mode}.retry")
                        .Icon(WidgetGlyph.Refresh, "Try Spotify again")
                        .Classes("spotify-primary")),
                $"spotify.{mode}.retry"),
            SpotifyWidgetViewState.Authorizing => (
                StateCard(WidgetGlyph.Music, "Finish in your browser",
                    "This pinned view will update when Spotify finishes connecting."),
                null),
            _ => (
                StateCard(WidgetGlyph.Music, "Loading Spotify",
                    "Checking your local account state…"),
                null),
        };

    private static WidgetElement PinnedUpNext(
        WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem> queue,
        string mode,
        string playerFocusId)
    {
        WidgetElement content;
        if (queue.Items.Count != 0)
        {
            var items = queue.Items.Take(PinnedUpNextMaximumItems).ToArray();
            var projectedAnchor = queue.Anchor is { } retainedAnchor &&
                                  items.Any(item => item.Key == retainedAnchor)
                ? retainedAnchor
                : items[0].Key;
            var rows = items.Select((item, index) =>
            {
                var itemId = SpotifyCollectionIdentity.FocusId(
                    "spotify.queue.item", mode, item.Key);
                var row = MediaRow(item.Value,
                        $"spotify.queue.play.{item.Key.Value}", itemId,
                        SpotifyCollectionIdentity.FocusId(
                            "spotify.queue.persist", "shared", item.Key))
                    .FocusLeft(playerFocusId);
                if (index != 0)
                    row = row.FocusUp(SpotifyCollectionIdentity.FocusId(
                        "spotify.queue.item", mode, items[index - 1].Key));
                if (index + 1 != items.Length)
                    row = row.FocusDown(SpotifyCollectionIdentity.FocusId(
                        "spotify.queue.item", mode, items[index + 1].Key));
                return row.CollectionItem(item.Key)
                    .Classes("spotify-pinned-next-row");
            }).ToArray();
            content = UI.VerticalScroll($"spotify.{mode}.scroll", rows)
                .Classes("spotify-pinned-queue-scroll") with
            { CollectionAnchorKey = projectedAnchor.Value };
        }
        else if (queue.Status is WidgetPagedResourceStatus.Loading or
                 WidgetPagedResourceStatus.NotLoaded)
        {
            content = UI.LoadingIndicator(
                $"spotify.{mode}.loading", "Loading next queue item");
        }
        else if (queue.Error is { } error)
        {
            content = UI.Alert("Queue unavailable", error.Message, AlertTone.Warning,
                $"spotify.{mode}.error",
                new ComponentAction("Retry", "spotify.refresh", WidgetGlyph.Refresh));
        }
        else
        {
            content = UI.EmptyState("Queue is empty", "Spotify has no upcoming item.",
                $"spotify.{mode}.empty",
                new ComponentAction("Refresh", "spotify.refresh", WidgetGlyph.Refresh),
                WidgetGlyph.Next);
        }

        return UI.Stack($"spotify.{mode}.queue",
                UI.SectionHeader("Up next", $"spotify.{mode}.header", "QUEUE",
                    "The next item from Spotify's current queue."),
                content)
            .Classes("spotify-pinned-queue");
    }

    private static StackElement Header(
        string status,
        SpotifyWidgetViewState state)
    {
        var statusText = UI.Text(status, "spotify.status", status).Classes(
            "spotify-status",
            state is SpotifyWidgetViewState.Error or
                SpotifyWidgetViewState.PermissionDenied
                ? "is-error" : state == SpotifyWidgetViewState.Ready
                    ? "is-live" : "is-neutral");
        return UI.Stack("spotify.header",
                UI.Row("spotify.heading",
                        UI.Icon(SpotifyFullLogo, "spotify.brand.full-logo", "Spotify")
                            .Classes("spotify-full-logo"),
                        statusText)
                    .Classes("spotify-heading"))
            .Classes("spotify-header");
    }

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

    private static WidgetView RenderSetup(
        string status,
        long setupViewGeneration,
        bool setupBusy)
    {
        var setupScroll = SetupContent(
            setupViewGeneration, setupBusy, includeDisconnect: false);
        var root = UI.Stack("spotify.setup-root",
                Header(status, SpotifyWidgetViewState.Unconfigured),
                setupScroll)
            .Classes("spotify-widget", "spotify-setup");
        return new WidgetView(root, "spotify.setup.dashboard", Surface: StandardSurface);
    }

    private static ScrollElement SetupContent(
        long setupViewGeneration,
        bool setupBusy,
        bool includeDisconnect)
    {
        var actions = new List<WidgetElement>
        {
            UI.Text("Spotify setup", "spotify.setup-title",
                    "Spotify developer app setup")
                .Classes("spotify-state-title", "spotify-setup-title"),
            UI.Text("1. Create an app in the Spotify developer dashboard.",
                    "spotify.setup-step-1").Classes("spotify-setup-step"),
            UI.Button("Open developer dashboard", "spotify.setup.dashboard",
                    "spotify.setup.dashboard")
                .Disabled(setupBusy)
                .FocusDown("spotify.setup.copy-redirect")
                .Classes("spotify-secondary", "spotify-setup-action"),
            UI.Text($"2. Add this exact redirect URI: {SpotifyApplicationContract.ExactRedirectUri}",
                    "spotify.setup-step-2").Classes("spotify-setup-step"),
            UI.Button("Copy redirect URI", "spotify.setup.copy-redirect",
                    "spotify.setup.copy-redirect")
                .Disabled(setupBusy)
                .FocusUp("spotify.setup.dashboard")
                .FocusDown("spotify.setup.client-id")
                .Classes("spotify-secondary", "spotify-setup-action"),
            UI.Text("3. Save only the public Client ID; never enter a Client Secret.",
                    "spotify.setup-step-3").Classes("spotify-setup-step"),
            UI.TextEntry(string.Empty, "Enter public Spotify Client ID",
                    "spotify.setup.client-id", "spotify.setup.client-id",
                    SpotifyApplicationContract.MaximumClientIdInputCharacters)
                .Disabled(setupBusy)
                .FocusUp("spotify.setup.copy-redirect")
                .FocusDown("spotify.setup.done")
                .Classes("spotify-setup-input"),
            UI.Button("Check configuration", "spotify.setup.done",
                    "spotify.setup.done")
                .Disabled(setupBusy)
                .FocusUp("spotify.setup.client-id")
                .Classes("spotify-primary", "spotify-setup-action"),
        };
        var closeFocusUp = "spotify.setup.done";
        if (includeDisconnect)
        {
            actions[^1] = ((ButtonElement)actions[^1])
                .FocusDown("spotify.disconnect.setup");
            actions.Add(UI.Button("Disconnect account", "spotify.disconnect",
                    "spotify.disconnect.setup")
                .Disabled(setupBusy)
                .FocusUp("spotify.setup.done")
                .FocusDown("spotify.setup.close")
                .Classes("spotify-secondary", "spotify-setup-action", "is-quiet"));
            closeFocusUp = "spotify.disconnect.setup";
        }
        else
            actions[^1] = ((ButtonElement)actions[^1])
                .FocusDown("spotify.setup.close");
        actions.Add(UI.Button("Close setup", "spotify.setup.close",
                "spotify.setup.close")
            .FocusUp(closeFocusUp)
            .Classes("spotify-secondary", "spotify-setup-action", "is-quiet"));
        var instructions = UI.Stack("spotify.setup-card", actions.ToArray())
            .Classes("spotify-setup-card");
        return UI.VerticalScroll(
                $"spotify.setup-scroll.{setupViewGeneration}", instructions)
            .Classes("spotify-setup-scroll");
    }

    private static WidgetView RenderConnected(SpotifyPresentationState presentation)
    {
        var playback = presentation.Playback;
        var pending = presentation.PendingOperation;
        var route = presentation.Navigation;
        var destination = SpotifyRouteActionPolicy.Destination(route.RootRoute);
        var playlists = presentation.Playlists;
        var playlistDetail = presentation.PlaylistDetail;
        var setup = route.Route == SpotifyRoute.Setup;
        var page = setup
            ? SetupContent(
                presentation.SetupViewGeneration,
                presentation.SetupBusy,
                includeDisconnect: true)
            : destination == SpotifyDestination.Search ? SearchPage(presentation.Search)
            : DestinationPage(destination, presentation.Queue, playlists,
                playlistDetail, presentation.Devices, presentation.LocalPlayback,
                presentation.LocalPlaybackBusy, presentation.LocalPlaybackFeedback,
                presentation.PageLoading,
                presentation.PageError, "shared");
        var contentEntry = PageEntryFocusId(presentation, destination);
        var pageGroup = UI.Stack(SpotifyRouteActionPolicy.FocusGroupId(route.Route), page)
            .RememberChildFocus(contentEntry)
            .Classes("spotify-page-group");
        NavigationShellDestination[] destinations =
        [
            new("search", "Search", "spotify.nav.search", WidgetGlyph.Music,
                IsDisabled: setup),
            new("queue", "Queue", "spotify.nav.queue", WidgetGlyph.Next,
                IsDisabled: setup),
            new("playlists", "Playlists", "spotify.nav.playlists", WidgetGlyph.Music,
                IsDisabled: setup),
            new("devices", "Devices", "spotify.nav.devices", WidgetGlyph.Connection,
                IsDisabled: setup),
        ];
        var parts = UI.NavigationShellParts(
            "spotify.nav",
            DestinationToken(destination),
            contentEntry,
            pageGroup,
            destinations,
            compactLeadingAdornment: CompactControllerKey(
                "LT", "Left trigger, previous section", "spotify.section.previous.hint"),
            compactTrailingAdornment: CompactControllerKey(
                "RT", "Right trigger, next section", "spotify.section.next.hint"));

        var header = Header(presentation.Status, presentation.ViewState);
        var content = new List<WidgetElement> { header };
        if (!setup && presentation.RefreshWarning is { } warning)
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
        content.Add(UI.Row(
                "spotify.navigation.header",
                parts.CompactNavigation
                    .VisibleWhen(ResponsiveVisibility.Always)
                    .AddClasses("spotify-navigation-tabs"),
                UI.ControllerHint(
                        ControllerButton.Y,
                        "Settings",
                        "spotify.navigation.settings.hint")
                    .AddClasses("spotify-navigation-settings-hint"))
            .Classes("spotify-navigation-header"));
        if (!setup && presentation.LocalPlayback is
                { State: SpotifyLocalPlaybackState.AutoplayBlocked } blockedLocalPlayback)
            content.Add(UI.Alert(
                    "Local playback blocked",
                    blockedLocalPlayback.DisplayMessage ??
                        "Spotify audio was blocked by browser autoplay policy.",
                    AlertTone.Warning,
                    "spotify.local.autoplay-blocked")
                .Classes("spotify-local-feedback"));
        content.Add(UI.Row(
                "spotify.connected.panes",
                PlayerPanel(
                    playback, pending, "wide", pane: true,
                    controlsEnabled: !setup),
                pageGroup)
            .Classes("spotify-connected-panes"));
        var root = UI.Stack("spotify.root", content.ToArray())
            .Classes("spotify-widget", "is-ready");
        if (!setup)
            root = ApplyPlaybackShortcuts(root, playback)
                .Shortcut(ControllerButton.Y, "spotify.setup.open", label: "Settings");
        if (SpotifyRouteActionPolicy.CanSwitchSection(route.Route, route.Depth))
            root = root
                .Shortcut(ControllerButton.LeftTrigger,
                    "spotify.nav.previous-section", label: "Previous section")
                .Shortcut(ControllerButton.RightTrigger,
                    "spotify.nav.next-section", label: "Next section");

        var quickActions = new List<WidgetQuickAction>();
        if (!setup && playback is { IsAvailable: true })
        {
            var blocked = playback.DisallowedActions;
            if (!blocked.SkippingPrevious)
                quickActions.Add(new(ControllerButton.LeftBumper, "spotify.previous",
                    "Previous track"));
            if (!(playback.IsPlaying ? blocked.Pausing : blocked.Resuming))
                quickActions.Add(new(ControllerButton.X, "spotify.play-toggle",
                    playback.IsPlaying ? "Pause" : "Play"));
            if (!blocked.SkippingNext)
                quickActions.Add(new(ControllerButton.RightBumper, "spotify.next",
                    "Next track"));
        }
        return new WidgetView(root, contentEntry,
            quickActions, Surface: StandardSurface);
    }

    private static string PageEntryFocusId(
        SpotifyPresentationState presentation,
        SpotifyDestination destination)
    {
        const string mode = "shared";
        if (presentation.Navigation.Route == SpotifyRoute.Setup)
            return presentation.SetupBusy
                ? "spotify.setup.close"
                : "spotify.setup.dashboard";
        if (presentation.Navigation.Route == SpotifyRoute.PlaylistDetail &&
            presentation.PlaylistDetail is { } detail)
        {
            var items = detail.Items.Snapshot;
            if (items.Items.Count != 0 || items.Status == WidgetPagedResourceStatus.Ready)
                return "spotify.playlist.play.shared";
            return items.Error is null
                ? "spotify.page.loading.shared.action"
                : "spotify.page.error.shared.action";
        }
        if (destination == SpotifyDestination.Search) return "spotify.search.query";
        if (destination == SpotifyDestination.Queue)
        {
            var queue = presentation.Queue;
            if (queue.Items.Count != 0)
                return SpotifyCollectionIdentity.FocusId(
                    "spotify.queue.item", mode, queue.Items[0].Key);
            if (queue.Status is WidgetPagedResourceStatus.Loading or
                WidgetPagedResourceStatus.NotLoaded)
                return "spotify.page.loading.shared.action";
            if (queue.Error is not null || presentation.PageError is not null)
                return "spotify.page.error.shared.action";
            return "spotify.queue.empty.shared.action";
        }
        if (destination == SpotifyDestination.Playlists)
        {
            var items = presentation.Playlists.Snapshot;
            if (items.Items.Count != 0)
                return SpotifyCollectionIdentity.FocusId(
                    "spotify.playlist.item", mode, items.Items[0].Key);
            if (items.Status is WidgetPagedResourceStatus.Loading or
                WidgetPagedResourceStatus.NotLoaded)
                return "spotify.page.loading.shared.action";
            if (items.Error is not null)
                return "spotify.page.error.shared.action";
            return items.HasBefore || items.HasAfter || items.RequestedFocusId is not null
                ? "spotify.page.sparse.playlist.shared"
                : "spotify.playlists.empty.shared.action";
        }
        if (presentation.PageLoading && presentation.Devices is null)
            return "spotify.page.loading.shared.action";
        if (presentation.PageError is not null)
            return "spotify.page.error.shared.action";
        if (presentation.LocalPlayback is { State: not (
                SpotifyLocalPlaybackState.Starting or
                SpotifyLocalPlaybackState.PremiumRequired or
                SpotifyLocalPlaybackState.Unavailable) })
            return "spotify.local.shared.action";
        var remote = presentation.Devices?.Devices
            .Select((device, index) => (device, index))
            .FirstOrDefault(entry => !entry.device.IsLocalHost && !entry.device.IsRestricted);
        return remote is { device: not null }
            ? $"spotify.device.shared.{remote.Value.index}"
            : presentation.Devices is { Devices.Count: > 0 } ||
                presentation.LocalPlayback is not null
                ? "spotify.devices.refresh.shared"
                : "spotify.devices.empty.shared.action";
    }

    private static RowElement CompactControllerKey(
        string key,
        string accessibilityLabel,
        string id) => UI.Row(
            id,
            UI.Text(key, id + ".label", accessibilityLabel)
                .Classes("wrail-controller-hint__key", "spotify-section-trigger-key"))
        .Classes("spotify-section-trigger-hint");

    private static WidgetElement PlayerPanel(
        SpotifyPlaybackSummary? playback,
        SpotifyPlaybackOperation? pending,
        string mode,
        bool pinned = false,
        string? adjacentFocusId = null,
        bool docked = false,
        bool pane = false,
        bool controlsEnabled = true)
    {
        if (playback is not { IsAvailable: true, Item: not null })
        {
            if (docked)
            {
                return UI.Row(
                        $"spotify.player.empty.{mode}",
                        UI.Icon(WidgetGlyph.Music,
                                $"spotify.player.empty.{mode}.icon", "Nothing playing")
                            .Classes("spotify-player-empty-icon"),
                        UI.Stack(
                                $"spotify.player.empty.{mode}.copy",
                                UI.Text("Nothing playing",
                                        $"spotify.player.empty.{mode}.title",
                                        "Nothing playing")
                                    .Classes("spotify-player-empty-title"),
                                UI.Text("Choose a playlist or start Spotify on a device.",
                                        $"spotify.player.empty.{mode}.message",
                                        "Choose a playlist or start Spotify on a device.")
                                    .Classes("spotify-player-empty-message"))
                            .Classes("spotify-player-empty-copy"))
                    .Classes("spotify-player-card", "spotify-player-dock",
                        "spotify-player-standard", "spotify-player-empty");
            }
            var empty = UI.EmptyState("Nothing playing",
                    "Choose a playlist or start Spotify on a device.",
                    $"spotify.player.empty.{mode}",
                    controlsEnabled && !pane
                        ? new ComponentAction(
                            "Refresh", "spotify.refresh", WidgetGlyph.Refresh)
                        : null,
                    WidgetGlyph.Music);
            if (pane)
                return empty.Classes("spotify-player-card", "spotify-player-pane",
                    "spotify-player-standard");
            return docked
                ? empty.Classes("spotify-player-card", "spotify-player-dock",
                    "spotify-player-standard")
                : empty.Classes("spotify-player-card");
        }

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
        var compact = mode == "compact" || pinned || docked;
        WidgetElement artwork = string.IsNullOrWhiteSpace(item.ArtworkUrl)
            ? UI.Icon(WidgetGlyph.Music, placeholderId, "No artwork")
                .Classes("spotify-artwork-placeholder",
                    compact ? "spotify-compact-artwork-placeholder" :
                        "spotify-wide-artwork-placeholder")
            : UI.Image(item.ArtworkUrl, artworkId, $"Artwork for {item.Title}",
                    ImageFit.Cover)
                .Classes("spotify-artwork",
                    compact ? "spotify-compact-artwork" : "spotify-wide-artwork");
        if (docked)
            artwork = artwork.AddClasses(string.IsNullOrWhiteSpace(item.ArtworkUrl)
                ? "spotify-dock-artwork-placeholder" : "spotify-dock-artwork");
        var previous = UI.IconButton(WidgetGlyph.Previous, "spotify.previous",
                previousId, "Previous track", size: IconButtonSize.Medium)
            .Disabled(!controlsEnabled || disallowed.SkippingPrevious ||
                pending == SpotifyPlaybackOperation.Previous)
            .Busy(pending == SpotifyPlaybackOperation.Previous)
            .PersistFocusAs("spotify.transport.previous")
            .FocusLeft(shuffleId).FocusUp(seekSliderId).FocusRight(toggleId)
            .Classes("spotify-transport");
        var toggle = UI.IconButton(playback.IsPlaying ? WidgetGlyph.Pause : WidgetGlyph.Play,
                "spotify.play-toggle", toggleId,
                playback.IsPlaying ? "Pause" : "Play", IconButtonVariant.Primary,
                IconButtonSize.Large)
            .Disabled(!controlsEnabled || toggleBlocked ||
                pending is SpotifyPlaybackOperation.Play or
                SpotifyPlaybackOperation.Pause)
            .Busy(pending is SpotifyPlaybackOperation.Play or
                SpotifyPlaybackOperation.Pause)
            .PersistFocusAs("spotify.transport.play-toggle")
            .FocusLeft(previousId).FocusUp(seekSliderId).FocusRight(nextId)
            .Classes("spotify-play");
        var next = UI.IconButton(WidgetGlyph.Next, "spotify.next", nextId,
                "Next track", size: IconButtonSize.Medium)
            .Disabled(!controlsEnabled || disallowed.SkippingNext ||
                pending == SpotifyPlaybackOperation.Next)
            .Busy(pending == SpotifyPlaybackOperation.Next)
            .PersistFocusAs("spotify.transport.next")
            .FocusLeft(toggleId).FocusUp(seekSliderId).FocusRight(repeatId)
            .Classes("spotify-transport");
        var shuffle = UI.IconButton(WidgetGlyph.Shuffle, "spotify.shuffle",
                shuffleId, "Toggle shuffle", size: IconButtonSize.Small)
            .Selected(playback.ShuffleState)
            .Disabled(!controlsEnabled || disallowed.TogglingShuffle ||
                pending == SpotifyPlaybackOperation.SetShuffle)
            .Busy(pending == SpotifyPlaybackOperation.SetShuffle)
            .PersistFocusAs("spotify.transport.shuffle")
            .FocusUp(seekSliderId).FocusRight(previousId)
            .Classes("spotify-secondary-action");
        var repeat = UI.IconButton(WidgetGlyph.Repeat, "spotify.repeat",
                repeatId, $"Repeat {playback.RepeatState.ToString().ToLowerInvariant()}",
                size: IconButtonSize.Small)
            .Selected(playback.RepeatState != SpotifyRepeatState.Off)
            .Disabled(!controlsEnabled || RepeatUnavailable(playback) ||
                pending == SpotifyPlaybackOperation.SetRepeat)
            .Busy(pending == SpotifyPlaybackOperation.SetRepeat)
            .PersistFocusAs("spotify.transport.repeat")
            .FocusUp(seekSliderId).FocusLeft(nextId)
            .Classes("spotify-secondary-action");
        var seek = UI.Scrubber(TimeSpan.FromMilliseconds(position),
                TimeSpan.FromMilliseconds(duration), TimeSpan.FromSeconds(5), "spotify.seek",
                seekId, "Spotify playback position")
            .Disabled(!controlsEnabled || disallowed.Seeking ||
                pending == SpotifyPlaybackOperation.Seek)
            .Busy(pending == SpotifyPlaybackOperation.Seek)
            .PersistFocusAs("spotify.transport.seek")
            .FocusDown(toggleId)
            .RequireControllerActivation()
            .AddClasses("spotify-scrubber");
        if (adjacentFocusId is not null)
            repeat = repeat.FocusRight(adjacentFocusId);

        var title = UI.Text(item.Title, titleId, item.Title)
            .Classes("spotify-track-title",
                compact ? "spotify-compact-track-title" : "spotify-wide-track-title");
        var subtitle = UI.Text(item.Subtitle, subtitleId, item.Subtitle)
            .Classes("spotify-track-subtitle");
        var context = UI.Text(item.ContextName ?? "Spotify", contextId,
                item.ContextName ?? "Spotify")
            .Classes("spotify-context");
        if (docked)
        {
            title = title.AddClasses("spotify-dock-track-title");
            subtitle = subtitle.AddClasses("spotify-dock-track-subtitle");
            context = context.AddClasses("spotify-dock-context");
        }
        var details = UI.Stack(detailsId, title, subtitle, context)
                .Classes("spotify-details",
                    compact ? "spotify-compact-details" : "spotify-wide-details");
        if (docked) details = details.AddClasses("spotify-dock-details");
        var artworkFrame = UI.Stack(frameId, artwork).Classes("spotify-artwork-frame",
            compact ? "spotify-compact-artwork-frame" : "spotify-wide-artwork-frame");
        if (docked) artworkFrame = artworkFrame.AddClasses("spotify-dock-artwork-frame");
        var transport = UI.Stack($"{prefix}.transport",
                seek.AddClasses(compact ? "spotify-compact-scrubber" :
                    "spotify-wide-scrubber"),
                UI.Row(controlsId, shuffle, previous, toggle, next, repeat)
                    .Classes("spotify-primary-controls",
                        compact ? "spotify-compact-primary-controls" :
                            "spotify-wide-primary-controls"),
                UI.Text(playback.Attribution, attributionId, "Powered by Spotify")
                    .Classes("spotify-attribution",
                        compact ? "spotify-compact-attribution" :
                            "spotify-wide-attribution"))
            .Classes("spotify-player-transport");
        if (pane) transport = transport.AddClasses("spotify-player-pane-transport");
        if (docked)
            return UI.Row($"{prefix}.card", artworkFrame, details, transport)
                .Classes("spotify-player-card", "spotify-player-dock",
                    "spotify-player-standard");

        return UI.Stack($"{prefix}.card",
                artworkFrame,
                details,
                transport)
            .Classes("spotify-player-card",
                compact ? "spotify-player-card-compact" :
                    "spotify-player-card-wide",
                pane ? "spotify-player-pane" :
                    "spotify-player-flow",
                pinned ? "spotify-pinned-player" : "spotify-player-standard");
    }

    private static StackElement ApplyPlaybackShortcuts(
        StackElement root,
        SpotifyPlaybackSummary? playback)
    {
        if (playback is not { IsAvailable: true }) return root;
        var disallowed = playback.DisallowedActions;
        var toggleBlocked = playback.IsPlaying ? disallowed.Pausing : disallowed.Resuming;
        if (!disallowed.SkippingPrevious)
            root = root.Shortcut(
                ControllerButton.LeftBumper, "spotify.previous", label: "Previous track");
        if (!toggleBlocked)
            root = root.Shortcut(
                ControllerButton.X, "spotify.play-toggle",
                label: playback.IsPlaying ? "Pause" : "Play");
        if (!disallowed.SkippingNext)
            root = root.Shortcut(
                ControllerButton.RightBumper, "spotify.next", label: "Next track");
        return root;
    }

    private static WidgetElement DestinationPage(
        SpotifyDestination destination,
        WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem> queue,
        SpotifyCursorPresentation<SpotifyPlaylistCollectionItem> playlists,
        SpotifyPlaylistDetailPresentation? playlistDetail,
        SpotifyDevicesSummary? devices,
        SpotifyLocalPlaybackSummary? localPlayback,
        bool localPlaybackBusy,
        string? localPlaybackFeedback,
        bool loading,
        string? error,
        string mode) =>
        destination switch
        {
            SpotifyDestination.Queue => QueuePage(queue, loading, error, mode),
            SpotifyDestination.Playlists => PlaylistsPage(playlists, playlistDetail, mode),
            SpotifyDestination.Devices => DevicesPage(devices, localPlayback,
                localPlaybackBusy, localPlaybackFeedback, loading, error, mode),
            _ => PlaylistsPage(playlists, playlistDetail, mode),
        };

    private static WidgetElement QueuePage(
        WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem> queue,
        bool loading, string? error, string mode)
    {
        if (queue.Items.Count == 0 && queue.Status is (
                WidgetPagedResourceStatus.Loading or
                WidgetPagedResourceStatus.NotLoaded))
            return LoadingPage("Loading queue", mode);
        if (queue.Error is { } queueError && queue.Items.Count == 0)
            return PageFailure("Queue unavailable", queueError.Message, mode);
        if (error is not null && queue.Items.Count == 0)
            return PageFailure("Queue unavailable", error, mode);
        if (queue.Items.Count == 0)
            return UI.EmptyState("Queue is empty", "Spotify has no upcoming items.",
                $"spotify.queue.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Next).Classes("spotify-page");
        var rows = queue.Items.Select((item, index) => QueueRow(
                item,
                mode,
                index == 0
                    ? null
                    : SpotifyCollectionIdentity.FocusId(
                        "spotify.queue.item", mode, queue.Items[index - 1].Key),
                index == queue.Items.Count - 1
                    ? null
                    : SpotifyCollectionIdentity.FocusId(
                        "spotify.queue.item", mode, queue.Items[index + 1].Key)))
            .ToArray();
        var scroll = UI.VerticalScroll("spotify.queue.scroll", rows)
            .Classes("spotify-page-scroll") with
        { CollectionAnchorKey = queue.Anchor?.Value };
        return UI.Stack($"spotify.queue.page.{mode}",
                UI.SectionHeader("Up next", $"spotify.queue.header.{mode}", "QUEUE",
                    $"{queue.Items.Count} upcoming items",
                    SectionRefresh("queue", mode)),
                scroll)
            .Classes("spotify-page");
    }

    private static WidgetElement PlaylistsPage(
        SpotifyCursorPresentation<SpotifyPlaylistCollectionItem> playlistPresentation,
        SpotifyPlaylistDetailPresentation? playlistDetail,
        string mode)
    {
        if (playlistDetail is not null)
            return PlaylistDetail(playlistDetail.Selection.Playlist,
                playlistDetail.Items, mode);
        var playlists = playlistPresentation.Snapshot;
        if (playlists.Items.Count == 0 && playlists.Status is (
                WidgetPagedResourceStatus.Loading or
                WidgetPagedResourceStatus.NotLoaded))
            return LoadingPage("Loading playlists", mode);
        if (playlists.Error is { } playlistError && playlists.Items.Count == 0)
            return PageFailure("Playlists unavailable", playlistError.Message, mode);
        if (playlists.Items.Count == 0 && !playlists.HasBefore && !playlists.HasAfter &&
            playlists.RequestedFocusId is null)
            return UI.EmptyState("No playlists", "Your Spotify library has no playlists.",
                $"spotify.playlists.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Music).Classes("spotify-page");
        var rows = playlists.Items.Select(item =>
            (Item: item, Row: UI.Tile(
                item.Value.Name, $"{item.Value.ItemCount} items",
                $"spotify.playlist.open.{item.Key.Value}",
                SpotifyCollectionIdentity.FocusId("spotify.playlist.item", mode, item.Key),
                item.Value.OwnerName,
                item.Value.Description, Artwork(item.Value.ArtworkUrl, item.Value.Name),
                $"Open playlist {item.Value.Name}")
                .PersistFocusAs(SpotifyCollectionIdentity.FocusId(
                    "spotify.playlist.persist", "shared", item.Key))))
            .Select(entry =>
                entry.Row.CollectionItem(entry.Item.Key)
                    .Classes("spotify-media-row", "spotify-playlist-row"))
            .ToList<WidgetElement>();
        if (rows.Count == 0)
            rows.Add(SparsePagePlaceholder("playlist", mode));
        var grid = UI.ResponsiveGrid(
                "spotify.playlists.grid", 280, 3, rows.ToArray())
            .Classes("spotify-playlist-grid");
        var scroll = playlistPresentation.Present([grid]);
        var content = new List<WidgetElement>
        {
            UI.SectionHeader("Your playlists", $"spotify.playlists.header.{mode}",
                "LIBRARY", trailing: SectionRefresh("playlists", mode)),
            scroll.Classes("spotify-page-scroll"),
        };
        if (playlists.Error is { } retainedError)
            content.Add(RetainedPageError(retainedError.Message, mode));
        return UI.Stack($"spotify.playlists.page.{mode}", content.ToArray())
            .Classes("spotify-page");
    }

    private static WidgetElement PlaylistDetail(
        SpotifyPlaylistSummary playlist,
        SpotifyCursorPresentation<SpotifyMediaCollectionItem> itemPresentation,
        string mode)
    {
        var items = itemPresentation.Snapshot;
        if (items.Status is (WidgetPagedResourceStatus.Loading or
                WidgetPagedResourceStatus.NotLoaded) && items.Items.Count == 0)
            return LoadingPage($"Loading {playlist.Name}", mode);
        if (items.Error is { } itemError && items.Items.Count == 0)
            return PageFailure("Playlist unavailable", itemError.Message, mode);
        var rows = items.Items.Select((item, index) => PlaylistTrackRow(
                item,
                mode,
                items.Items.Count == 1
                    ? $"spotify.playlist.play.{mode}"
                    : index == 0
                        ? !items.HasBefore
                            ? $"spotify.playlist.play.{mode}"
                            : SpotifyCollectionIdentity.FocusId(
                                "spotify.playlist.track", mode, item.Key)
                        : SpotifyCollectionIdentity.FocusId(
                            "spotify.playlist.track", mode, items.Items[index - 1].Key),
                items.Items.Count == 1
                    ? null
                    : index == items.Items.Count - 1
                        ? SpotifyCollectionIdentity.FocusId(
                            "spotify.playlist.track", mode, item.Key)
                        : SpotifyCollectionIdentity.FocusId(
                            "spotify.playlist.track", mode, items.Items[index + 1].Key)))
            .ToList<WidgetElement>();
        var scroll = itemPresentation.Present(rows);
        var loading = items.Status is WidgetPagedResourceStatus.Loading or
            WidgetPagedResourceStatus.Refreshing or
            WidgetPagedResourceStatus.LoadingAdjacent;
        var play = UI.Button("Play", "spotify.playlist.play",
                $"spotify.playlist.play.{mode}")
            .Icon(WidgetGlyph.Play, $"Play {playlist.Name}")
            .Busy(loading)
            .PersistFocusAs("spotify.playlist.play")
            .Classes("spotify-page-action",
                "spotify-playlist-header-action");
        if (rows.FirstOrDefault() is { } firstRow) play = play.FocusDown(firstRow.Id);
        var content = new List<WidgetElement>
        {
            UI.SectionHeader(playlist.Name, $"spotify.playlist.detail.header.{mode}",
                    "PLAYLIST", playlist.Description,
                    play)
                .Classes("spotify-playlist-header"),
            scroll.Classes("spotify-page-scroll"),
        };
        if (items.Error is { } retainedError)
            content.Add(RetainedPageError(retainedError.Message, mode));
        return UI.Stack($"spotify.playlist.detail.{mode}", content.ToArray())
            .Classes("spotify-page", "spotify-playlist-detail");
    }

    private static ButtonElement SparsePagePlaceholder(string itemKind, string mode) =>
        UI.Button($"No available {itemKind}s on this page", "spotify.page.noop",
                $"spotify.page.sparse.{itemKind}.{mode}")
            .PersistFocusAs($"spotify.page.sparse.{itemKind}")
            .Classes("spotify-media-row", "is-quiet");

    private static WidgetElement RetainedPageError(string error, string mode) =>
        UI.Alert("More items unavailable", error, AlertTone.Warning,
                $"spotify.page.retained-error.{mode}",
                new ComponentAction("Try again", "spotify.page.retry", WidgetGlyph.Refresh))
            .Classes("spotify-page-inline-error");

    private static WidgetElement DevicesPage(
        SpotifyDevicesSummary? devices,
        SpotifyLocalPlaybackSummary? local,
        bool localPlaybackBusy,
        string? localPlaybackFeedback,
        bool loading,
        string? error,
        string mode)
    {
        if (loading && devices is null) return LoadingPage("Loading devices", mode);
        if (error is not null) return PageFailure("Devices unavailable", error, mode);
        var rows = new List<WidgetElement>();
        if (local is not null)
        {
            var localActive = local.State is SpotifyLocalPlaybackState.Active or
                SpotifyLocalPlaybackState.AutoplayBlocked;
            var localStarting = local.State == SpotifyLocalPlaybackState.Starting;
            var localPending = localStarting || localPlaybackBusy;
            var localNeedsReconnect = local.State ==
                SpotifyLocalPlaybackState.ReauthorizationRequired;
            rows.Add(UI.SettingsRow("This overlay",
                    new ComponentAction(localActive ? "Stop" : localPending ? "Starting…" :
                        localNeedsReconnect ? "Reconnect" : "Play here",
                        localActive ? "spotify.local.stop" : localNeedsReconnect
                            ? "spotify.connect.features" : "spotify.local.start",
                        localActive ? WidgetGlyph.Pause : localNeedsReconnect
                            ? WidgetGlyph.Refresh : WidgetGlyph.Play),
                    $"spotify.local.{mode}",
                    local.DisplayMessage ?? "Web Playback SDK audio stays in the trusted host.",
                    local.DeviceName,
                    localPlaybackBusy ? "Starting" : LocalStateLabel(local.State),
                    localPlaybackBusy ? StatusTone.Info : LocalStateTone(local.State),
                    isDisabled: local.State is SpotifyLocalPlaybackState.PremiumRequired or
                        SpotifyLocalPlaybackState.Unavailable || localPending,
                    isBusy: localPending,
                    glyph: WidgetGlyph.Music)
                .Classes("spotify-device-row"));
        }
        if (localPlaybackFeedback is not null)
            rows.Add(UI.Alert(
                    "Local playback unavailable",
                    localPlaybackFeedback,
                    AlertTone.Warning,
                    $"spotify.local.feedback.{mode}")
                .Classes("spotify-local-feedback"));
        if (devices is not null)
        {
            rows.AddRange(devices.Devices.Select((device, index) => (device, index))
                .Where(entry => !entry.device.IsLocalHost)
                .Select(entry => UI.ChoiceRow(
                        entry.device.Name, $"spotify.device.select.{entry.index}",
                        $"spotify.device.{mode}.{entry.index}", entry.device.IsActive,
                        entry.device.IsRestricted, false, WidgetGlyph.Connection,
                        $"{entry.device.Name}, {entry.device.Type}" )
                    .PersistFocusAs($"spotify.device.remote.{entry.index}")
                    .Classes("spotify-device-row")));
        }
        if (rows.Count == 0)
            return UI.EmptyState("No Spotify devices", "Open Spotify on another device or play here.",
                $"spotify.devices.empty.{mode}",
                new ComponentAction("Refresh", "spotify.page.retry", WidgetGlyph.Refresh),
                WidgetGlyph.Connection).Classes("spotify-page");
        return UI.Stack($"spotify.devices.page.{mode}",
                UI.SectionHeader("Playback devices", $"spotify.devices.header.{mode}",
                    "DEVICES", trailing: SectionRefresh("devices", mode)),
                UI.VerticalScroll($"spotify.devices.scroll.{mode}", rows.ToArray())
                    .Classes("spotify-page-scroll"))
            .Classes("spotify-page");
    }

    private static ButtonElement SectionRefresh(string section, string mode) =>
        UI.Button("Refresh", "spotify.refresh", $"spotify.{section}.refresh.{mode}")
            .Icon(WidgetGlyph.Refresh, $"Refresh Spotify {section}")
            .PersistFocusAs($"spotify.{section}.refresh")
            .Classes("spotify-page-action", "is-quiet", "spotify-section-refresh");

    private static WidgetElement LoadingPage(string label, string mode) =>
        UI.Stack($"spotify.page.loading.{mode}",
                UI.LoadingIndicator($"spotify.page.loading.indicator.{mode}", label),
                UI.Text(label, $"spotify.page.loading.label.{mode}", label),
                UI.Button("Refresh", "spotify.page.retry",
                        $"spotify.page.loading.{mode}.action")
                    .Icon(WidgetGlyph.Refresh, $"Refresh {label}")
                    .Classes("spotify-page-action", "is-quiet"))
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
        SpotifyMediaItemSummary item,
        string action,
        string id,
        string focusPersistenceId) => UI.Tile(
            item.Title, FormatTime(item.DurationMilliseconds),
            action, id, item.Subtitle, null, Artwork(item.ArtworkUrl, item.Title),
            $"Play {item.Title} by {item.Subtitle}")
        .Disabled(!item.IsPlayable)
        .PersistFocusAs(focusPersistenceId)
        .Classes("spotify-media-row");

    private static WidgetElement QueueRow(
        SpotifyMediaCollectionItem item,
        string mode,
        string? up,
        string? down)
    {
        var row = MediaRow(item.Value, $"spotify.queue.play.{item.Key.Value}",
                SpotifyCollectionIdentity.FocusId("spotify.queue.item", mode, item.Key),
                SpotifyCollectionIdentity.FocusId(
                    "spotify.queue.persist", "shared", item.Key));
        if (up is not null) row = row.FocusUp(up);
        if (down is not null) row = row.FocusDown(down);
        return row.CollectionItem(item.Key);
    }

    private static WidgetElement PlaylistTrackRow(
        SpotifyMediaCollectionItem item,
        string mode,
        string up,
        string? down)
    {
        var row = MediaRow(item.Value, $"spotify.playlist.track.{item.Key.Value}",
            SpotifyCollectionIdentity.FocusId("spotify.playlist.track", mode, item.Key),
            SpotifyCollectionIdentity.FocusId(
                "spotify.playlist.track.persist", "shared", item.Key));
        row = row.FocusUp(up);
        if (down is not null) row = row.FocusDown(down);
        return row.CollectionItem(item.Key);
    }

    private static TileArtwork Artwork(string? url, string label) =>
        string.IsNullOrWhiteSpace(url)
            ? TileArtwork.FromGlyph(WidgetGlyph.Music, label)
            : TileArtwork.FromHttps(url, label);

    private static string LocalStateLabel(SpotifyLocalPlaybackState state) => state switch
    {
        SpotifyLocalPlaybackState.Active => "Playing here",
        SpotifyLocalPlaybackState.Ready => "Ready",
        SpotifyLocalPlaybackState.Starting => "Starting",
        SpotifyLocalPlaybackState.AutoplayBlocked => "Playback blocked",
        SpotifyLocalPlaybackState.PremiumRequired => "Premium required",
        SpotifyLocalPlaybackState.ReauthorizationRequired => "Reconnect required",
        SpotifyLocalPlaybackState.Unavailable => "Unavailable",
        SpotifyLocalPlaybackState.Error => "Error",
        _ => "Stopped",
    };

    private static StatusTone LocalStateTone(SpotifyLocalPlaybackState state) => state switch
    {
        SpotifyLocalPlaybackState.Active or SpotifyLocalPlaybackState.Ready =>
            StatusTone.Success,
        SpotifyLocalPlaybackState.Starting => StatusTone.Info,
        SpotifyLocalPlaybackState.AutoplayBlocked => StatusTone.Warning,
        SpotifyLocalPlaybackState.PremiumRequired or
            SpotifyLocalPlaybackState.ReauthorizationRequired => StatusTone.Warning,
        SpotifyLocalPlaybackState.Error => StatusTone.Danger,
        _ => StatusTone.Neutral,
    };

    private static string DestinationToken(SpotifyDestination destination) =>
        destination.ToString().ToLowerInvariant();

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

    private static bool RepeatUnavailable(SpotifyPlaybackSummary playback) =>
        playback.RepeatState switch
        {
            SpotifyRepeatState.Off => playback.DisallowedActions.TogglingRepeatContext,
            _ => false,
        };

    internal static string FormatTime(long milliseconds)
    {
        var totalSeconds = Math.Max(0, milliseconds / 1_000);
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    private static WidgetElement SearchPage(SpotifySearchPresentation? search)
    {
        var query = search?.Query ?? string.Empty;
        var kind = search?.Kind ?? SpotifySearchKind.Track;
        var entry = UI.TextEntry(query, "Search Spotify", "spotify.search.query",
                "spotify.search.query", ProtocolConstants.MaximumTextEntryLength)
            .Classes("spotify-search-entry");
        var kinds = Enum.GetValues<SpotifySearchKind>().Select(value => new SelectOption(
            "spotify.search.type." + value, value + "s", "spotify.search.type." + value,
            IsSelected: value == kind)).ToArray();
        var selector = UI.Select("Results", kinds, "spotify.search.type", "Search result type")
            .FocusUp("spotify.search.query").Classes("spotify-search-type");
        var controls = UI.Row("spotify.search.controls", selector,
            UI.Button("Clear", "spotify.search.clear", "spotify.search.clear")
                .Disabled(query.Length == 0).Classes("spotify-page-action", "spotify-search-clear"))
            .Classes("spotify-search-controls");
        var children = new List<WidgetElement> { entry, controls };
        var results = search?.Results.Snapshot;
        if (query.Length == 0)
            children.Add(UI.Text("Find tracks, albums, artists and playlists.", "spotify.search.prompt")
                .Classes("spotify-search-message"));
        else if (search is not null && results is not null)
        {
            var rows = results.Items.Select(item => (WidgetElement)UI.Tile(
                    item.Value.Title, item.Value.IsPlayable ? "Play " + item.Value.Kind.ToString().ToLowerInvariant() : "Unavailable",
                    "spotify.search.play." + item.Key.Value, "spotify.search.item." + item.Key.Value,
                    item.Value.Subtitle, artwork: Artwork(item.Value.ArtworkUrl, item.Value.Title),
                    accessibilityLabel: "Play " + item.Value.Kind.ToString().ToLowerInvariant() + " " + item.Value.Title)
                .Disabled(!item.Value.IsPlayable).CollectionItem(item.Key).Classes("spotify-media-row"))
                .ToList();
            if (rows.Count == 0)
                rows.Add(UI.Text(results.Error is not null ? "Search couldn't finish. Try again." :
                    results.Status == WidgetPagedResourceStatus.Ready ? "No results. Try another search or result type." : "Searching Spotify…",
                    "spotify.search.message").Classes("spotify-search-message"));
            children.Add(search.Results.Present(rows).Classes("spotify-page-scroll"));
            if (results.Error is { } error)
                children.Add(UI.Alert("Search unavailable", error.Message, AlertTone.Warning, "spotify.search.error",
                    new ComponentAction("Try again", "spotify.page.retry", WidgetGlyph.Refresh)));
        }
        return UI.Stack("spotify.search.page", children.ToArray()).Classes("spotify-page", "spotify-search-page");
    }

}
