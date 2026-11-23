using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayingShelfView(WidgetElement Cards, WidgetElement Summary, string EntryId, bool ConditionalEntry, string? ProcessKey = null);
internal sealed record PlayingReader(string GameId, SteamAnnouncements Feed, string ArticleId, WidgetFocusRequest? Focus = null);

public sealed partial class PlayniteLibraryWidget
{
    private const string Playing = "playnite.playing.";
    private readonly WidgetQueryResource<string, PlayniteGameDetails> playingDetails;
    private readonly WidgetQueryResource<string, SteamAnnouncements> playingNews;
    private sealed record PlayingAwardQuery(string Game, TrackedAchievement? Tracked);
    private readonly WidgetQueryResource<PlayingAwardQuery, PlayingAwards> playingAwards;
    private PlayingAwardQuery AwardsQuery(string game)
    {
        lock (_gate) return new(game, _organization.TrackedAchievements.FirstOrDefault(t =>
            t.Game == game && (playingAuthority is null || t.Connection == playingAuthority.Value.Connection)));
    }
    private readonly WidgetQueryResource<string, IReadOnlyList<CaptureLibraryItem>> playingCaptures;

    private static string? PlayingId(PlayniteLibraryRenderState state) =>
        state.RunningGames is { Count: > 0 } games ? state.PlayingGame is { } selected && games.Contains(selected)
            ? selected : games.Min(StringComparer.Ordinal) : null;

    private void LoadPlaying()
    {
        var id = PlayingId(_model.Value);
        if (id is null || LifecycleState != WidgetLifecycleState.Interactive || _navigation.Value.Route != PlayniteLibraryRoute.Library) return;
        var route = _navigation.Value.RouteCancellationToken;
        var hidden = HiddenCards;
        if (hidden == PlayingCardKind.All) return;
        if (!hidden.HasFlag(PlayingCardKind.Achievements)) playingAwards.EnsureLoaded(AwardsQuery(id), route);
        playingDetails.EnsureLoaded(id, route);
        if (!hidden.HasFlag(PlayingCardKind.News)) playingNews.EnsureLoaded(id, route);
        if (!hidden.HasFlag(PlayingCardKind.Capture)) playingCaptures.EnsureLoaded(id, route);
    }

    private async ValueTask<SteamAnnouncements> ReadPlayingNewsAsync(string id, CancellationToken token)
    {
        await playingDetails.WhenIdleAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (playingDetails.GetSnapshot(id).Value is not { } details)
            throw new PlayniteBridgeDataException("game_not_found");
        return details.StoreLookup?.AppId is uint steamId
            ? await _application.GetAnnouncementsAsync(steamId, token).ConfigureAwait(false) : new(0, []);
    }

    private void ResetPlayingResources()
    {
        playingAwards.Reset(invalidate: false);
        playingNews.Reset(invalidate: false);
        playingDetails.Reset(invalidate: false);
        playingCaptures.Reset(invalidate: false);
    }

    private PlayniteLibraryItem? PlayingItem(string id)
    {
        var loaded = playingDetails.GetSnapshot(id).Value;
        return loaded is not null ? PlayniteLibraryItem.From(loaded.Item) :
            HomeState.Publication?.Query.Find(PlayniteLibraryIdentity.Key(id))?.Row.Current;
    }

