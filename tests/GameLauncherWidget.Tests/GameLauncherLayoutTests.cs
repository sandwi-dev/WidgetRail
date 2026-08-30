using WidgetRail.Samples.GameLauncher;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace WidgetRail.Tests.GameLauncher;

[TestClass]
public sealed class GameLauncherLayoutTests
{
    [TestMethod, Timeout(30_000)]
    public void EveryRouteHasOneFixedChromeAndOneBoundedCollectionViewport()
    {
        var items = Enumerable.Range(0, 64)
            .Select(index => GameLauncherItem.From(Item(
                $"app-{index:D3}", $"saved-{index:D3}", $"Game {index:D3}",
                index % 2 == 0 ? "Steam" : "Windows")))
            .ToArray();
        var display = items.Select(item => new GameLauncherDisplayItem(
            item.Value.SavedId,
            item.Value.Presentation.DisplayName,
            item.Value.Presentation.Source.DisplayName))
            .ToArray();
        var organized = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, display)
        {
            RecentSavedIds = [display[0].SavedId],
            ManualSavedIds = [display[1].SavedId],
            ExcludedSavedIds = [display[2].SavedId],
        };
        var sources = new WidgetAppLibrarySource[]
        {
            new("steam", "Steam", WidgetAppLibrarySourceHealth.Healthy, 7, "healthy"),
            new("windows", "Windows", WidgetAppLibrarySourceHealth.Degraded, 4,
                "source_degraded"),
        };
        var ready = Snapshot(WidgetPagedResourceStatus.Ready, items,
            before: "cursor.before", after: "cursor.after");
        var cases = new (string Name, GameLauncherPresentationState State,
            ScrollAxis? Axis)[]
        {
            ("library", State(ready, organized, GameLauncherRoute.Library, sources),
                ScrollAxis.Horizontal),
            ("add", State(ready, organized, GameLauncherRoute.AddGames, sources),
                ScrollAxis.Vertical),
            ("running", State(ready, organized, GameLauncherRoute.Running, sources),
                ScrollAxis.Vertical),
            ("hidden", State(ready, organized, GameLauncherRoute.Hidden, sources),
                ScrollAxis.Vertical),
            ("warm", State(Snapshot(WidgetPagedResourceStatus.Loading, []), organized,
                GameLauncherRoute.Library, sources), ScrollAxis.Horizontal),
            ("loading", State(Snapshot(WidgetPagedResourceStatus.Loading, []),
                GameLauncherPrivateState.Empty, GameLauncherRoute.Library, sources), null),
            ("error", State(Snapshot(WidgetPagedResourceStatus.Error, [],
                    error: new WidgetResourceError("platform_unavailable", "Try again.")),
                GameLauncherPrivateState.Empty, GameLauncherRoute.Library, sources), null),
            ("empty", State(Snapshot(WidgetPagedResourceStatus.Ready, []),
                GameLauncherPrivateState.Empty, GameLauncherRoute.Library, sources), null),
        };

