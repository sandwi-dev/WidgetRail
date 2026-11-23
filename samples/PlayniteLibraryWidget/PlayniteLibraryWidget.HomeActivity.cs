using System.Globalization;
using WidgetRail.Playnite;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    internal const string HomeActivityList = "playnite-library.home.activity.list";
    private sealed record HomeCapture(string GameId, string GameName, CaptureLibraryItem Item);
    private sealed record HomeCapturePage(IReadOnlyList<HomeCapture> Items, string? Profile, bool HasMore);
    private sealed record HomeEvents(PlayniteRecentActivity? Activity, IReadOnlyList<HomeCapture>? Captures, PlayniteLibraryHomeQuery? Catalog = null);
    private sealed record HomePost(string Key, string GameId, string Name, DateTimeOffset When,
        PlayniteRecentSession? Session = null, PlayniteRecentAchievement? Unlock = null)
    {
        internal IReadOnlyList<CaptureLibraryItem> Captures { get; init; } = [];
        internal int Count { get; init; } = 1;
        internal long PlayedSeconds { get; init; }
        internal string Category => Unlock is not null ? "Achievements" : Session is not null ? "Play sessions" : "Captures";
        internal PlayniteLibraryHomeItem? Game { get; init; }
        internal string? RecentHero { get; init; }
        internal string? Hero => Game?.Row.Current?.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Hero)?.Handle ?? RecentHero;
    }
    private static readonly WidgetIdScope HomeEventIds = WidgetIds.Scope("playnite.history");
    private sealed record HomePostContext(HomeEvents Input, IndexedCollectionGroup[] Groups);
    private readonly WidgetResource<PlayniteRecentActivity> homeRecentActivity;
    private readonly WidgetResource<HomeCapturePage> homeCaptures;
    private readonly WidgetLoadedCollection<HomePostContext, HomePost> homePosts;

    private void LoadHomeActivity()
    {
        if (_navigation.Value.Route != PlayniteLibraryRoute.Library || LifecycleState != WidgetLifecycleState.Interactive ||
            HomeState.Publication is not { Query.IsWarmDisplayOnly: false }) return;
        LoadPlaying();
        homeRecentActivity.EnsureLoaded();
        homeCaptures.EnsureLoaded();
    }

    private async ValueTask<HomeCapturePage> ReadHomeCapturesAsync(CancellationToken token)
    {
        var access = await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission], token);
        if (!access.Any(p => p.CapabilityId == WidgetCaptureLibraryCapabilities.ReadPermission && p.State == WidgetPermissionState.Granted)) return new([], null, false);
        if (await _application.GetCaptureIntegrationAsync(token) is not { } integration) return new([], null, false);
        var page = await HostServices.CaptureLibrary.QueryAsync(new() { OriginPackageId = PlaynitePackage, AvailableOnly = true },
            count: 16, cancellationToken: token);
        var result = new List<HomeCapture>();
        foreach (var item in page.Items.Where(item => item.ContextCount > 0))
        {
            var games = new HashSet<Guid>();
            var offset = 0;
            string? revision = null;
            while (true)
            {
                var contexts = await HostServices.CaptureLibrary.GetDestinationsAsync(item.Id, offset, 16, revision, token);
                foreach (var target in contexts.Items.Where(target => target.PackageId == PlaynitePackage && target.Kind == CaptureRelatedIdentity.GameKind))
                    if (CaptureRelatedIdentity.TryGame(integration.ProfileId, target.Kind, target.Id, out var game) &&
                        !string.IsNullOrWhiteSpace(target.Label) && games.Add(game))
                        result.Add(new(game.ToString("D"), target.Label, item));
                offset += contexts.Items.Count;
                if (contexts.Items.Count == 0 || offset >= contexts.TotalCount) break;
                revision = contexts.Revision;
            }
        }
        return new(result, integration.ProfileId, page.Items.Count < page.TotalCount);
    }

    private WidgetElement? HomeActivityView()
    {
        var input = new HomeEvents(homeRecentActivity.Snapshot.Value, homeCaptures.Snapshot.Value?.Items, HomeState.Publication?.Query);
        if (homePosts.Context.Input != input)
        {
            var events = new List<HomePost>();
            foreach (var session in input.Activity?.Sessions ?? [])
                events.Add(new("", session.GameId, session.GameName, session.StartedAt, Session: session));
            foreach (var unlock in input.Activity?.Unlocks ?? [])
                events.Add(new(HomeEventIds.KeyedId("unlock", unlock.GameId + "/" + unlock.AchievementId),
                    unlock.GameId, unlock.GameName, unlock.UnlockedAt, Unlock: unlock));
            foreach (var capture in input.Captures ?? [])
            {
                if (input.Catalog?.Source.Authority.ContainsGame(capture.GameId) != true) continue;
                var stamp = capture.Item.CapturedAtUnixMilliseconds ?? capture.Item.SavedAtUnixMilliseconds;
                if (stamp is >= -62135596800000 and <= 253402300799999)
                    events.Add(new HomePost(HomeEventIds.KeyedId("capture", capture.GameId + "/" + capture.Item.Id), capture.GameId,
                        capture.GameName, DateTimeOffset.FromUnixTimeMilliseconds(stamp)) { Captures = [capture.Item] });
            }
            var hidden = input.Catalog?.Source.Authority.HiddenGameIds.ToHashSet(StringComparer.Ordinal);
            var games = events.Select(e => e.GameId).Distinct().ToDictionary(id => id, id => input.Catalog?.Find(PlayniteLibraryIdentity.Key(id)));
            var rows = new List<HomePost>();
            var groups = new List<IndexedCollectionGroup>();
            var visible = events.Where(e => hidden?.Contains(e.GameId) != true)
                .OrderByDescending(e => e.When).ThenBy(e => e.Key, StringComparer.Ordinal);
            foreach (var day in visible.GroupBy(e => DateOnly.FromDateTime(e.When.LocalDateTime)).OrderByDescending(g => g.Key))
            {
                var dateKey = day.Key.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var dayStart = rows.Count;
                foreach (var category in day.GroupBy(e => e.Category))
                {
                    var start = rows.Count;
                    if (category.First().Unlock is not null)
                    {
                        rows.AddRange(category.Select(post => post with { Game = games[post.GameId],
                            RecentHero = input.Activity?.HeroHandles?.GetValueOrDefault(post.GameId) }));
                    }
                    else
                    {
                        foreach (var game in category.GroupBy(e => e.GameId))
                        {
                            var first = game.First();
                            rows.Add(first with
                            {
                                Key = HomeEventIds.KeyedId("digest", $"{dateKey}/{category.Key}/{game.Key}"),
                                Game = games[game.Key], RecentHero = input.Activity?.HeroHandles?.GetValueOrDefault(game.Key),
                                Count = game.Count(), PlayedSeconds = (long)Math.Min(long.MaxValue, game.Sum(e => (decimal)(e.Session?.Seconds ?? 0))),
                                Captures = game.SelectMany(e => e.Captures).ToArray(),
                            });
                        }
                    }
                    var firstCategory = start == dayStart;
                    var heading = firstCategory ? $"{JournalDate(day.Key)}   ·   {category.Key}" : category.Key;
                    groups.Add(new(HomeEventIds.KeyedId("group", dateKey + "/" + category.Key), heading, rows.Count - start)
                        { ShowHeaderRule = firstCategory });
                }
            }
            homePosts.Publish("library", new(input, groups.ToArray()), rows);
        }
        if (homePosts.Items.Count == 0)
        {
            if (homeRecentActivity.Snapshot.Status == WidgetResourceStatus.Loading || homeCaptures.Snapshot.Status == WidgetResourceStatus.Loading)
                return UI.LoadingIndicator(HomeActivityList + ".loading", size: LoadingIndicatorSize.Compact);
            if ((homeRecentActivity.Snapshot.Error ?? homeCaptures.Snapshot.Error)?.Message is { } error)
                return UI.Text(error, HomeActivityList + ".unavailable").Classes("playnite-library-details-meta");
            return null;
        }
        return UI.CollectionGrid(HomeActivityList, homePosts, 410, 176, "Recent game activity", maximumColumns: 2)
            .WithDirectionalAnchor(FocusAnchorPoint.Leading, 25)
            .Grouped(homePosts.Context.Groups).Classes("story-feed-list");
    }

    private WidgetLoadedCollection<HomePostContext, HomePost> CreateHomePosts() =>
        CreateLoadedCollection<HomePostContext, HomePost>(HomeActivityList + ".source", "library", new(new(null, null), []), [], new()
        {
            PreserveOccurrences = true,
            ItemKey = post => new(post.Key),
            RenderItem = (_, post, ids) => HomeActivityCard(post, ids),
            ResolveArtworkSource = async (_, post, handle, token) =>
            {
                if (post.Unlock is { Icon: { } path } award && handle == StoryIcon(RecentKey(award)) && ArtworkFileContract.IsLocalPath(path))
                    return new WidgetFileArtwork(path, RecentKey(award));
                if (post.Game is { } game && game.Row.Current?.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Hero)?.Handle == handle.Value)
                    return await game.Query.ResolveArtworkSourceAsync(game, handle, OnResolveArtworkAsync, token);
                if (post.RecentHero == handle.Value) return await _application.ResolveArtworkSourceAsync(handle, token);
                var item = post.Captures.Take(2).FirstOrDefault(value => CaptureArtwork(value) == handle);
                if (item is null) return null;
                try { return new WidgetEncodedArtwork(WidgetArtworkContentType.Png, (await HostServices.CaptureLibrary.GetThumbnailAsync(item.Id, token)).Bytes); }
                catch (WidgetCapabilityException) { return null; }
            },
            OnAction = async (context, post, action, token) =>
            {
                var scope = _navigation.Value.InputScopeId;
                bool Current() => _navigation.Value.Route == PlayniteLibraryRoute.Library && _navigation.Value.InputScopeId == scope &&
                    LifecycleState == WidgetLifecycleState.Interactive && ReferenceEquals(homePosts.Context, context);
                if (!Current()) return;
                if (action.ActionId != HomeActivityList + ".open") return;
                if (await _application.GetGameDetailsAsync(post.GameId, token) is not { } details || details.Item.SavedId != post.GameId) return;
                token.ThrowIfCancellationRequested();
                if (!Current()) return;
                var item = PlayniteLibraryItem.From(details.Item);
                var destination = new PlayniteDetailsExtras { Full = details };
                if (post.Captures.Count > 0)
                    destination = destination with { CaptureViewer = post.Captures.Select(capture => new MediaPlayerGalleryItem(capture.Id,
                        CaptureType(capture) + " · " + CaptureTime(capture), MediaPlayerSource.FromSavedCapture(capture.Id, capture.Revision))).ToArray(),
                        CaptureViewerFromHome = true };
                else if (post.Unlock is { } unlock)
                    destination = destination with { Tab = PlayniteDetailsTab.Achievements, TabFocus = CreateFocusRequest(),
                        AchievementSearch = unlock.Name[..Math.Min(128, unlock.Name.Length)], AchievementFilter = AchievementFilter.Unlocked };
                else
                    destination = destination with { Tab = PlayniteDetailsTab.Activity, TabFocus = CreateFocusRequest() };
                OpenDetails(item, capturedTarget: new(item, () => _navigation.Value.RootRoute == PlayniteLibraryRoute.Library &&
                    homePosts.Items.Any(row => row.Key == post.Key)), openingAction: action, initialDetails: destination);
            },
        });

}
