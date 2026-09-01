using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using LauncherWidget = WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget;

namespace WidgetRail.Tests.PlayniteLibrary;

[TestClass]
public sealed class PlayniteLibraryLayoutTests
{
    [TestMethod, Timeout(30_000)]
    public void CurrentDeclarativeLibraryPreservesExactGameAuthorityAndSemanticRegions()
    {
        var items = Enumerable.Range(0, 64)
            .Select(index => PlayniteLibraryItem.From(Item(
                $"app-{index:D3}", $"saved-{index:D3}",
                index == 0 ? new string('L', 96) : $"Game {index:D3}",
                "Fixture", artworkHandle: null)))
            .ToArray();
        var display = items.Select(item => new PlayniteLibraryDisplayItem(
            item.Value.SavedId, item.Presentation.DisplayName,
            item.Presentation.Source.DisplayName)).ToArray();
        var collection = Snapshot(WidgetPagedResourceStatus.Ready, items,
            after: "cursor.after");
        var organization = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, display);
        var view = PlayniteLibraryPresentation.Render(State(
            collection, organization, PlayniteLibraryRoute.Library, []));
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, view.Surface!.WidthMode);
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, view.Surface.HeightMode);
        var snapshot = new PresentationWidget(view).RenderSnapshot(
            "playnite-library.presentation", 1);
        var nodes = Nodes(snapshot.Root).ToArray();
        Assert.AreEqual(1, snapshot.Root.Children.Count);
        Assert.AreEqual(ViewNodeKind.BackgroundSurface, snapshot.Root.Children[0].Kind);
        Assert.AreEqual(1, nodes.Count(node => node.Kind == ViewNodeKind.FocusPresentationSurface));
        var topActions = nodes.Single(node => node.Id == "playnite-library.home.actions");
        Assert.AreEqual("playnite-library.home.utilities", topActions.Children[0].Id);
        Assert.AreEqual("playnite-library.library.menu", topActions.Children[^1].Id);
        CollectionAssert.AreEqual(new[]
        {
            "playnite-library.refresh",
            "playnite-library.recent.clear",
        }, topActions.Children[0].Children.Select(node => node.Id).ToArray());
        Assert.IsFalse(nodes.Any(node => node.Id is "playnite-library.eyebrow" or
            "playnite-library.title" or "playnite-library.status"));
        Assert.IsFalse(nodes.Any(node => node.StyleClasses.Contains(
            "playnite-library-header-row", StringComparer.Ordinal)));
        var library = nodes.Single(node => node.Id == "playnite-library.library.menu");
        Assert.AreEqual("playnite-library.browse.open", library.ActionId);
        CollectionAssert.Contains(library.StyleClasses.ToArray(),
            "playnite-library-control");
        CollectionAssert.AreEqual(new[] { "playnite-library.search.open", "playnite-library.browse.open", "playnite-library.filter.recent", "playnite-library.filter.favorites", "playnite-library.categories.open", "playnite-library.hidden.open", LauncherWidget.PlayniteOpenActionId, "playnite-library.management.open" }, library.ContextActions.Select(action => action.ActionId).ToArray());
        var rail = nodes.Single(node => node.Id == PlayniteLibraryPresentation.HomeRailId);
        Assert.AreEqual(ViewNodeKind.Scroll, rail.Kind);
        Assert.AreEqual(ScrollAxis.Horizontal, rail.ScrollAxis);
        Assert.AreEqual(items[0].Key.Value, rail.CollectionAnchorKey);
        Assert.IsTrue(rail.Shortcuts.Any(shortcut => shortcut.Button ==
            ControllerButton.RightBumper && shortcut.ActionId == "playnite-library.next"));
        var collectionItems = nodes.Where(node => node.CollectionItemKey is not null)
            .Select(node => node.CollectionItemKey!)
            .ToArray();
        CollectionAssert.AreEquivalent(items.Select(item => item.Key.Value).ToArray(),
            collectionItems);
        var launchTiles = nodes.Where(node => node.ActionId == "playnite-library.launch").ToArray();
        Assert.IsTrue(launchTiles.All(node =>
            node.FocusPresentation is not null && node.ContextActions.Count <= 8));
        var focusedSummary = launchTiles[0].FocusPresentation!;
        CollectionAssert.Contains(focusedSummary.StyleClasses.ToArray(),
            "playnite-library-home-summary");
        Assert.IsTrue(Nodes(focusedSummary).Any(node =>
            node.StyleClasses.Contains("playnite-library-summary-title", StringComparer.Ordinal)));
        Assert.IsTrue(Nodes(focusedSummary).Any(node =>
            node.StyleClasses.Contains("playnite-library-summary-meta", StringComparer.Ordinal)));
        Assert.IsFalse(nodes.Any(node => node.Id.StartsWith("playnite-library.hero.",
            StringComparison.Ordinal)),
            "Home must use FocusPresentation as its sole selected-game summary.");
        Assert.IsFalse(nodes.Any(node => node.ActionId is "playnite-library.details.open" or "playnite-library.add.open" or "playnite-library.running.open"));
        var hintRegion = nodes.Single(node =>
            node.Id == "playnite-library.organization.hints");
        Assert.AreEqual(ViewNodeKind.Row, hintRegion.Kind,
            "controller hints must be a non-scroll row for wrapping");
        Assert.IsNull(hintRegion.ScrollAxis,
            "controller hints unexpectedly own scrolling");
        Assert.IsTrue(Nodes(hintRegion).Any(node =>
            node.Id.StartsWith("playnite-library.hint.", StringComparison.Ordinal)));
        Assert.IsFalse(Nodes(hintRegion).Any(node =>
                node.Id == "playnite-library.hint.options.key" && node.Text == "Y"),
            "Home must not advertise the retired Y game-action surface.");
        Assert.IsTrue(Nodes(hintRegion).Any(node =>
                node.Id == "playnite-library.hint.options.key" && node.Text == "Menu"),
            "Home must advertise the current Menu-owned game options.");
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid", items[0].Key),
            snapshot.InitialFocusId);
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "styles", "default.wrss"));
        Assert.IsFalse(styles.Contains("\nbutton {", StringComparison.Ordinal),
            "Playnite WRSS must not style every Button through a bare element selector.");
        Assert.IsTrue(topActions.Children[0].Children.All(node =>
            node.StyleClasses.Contains("playnite-library-control", StringComparer.Ordinal)));
        StringAssert.Contains(styles,
            ".playnite-library-rail { width: 100%; min-width: 0px; flex-shrink: 0; gap: 12px; padding: 4px 6px 10px; }");
        StringAssert.Contains(styles,
            ".playnite-library-tile { aspect-ratio: 2/3;");
        StringAssert.Contains(styles,
            ".playnite-library-fixed-tile { width: 150px; min-width: 150px; flex-basis: 150px; flex-grow: 0; flex-shrink: 0; }");
        StringAssert.Contains(styles,
            ".playnite-library-home-foreground { width: 100%; min-width: 520px; max-width: 1180px;");
        StringAssert.Contains(styles,
            ".wrail-background-surface { background: rgba(0, 0, 0, 0); }");
        StringAssert.Contains(styles,
            ".playnite-library-home-summary { width: 100%; min-width: 0px; max-width: 620px;");
        StringAssert.Contains(styles,
            ".wrail-poster-tile__scrim { width: 100%; height: 0px; min-height: 0px; padding: 0px; background: rgba(0, 0, 0, 0); corner-radius: 12px; overflow: clip; }");
        StringAssert.Contains(styles,
            ".wrail-poster-tile__content { height: 0px; min-height: 0px; gap: 0px; overflow: clip; }");

        var theme = CompileStyles();
        var homeStyle = theme.Resolve(new WrssElement("stack", null,
            new HashSet<string>(["playnite-library-home-foreground"],
                StringComparer.Ordinal)));
        Assert.AreEqual("1180px", homeStyle.Get("max-width")?.Text);
        Assert.IsNull(homeStyle.Get("max-height"),
            "Home must not inherit the retired 680-DIP page-scroll cap.");
        var posterScrimStyle = theme.Resolve(new WrssElement("stack", null,
            new HashSet<string>(["wrail-poster-tile__scrim"],
                StringComparer.Ordinal)));
        Assert.AreEqual("0px", posterScrimStyle.Get("height")?.Text,
            "Playnite posters must not reserve a visible text/scrim overlay.");
        Assert.AreEqual("rgba(0, 0, 0, 0)",
            posterScrimStyle.Get("background")?.Text,
            "Playnite posters must show their accepted artwork without a dark text scrim.");
        Assert.IsFalse(nodes.Any(node => node.StyleClasses.Contains(
                "playnite-library-page-scroll", StringComparer.Ordinal)),
            "Home must not emit the retired shared page-scroll class.");
        var focusSurfaceStyle = theme.Resolve(new WrssElement(
            "focus-presentation-surface", null,
            new HashSet<string>(["wrail-focus-presentation-surface"],
                StringComparer.Ordinal)));
        Assert.AreEqual("start", focusSurfaceStyle.Get("align")?.Text,
            "The selected-game details must align with the left edge of the poster rail.");
        var homeContentStyle = theme.Resolve(new WrssElement("stack", null,
            new HashSet<string>(["playnite-library-home-content"],
                StringComparer.Ordinal)));
        Assert.AreEqual("0", homeContentStyle.Get("flex-grow")?.Text,
            "Home content must not grow a spacer between selected-game details and the rail.");
        Assert.AreEqual("0", homeContentStyle.Get("flex-shrink")?.Text,
            "Home details and the rail must remain one adjacent bottom composition.");
        var topActionsStyle = theme.Resolve(new WrssElement("row", null,
            new HashSet<string>(["playnite-library-home-actions"],
                StringComparer.Ordinal)));
        Assert.AreEqual("rgba(0, 0, 0, 0)", topActionsStyle.Get("background")?.Text);

        var browseView = PlayniteLibraryPresentation.Render(State(
            collection, organization, PlayniteLibraryRoute.Browse, []));
        var browseSnapshot = new PresentationWidget(browseView).RenderSnapshot(
            "playnite-library.presentation.browse", 2);
        var browseNodes = Nodes(browseSnapshot.Root).ToArray();
        foreach (var id in new[]
                 {
                     "playnite-library.browse.back",
                     "playnite-library.filter.favorites",
                     "playnite-library.filter.recent",
                     "playnite-library.filter.source",
                     "playnite-library.filter.sort",
                     "playnite-library.query.clear",
                     "playnite-library.search",
                 })
        {
            var control = browseNodes.Single(node => node.Id == id);
            CollectionAssert.Contains(control.StyleClasses.ToArray(),
                "playnite-library-control",
                id + " must share the Library control styling contract.");
        }
    }

    [TestMethod, Timeout(30_000)]
    public void CatalogCardsAdoptPosterTilePresentationWithSemanticArtworkFallback()
    {
        var artwork = PlayniteLibraryItem.From(Item(
            "app-poster", "saved-poster", "Poster game", "Steam", "poster-handle"));
        var fallback = PlayniteLibraryItem.From(Item(
            "app-fallback", "saved-fallback", "Fallback game", "Windows"));
        var items = new[] { artwork, fallback };
        var display = items.Select(item => new PlayniteLibraryDisplayItem(
            item.Value.SavedId, item.Value.Presentation.DisplayName,
            item.Value.Presentation.Source.DisplayName)).ToArray();
        var view = PlayniteLibraryPresentation.Render(State(
            Snapshot(WidgetPagedResourceStatus.Ready, items),
            new PlayniteLibraryPrivateState(
                PlayniteLibraryPrivateState.CurrentVersion, display),
            PlayniteLibraryRoute.Library, []));
        var snapshot = new PresentationWidget(view).RenderSnapshot(
            "playnite-library.poster", 1);
        var nodes = Nodes(snapshot.Root).ToArray();
        var posterId = PlayniteLibraryIdentity.FocusId("grid", artwork.Key);
        var fallbackId = PlayniteLibraryIdentity.FocusId("grid", fallback.Key);

        foreach (var id in new[] { posterId, fallbackId })
        {
            var card = nodes.Single(node => node.Id == id);
            Assert.AreEqual(ViewNodeKind.ActionSurface, card.Kind);
            Assert.AreEqual(ActionSurfacePresentation.Poster, card.ActionSurfacePresentation);
            Assert.AreEqual(ActionSurfaceOrientation.Vertical, card.ActionSurfaceOrientation);
            Assert.AreEqual("playnite-library.launch", card.ActionId);
            CollectionAssert.AreEqual(
            new[]
            {
                "wrail-action-surface",
                "wrail-poster-tile",
                "playnite-library-tile",
                "playnite-library-fixed-tile",
            }, card.StyleClasses.ToArray());
            Assert.IsNotNull(card.CollectionItemKey);
            Assert.IsTrue(nodes.Any(node => node.Id == id + ".scrim"));
            Assert.IsTrue(nodes.Any(node => node.Id == id + ".content"));
        }

        var renderedArtwork = nodes.Single(node => node.Id == posterId + ".artwork");
        Assert.AreEqual("poster-handle", renderedArtwork.ArtworkHandle);
        Assert.AreEqual(ImageFit.Cover, renderedArtwork.ImageFit);
        Assert.IsFalse(nodes.Any(node => node.Id == fallbackId + ".artwork"),
            "A missing cover must use the generic poster fallback rather than glyph artwork.");
        Assert.AreEqual("Fallback game, Windows, Play",
            nodes.Single(node => node.Id == fallbackId).AccessibilityLabel);
    }

    [TestMethod, Timeout(30_000)]
    public void HomeOwnsFocusSummariesAndBrowseDoesNot()
    {
        var first = PlayniteLibraryItem.From(Item("app-a", "saved-a", "Alpha", "Steam"));
        var second = PlayniteLibraryItem.From(Item("app-b", "saved-b", "Bravo", "Windows"));
        var home = new PresentationWidget(PlayniteLibraryPresentation.Render(State(
            Snapshot(WidgetPagedResourceStatus.Ready, [first, second]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, [])))
            .RenderSnapshot("playnite-library.home.focus", 1);
        var homeNodes = Nodes(home.Root).ToArray();
        Assert.AreEqual(1, homeNodes.Count(node => node.Kind ==
            ViewNodeKind.FocusPresentationSurface));
        Assert.AreEqual(2, homeNodes.Count(node => node.ActionId ==
            "playnite-library.launch" && node.FocusPresentation is not null));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(home).Count);

        var browse = new PresentationWidget(PlayniteLibraryPresentation.Render(State(
            Snapshot(WidgetPagedResourceStatus.Ready, [first, second]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, [])))
            .RenderSnapshot("playnite-library.browse.focus", 1);
        var browseView = PlayniteLibraryPresentation.Render(State(
            Snapshot(WidgetPagedResourceStatus.Ready, [first, second]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, []));
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, browseView.Surface!.WidthMode);
        Assert.IsTrue(Nodes(browse.Root).Any(node =>
            node.Id == "playnite-library.browse.cinematic" &&
            node.Kind == ViewNodeKind.BackgroundSurface));
        Assert.IsTrue(Nodes(browse.Root).Single(node =>
                node.Id == "playnite-library.header").StyleClasses.Contains(
                "playnite-library-browse-header", StringComparer.Ordinal));
        Assert.IsFalse(browse.Root.StyleClasses.Contains(
                "playnite-library-widget", StringComparer.Ordinal),
            "Browse must not inherit the bounded ordinary widget root.");
        Assert.IsFalse(Nodes(browse.Root).Any(node => node.FocusPresentation is not null));
        Assert.IsFalse(Nodes(browse.Root).Any(node => node.Id.StartsWith(
            "playnite-library.hero.", StringComparison.Ordinal)),
            "Browse must not duplicate selected-game details beneath its filters.");
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(browse).Count);
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "styles", "default.wrss"));
        StringAssert.Contains(styles,
            ".playnite-library-browse-grid { width: 100%; min-width: 0px; flex-shrink: 0; gap: 16px 14px; padding: 4px; justify: center; }");
        StringAssert.Contains(styles,
            ".playnite-library-browse-foreground { width: 100%; min-width: 0px; min-height: 0px; max-width: 1120px;");
        var theme = CompileStyles();
        var browseStyle = theme.Resolve(new WrssElement("scroll", null,
            new HashSet<string>(
            [
                "playnite-library-browse-scroll",
                "playnite-library-browse-foreground",
                "playnite-library-main",
            ], StringComparer.Ordinal)));
        Assert.AreEqual("1120px", browseStyle.Get("max-width")?.Text);
        Assert.IsNull(browseStyle.Get("max-height"),
            "Browse must not inherit the retired 680-DIP page-scroll cap.");
        var browseBackgroundStyle = theme.Resolve(new WrssElement("background-surface", null,
            new HashSet<string>(
            [
                "wrail-background-surface",
                "playnite-library-browse-background",
            ], StringComparer.Ordinal)));
        Assert.AreEqual("center", browseBackgroundStyle.Get("align")?.Text);
        Assert.AreEqual("center", browseBackgroundStyle.Get("justify")?.Text);
        Assert.IsFalse(Nodes(browse.Root).Any(node => node.StyleClasses.Contains(
                "playnite-library-page-scroll", StringComparer.Ordinal)),
            "Browse must not emit the retired shared page-scroll class.");
        var browseGrid = Nodes(browse.Root).Single(node =>
            node.Id == "playnite-library.browse.grid");
        Assert.AreEqual(150D, browseGrid.GridMinimumColumnWidth);
        Assert.AreEqual(7, browseGrid.GridMaximumColumns);
        var browseRootStyle = theme.Resolve(new WrssElement("stack", null,
            browse.Root.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        Assert.AreEqual("100%", browseRootStyle.Get("width")?.Text);
        Assert.AreEqual("100%", browseRootStyle.Get("height")?.Text);
        Assert.IsNull(browseRootStyle.Get("max-width"),
            "Browse root must fill the admitted width; only its foreground is bounded.");
        Assert.IsNull(browseRootStyle.Get("max-height"),
            "Browse root must fill the admitted height; only its foreground is bounded.");
        var browseTile = Nodes(browse.Root).First(node =>
            node.ActionId == "playnite-library.launch");
        CollectionAssert.Contains(browseTile.StyleClasses.ToArray(),
            "playnite-library-browse-tile");
        CollectionAssert.DoesNotContain(browseTile.StyleClasses.ToArray(),
            "playnite-library-fixed-tile");
        var browseTileStyle = theme.Resolve(new WrssElement("action-surface", null,
            browseTile.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        Assert.IsNull(browseTileStyle.Get("width"),
            "ResponsiveGrid must own the Browse tile's resolved track width.");
        Assert.AreEqual("0px", browseTileStyle.Get("min-width")?.Text);
        Assert.IsNull(browseTileStyle.Get("flex-basis"),
            "Browse must not retain the fixed Home rail basis.");
        Assert.AreEqual(2D / 3D, browseTileStyle.Get("aspect-ratio")?.Number);
        Assert.AreEqual(0D, browseTileStyle.Get("flex-grow")?.Number);
        Assert.AreEqual(1D, browseTileStyle.Get("flex-shrink")?.Number);
    }

    [TestMethod, Timeout(30_000)]
    public void HomeAndBrowseDefaultBackgroundUseProjectedSelectedArtwork()
    {
        var hero = ArtworkItem("app-hero", "saved-hero", "Hero game", "Steam",
            "library.tile.hero", "library.background.hero");
        var homeState = State(Snapshot(WidgetPagedResourceStatus.Ready, [hero]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, []) with
        {
            HeroSavedId = hero.Value.SavedId,
        };
        var home = new PresentationWidget(PlayniteLibraryPresentation.Render(homeState))
            .RenderSnapshot("playnite-library.home.background", 1);
        var homeBackground = Nodes(home.Root).Single(node =>
            node.Id == "playnite-library.cinematic");
        Assert.AreEqual("library.background.hero", homeBackground.ArtworkHandle);
        Assert.AreEqual(ImageFit.Cover, homeBackground.ImageFit);
        Assert.AreEqual(true, homeBackground.UsesFocusedDescendantArtwork);

        var tileOnly = ArtworkItem("app-tile", "saved-tile", "Tile game", "Windows",
            "library.tile.fallback", heroHandle: null);
        var browseState = State(Snapshot(WidgetPagedResourceStatus.Ready, [tileOnly]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, []) with
        {
            HeroSavedId = tileOnly.Value.SavedId,
        };
        var browse = new PresentationWidget(PlayniteLibraryPresentation.Render(browseState))
            .RenderSnapshot("playnite-library.browse.background", 2);
        var browseBackground = Nodes(browse.Root).Single(node =>
            node.Id == "playnite-library.browse.cinematic");
        Assert.AreEqual("library.tile.fallback", browseBackground.ArtworkHandle,
            "A selected game without Hero artwork must use its exact Tile handle.");
        Assert.AreEqual(ImageFit.Cover, browseBackground.ImageFit);
        Assert.AreEqual(true, browseBackground.UsesFocusedDescendantArtwork);
    }

    [TestMethod, Timeout(30_000)]
    public void BrowseSearchTransitionsAlwaysPublishAValidFocusableInitialTarget()
    {
        var item = PlayniteLibraryItem.From(Item(
            "app-search", "saved-search", "Search result", "Steam"));
        var staleFocus = PlayniteLibraryIdentity.FocusId(
            "grid", PlayniteLibraryIdentity.Key("saved-retired"));

        var matchingState = State(
            Snapshot(WidgetPagedResourceStatus.Ready, [item]) with
            {
                RequestedFocusId = staleFocus,
            }, PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, []);
        var matching = new PresentationWidget(PlayniteLibraryPresentation.Render(matchingState))
            .RenderSnapshot("playnite-library.search.matching", 1);
        Assert.AreEqual(item.Key.Value,
            Nodes(matching.Root).Single(node => node.Id == matching.InitialFocusId)
                .CollectionItemKey);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(matching).Count);

        foreach (var state in new[]
                 {
                     State(Snapshot(WidgetPagedResourceStatus.Loading, []) with
                         {
                             RequestedFocusId = staleFocus,
                         }, PlayniteLibraryPrivateState.Empty,
                         PlayniteLibraryRoute.Browse, []),
                     State(Snapshot(WidgetPagedResourceStatus.Ready, []) with
                         {
                             RequestedFocusId = staleFocus,
                         }, PlayniteLibraryPrivateState.Empty,
                         PlayniteLibraryRoute.Browse, []),
                 })
        {
            var snapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(state))
                .RenderSnapshot("playnite-library.search.transient", 2);
            Assert.AreEqual("playnite-library.search", snapshot.InitialFocusId);
            var search = Nodes(snapshot.Root).Single(node => node.Id == snapshot.InitialFocusId);
            Assert.AreEqual(ViewNodeKind.TextEntry, search.Kind);
            Assert.IsTrue(search.IsDisabled != true);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        }
    }

    [TestMethod, Timeout(30_000)]
    public void HeroRailPolicyBoundsLongFallbackAndSelectedState()
    {
        var longTitle = new string('H', 96);
        var items = Enumerable.Range(0, 20).Select(index =>
            PlayniteLibraryItem.From(Item(
                $"app-hero-{index:D2}", $"saved-hero-{index:D2}",
                index == 7 ? longTitle : $"Hero {index:D2}",
                index % 2 == 0 ? "Steam" : "Windows",
                index == 7 ? null :
                    $"hero.art.{index:D2}.0123456789abcdef0123456789abcdef")))
            .ToArray();
        var organization = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion,
            items.Select(item => new PlayniteLibraryDisplayItem(
                item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName)).ToArray())
        {
            FavoriteSavedIds = [items[7].Value.SavedId],
            VariantGroups = [new("variant.hero",
                [items[7].Value.SavedId, items[8].Value.SavedId],
                items[7].Value.SavedId)],
        };
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, items), organization,
            PlayniteLibraryRoute.Library, [] ) with
        {
            HeroSavedId = items[7].Value.SavedId,
            HeroIndex = 7,
        };
        var model = PlayniteLibraryHeroRailPolicy.Project(
            state, state.HeroSavedId, state.HeroIndex);
        Assert.AreEqual(20, model.Items.Count);
        Assert.AreEqual(items[7].Value.SavedId, model.Selected?.Display.SavedId);
        var snapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(state))
            .RenderSnapshot("hero.layout", 30);
        var nodes = Nodes(snapshot.Root).ToArray();
        var selected = nodes.Single(node => node.Id == PlayniteLibraryIdentity.FocusId(
            "grid", items[7].Key));
        Assert.IsNotNull(selected.FocusPresentation,
            "The selected Home poster must own the focused-game summary.");
        Assert.IsFalse(nodes.Any(node => node.Id.StartsWith("playnite-library.hero.",
            StringComparison.Ordinal)),
            "The legacy hero must not duplicate the focused-game summary on Home.");
        Assert.IsLessThanOrEqualTo(20 * 8 + 40, nodes.Length,
            "Hero-rail semantic nodes must remain linear in the bounded page.");

        var filtered = state with
        {
            Collection = Snapshot(WidgetPagedResourceStatus.Ready,
                items.Where((_, index) => index != 7).ToArray()),
            Organization = organization with
            {
                ExcludedSavedIds = [items[7].Value.SavedId],
            },
        };
        var fallback = PlayniteLibraryHeroRailPolicy.Project(
            filtered, state.HeroSavedId, state.HeroIndex);
        Assert.AreEqual(items[8].Value.SavedId, fallback.Selected?.Display.SavedId,
            "A removed selection must choose the nearest retained rail position.");
    }

    private static PlayniteLibraryPresentationState State(
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> collection,
        PlayniteLibraryPrivateState organization,
        PlayniteLibraryRoute route,
        IReadOnlyList<WidgetAppLibrarySource> sources) => new(
            collection, organization, "Ready", null,
            new Dictionary<string, PlayniteLibraryLaunchState>(StringComparer.Ordinal),
            OrganizationBusy: false, Interactive: true, new WidgetAppLibraryQuery(),
            PlayniteLibraryRecentMode.Off, FavoriteFilter: false, route,
            PlayniteLibraryFixedRows.Empty, sources, HeroSavedId: null, HeroIndex: 0)
        {
            Collections = PlayniteLibraryCollectionPolicy.Options(
                organization, organization.ProvenSources.ToArray(),
                PlayniteLibraryCollectionPolicy.AllInstalled),
        };

    private static WidgetCursorResourceSnapshot<PlayniteLibraryItem> Snapshot(
        WidgetPagedResourceStatus status,
        IReadOnlyList<PlayniteLibraryItem> items,
        string? before = null,
        string? after = null,
        WidgetResourceError? error = null) => new(
            status, items,
            before is null ? null : new WidgetCollectionCursor(before),
            after is null ? null : new WidgetCollectionCursor(after),
            items.Count == 0 ? null : items[0].Key,
            null,
            error, 1);

    private static IEnumerable<ViewNode> Nodes(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in Nodes(child))
            yield return node;
    }

    private static WrssTheme CompileStyles()
    {
        var stylesRoot = Path.Combine(AppContext.BaseDirectory, "styles");
        var package = WrssPackageLoader.Load(
            "default.wrss", new WrssFileSourceProvider(stylesRoot));
        var compiled = WrssThemeCompiler.Compile(package);
        Assert.IsTrue(compiled.IsValid,
            string.Join(Environment.NewLine,
                compiled.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return compiled.Theme!;
    }

    private static IEnumerable<ViewNode> VisibleNodes(ViewNode root, bool compact)
    {
        if (root.VisibleWhen == ResponsiveVisibility.CompactOnly && !compact ||
            root.VisibleWhen == ResponsiveVisibility.ExpandedOnly && compact)
            yield break;
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in VisibleNodes(child, compact))
            yield return node;
    }

    private static WidgetAppLibraryItem Item(
        string appId,
        string savedId,
        string displayName,
        string source,
        string? artworkHandle = null)
    {
        WidgetAppLibraryArtwork[] artwork = artworkHandle is null ? [] :
        [
            new(WidgetAppLibraryArtworkRole.Tile, artworkHandle, "fixture",
                WidgetAppLibraryArtworkFallback.Game),
        ];
        var item = new WidgetAppLibraryItem(appId, savedId, new(
            displayName,
            WidgetAppLibraryKind.Game,
            new("source-fixture", source),
            new(WidgetAppLibraryAvailabilityState.Installed, true, "installed"),
            new(artwork),
            Metadata: null,
            new([WidgetAppLibraryAction.Launch]),
            ActiveOperation: null));
        if (artworkHandle is null) return item;
        return item with
        {
            Presentation = item.Presentation with
            {
                Artwork = new([
                    item.Presentation.Artwork.Items.Single(),
                    new(WidgetAppLibraryArtworkRole.Hero, artworkHandle, "fixture",
                        WidgetAppLibraryArtworkFallback.Game),
                ]),
            },
        };
    }

    private static PlayniteLibraryItem ArtworkItem(
        string appId,
        string savedId,
        string displayName,
        string source,
        string tileHandle,
        string? heroHandle)
    {
        var item = Item(appId, savedId, displayName, source) with
        {
            Presentation = Item(appId, savedId, displayName, source).Presentation with
            {
                Artwork = new([
                    new(WidgetAppLibraryArtworkRole.Tile, tileHandle, "fixture",
                        WidgetAppLibraryArtworkFallback.Game),
                    .. heroHandle is null ? [] : new WidgetAppLibraryArtwork[]
                    {
                        new(WidgetAppLibraryArtworkRole.Hero, heroHandle, "fixture",
                            WidgetAppLibraryArtworkFallback.Game),
                    },
                ]),
            },
        };
        return PlayniteLibraryItem.From(item);
    }

    private sealed class PresentationWidget(WidgetView view) : Widget
    {
        public override WidgetView Render() => view;
    }
}
