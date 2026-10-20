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
        var search = UI.TextEntry(state.AchievementSearch, "Search achievements", PlayniteAchievementBrowse.SearchId, PlayniteAchievementBrowse.SearchId, 128);
        if (query is not null) search = search.Suggest(query.SuggestionSource);
        var toolbar = UI.Row(Prefix + "achievements.tools",
            UI.Row(PlayniteAchievementBrowse.SearchGroupId,
                search.Classes("playnite-library-achievement-search"),
                UI.ControllerGlyph(ControllerButton.LeftStick, Prefix + "achievement.search.hint"))
                .RememberChildFocus(PlayniteAchievementBrowse.SearchId).Classes("playnite-library-achievement-search-group"),
            UI.Row(Prefix + "achievement.filters", filters).Classes("playnite-library-achievement-filters"),
            UI.Select("Sort", sorts, Prefix + "achievement.sort").Classes("playnite-library-achievement-sort"))
            .Classes("playnite-library-achievement-tools");
        WidgetElement results = data.Total == 0 ? UI.Text(data.ProviderName + " reports no achievements for this game.", Prefix + "achievements.empty") :
            query?.Rows.Count == 0 ? UI.Text(state.AchievementFilter == AchievementFilter.All ? "No achievements match this search." :
                "No " + state.AchievementFilter.ToString().ToLowerInvariant() + " achievements match. Select All to search every achievement.", Prefix + "achievements.no-matches") :
            list ?? UI.Text("Loading achievements…", Prefix + "achievements.loading");
        return UI.Grid(Prefix + "achievements.page", [GridTrack.Auto(), GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            summary, toolbar.InGrid(row: 1), results.InGrid(row: 2)).Spacing(8, 0).Classes("playnite-library-achievement-page")
            .Shortcut(ControllerButton.LeftStick, PlayniteAchievementBrowse.SearchFocusAction, "Search achievements");
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
        if (item.Unlocked && item.UnlockedAt is { } date) meta += " · " + date.ToLocalTime().ToString("g");
        if (item.GamerScore is { } points) meta += $" · {points} points";
        var text = new List<WidgetElement>
        {
            UI.Text(entry.Name, id + ".name").Classes("playnite-library-achievement-name"),
            UI.Text(meta, id + ".meta").Classes("playnite-library-achievement-meta"),
            UI.Text(entry.Secret ? "Reveal this achievement to see its description and artwork." : item.Description, id + ".description")
                .Classes("playnite-library-achievement-description"),
        };
        if (!entry.Secret && !item.Unlocked && item.Progress is { } progress)
        {
            text.Add(UI.Text($"{progress.Current:N0} / {progress.Maximum:N0}", id + ".progress-label").Classes("playnite-library-achievement-progress-label"));
            text.Add(UI.Progress(progress.Current, progress.Maximum, id + ".progress"));
        }
        children.Add(UI.Stack(id + ".text", text.ToArray()).Classes("playnite-library-achievement-row-text"));
        if (item.Unlocked) children.Add(UI.Icon(WidgetGlyph.Check, id + ".check", "Unlocked").Classes("playnite-library-positive"));
        if (entry.Secret) children.Add(UI.ControllerHint(ControllerButton.A, "Reveal", id + ".reveal-hint"));
        var label = entry.Name + ". " + meta + ". " + (entry.Secret ? "Reveal achievement." : item.Description);
        // The visible description remains complete; the accessible row name has the protocol's bound.
        label = label[..Math.Min(label.Length, ProtocolConstants.MaximumStringLength)];
        return (entry.Secret
            ? UI.ActionSurface(Prefix + "achievement.reveal." + entry.Key, id, label, ActionSurfaceOrientation.Horizontal, children.ToArray())
            : UI.ReadingSurface(id, label, ActionSurfaceOrientation.Horizontal, children.ToArray()))
            .Classes("playnite-library-achievement-row");
    }

    private static WidgetElement? Icon(PlayniteAchievementEntry entry, string id) => entry.Secret || entry.Item.Icon is not { } icon ? null :
        ArtworkFileContract.IsLocalPath(icon) ? UI.Artwork(PlayniteAchievementBrowse.IconHandle(entry.Key), id, "Achievement icon", ImageFit.Contain) :
        UI.Image(icon, id, "Achievement icon", ImageFit.Contain);
}
