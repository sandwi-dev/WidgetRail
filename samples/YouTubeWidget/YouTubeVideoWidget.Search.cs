using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

public sealed partial class YouTubeVideoWidget
{
    internal const int SearchPageSize = 12;
    internal const int MaximumRetainedSearchItems = 48;
    internal const string SearchScrollId = "youtube.search.results";
    private const string SearchCommitActionId = "youtube.search.query.commit";
    private const string SearchSubmitActionId = "youtube.search.submit";
    private const string SearchOpenActionId = "youtube.search.open";
    private const string SearchRetryActionId = "youtube.search.retry";
    private const string SetupKeyActionId = "youtube.setup.key.commit";
    private const string SetupOpenConsoleActionId = "youtube.setup.console";
    private const string SetupDeleteActionId = "youtube.setup.delete";
    private const string SetupRouteActionId = "youtube.setup.open";
    private const string LinkRouteActionId = "youtube.link.open";
    private const string SearchRouteActionId = "youtube.search.open-route";
    private const string BackActionId = "youtube.back";
    private const string ConfigurationKey = "youtube.configuration";
    private const string SetupCommandKey = "youtube.configure";
    private readonly IYouTubeApplicationService _application;
    private readonly WidgetCursorResource<YouTubeSearchItem> _searchResults;
    private readonly WidgetOptimisticCommand<
        YouTubeWidgetState,
        YouTubeSetupRequest,
        YouTubeSetupRequest,
        YouTubeSetupOperation> _setupCommand;
    private readonly WidgetOutOfBandCommand<
        YouTubeWidgetState,
        YouTubePlaybackRequest,
        EmbeddedMediaPlaybackEvent,
        string> _playbackCommand;

    public YouTubeVideoWidget() : this(
        new UnavailableYouTubeApplicationService(), TimeProvider.System) { }

    public YouTubeVideoWidget(IYouTubeApplicationService application) :
        this(application, TimeProvider.System) { }

    internal YouTubeVideoWidget(
        IYouTubeApplicationService application,
        TimeProvider timeProvider)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _model = CreateModel(YouTubeWidgetState.Initial);
        _playbackCommand = CreateOutOfBandCommand(_model,
            new WidgetOutOfBandCommandOptions<
                YouTubeWidgetState,
                YouTubePlaybackRequest,
                EmbeddedMediaPlaybackEvent,
                string>
            {
                Apply = ApplyPlaybackRequest,
                ObservationSequence = playbackEvent => playbackEvent.Sequence,
                CorrelationSequence = playbackEvent => playbackEvent.CommandSequence,
                MatchesAuthority = static (state, playbackEvent) =>
                    state.Playback.VideoId is { } videoId &&
                    string.Equals(videoId, playbackEvent.MediaKey,
                        StringComparison.Ordinal),
                MatchesProjectionAuthority = static (mediaKey, playbackEvent) =>
                    string.Equals(mediaKey, playbackEvent.MediaKey,
                        StringComparison.Ordinal),
                Reconcile = static (state, playbackEvent, confirmsProjection) =>
                    state.WithPlayback(playback => playback.WithPlaybackEvent(
                        playbackEvent, confirmsProjection)),
            });
        _searchResults = CreateCursorResource<YouTubeSearchItem>("youtube.search", new()
        {
            PageSize = SearchPageSize,
            MaximumRetainedItems = MaximumRetainedSearchItems,
            PaginationThreshold = 2,
            LoadPage = LoadSearchPageAsync,
            MapError = MapSearchError,
            Viewports =
            [
                new(SearchScrollId,
                    item => new WidgetCollectionItemKey("youtube-video-" + item.VideoId),
                    item => ResultFocusId(item.VideoId),
                    SearchRetryActionId)
                {
                    EstimatedItemExtent = 92,
                },
            ],
        });
        // Both key mutations share one latest-wins key, so a delete supersedes an
        // in-flight configure exactly as before. Cancellation removes only this
        // command's Busy/Error projection from the current model.
        _setupCommand = CreateOptimisticCommand(SetupCommandKey, _model,
            new WidgetOptimisticCommandOptions<
                YouTubeWidgetState,
                YouTubeSetupRequest,
                YouTubeSetupRequest,
                YouTubeSetupOperation>
            {
                Policy = WidgetCommandPolicy.Latest,
                Apply = (state, request) => new(state.WithSetupInFlight(), request),
                Execute = ExecuteSetupAsync,
                Reconcile = (state, _, result) => result == YouTubeSetupOperation.Configure
                    ? state.WithConfiguredKey()
                    : state.WithDeletedKey(),
                Rollback = (current, baseline, _) =>
                    current.WithSetupProjectionRolledBack(baseline.Setup),
                Fail = (current, _, _, error) => current.WithSetupFailure(error),
                MapError = MapSetupError,
            });
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        var state = _model.Value;
        if (state.Playback.PendingCommand is { } pending &&
            state.Playback.PendingControl != PendingMediaControl.None)
            SchedulePendingFeedback(pending.Sequence);
        if (state.Setup.ConfigurationKnown) return ValueTask.CompletedTask;
        Operations.RunSingleFlight(ConfigurationKey, async context =>
        {
            try
            {
                var summary = await _application.GetConfigurationAsync(context.CancellationToken)
                    .ConfigureAwait(false);
                if (!context.IsCurrent) return;
                _model.Update(current => current.WithConfigurationSummary(summary.IsConfigured));
            }
            catch (Exception exception)
            {
                if (!context.IsCurrent) return;
                _model.Update(current => current.WithConfigurationFailure(SafeMessage(exception)));
            }
        });
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        // The feedback timer runs under the Active lifetime, so the runtime
        // cancels it here; only its committed projection needs retiring.
        _model.Update(state => state.WithPlayback(
            playback => playback.WithTransientStateCleared()));
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        _model.Update(state => state.WithPlayback(
            playback => playback.WithTransientStateCleared()));
        try
        {
            await _application.DisposeAsync().AsTask().WaitAsync(shutdownToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested) { }
    }

