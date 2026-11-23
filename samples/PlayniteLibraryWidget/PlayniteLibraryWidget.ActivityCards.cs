using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private static WidgetArtworkHandle StoryIcon(string key) => new("playnite.story." + key);
    private static string EventDay(DateTime local) => local.Date == DateTime.Today ? "Today" :
        local.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : local.ToString(local.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy");

    private static WidgetElement HomeActivityCard(HomePost post, WidgetIndexedItemContext ids)
    {
        var local = post.When.ToLocalTime();
        var date = $"{EventDay(local.Date)}, {local:t}";
        string title;
        WidgetElement panel;
        if (post.Unlock is { } earned)
        {
            var surface = HomeUnlockTile(earned, ids.Id("award"));
            return post.Hero is { } hero ? surface.FocusBackground(new(hero)) : surface;
        }
        if (post.Session is not null)
        {
            var duration = PlayniteLibraryDetailsPresentation.Duration(post.PlayedSeconds);
            title = post.Count == 1 ? $"Played for {duration}" : $"{duration} across {post.Count} sessions";
            panel = SessionPanel(ids, post, duration);
        }
        else
        {
            title = CaptureSeriesTitle(post.Captures);
            panel = UI.Grid(ids.Id("layout"), [], [GridTrack.Star(1.15), GridTrack.Star()],
                PhotoSpread(ids, post.Captures, "home-digest-photos"), GameCredit(title).InGrid(column: 1))
                .Classes("home-digest-layout");
        }
        var material = panel.AddClasses("home-activity-tile", "story-card-material");
        var card = UI.ActionSurface(HomeActivityList + ".open", ids.Id("post"), $"{title}, {post.Name}, {date}",
            ActionSurfaceOrientation.Vertical, material).Classes("story-card");
        var backdrop = post.Hero ?? (post.Captures.Count > 0 ? CaptureArtwork(post.Captures[0]).Value : null);
        return backdrop is null ? card : card.FocusBackground(new(backdrop));

        WidgetElement GameCredit(string detail) => UI.Stack(ids.Id("credit"),
            UI.Text(post.Name, ids.Id("game")).Classes("home-digest-game"),
            UI.Text(detail, ids.Id("detail")).Classes("story-credit-time")).Classes("home-digest-credit");
    }

    private static ActionSurfaceElement HomeUnlockTile(PlayniteRecentAchievement earned, string id)
    {
        var badgeLayers = new List<WidgetElement> { UI.Icon(PlayniteIcons.Achievements, id + ".trophy", "Achievement")
            .Classes("story-medal").AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center) };
        if (earned.Icon is { Length: > 0 } path)
        {
            var art = ArtworkFileContract.IsLocalPath(path)
                ? (WidgetElement)UI.Artwork(StoryIcon(RecentKey(earned)), id + ".art", earned.Name, ImageFit.Contain)
                : UI.Image(path, id + ".art", earned.Name, ImageFit.Contain);
            badgeLayers.Add(art.Classes("story-insignia"));
        }
        var detail = earned.UnlockedAt.ToLocalTime().ToString("t");
        if (earned.Percent is { } rarity) detail += $"   ·   {rarity:0.#}% of players";
        var copy = new List<WidgetElement>
        {
            UI.Text(earned.Name, id + ".name").Classes("home-unlock-name"),
        };
        if (!string.IsNullOrWhiteSpace(earned.Description))
            copy.Add(UI.Text(earned.Description, id + ".condition").Classes("home-unlock-condition")
                .InGrid(row: 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Start));
        var credit = UI.Grid(id + ".credit", [], [GridTrack.Star(), GridTrack.Auto()],
            UI.Text(earned.GameName, id + ".game").Classes("playnite-hub-recent-meta", "playnite-ink-source"),
            UI.Text(detail, id + ".time").Classes("playnite-hub-recent-meta", "playnite-ink-time").InGrid(column: 1))
            .Spacing(0, 10);
        var panel = UI.Grid(id + ".panel", [GridTrack.Star(), GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star()],
            UI.Grid(id + ".badge", [], [], badgeLayers.ToArray()).Classes("story-badge")
                .AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Start),
            UI.Grid(id + ".copy", [GridTrack.Auto(), GridTrack.Star()], [], copy.ToArray()).Spacing(4, 0).InGrid(column: 1),
            credit.InGrid(row: 1, columnSpan: 2))
            .Spacing(6, 14).Classes("home-activity-tile", "home-unlock-panel");
        return UI.ActionSurface(HomeActivityList + ".open", id, $"Unlocked {earned.Name}, {earned.GameName}, {detail}",
            ActionSurfaceOrientation.Vertical, panel).Classes("home-unlock-card");
    }

    private static WidgetElement AwardPanel(WidgetIndexedItemContext ids, string slot, string name, string? description,
        double? percent, string? iconPath, WidgetArtworkHandle iconHandle)
    {
        string Id(string suffix) => ids.Id(slot + "-" + suffix);
        var badgeLayers = new List<WidgetElement> { UI.Icon(PlayniteIcons.Achievements, Id("trophy"), "Achievement unlocked")
            .Classes("story-medal").AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center) };
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var icon = ArtworkFileContract.IsLocalPath(iconPath) ? (WidgetElement)UI.Artwork(iconHandle, Id("art"), name, ImageFit.Contain) :
                UI.Image(iconPath, Id("art"), name, ImageFit.Contain);
            badgeLayers.Add(icon.Classes("story-insignia").InGrid());
        }
        var badge = UI.Grid(Id("badge"), [], [], badgeLayers.ToArray()).Classes("story-badge")
            .AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center);
        var words = new List<WidgetElement> {
            UI.Text("Achievement unlocked", Id("kind")).Classes("story-gold-label"),
            UI.Text(name, Id("title")).Classes("story-award-title"),
        };
        if (!string.IsNullOrWhiteSpace(description)) words.Add(UI.Text(description, Id("description")).Classes("story-description"));
        var fields = new List<WidgetElement> { badge, UI.Stack(Id("words"), words.ToArray()).Classes("story-words").InGrid(column: 1) };
        if (percent is { } rarity)
            fields.Add(UI.Grid(Id("rarity"), [GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star()],
                UI.Text($"{rarity:0.#}% of players", Id("percent")).Classes("story-rarity-label"),
                UI.Progress(rarity, 100, Id("meter"), "Global unlock rate").InGrid(column: 1)
                    .AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center)).Spacing(0, 12)
                .Classes("story-rarity").InGrid(row: 1, columnSpan: 2));
        return UI.Grid(Id("panel"), [GridTrack.Auto(), GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star()], fields.ToArray())
            .Spacing(6, 14).Classes("story-award-panel");
    }

    private static WidgetElement SessionPanel(WidgetIndexedItemContext ids, HomePost post, string duration)
    {
        var session = post.Session!;
        var last = post.Count == 1 ? SessionClock(post.When, session.Seconds) : $"Latest start · {post.When.ToLocalTime():t}";
        var count = post.Count == 1 ? "1 session" : $"{post.Count} sessions";
        var game = UI.Stack(ids.Id("session-copy"),
            UI.Text(post.Name, ids.Id("game")).Classes("home-digest-game"),
            UI.Text($"{duration} · {count}", ids.Id("duration")).Classes("story-time-label"),
            UI.Text(last, ids.Id("clock")).Classes("story-clock")).Classes("home-digest-session-copy");
        var scene = post.Hero is { } hero ? UI.Artwork(new(hero), ids.Id("scene"), post.Name, ImageFit.Cover).Classes("story-scene")
            : null;
        return scene is null
            ? UI.Grid(ids.Id("session"), [], [], game).Classes("home-digest-session")
            : UI.Grid(ids.Id("session"), [], [GridTrack.Star(2), GridTrack.Star()], game,
                scene.InGrid(column: 1)).Classes("home-digest-session");
    }

    private static string SessionClock(DateTimeOffset start, long seconds)
    {
        var from = start.ToLocalTime();
        if (seconds <= 0 || seconds > (DateTimeOffset.MaxValue.UtcTicks - start.UtcTicks) / TimeSpan.TicksPerSecond)
            return from.ToString("t");
        var to = start.ToUniversalTime().AddSeconds(seconds).ToLocalTime();
        return to.Date - from.Date <= TimeSpan.FromDays(1) ? $"{from:t} – {to:t}" : $"{from:t} – {to:MMM d}, {to:t}";
    }

    private static string CaptureSeriesTitle(IReadOnlyList<CaptureLibraryItem> captures)
    {
        if (captures.Count == 1) return captures[0].Title ?? CaptureType(captures[0]);
        var videos = captures.Count(item => item.Media.ContentType == "video/mp4");
        return videos == 0 ? $"{captures.Count} screenshots" : videos == captures.Count ? $"{videos} clips" : $"{captures.Count} captures";
    }

    private static WidgetElement PhotoSpread(WidgetIndexedItemContext ids, IReadOnlyList<CaptureLibraryItem> captures, string style)
    {
        var tracks = captures.Count == 1 ? new[] { GridTrack.Star() } : [GridTrack.Star(1.6), GridTrack.Star()];
        var images = new List<WidgetElement>();
        for (var i = 0; i < tracks.Length; i++)
        {
            var item = captures[i];
            images.Add(UI.Artwork(CaptureArtwork(item), ids.Id("photo" + i), CaptureType(item), ImageFit.Cover)
                .Classes("story-photo").InGrid(column: i));
            var extra = i == tracks.Length - 1 ? captures.Count - tracks.Length : 0;
            var label = extra > 0 ? $"+{extra}" : item.Media.ContentType == "video/mp4" ? PlayniteLibraryDetailsPresentation.Duration((long)item.Media.DurationSeconds) : null;
            if (label is not null) images.Add(UI.Text(label, ids.Id("photo-note" + i)).Classes("story-photo-note")
                .InGrid(column: i).AlignInGrid(GridCellAlignment.End, GridCellAlignment.End));
        }
        return UI.Grid(ids.Id("photos"), [GridTrack.Star()], tracks, images.ToArray()).Spacing(0, 3).Classes(style);
    }
}