        foreach (var scenario in cases)
        {
            var view = GameLauncherPresentation.Render(scenario.State);
            ViewSnapshot snapshot;
            try
            {
                snapshot = new PresentationWidget(view).RenderSnapshot(
                    "game-launcher.layout", 1);
            }
            catch (ProtocolValidationException exception)
            {
                Assert.Fail($"{scenario.Name}: " + string.Join(Environment.NewLine,
                    exception.Errors.Select(error =>
                        $"{error.Path}: {error.Code}: {error.Message}")));
                throw;
            }
            var nodes = Nodes(snapshot.Root).ToArray();
            Assert.AreEqual(nodes.Length, nodes.Select(node => node.Id).Distinct(
                StringComparer.Ordinal).Count(), $"{scenario.Name}: duplicate semantic ID");

            var root = snapshot.Root;
            Assert.AreEqual(4, root.Children.Count,
                $"{scenario.Name}: fixed/main root ownership changed");
            CollectionAssert.AreEqual(new[]
            {
                "game-launcher.header",
                "game-launcher.sources",
                "game-launcher.query",
            }, root.Children.Take(3).Select(child => child.Id).ToArray(),
                $"{scenario.Name}: fixed chrome order changed");
            foreach (var fixedId in new[]
                     {
                         "game-launcher.header", "game-launcher.sources",
                         "game-launcher.query",
                     })
                Assert.IsTrue(nodes.Single(node => node.Id == fixedId).StyleClasses.Contains(
                    "game-launcher-fixed", StringComparer.Ordinal),
                    $"{scenario.Name}: {fixedId} is not fixed chrome");
            Assert.IsTrue(nodes.Single(node => node.Id == "game-launcher.content")
                .StyleClasses.Contains("game-launcher-main", StringComparer.Ordinal),
                $"{scenario.Name}: main region does not own remaining height");

            if (scenario.Name == "library")
            {
                var favorites = nodes.Single(node =>
                    node.Id == "game-launcher.filter.favorites");
                CollectionAssert.AreEqual(
                    new[] { "wrail-switch", "wrail-switch--off" },
                    favorites.StyleClasses.ToArray());
                Assert.AreEqual("game-launcher.filter.favorites", favorites.ActionId);
                Assert.AreEqual("Favorites, Off", favorites.AccessibilityLabel);

                var allInstalled = nodes.Single(node =>
                    node.ActionId == "game-launcher.collection.select.all");
                CollectionAssert.AreEqual(
                    new[] { "wrail-switch", "wrail-switch--on" },
                    allInstalled.StyleClasses.ToArray());
                Assert.AreEqual("All installed, On", allInstalled.AccessibilityLabel);
            }

            Assert.AreEqual(ResponsiveVisibility.CompactOnly,
                nodes.Single(node => node.Id == "game-launcher.compact.title").VisibleWhen,
                $"{scenario.Name}: compact title branch missing");
            Assert.AreEqual(ResponsiveVisibility.ExpandedOnly,
                nodes.Single(node => node.Id == "game-launcher.header.expanded").VisibleWhen,
                $"{scenario.Name}: expanded header branch missing");
            Assert.AreEqual(ResponsiveVisibility.CompactOnly,
                nodes.Single(node => node.Id == "game-launcher.sources.compact").VisibleWhen,
                $"{scenario.Name}: compact source summary missing");
            Assert.AreEqual(ResponsiveVisibility.ExpandedOnly,
                nodes.Single(node => node.Id == "game-launcher.sources.expanded").VisibleWhen,
                $"{scenario.Name}: expanded source detail missing");

            var collection = nodes.Where(node => node.Kind == ViewNodeKind.Scroll &&
                node.Id == GameLauncherPresentation.ScrollId).ToArray();
            Assert.AreEqual(scenario.Axis is null ? 0 : 1, collection.Length,
                $"{scenario.Name}: collection scroll ownership changed");
            if (scenario.Axis is { } axis)
            {
                Assert.AreEqual(axis, collection[0].ScrollAxis,
                    $"{scenario.Name}: collection axis changed");
                Assert.IsTrue(collection[0].StyleClasses.Contains(
                    "game-launcher-scroll", StringComparer.Ordinal),
                    $"{scenario.Name}: collection viewport does not own remaining height");
                Assert.IsTrue(nodes.Any(node => node.Id == collection[0].Id),
                    $"{scenario.Name}: collection viewport escaped the main region");
            }
            if (scenario.State.Route == GameLauncherRoute.Library)
                Assert.AreEqual(1, nodes.Count(node => node.Id == "game-launcher.hero"),
                    $"{scenario.Name}: Library route omitted its single hero region");

            var filters = nodes.SingleOrDefault(node => node.Id == "game-launcher.filters");
            Assert.AreEqual(ScrollAxis.Horizontal,
                (filters ?? nodes.Single(node => node.Id == "game-launcher.query")).ScrollAxis,
                $"{scenario.Name}: filters are not predictably reachable at narrow widths");
            var actions = nodes.SingleOrDefault(node => node.Id == "game-launcher.actions");
            if (actions is not null)
                Assert.AreEqual(ScrollAxis.Horizontal, actions.ScrollAxis,
                    $"{scenario.Name}: footer actions are not predictably reachable");
            var footer = nodes.SingleOrDefault(node =>
                node.Id == "game-launcher.organization.hints");
            if (footer is not null)
                Assert.IsTrue(footer.StyleClasses.Contains(
                    "game-launcher-footer", StringComparer.Ordinal),
                    $"{scenario.Name}: guidance is not a fixed footer");
        }

