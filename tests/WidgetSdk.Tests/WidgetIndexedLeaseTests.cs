using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetIndexedLeaseTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public static async Task CapturedActions()
    {
        await using var widget = new Fixture(); await widget.Start();
        var lease = await widget.Acquire();
        Equal(WidgetOperationAdmission.Enqueued, widget.AdmitAction(new("block", "header", InputScopeId: "scope")));
        await widget.BlockEntered.Task.WaitAsync(Limit);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.A));
        True(widget.ReleaseIndexedRange(lease.LeaseId), "release admitted data lease");
        widget.Source.PublishQuery(new("replacement"), 1); widget.Publish();
        widget.BlockRelease.TrySetResult();
        Equal("initial:key-0:open", await widget.Next());
        Equal<WidgetOperationAdmission?>(null, widget.Send(lease, ControllerButton.A));
        var replacement = await widget.Acquire();
        True(replacement.LeaseId != lease.LeaseId, "rerealization receives fresh semantic authority");
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(replacement, ControllerButton.A));
        Equal("replacement:key-0:open", await widget.Next());
        await WidgetTestHost.DestroyAsync(widget);
        await Throws<ObjectDisposedException>(async () => await widget.Acquire());
        True(!widget.ReleaseIndexedRange(replacement.LeaseId), "destruction retires semantic leases");
    }

    public static async Task LogicalOwnership()
    {
        await using var widget = new Fixture(); await widget.Start();
        var lease = await widget.Acquire();
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.Y));
        Equal("initial:key-0:row-y", await widget.Next());
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.RightTrigger));
        Equal("parent:next", await widget.Next());
        var root = lease.Range.Items[0].Root.Id;
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.A, owner: root, action: "favorite"));
        Equal("initial:key-0:favorite", await widget.Next());
        Equal<WidgetOperationAdmission?>(null, widget.Send(lease, ControllerButton.A, owner: root, action: "disabled"));
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.A, owner: "collection", action: "collection-menu"));
        Equal("parent:collection-menu", await widget.Next());
        Equal<WidgetOperationAdmission?>(null, widget.Send(lease, ControllerButton.A, owner: "missing", action: "favorite"));
        var oldPageSequence = widget.PublishedSequence;
        widget.ParentAction = "changed-next"; widget.Publish();
        Equal<WidgetOperationAdmission?>(null, widget.Send(lease, ControllerButton.RightTrigger, snapshotSequence: oldPageSequence));
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.RightTrigger));
        Equal("parent:changed-next", await widget.Next());
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.A));
        Equal("initial:key-0:open", await widget.Next());
        widget.CollectionBusy = true; widget.Publish();
        Equal<WidgetOperationAdmission?>(null, widget.Send(lease, ControllerButton.A));
        Equal<WidgetOperationAdmission?>(null, widget.Send(lease, ControllerButton.A, owner: root, action: "favorite"));
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(lease, ControllerButton.LeftTrigger));
        Equal("parent:previous", await widget.Next());
        widget.CollectionBusy = false; widget.Publish();
        widget.Source.UpdateContent(new("busy", Busy: true)); widget.Publish();
        Equal<WidgetOperationAdmission?>(null, widget.Send(lease, ControllerButton.A));
        var busy = await widget.Acquire();
        Equal<WidgetOperationAdmission?>(null, widget.Send(busy, ControllerButton.Y));
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(busy, ControllerButton.RightTrigger));
        Equal("parent:changed-next", await widget.Next());
        widget.SeparateCollectionScope = true; widget.Source.UpdateContent(new("private-scope")); widget.Publish();
        var scoped = await widget.Acquire();
        Equal("collection-scope", scoped.Range.ScopeId);
        Equal<WidgetOperationAdmission?>(null, widget.Send(scoped, ControllerButton.LeftTrigger));

    }

    public static async Task RepeatIdentity()
    {
        await using var widget = new Fixture(); await widget.Start();
        widget.HoldRows = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await widget.Acquire();
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(first, ControllerButton.Y));
        await widget.RowEntered.Task.WaitAsync(Limit);
        widget.ReleaseIndexedRange(first.LeaseId);
        var same = await widget.Acquire();
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Joined, widget.Send(same, ControllerButton.Y, ControllerEventPhase.Repeated));
        widget.Source.PublishQuery(new("new-query"), 1); widget.Publish();
        var changed = await widget.Acquire();
        Equal(first.Range.Items[0].Root.Id, changed.Range.Items[0].Root.Id);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(changed, ControllerButton.Y, ControllerEventPhase.Repeated));
        widget.HoldRows.SetResult();
        Equal("initial:key-0:row-y", await widget.Next());
        Equal("new-query:key-0:row-y", await widget.Next());
    }

    public static async Task ModalPinnedAndArtwork()
    {
        await using var widget = new Fixture(new("initial", Artwork: true)); await widget.Start();
        var main = await widget.Acquire();
        widget.Pinned = true; widget.Publish();
        var pinned = await widget.Acquire("pinned-collection", "pinned");
        widget.Modal = true; widget.Publish();
        Equal<WidgetOperationAdmission?>(null, widget.Send(main, ControllerButton.A));
        var mainReference = new IndexedCollectionItemReference(main.LeaseId, "key-0");
        True(await widget.ResolveIndexedArtworkAsync(mainReference, "cover", default) is not null, "modal retains parent artwork authority");
        Equal<WidgetOperationAdmission?>(null, widget.Send(main, ControllerButton.A, scope: "pinned-scope"));
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, widget.Send(pinned, ControllerButton.A));
        Equal("initial:key-0:open", await widget.Next());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        widget.Artwork = async (_, _, _, token) => { started.SetResult(); await release.Task.WaitAsync(token); return Fixture.Png; };
        var pending = widget.ResolveIndexedArtworkAsync(mainReference, "cover", default).AsTask();
        await started.Task.WaitAsync(Limit);
        widget.ReleaseIndexedRange(main.LeaseId); release.SetResult();
        True(await pending.WaitAsync(Limit) is null, "retired range cannot publish late artwork");
        True(await widget.ResolveIndexedArtworkAsync(new(pinned.LeaseId, "other-key"), "cover", default) is null, "artwork cannot borrow a different item");
        True(await widget.ResolveIndexedArtworkAsync(new(pinned.LeaseId, "key-0"), "other-handle", default) is null, "only declared artwork may resolve");
    }

    public static async Task RetentionBounds()
    {
        await using var widget = new Fixture(); await widget.Start();
        var leases = new List<IndexedCollectionLease>();
        for (var index = 0; index < Widget.MaximumIndexedLeases; ++index) leases.Add(await widget.Acquire());
        await Throws<InvalidOperationException>(async () => await widget.Acquire());
        widget.ReleaseIndexedRange(leases[0].LeaseId); _ = await widget.Acquire();
        widget.Source.PublishQuery(new("replacement"), 64); widget.Publish();
        for (var index = 0; index < Widget.MaximumIndexedLeaseItems / 64; ++index) _ = await widget.Acquire(count: 64);
        await Throws<InvalidOperationException>(async () => await widget.Acquire(count: 1));
        foreach (var lease in leases) True(!widget.ReleaseIndexedRange(lease.LeaseId), "query replacement released old leases");
    }

    public static async Task CancellationBudgets()
    {
        await using var widget = new Fixture(new("initial", Artwork: true)); await widget.Start();
        using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
        for (var index = 0; index < Widget.MaximumIndexedLeases + 1; ++index)
            await Throws<OperationCanceledException>(async () => await widget.Acquire(cancellationToken: alreadyCancelled.Token));
        var lease = await widget.Acquire();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCount = 0;
        widget.Artwork = async (_, _, _, _) =>
        {
            if (Interlocked.Increment(ref providerCount) == 4) entered.SetResult();
            await release.Task; return Fixture.Png;
        };
        using var cancellation = new CancellationTokenSource();
        var reference = new IndexedCollectionItemReference(lease.LeaseId, "key-0");
        var reads = Enumerable.Range(0, 4).Select(_ => widget.ResolveIndexedArtworkAsync(reference, "cover", cancellation.Token).AsTask()).ToArray();
        try
        {
            await entered.Task.WaitAsync(Limit);
            cancellation.Cancel();
            foreach (var read in reads) await Throws<OperationCanceledException>(async () => await read);
            await Throws<InvalidOperationException>(async () => await widget.ResolveIndexedArtworkAsync(reference, "cover", default));
            Equal(4, providerCount);
        }
        finally { release.TrySetResult(); }
    }

    private sealed record Query(string Name, bool Artwork = false, bool Busy = false);
    private sealed record Item(string Key);
    private sealed class Fixture : Widget, IAsyncDisposable
    {
        internal static readonly WidgetEncodedArtwork Png = new(WidgetArtworkContentType.Png,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
        internal readonly WidgetIndexedCollection<Query, Item> Source;
        private readonly Channel<string> events = Channel.CreateUnbounded<string>();
        internal readonly TaskCompletionSource BlockEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource BlockRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RowEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? HoldRows;
        internal string ParentAction = "next";
        internal bool Modal, Pinned, CollectionBusy, SeparateCollectionScope;
        private long sequence;
        internal long PublishedSequence => sequence;
        internal Func<Query, Item, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>> Artwork = (_, _, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(Png);
        internal Fixture(Query? initial = null)
        {
            Source = CreateIndexedCollection<Query, Item>("source", initial ?? new("initial"), 1, new()
            {
                ReadRange = (_, start, count, _) => ValueTask.FromResult<IReadOnlyList<Item>>(Enumerable.Range(start, count).Select(index => new Item($"key-{index}")).ToArray()),
                ItemKey = item => new(item.Key),
                RenderItem = (query, item, context) => new Raw(new ViewNode
                {
                    Id = context.Id("row"), Kind = ViewNodeKind.ActionSurface, ActionId = "open", AccessibilityLabel = item.Key,
                    ActionSurfaceOrientation = ActionSurfaceOrientation.Horizontal, IsBusy = query.Busy,
                    FocusBackgroundArtworkHandle = query.Artwork ? "cover" : null,
                    Shortcuts = [new(ControllerButton.Y, "row-y", RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld)],
                    ContextMenuButton = ControllerButton.X,
                    ContextActions = [new("favorite", "Favorite"), new("disabled", "Disabled", IsDisabled: true)],
                    Children = [new() { Id = context.Id("title"), Kind = ViewNodeKind.Text, Text = query.Name }],
                }),
                OnAction = async (query, item, action, token) =>
                {
                    if (HoldRows is { } hold) { RowEntered.TrySetResult(); await hold.Task.WaitAsync(token); }
                    await events.Writer.WriteAsync($"{query.Name}:{item.Key}:{action.ActionId}", token);
                },
                ResolveArtwork = (query, item, handle, token) => Artwork(query, item, handle, token),
            });
        }
        internal async Task Start() { await WidgetTestHost.InitializeAsync(this); await WidgetTestHost.SetLifecycleStateAsync(this, WidgetLifecycleState.Visible); Publish(); }
        internal void Publish() => RenderSnapshot("lease-widget", ++sequence);
        internal ValueTask<IndexedCollectionLease> Acquire(string collection = "collection", string? pinned = null, int count = 1, CancellationToken cancellationToken = default) =>
            AcquireIndexedRangeAsync(new(collection, Source.Descriptor, 0, count, Guid.NewGuid().ToString("N"), pinned), cancellationToken);
        internal WidgetOperationAdmission? Send(IndexedCollectionLease lease, ControllerButton button,
            ControllerEventPhase phase = ControllerEventPhase.Pressed, string? owner = null, string? action = null, string? scope = null, long? snapshotSequence = null) =>
            AdmitIndexedInput(new(new(lease.LeaseId, "key-0"), button, phase, owner, action),
                new(scope ?? lease.Range.ScopeId, snapshotSequence ?? sequence));
        internal Task<string> Next() => events.Reader.ReadAsync().AsTask().WaitAsync(Limit);
        public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
        {
            if (action.ActionId == "block") { BlockEntered.SetResult(); await BlockRelease.Task.WaitAsync(cancellationToken); return; }
            await events.Writer.WriteAsync("parent:" + action.ActionId, cancellationToken);
        }
        public override WidgetView Render()
        {
            var collection = UI.CollectionList("collection", Source, 70, "Items")
                .Shortcut(ControllerButton.RightTrigger, ParentAction)
                .ContextMenu(ControllerButton.Menu, new WidgetContextAction("collection-menu", "Collection options"));
            var view = new WidgetView(UI.Stack("root", UI.Button("Header", "header", "header"),
                new Raw(collection.ToProtocolNode() with { IsBusy = CollectionBusy, InputScopeId = SeparateCollectionScope ? "collection-scope" : null }))
                .InputScope("scope").Shortcut(ControllerButton.Y, "parent-y").Shortcut(ControllerButton.LeftTrigger, "previous"), SeparateCollectionScope ? "collection" : "header",
                ActiveInputScopeId: SeparateCollectionScope ? "collection-scope" : "scope");
            if (Pinned) view = view with { PinnedLayouts = [WidgetView.PinnedLayout("pinned", "Pinned", new(),
                UI.Stack("pinned-root", UI.CollectionList("pinned-collection", Source, 70, "Pinned items")).InputScope("pinned-scope"),
                initialFocusId: "pinned-collection", activeInputScopeId: "pinned-scope")] };
            return Modal ? view.WithModal(new("details", "Details", UI.Button("Close", "close", "close"), "close", "close")) : view;
        }
        public ValueTask DisposeAsync() => WidgetTestHost.DestroyAsync(this);
    }
    private sealed record Raw(ViewNode Node) : WidgetElement(Node.Id) { internal override ViewNode ToProtocolNode() => Node; }
    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static async Task Throws<T>(Func<Task> work) where T : Exception
    { try { await work().WaitAsync(Limit); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
