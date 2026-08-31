using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace WidgetRail.Tests.PlayniteLibrary;

[TestClass]
public sealed class PlayniteLibraryLayoutTests
{
    [TestMethod, Timeout(30_000)]
    public void EveryRouteHasOneFixedChromeAndOneBoundedCollectionViewport()
    {
        var items = Enumerable.Range(0, 64)
            .Select(index => PlayniteLibraryItem.From(Item(
                $"app-{index:D3}", $"saved-{index:D3}", $"Game {index:D3}",
                index % 2 == 0 ? "Steam" : "Windows")))
            .ToArray();
        var display = items.Select(item => new PlayniteLibraryDisplayItem(
            item.Value.SavedId,
            item.Value.Presentation.DisplayName,
            item.Value.Presentation.Source.DisplayName))
            .ToArray();
        var organized = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, display)
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
        var cases = new (string Name, PlayniteLibraryPresentationState State,
            ScrollAxis? Axis)[]
        {
            ("library", State(ready, organized, PlayniteLibraryRoute.Library, sources),
                ScrollAxis.Horizontal),
            ("add", State(ready, organized, PlayniteLibraryRoute.AddGames, sources),
                ScrollAxis.Vertical),
            ("running", State(ready, organized, PlayniteLibraryRoute.Running, sources),
                ScrollAxis.Vertical),
            ("hidden", State(ready, organized, PlayniteLibraryRoute.Hidden, sources),
                ScrollAxis.Vertical),
            ("warm", State(Snapshot(WidgetPagedResourceStatus.Loading, []), organized,
                PlayniteLibraryRoute.Library, sources), ScrollAxis.Horizontal),
            ("loading", State(Snapshot(WidgetPagedResourceStatus.Loading, []),
                PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, sources), null),
            ("error", State(Snapshot(WidgetPagedResourceStatus.Error, [],
                    error: new WidgetResourceError("platform_unavailable", "Try again.")),
                PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, sources), null),
            ("empty", State(Snapshot(WidgetPagedResourceStatus.Ready, []),
                PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, sources), null),
        };

        foreach (var scenario in cases)
        {
            var view = PlayniteLibraryPresentation.Render(scenario.State);
            ViewSnapshot snapshot;
            try
            {
                snapshot = new PresentationWidget(view).RenderSnapshot(
                    "playnite-library.layout", 1);
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
                "playnite-library.header",
                "playnite-library.sources",
                "playnite-library.query",
            }, root.Children.Take(3).Select(child => child.Id).ToArray(),
                $"{scenario.Name}: fixed chrome order changed");
            foreach (var fixedId in new[]
                     {
                         "playnite-library.header", "playnite-library.sources",
                         "playnite-library.query",
                     })
                Assert.IsTrue(nodes.Single(node => node.Id == fixedId).StyleClasses.Contains(
                    "playnite-library-fixed", StringComparer.Ordinal),
                    $"{scenario.Name}: {fixedId} is not fixed chrome");
            Assert.IsTrue(nodes.Single(node => node.Id == "playnite-library.content")
                .StyleClasses.Contains("playnite-library-main", StringComparer.Ordinal),
                $"{scenario.Name}: main region does not own remaining height");

            if (scenario.Name == "library")
            {
                var favorites = nodes.Single(node =>
                    node.Id == "playnite-library.filter.favorites");
                CollectionAssert.AreEqual(
                    new[] { "wrail-switch", "wrail-switch--off" },
                    favorites.StyleClasses.ToArray());
                Assert.AreEqual("playnite-library.filter.favorites", favorites.ActionId);
                Assert.AreEqual("Favorites, Off", favorites.AccessibilityLabel);

                var allInstalled = nodes.Single(node =>
                    node.ActionId == "playnite-library.collection.select.all");
                CollectionAssert.AreEqual(
                    new[] { "wrail-switch", "wrail-switch--on" },
                    allInstalled.StyleClasses.ToArray());
                Assert.AreEqual("All installed, On", allInstalled.AccessibilityLabel);
            }

