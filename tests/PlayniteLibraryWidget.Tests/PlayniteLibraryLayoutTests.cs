using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
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
        var snapshot = new PresentationWidget(view).RenderSnapshot(
            "playnite-library.presentation", 1);
        var nodes = Nodes(snapshot.Root).ToArray();
        Assert.AreEqual(1, snapshot.Root.Children.Count);
        Assert.AreEqual(ViewNodeKind.BackgroundSurface, snapshot.Root.Children[0].Kind);
        Assert.AreEqual(1, nodes.Count(node => node.Kind == ViewNodeKind.FocusPresentationSurface));
        var library = nodes.Single(node => node.Id == "playnite-library.library.menu");
        Assert.AreEqual("playnite-library.browse.open", library.ActionId);
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
        Assert.IsTrue(nodes.Where(node => node.ActionId == "playnite-library.launch").All(node =>
            node.FocusPresentation is not null && node.ContextActions.Count <= 8));
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
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid", items[0].Key),
            snapshot.InitialFocusId);
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "styles", "default.wrss"));
        StringAssert.Contains(styles,
            ".playnite-library-rail { width: 100%; min-width: 0px; flex-shrink: 0; gap: 12px; }");
        StringAssert.Contains(styles,
            ".playnite-library-tile { width: 200px; min-width: 200px; flex-basis: 200px; flex-grow: 0; flex-shrink: 0; aspect-ratio: 2/3; min-height: 0px; }");
        StringAssert.Contains(styles,
            ".wrail-poster-tile__scrim { corner-radius: 12px; overflow: clip; }");
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
        Assert.AreEqual("Fallback game, Windows, Ready",
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
        Assert.IsFalse(Nodes(browse.Root).Any(node => node.FocusPresentation is not null));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(browse).Count);
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

    private sealed class PresentationWidget(WidgetView view) : Widget
    {
        public override WidgetView Render() => view;
    }
}
