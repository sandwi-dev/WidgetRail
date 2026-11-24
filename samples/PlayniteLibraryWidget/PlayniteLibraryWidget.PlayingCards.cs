using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayingBadge(string Key, string RowKey, string Group, PlayniteAchievement Award, WidgetArtworkHandle? LocalArtwork, bool Latest);
internal sealed record PlayingAwards(int Total, int Unlocked, IReadOnlyList<PlayingBadge> Badges, int NextActivityStart)
{
    internal const string ArtworkPrefix = "playnite.playing-award.";
    private static readonly WidgetIdScope Ids = WidgetIds.Scope("playnite.playing-award");
    private static readonly WidgetIdScope Keys = WidgetIds.Scope("a");
    internal PlayingBadge? Tracked => Badges.FirstOrDefault(b => b.Group == "Tracked");

    internal PlayingAwards LimitNextActivity(int count)
    {
        // Tracked and recent cards precede the ranked suggestions.
        var visibleCount = NextActivityStart + count;
        return visibleCount >= Badges.Count ? this : this with { Badges = Badges.Take(visibleCount).ToArray() };
    }

    internal static PlayingAwards From(string game, PlayniteAchievements data, TrackedAchievement? tracked = null)
    {
        if (!data.Available || data.Total == 0) return new(0, 0, [], 0);
        var badges = new List<PlayingBadge>(PlayniteLibraryPrivateState.MaximumNextActivityCount + 2);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var latest = data.Items.Where(a => a.Unlocked).MaxBy(a => a.UnlockedAt ?? DateTimeOffset.MinValue);
        if (tracked?.Matches(game, data) == true &&
            data.Items.Where(a => a.Id == tracked.Id).Take(2).ToArray() is [var match] &&
            (match.Unlocked || PlayniteAchievementPresentation.CanTrack(match)))
            Add(match, "Tracked");
        if (latest is not null) Add(latest, "Recent unlock");
        var nextActivityStart = badges.Count;
        var limit = nextActivityStart + PlayniteLibraryPrivateState.MaximumNextActivityCount;
        foreach (var award in data.Items.Where(a => !a.Unlocked && !a.Hidden && a.Percent is >= 0 and <= 100)
            .OrderByDescending(a => a.Percent).ThenBy(a => a.Name, StringComparer.Ordinal))
        {
            Add(award, "Next activity");
            if (badges.Count == limit) break;
        }
        return new(data.Total, data.Unlocked, badges, nextActivityStart);

        void Add(PlayniteAchievement award, string group)
        {
            // Resolve only the nominated occurrences using the same provider ordering as the full list.
            var identity = PlayniteAchievementEntry.IdentityFor(award);
            var occurrence = 0;
            foreach (var earlier in data.Items)
            {
                if (ReferenceEquals(earlier, award)) break;
                if (PlayniteAchievementEntry.IdentityFor(earlier) == identity) occurrence++;
            }
            var rowKey = PlayniteAchievementEntry.OccurrenceKey(PlayniteAchievementEntry.KeyFor(award), occurrence);
            var key = AwardKey(game, data, rowKey);
            if (!seen.Add(key)) return;
            var art = award.Icon is { } path && ArtworkFileContract.IsLocalPath(path)
                ? new WidgetArtworkHandle(Ids.KeyedId("icon", $"{key}/{path}")) : (WidgetArtworkHandle?)null;
            badges.Add(new(key, rowKey, group, award, art, ReferenceEquals(award, latest)));
        }
    }

    internal static string AwardKey(string game, PlayniteAchievements data, string occurrenceKey) =>
        Keys.KeyedId("award", $"{data.ConnectionId}/{data.Provider}/{game}/{occurrenceKey}");
}