        var libraryView = GameLauncherPresentation.Render(cases[0].State);
        var firstRender = new PresentationWidget(libraryView).RenderSnapshot(
            "game-launcher.layout", 20);
        var reopened = new PresentationWidget(libraryView).RenderSnapshot(
            "game-launcher.layout", 21);
        Assert.AreEqual(JsonSerializer.Serialize(firstRender.Root),
            JsonSerializer.Serialize(reopened.Root),
            "Reopening an unchanged presentation changed its semantic tree.");
        foreach (var profile in new[]
                 {
                     (Name: "minimum-100", Width: 420, Height: 340, Scale: 1.0),
                     (Name: "compact-100", Width: 520, Height: 420, Scale: 1.0),
                     (Name: "compact-150", Width: 520, Height: 420, Scale: 1.5),
                     (Name: "standard-100", Width: 960, Height: 600, Scale: 1.0),
                     (Name: "standard-150", Width: 960, Height: 600, Scale: 1.5),
                     (Name: "wide-100", Width: 1180, Height: 700, Scale: 1.0),
                     (Name: "wide-150", Width: 1180, Height: 700, Scale: 1.5),
                 })
        {
            var compact = profile.Width < 960 || profile.Height < 540;
            var visible = VisibleNodes(firstRender.Root, compact).ToArray();
            Assert.AreEqual(compact,
                visible.Any(node => node.Id == "game-launcher.compact.title"),
                $"{profile.Name}: wrong title branch at {profile.Scale:P0} scale");
            Assert.AreEqual(!compact,
                visible.Any(node => node.Id == "game-launcher.header.expanded"),
                $"{profile.Name}: wrong expanded header at {profile.Scale:P0} scale");
            Assert.AreEqual(compact,
                visible.Any(node => node.Id == "game-launcher.compact.status" &&
                    node.Text == cases[0].State.Status),
                $"{profile.Name}: compact status is not truthful and visible");
            var scroll = visible.Single(node =>
                node.Id == GameLauncherPresentation.ScrollId);
            var scrollNodes = Nodes(scroll).ToArray();
            foreach (var index in new[] { 0, items.Length / 2, items.Length - 1 })
                Assert.IsTrue(scrollNodes.Any(node => node.Id ==
                    GameLauncherIdentity.FocusId("grid", items[index].Key)),
                    $"{profile.Name}: item {index} is not reachable in the collection viewport");
            Assert.IsFalse(string.IsNullOrWhiteSpace(scroll.CollectionAnchorKey),
                $"{profile.Name}: collection viewport omitted its stable anchor");
            var controllerHints = visible.Single(node =>
                node.Id == "game-launcher.organization.hints");
            Assert.AreEqual(ViewNodeKind.Row, controllerHints.Kind,
                $"{profile.Name}: wrapping controller hints are not row-owned");
            Assert.IsNull(controllerHints.ScrollAxis,
                $"{profile.Name}: controller hints unexpectedly own a scroll viewport");
            Assert.IsTrue(controllerHints.StyleClasses.Contains(
                "game-launcher-footer", StringComparer.Ordinal),
                $"{profile.Name}: controller hints lost their responsive footer style");
            var focused = visible.Single(node => node.Id == firstRender.InitialFocusId);
            Assert.AreEqual("game-launcher.launch", focused.ActionId,
                $"{profile.Name}: focused game lost its primary action");
            Assert.IsTrue(visible.Any(node => node.Id == "game-launcher.collections"),
                $"{profile.Name}: collection navigation is not visible");
            var search = visible.Single(node => node.Id == "game-launcher.search");
            Assert.AreEqual(ViewNodeKind.TextEntry, search.Kind,
                $"{profile.Name}: Search does not use the host-owned TextEntry");
            Assert.AreEqual("game-launcher.search.commit", search.ActionId,
                $"{profile.Name}: Search changed its exact commit action");
            Assert.IsTrue(Nodes(controllerHints).Any(node =>
                    node.Id.StartsWith("game-launcher.hint.", StringComparison.Ordinal)),
                $"{profile.Name}: controller help is not visible");
            foreach (var secondaryId in new[]
                     {
                         "game-launcher.hero", "game-launcher.filters",
                         "game-launcher.actions",
                     })
                Assert.AreEqual(!compact, visible.Any(node => node.Id == secondaryId),
                    $"{profile.Name}: {secondaryId} responsive ownership is wrong");
        }