            Assert.AreEqual(ResponsiveVisibility.CompactOnly,
                nodes.Single(node => node.Id == "playnite-library.compact.title").VisibleWhen,
                $"{scenario.Name}: compact title branch missing");
            Assert.AreEqual(ResponsiveVisibility.ExpandedOnly,
                nodes.Single(node => node.Id == "playnite-library.header.expanded").VisibleWhen,
                $"{scenario.Name}: expanded header branch missing");
            Assert.AreEqual(ResponsiveVisibility.CompactOnly,
                nodes.Single(node => node.Id == "playnite-library.sources.compact").VisibleWhen,
                $"{scenario.Name}: compact source summary missing");
            Assert.AreEqual(ResponsiveVisibility.ExpandedOnly,
                nodes.Single(node => node.Id == "playnite-library.sources.expanded").VisibleWhen,
                $"{scenario.Name}: expanded source detail missing");

            var collection = nodes.Where(node => node.Kind == ViewNodeKind.Scroll &&
                node.Id == PlayniteLibraryPresentation.ScrollId).ToArray();
            Assert.AreEqual(scenario.Axis is null ? 0 : 1, collection.Length,
                $"{scenario.Name}: collection scroll ownership changed");
            if (scenario.Axis is { } axis)
            {
                Assert.AreEqual(axis, collection[0].ScrollAxis,
                    $"{scenario.Name}: collection axis changed");
                Assert.IsTrue(collection[0].StyleClasses.Contains(
                    "playnite-library-scroll", StringComparer.Ordinal),
                    $"{scenario.Name}: collection viewport does not own remaining height");
                Assert.IsTrue(nodes.Any(node => node.Id == collection[0].Id),
                    $"{scenario.Name}: collection viewport escaped the main region");
            }
            if (scenario.State.Route == PlayniteLibraryRoute.Library)
                Assert.AreEqual(1, nodes.Count(node => node.Id == "playnite-library.hero"),
                    $"{scenario.Name}: Library route omitted its single hero region");