public sealed partial class PlayniteLibraryWidget
{
    private static ActionSurfaceElement PlayingResumeCard(string id, string title, string? cover, PlayniteBridgeGame? game)
    {
        var copy = new List<WidgetElement> { UI.Text(title, id + ".game").Classes("playing-award-name") };
        if (game?.Developers.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) is { } developer)
            copy.Add(UI.Text(developer, id + ".developer").Classes("playing-detail-developer"));
        var genre = game?.Genres.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var year = DateOnly.TryParseExact(game?.ReleaseDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date.Year.ToString(System.Globalization.CultureInfo.CurrentCulture) : null;
        if (genre is not null || year is not null)
            copy.Add(UI.Text(string.Join(" · ", new[] { genre, year }.Where(v => v is not null)), id + ".metadata").Classes("playing-detail-metadata"));
        var mark = UI.Icon(PlayniteIcons.Library, id + ".icon", "Game library").Classes("playing-detail-icon");
        var portraitLayers = new List<WidgetElement> { mark.AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center) };
        if (!string.IsNullOrWhiteSpace(cover))
            portraitLayers.Add(UI.Artwork(new(cover), id + ".art", title, ImageFit.Contain).Classes("playing-detail-art"));
        var portrait = UI.Grid(id + ".portrait", [], [], portraitLayers.ToArray()).Classes("playing-detail-portrait");
        var face = UI.Grid(id + ".face", [GridTrack.Auto(), GridTrack.Star()], [],
            UI.Text("Resume playing", id + ".title").Classes("playing-card-title"),
            UI.Grid(id + ".body", [], [GridTrack.Auto(), GridTrack.Star()], portrait,
                UI.Stack(id + ".copy", copy.ToArray()).Classes("playing-detail-copy").InGrid(column: 1))
                .Spacing(0, 14).InGrid(row: 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center))
            .Spacing(6, 0).Classes("playing-card-face", "playing-detail-face");
        return UI.ActionSurface(id, id, $"Resume playing, {title}", ActionSurfaceOrientation.Vertical, face)
            .Classes("playing-action", "playing-current-game");
    }

    private static ActionSurfaceElement PlayingAchievementCard(string id, PlayingAwards awards, PlayingBadge? badge)
    {
        var count = $"{awards.Unlocked} of {awards.Total} unlocked";
        var award = badge?.Award;
        var title = award?.Name ?? (awards.Unlocked == 0 ? "No unlocks yet" : "Achievements");
        var group = badge?.Group ?? "Achievements";
        var rarity = award?.Percent is { } percent ? $"{percent:0.#}% of players" : count;
        var header = UI.RichText(id + ".heading",
            UI.Text(group, id + ".title").Classes("playing-card-title"),
            UI.Text("\u2002" + rarity, id + ".count").Classes("playing-award-count"))
            .Classes("playing-award-heading");
        var art = award?.Icon is { } source
            ? badge?.LocalArtwork is { } handle ? (WidgetElement)UI.Artwork(handle, id + ".art", title, ImageFit.Contain)
                : UI.Image(source, id + ".art", title, ImageFit.Contain)
            : UI.Icon(PlayniteIcons.Achievements, id + ".art", "Achievement");
        var body = UI.Grid(id + ".body", [], [GridTrack.Auto(), GridTrack.Star()],
            art.Classes("playing-lead-medal").AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center),
            UI.Stack(id + ".words", UI.Text(title, id + ".name").Classes("playing-award-name"),
                UI.Text(award?.Description ?? "", id + ".description").Classes("playing-award-description")).Classes("playing-award-words")
                .InGrid(column: 1)).Spacing(0, 14);
        var facts = award is null ? "" : PlayniteAchievementPresentation.AwardFacts(award);
        if (badge is { Latest: true, Group: "Tracked" }) facts = "Recent unlock · " + facts;
        WidgetElement footer = UI.Text(facts, id + ".facts").Classes("playing-award-facts");
        if (award is { Unlocked: false, Progress: { Maximum: > 0 } progress })
            footer = UI.Stack(id + ".footer", footer,
                UI.Progress(progress.Current, progress.Maximum, id + ".progress", title))
                .Classes("playing-award-progress");
        var face = UI.Grid(id + ".face", [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto()], [], header,
            body.InGrid(row: 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center),
            footer.InGrid(row: 2)).Spacing(4, 0)
            .Classes("playing-card-face", "playing-award-face");
        var accessible = string.Join(". ", new[] { group, title, award?.Description, facts, award?.Percent is not null ? rarity : null, count }.Where(v => !string.IsNullOrEmpty(v)));
        return UI.ActionSurface(id, id, BoundProgress(accessible, ProtocolConstants.MaximumStringLength), ActionSurfaceOrientation.Vertical, face)
            .Classes("playing-action", "playing-trophy-case");
    }

    private static ActionSurfaceElement PlayingStoryCard(string id, SteamAnnouncement article)
    {
        var story = new List<WidgetElement> {
            UI.Text($"Game updates  ·  {article.Published.ToLocalTime():MMM d}", id + ".meta").Classes("playing-card-meta"),
            UI.Text(article.Title, id + ".title").Classes("playing-headline") };
        if (article.Excerpt.Length > 0)
            story.Add(UI.Text(article.Excerpt, id + ".excerpt").Classes("playing-excerpt"));
        var copy = UI.Stack(id + ".copy", story.ToArray()).Classes("playing-story-copy");
        var body = article.Artwork is { } cover
            ? UI.Grid(id + ".face", [], [GridTrack.Star(), GridTrack.Pixel(112)], copy,
                UI.Image(cover, id + ".art", article.Title, ImageFit.Cover).Classes("playing-story-art").InGrid(column: 1))
            : UI.Grid(id + ".face", [], [], copy);
        return UI.ActionSurface(id, id, article.Title, ActionSurfaceOrientation.Vertical, body.Classes("playing-card-face"))
            .Classes("playing-action", "playing-story");
    }

    private static ActionSurfaceElement PlayingSessionCard(string id, DateTimeOffset? started, PlayniteRecentSession? last)
    {
        var rows = new List<WidgetElement> { UI.Text("Sessions", id + ".title").Classes("playing-card-title", "playing-single-heading") };
        if (started is { } time)
            rows.Add(UI.Text($"Started {time:t}", id + ".time").Classes("playing-start-time")
                .InGrid(row: 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center));
        if (last is not null)
        {
            var day = EventDay(last.StartedAt.LocalDateTime);
            var duration = PlayniteLibraryDetailsPresentation.Duration(last.Seconds);
            rows.Add(UI.Stack(id + ".previous", UI.Text("Last session", id + ".previous-label").Classes("playing-card-meta"),
                UI.Text($"{duration}  ·  {day}", id + ".previous-value").Classes("playing-session-duration"))
                .Classes("playing-session-previous").InGrid(row: started.HasValue ? 2 : 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center));
        }
        var face = UI.Grid(id + ".face", [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto()], [], rows.ToArray())
            .Classes("playing-card-face", "playing-session-face");
        return UI.ActionSurface(id, id, "Sessions", ActionSurfaceOrientation.Vertical, face).Classes("playing-action", "playing-sessions");
    }

    private static ActionSurfaceElement PlayingCaptureCard(string id, CaptureLibraryItem capture)
    {
        var time = CaptureTime(capture);
        var local = DateTimeOffset.FromUnixTimeMilliseconds(capture.CapturedAtUnixMilliseconds ?? capture.SavedAtUnixMilliseconds).LocalDateTime;
        var title = capture.Media.ContentType == "video/mp4" ? "Latest clip" : "Latest screenshot";
        var caption = UI.Grid(id + ".caption", [], [GridTrack.Star(), GridTrack.Auto()],
            UI.Text(title, id + ".title").Classes("playing-card-title"),
            UI.Text(local.Date == DateTime.Today ? local.ToString("t") : EventDay(local), id + ".time", time).Classes("playing-card-meta")
                .InGrid(column: 1).AlignInGrid(GridCellAlignment.End, GridCellAlignment.Center)).Spacing(0, 10)
            .Classes("playing-photo-caption");
        var face = UI.Grid(id + ".face", [], [],
            UI.Artwork(CaptureArtwork(capture), id + ".art", title, ImageFit.Contain).Classes("playing-photo-art"),
            caption.AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.End))
            .Classes("playing-photo-face");
        return UI.ActionSurface(id, id, $"{title}, {time}", ActionSurfaceOrientation.Vertical, face)
            .Classes("playing-action", "playing-photo");
    }
}
