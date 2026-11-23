using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Marketplace;

public sealed partial class MarketplaceWidget
{
    private static readonly (string Name, string File)[] Scenes =
        [("Radial tray", "tray.png"), ("Poster rail", "posters.png"), ("Notification", "notification.png")];
    private static readonly Lazy<Dictionary<string, string>> BundledArt = new(() =>
    {
        using var stream = typeof(MarketplaceWidget).Assembly.GetManifestResourceStream("ThemeSeeds.json")!;
        using var index = JsonDocument.Parse(stream);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var theme in index.RootElement.GetProperty("themes").EnumerateArray())
        {
            var folder = theme.GetProperty("sha256").GetString() + "/" + theme.GetProperty("previewKey").GetString();
            foreach (var file in Scenes.Select(scene => scene.File).Concat(["tray-480.png", "tray-960.png"]))
                paths.Add(ArtHandle(folder, file), "assets/themes/" + folder + "/" + file);
        }
        return paths;
    });

    private static string ArtHandle(string folder, string file) => "theme.art." + Key(folder + "/" + file);
    public override ValueTask<WidgetArtworkSource?> OnResolveArtworkSourceAsync(WidgetArtworkHandle handle, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult<WidgetArtworkSource?>(BundledArt.Value.TryGetValue(handle.Value, out var path)
            ? new WidgetFileArtwork(path, handle.Value, packageRelative: true) : null);
    }

    private static WidgetElement ThemeArt(CatalogueTheme theme, string file, string id, string label, string style)
    {
        var folder = theme.Sha256 + "/" + theme.PreviewKey;
        var handle = ArtHandle(folder, file);
        var url = ThemeCataloguePublication.Origin + "previews/" + folder + "/" + file;
        if (theme.PreviewHashes?.TryGetValue(file, out var digest) == true) url += "?sha256=" + digest;
        var image = BundledArt.Value.ContainsKey(handle) ? UI.Artwork(new(handle), id, label, ImageFit.Contain) :
            UI.PublicImage(url, id, label, ImageFit.Contain);
        return UI.Grid(id + ".frame", [GridTrack.Star()], [GridTrack.Star()], image.Classes("theme-art"))
            .Classes(style);
    }

    private WidgetResourceSnapshot<ThemeBrowsePage> ThemePageFor(State state) =>
        themePages.GetSnapshot(new(state.ThemeSearch, state.ThemePage));
    private static CatalogueThemeSelection Selection(CatalogueTheme theme) => new(theme.Id, theme.Version, theme.Sha256);
    private void LoadThemePage(State state, bool force = false) =>
        themePages.EnsureLoaded(new(state.ThemeSearch, state.ThemePage), forceRefresh: force);

    private static string CardId(CatalogueTheme item, string shelf) => "theme." + shelf + "." + Key(item.Id);
    private static IEnumerable<(CatalogueTheme Item, string Shelf)> Cards(ThemeBrowsePage page)
    {
        if (page.Featured is { } lead) yield return (lead, "feature");
        foreach (var item in page.Items)
            if (page.Featured?.Id != item.Id) yield return (item, "releases");
        var shown = page.Items.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        for (int index = 0; index < page.Collections.Length; index++)
        {
            var shelf = page.Collections[index];
            if (shelf.Items.All(t => shown.Contains(t.Id))) continue;
            foreach (var item in shelf.Items) yield return (item, "collection" + index);
        }
    }

    private bool HandleThemeAction(State state, WidgetActionEvent action)
    {
        var page = ThemePageFor(state).Value;
        if (state.SelectedTheme is { } selected)
        {
            var key = Selection(selected);
            if (action.ActionId == "theme.cancel" && state.Snapshot.Operation is { State: "running" } operation && operation.PackageId == selected.Id)
                Work(ct => ReadAsync(new("cancel", OperationId: operation.Id), ct));
            if (action.ActionId is "theme.install" or "theme.install-apply" or "theme.apply" && !state.Busy &&
                state.Snapshot.Operation?.State is not ("review" or "running") && themeStatus.GetSnapshot(key).Value is { } status &&
                (action.ActionId == "theme.apply" ? status.Installed && !status.Applied : !status.Installed && !status.AtCapacity))
                Work(ct => ReadAsync(new(action.ActionId.Replace('.', '-'), Theme: key), ct));
            if (action.ActionId == "theme.back") model.Update(s => s with { SelectedTheme = null });
            if (action.ActionId.StartsWith("theme.scene.", StringComparison.Ordinal) &&
                int.TryParse(action.ActionId["theme.scene.".Length..], out var scene) && scene >= 0 && scene < Scenes.Length)
                model.Update(s => s with { ThemeImage = scene });
            return true;
        }
        if (action.ActionId.StartsWith("marketplace.tab.", StringComparison.Ordinal)) return false;
        if (action.ActionId == "marketplace.search.focus") return false;
        if (action.ActionId == "theme.search" && action.CommittedText is { } search && search.Length <= 128)
        {
            model.Update(s => s with { ThemeSearch = search.Trim(), ThemePage = 0, ThemeReturnId = null,
                SearchFocusRequested = false, ThemePageFocus = null });
            LoadThemePage(model.Value);
        }
        else if (action.ActionId == "theme.refresh") LoadThemePage(state, true);
        else if (page is not null)
        {
            if (action.ActionId is "theme.next" or "theme.previous")
            {
                var next = page.Page + (action.ActionId == "theme.next" ? 1 : -1);
                if (next >= 0 && next * 8 < page.Total)
                {
                    var focus = CreateFocusRequest();
                    model.Update(s => s with { ThemePage = next, ThemeReturnId = null, SearchFocusRequested = false, ThemePageFocus = focus });
                    LoadThemePage(model.Value);
                }
            }
            else if (action.ActionId.StartsWith("theme.open.", StringComparison.Ordinal))
            {
                foreach (var (item, shelf) in Cards(page))
                {
                    var id = CardId(item, shelf);
                    if (action.ActionId != "theme.open." + id) continue;
                    model.Update(s => s with { SelectedTheme = item, ThemeImage = 0, ThemeReturnId = id, SearchFocusRequested = false, ThemePageFocus = null });
                    themeStatus.EnsureLoaded(Selection(item), forceRefresh: true);
                    break;
                }
            }
        }
        return true;
    }

    private WidgetView RenderThemes(State state, List<WidgetElement> header)
    {
        var resource = ThemePageFor(state);
        var page = resource.Value;
        var content = new List<WidgetElement>();
        var initial = "marketplace.tab.themes";
        FocusGroupEntryRequest? focusEntry = null;
        var selected = state.SelectedTheme;
        if (selected is not null)
        {
            initial = "theme.scene." + state.ThemeImage;
            header.Clear();
            header.Add(UI.Row("theme.detail.heading", UI.Button("Back to themes", "theme.back", "theme.back"),
                UI.Text(selected.Name, "theme.name").Classes("theme-title")).Classes("marketplace-commands"));
            var scene = Scenes[state.ThemeImage];
            var gallery = ThemeArt(selected, scene.File, "theme.detail.art", selected.Name + ", " + scene.Name, "theme-gallery-art");
            var scenes = UI.SegmentedTabs("theme.scenes", initial, Scenes.Select((s, i) =>
                new SegmentedTab("theme.scene." + i, s.Name, "theme.scene." + i)).ToArray());
            var status = themeStatus.GetSnapshot(Selection(selected));
            var op = state.Snapshot.Operation;
            var busy = state.Busy || op?.State is "running" or "review";
            var actions = new List<WidgetElement>
            {
                UI.Button("Try theme", ThemeCataloguePublication.PreviewActionPrefix + ThemeCataloguePublication.PreviewKeyFor(selected), "theme.try")
                    .Classes("primary-button"),
            };
            if (status.Value is { Installed: false, AtCapacity: false })
            {
                actions.Add(UI.Button("Install", "theme.install", "theme.install").Busy(busy));
                actions.Add(UI.Button("Install and apply", "theme.install-apply", "theme.install-apply").Busy(busy));
            }
            else if (status.Value is { Installed: true, Applied: false })
                actions.Add(UI.Button("Apply theme", "theme.apply", "theme.apply").Busy(busy));
            else if (status.Value?.Applied == true)
                actions.Add(UI.Text("Current theme", "theme.current").Classes("page-help"));
            else if (status.Value?.AtCapacity == true)
                actions.Add(UI.Button("Manage installed themes", "theme.manage", "theme.manage")
                    .OpenIntent(WidgetIntentContracts.Appearance, JsonSerializer.SerializeToElement(new {}), WidgetIntentPresentation.OpenWidget));
            if (op is { State: "running" } && op.PackageId == selected.Id)
                actions.Add(UI.Button("Cancel", "theme.cancel", "theme.cancel").Busy(state.Busy));
            var overview = new List<WidgetElement>
            {
                UI.Text(selected.Description, "theme.description").Classes("diagnostic-line"),
                UI.Text(selected.Creator + " · " + selected.Version, "theme.credit").Classes("page-help"),
                UI.Stack("theme.commands", actions.ToArray()).Classes("marketplace-commands"),
            };
            var message = state.Error ?? status.Error?.Message ?? (op?.PackageId == selected.Id ? op.Message : null);
            if (message is not null) overview.Add(UI.Text(message, "theme.operation").Classes("page-help"));
            content.Add(UI.Grid("theme.detail.layout", [GridTrack.Auto()], [GridTrack.Star(2.1), GridTrack.Star()],
                UI.Stack("theme.gallery", gallery, scenes).Classes("theme-gallery"),
                UI.Stack("theme.overview", overview.ToArray()).InGrid(column: 1).Classes("theme-overview"))
                .Spacing(0, 16));
        }
        else
        {
            header.Add(UI.Stack("marketplace.search.group",
                UI.TextEntry(state.ThemeSearch, "Search themes, creators or games", "theme.search", "theme.search", 128))
                .RememberChildFocus("theme.search"));
            if (page is null)
            {
                content.Add(UI.Text(resource.Error?.Message ?? "Loading themes…", "theme.status").Classes("page-help"));
                if (resource.Error is not null) content.Add(UI.Button("Try again", "theme.refresh", "theme.refresh"));
            }
            else
            {
                var listedCards = Cards(page).ToArray();
                var groups = listedCards.GroupBy(t => t.Shelf);
                foreach (var group in groups)
                {
                    var cards = group.Select(t => ThemeCard(t.Item, t.Shelf)).ToArray();
                    if (group.Key == "feature") content.Add(cards[0]);
                    else
                    {
                        var title = group.Key == "releases" ? state.ThemeSearch.Length > 0 ? "Search results" : "New releases" :
                            page.Collections[int.Parse(group.Key["collection".Length..])].Name;
                        content.Add(UI.Text(title, "theme.heading." + group.Key).Classes("section-heading"));
                        content.Add(UI.ResponsiveGrid("theme.shelf." + group.Key, 205, 3, cards).Classes("theme-shelf"));
                    }
                }
                if (page.Total == 0) content.Add(UI.Text(state.ThemeSearch.Length == 0 ? "No themes available yet." :
                    "No themes match your search.", "theme.empty").Classes("page-help"));
                if (page.Total > 8)
                {
                    var controls = new List<WidgetElement>();
                    if (page.Page > 0) controls.Add(UI.Button("Previous", "theme.previous", "theme.previous"));
                    controls.Add(UI.Text($"Page {page.Page + 1} of {(page.Total + 7) / 8}", "theme.pages").Classes("page-help"));
                    if ((page.Page + 1) * 8 < page.Total) controls.Add(UI.Button("Next", "theme.next", "theme.next"));
                    content.Add(UI.Row("theme.paging", controls.ToArray()).Classes("marketplace-commands"));
                }
                if (page.Offline) content.Add(UI.Text("Catalogue offline", "theme.offline").Classes("page-help"));
                var returnId = state.ThemeReturnId;
                if (returnId is not null && listedCards.Any(t => CardId(t.Item, t.Shelf) == returnId)) initial = returnId;
                if (state.ThemePageFocus is { } pageFocus && listedCards.Length > 0)
                    focusEntry = pageFocus.FocusElement("theme.browse", CardId(listedCards[0].Item, listedCards[0].Shelf));
            }
            if (state.SearchFocusRequested)
                focusEntry = new() { RequestId = state.FocusRequest, GroupId = "marketplace.search.group" };
        }
        var root = UI.Grid("marketplace.page", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            UI.Stack("marketplace.header", header.ToArray()).Classes("marketplace-header"),
            UI.VerticalScroll(selected is null ? "theme.browse" : "theme.detail", content.ToArray()).InGrid(row: 1).Classes("marketplace-content"))
            .Spacing(12, 0).InputScope("marketplace.page").Classes("marketplace-widget");
        root = selected is not null ? root.Shortcut(ControllerButton.B, "theme.back", label: "Themes") :
            root.Shortcut(ControllerButton.LeftBumper, "marketplace.tab.updates", label: "Previous section")
                .Shortcut(ControllerButton.RightBumper, "marketplace.tab.browse", label: "Next section")
                .Shortcut(ControllerButton.LeftStick, "marketplace.search.focus", label: "Search");
        return new(root, initial, Surface: new WidgetSurfaceHints { PreferredWidth = 1000, PreferredHeight = 640, MinimumWidth = 520,
            MinimumHeight = 400, Appearance = WidgetSurfaceAppearance.DesktopAcrylic, HeightMode = WidgetSurfaceAxisMode.Preferred })
        {
            FocusGroupEntryRequest = focusEntry,
        };
    }

    private static WidgetElement ThemeCard(CatalogueTheme theme, string shelf)
    {
        bool featured = shelf == "feature";
        var id = CardId(theme, shelf);
        var image = ThemeArt(theme, featured ? "tray-960.png" : "tray-480.png", id + ".art",
            theme.Name + " radial tray", featured ? "theme-feature-art" : "theme-card-art");
        var copy = new List<WidgetElement> { UI.Text(theme.Name, id + ".name").Classes(featured ? "theme-title" : "section-heading"),
            UI.Text(theme.Creator, id + ".creator").Classes("page-help") };
        if (featured)
        {
            copy.Add(UI.Text(theme.Description, id + ".summary").Classes("diagnostic-line"));
            copy.Add(UI.Text("View theme ›", id + ".view").Classes("theme-feature-link"));
        }
        var caption = UI.Stack(id + ".caption", copy.ToArray()).Classes("theme-caption");
        WidgetElement layout = featured ? UI.Grid(id + ".layout", [GridTrack.Auto()], [GridTrack.Star(1.35), GridTrack.Star()],
                image, caption.InGrid(column: 1))
            : UI.Stack(id + ".layout", caption, image);
        return UI.ActionSurface("theme.open." + id, id, theme.Name + ", by " + theme.Creator + ". " + theme.Description,
            ActionSurfaceOrientation.Vertical, layout).Classes("theme-card");
    }
}