            var filters = nodes.SingleOrDefault(node => node.Id == "playnite-library.filters");
            Assert.AreEqual(ScrollAxis.Horizontal,
                (filters ?? nodes.Single(node => node.Id == "playnite-library.query")).ScrollAxis,
                $"{scenario.Name}: filters are not predictably reachable at narrow widths");
            var actions = nodes.SingleOrDefault(node => node.Id == "playnite-library.actions");
            if (actions is not null)
                Assert.AreEqual(ScrollAxis.Horizontal, actions.ScrollAxis,
                    $"{scenario.Name}: footer actions are not predictably reachable");
            var footer = nodes.SingleOrDefault(node =>
                node.Id == "playnite-library.organization.hints");
            if (footer is not null)
                Assert.IsTrue(footer.StyleClasses.Contains(
                    "playnite-library-footer", StringComparer.Ordinal),
                    $"{scenario.Name}: guidance is not a fixed footer");
        }

        var libraryView = PlayniteLibraryPresentation.Render(cases[0].State);
        var firstRender = new PresentationWidget(libraryView).RenderSnapshot(
            "playnite-library.layout", 20);
        var reopened = new PresentationWidget(libraryView).RenderSnapshot(
            "playnite-library.layout", 21);
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
                visible.Any(node => node.Id == "playnite-library.compact.title"),
                $"{profile.Name}: wrong title branch at {profile.Scale:P0} scale");
            Assert.AreEqual(!compact,
                visible.Any(node => node.Id == "playnite-library.header.expanded"),
                $"{profile.Name}: wrong expanded header at {profile.Scale:P0} scale");
            Assert.AreEqual(compact,
                visible.Any(node => node.Id == "playnite-library.compact.status" &&
                    node.Text == cases[0].State.Status),
                $"{profile.Name}: compact status is not truthful and visible");
            var scroll = visible.Single(node =>
                node.Id == PlayniteLibraryPresentation.ScrollId);
            var scrollNodes = Nodes(scroll).ToArray();
            foreach (var index in new[] { 0, items.Length / 2, items.Length - 1 })
                Assert.IsTrue(scrollNodes.Any(node => node.Id ==
                    PlayniteLibraryIdentity.FocusId("grid", items[index].Key)),
                    $"{profile.Name}: item {index} is not reachable in the collection viewport");
            Assert.IsFalse(string.IsNullOrWhiteSpace(scroll.CollectionAnchorKey),
                $"{profile.Name}: collection viewport omitted its stable anchor");
            var controllerHints = visible.Single(node =>
                node.Id == "playnite-library.organization.hints");
            Assert.AreEqual(ViewNodeKind.Row, controllerHints.Kind,
                $"{profile.Name}: wrapping controller hints are not row-owned");
            Assert.IsNull(controllerHints.ScrollAxis,
                $"{profile.Name}: controller hints unexpectedly own a scroll viewport");
            Assert.IsTrue(controllerHints.StyleClasses.Contains(
                "playnite-library-footer", StringComparer.Ordinal),
                $"{profile.Name}: controller hints lost their responsive footer style");
            var focused = visible.Single(node => node.Id == firstRender.InitialFocusId);
            Assert.AreEqual("playnite-library.launch", focused.ActionId,
                $"{profile.Name}: focused game lost its primary action");
            Assert.IsTrue(visible.Any(node => node.Id == "playnite-library.collections"),
                $"{profile.Name}: collection navigation is not visible");
            var search = visible.Single(node => node.Id == "playnite-library.search");
            Assert.AreEqual(ViewNodeKind.TextEntry, search.Kind,
                $"{profile.Name}: Search does not use the host-owned TextEntry");
            Assert.AreEqual("playnite-library.search.commit", search.ActionId,
                $"{profile.Name}: Search changed its exact commit action");
            Assert.IsTrue(Nodes(controllerHints).Any(node =>
                    node.Id.StartsWith("playnite-library.hint.", StringComparison.Ordinal)),
                $"{profile.Name}: controller help is not visible");
            foreach (var secondaryId in new[]
                     {
                         "playnite-library.hero", "playnite-library.filters",
                         "playnite-library.actions",
                     })
                Assert.AreEqual(!compact, visible.Any(node => node.Id == secondaryId),
                    $"{profile.Name}: {secondaryId} responsive ownership is wrong");
        }

        var stylePath = Path.Combine(AppContext.BaseDirectory, "styles", "default.wrss");
        var styles = File.ReadAllText(stylePath);
        StringAssert.Contains(styles,
            ".playnite-library-fixed { flex-shrink: 0; }");
        StringAssert.Contains(styles,
            ".playnite-library-main { flex-grow: 1; min-height: 0; }");
        StringAssert.Contains(styles,
            ".playnite-library-scroll { flex-grow: 1; min-height: 0;");
        StringAssert.Contains(styles,
            ".playnite-library-hero { flex-shrink: 0; min-height: 150px;");
        StringAssert.Contains(styles,
            ".playnite-library-rail { flex-shrink: 0; min-height: 150px;");
        StringAssert.Contains(styles,
            ".playnite-library-footer { flex-shrink: 0; flex-wrap: wrap;");
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
            PlayniteLibraryPrivateState.CurrentVersion, display);
        var view = PlayniteLibraryPresentation.Render(State(
            collection, organization, PlayniteLibraryRoute.Library, []));
        var snapshot = new PresentationWidget(view).RenderSnapshot(
            "playnite-library.presentation", 1);
        var nodes = Nodes(snapshot.Root).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "playnite-library.header",
            "playnite-library.sources.pending",
            "playnite-library.query",
            "playnite-library.content",
        }, snapshot.Root.Children.Select(child => child.Id).ToArray());
        var collectionItems = nodes.Where(node => node.CollectionItemKey is not null)
            .Select(node => node.CollectionItemKey!)
            .ToArray();
        CollectionAssert.AreEquivalent(items.Select(item => item.Key.Value).ToArray(),
            collectionItems);
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
        Assert.AreEqual(new string('L', 96),
            nodes.Single(node => node.Id == "playnite-library.hero.title").Text);
        var heroArtwork = nodes.Single(node =>
            node.Id == "playnite-library.hero.artwork");
        Assert.IsNull(heroArtwork.ArtworkHandle);
        Assert.IsNotNull(heroArtwork.Glyph);
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
    public void DetailsProjectionIsBoundedDeterministicAndPreservesLongLabels()
    {
        var title = new string('T', 96);
        var source = new string('S', 64);
        var item = PlayniteLibraryItem.From(Item(
            "app-details", "saved-details", title, source));
        var selection = new PlayniteLibraryDetailsSelection(
            item.Value.SavedId, item.Key,
            PlayniteLibraryIdentity.FocusId("grid", item.Key),
            item.Value.Presentation.DisplayName,
            item.Value.Presentation.Source.DisplayName,
            PageBumpers: false);
        var organization = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion,
            [new(item.Value.SavedId,
                item.Value.Presentation.DisplayName,
                item.Value.Presentation.Source.DisplayName)])
        {
            FavoriteSavedIds = [item.Value.SavedId],
            VariantGroups = [new("variant.details",
                [item.Value.SavedId, "saved-other"], item.Value.SavedId)],
        };
        var collection = Snapshot(WidgetPagedResourceStatus.Ready, [item]);
        var state = PlayniteLibraryDetailsPolicy.Project(selection, collection,
            PlayniteLibraryFixedRows.Empty, organization, null,
            new Dictionary<string, PlayniteLibraryLaunchState>(), "Ready", null,
            false, true);
        var view = PlayniteLibraryDetailsPresentation.Render(state);
        var first = new PresentationWidget(view).RenderSnapshot("details.layout", 1);
        var second = new PresentationWidget(view).RenderSnapshot("details.layout", 2);

        Assert.AreEqual(JsonSerializer.Serialize(first.Root),
            JsonSerializer.Serialize(second.Root));
        Assert.AreEqual(title, Nodes(first.Root).Single(node =>
            node.Id == "playnite-library.details.title").Text);
        StringAssert.Contains(Nodes(first.Root).Single(node =>
            node.Id == "playnite-library.details.source").Text!, source);
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
                node.Id == "playnite-library.details.launch" && node.IsDisabled is not true),
                $"{profile.Name}: launch is not reachable at {profile.Scale:P0}.");
            Assert.IsTrue(nodes.Any(node =>
                node.Id == "playnite-library.details.variant" &&
                node.Text == "Choose another variant"),
                $"{profile.Name}: variant selection is not semantically reachable.");
            Assert.AreEqual(1, nodes.Count(node => node.Id ==
                "playnite-library.details.scroll"),
                $"{profile.Name}: details scroll ownership changed.");
        }

        var busyState = PlayniteLibraryDetailsPolicy.Project(selection, collection,
            PlayniteLibraryFixedRows.Empty, organization, item.Value.SavedId,
            new Dictionary<string, PlayniteLibraryLaunchState>(), "Pending",
            item.Value.SavedId, true, true);
        var busy = new PresentationWidget(
            PlayniteLibraryDetailsPresentation.Render(busyState))
            .RenderSnapshot("details.busy", 3);
        Assert.IsTrue(Nodes(busy.Root).Single(node =>
            node.Id == "playnite-library.details.launch").IsBusy);
        Assert.IsTrue(Nodes(busy.Root).Where(node => node.ActionId is
                "playnite-library.launch" or "playnite-library.favorite" or
                "playnite-library.hide" or "playnite-library.variant" or
                "playnite-library.prefer")
            .All(node => node.IsDisabled is true));
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
        Assert.AreEqual(longTitle, nodes.Single(node =>
            node.Id == "playnite-library.hero.title").Text);
        Assert.IsNull(nodes.Single(node =>
            node.Id == "playnite-library.hero.artwork").ArtworkHandle,
            "Missing selected artwork must use a semantic fallback.");
        StringAssert.Contains(nodes.Single(node =>
            node.Id == "playnite-library.hero.state").Text!, "Favorite");
        StringAssert.Contains(nodes.Single(node =>
            node.Id == "playnite-library.hero.state").Text!, "Preferred variant");
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
