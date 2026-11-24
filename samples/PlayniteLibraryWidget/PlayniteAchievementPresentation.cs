using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteAchievementPresentation
{
    private const string Prefix = PlayniteAchievementBrowse.Prefix;
    internal static WidgetElement Page(PlayniteDetailsExtras state, PlayniteAchievementBrowse? query, WidgetElement? list, WidgetElement? install = null,
        bool canSyncGame = true)
    {
        if (state.Achievements is not { Available: true } data)
        {
            if (!state.AchievementsLoading && state.AchievementsError is { } error)
                return PlayniteLibraryDetailsPresentation.ReadError(Prefix + "achievements.error", error, PlayniteLibraryDetailsPresentation.RetryAchievementsAction);
            if (install is not null)
                return UI.Stack(Prefix + "achievements.setup", UI.Text("Playnite Achievements tracks this game’s unlocks.",
                    Prefix + "achievements.unavailable").Classes("playnite-library-details-copy"), install);
            return UI.Text(state.AchievementsLoading ? "Loading achievements…" :
                "No achievement data yet. Set up achievement sources and sync this game in Playnite.", Prefix + "achievements.unavailable")
                .Classes("playnite-library-details-copy");
        }
        var summary = UI.Row(Prefix + "achievements.summary-row",
            UI.Text($"{data.Unlocked} / {data.Total} unlocked · {(data.Total == 0 ? 0 : 100.0 * data.Unlocked / data.Total):0}%", Prefix + "achievements.summary")
                .Classes("playnite-library-achievement-summary"),
            UI.Progress(data.Unlocked, Math.Max(1, data.Total), Prefix + "achievements.progress").Classes("playnite-library-achievement-total-progress"),
            UI.Text(state.AchievementsLoading ? "Loading…" : data.ProviderName, Prefix + "achievements.provider").Classes("playnite-library-details-meta"))
            .Classes("playnite-library-achievement-summary-row");
        var filters = Enum.GetValues<AchievementFilter>().Select(filter => new SelectOption(filter.ToString(), filter.ToString(),
            Prefix + "achievement.filter." + filter, IsSelected: state.AchievementFilter == filter)).ToArray();
        var sorts = Enum.GetValues<AchievementSort>().Where(sort => sort != AchievementSort.Progress || state.AchievementSort == sort ||
            query?.All.Any(entry => !entry.Secret && entry.Item.Progress is not null) == true).Select(sort =>
            new SelectOption(sort.ToString(), sort switch { AchievementSort.Progress => "Closest to completion", _ => sort.ToString() },
                Prefix + "achievement.sort." + sort, IsSelected: state.AchievementSort == sort)).ToArray();
        var search = UI.TextEntry(state.AchievementSearch, "Search achievements", PlayniteAchievementBrowse.SearchId, PlayniteAchievementBrowse.SearchId, 128);
        if (query is not null) search = search.Suggest(query.SuggestionSource);
        var toolbar = UI.Row(Prefix + "achievements.tools",
            UI.Row(PlayniteAchievementBrowse.SearchGroupId,
                UI.ControllerGlyph(ControllerButton.LeftStick, Prefix + "achievement.search.hint"),
                search.Classes("playnite-library-achievement-search"))
                .RememberChildFocus(PlayniteAchievementBrowse.SearchId).Classes("playnite-library-achievement-search-group"),
            UI.Button("Clear", Prefix + "achievement.search.clear", Prefix + "achievement.search.clear")
                .Disabled(string.IsNullOrEmpty(state.AchievementSearch)).Classes("playnite-library-search-clear"),
            UI.Select("Show", filters, Prefix + "achievement.filters", "Filter achievements").Classes("playnite-library-achievement-filter"),
            UI.Select("Sort", sorts, Prefix + "achievement.sort").Classes("playnite-library-achievement-sort"))
            .Classes("playnite-library-achievement-tools");
        WidgetElement results = data.Total == 0 ? UI.Text(data.ProviderName + " reports no achievements for this game.", Prefix + "achievements.empty") :
            query?.Rows.Count == 0 ? UI.Text(state.AchievementFilter == AchievementFilter.All ? "No achievements match this search." :
                "No " + state.AchievementFilter.ToString().ToLowerInvariant() + " achievements match. Select All to search every achievement.", Prefix + "achievements.no-matches") :
            list ?? UI.Text("Loading achievements…", Prefix + "achievements.loading");
        var providerTools = new List<WidgetElement>();
        if (data.CanRefresh)
            providerTools.Add(UI.Button(state.AchievementRefreshBusy ? "Syncing…" : "Sync this game",
                PlayniteLibraryDetailsPresentation.SyncGameAction, Prefix + "achievement.sync")
                .Disabled(!canSyncGame || state.AchievementRefreshBusy).Classes("playnite-library-control"));
        if (state.ProgressPinSelectionId is { } selection)
        {
            providerTools.Add(UI.Button("Adjust progress pin", Prefix + "achievement.manage." + selection, Prefix + "achievement.manage"));
            providerTools.Add(UI.Button("Unpin progress", Prefix + "achievement.unpin." + selection, Prefix + "achievement.unpin"));
        }
        var rows = new List<GridTrackDefinition> { GridTrack.Auto() };
        var content = new List<WidgetElement> { summary };
        if (providerTools.Count > 0)
        {
            content.Add(UI.ResponsiveGrid(Prefix + "achievements.provider-tools", 170, 4, providerTools.ToArray()).InGrid(row: rows.Count));
            rows.Add(GridTrack.Auto());
        }
        if (!state.AchievementsLoading && state.AchievementsError is { } readError)
        {
            content.Add(PlayniteLibraryDetailsPresentation.ReadError(Prefix + "achievements.error", readError,
                PlayniteLibraryDetailsPresentation.RetryAchievementsAction).InGrid(row: rows.Count));
            rows.Add(GridTrack.Auto());
        }
        if (state.AchievementRefreshMessage is { } message)
        {
            content.Add(UI.Text(message, Prefix + "achievements.freshness").Classes("playnite-library-details-meta").InGrid(row: rows.Count));
            rows.Add(GridTrack.Auto());
        }
        content.Add(toolbar.InGrid(row: rows.Count)); rows.Add(GridTrack.Auto());
        content.Add(results.InGrid(row: rows.Count)); rows.Add(GridTrack.Star());
        var page = UI.Grid(Prefix + "achievements.page", rows.ToArray(), [GridTrack.Star()], content.ToArray()).Spacing(8, 0).Classes("playnite-library-achievement-page")
            .Shortcut(ControllerButton.LeftStick, PlayniteAchievementBrowse.SearchFocusAction, "Search achievements");
        return data.CanRefresh && canSyncGame && !state.AchievementRefreshBusy
            ? page.Shortcut(ControllerButton.Y, PlayniteLibraryDetailsPresentation.SyncGameAction, "Sync this game")
            : page;
    }

    internal static bool CanTrack(PlayniteAchievement item) =>
        !item.Unlocked && !item.Hidden && item.Id is { Length: > 0 } && (item.Progress is null || CanPinProgress(item));

    internal static bool CanPinProgress(PlayniteAchievement item) =>
        !item.Unlocked && !item.Hidden && item.Id is { Length: > 0 } &&
        item.Progress is { Current: >= 0, Maximum: > 0 } progress && progress.Current < progress.Maximum;

    internal static string AwardFacts(PlayniteAchievement item)
    {
        var facts = new List<string>();
        if (item.UnlockedAt is { } when && item.Unlocked) facts.Add(when.ToLocalTime().ToString("g"));
        if (!item.Unlocked && item.Progress is { } progress) facts.Add($"{progress.Current:N0} / {progress.Maximum:N0}");
        if (item.GamerScore is { } points) facts.Add($"{points} points");
        if (!string.IsNullOrWhiteSpace(item.TrophyType)) facts.Add(item.TrophyType);
        if (!string.IsNullOrWhiteSpace(item.Category) && !string.Equals(item.Category, "Default", StringComparison.OrdinalIgnoreCase)) facts.Add(item.Category);
        if (item.IsGoal) facts.Add("Goal");
        if (item.IsCapstone) facts.Add("Capstone");
        return string.Join(" · ", facts);
    }

    internal static WidgetElement Row(PlayniteAchievementEntry entry, string id, bool capturesAvailable = false, bool trackableSource = true, bool tracked = false)
    {
        var item = entry.Item;
        var children = new List<WidgetElement>();
        if (Icon(entry, id + ".icon") is { } icon) children.Add(icon.Classes("playnite-library-achievement-row-icon"));
        else children.Add(entry.Secret ? UI.Text("?", id + ".icon").Classes("playnite-library-achievement-row-icon") :
            UI.Icon(PlayniteIcons.Achievements, id + ".icon", "Achievement").Classes("playnite-library-achievement-row-icon"));
        var meta = item.Unlocked ? "Unlocked" : "Locked";
        if (tracked) meta += " · Tracked";
        if (item.Percent is { } rarity) meta += $" · {rarity:0.#}% of players";
        if (item.Unlocked && item.UnlockedAt is { } date) meta += " · " + date.ToLocalTime().ToString("g");
        if (item.GamerScore is { } points) meta += $" · {points} points";
        if (!entry.Secret && item.TrophyType is { Length: > 0 } trophy) meta += " · " + trophy;
        if (!entry.Secret && item.IsGoal) meta += " · Goal";
        var heading = new List<WidgetElement>
        {
            UI.Text(entry.Name, id + ".name").Classes("playnite-library-achievement-name"),
            UI.Text(item.Unlocked ? " · Unlocked" : " · Locked", id + ".meta")
                .Classes("playnite-library-achievement-meta", item.Unlocked ? "playnite-ink-progress" : "playnite-ink-muted"),
        };
        if (tracked) heading.Add(UI.Text(" · Tracked", id + ".tracked").Classes("playnite-library-achievement-meta", "playnite-ink-progress"));
        if (item.Percent is { } percent) heading.Add(UI.Text($" · {percent:0.#}% of players", id + ".rarity")
            .Classes("playnite-library-achievement-meta", "playnite-ink-reward"));
        if (item.Unlocked && item.UnlockedAt is { } unlockedAt) heading.Add(UI.Text(" · " + unlockedAt.ToLocalTime().ToString("g"), id + ".date")
            .Classes("playnite-library-achievement-meta", "playnite-ink-time"));
        if (item.GamerScore is { } gamerScore) heading.Add(UI.Text($" · {gamerScore} points", id + ".points")
            .Classes("playnite-library-achievement-meta", "playnite-ink-reward"));
        if (!entry.Secret && item.TrophyType is { Length: > 0 } trophyType) heading.Add(UI.Text(" · " + trophyType, id + ".trophy")
            .Classes("playnite-library-achievement-meta", "playnite-ink-reward"));
        if (!entry.Secret && item.IsGoal) heading.Add(UI.Text(" · Goal", id + ".goal")
            .Classes("playnite-library-achievement-meta", "playnite-ink-source"));
        if (!entry.Secret && !string.IsNullOrWhiteSpace(item.Category))
            heading.Add(UI.Text(" · " + item.Category, id + ".category").Classes("playnite-library-achievement-meta"));
        if (!entry.Secret && item.IsCapstone)
            heading.Add(UI.Text(" · Capstone", id + ".capstone").Classes("playnite-library-achievement-meta"));
        var text = new List<WidgetElement>
        {
            UI.RichText(id + ".heading", heading.ToArray()).Classes("playnite-library-achievement-heading"),
            UI.Text(entry.Secret ? "Reveal this achievement to see its description and artwork." : item.Description, id + ".description")
                .Classes("playnite-library-achievement-description"),
        };
        if (!entry.Secret && !item.Unlocked && item.Progress is { } progress)
        {
            text.Add(UI.Row(id + ".progress-row",
                UI.Text($"{progress.Current:N0} / {progress.Maximum:N0}", id + ".progress-label")
                    .Classes("playnite-library-achievement-progress-label"),
                UI.Progress(progress.Current, progress.Maximum, id + ".progress")
                    .Classes("playnite-library-achievement-total-progress"))
                .Classes("playnite-library-achievement-summary-row"));
        }
        children.Add(UI.Stack(id + ".text", text.ToArray()).Classes("playnite-library-achievement-row-text"));
        if (item.Unlocked) children.Add(UI.Icon(WidgetGlyph.Check, id + ".check", "Unlocked").Classes("playnite-library-positive"));
        if (entry.Secret) children.Add(UI.ControllerHint(ControllerButton.A, "Reveal", id + ".reveal-hint"));
        var label = entry.Name + ". " + meta + ". " + (entry.Secret ? "Reveal achievement." : item.Description);
        // The visible description remains complete; the accessible row name has the protocol's bound.
        label = label[..Math.Min(label.Length, ProtocolConstants.MaximumStringLength)];
        var surface = (entry.Secret
            ? UI.ActionSurface(Prefix + "achievement.reveal." + entry.Key, id, label, ActionSurfaceOrientation.Horizontal, children.ToArray())
            : UI.ReadingSurface(id, label, ActionSurfaceOrientation.Horizontal, children.ToArray()));
        var actions = new List<WidgetContextAction>();
        if (tracked) actions.Add(new(Prefix + "achievement.untrack." + entry.Key, "Stop tracking"));
        else if (trackableSource && entry.UniqueId && CanTrack(item))
            actions.Add(new(Prefix + "achievement.track." + entry.Key, "Track achievement"));
        if (trackableSource && entry.UniqueId && CanPinProgress(item))
            actions.Add(new(Prefix + "achievement.pin." + entry.Key, "Pin progress"));
        if (!entry.Secret && capturesAvailable && item.Unlocked && item.Id is not null)
            actions.Add(new(Prefix + "achievement.captures." + entry.Key, "View captures"));
        if (actions.Count > 0) surface = surface with
        {
            ContextMenuButton = ControllerButton.X,
            ContextActions = actions,
        };
        return surface.Classes("playnite-library-achievement-row");
    }

    private static WidgetElement? Icon(PlayniteAchievementEntry entry, string id) => entry.Secret || entry.Item.Icon is not { } icon ? null :
        ArtworkFileContract.IsLocalPath(icon) ? UI.Artwork(PlayniteAchievementBrowse.IconHandle(entry.Key), id, "Achievement icon", ImageFit.Contain) :
        UI.Image(icon, id, "Achievement icon", ImageFit.Contain);
}
