using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

internal enum YouTubeRoute { Setup, Search, Link, Player }

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
    private readonly IYouTubeApplicationService _application;
    private readonly WidgetCursorResource<YouTubeSearchItem> _searchResults;
    private YouTubeRoute _route = YouTubeRoute.Setup;
    private bool _configurationKnown;
    private bool _configured;
    private bool _setupBusy;
    private string? _setupError;
    private string _queryDraft = string.Empty;
    private string _activeQuery = string.Empty;
    private string? _searchReturnFocus;

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
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        lock (_gate)
        {
            _isActive = true;
            _activeLifetime = activeLifetime;
            SchedulePendingFeedbackLocked();
        }
        if (_configurationKnown) return ValueTask.CompletedTask;
        Operations.RunSingleFlight("youtube.configuration", async context =>
        {
            try
            {
                var summary = await _application.GetConfigurationAsync(context.CancellationToken)
                    .ConfigureAwait(false);
                if (!context.IsCurrent) return;
                lock (_gate)
                {
                    _configurationKnown = true;
                    _configured = summary.IsConfigured;
                    SetRouteLocked(summary.IsConfigured
                        ? YouTubeRoute.Search : YouTubeRoute.Setup);
                    _setupError = null;
                }
            }
            catch (Exception exception)
            {
                if (!context.IsCurrent) return;
                lock (_gate)
                {
                    _configurationKnown = true;
                    _configured = false;
                    SetRouteLocked(YouTubeRoute.Setup);
                    _setupError = SafeMessage(exception);
                }
            }
            finally { if (context.IsCurrent) Invalidate(); }
        });
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        lock (_gate)
        {
            _isActive = false;
            _overlayFullscreen = false;
            CancelPendingFeedbackLocked();
        }
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        Task pendingFeedback;
        lock (_gate)
        {
            _isActive = false;
            CancelPendingFeedbackLocked();
            pendingFeedback = _pendingFeedbackTask;
        }
        try
        {
            await pendingFeedback.WaitAsync(shutdownToken).ConfigureAwait(false);
            await _application.DisposeAsync().AsTask().WaitAsync(shutdownToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested) { }
    }

    private WidgetView RenderApplication()
    {
        YouTubeRoute route;
        bool known;
        lock (_gate)
        {
            route = _route;
            known = _configurationKnown;
        }
        if (!known)
            return ApplicationView(
                UI.Stack("youtube.configuration.loading",
                    UI.LoadingIndicator("youtube.configuration.indicator", "Checking YouTube setup"),
                    UI.Text("Checking YouTube setup…", "youtube.configuration.text"))
                    .Classes("youtube-page", "youtube-centered"),
                null);
        return route switch
        {
            YouTubeRoute.Setup => RenderSetup(),
            YouTubeRoute.Search => RenderSearch(),
            YouTubeRoute.Link => RenderLinkPlayer(),
            _ => RenderPlayer(),
        };
    }

    private WidgetView RenderSetup()
    {
        bool configured;
        bool busy;
        string? error;
        lock (_gate)
        {
            configured = _configured;
            busy = _setupBusy;
            error = _setupError;
        }
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
        if (error is not null)
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
        return ApplicationView(UI.VerticalScroll("youtube.setup.scroll", children.ToArray())
            .Classes("youtube-page", "youtube-setup"), "youtube.setup.console");
    }

    private WidgetView RenderSearch()
    {
        string draft;
        string active;
        lock (_gate)
        {
            draft = _queryDraft;
            active = _activeQuery;
        }
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
                    UI.Text($"Looking for {active}…", "youtube.search.loading.text")
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
        if (NowPlayingRow() is { } nowPlaying) children.Add(nowPlaying);
        children.Add(content);
        var root = UI.Stack("youtube.search.root", children.ToArray())
            .InputScope("youtube.search.root").Classes("youtube-root", "youtube-search-root");
        return new WidgetView(root, SearchInitialFocus(snapshot),
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
        { EmbeddedMedia = RetainedHiddenMediaSurface() };
    }

    private WidgetElement? NowPlayingRow()
    {
        string? videoId;
        EmbeddedMediaPlaybackState state;
        lock (_gate) { videoId = _videoId; state = _state; }
        return videoId is null ? null :
            UI.Button(
                    $"Return to player  ·  {(state == EmbeddedMediaPlaybackState.Playing ? "Playing" : "Paused or ready")}",
                    "youtube.player.return", "youtube.player.return")
                .Classes("youtube-now-playing");
    }

    private WidgetView RenderLinkPlayer()
    {
        var view = RenderPlayer(includeDashboardQuickActions: false);
        return view with { InitialFocusId = "youtube.link" };
    }

    private WidgetView ApplicationView(WidgetElement content, string? initialFocus)
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
        { EmbeddedMedia = RetainedHiddenMediaSurface() };
    }

    private EmbeddedMediaSurface? RetainedHiddenMediaSurface()
    {
        lock (_gate)
        {
            return _eventSequence > 0 && _videoId is { } videoId
                ? CreateMediaSurface(videoId, _pendingCommand, retainSessionWhenHidden: true)
                : null;
        }
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
                StartConfigure(secret);
                return true;
            case SetupDeleteActionId:
                StartDelete();
                return true;
            case SetupRouteActionId:
                lock (_gate) { SetRouteLocked(YouTubeRoute.Setup); _setupError = null; }
                Invalidate();
                return true;
            case SearchRouteActionId:
                lock (_gate) SetRouteLocked(_configured
                    ? YouTubeRoute.Search : YouTubeRoute.Setup);
                Invalidate();
                return true;
            case LinkRouteActionId:
                lock (_gate) SetRouteLocked(YouTubeRoute.Link);
                Invalidate();
                return true;
            case "youtube.player.return":
                lock (_gate) SetRouteLocked(YouTubeRoute.Player);
                Invalidate();
                return true;
            case BackActionId:
                lock (_gate) SetRouteLocked(_configured
                    ? YouTubeRoute.Search : YouTubeRoute.Setup);
                Invalidate();
                return true;
            case SearchCommitActionId when action.CommittedText is { } query:
                lock (_gate) _queryDraft = query.Trim();
                Invalidate();
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

    private void StartConfigure(string secret)
    {
        lock (_gate) { _setupBusy = true; _setupError = null; }
        Invalidate();
        Operations.RunLatest("youtube.configure", async context =>
        {
            try
            {
                await _application.ConfigureApiKeyAsync(secret, context.CancellationToken)
                    .ConfigureAwait(false);
                if (!context.IsCurrent) return;
                lock (_gate)
                {
                    _configured = true;
                    _configurationKnown = true;
                    _setupBusy = false;
                    _setupError = null;
                    SetRouteLocked(YouTubeRoute.Search);
                }
            }
            catch (Exception exception)
            {
                if (!context.IsCurrent) return;
                lock (_gate) { _setupBusy = false; _setupError = SafeMessage(exception); }
            }
            finally { if (context.IsCurrent) Invalidate(); }
        });
    }

    private void StartDelete()
    {
        lock (_gate) { _setupBusy = true; _setupError = null; }
        Invalidate();
        Operations.RunLatest("youtube.configure", async context =>
        {
            try
            {
                await _application.DeleteApiKeyAsync(context.CancellationToken).ConfigureAwait(false);
                if (!context.IsCurrent) return;
                _searchResults.Reset(invalidate: false);
                lock (_gate)
                {
                    _configured = false;
                    _setupBusy = false;
                    _queryDraft = string.Empty;
                    _activeQuery = string.Empty;
                }
            }
            catch (Exception exception)
            {
                if (!context.IsCurrent) return;
                lock (_gate) { _setupBusy = false; _setupError = SafeMessage(exception); }
            }
            finally { if (context.IsCurrent) Invalidate(); }
        });
    }

    private void StartSearch()
    {
        string query;
        lock (_gate) query = _queryDraft.Trim();
        if (query.Length == 0) return;
        _searchResults.Reset(invalidate: false);
        lock (_gate) _activeQuery = query;
        _searchResults.EnsureLoaded();
        Invalidate();
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
        lock (_gate)
        {
            _searchReturnFocus = sourceId;
            _link = "https://www.youtube.com/watch?v=" + item.VideoId;
            _videoId = item.VideoId;
            _validationError = null;
            _playbackError = null;
            _position = 0;
            _duration = 0;
            SetRouteLocked(YouTubeRoute.Player);
            QueueCommand(EmbeddedMediaPlaybackCommandKind.Load, item.VideoId);
        }
        return true;
    }

    private void SetRouteLocked(YouTubeRoute route)
    {
        _route = route;
        if (route != YouTubeRoute.Player) _overlayFullscreen = false;
    }

    private async ValueTask<WidgetCursorPage<YouTubeSearchItem>> LoadSearchPageAsync(
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int pageSize,
        CancellationToken cancellationToken)
    {
        string query;
        lock (_gate) query = _activeQuery;
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

    private string? SearchInitialFocus(WidgetCursorResourceSnapshot<YouTubeSearchItem> snapshot)
    {
        if (snapshot.RequestedFocusId is { } requested) return requested;
        if (_searchReturnFocus is { } retained && snapshot.Items.Any(item =>
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

    private static string SafeMessage(Exception exception) => exception is YouTubeApplicationException known
        ? known.Message
        : "YouTube setup could not be completed. Try again.";
}