        var stylePath = Path.Combine(AppContext.BaseDirectory, "styles", "default.wrss");
        var styles = File.ReadAllText(stylePath);
        StringAssert.Contains(styles,
            ".game-launcher-fixed { flex-shrink: 0; }");
        StringAssert.Contains(styles,
            ".game-launcher-main { flex-grow: 1; min-height: 0; }");
        StringAssert.Contains(styles,
            ".game-launcher-scroll { flex-grow: 1; min-height: 0;");
        StringAssert.Contains(styles,
            ".game-launcher-hero { flex-shrink: 0; min-height: 150px;");
        StringAssert.Contains(styles,
            ".game-launcher-rail { flex-shrink: 0; min-height: 150px;");
        StringAssert.Contains(styles,
            ".game-launcher-footer { flex-shrink: 0; flex-wrap: wrap;");
    }

    [TestMethod, Timeout(30_000)]
    public void CurrentDeclarativeLibraryPreservesExactGameAuthorityAndSemanticRegions()
    {
        var items = Enumerable.Range(0, 64)
            .Select(index => GameLauncherItem.From(Item(
                $"app-{index:D3}", $"saved-{index:D3}",
                index == 0 ? new string('L', 96) : $"Game {index:D3}",
                "Fixture", artworkHandle: null)))
            .ToArray();
        var display = items.Select(item => new GameLauncherDisplayItem(
            item.Value.SavedId, item.Presentation.DisplayName,
            item.Presentation.Source.DisplayName)).ToArray();
        var collection = Snapshot(WidgetPagedResourceStatus.Ready, items,
            after: "cursor.after");
        var organization = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, display);
        var view = GameLauncherPresentation.Render(State(
            collection, organization, GameLauncherRoute.Library, []));
        var snapshot = new PresentationWidget(view).RenderSnapshot(
            "game-launcher.presentation", 1);
        var nodes = Nodes(snapshot.Root).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "game-launcher.header",
            "game-launcher.sources.pending",
            "game-launcher.query",
            "game-launcher.content",
        }, snapshot.Root.Children.Select(child => child.Id).ToArray());
        var collectionItems = nodes.Where(node => node.CollectionItemKey is not null)
            .Select(node => node.CollectionItemKey!)
            .ToArray();
        CollectionAssert.AreEquivalent(items.Select(item => item.Key.Value).ToArray(),
            collectionItems);
        var hintRegion = nodes.Single(node =>
            node.Id == "game-launcher.organization.hints");
        Assert.AreEqual(ViewNodeKind.Row, hintRegion.Kind,
            "controller hints must be a non-scroll row for wrapping");
        Assert.IsNull(hintRegion.ScrollAxis,
            "controller hints unexpectedly own scrolling");
        Assert.IsTrue(Nodes(hintRegion).Any(node =>
            node.Id.StartsWith("game-launcher.hint.", StringComparison.Ordinal)));
        Assert.AreEqual(GameLauncherIdentity.FocusId("grid", items[0].Key),
            snapshot.InitialFocusId);
        Assert.AreEqual(new string('L', 96),
            nodes.Single(node => node.Id == "game-launcher.hero.title").Text);
        var heroArtwork = nodes.Single(node =>
            node.Id == "game-launcher.hero.artwork");
        Assert.IsNull(heroArtwork.ArtworkHandle);
        Assert.IsNotNull(heroArtwork.Glyph);
    }

    [TestMethod, Timeout(30_000)]
    public void DetailsProjectionIsBoundedDeterministicAndPreservesLongLabels()
    {
        var title = new string('T', 96);
        var source = new string('S', 64);
        var item = GameLauncherItem.From(Item(
            "app-details", "saved-details", title, source));
        var selection = new GameLauncherDetailsSelection(
            item.Value.SavedId, item.Key,
            GameLauncherIdentity.FocusId("grid", item.Key),
            item.Value.Presentation.DisplayName,
            item.Value.Presentation.Source.DisplayName,
            PageBumpers: false);
        var organization = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion,
            [new(item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName)])
        {
            FavoriteSavedIds = [item.Value.SavedId],
            VariantGroups = [new("variant.details",
                [item.Value.SavedId, "saved-other"], item.Value.SavedId)],
        };
        var collection = Snapshot(WidgetPagedResourceStatus.Ready, [item]);
        var state = GameLauncherDetailsPolicy.Project(selection, collection,
            GameLauncherFixedRows.Empty, organization, null,
            new Dictionary<string, GameLauncherLaunchState>(), "Ready", null,
            false, true);
        var view = GameLauncherDetailsPresentation.Render(state);
        var first = new PresentationWidget(view).RenderSnapshot("details.layout", 1);
        var second = new PresentationWidget(view).RenderSnapshot("details.layout", 2);

        Assert.AreEqual(JsonSerializer.Serialize(first.Root),
            JsonSerializer.Serialize(second.Root));
        Assert.AreEqual(title, Nodes(first.Root).Single(node =>
            node.Id == "game-launcher.details.title").Text);
        StringAssert.Contains(Nodes(first.Root).Single(node =>
            node.Id == "game-launcher.details.source").Text!, source);
        Assert.AreEqual(1, Nodes(first.Root).Count(node =>
            node.Kind == ViewNodeKind.Scroll && node.ScrollAxis == ScrollAxis.Vertical));
        Assert.IsLessThan(40, Nodes(first.Root).Count());
        Assert.AreEqual(WidgetSurfaceMode.Wide, first.Surface?.Mode);
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, first.Surface?.WidthMode);
        Assert.AreEqual(WidgetSurfaceAxisMode.FillAvailable, first.Surface?.HeightMode);
        Assert.AreEqual(1600, first.Surface?.PreferredWidth);
        Assert.AreEqual(1200, first.Surface?.PreferredHeight);
        foreach (var profile in new[]
                 {
                     (Name: "compact", Width: 420, Height: 340, Scale: 1.0),
                     (Name: "standard", Width: 640, Height: 480, Scale: 1.25),
                     (Name: "wide-150", Width: 980, Height: 700, Scale: 1.5),
                 })
        {
            var nodes = Nodes(first.Root).ToArray();
            Assert.IsTrue(nodes.Any(node =>
                node.Id == "game-launcher.details.launch" && node.IsDisabled is not true),
                $"{profile.Name}: launch is not reachable at {profile.Scale:P0}.");
            Assert.IsTrue(nodes.Any(node =>
                node.Id == "game-launcher.details.variant" &&
                node.Text == "Choose another variant"),
                $"{profile.Name}: variant selection is not semantically reachable.");
            Assert.AreEqual(1, nodes.Count(node => node.Id ==
                "game-launcher.details.scroll"),
                $"{profile.Name}: details scroll ownership changed.");
        }

        var busyState = GameLauncherDetailsPolicy.Project(selection, collection,
            GameLauncherFixedRows.Empty, organization, item.Value.SavedId,
            new Dictionary<string, GameLauncherLaunchState>(), "Pending",
            item.Value.SavedId, true, true);
        var busy = new PresentationWidget(
            GameLauncherDetailsPresentation.Render(busyState))
            .RenderSnapshot("details.busy", 3);
        Assert.IsTrue(Nodes(busy.Root).Single(node =>
            node.Id == "game-launcher.details.launch").IsBusy);
        Assert.IsTrue(Nodes(busy.Root).Where(node => node.ActionId is
                "game-launcher.launch" or "game-launcher.favorite" or
                "game-launcher.hide" or "game-launcher.variant" or
                "game-launcher.prefer")
            .All(node => node.IsDisabled is true));
    }

    [TestMethod, Timeout(30_000)]
    public void HeroRailPolicyBoundsLongFallbackAndSelectedState()
    {
        var longTitle = new string('H', 96);
        var items = Enumerable.Range(0, 20).Select(index =>
            GameLauncherItem.From(Item(
                $"app-hero-{index:D2}", $"saved-hero-{index:D2}",
                index == 7 ? longTitle : $"Hero {index:D2}",
                index % 2 == 0 ? "Steam" : "Windows",
                index == 7 ? null :
                    $"hero.art.{index:D2}.0123456789abcdef0123456789abcdef")))
            .ToArray();
        var organization = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion,
            items.Select(item => new GameLauncherDisplayItem(
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
            GameLauncherRoute.Library, [] ) with
        {
            HeroSavedId = items[7].Value.SavedId,
            HeroIndex = 7,
        };
        var model = GameLauncherHeroRailPolicy.Project(
            state, state.HeroSavedId, state.HeroIndex);
        Assert.AreEqual(20, model.Items.Count);
        Assert.AreEqual(items[7].Value.SavedId, model.Selected?.Display.SavedId);
        var snapshot = new PresentationWidget(GameLauncherPresentation.Render(state))
            .RenderSnapshot("hero.layout", 30);
        var nodes = Nodes(snapshot.Root).ToArray();
        Assert.AreEqual(longTitle, nodes.Single(node =>
            node.Id == "game-launcher.hero.title").Text);
        Assert.IsNull(nodes.Single(node =>
            node.Id == "game-launcher.hero.artwork").ArtworkHandle,
            "Missing selected artwork must use a semantic fallback.");
        StringAssert.Contains(nodes.Single(node =>
            node.Id == "game-launcher.hero.state").Text!, "Favorite");
        StringAssert.Contains(nodes.Single(node =>
            node.Id == "game-launcher.hero.state").Text!, "Preferred variant");
        Assert.IsLessThanOrEqualTo(20 * 8 + 32, nodes.Length,
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
        var fallback = GameLauncherHeroRailPolicy.Project(
            filtered, state.HeroSavedId, state.HeroIndex);
        Assert.AreEqual(items[8].Value.SavedId, fallback.Selected?.Display.SavedId,
            "A removed selection must choose the nearest retained rail position.");
    }

    private static GameLauncherPresentationState State(
        WidgetCursorResourceSnapshot<GameLauncherItem> collection,
        GameLauncherPrivateState organization,
        GameLauncherRoute route,
        IReadOnlyList<WidgetAppLibrarySource> sources) => new(
            collection, organization, "Ready", null,
            new Dictionary<string, GameLauncherLaunchState>(StringComparer.Ordinal),
            OrganizationBusy: false, Interactive: true, new WidgetAppLibraryQuery(),
            GameLauncherRecentMode.Off, FavoriteFilter: false, route,
            GameLauncherFixedRows.Empty, sources, HeroSavedId: null, HeroIndex: 0)
        {
            Collections = GameLauncherCollectionPolicy.Options(
                organization, organization.ProvenSources.ToArray(),
                GameLauncherCollectionPolicy.AllInstalled),
        };

    private static WidgetCursorResourceSnapshot<GameLauncherItem> Snapshot(
        WidgetPagedResourceStatus status,
        IReadOnlyList<GameLauncherItem> items,
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