    // These are flat lateral routes with established public scope IDs, not a
    // nested Back stack. Keeping route state in the model preserves exact
    // action/scope authority; WidgetNavigator would generate different scopes.
    private WidgetView RenderApplication(YouTubeWidgetState state)
    {
        if (!state.Setup.ConfigurationKnown)
            return ApplicationView(
                state,
                UI.Stack("youtube.configuration.loading",
                    UI.LoadingIndicator("youtube.configuration.indicator", "Checking YouTube setup"),
                    UI.Text("Checking YouTube setup…", "youtube.configuration.text"))
                    .Classes("youtube-page", "youtube-centered"),
                null);
        return state.Route switch
        {
            YouTubeRoute.Setup => RenderSetup(state),
            YouTubeRoute.Search => RenderSearch(state),
            YouTubeRoute.Link => RenderLinkPlayer(state),
            YouTubeRoute.PlayerSettings => RenderPlayerSettings(state),
            YouTubeRoute.PlaybackRatePicker => RenderPlaybackRatePicker(state),
            YouTubeRoute.Captions => RenderCaptions(state),
            _ => RenderPlayer(state),
        };
    }

    private WidgetView RenderSetup(YouTubeWidgetState state)
    {
        var setup = state.Setup;
        var configured = setup.Configured;
        var busy = setup.Busy;
        var keyEntry = UI.SensitiveTextEntry(
                configured ? "Enter a replacement Google API key" : "Enter your Google API key",
                SetupKeyActionId,
                "youtube.setup.key",
                96)
            .Disabled(busy)
            .FocusUp("youtube.setup.console")
            .FocusDown(configured ? "youtube.setup.delete" : LinkRouteActionId)
            .Classes("youtube-entry");
        var routeActionId = configured ? SearchRouteActionId : LinkRouteActionId;
        var routeActionLabel = configured ? "Back to search" : "Play a link";
        var children = new List<WidgetElement>
        {
            UI.Row("youtube.setup.appbar",
                    UI.Stack("youtube.setup.heading-copy",
                        UI.Text("YOUTUBE", "youtube.setup.eyebrow").Classes("youtube-eyebrow"),
                        UI.Text("Search setup", "youtube.setup.title").Classes("youtube-title"))
                        .Classes("youtube-appbar-copy"),
                    UI.Button(routeActionLabel, routeActionId, routeActionId)
                        .Disabled(busy).Classes("youtube-route-button"))
                .RememberChildFocus(routeActionId)
                .Classes("youtube-appbar"),
            UI.Stack("youtube.setup.intro-card",
                    UI.Text(configured ? "Update your search access" : "Connect public-video search",
                        "youtube.setup.card-title").Classes("youtube-card-title"),
                    UI.Text("A restricted YouTube Data API key enables search only. Link playback works without it.",
                        "youtube.setup.disclosure").Classes("youtube-copy"))
                .Classes("youtube-card", "youtube-setup-intro"),
            UI.Stack("youtube.setup.console-card",
                    UI.Text("1  CREATE A RESTRICTED KEY", "youtube.setup.console-label")
                        .Classes("youtube-section-label"),
                    UI.Text("In Google Cloud Console, enable YouTube Data API v3 and restrict the key to that API.",
                        "youtube.setup.instructions").Classes("youtube-copy"),
                    UI.Button("Open Google Cloud Console", SetupOpenConsoleActionId, "youtube.setup.console")
                        .Disabled(busy).FocusDown("youtube.setup.key").Classes("youtube-secondary"))
                .Classes("youtube-card", "youtube-setup-step"),
            UI.Stack("youtube.setup.key-card",
                    UI.Text("2  SAVE IT SECURELY", "youtube.setup.key-label")
                        .Classes("youtube-section-label"),
                    UI.Text("The key stays protected in Windows Credential Manager and is never displayed again.",
                        "youtube.setup.key-help").Classes("youtube-copy"),
                    keyEntry)
                .Classes("youtube-card", "youtube-setup-step"),
        };
        if (busy)
            children.Add(UI.Stack("youtube.setup.busy-card",
                    UI.LoadingIndicator("youtube.setup.busy", "Testing and saving API key"),
                    UI.Text("Testing and saving your key…", "youtube.setup.busy-text")
                        .Classes("youtube-copy"))
                .Classes("youtube-state-card", "is-loading"));
        if (setup.Error is { } error)
            children.Add(UI.Stack("youtube.setup.error-card",
                    UI.Text("Setup needs attention", "youtube.setup.error-title")
                        .Classes("youtube-card-title"),
                    UI.Text(error, "youtube.setup.error").Classes("youtube-status", "is-error"))
                .Classes("youtube-state-card", "is-error"));
        if (configured)
            children.Add(UI.Stack("youtube.setup.manage-card",
                    UI.Text("SAVED KEY", "youtube.setup.manage-label").Classes("youtube-section-label"),
                    UI.Text("Remove the saved key to disconnect search. Link playback remains available.",
                        "youtube.setup.manage-help").Classes("youtube-copy"),
                    UI.Button("Delete saved API key", SetupDeleteActionId, "youtube.setup.delete")
                        .Disabled(busy).FocusUp("youtube.setup.key").FocusDown(SearchRouteActionId)
                        .Classes("youtube-danger"))
                .Classes("youtube-card", "youtube-setup-manage"));
        return ApplicationView(state,
            UI.VerticalScroll("youtube.setup.scroll", children.ToArray())
                .Classes("youtube-page", "youtube-setup"), "youtube.setup.console");
    }