    private PlayingShelfView? PlayingShelf(PlayniteLibraryRenderState state, string homeTab)
    {
        if (PlayingId(state) is not { } id) return null;
        var hidden = HiddenCards;
        if (hidden == PlayingCardKind.All) return null;
        var item = PlayingItem(id);
        var key = Playing + id;
        var title = item?.Presentation.DisplayName ?? state.RunningNames?.GetValueOrDefault(id) ?? "Running game";
        var cards = new List<(PlayingCardKind Kind, ActionSurfaceElement Card)>();
        void Add(PlayingCardKind kind, ActionSurfaceElement card)
        {
            if (!hidden.HasFlag(kind))
                cards.Add((kind, card.ContextAction(CardPreferences + ".hide." + id + "." + kind,
                    kind == PlayingCardKind.Achievements ? "Hide achievement cards" : "Hide this card")
                    .ContextAction(ManageCards, "Manage cards").ContextMenuShortcut(ControllerButton.X)));
        }
        var hero = item?.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Hero);
        var started = state.RunningSince?.GetValueOrDefault(id) is > 0 and var stamp
            ? DateTimeOffset.FromUnixTimeMilliseconds(stamp).ToLocalTime() : (DateTimeOffset?)null;
        var entry = key + ".resume";
        var below = _indexedHome.Descriptor.Count > 0 ? PlayniteLibraryPresentation.HomeRailId : "playnite-library.home.view-library";
        if (!hidden.HasFlag(PlayingCardKind.Details))
        {
            var cover = item?.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile);
            Add(PlayingCardKind.Details, PlayingResumeCard(entry, title, cover?.Handle == hero?.Handle ? null : cover?.Handle,
                playingDetails.GetSnapshot(id).Value?.Game).FocusDown(below));
        }
        if (LatestPlayingCapture(id) is { } capture)
            Add(PlayingCardKind.Capture, PlayingCaptureCard(key + ".capture", capture).FocusDown(below));
        if (!hidden.HasFlag(PlayingCardKind.Achievements) && playingAwards.GetSnapshot(AwardsQuery(id)).Value is { Total: > 0 } awards)
        {
            if (awards.Badges.Count == 0)
                Add(PlayingCardKind.Achievements, PlayingAchievementCard(key + ".achievement", awards, null).FocusDown(below));
            foreach (var badge in awards.Badges)
                Add(PlayingCardKind.Achievements, PlayingAchievementCard(key + ".achievement." + badge.Key, awards, badge).FocusDown(below));
        }
        if (playingNews.GetSnapshot(id).Value?.Items.FirstOrDefault() is { } article)
            Add(PlayingCardKind.News, PlayingStoryCard(key + ".news", article).FocusDown(below));
        if (item is not null && title.Length <= 256 && !title.Any(char.IsControl))
        {
            var help = key + ".help";
            var face = UI.Grid(help + ".face", [GridTrack.Star(), GridTrack.Auto()], [],
                UI.Icon(PlayniteIcons.GameHelp, help + ".icon", "Game Help").Classes("playing-help-mark")
                    .AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center),
                UI.Text("Ask Game Help", help + ".title").Classes("playing-card-title").InGrid(row: 1))
                .Classes("playing-card-face", "playing-help-face");
            Add(PlayingCardKind.Help, UI.ActionSurface(help, help, "Ask Game Help", ActionSurfaceOrientation.Vertical, face)
                .OpenIntent(WidgetIntentContracts.GameHelp, JsonSerializer.SerializeToElement(new { gameName = title }),
                    WidgetIntentPresentation.OpenWidget).FocusDown(below).Classes("playing-action", "playing-help"));
        }
        var session = homeRecentActivity.Snapshot.Value?.Sessions
            .Where(s => s.GameId == id && s.Seconds > 0 && (started is null || (started.Value - s.StartedAt).TotalSeconds >= s.Seconds))
            .MaxBy(s => s.StartedAt);
        if (session is not null || started is not null)
            Add(PlayingCardKind.Sessions, PlayingSessionCard(key + ".sessions", started, session).FocusDown(below));
        if (cards.Count == 0) return null;
        if (cards.Count == 1 && cards[0].Kind == PlayingCardKind.Help)
            cards.Add((PlayingCardKind.Help, UI.ActionSurface(ManageCards, key + ".manage", "Manage cards", ActionSurfaceOrientation.Vertical,
                UI.Text("Manage cards", key + ".manage.label").Classes("playing-card-title")).FocusDown(below).Classes("playing-action", "playing-manage")));
        var processKey = state.RunningProcesses?.GetValueOrDefault(id);
        var tiles = cards.Select(c =>
        {
            WidgetElement card = processKey is null ? c.Card : c.Card.FocusUp(homeTab);
            if (hero is { Handle.Length: > 0 }) card = card.FocusBackground(new(hero.Handle));
            return processKey is null ? card : card.RevealForegroundOnFocus(processKey);
        }).ToArray();
        entry = tiles[0].Id;
        if (Enum.TryParse<PlayingCardKind>(state.HiddenCardFocusOrigin, out var removed))
        {
            var removedIndex = Array.FindIndex(CardChoices, choice => choice.Kind == removed);
            var next = cards.FindIndex(c => Array.FindIndex(CardChoices, choice => choice.Kind == c.Kind) > removedIndex);
            entry = tiles[next >= 0 ? next : tiles.Length - 1].Id;
        }
        var row = UI.Row(key + ".cards", tiles).RememberChildFocus(entry).Classes("playing-cards");
        var body = new List<WidgetElement> { UI.HorizontalScroll(key + ".scroll", row)
            .WithDirectionalAnchor(FocusAnchorPoint.Leading, 10).Classes("playing-scroll") };
        if (state.RunningGames!.Count > 1)
        {
            var options = state.RunningGames.Order(StringComparer.Ordinal).Take(32).Select(game =>
                new SelectOption(game, state.RunningNames?.GetValueOrDefault(game) ?? "Running game",
                    Playing + game + ".switch", IsSelected: game == id)).ToArray();
            body.Add(UI.Select("Running games", options, key + ".games").FocusDown(below));
        }
        var summary = item is null
            ? UI.Stack(key + ".summary", UI.Text(title, key + ".name").Classes("playnite-library-summary-title-home"))
                .Classes("playnite-library-home-summary")
            : PlayniteLibraryPresentation.FocusedGameSummary(id, title, item.Presentation.Source.DisplayName,
                "Running", HomeState.Publication?.Query.Source.Authority.FavoriteGameIds.Contains(id) == true, null, item, browseLayout: false, canQuickPlay: false, runningShelf: true);
        return new(UI.Stack(key + ".body", body.ToArray()).RememberChildFocus(entry).Classes("playing-body"), summary, entry, entry == key + ".help", processKey);

    }

    private async ValueTask<IReadOnlyList<CaptureLibraryItem>> ReadPlayingCapturesAsync(string game, CancellationToken token)
    {
        await homeCaptures.EnsureLoaded().Completion.WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (homeCaptures.Snapshot is not { Status: WidgetResourceStatus.Ready, Value: { Profile: { } profile } page }) return [];
        if (page.Items.FirstOrDefault(c => c.GameId == game) is { } latest) return [latest.Item];
        if (!page.HasMore || !Guid.TryParseExact(game, "D", out var gameId)) return [];
        return await QueryGameCapturesAsync(profile, gameId, 1, token).ConfigureAwait(false);
    }

    private CaptureLibraryItem? LatestPlayingCapture(string id) => playingCaptures.GetSnapshot(id).Value?.FirstOrDefault(c => c.Available) ?? homeCaptures.Snapshot.Value?.Items
        .Where(c => c.GameId == id).MaxBy(c => c.Item.CapturedAtUnixMilliseconds ?? c.Item.SavedAtUnixMilliseconds)?.Item;

    private async ValueTask<bool> HandlePlayingAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (!action.ActionId.StartsWith(Playing, StringComparison.Ordinal)) return false;
        var state = _model.Value;
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library || LifecycleState != WidgetLifecycleState.Interactive ||
            PlayingId(state) is not { } id) return true;
        var key = Playing + id;
        if (action.ActionId.EndsWith(".switch", StringComparison.Ordinal))
        {
            var selected = action.ActionId[Playing.Length..^7];
            if (state.RunningGames!.Contains(selected))
            {
                _model.Update(s => s with { PlayingGame = selected, NewsReader = null });
                LoadPlaying();
            }
            return true;
        }
        if (!action.ActionId.StartsWith(key + ".", StringComparison.Ordinal)) return true;
        if (action.ActionId == key + ".resume")
        {
            if (state.NewsReader is null && !HiddenCards.HasFlag(PlayingCardKind.Details))
                await ResumeGameAsync(id, PlayingItem(id)?.Presentation.DisplayName ?? state.RunningNames?.GetValueOrDefault(id) ?? "Game",
                    () => _navigation.Value.Route == PlayniteLibraryRoute.Library && PlayingId(_model.Value) == id, token);
            return true;
        }
        if (action.ActionId == key + ".news" && playingNews.GetSnapshot(id).Value is { Items.Count: > 0 } feed)
            _model.Update(s => s with { NewsReader = new(id, feed, feed.Items[0].Id) });
        else if (state.NewsReader is { } reader && reader.GameId == id)
        {
            var cmd = action.ActionId[(key.Length + 1)..];
            if (cmd == "close") _model.Update(s => s with { NewsReader = null });
            else
            {
                var articles = reader.Feed.Items;
                var next = articles.FirstOrDefault(article => cmd == "article." + article.Id);
                if (next is not null) _model.Update(s => s with { NewsReader = reader with { ArticleId = next.Id, Focus = CreateFocusRequest() } });
            }
        }
        else
        {
            var badge = playingAwards.GetSnapshot(AwardsQuery(id)).Value?.Badges.FirstOrDefault(b =>
                action.ActionId == key + ".achievement." + b.Key);
            if (badge is null && action.ActionId != key + ".capture" &&
                action.ActionId != key + ".achievement" && action.ActionId != key + ".sessions") return true;
            var game = PlayingItem(id);
            if (game is null)
            {
                var route = _navigation.Value.RouteCancellationToken;
                await playingDetails.EnsureLoaded(id, route).Completion.WaitAsync(token);
                if (route.IsCancellationRequested || PlayingId(_model.Value) != id ||
                    LifecycleState != WidgetLifecycleState.Interactive) return true;
                if (badge is not null && playingAwards.GetSnapshot(AwardsQuery(id)).Value?.Badges.Any(b => b.Key == badge.Key) != true) return true;
                game = PlayingItem(id);
                if (game is null) ShowActionToast("Game details", "The game could not be read from Playnite.", false);
            }
            if (game is null) return true;
            var extras = new PlayniteDetailsExtras { Full = playingDetails.GetSnapshot(id).Value };
            if (action.ActionId == key + ".capture" && LatestPlayingCapture(id) is { } capture)
                extras = extras with { CaptureViewer = [new(capture.Id, CaptureType(capture), MediaPlayerSource.FromSavedCapture(capture.Id, capture.Revision))], CaptureViewerFromHome = true };
            else if (badge is not null || action.ActionId == key + ".achievement")
                extras = extras with { Tab = PlayniteDetailsTab.Achievements, AchievementSearch = badge is null ? "" :
                    BoundProgress(badge.Award.Name, 128), TabFocus = CreateFocusRequest(),
                    ShelfAchievement = badge is null ? null : (badge.Key, badge.RowKey, CreateFocusRequest()) };
            else if (action.ActionId == key + ".sessions") extras = extras with { Tab = PlayniteDetailsTab.Activity, TabFocus = CreateFocusRequest() };
            else return true;
            OpenDetails(game, openingAction: action, initialDetails: extras);
        }
        return true;
    }

    private WidgetView NewsView(WidgetView parent, PlayingReader reader)
    {
        const int pageSize = 5;
        var key = Playing + reader.GameId;
        var articles = reader.Feed.Items;
        var position = articles.ToList().FindIndex(a => a.Id == reader.ArticleId);
        var selected = articles[position];
        var start = position / pageSize * pageSize;
        string ArticleId(SteamAnnouncement article) => key + ".article." + article.Id;
        var group = key + ".headlines";
        var rows = new List<WidgetElement>();
        if (start > 0) rows.Add(UI.Button("Newer announcements", ArticleId(articles[start - 1]), key + ".newer"));
        string? previousMonth = null;
        for (var i = start; i < Math.Min(start + pageSize, articles.Count); ++i)
        {
            var article = articles[i];
            var month = article.Published.ToLocalTime().ToString("MMMM yyyy");
            var id = ArticleId(article);
            if (previousMonth != month)
                rows.Add(UI.Text(month, id + ".month").Classes("news-month"));
            previousMonth = month;
            var button = UI.ActionSurface(id, id, article.Title, ActionSurfaceOrientation.Vertical,
                UI.Text(article.Title, id + ".label").Classes("news-headline-text")).Classes("news-headline");
            if (i > 0) button = button.Shortcut(ControllerButton.LeftBumper, "Previous story", actionId: ArticleId(articles[i - 1]));
            if (i + 1 < articles.Count) button = button.Shortcut(ControllerButton.RightBumper, "Next story", actionId: ArticleId(articles[i + 1]));
            var original = UI.IconButton(PlayniteIcons.Links, id + ".original", id + ".original", "Read original: " + article.Title)
                .OpenIntent(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = reader.Feed.Original(article) }));
            rows.Add(UI.Grid(id + ".row", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Auto()],
                button.PresentOnFocus(Story(article, id + ".preview")), original.InGrid(column: 1)).Spacing(0, 4));
        }
        if (start + pageSize < articles.Count) rows.Add(UI.Button("Older announcements", ArticleId(articles[start + pageSize]), key + ".older"));
        var index = UI.VerticalScroll(key + ".index", UI.Stack(group, rows.ToArray()).RememberChildFocus(ArticleId(selected)).Classes("news-index"));
        var panes = (UI.FocusPresentationSurface(index, Story(selected, key + ".initial"), key + ".panes") with { RetainLastPresentation = true })
            .Classes("news-reading-panes");
        var view = parent.WithModal(new WidgetModal(key + ".reader", "Game updates", panes, ArticleId(selected), key + ".close")
        {
            ScrollContent = false, StyleClass = "news-dialog",
            HeaderActions = UI.Row(key + ".links", UI.Button("View all on Steam", "unused", key + ".archive")
                .OpenIntent(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = reader.Feed.Archive })),
                UI.IconButton(PlayniteIcons.Back, key + ".close", key + ".close", "Close"))
        });
        return reader.Focus is { } focus ? view with { FocusGroupEntryRequest = focus.FocusElement(group, ArticleId(selected)) } : view;

        static WidgetElement Story(SteamAnnouncement article, string id)
        {
            var body = new List<WidgetElement> { UI.Text("Steam announcements  ·  " + article.Published.ToLocalTime().ToString("MMMM d, yyyy"),
                id + ".date").Classes("news-month"), UI.Text(article.Title, id + ".title").Classes("news-title") };
            if (article.Artwork is { } img) body.Add(UI.Image(img, id + ".image", article.Title, ImageFit.Contain).Classes("news-image"));
            var part = 0;
            foreach (var text in WidgetTextChunks.Split(article.Text, 1800, preferWhitespace: true))
                body.Add(UI.Text(text, id + ".text." + part++).Classes("news-copy"));
            return UI.Stack(id, body.ToArray()).Classes("news-article");
        }
    }
}
