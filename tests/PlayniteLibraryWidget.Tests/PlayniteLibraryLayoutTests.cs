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
    [TestMethod]
    public void BrowsePagingPreservesFocusAndMatchingCount()
    {
        var items = Enumerable.Range(0, 6).Select(index => PlayniteLibraryItem.From(
            Item($"app-page-{index}", $"saved-page-{index}", $"Game {index}", "Steam"))).ToArray();
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, items, after: "next"),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, []) with
        {
            HeroSavedId = items[2].Value.SavedId, HeroIndex = 2, MatchingGameCount = 34,
            Status = "6+ games in the current catalog window",
        };
        var expected = PlayniteLibraryIdentity.FocusId("grid", items[2].Key);
        foreach (var phase in new[] { WidgetPagedResourceStatus.Ready,
                     WidgetPagedResourceStatus.LoadingAdjacent, WidgetPagedResourceStatus.Refreshing })
        {
            var view = PlayniteLibraryPresentation.Render(state with
                { Collection = state.Collection with { Status = phase, LoadingDirection = WidgetCursorDirection.After } });
            Assert.AreEqual(expected, view.InitialFocusId, phase.ToString());
            var snapshot = new PresentationWidget(view).RenderSnapshot("browse.paging", 1);
            Assert.AreEqual("34 games", Nodes(snapshot.Root).Single(node => node.Id == "playnite-library.status").Text);
            var scroll = Nodes(snapshot.Root).Single(node => node.Id == PlayniteLibraryPresentation.ScrollId);
            Assert.AreEqual(phase == WidgetPagedResourceStatus.Ready, scroll.ScrollNearEndActionId is not null,
                "Only a settled collection may advertise another page request.");
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        }
        var finalPage = state with { Collection = Snapshot(WidgetPagedResourceStatus.Ready, items.Take(4).ToArray(), before: "prev") };
        var finalSnapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(finalPage)).RenderSnapshot("browse.final", 2);
        Assert.AreEqual("34 games", Nodes(finalSnapshot.Root).Single(node => node.Id == "playnite-library.status").Text);
        var unknown = finalPage with { MatchingGameCount = null };
        var unknownSnapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(unknown)).RenderSnapshot("browse.unknown", 3);
        Assert.AreEqual("4 games loaded", Nodes(unknownSnapshot.Root).Single(node => node.Id == "playnite-library.status").Text);
    }

    [TestMethod]
    public void HomePagingPreservesProviderOrderDespiteFavoriteAndVariantMetadata()
    {
        var items = Enumerable.Range(0, 8).Select(index => PlayniteLibraryItem.From(
            Item($"app-order-{index}", $"saved-order-{index}", $"Game {index}", "Steam"))).ToArray();
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            Items = items.Select(item => new PlayniteLibraryDisplayItem(item.Value.SavedId,
                item.Presentation.DisplayName, item.Presentation.Source.DisplayName)).ToArray(),
            FavoriteSavedIds = [items[6].Value.SavedId, items[1].Value.SavedId],
            VariantGroups = [new("variant.order", [items[4].Value.SavedId, items[5].Value.SavedId], items[5].Value.SavedId)],
        };
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, items.Take(4).ToArray(), after: "next"),
            organization, PlayniteLibraryRoute.Library, []);
        var first = PlayniteLibraryHeroRailPolicy.Project(state, null, 0);
        var next = PlayniteLibraryHeroRailPolicy.Project(state with
            { Collection = Snapshot(WidgetPagedResourceStatus.Ready, items) }, null, 0);
        CollectionAssert.AreEqual(items.Select(item => item.Value.SavedId).ToArray(),
            next.Items.Select(item => item.Display.SavedId).ToArray());
        CollectionAssert.AreEqual(first.Items.Select(item => item.Display.SavedId).ToArray(),
            next.Items.Take(4).Select(item => item.Display.SavedId).ToArray());
    }

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
            PlayniteLibraryPrivateState.CurrentVersion, display)
        {
            Categories = [new(
                "category.11111111111111111111111111111111", "Strategy", [])],
        };
        var presentation = State(
            collection, organization, PlayniteLibraryRoute.Library, []);
        var view = PlayniteLibraryPresentation.Render(presentation);
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, view.Surface!.WidthMode);
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, view.Surface.HeightMode);
        var snapshot = new PresentationWidget(view).RenderSnapshot(
            "playnite-library.presentation", 1);
        var nodes = Nodes(snapshot.Root).ToArray();
        Assert.AreEqual(1, snapshot.Root.Children.Count);
        Assert.AreEqual(ViewNodeKind.BackgroundSurface, snapshot.Root.Children[0].Kind);
        var homeStage = snapshot.Root.Children[0].Children.Single();
        Assert.AreEqual("playnite-library.home.stage", homeStage.Id);
        CollectionAssert.Contains(homeStage.StyleClasses.ToArray(),
            "playnite-library-home-stage");
        CollectionAssert.Contains(homeStage.StyleClasses.ToArray(),
            "playnite-library-surface-stage");
        Assert.AreEqual("playnite-library.home.foreground",
            homeStage.Children.Single().Id);
        Assert.AreEqual(1, nodes.Count(node => node.Kind == ViewNodeKind.FocusPresentationSurface));
        var topActions = nodes.Single(node => node.Id == "playnite-library.home.actions");
        Assert.AreEqual("playnite-library.home.utilities", topActions.Children[0].Id);
        Assert.AreEqual("playnite-library.library.menu", topActions.Children[^1].Id);
        CollectionAssert.AreEqual(new[]
        {
            "playnite-library.hint.refresh",
        }, topActions.Children[0].Children.Select(node => node.Id).ToArray());
        Assert.IsFalse(nodes.Any(node => node.Id is "playnite-library.eyebrow" or
            "playnite-library.title" or "playnite-library.status"));
        Assert.IsFalse(nodes.Any(node => node.StyleClasses.Contains(
            "playnite-library-header-row", StringComparer.Ordinal)));
        var library = nodes.Single(node => node.Id == "playnite-library.library.menu");
        Assert.IsNull(library.ActionId);
        Assert.IsFalse(library.IsFocusable);
        Assert.AreEqual(ControllerButton.Menu, library.ContextMenuButton);
        Assert.IsFalse(Nodes(topActions).Any(node => node.IsFocusable));
        CollectionAssert.AreEqual(new[]
        {
            "playnite-library.browse.open",
            "playnite-library.categories.open",
            "playnite-library.hidden.open",
            LauncherWidget.PlayniteOpenActionId,
        }, library.ContextActions.Select(action => action.ActionId).ToArray());
        Assert.IsTrue(nodes.Any(node =>
            node.Id == "playnite-library.library.menu.key" && node.Text == "Menu"));
        var rail = nodes.Single(node => node.Id == PlayniteLibraryPresentation.HomeRailId);
        Assert.AreEqual(ViewNodeKind.Scroll, rail.Kind);
        Assert.AreEqual(ScrollAxis.Horizontal, rail.ScrollAxis);
        Assert.AreEqual(items[0].Key.Value, rail.CollectionAnchorKey);
        Assert.IsFalse(nodes.Any(node => node.Shortcuts.Any(shortcut =>
            shortcut.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper)));
        var collectionItems = nodes.Where(node => node.CollectionItemKey is not null)
            .Select(node => node.CollectionItemKey!)
            .ToArray();
        CollectionAssert.AreEquivalent(items.Select(item => item.Key.Value).ToArray(),
            collectionItems);
        var launchTiles = nodes.Where(node => node.ActionId == "playnite-library.launch").ToArray();
        Assert.IsTrue(launchTiles.All(node =>
            node.FocusPresentation is not null && node.ContextActions.Count <= 8));
        Assert.IsFalse(nodes.Any(node => node.Shortcuts.Any(shortcut =>
                (shortcut.Button is ControllerButton.LeftTrigger or
                    ControllerButton.RightTrigger) &&
                (shortcut.ActionId is PlayniteLibraryActions.CollectionPrevious or
                    PlayniteLibraryActions.CollectionNext))),
            "Home scope and posters must not bind LT/RT collection switching.");
        Assert.IsFalse(nodes.Any(node =>
                node.Id is "playnite-library.collection.hint.previous" or
                    "playnite-library.collection.hint.next" ||
                node.Text is "Previous collection" or "Next collection"),
            "Home must not advertise previous/next collection trigger hints.");
        Assert.IsTrue(launchTiles.All(node => node.ContextMenuButton == ControllerButton.X),
            "X opens the focused game's options.");
        Assert.IsTrue(nodes.SelectMany(node => node.Shortcuts).Any(shortcut =>
            shortcut.Button == ControllerButton.Y && shortcut.ActionId == PlayniteLibraryActions.Refresh));
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
                node.Id == "playnite-library.hint.options.key" && node.Text == "X"),
            "Home must advertise X for game options.");
        var unavailableView = PlayniteLibraryPresentation.Render(
            presentation with { OrganizationBusy = true });
        var unavailableSnapshot = new PresentationWidget(unavailableView)
            .RenderSnapshot("playnite-library.unavailable-hints", 2);
        var unavailableHints = Nodes(unavailableSnapshot.Root)
            .Single(node => node.Id == "playnite-library.organization.hints");
        CollectionAssert.AreEqual(
            hintRegion.Children.Select(child => child.Id).ToArray(),
            unavailableHints.Children.Select(child => child.Id).ToArray(),
            "Game-option availability changed the Home footer structure.");
        var unavailableOptionsLabel = Nodes(unavailableHints).Single(node =>
            node.Id == "playnite-library.hint.options.label");
        Assert.AreEqual("Game options", unavailableOptionsLabel.Text,
            "Game-option availability changed the visible Home hint width.");
        Assert.AreEqual("Game options unavailable", unavailableOptionsLabel.AccessibilityLabel,
            "The stable Home footer must expose unavailable game options.");
        CollectionAssert.Contains(unavailableHints.Children[0].StyleClasses.ToArray(),
            "playnite-library-hint-unavailable");
        Assert.IsFalse(Nodes(unavailableHints).Any(node =>
                node.ActionId == PlayniteLibraryActions.Favorite),
            "The inactive Home hint became an executable Favorite action.");
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid", items[0].Key),
            snapshot.InitialFocusId);
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "styles", "default.wrss"));
        Assert.IsFalse(styles.Contains("\nbutton {", StringComparison.Ordinal),
            "Playnite WRSS must not style every Button through a bare element selector.");
        Assert.IsTrue(topActions.Children[0].Children.All(node =>
            !node.IsFocusable));
        StringAssert.Contains(styles,
            ".playnite-library-rail { width: 100%; min-width: 0px; flex-shrink: 0; gap: 12px; padding: 4px 6px 10px; }");
        StringAssert.Contains(styles,
            ".playnite-library-footer { min-height: 28px; flex-shrink: 0; flex-wrap: wrap; gap: 6px; }");
        StringAssert.Contains(styles,
            ".playnite-library-hint-unavailable { opacity: 0.64; }");
        StringAssert.Contains(styles,
            ".playnite-library-tile { aspect-ratio: 2/3;");
        StringAssert.Contains(styles,
            ".playnite-library-fixed-tile { width: 150px; min-width: 150px; flex-basis: 150px; flex-grow: 0; flex-shrink: 0; }");
        StringAssert.Contains(styles,
            ".playnite-library-surface-stage { width: 100%; height: 100%; min-width: 0px; min-height: 0px; flex-grow: 1; flex-basis: 0; align: center; justify: center; overflow: clip; }");
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
        var stageStyle = theme.Resolve(new WrssElement("stack", null,
            new HashSet<string>(["playnite-library-surface-stage",
                "playnite-library-home-stage"],
                StringComparer.Ordinal)));
        Assert.AreEqual("100%", stageStyle.Get("width")?.Text);
        Assert.AreEqual("100%", stageStyle.Get("height")?.Text);
        Assert.IsNull(stageStyle.Get("max-width"),
            "The BackgroundSurface content owner must not inherit the foreground cap.");
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
        var browsePage = browseNodes.Single(node =>
            node.Id == "playnite-library.browse.page");
        var browsePageNodes = Nodes(browsePage).ToArray();
        foreach (var id in new[]
                 {
                     "playnite-library.search",
                     "playnite-library.filter.source",
                     "playnite-library.filter.sort",
                     "playnite-library.filter.recent",
                     "playnite-library.filter.favorites",
                     "playnite-library.query.clear",
                     "playnite-library.browse.hint.refresh",
                 })
            Assert.IsTrue(browsePageNodes.Any(node => node.Id == id),
                id + " must remain inside the page-wide Browse shortcut owner.");
        Assert.IsTrue(browsePageNodes.Any(node =>
            node.ActionId == PlayniteLibraryActions.Launch));
        Assert.AreEqual(1, browseNodes.Count(node => node.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.LeftTrigger)));
        Assert.AreEqual(1, browseNodes.Count(node => node.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.RightTrigger)));
        Assert.IsTrue(browsePage.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.LeftTrigger &&
            shortcut.ActionId == PlayniteLibraryActions.CollectionPrevious));
        Assert.IsTrue(browsePage.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.RightTrigger &&
            shortcut.ActionId == PlayniteLibraryActions.CollectionNext));
        Assert.IsFalse(browseNodes.Where(node =>
                node.ActionId == PlayniteLibraryActions.Launch).Any(node =>
                node.Shortcuts.Any(shortcut => shortcut.Button is
                    ControllerButton.LeftTrigger or ControllerButton.RightTrigger)),
            "Browse posters must inherit one page-wide collection shortcut owner.");
        var previousCollectionHint = browseNodes.Single(node =>
            node.Id == "playnite-library.collection.hint.previous");
        Assert.IsTrue(Nodes(previousCollectionHint).Any(node =>
            node.Text == "Previous collection"));
        var nextCollectionHint = browseNodes.Single(node =>
            node.Id == "playnite-library.collection.hint.next");
        Assert.IsTrue(Nodes(nextCollectionHint).Any(node =>
            node.Text == "Next collection"));
        var emptyBrowse = new PresentationWidget(PlayniteLibraryPresentation.Render(State(
                Snapshot(WidgetPagedResourceStatus.Ready, []), organization,
                PlayniteLibraryRoute.Browse, [])))
            .RenderSnapshot("playnite-library.presentation.browse.empty", 3);
        var emptyBrowsePage = Nodes(emptyBrowse.Root).Single(node =>
            node.Id == "playnite-library.browse.page");
        Assert.IsTrue(emptyBrowsePage.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.LeftTrigger));
        Assert.IsTrue(emptyBrowsePage.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.RightTrigger));
        Assert.IsTrue(Nodes(emptyBrowsePage).Any(node =>
            node.Id == "playnite-library.browse.empty.action"));
        foreach (var id in new[]
                 {
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
        Assert.IsFalse(browseNodes.Any(node =>
            node.ActionId == "playnite-library.browse.back"));
        Assert.IsFalse(browseNodes.Any(node =>
            node.Id.StartsWith("playnite-library.management", StringComparison.Ordinal) ||
            node.ActionId?.StartsWith("playnite-library.management", StringComparison.Ordinal) == true));
        var favorites = browseNodes.Single(node =>
            node.Id == "playnite-library.filter.favorites");
        Assert.AreEqual(ViewNodeKind.Button, favorites.Kind);
        Assert.IsNull(favorites.IsSelected,
            "Favorites is an ordinary On/Off command, not a selected-state toggle surface.");
        Assert.IsFalse(favorites.StyleClasses.Contains(
            "playnite-library-filter-active", StringComparer.Ordinal));
        Assert.IsFalse(browseNodes.Any(node => node.IsSelected is not null ||
            node.Glyph == WidgetGlyph.Check),
            "Browse filters must remain ordinary commands without selected/check semantics.");

        var activeBrowse = State(collection, organization, PlayniteLibraryRoute.Browse, [])
            with { FavoriteFilter = true, RecentlyPlayed = true };
        var activeNodes = Nodes(new PresentationWidget(
                PlayniteLibraryPresentation.Render(activeBrowse)).RenderSnapshot(
                "playnite-library.presentation.browse.active", 3).Root).ToArray();
        foreach (var id in new[]
                 {
                     PlayniteLibraryActions.FavoritesFilter,
                     PlayniteLibraryActions.RecentlyPlayedFilter,
                 })
            CollectionAssert.Contains(activeNodes.Single(node => node.Id == id)
                .StyleClasses.ToArray(), "playnite-library-filter-active");
        StringAssert.Contains(styles,
            ".playnite-library-filter-active:focused { background:",
            "The active hue must retain an independently visible focused state.");

        var productionSources = Directory.GetFiles(RepositoryRoot(), "*.cs",
                SearchOption.AllDirectories)
            .Where(path => path.Contains(Path.Combine("samples", "PlayniteLibraryWidget"),
                StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();
        Assert.IsFalse(productionSources.Any(source =>
            source.Contains("SearchOpen", StringComparison.Ordinal) ||
            source.Contains("playnite-library.search.open", StringComparison.Ordinal)),
            "The retired Home-to-search action must have no declaration or dispatch path.");
        Assert.IsFalse(productionSources.Any(source =>
            source.Contains("PlayniteLibraryRoute.Management", StringComparison.Ordinal) ||
            source.Contains("playnite-library.management", StringComparison.Ordinal)),
            "Management must remain absent from the route and action surface.");
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
        var browseBackground = browse.Root.Children.Single();
        var browseStage = browseBackground.Children.Single();
        Assert.AreEqual("playnite-library.browse.stage", browseStage.Id);
        CollectionAssert.Contains(browseStage.StyleClasses.ToArray(),
            "playnite-library-surface-stage");
        CollectionAssert.Contains(browseStage.StyleClasses.ToArray(),
            "playnite-library-browse-stage");
        Assert.AreEqual("playnite-library.browse.page",
            browseStage.Children.Single().Id);
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
            ".playnite-library-browse-foreground { width: 100%; height: 100%; min-width: 0px; min-height: 0px; max-width: 1120px;");
        var theme = CompileStyles();
        var browseStyle = theme.Resolve(new WrssElement("stack", null,
            new HashSet<string>(
            [
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
        Assert.IsNull(browseGrid.InitialChildFocusId,
            "Mutable Browse results must not retain native remembered-child authority.");
        var browseQuery = Nodes(browse.Root).Single(node =>
            node.Id == "playnite-library.query");
        Assert.IsNull(browseQuery.InitialChildFocusId,
            "The query row must not claim remembered-child ownership from its native Select controls.");
        Assert.IsTrue(Nodes(browseQuery).Any(node =>
            node.Id == "playnite-library.search"));
        var browseActions = Nodes(browse.Root).Single(node =>
            node.Id == "playnite-library.collection.hints");
        Assert.IsNull(browseActions.InitialChildFocusId);
        Assert.IsFalse(Nodes(browseActions).Any(node => node.IsFocusable));
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
    public void HiddenMatchesHomePosterGeometryAndConnectionShellCentersAcrossWideSurfaces()
    {
        var item = PlayniteLibraryItem.From(Item(
            "app-layout", "saved-layout", "Layout game", "Steam"));
        var display = new PlayniteLibraryDisplayItem(
            item.Value.SavedId, item.Presentation.DisplayName,
            item.Presentation.Source.DisplayName);
        var home = new PresentationWidget(PlayniteLibraryPresentation.Render(State(
                Snapshot(WidgetPagedResourceStatus.Ready, [item]),
                new PlayniteLibraryPrivateState(
                    PlayniteLibraryPrivateState.CurrentVersion, [display]),
                PlayniteLibraryRoute.Library, [])))
            .RenderSnapshot("playnite-library.layout.home", 1);
        var hiddenOrganization = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            ExcludedSavedIds = [display.SavedId],
        };
        var hiddenState = State(
            Snapshot(WidgetPagedResourceStatus.Ready, []),
            hiddenOrganization, PlayniteLibraryRoute.Hidden, []) with
        {
            HiddenRows = [item],
        };
        var hidden = new PresentationWidget(
                PlayniteLibraryPresentation.Render(hiddenState))
            .RenderSnapshot("playnite-library.layout.hidden", 2);
        var homeTile = Nodes(home.Root).Single(node =>
            node.ActionId == PlayniteLibraryActions.Launch);
        var hiddenTile = Nodes(hidden.Root).Single(node =>
            node.ActionId == PlayniteLibraryActions.Restore);
        CollectionAssert.Contains(homeTile.StyleClasses.ToArray(),
            "playnite-library-fixed-tile");
        CollectionAssert.Contains(hiddenTile.StyleClasses.ToArray(),
            "playnite-library-fixed-tile");
        var hiddenGrid = Nodes(hidden.Root).Single(node =>
            node.Id == "playnite-library.hidden.grid");
        Assert.AreEqual(150D, hiddenGrid.GridMinimumColumnWidth);
        Assert.AreEqual(7, hiddenGrid.GridMaximumColumns);
        CollectionAssert.Contains(hiddenGrid.StyleClasses.ToArray(),
            "playnite-library-hidden-grid");
        var theme = CompileStyles();
        var homeStyle = theme.Resolve(new WrssElement("action-surface", null,
            homeTile.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        var hiddenStyle = theme.Resolve(new WrssElement("action-surface", null,
            hiddenTile.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        Assert.AreEqual("150px", homeStyle.Get("width")?.Text);
        Assert.AreEqual(homeStyle.Get("width")?.Text,
            hiddenStyle.Get("width")?.Text);
        Assert.AreEqual(2D / 3D, hiddenStyle.Get("aspect-ratio")?.Number);
        Assert.AreEqual(225D, 150D / hiddenStyle.Get("aspect-ratio")!.Number!.Value,
            0.001D);

        var connection = PlayniteLibraryConnectionPresentation.Render(new(
            PlayniteBridgeConnectionKind.NotConfigured, Busy: false,
            "credential_missing", Interactive: true, Feedback: null));
        CollectionAssert.DoesNotContain(connection.Root.StyleClasses.ToArray(),
            "playnite-library-widget");
        CollectionAssert.Contains(connection.Root.StyleClasses.ToArray(),
            "playnite-library-playnite");
        Assert.AreEqual(WidgetSurfaceAppearance.Transparent,
            connection.Surface!.Appearance);
        Assert.AreEqual(PlayniteLibraryPresentation.Render(hiddenState).Surface!.Appearance,
            connection.Surface.Appearance,
            "Connection and Hidden must share the transparent secondary-page treatment.");
        var categoriesState = State(
            Snapshot(WidgetPagedResourceStatus.Ready, [item]),
            hiddenOrganization, PlayniteLibraryRoute.Categories, []);
        Assert.AreEqual(PlayniteLibraryPresentation.Render(categoriesState).Surface!.Appearance,
            connection.Surface.Appearance,
            "Connection and Categories must share the transparent secondary-page treatment.");
        var connectionRootStyle = theme.Resolve(new WrssElement("stack", null,
            connection.Root.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        Assert.AreEqual("100%", connectionRootStyle.Get("width")?.Text);
        Assert.AreEqual("100%", connectionRootStyle.Get("height")?.Text);
        Assert.IsNull(connectionRootStyle.Get("max-width"));
        Assert.IsNull(connectionRootStyle.Get("max-height"));
        Assert.AreEqual("center", connectionRootStyle.Get("align")?.Text);
        Assert.AreEqual("center", connectionRootStyle.Get("justify")?.Text);
        Assert.AreEqual("12px", connectionRootStyle.Get("padding")?.Text);
        Assert.AreEqual("rgba(0, 0, 0, 0)",
            connectionRootStyle.Get("background")?.Text);
        var shell = Nodes(new PresentationWidget(connection).RenderSnapshot(
                "playnite-library.layout.connection", 4).Root)
            .Single(node => node.Id == "playnite-library.playnite.shell");
        var shellStyle = theme.Resolve(new WrssElement("stack", null,
            shell.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        Assert.AreEqual("100%", shellStyle.Get("width")?.Text);
        Assert.AreEqual("100%", shellStyle.Get("height")?.Text);
        Assert.AreEqual("900px", shellStyle.Get("max-width")?.Text);
        Assert.AreEqual("620px", shellStyle.Get("max-height")?.Text);

        foreach (var (surfaceWidth, surfaceHeight) in new[]
                 {
                     (1920D, 1080D),
                     (3440D, 1440D),
                 })
        {
            var shellWidth = Math.Min(900D, surfaceWidth - 24D);
            var shellHeight = Math.Min(620D, surfaceHeight - 24D);
            var left = (surfaceWidth - shellWidth) / 2D;
            var top = (surfaceHeight - shellHeight) / 2D;
            Assert.AreEqual(surfaceWidth / 2D, left + shellWidth / 2D, 0.001D);
            Assert.AreEqual(surfaceHeight / 2D, top + shellHeight / 2D, 0.001D);
            Assert.IsGreaterThan(0D, left);
            Assert.IsGreaterThan(0D, top);
        }
    }

    [TestMethod, Timeout(30_000)]
    public void BrowseShellAndPackageControlsRemainStableAcrossResourceStates()
    {
        var item = PlayniteLibraryItem.From(Item(
            "app-state", "saved-state", "State game", "Steam"));
        var organization = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion,
            [new(item.Value.SavedId, item.Presentation.DisplayName,
                item.Presentation.Source.DisplayName)]);
        var states = new[]
        {
            ("ready", State(Snapshot(WidgetPagedResourceStatus.Ready, [item]),
                organization, PlayniteLibraryRoute.Browse, [])),
            ("not-loaded", State(Snapshot(WidgetPagedResourceStatus.NotLoaded, []),
                organization, PlayniteLibraryRoute.Browse, [])),
            ("loading", State(Snapshot(WidgetPagedResourceStatus.Loading, []),
                organization, PlayniteLibraryRoute.Browse, [])),
            ("refreshing", State(Snapshot(WidgetPagedResourceStatus.Refreshing, [item]),
                organization, PlayniteLibraryRoute.Browse, [])),
            ("error", State(Snapshot(WidgetPagedResourceStatus.Error, [],
                    error: new WidgetResourceError("fixture_error", "Try again.")),
                organization, PlayniteLibraryRoute.Browse, [])),
            ("empty", State(Snapshot(WidgetPagedResourceStatus.Ready, []),
                PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, [])),
        };

        foreach (var (phase, state) in states)
        {
            var snapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(state))
                .RenderSnapshot("playnite-library.browse." + phase, 10);
            var nodes = Nodes(snapshot.Root).ToArray();
            Assert.AreEqual("playnite-library.root", snapshot.Root.Id, phase);
            CollectionAssert.Contains(snapshot.Root.StyleClasses.ToArray(),
                "playnite-library-browse-surface", phase);
            var background = nodes.Single(node =>
                node.Id == "playnite-library.browse.cinematic");
            Assert.AreEqual(ViewNodeKind.BackgroundSurface, background.Kind, phase);
            CollectionAssert.Contains(background.StyleClasses.ToArray(),
                "playnite-library-browse-background", phase);
            Assert.AreEqual(1, background.Children.Count, phase);
            var stage = background.Children[0];
            Assert.AreEqual("playnite-library.browse.stage", stage.Id, phase);
            CollectionAssert.Contains(stage.StyleClasses.ToArray(),
                "playnite-library-surface-stage", phase);
            CollectionAssert.Contains(stage.StyleClasses.ToArray(),
                "playnite-library-browse-stage", phase);
            Assert.AreEqual(1, stage.Children.Count, phase);
            Assert.AreEqual("playnite-library.browse.page",
                stage.Children[0].Id, phase);
            var foreground = nodes.Single(node =>
                node.Id == "playnite-library.browse.page");
            CollectionAssert.Contains(foreground.StyleClasses.ToArray(),
                "playnite-library-browse-foreground", phase);
            Assert.IsTrue(nodes.Any(node => node.Id == "playnite-library.header"), phase);
            var query = nodes.Single(node => node.Id == "playnite-library.query");
            Assert.IsNull(query.InitialChildFocusId,
                phase + " must leave native Select popup ownership outside the query row.");
            Assert.IsTrue(Nodes(query).Any(node =>
                node.Id == "playnite-library.search"), phase);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count,
                phase + " must remain protocol-valid.");
            if (phase is "ready" or "refreshing")
            {
                var catalogScroll = nodes.Single(node =>
                    node.Id == PlayniteLibraryPresentation.ScrollId);
                Assert.AreEqual(ViewNodeKind.Scroll, catalogScroll.Kind, phase);
                CollectionAssert.Contains(catalogScroll.StyleClasses.ToArray(),
                    "playnite-library-catalog-scroll", phase);
                Assert.AreEqual(item.Key.Value, catalogScroll.CollectionAnchorKey);
                var grid = nodes.Single(node =>
                    node.Id == "playnite-library.browse.grid");
                Assert.IsNull(grid.InitialChildFocusId,
                    phase + " mutable Browse results must not retain remembered focus.");
                var actions = nodes.Single(node =>
                    node.Id == "playnite-library.collection.hints");
                Assert.AreEqual(ViewNodeKind.Row, actions.Kind);
                Assert.IsTrue(actions.Children.Any(node => node.Id == "playnite-library.browse.hint.refresh"));
                Assert.IsTrue(actions.Children.Any(node => node.Id == "playnite-library.hint.options"));
                Assert.IsNull(actions.InitialChildFocusId);
                Assert.IsFalse(Nodes(actions).Any(node => node.IsFocusable));
                Assert.IsTrue(nodes.Any(node => node.ActionId == "playnite-library.launch"),
                    phase + " must retain the admitted catalog while refreshing.");
            }
            else
            {
                Assert.IsFalse(nodes.Any(node =>
                    node.Id == PlayniteLibraryPresentation.ScrollId),
                    phase + " must not publish an empty collection owner.");
                Assert.IsFalse(nodes.Any(node =>
                        node.ActionId == "playnite-library.launch"),
                    phase + " must not retain stale actionable rows.");
            }
        }

        var filtered = State(Snapshot(WidgetPagedResourceStatus.Ready, []),
            organization, PlayniteLibraryRoute.Browse, []) with
        {
            Query = new WidgetAppLibraryQuery { SearchText = "missing" },
        };
        var filteredSnapshot = new PresentationWidget(
                PlayniteLibraryPresentation.Render(filtered))
            .RenderSnapshot("playnite-library.browse.filtered-empty", 11);
        var filteredNodes = Nodes(filteredSnapshot.Root).ToArray();
        Assert.AreEqual("No matching games", filteredNodes.Single(node =>
            node.Id == "playnite-library.browse.empty.title").Text);
        Assert.AreEqual("playnite-library.query.clear", filteredNodes.Single(node =>
            node.Id == "playnite-library.browse.empty.action").ActionId);
        Assert.AreEqual(1, filteredNodes.Count(node =>
            node.Id == "playnite-library.browse.empty"));
        Assert.IsFalse(filteredNodes.Any(node =>
            node.Id.StartsWith("playnite-library.hero", StringComparison.Ordinal) ||
            node.Id.StartsWith("playnite-library.empty", StringComparison.Ordinal) ||
            node.Id == "playnite-library.browse.empty.icon"));

        var trulyEmpty = new PresentationWidget(PlayniteLibraryPresentation.Render(
                State(Snapshot(WidgetPagedResourceStatus.Ready, []),
                    PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, [])))
            .RenderSnapshot("playnite-library.browse.catalog-empty", 12);
        var emptyNodes = Nodes(trulyEmpty.Root).ToArray();
        Assert.AreEqual("No games", emptyNodes.Single(node =>
            node.Id == "playnite-library.browse.empty.title").Text);
        Assert.AreEqual("playnite-library.refresh", emptyNodes.Single(node =>
            node.Id == "playnite-library.browse.empty.action").ActionId);

        var fixedRowsOnly = State(Snapshot(WidgetPagedResourceStatus.Ready, []),
            organization, PlayniteLibraryRoute.Browse, []) with
        {
            Query = new WidgetAppLibraryQuery { SearchText = "State" },
            FixedRows = new([], [], [item]),
        };
        var fixedRowsOnlySnapshot = new PresentationWidget(
                PlayniteLibraryPresentation.Render(fixedRowsOnly))
            .RenderSnapshot("playnite-library.browse.fixed-rows-only", 13);
        var fixedRowsOnlyNodes = Nodes(fixedRowsOnlySnapshot.Root).ToArray();
        Assert.AreEqual("No matching games", fixedRowsOnlyNodes.Single(node =>
            node.Id == "playnite-library.browse.empty.title").Text);
        Assert.AreEqual("playnite-library.search",
            fixedRowsOnlySnapshot.InitialFocusId);
        Assert.IsFalse(fixedRowsOnlyNodes.Any(node =>
            node.Id == "playnite-library.browse.grid"));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(fixedRowsOnlySnapshot).Count,
            "Browse fixed rows must not create an empty catalog or invalid focus target.");

        var theme = CompileStyles();
        var classes = new HashSet<string>(["playnite-library-control"],
            StringComparer.Ordinal);
        var control = theme.Resolve(new WrssElement("button", null, classes));
        Assert.AreEqual("Segoe UI Variable Text, Segoe UI",
            control.Get("font-family")?.Text);
        Assert.AreEqual("13px", control.Get("font-size")?.Text);
        Assert.AreEqual("600", control.Get("font-weight")?.Text);
        Assert.AreEqual("center", control.Get("text-align")?.Text);
        Assert.AreEqual("rgba(15, 24, 35, 0.96)", control.Get("background")?.Text);
        Assert.AreEqual("1px", control.Get("border-width")?.Text);
        Assert.AreEqual("10px", control.Get("corner-radius")?.Text);
        foreach (var state in new[]
                 {
                     WrssPseudoState.Focused,
                     WrssPseudoState.Pressed,
                     WrssPseudoState.Selected,
                     WrssPseudoState.Disabled,
                 })
        {
            var styled = theme.Resolve(new WrssElement("button", null, classes,
                new HashSet<WrssPseudoState>([state])));
            Assert.IsNotNull(styled.Get("background"), state.ToString());
            Assert.IsNotNull(styled.Get("border-color"), state.ToString());
            if (state == WrssPseudoState.Focused)
            {
                Assert.AreEqual("2px", styled.Get("outline-width")?.Text);
                Assert.AreEqual("#ffffff", styled.Get("outline-color")?.Text);
            }
        }
        var search = theme.Resolve(new WrssElement("text-entry", null,
            new HashSet<string>(["playnite-library-control", "playnite-library-search"],
                StringComparer.Ordinal)));
        Assert.AreEqual("start", search.Get("text-align")?.Text);
        Assert.AreEqual("400", search.Get("font-weight")?.Text);
        var emptyAction = emptyNodes.Single(node =>
            node.Id == "playnite-library.browse.empty.action");
        CollectionAssert.Contains(emptyAction.StyleClasses.ToArray(),
            "playnite-library-control");
        CollectionAssert.Contains(emptyAction.StyleClasses.ToArray(),
            "playnite-library-empty-action");
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
            OrganizationBusy: false, Interactive: true,
            new WidgetAppLibraryQuery { InstalledOnly = route != PlayniteLibraryRoute.Browse },
            RecentlyPlayed: false, FavoriteFilter: false, route,
            PlayniteLibraryFixedRows.Empty, HiddenRows: [], sources,
            HeroSavedId: null, HeroIndex: 0)
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("WidgetRail repository root was not found.");
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