    private WidgetView RenderSearch(YouTubeWidgetState state)
    {
        var search = state.Search;
        var draft = search.QueryDraft;
        var snapshot = _searchResults.Snapshot;
        var query = UI.TextEntry(draft, "Search public YouTube videos", SearchCommitActionId,
                "youtube.search.query", 96)
            .FocusRight(SearchSubmitActionId).Classes("youtube-entry", "youtube-search-entry");
        var header = UI.Row("youtube.search.header",
                UI.Stack("youtube.search.heading-copy",
                    UI.Text("YOUTUBE", "youtube.search.eyebrow").Classes("youtube-eyebrow"),
                    UI.Text("Discover", "youtube.search.title").Classes("youtube-title"))
                    .Classes("youtube-appbar-copy"),
                UI.Row("youtube.search.routes",
                    UI.Button("Play a link", LinkRouteActionId, LinkRouteActionId)
                        .Classes("youtube-route-button"),
                    UI.Button("Setup", SetupRouteActionId, "youtube.search.setup")
                        .Classes("youtube-route-button"))
                    .RememberChildFocus(LinkRouteActionId)
                    .Classes("youtube-route-actions"))
            .Classes("youtube-appbar");
        var searchTask = UI.Stack("youtube.search.task-card",
                UI.Text("SEARCH PUBLIC VIDEOS", "youtube.search.task-label")
                    .Classes("youtube-section-label"),
                UI.Text("Find a video by title, channel, or topic.", "youtube.search.task-help")
                    .Classes("youtube-section-copy"),
                UI.Row("youtube.search.controls",
                        query,
                        UI.Button("Search", SearchSubmitActionId, SearchSubmitActionId)
                            .Disabled(string.IsNullOrWhiteSpace(draft) || _searchResults.IsBusy)
                            .FocusLeft("youtube.search.query").Classes("youtube-primary", "youtube-search-submit"))
                    .RememberChildFocus("youtube.search.query")
                    .Classes("youtube-search-controls"))
            .Classes("youtube-card", "youtube-search-task");
        WidgetElement content;
        if (snapshot.Status == WidgetPagedResourceStatus.NotLoaded)
            content = UI.Stack("youtube.search.empty-start",
                    UI.Text("Ready to discover", "youtube.search.empty-title").Classes("youtube-card-title"),
                    UI.Text("Enter a query above. Results load only when you choose Search.",
                        "youtube.search.help").Classes("youtube-section-copy"))
                .Classes("youtube-state-card", "is-empty");
        else if (snapshot.Status == WidgetPagedResourceStatus.Loading && snapshot.Items.Count == 0)
            content = UI.Stack("youtube.search.loading",
                    UI.LoadingIndicator("youtube.search.loading.indicator", "Searching YouTube"),
                    UI.Text("Searching YouTube", "youtube.search.loading.title").Classes("youtube-card-title"),
                    UI.Text($"Looking for {search.ActiveQuery}…", "youtube.search.loading.text")
                        .Classes("youtube-section-copy"))
                .Classes("youtube-state-card", "is-loading");
        else if (snapshot.Status == WidgetPagedResourceStatus.Error && snapshot.Items.Count == 0)
            content = UI.Stack("youtube.search.error",
                    UI.Text("Search needs attention", "youtube.search.error.title")
                        .Classes("youtube-card-title"),
                    UI.Text(snapshot.Error?.Message ?? "YouTube search failed.", "youtube.search.error.text")
                        .Classes("youtube-status", "is-error"),
                    UI.Button("Try again", SearchRetryActionId, SearchRetryActionId).Classes("youtube-primary"))
                .Classes("youtube-state-card", "is-error");
        else if (snapshot.Items.Count == 0)
            content = UI.Stack("youtube.search.no-results",
                    UI.Text("No matches", "youtube.search.no-results.title").Classes("youtube-card-title"),
                    UI.Text("Try a broader title, channel, or topic.", "youtube.search.no-results.text")
                        .Classes("youtube-section-copy"))
                .Classes("youtube-state-card", "is-empty");
        else
        {
            var rows = snapshot.Items.Select(item => _searchResults.PresentItem(item,
                UI.Row("youtube.result.row." + item.VideoId,
                    UI.Image(item.ThumbnailUrl, "youtube.result.image." + item.VideoId,
                        $"Thumbnail for {item.Title}", ImageFit.Cover).Classes("youtube-result-image"),
                    UI.Stack("youtube.result.copy." + item.VideoId,
                        UI.Text(item.Title, "youtube.result.title." + item.VideoId).Classes("youtube-result-title"),
                        UI.Text($"{item.Channel} · {item.Duration}", "youtube.result.meta." + item.VideoId)
                            .Classes("youtube-result-meta"),
                        UI.Button("Play", SearchOpenActionId, ResultFocusId(item.VideoId))
                            .Classes("youtube-result-action"))
                    .Classes("youtube-result-copy"))
                .Classes("youtube-result-row"))).ToArray();
            content = _searchResults.Present(UI.VerticalScroll(SearchScrollId, rows))
                .Classes("youtube-results");
        }
        var children = new List<WidgetElement> { header, searchTask };
        if (NowPlayingRow(state.Playback) is { } nowPlaying) children.Add(nowPlaying);
        children.Add(content);
        var root = UI.Stack("youtube.search.root", children.ToArray())
            .InputScope("youtube.search.root").Classes("youtube-root", "youtube-search-root");
        return new WidgetView(root, SearchInitialFocus(search, snapshot),
            ActiveInputScopeId: "youtube.search.root", Surface: new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Standard,
            WidthMode = WidgetSurfaceAxisMode.Preferred,
            HeightMode = snapshot.Items.Count == 0
                ? WidgetSurfaceAxisMode.Content
                : WidgetSurfaceAxisMode.Preferred,
            PreferredWidth = 820,
            PreferredHeight = 640,
            MinimumWidth = 420,
            MinimumHeight = 420,
        })
        { EmbeddedMedia = RetainedHiddenMediaSurface(state.Playback) };
    }

    private static WidgetElement? NowPlayingRow(YouTubePlaybackState playback)
    {
        return playback.VideoId is null ? null :
            UI.Button(
                    $"Return to player  ·  {(playback.PlaybackSemantic == EmbeddedMediaPlaybackState.Playing ? "Playing" : "Paused or ready")}",
                    "youtube.player.return", "youtube.player.return")
                .Classes("youtube-now-playing");
    }

    private WidgetView RenderLinkPlayer(YouTubeWidgetState state)
    {
        var view = RenderPlayer(state);
        return view with { InitialFocusId = "youtube.link" };
    }

    private WidgetView RenderPlayerSettings(YouTubeWidgetState state)
    {
        var playback = state.Playback;
        var settingsUnavailable = !state.CanDispatchPlayerSetting(IsActive);
        var busyControl = playback.BusyControl;
        var rate = FormatPlaybackRate(playback.PlaybackRate);
        var body = new List<WidgetElement>
        {
            UI.SettingsRow(
                    "Playback speed",
                    new ComponentAction("Choose speed", PlaybackRateActionId,
                        WidgetGlyph.Settings),
                    "youtube.player.settings.rate-row",
                    description: "Available speeds depend on the current video.",
                    value: rate,
                    isDisabled: settingsUnavailable,
                    isBusy: busyControl == PendingMediaControl.PlaybackRate),
                UI.Switch("Mute", playback.Muted, MutedActionId,
                        "youtube.player.settings.muted", settingsUnavailable)
                    .Busy(busyControl == PendingMediaControl.Muted),
                UI.Switch("Loop current video", playback.Loop, LoopActionId,
                        "youtube.player.settings.loop", settingsUnavailable)
                    .Busy(busyControl == PendingMediaControl.Loop),
                UI.ValueRow("Quality", "Auto · managed by YouTube",
                    "youtube.player.settings.quality",
                    "YouTube adapts stream quality to playback conditions."),
                UI.SettingsRow(
                    "Captions",
                    new ComponentAction("Caption options", CaptionsActionId,
                        WidgetGlyph.Settings),
                    "youtube.player.settings.captions-row",
                    description: "Open the supported caption path for this video.",
                    isDisabled: playback.VideoId is null),
                UI.SettingsRow(
                    "Open in YouTube",
                    new ComponentAction("Open video", OpenInYouTubeActionId,
                        WidgetGlyph.Connection),
                    "youtube.player.settings.open-row",
                    description: "Use YouTube for provider-owned controls and preferences.",
                    isDisabled: playback.VideoId is null),
        };
        if (playback.PreferenceError is { } error)
            body.Add(UI.Text(error, "youtube.player.settings.error")
                .Classes("youtube-status", "is-error"));
        var content = UI.ScopedDialog(
            "Player settings",
            "youtube.player.settings",
            "youtube.player.settings",
            PlayerSettingsBackActionId,
            UI.VerticalScroll("youtube.player.settings.scroll", body.ToArray())
                .Classes("youtube-player-settings-scroll"));
        return PlayerModalView(state, content, "youtube.player.settings.rate-row.action",
            "youtube.player.settings", WidgetSurfaceAxisMode.Preferred);
    }

    private WidgetView RenderPlaybackRatePicker(YouTubeWidgetState state)
    {
        var playback = state.Playback;
        var settingsUnavailable = !state.CanDispatchPlayerSetting(IsActive);
        var options = CanonicalPlaybackRates.Select(rate =>
            new PickerOption(
                PlaybackRateOptionId(rate),
                FormatPlaybackRate(rate),
                PlaybackRateOptionId(rate),
                IsSelected: Math.Abs(playback.PlaybackRate - rate) < 0.001,
                IsDisabled: settingsUnavailable,
                IsBusy: playback.BusyControl == PendingMediaControl.PlaybackRate &&
                        playback.PendingCommand?.PlaybackRate == rate)).ToArray();
        var picker = UI.Picker(
            "Playback speed",
            "youtube.player.settings.rate-picker",
            "youtube.player.settings.rate-picker",
            PlaybackRateBackActionId,
            options,
            "YouTube may make only some speeds available for the current video.");
        var initial = options.FirstOrDefault(option => option.IsSelected)?.Id ??
                      PlaybackRateOptionId(1);
        return PlayerModalView(state, picker, initial,
            "youtube.player.settings.rate-picker");
    }

    private WidgetView RenderCaptions(YouTubeWidgetState state)
    {
        var sheet = UI.ActionSheet(
            "Captions",
            "youtube.player.captions.sheet",
            "youtube.player.captions.sheet",
            CaptionsBackActionId,
            [
                new ActionSheetItem(
                    "youtube.player.captions.open-youtube",
                    "Open caption options in YouTube",
                    OpenInYouTubeActionId,
                    WidgetGlyph.Connection),
            ],
            "YouTube's documented caption preferences are player-construction options. " +
            "Changing them here would recreate the player and interrupt this session.");
        return PlayerModalView(state, sheet,
            "youtube.player.captions.open-youtube", "youtube.player.captions.sheet");
    }

    private static WidgetView PlayerModalView(
        YouTubeWidgetState state,
        StackElement content,
        string initialFocus,
        string inputScope,
        WidgetSurfaceAxisMode heightMode = WidgetSurfaceAxisMode.Content) => new(
            content.AddClasses("youtube-player-settings"),
            initialFocus,
            ActiveInputScopeId: inputScope,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                WidthMode = WidgetSurfaceAxisMode.Preferred,
                HeightMode = heightMode,
                PreferredWidth = 620,
                PreferredHeight = 620,
                MinimumWidth = 420,
                MinimumHeight = 420,
            })
        { EmbeddedMedia = RetainedHiddenMediaSurface(state.Playback) };

    private WidgetView ApplicationView(
        YouTubeWidgetState state,
        WidgetElement content,
        string? initialFocus)
    {
        var root = UI.Stack("youtube.application.root", content)
            .InputScope("youtube.application.root").Classes("youtube-root");
        return new WidgetView(root, initialFocus,
            ActiveInputScopeId: "youtube.application.root", Surface: new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Standard,
            WidthMode = WidgetSurfaceAxisMode.Preferred,
            HeightMode = WidgetSurfaceAxisMode.Content,
            PreferredWidth = 760,
            PreferredHeight = 640,
            MinimumWidth = 420,
            MinimumHeight = 420,
        })
        { EmbeddedMedia = RetainedHiddenMediaSurface(state.Playback) };
    }

    private static EmbeddedMediaSurface? RetainedHiddenMediaSurface(
        YouTubePlaybackState playback)
    {
        return playback.HasPlaybackObservation && playback.VideoId is { } videoId
            ? CreateMediaSurface(videoId, playback.PendingCommand, retainSessionWhenHidden: true)
            : null;
    }

    private bool TryHandleApplicationAction(WidgetActionEvent action)
    {
        if (_searchResults.TryHandlePagination(action, out _)) return true;
        switch (action.ActionId)
        {
            case SetupOpenConsoleActionId:
                Operations.RunSingleFlight("youtube.open-console",
                    context => _application.OpenGoogleCloudConsoleAsync(context.CancellationToken));
                return true;
            case SetupKeyActionId when action.CommittedText is { } secret:
                _setupCommand.Run(new(YouTubeSetupOperation.Configure, secret));
                return true;
            case SetupDeleteActionId:
                _setupCommand.Run(new(YouTubeSetupOperation.Delete));
                return true;
            case SetupRouteActionId:
                _model.Update(state => state.WithSetupRoute());
                return true;
            case SearchRouteActionId:
                _model.Update(state => state.WithConfiguredRoute());
                return true;
            case LinkRouteActionId:
                _model.Update(state => state.WithRoute(YouTubeRoute.Link));
                return true;
            case CaptionsActionId when _model.Value.Route is YouTubeRoute.Player or
                YouTubeRoute.PlayerSettings:
                _model.Update(state => state.WithRoute(
                    state.Route == YouTubeRoute.Player
                        ? YouTubeRoute.PlayerSettings
                        : YouTubeRoute.Captions));
                return true;
            case PlaybackRateActionId when _model.Value.Route == YouTubeRoute.PlayerSettings:
                _model.Update(state => state.WithRoute(YouTubeRoute.PlaybackRatePicker));
                return true;
            case PlayerSettingsBackActionId:
                _model.Update(state => state.WithRoute(YouTubeRoute.Player));
                return true;
            case PlaybackRateBackActionId:
                _model.Update(state => state.WithRoute(YouTubeRoute.PlayerSettings));
                return true;
            case CaptionsBackActionId:
                _model.Update(state => state.WithRoute(YouTubeRoute.PlayerSettings));
                return true;
            case OpenInYouTubeActionId when _model.Value.Playback.VideoId is { } videoId:
                Operations.RunSingleFlight("youtube.open-video",
                    context => _application.OpenVideoInYouTubeAsync(videoId,
                        context.CancellationToken));
                return true;
            case "youtube.player.return":
                _model.Update(state => state.WithRoute(YouTubeRoute.Player));
                return true;
            case BackActionId:
                _model.Update(state => state.WithConfiguredRoute());
                return true;
            case SearchCommitActionId when action.CommittedText is { } query:
                _model.Update(state => state.WithQueryDraft(query));
                return true;
            case SearchSubmitActionId:
                StartSearch();
                return true;
            case SearchRetryActionId:
                _searchResults.Retry();
                return true;
            case SearchOpenActionId:
                return OpenSearchResult(action.SourceElementId);
            default:
                return false;
        }
    }

    private async ValueTask<YouTubeSetupOperation> ExecuteSetupAsync(
        YouTubeSetupRequest request,
        WidgetOperationContext context)
    {
        var cancellationToken = context.CancellationToken;
        if (request.Operation == YouTubeSetupOperation.Configure)
        {
            await _application.ConfigureApiKeyAsync(request.Secret, cancellationToken)
                .ConfigureAwait(false);
            return YouTubeSetupOperation.Configure;
        }
        await _application.DeleteApiKeyAsync(cancellationToken).ConfigureAwait(false);
        // The cursor resource is not part of the model, so a superseded delete
        // must not clear it. Cancellation here rolls the attempt back untouched.
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.IsCurrent) return YouTubeSetupOperation.Delete;
        _searchResults.Reset(invalidate: false);
        return YouTubeSetupOperation.Delete;
    }

    private void StartSearch()
    {
        var query = _model.Value.Search.QueryDraft.Trim();
        if (query.Length == 0) return;
        // Each owner publishes its own change: the model commits the active query,
        // and the cursor resource publishes when its load starts. Adding an
        // Invalidate() here would add a third redundant invalidation request;
        // the runtime may still coalesce those requests into fewer renders.
        _searchResults.Reset(invalidate: false);
        _model.Update(state => state.WithActiveQuery(query));
        _searchResults.EnsureLoaded();
    }

    private bool OpenSearchResult(string sourceId)
    {
        const string prefix = "youtube.result.";
        if (!sourceId.StartsWith(prefix, StringComparison.Ordinal)) return true;
        var id = sourceId[prefix.Length..];
        var item = _searchResults.Snapshot.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.VideoId, id, StringComparison.Ordinal));
        if (item is null) return true;
        _searchResults.SelectAnchor(new("youtube-video-" + item.VideoId), invalidate: false);
        RunPlaybackLatest(new(
            YouTubePlaybackIntent.SelectResult,
            ReturnFocusId: sourceId,
            VideoId: item.VideoId));
        return true;
    }

    private async ValueTask<WidgetCursorPage<YouTubeSearchItem>> LoadSearchPageAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _model.Value.Search.ActiveQuery;
        var page = await _application.SearchAsync(query, cursor?.Value, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return new WidgetCursorPage<YouTubeSearchItem>(
            page.Items,
            null,
            page.NextPageToken is null ? null : new WidgetCollectionCursor(page.NextPageToken))
        {
            FirstItemIndex = direction == WidgetCursorDirection.After
                ? _searchResults.Snapshot.Items.Count : 0,
            TotalItemCount = page.TotalResults,
        };
    }

    private static WidgetResourceError MapSearchError(Exception exception) => exception switch
    {
        YouTubeApplicationException application => new(application.Code, application.Message),
        OperationCanceledException => new("search_canceled", "YouTube search was canceled."),
        _ => new("youtube_search_failed", "YouTube search could not be completed. Try again."),
    };

    private static string PlaybackRateOptionId(double rate) =>
        PlaybackRateOptionPrefix + rate.ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatPlaybackRate(double rate) =>
        rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×";

    /// <summary>
    /// Preserves the existing setup copy exactly. A message the bounded command
    /// error refuses falls back to the same text <see cref="SafeMessage"/> uses,
    /// rather than to the SDK's generic wording.
    /// </summary>
    private static WidgetCommandError MapSetupError(Exception exception)
    {
        var code = exception is YouTubeApplicationException known
            ? known.Code
            : "youtube_setup_failed";
        try { return new WidgetCommandError(code, SafeMessage(exception)); }
        catch (ArgumentException)
        {
            return new WidgetCommandError("youtube_setup_failed", UnknownSetupFailure);
        }
    }

    private string? SearchInitialFocus(
        YouTubeSearchState search,
        WidgetCursorResourceSnapshot<YouTubeSearchItem> snapshot)
    {
        if (snapshot.RequestedFocusId is { } requested) return requested;
        if (search.ReturnFocusId is { } retained && snapshot.Items.Any(item =>
                string.Equals(ResultFocusId(item.VideoId), retained, StringComparison.Ordinal)))
            return retained;
        return snapshot.Status switch
        {
            WidgetPagedResourceStatus.Error when snapshot.Items.Count == 0 => SearchRetryActionId,
            _ when snapshot.Items.Count != 0 => ResultFocusId(snapshot.Items[0].VideoId),
            _ => "youtube.search.query",
        };
    }

    private static string ResultFocusId(string videoId) => "youtube.result." + videoId;

    private const string UnknownSetupFailure =
        "YouTube setup could not be completed. Try again.";

    private static string SafeMessage(Exception exception) =>
        exception is YouTubeApplicationException known ? known.Message : UnknownSetupFailure;
}
