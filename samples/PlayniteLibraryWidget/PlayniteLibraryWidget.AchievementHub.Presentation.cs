using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private WidgetView RenderAchievementHub(PlayniteLibraryRenderState local)
    {
        var snapshot = achievementHub.Snapshot; var data = snapshot.Value; var controls = local.AchievementHub;
        var busy = snapshot.Status is WidgetResourceStatus.Loading or WidgetResourceStatus.Refreshing;
        var header = UI.Row(Hub + ".heading",
            PlayniteLibraryPresentation.NavigationTabs(PlayniteLibraryRoute.Achievements),
            UI.Row(Hub + ".utilities",
                UI.Button(busy ? "Updating…" : "Refresh", Hub + ".refresh", Hub + ".refresh").Disabled(busy)
                    .Classes("playnite-library-control")).Classes("playnite-library-home-utilities").VisibleWhen(ResponsiveVisibility.ExpandedOnly))
            .Classes("playnite-library-home-actions");
        WidgetElement body;
        if (data is not { Availability: "ready" } || snapshot.Error is not null && data is null)
        {
            var availability = data?.Availability;
            var title = snapshot.Error is not null ? "Achievements unavailable" : busy || snapshot.Status == WidgetResourceStatus.NotLoaded ? "Loading achievements…" : availability switch
            {
                "missing" => "Add PlayniteAchievements",
                "unsupported" => "Update PlayniteAchievements",
                "update-required" => "Update WidgetRail for Playnite",
                _ => "Achievements unavailable",
            };
            var description = snapshot.Error?.Message ?? (busy ? "Reading your cached progress from Playnite." : availability switch
            {
                "missing" => "Install or enable PlayniteAchievements in Playnite’s Add-ons menu, then return here and refresh. Home and Library are still available.",
                "unsupported" => "This version of the add-on does not expose its achievement overview. Update it in Playnite and refresh this page.",
                "update-required" => "The installed WidgetRail plugin does not support the achievement overview yet. Open Playnite connection to update it.",
                _ => "Open Playnite and check that PlayniteAchievements is enabled, then refresh.",
            });
            body = UI.Stack(Hub + ".empty", UI.Icon(PlayniteIcons.Achievements, Hub + ".empty.icon", "Achievements").Classes("playnite-hub-empty-icon"),
                UI.Text(title, Hub + ".empty.title").Classes("playnite-hub-title"),
                UI.Text(description, Hub + ".empty.description").Classes("playnite-hub-empty-description"),
                UI.Button("Playnite connection", PlayniteOpenActionId, Hub + ".connection").Classes("playnite-library-control"),
                UI.Button("Refresh", Hub + ".refresh", Hub + ".retry").Disabled(busy).VisibleWhen(ResponsiveVisibility.CompactOnly))
                .Classes("playnite-hub-empty");
        }
        else
        {
            PublishAchievementHub(data, controls);
            var summary = UI.ResponsiveGrid(Hub + ".summary", 130, 3,
                HubStat("unlocked", data.Unlocked.ToString("N0"), "Achievements unlocked"),
                HubStat("completed", data.Completed.ToString("N0"), "Games completed"),
                HubStat("games", data.Games.Count.ToString("N0"), "Games with achievements")).Classes("playnite-hub-summary").VisibleWhen(ResponsiveVisibility.ExpandedOnly);
            var recent = UI.Stack(Hub + ".recent.section",
                UI.Text("Recent unlocks", Hub + ".recent.title").Classes("playnite-hub-section-title"),
                data.Recent.Count == 0 ? UI.Text("Recent unlocks will appear after PlayniteAchievements records them.", Hub + ".recent.empty").Classes("playnite-hub-meta") :
                UI.CollectionList(Hub + ".recent", recentAchievements!, 300, "Recent unlocks", ScrollAxis.Horizontal).Classes("playnite-hub-recent"))
                .Classes("playnite-hub-recent-section").VisibleWhen(ResponsiveVisibility.ExpandedOnly);
            WidgetElement toolbar = UI.ResponsiveGrid(Hub + ".toolbar", 380, 2,
                UI.Row(Hub + ".search.controls",
                    UI.Row(Hub + ".search.group", UI.ControllerGlyph(ControllerButton.LeftStick, Hub + ".search.hint"),
                        UI.TextEntry(controls.Search, "Search games", Hub + ".search", Hub + ".search", 160)
                            .Classes("playnite-library-achievement-search"))
                        .RememberChildFocus(Hub + ".search").Classes("playnite-library-achievement-search-group"),
                    UI.Button("Clear", Hub + ".clear", Hub + ".clear").Disabled(string.IsNullOrEmpty(controls.Search))
                        .Classes("playnite-library-search-clear"),
                    UI.Button("Refresh", Hub + ".refresh", Hub + ".refresh.compact").Disabled(busy)
                        .Classes("playnite-library-search-clear").VisibleWhen(ResponsiveVisibility.CompactOnly))
                    .Classes("playnite-library-achievement-search-group"),
                UI.ResponsiveGrid(Hub + ".filters", 170, 2,
                UI.Select("Progress", new SelectOption[]
                {
                    new("all", "All games", Hub + ".filter.all", controls.Filter == "all"),
                    new("progress", "In progress", Hub + ".filter.progress", controls.Filter == "progress"),
                    new("completed", "Completed", Hub + ".filter.completed", controls.Filter == "completed"),
                    new("new", "Not started", Hub + ".filter.new", controls.Filter == "new"),
                }, Hub + ".filter").Classes("playnite-library-control"),
                UI.Select("Sort", new SelectOption[]
                {
                    new("recent", "Recent unlock", Hub + ".sort.recent", controls.Sort == "recent"),
                    new("completion", "Completion", Hub + ".sort.completion", controls.Sort == "completion"),
                    new("name", "Name", Hub + ".sort.name", controls.Sort == "name"),
                }, Hub + ".sort").Classes("playnite-library-control")));
            var filteredCount = achievementGames!.Descriptor.Count;
            var games = filteredCount == 0 ? UI.Stack(Hub + ".games.empty",
                UI.Text(data.Games.Count == 0 ? "No cached achievements yet" : "No matching games", Hub + ".games.empty.title").Classes("playnite-hub-section-title"),
                UI.Text(data.Games.Count == 0 ? "Refresh achievements for a game in PlayniteAchievements, then refresh this page." :
                    "Try another search or progress filter.", Hub + ".games.empty.detail").Classes("playnite-hub-meta")) :
                (WidgetElement)UI.CollectionGrid(Hub + ".games", achievementGames, 380, 100, "Game achievement progress", maximumColumns: 2).Classes("playnite-hub-games");
            var summaryGroup = UI.Stack(Hub + ".overview", summary,
                UI.Text($"{data.Unlocked:N0} unlocked · {data.Completed:N0} completed games · {data.Games.Count:N0} games", Hub + ".summary.compact")
                    .Classes("playnite-hub-summary-compact").VisibleWhen(ResponsiveVisibility.CompactOnly), recent).Classes("playnite-hub-recent-section");
            if (snapshot.Error is { } error)
                toolbar = UI.Stack(Hub + ".toolbar.status", toolbar,
                    UI.Text(error.Message, Hub + ".error").Classes("playnite-hub-list-status"));
            body = UI.Grid(Hub + ".body", [GridTrack.Auto(), GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
                summaryGroup, toolbar.InGrid(row: 1), games.InGrid(row: 2)).Spacing(8, 0).Classes("playnite-hub-body");
        }
        var root = UI.Grid(Hub + ".page", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            header, body.InGrid(row: 1))
            .Spacing(12, 0).Classes("playnite-hub-page")
            .Shortcut(ControllerButton.LeftBumper, PlayniteLibraryActions.BrowseOpen, "Previous tab")
            .Shortcut(ControllerButton.RightBumper, PlayniteLibraryActions.HomeOpen, "Next tab")
            .Shortcut(ControllerButton.Y, Hub + ".refresh", "Refresh")
            .Shortcut(ControllerButton.LeftStick, Hub + ".search-focus", "Search games");
        var view = new WidgetView(root, Hub + (data?.Availability == "ready" ? ".search" : ".connection"), Surface: new()
        {
            Appearance = WidgetSurfaceAppearance.DesktopAcrylic, Mode = WidgetSurfaceMode.Wide, PreferredWidth = 1180, PreferredHeight = 760, MinimumWidth = 520, MinimumHeight = 560,
        });
        if (local.SearchFocusPending && local.ContentEntryRequestId > 0 && local.ContentEntryScopeId == _navigation.Value.InputScopeId && data?.Availability == "ready")
            return view with { FocusGroupEntryRequest = new() { RequestId = local.ContentEntryRequestId, GroupId = Hub + ".search.group" } };
        if (local.DetailsItem is null && local.ContentEntryRequestId > 0 && local.ContentEntryScopeId == _navigation.Value.InputScopeId &&
            controls.ReturnTarget is { } target)
        {
            FocusGroupEntryRequest? entry = null;
            if (data?.Availability == "ready" && data.ConnectionId == controls.ReturnConnection)
            {
                // The game/achievement is domain intent. Re-resolve its position in
                // the current ordered snapshot; never reuse a retired query target.
                if (controls.ReturnCollection == Hub + ".recent" && recentAchievements is { } recentSource)
                {
                    var index = recentSource.Items.ToList().FindIndex(item => RecentKey(item) == target.ItemKey);
                    if (index >= 0) entry = recentSource.Enter(Hub + ".recent", local.ContentEntryRequestId,
                        recentSource.FocusTarget(Hub + ".recent", new(target.ItemKey), index));
                }
                else if (achievementGames is { } gameSource)
                {
                    var index = gameSource.Items.ToList().FindIndex(item => item.Id == target.ItemKey);
                    if (index >= 0) entry = gameSource.Enter(Hub + ".games", local.ContentEntryRequestId,
                        gameSource.FocusTarget(Hub + ".games", new(target.ItemKey), index));
                }
            }
            if (data?.Availability == "ready") entry ??= new() { RequestId = local.ContentEntryRequestId, GroupId = Hub + ".search.group" };
            view = view with { FocusGroupEntryRequest = entry };
        }
        return view;
    }
    private static WidgetElement HubStat(string key, string number, string label) => UI.Row(Hub + ".stat." + key,
        UI.Text(number, Hub + ".stat." + key + ".value").Classes("playnite-hub-stat-value", key switch { "unlocked" => "playnite-ink-reward", "completed" => "playnite-ink-progress", _ => "playnite-ink-source" }),
        UI.Text(label, Hub + ".stat." + key + ".label").Classes("playnite-hub-meta")).Classes("playnite-hub-stat");
}
