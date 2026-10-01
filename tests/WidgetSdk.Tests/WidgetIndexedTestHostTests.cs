using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetIndexedTestHostTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public static async Task Run()
    {
        await CapturedQueueAndOwnership();
        await SnapshotScopeAndArtwork();
        await CancellationAndBounds();
    }

    private static async Task CapturedQueueAndOwnership()
    {
        await using var widget = new Fixture();
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "author-test");
        using var first = await host.AcquireAsync("items", 0, 1);
        using var second = await host.AcquireAsync("items", 1, 1);
        widget.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, first.RouteAction("item-0", ControllerButton.A));
        await widget.Entered.Task.WaitAsync(Limit);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, second.RouteAction("item-1", ControllerButton.A));
        // A page shortcut belongs to the same serial queue as both captured row actions.
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, second.RouteAction("item-1", ControllerButton.RightTrigger));
        first.Dispose(); second.Dispose();
        widget.Source.PublishQuery("replacement", 3);
        host.PublishSnapshot();
        widget.Block.SetResult();
        Equal("initial:item-0:open", await widget.Next());
        Equal("initial:item-1:open", await widget.Next());
        Equal("parent:next", await widget.Next());
        Equal<WidgetOperationAdmission?>(null, first.RouteAction("item-0", ControllerButton.A));
        using var current = await host.AcquireAsync("items", 0, 1);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, current.RouteAction("item-0", ControllerButton.A));
        Equal("replacement:item-0:open", await widget.Next());
        var nextSequence = host.CurrentSnapshot.Sequence + 1;
        host.Dispose(); host.Dispose();
        Equal(0, widget.DestroyCount);
        // Disposing the helper releases data, without destroying/reinitializing the widget.
        using var replacement = WidgetTestHost.CreateIndexedCollectionHost(widget, "author-test", nextSequence);
        using var another = await replacement.AcquireAsync("items", 0, 1);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, another.RouteAction("item-0", ControllerButton.A));
        Equal("replacement:item-0:open", await widget.Next());
        Equal(1, widget.InitializeCount);
    }

    private static async Task SnapshotScopeAndArtwork()
    {
        await using var widget = new Fixture(); await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "author-test", 10);
        using var lease = await host.AcquireAsync("items", 1, 1);
        Equal(1, lease.Range.StartIndex);
        var old = host.CurrentSnapshot;
        Equal(11L, host.PublishSnapshot().Sequence);
        Equal<WidgetOperationAdmission?>(null, lease.RouteAction("item-1", ControllerButton.A,
            context: new(lease.Range.ScopeId, old.Sequence)));
        Equal<WidgetOperationAdmission?>(null, lease.RouteAction("item-1", ControllerButton.A,
            context: new("other-scope", host.CurrentSnapshot.Sequence)));
        Equal<WidgetOperationAdmission?>(null, lease.RouteAction("absent", ControllerButton.A));
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, lease.RouteAction("item-1", ControllerButton.Y));
        Equal("initial:item-1:row-refresh", await widget.Next());
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, lease.RouteAction("item-1", ControllerButton.A,
            contextActionOwnerId: lease.Range.Items[0].Root.Id, contextActionId: "favorite"));
        Equal("initial:item-1:favorite", await widget.Next());
        True(await lease.ResolveArtworkAsync("item-1", new("cover")) is not null);
        True(await lease.ResolveArtworkAsync("item-1", new("undeclared")) is null);
        True(await lease.ResolveArtworkAsync("absent", new("cover")) is null);
        widget.InvalidArtwork = true;
        await Throws<InvalidOperationException>(async () => await lease.ResolveArtworkAsync("item-1", new("cover")));
        widget.InvalidArtwork = false;
        widget.Modal = true; host.PublishSnapshot();
        Equal<WidgetOperationAdmission?>(null, lease.RouteAction("item-1", ControllerButton.A));
        True(await lease.ResolveArtworkAsync("item-1", new("cover")) is not null);
        using var pinned = await host.AcquireAsync("pinned-items", 0, 1, "pinned");
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, pinned.RouteAction("item-0", ControllerButton.A));
        Equal("initial:item-0:open", await widget.Next());
        widget.Modal = false; host.PublishSnapshot();
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, lease.RouteAction("item-1", ControllerButton.A));
        Equal("initial:item-1:open", await widget.Next());
        widget.Source.UpdateContent("new-content"); host.PublishSnapshot();
        Equal<WidgetOperationAdmission?>(null, lease.RouteAction("item-1", ControllerButton.A));
        True(await lease.ResolveArtworkAsync("item-1", new("cover")) is null);
    }

    private static async Task CancellationAndBounds()
    {
        await using var widget = new Fixture(); await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "author-test");
        await Throws<ArgumentException>(async () => await host.AcquireAsync("missing", 0, 1));
        await Throws<ArgumentException>(async () => await host.AcquireAsync("items", 0, 65));
        widget.ReadBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = host.AcquireAsync("items", 0, 1).AsTask();
        await widget.ReadEntered.Task.WaitAsync(Limit);
        host.Dispose();
        await Throws<OperationCanceledException>(async () => await pending);
        Equal(0, widget.DestroyCount);
        await Throws<ObjectDisposedException>(async () => await host.AcquireAsync("items", 0, 1));
        using var next = WidgetTestHost.CreateIndexedCollectionHost(widget, "author-test", 2);
        widget.ReadBlock = null;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Throws<OperationCanceledException>(async () => await next.AcquireAsync("items", 0, 1, cancellationToken: cancelled.Token));
        using var live = await next.AcquireAsync("items", 0, 1);
        live.Dispose(); live.Dispose();
        Equal<WidgetOperationAdmission?>(null, live.RouteAction("item-0", ControllerButton.A));
        True(await live.ResolveArtworkAsync("item-0", new("cover")) is null);
    }

    // Uses public/protected author APIs only; no friend-only widget ingress or raw protocol elements.
    private sealed class Fixture : Widget, IAsyncDisposable
    {
        private static readonly WidgetEncodedArtwork Png = new(WidgetArtworkContentType.Png,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
        private readonly Channel<string> events = Channel.CreateUnbounded<string>();
        public readonly WidgetIndexedCollection<string, string> Source;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Block, ReadBlock;
        public bool Modal, InvalidArtwork;
        public int InitializeCount, DestroyCount;

        public Fixture()
        {
            Source = CreateIndexedCollection<string, string>("songs", "initial", 3, new()
            {
                ReadRange = async (_, start, count, token) =>
                {
                    if (ReadBlock is { } block) { ReadEntered.TrySetResult(); await block.Task.WaitAsync(token); }
                    return Enumerable.Range(start, count).Select(i => $"item-{i}").ToArray();
                },
                ItemKey = item => new(item),
                RenderItem = (_, item, context) => UI.ActionSurface("open", context.Id("row"), item,
                    ActionSurfaceOrientation.Horizontal, UI.Artwork(new("cover"), context.Id("art"), "Cover"),
                    UI.Text(item, context.Id("title")))
                    .Shortcut(ControllerButton.Y, actionId: "row-refresh")
                    .ContextMenuShortcut(ControllerButton.X).ContextAction("favorite", "Favorite"),
                OnAction = async (query, item, action, token) =>
                {
                    if (Block is { } block) { Entered.TrySetResult(); await block.Task.WaitAsync(token); }
                    await events.Writer.WriteAsync($"{query}:{item}:{action.ActionId}", token);
                },
                ResolveArtwork = (_, _, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(
                    InvalidArtwork ? new(WidgetArtworkContentType.Png, new byte[] { 1, 2, 3 }) : Png),
            });
        }
        public async Task Start()
        {
            await WidgetTestHost.InitializeAsync(this);
            await WidgetTestHost.SetLifecycleStateAsync(this, WidgetLifecycleState.Visible);
        }
        public Task<string> Next() => events.Reader.ReadAsync().AsTask().WaitAsync(Limit);
        public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default) =>
            events.Writer.WriteAsync("parent:" + action.ActionId, cancellationToken);
        protected override ValueTask OnLifecycleStateChangedAsync(WidgetLifecycleState previous,
            WidgetLifecycleState current, CancellationToken cancellationToken)
        {
            if (previous == WidgetLifecycleState.Created) ++InitializeCount;
            return ValueTask.CompletedTask;
        }
        protected override ValueTask OnDestroyingAsync(CancellationToken cancellationToken)
        { ++DestroyCount; return ValueTask.CompletedTask; }
        public override WidgetView Render()
        {
            var view = new WidgetView(UI.Stack("root", UI.Button("Header", "header", "header"),
                UI.CollectionList("items", Source, 70, "Songs"))
                .InputScope("scope").Shortcut(ControllerButton.RightTrigger, "next"), "header", ActiveInputScopeId: "scope")
            {
                PinnedLayouts = [WidgetView.PinnedLayout("pinned", "Pinned", new(),
                    UI.Stack("pinned-root", UI.CollectionList("pinned-items", Source, 70, "Pinned songs")).InputScope("pinned-scope"),
                    initialFocusId: "pinned-items", activeInputScopeId: "pinned-scope")],
            };
            return Modal ? view.WithModal(new("details", "Details", UI.Button("Close", "close", "close"), "close", "close")) : view;
        }
        public ValueTask DisposeAsync() => WidgetTestHost.DestroyAsync(this);
    }
    private static void True(bool value) { if (!value) throw new Exception("Expected true."); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action().WaitAsync(Limit); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
