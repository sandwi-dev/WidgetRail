using GameBarAlternative.FirstPartyWidgets.GameLauncher;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace GameBarAlternative.Tests.GameLauncher;

[TestClass]
public sealed class GameLauncherLayoutTests
{
    [TestMethod, Timeout(30_000)]
    public void EveryRouteHasOneFixedChromeAndOneBoundedCollectionViewport()
    {
        var items = Enumerable.Range(0, 64)
            .Select(index => GameLauncherItem.From(new WidgetAppLibraryItem(
                $"app-{index:D3}", $"Game {index:D3}", WidgetAppLibraryKind.Game)
            {
                SavedId = $"saved-{index:D3}",
                SourceAttribution = index % 2 == 0 ? "Steam" : "Windows",
            }))
            .ToArray();
        var display = items.Select(item => new GameLauncherDisplayItem(
            item.Value.SavedId, item.Value.DisplayName, item.Value.SourceAttribution))
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
        var cases = new (string Name, GameLauncherPresentationState State, bool Scroll)[]
        {
            ("library", State(ready, organized, GameLauncherRoute.Library, sources), true),
            ("add", State(ready, organized, GameLauncherRoute.AddGames, sources), true),
            ("running", State(ready, organized, GameLauncherRoute.Running, sources), true),
            ("hidden", State(ready, organized, GameLauncherRoute.Hidden, sources), true),
            ("warm", State(Snapshot(WidgetPagedResourceStatus.Loading, []), organized,
                GameLauncherRoute.Library, sources), true),
            ("loading", State(Snapshot(WidgetPagedResourceStatus.Loading, []),
                GameLauncherPrivateState.Empty, GameLauncherRoute.Library, sources), false),
            ("error", State(Snapshot(WidgetPagedResourceStatus.Error, [],
                    error: new WidgetResourceError("platform_unavailable", "Try again.")),
                GameLauncherPrivateState.Empty, GameLauncherRoute.Library, sources), false),
            ("empty", State(Snapshot(WidgetPagedResourceStatus.Ready, []),
                GameLauncherPrivateState.Empty, GameLauncherRoute.Library, sources), false),
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
            Assert.IsTrue(root.Children[^1].StyleClasses.Contains(
                "game-launcher-main", StringComparer.Ordinal),
                $"{scenario.Name}: main region does not own remaining height");

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

            var vertical = nodes.Where(node => node.Kind == ViewNodeKind.Scroll &&
                node.ScrollAxis == ScrollAxis.Vertical).ToArray();
            Assert.AreEqual(scenario.Scroll ? 1 : 0, vertical.Length,
                $"{scenario.Name}: vertical scroll ownership changed");
            if (scenario.Scroll)
            {
                Assert.AreEqual(GameLauncherPresentation.ScrollId, vertical[0].Id,
                    $"{scenario.Name}: collection viewport identity changed");
                Assert.IsTrue(vertical[0].StyleClasses.Contains(
                    "game-launcher-scroll", StringComparer.Ordinal),
                    $"{scenario.Name}: collection viewport does not own remaining height");
                Assert.IsTrue(Nodes(root.Children[^1]).Any(node => node.Id == vertical[0].Id),
                    $"{scenario.Name}: collection viewport escaped the main region");
            }

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
                     (Name: "standard-125", Width: 640, Height: 480, Scale: 1.25),
                     (Name: "wide-150", Width: 980, Height: 700, Scale: 1.5),
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
            var scroll = visible.Single(node =>
                node.Id == GameLauncherPresentation.ScrollId);
            var scrollNodes = Nodes(scroll).ToArray();
            foreach (var index in new[] { 0, items.Length / 2, items.Length - 1 })
                Assert.IsTrue(scrollNodes.Any(node => node.Id ==
                    GameLauncherIdentity.FocusId("grid", items[index].Key)),
                    $"{profile.Name}: item {index} is not reachable in the collection viewport");
            Assert.IsFalse(string.IsNullOrWhiteSpace(scroll.CollectionAnchorKey),
                $"{profile.Name}: collection viewport omitted its stable anchor");
        }

        var stylePath = Path.Combine(AppContext.BaseDirectory, "styles", "default.gbss");
        var styles = File.ReadAllText(stylePath);
        StringAssert.Contains(styles,
            ".game-launcher-fixed { flex-shrink: 0; }");
        StringAssert.Contains(styles,
            ".game-launcher-main { flex-grow: 1; min-height: 0; }");
        StringAssert.Contains(styles,
            ".game-launcher-scroll { flex-grow: 1; min-height: 0;");
        StringAssert.Contains(styles,
            ".game-launcher-footer { flex-shrink: 0; flex-wrap: wrap;");
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
            GameLauncherFixedRows.Empty, sources);

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

    private sealed class PresentationWidget(WidgetView view) : Widget
    {
        public override WidgetView Render() => view;
    }
}
