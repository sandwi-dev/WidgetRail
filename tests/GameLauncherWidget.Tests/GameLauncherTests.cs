using System.Text.Json;
using GameBarAlternative.FirstPartyWidgets.GameLauncher;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LauncherWidget = GameBarAlternative.FirstPartyWidgets.GameLauncher.GameLauncherWidget;

namespace GameBarAlternative.Tests.GameLauncher;

[TestClass]
public sealed class GameLauncherTests
{
    [TestMethod, Timeout(30_000)]
    [DataRow(2_000)]
    [DataRow(10_000)]
    public async Task LargeLibrariesTraverseInBoundedCursorWindow(int total)
    {
        var host = new FakeHost(total);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var pages = (total + LauncherWidget.PageSize - 1) / LauncherWidget.PageSize;
        for (var page = 1; page < pages; page++)
        {
            var revision = widget.Collection.Revision;
            await widget.OnActionAsync(new("game-launcher.next", "game-launcher.next"));
            await Bounded(widget.WhenLibraryIdleAsync(), "forward page drain");
            Assert.IsGreaterThan(revision, widget.Collection.Revision);
            Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
            Assert.IsLessThanOrEqualTo(LauncherWidget.MaximumRetainedItems,
                widget.Collection.Items.Count);
        }

        Assert.AreEqual(total, host.MaximumObservedIndex + 1);
        Assert.AreEqual(LauncherWidget.PageSize, host.MaximumRequestedLimit);
        Assert.IsLessThanOrEqualTo(LauncherWidget.MaximumRetainedItems,
            widget.Collection.Items.Count);
        Assert.AreEqual($"Game {total - 1:D5}",
            widget.Collection.Items[^1].Value.DisplayName);
        var serialized = Snapshot(widget, total);
        Assert.AreEqual(widget.Collection.Items.Count, Nodes(serialized.Root).Count(node =>
            node.ActionId == "game-launcher.launch"));
        Assert.IsLessThanOrEqualTo(
            WidgetCursorResource<GameLauncherItem>.MaximumCursorHistory,
            widget.RetainedCursorCount);
        var beforeRevision = widget.Collection.Revision;
        await widget.OnActionAsync(new("game-launcher.previous", "game-launcher.previous"));
        await Bounded(widget.WhenLibraryIdleAsync(), "reverse page drain");
        Assert.IsGreaterThan(beforeRevision, widget.Collection.Revision);
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
        var finalPageStart = (pages - 1) * LauncherWidget.PageSize;
        var reversePageStart = finalPageStart - LauncherWidget.MaximumRetainedItems;
        Assert.AreEqual($"Game {reversePageStart:D5}",
            widget.Collection.Items[0].Value.DisplayName);
        Assert.AreEqual(
            GameLauncherIdentity.FocusId("grid", widget.Collection.Items[63].Key),
            widget.Collection.RequestedFocusId);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task WarmProjectionIsVisibleButCannotAuthorizeLaunch()
    {
        var warm = new GameLauncherPrivateState(1,
            [new("saved-warm", "Warm game", "Steam")]);
        var state = new WidgetTestPrivateState(JsonSerializer.Serialize(warm), 1);
        var pending = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(0, state)
        {
            QueryHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
                pending.Task.WaitAsync(token)),
        };
        var widget = Create(host);
        await Visible(widget);
        await Bounded(host.FirstQueryStarted.Task, "warm projection query admission");
        await Bounded(widget.WhenWarmStateIdleAsync(), "warm state load");
        Assert.AreEqual(1, widget.WarmItems.Count);

        var snapshot = Snapshot(widget, 1);
        var tile = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "game-launcher.launch");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));
        Assert.AreEqual(0, host.Launches.Count);

        await Background(widget);
        pending.TrySetResult(new([], null, null, "rev-1"));
    }

    [TestMethod]
    public void PrivateProjectionIsBoundedAndContainsNoLaunchAuthority()
    {
        var items = Enumerable.Range(0, GameLauncherPrivateState.MaximumItems)
            .Select(index => new GameLauncherDisplayItem(
                $"saved-{index:D3}-" + new string('s', 114),
                new string((char)('a' + index % 26), 120),
                new string((char)('A' + index % 26), 64)))
            .ToArray();
        var state = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, items);
        var json = JsonSerializer.SerializeToUtf8Bytes(state);

        Assert.IsLessThanOrEqualTo(64 * 1024, json.Length);
        Assert.AreEqual(items.Length, GameLauncherPrivateState.Normalize(state).Items.Count);
        Assert.AreEqual(0, GameLauncherPrivateState.Normalize(
            state with { Items = [.. items, items[0] with { SavedId = "saved-overflow" }] })
            .Items.Count);
        var text = System.Text.Encoding.UTF8.GetString(json);
        Assert.IsFalse(text.Contains("AppId", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("Artwork", StringComparison.Ordinal));
    }

    [TestMethod, Timeout(30_000)]
    public async Task ExactLaunchRevalidatesSavedIdentity()
    {
        var host = new FakeHost(1)
        {
            ResolveHandler = request =>
                [Item(0) with { AppId = "app-current" }],
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(widget.RenderSnapshot("launcher.test", 2).Root)
            .Single(node => node.ActionId == "game-launcher.launch");

        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));

        CollectionAssert.AreEqual(new[] { "saved-00000" }, host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-current" }, host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingCurrentIdentityCannotLaunch()
    {
        var host = new FakeHost(1) { ResolveHandler = _ => [] };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(widget.RenderSnapshot("launcher.test", 3).Root)
            .Single(node => node.ActionId == "game-launcher.launch");

        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));

        Assert.AreEqual(0, host.Launches.Count);
        Assert.AreEqual("The selected game is no longer installed",
            Nodes(widget.RenderSnapshot("launcher.test", 4).Root)
                .Single(node => node.Id == "game-launcher.status").Text);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task SameTitleVariantsKeepDistinctIdentityAndExactRouting()
    {
        var host = new FakeHost(2)
        {
            ItemFactory = index => Item(index) with
            {
                DisplayName = "Shared title",
                SourceAttribution = index == 0 ? "Steam" : "Windows",
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 5).Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();

        Assert.AreEqual(2, tiles.Length);
        Assert.AreNotEqual(tiles[0].Id, tiles[1].Id);
        StringAssert.Contains(tiles[0].AccessibilityLabel ?? string.Empty, "Steam");
        StringAssert.Contains(tiles[1].AccessibilityLabel ?? string.Empty, "Windows");
        await widget.OnActionAsync(new("game-launcher.launch", tiles[1].Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AdjacentFailureRetainsLastGoodWindow()
    {
        var host = new FakeHost(300);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        host.FailAfterOffset = LauncherWidget.PageSize;
        await widget.OnActionAsync(new("game-launcher.next", "game-launcher.next"));
        await Bounded(widget.WhenLibraryIdleAsync(), "adjacent failure drain");

        Assert.AreEqual(LauncherWidget.PageSize, widget.Collection.Items.Count);
        Assert.IsNotNull(widget.Collection.Error);
        Assert.IsTrue(Nodes(widget.RenderSnapshot("launcher.test", 5).Root)
            .Any(node => node.Id == "game-launcher.retained-error"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ArtworkAndResponsiveGridRemainSemanticAndBounded()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = index => Item(index) with
            {
                ArtworkHandle = "library.art.0123456789abcdef0123456789abcdef",
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var snapshot = widget.RenderSnapshot("launcher.test", 6);

        Assert.AreEqual(WidgetSurfaceMode.Wide, snapshot.Surface?.Mode);
        var grid = Nodes(snapshot.Root).Single(node => node.Id == "game-launcher.library.grid");
        Assert.AreEqual(ViewNodeKind.Grid, grid.Kind);
        Assert.AreEqual(5, grid.GridMaximumColumns);
        Assert.AreEqual("library.art.0123456789abcdef0123456789abcdef",
            Nodes(snapshot.Root).Single(node => node.ArtworkHandle is not null).ArtworkHandle);
        Assert.IsLessThan(100, Nodes(snapshot.Root).Count());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DeactivationDrainsCancellationIgnoringPage()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(0)
        {
            QueryHandler = async (_, token) =>
            {
                started.TrySetResult();
                return await release.Task.ConfigureAwait(false);
            },
        };
        var widget = Create(host);
        await Visible(widget);
        await Bounded(started.Task, "cancellation-ignoring query admission");
        var transition = WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background).AsTask();
        release.TrySetResult(new([Item(0)], null, null, "late"));
        await Bounded(transition, "cancellation-ignoring lifecycle drain");

        Assert.AreEqual(0, widget.Collection.Items.Count);
        Assert.AreEqual(WidgetPagedResourceStatus.NotLoaded, widget.Collection.Status);
    }

    private static LauncherWidget Create(FakeHost host) =>
        WidgetTestHost.Attach(new LauncherWidget(), host.Services());

    private static Task Visible(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible).AsTask(),
        "visible lifecycle");

    private static Task Interactive(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive).AsTask(),
        "interactive lifecycle");

    private static Task Background(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background).AsTask(),
        "background lifecycle");

    private static async Task Ready(LauncherWidget widget, FakeHost host)
    {
        await Bounded(host.FirstQueryStarted.Task, "first provider query");
        await Bounded(widget.WhenLibraryIdleAsync(), "initial page drain");
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
    }

    private static async Task Bounded(Task task, string phase)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Timed out during {phase}.", exception);
        }
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 20_000; attempt++)
        {
            if (predicate()) return;
            await Task.Yield();
        }
        Assert.Fail("Condition did not become true.");
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in Nodes(child)) yield return node;
    }

    private static ViewSnapshot Snapshot(LauncherWidget widget, long sequence)
    {
        try { return widget.RenderSnapshot("launcher.test", sequence); }
        catch (ProtocolValidationException exception)
        {
            Assert.Fail(string.Join(Environment.NewLine, exception.Errors.Select(error =>
                $"{error.Path}: {error.Code}: {error.Message}")));
            throw;
        }
    }

    private static WidgetAppLibraryItem Item(int index) => new(
        $"app-{index:D5}", $"Game {index:D5}", WidgetAppLibraryKind.Game)
    {
        SavedId = $"saved-{index:D5}",
        SourceAttribution = index % 2 == 0 ? "Steam" : "Windows",
    };

    private sealed class FakeHost
    {
        private readonly int _count;
        private readonly WidgetTestPrivateState _state;
        internal int MaximumObservedIndex { get; private set; } = -1;
        internal int MaximumRequestedLimit { get; private set; }
        internal int? FailAfterOffset { get; set; }
        internal Func<int, WidgetAppLibraryItem> ItemFactory { get; set; } = Item;
        internal Func<WidgetAppLibraryCursorRequest, CancellationToken,
            ValueTask<WidgetAppLibraryPage>>? QueryHandler { get; set; }
        internal Func<ResolveSavedWidgetAppLibraryItemsRequest,
            IReadOnlyList<WidgetAppLibraryItem>>? ResolveHandler { get; set; }
        internal List<IReadOnlyList<string>> ResolveRequests { get; } = [];
        internal List<string> Launches { get; } = [];
        internal TaskCompletionSource FirstQueryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal FakeHost(int count, WidgetTestPrivateState? state = null)
        {
            _count = count;
            _state = state ?? new WidgetTestPrivateState();
        }

        internal WidgetHostServices Services() => new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetAppLibraryCapabilities.GetPage, Query)
            .WithHandler(WidgetAppLibraryCapabilities.ResolveSaved, Resolve)
            .WithHandler(WidgetAppLibraryCapabilities.Launch, Launch)
            .WithPrivateState(_state)
            .Build();

        private ValueTask<WidgetAppLibraryPage> Query(
            WidgetAppLibraryCursorRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FirstQueryStarted.TrySetResult();
            if (QueryHandler is not null) return QueryHandler(request, token);
            var offset = request.Cursor is null ? 0 : int.Parse(
                request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
                System.Globalization.CultureInfo.InvariantCulture);
            if (FailAfterOffset == offset)
                return ValueTask.FromException<WidgetAppLibraryPage>(
                    new WidgetCapabilityException("platform_unavailable", "private"));
            MaximumRequestedLimit = Math.Max(MaximumRequestedLimit, request.Limit);
            var count = Math.Min(request.Limit, Math.Max(0, _count - offset));
            if (count != 0) MaximumObservedIndex = Math.Max(MaximumObservedIndex, offset + count - 1);
            var items = Enumerable.Range(offset, count).Select(ItemFactory).ToArray();
            var before = offset == 0 ? null : $"cursor.{Math.Max(0, offset - request.Limit)}";
            var after = offset + count < _count ? $"cursor.{offset + count}" : null;
            return ValueTask.FromResult(new WidgetAppLibraryPage(items, before, after, "revision-1"));
        }

        private ValueTask<ResolveSavedWidgetAppLibraryItemsResponse> Resolve(
            ResolveSavedWidgetAppLibraryItemsRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ResolveRequests.Add(request.SavedIds.ToArray());
            var values = ResolveHandler?.Invoke(request) ?? request.SavedIds.Select(saved =>
            {
                var index = int.Parse(saved.AsSpan(saved.LastIndexOf('-') + 1),
                    System.Globalization.CultureInfo.InvariantCulture);
                return ItemFactory(index);
            }).ToArray();
            return ValueTask.FromResult(new ResolveSavedWidgetAppLibraryItemsResponse(values));
        }

        private ValueTask<WidgetCapabilityAcknowledgement> Launch(
            LaunchWidgetAppLibraryItemRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Launches.Add(request.AppId);
            return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
        }
    }
}
