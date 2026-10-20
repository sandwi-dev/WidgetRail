using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteAchievementPresentation
{
    private const string Prefix = PlayniteAchievementBrowse.Prefix;
    internal static WidgetElement Page(PlayniteDetailsExtras state, PlayniteAchievementBrowse? query, WidgetElement? list)
    {
        if (state.Achievements is not { Available: true } data)
            return UI.Text(state.AchievementsLoading ? "Loading achievements…" : state.AchievementsError ??
                "No achievement data is available. Sync this game in PlayniteAchievements or SuccessStory, then refresh here.", Prefix + "achievements.unavailable")
                .Classes("playnite-library-details-copy");
        var summary = UI.Row(Prefix + "achievements.summary-row",
            UI.Text($"{data.Unlocked} / {data.Total} unlocked · {(data.Total == 0 ? 0 : 100.0 * data.Unlocked / data.Total):0}%", Prefix + "achievements.summary")
                .Classes("playnite-library-achievement-summary"),
            UI.Progress(data.Unlocked, Math.Max(1, data.Total), Prefix + "achievements.progress").Classes("playnite-library-achievement-total-progress"),
            UI.Text(state.AchievementsLoading ? "Refreshing…" : state.AchievementsError is not null ? "Refresh failed · Try again" : data.ProviderName, Prefix + "achievements.provider").Classes("playnite-library-details-meta"))
            .Classes("playnite-library-achievement-summary-row");
        var filters = Enum.GetValues<AchievementFilter>().Select(filter => (WidgetElement)UI.Button(filter.ToString(),
            Prefix + "achievement.filter." + filter, Prefix + "achievement.filter." + filter)
            .Selected(state.AchievementFilter == filter).Classes(state.AchievementFilter == filter
                ? ["playnite-library-chip", "playnite-library-filter-active"] : ["playnite-library-chip"])).ToArray();
        var sorts = Enum.GetValues<AchievementSort>().Where(sort => sort != AchievementSort.Progress || state.AchievementSort == sort ||
            query?.All.Any(entry => !entry.Secret && entry.Item.Progress is not null) == true).Select(sort =>
            new SelectOption(sort.ToString(), sort switch { AchievementSort.Recent => "Recent unlocks", AchievementSort.Progress => "Closest to completion", _ => sort.ToString() },
                Prefix + "achievement.sort." + sort, IsSelected: state.AchievementSort == sort)).ToArray();
        var toolbar = UI.Row(Prefix + "achievements.tools",
            UI.TextEntry(state.AchievementSearch, "Search achievements", Prefix + "achievement.search", Prefix + "achievement.search", 128)
                .Classes("playnite-library-achievement-search"),
            UI.Row(Prefix + "achievement.filters", filters).Classes("playnite-library-achievement-filters"),
            UI.Select("Sort", sorts, Prefix + "achievement.sort").Classes("playnite-library-achievement-sort"))
            .Classes("playnite-library-achievement-tools");
        WidgetElement results = data.Total == 0 ? UI.Text(data.ProviderName + " reports no achievements for this game.", Prefix + "achievements.empty") :
            query?.Rows.Count == 0 ? UI.Text("No achievements match these filters.", Prefix + "achievements.no-matches") :
            list ?? UI.Text("Loading achievements…", Prefix + "achievements.loading");
        return UI.Grid(Prefix + "achievements.page", [GridTrack.Auto(), GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            summary, toolbar.InGrid(row: 1), results.InGrid(row: 2)).Spacing(8, 0).Classes("playnite-library-achievement-page");
    }

    internal static WidgetElement Row(PlayniteAchievementEntry entry, string id)
    {
        var item = entry.Item;
        var children = new List<WidgetElement>();
        if (Icon(entry, id + ".icon") is { } icon) children.Add(icon.Classes("playnite-library-achievement-row-icon"));
        else children.Add(UI.Text(entry.Secret ? "?" : "◇", id + ".icon")
            .Classes("playnite-library-achievement-row-icon"));
        var meta = item.Unlocked ? "Unlocked" : "Locked";
        if (item.Percent is { } rarity) meta += $" · {rarity:0.#}% of players";
        if (item.Unlocked && item.UnlockedAt is { } date) meta += " · " + date.ToLocalTime().ToString("d");
        children.Add(UI.Stack(id + ".text",
            UI.Text(entry.Name, id + ".name").Classes("playnite-library-achievement-name"),
            UI.Text(meta, id + ".meta").Classes("playnite-library-achievement-meta"))
            .Classes("playnite-library-achievement-row-text"));
        if (!entry.Secret && !item.Unlocked && item.Progress is { } progress)
            children.Add(UI.Text($"{progress.Current:N0} / {progress.Maximum:N0}", id + ".progress").Classes("playnite-library-achievement-progress-label"));
        else if (item.Unlocked) children.Add(UI.Icon(WidgetGlyph.Check, id + ".check", "Unlocked").Classes("playnite-library-positive"));
        var action = Prefix + "achievement.open." + entry.Key;
        return UI.ActionSurface(action, id, entry.Name + ". " + meta + ". Open details.", ActionSurfaceOrientation.Horizontal, children.ToArray())
            .Classes("playnite-library-achievement-row").PresentOnFocus(entry.Key);
    }

    internal static WidgetElement Detail(PlayniteAchievementEntry entry, bool full)
    {
        var item = entry.Item;
        var id = Prefix + "achievement.detail";
        var heading = new List<WidgetElement>();
        if (Icon(entry, id + ".icon") is { } icon) heading.Add(icon.Classes("playnite-library-achievement-icon"));
        heading.Add(UI.Stack(id + ".heading-text",
            UI.Text(entry.Name, id + ".title").Classes(full ? "playnite-library-achievement-full-title" : "playnite-library-preview-title"),
            UI.Text(item.Unlocked ? "Unlocked" : "Locked", id + ".state")
                .Classes(item.Unlocked ? ["playnite-library-preview-state", "playnite-library-positive"] : ["playnite-library-preview-state"]))
            .Classes("playnite-library-achievement-heading-text"));
        var content = new List<WidgetElement>
        {
            UI.Row(id + ".heading", heading.ToArray()).Classes("playnite-library-achievement-heading"),
            UI.Text(entry.Secret ? "The description and artwork are hidden." : item.Description, id + ".description")
                .Classes(full ? "playnite-library-achievement-full-description" : "playnite-library-preview-description"),
        };
        if (entry.Secret)
            content.Add(full ? UI.Button("Reveal achievement", Prefix + "achievement.reveal." + entry.Key, id + ".reveal") :
                UI.Text("Select to view or reveal this achievement.", id + ".hint").Classes("playnite-library-details-meta"));
        else
        {
            if (item.UnlockedAt is { } date && item.Unlocked) content.Add(UI.Text("Unlocked " + date.ToLocalTime().ToString("g"), id + ".date").Classes("playnite-library-details-meta"));
            if (item.Percent is { } percent) content.Add(UI.Text($"{percent:0.#}% of players", id + ".rarity").Classes("playnite-library-details-meta"));
            if (item.GamerScore is { } score) content.Add(UI.Text($"{score} points", id + ".points").Classes("playnite-library-details-meta"));
            if (!item.Unlocked && item.Progress is { } progress)
            {
                content.Add(UI.Text($"{progress.Current:N0} of {progress.Maximum:N0}", id + ".progress-text").Classes("playnite-library-details-meta"));
                content.Add(UI.Progress(progress.Current, progress.Maximum, id + ".progress"));
            }
        }
        return UI.Stack(id, content.ToArray()).Classes("playnite-library-rich-preview");
    }

    private static WidgetElement? Icon(PlayniteAchievementEntry entry, string id) => entry.Secret || entry.Item.Icon is not { } icon ? null :
        ArtworkFileContract.IsLocalPath(icon) ? UI.Artwork(PlayniteAchievementBrowse.IconHandle(entry.Key), id, "Achievement icon", ImageFit.Contain) :
        UI.Image(icon, id, "Achievement icon", ImageFit.Contain);
}
