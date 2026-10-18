using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SpotifyMusicWorkflowTests
{
    internal static async Task SavedState()
    {
        var provider = SpotifyHarness.Ready();
        var library = new SpotifySavedLibrary(provider);
        var uris = Enumerable.Range(0, 50).Select(i => "spotify:track:" + i.ToString("D22")).ToArray();
        await library.ReadAsync(uris.Concat(uris), library.Generation, CancellationToken.None);
        Equal(2, provider.SavedReads.Count); Equal(40, provider.SavedReads[0].Length); Equal(10, provider.SavedReads[1].Length);
        await library.ReadAsync(uris, library.Generation, CancellationToken.None); Equal(2, provider.SavedReads.Count);
        await library.WriteAsync(uris[0], true, library.Generation, CancellationToken.None); Equal<bool?>(true, library.Get(uris[0]));
        var late = new TaskCompletionSource<IReadOnlyList<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SavedReadHandler = (_, _) => new(late.Task);
        library.Reset();
        var read = library.ReadAsync([uris[0]], library.Generation, CancellationToken.None);
        library.Reset(); late.SetResult([true]);
        try { await read; } catch (OperationCanceledException) { }
        Equal<bool?>(null, library.Get(uris[0]));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SavedWriteHandler = (_, _, _) => new(gate.Task);
        var write = library.WriteAsync(uris[0], true, library.Generation, CancellationToken.None);
        library.Reset(); gate.SetResult();
        try { True(!await write); } catch (OperationCanceledException) { }
        Equal<bool?>(null, library.Get(uris[0]));
        provider.SavedReadHandler = (_, _) => ValueTask.FromResult<IReadOnlyList<bool>>([]);
        try { await library.ReadAsync([uris[0]], library.Generation, CancellationToken.None); throw new Exception("Malformed status accepted"); }
        catch (SpotifyApplicationException) { }
        provider.SavedReadHandler = (values, _) => ValueTask.FromResult<IReadOnlyList<bool>>(values.Select(_ => false).ToArray());
        await library.ReadAsync([uris[0]], library.Generation, CancellationToken.None);
        provider.SavedWriteHandler = (_, _, _) => ValueTask.FromException(new SpotifyApplicationException("offline", "Unavailable"));
        try { await library.WriteAsync(uris[0], true, library.Generation, CancellationToken.None); throw new Exception("Failed write accepted"); }
        catch (SpotifyApplicationException) { }
        Equal<bool?>(false, library.Get(uris[0]));
        var many = Enumerable.Range(0, 520).Select(i => "spotify:track:" + i.ToString("D22")).ToArray();
        foreach (var batch in many.Chunk(40)) await library.ReadAsync(batch, library.Generation, CancellationToken.None);
        Equal<bool?>(null, library.Get(many[0])); Equal<bool?>(false, library.Get(many[^1]));
    }

    internal static async Task PlayerSavedState()
    {
        var provider = SpotifyHarness.Ready();
        var widget = await Start(provider);
        try
        {
            await Until(() => provider.SavedReads.Count == 1);
            var button = Nodes(widget.RenderSnapshot("spotify.saved", 1).Root).Single(node => node.Id == "spotify.saved");
            True(button.ActionId!.StartsWith("spotify.saved.toggle."));
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.SavedWriteHandler = (_, _, _) => new(pending.Task);
            var action = new WidgetActionEvent(button.ActionId!, button.Id);
            var first = widget.OnActionAsync(action).AsTask();
            await widget.OnActionAsync(action);
            Equal(1, provider.SavedWrites.Count);
            var busy = Nodes(widget.RenderSnapshot("spotify.saved", 2).Root).Single(node => node.Id == button.Id);
            True(busy.IsBusy == true && busy.IsDisabled != true && busy.IsFocusable);
            Equal(button.ActionId, busy.ActionId);
            Equal("spotify.saved.outline", busy.PackageIcon!.AssetId);
            pending.SetResult(); await first;
            var saved = Nodes(widget.RenderSnapshot("spotify.saved", 2).Root).Single(node => node.Id == "spotify.saved");
            True(saved.Text == "" && saved.AccessibilityLabel == "Remove from liked songs" && saved.IsSelected == true);
            Equal("spotify.saved.filled", saved.PackageIcon!.AssetId);
            True(saved.IsBusy != true && saved.IsDisabled != true);
            provider.PublishTransport(new(provider.Playback with { Item = provider.Playback.Item! with { Uri = "spotify:track:another", Title = "Another song" } }));
            await widget.OnActionAsync(action); Equal(1, provider.SavedWrites.Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task Suggestions()
    {
        var provider = SpotifyHarness.Ready();
        var widget = await Start(provider);
        try
        {
            await widget.OnActionAsync(new("spotify.nav.search", "spotify.nav.search"));
            await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "Night drive" });
            var first = Nodes(widget.RenderSnapshot("spotify.suggest", 1).Root).Single(node => node.Id == "spotify.search.query");
            True(first.TextEntrySuggestions is not null);
            var query = new TextSuggestionQuery(first.Id, first.TextEntrySuggestions!.Key, Guid.NewGuid().ToString("N"), 1, "Night");
            var results = await widget.OnSuggestTextAsync(query, CancellationToken.None);
            True(results.Items.Any(item => item.Value == "Night drive")); Equal(0, provider.SearchCalls);
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.suggest");
            await host.ContinueAsync("spotify.search.scroll"); host.PublishSnapshot();
            var current = Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == first.Id);
            True((await widget.OnSuggestTextAsync(query, CancellationToken.None)).Unavailable);
            var loaded = await widget.OnSuggestTextAsync(query with { SourceKey = current.TextEntrySuggestions!.Key }, CancellationToken.None);
            True(loaded.Items.Count > 1); Equal(1, provider.SearchCalls);
            await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.queue"));
            True((await widget.OnSuggestTextAsync(query with { SourceKey = current.TextEntrySuggestions!.Key }, CancellationToken.None)).Unavailable);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task QueueRefresh()
    {
        var provider = SpotifyHarness.Ready();
        var widget = await Start(provider);
        try
        {
            await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.queue"));
            await Until(() => Nodes(widget.RenderSnapshot("spotify.queue-workflow", 1).Root).Any(node => node.Id == "spotify.queue.scroll"));
            var before = widget.RenderSnapshot("spotify.queue-workflow", 2);
            var queue = Nodes(before.Root).Single(node => node.Id == "spotify.queue.scroll");
            True(queue.IndexedOccurrenceKeys?.Count == 1);
            True(before.ScrollRevealRequest is null); // Playing is in the fixed player, never the first upcoming occurrence.
            True(Nodes(before.Root).Any(node => node.Text?.StartsWith("Now playing ·") == true));
            await Until(() => provider.SavedReads.Count >= 2);
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.saved-queue");
            using var row = await host.AcquireAsync(queue.Id, 0, 1);
            var item = row.Range.Items[0];
            Equal("spotify.library.save", item.Root.ContextActions.Single().ActionId);
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued,
                row.RouteAction(item.Key, ControllerButton.A, contextActionOwnerId: item.Root.Id, contextActionId: "spotify.library.save"));
            await Until(() => provider.SavedWrites.Count == 1);
            host.PublishSnapshot();
            using var savedRow = await host.AcquireAsync(queue.Id, 0, 1);
            Equal("spotify.library.remove", savedRow.Range.Items[0].Root.ContextActions.Single().ActionId);
            Equal(queue.IndexedCollection!.QueryGeneration,
                Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == queue.Id).IndexedCollection!.QueryGeneration);
            Equal(1, provider.QueueCalls);
            var pending = new TaskCompletionSource<SpotifyQueueSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.QueueHandler = _ => new(pending.Task);
            var refresh = widget.OnActionAsync(new("spotify.refresh", "spotify.refresh")).AsTask();
            await Until(() => provider.QueueCalls == 2);
            var loading = Nodes(widget.RenderSnapshot("spotify.queue-workflow", 3).Root).Single(node => node.Id == queue.Id);
            Equal(queue.IndexedCollection!.QueryGeneration, loading.IndexedCollection!.QueryGeneration);
            Equal(1, loading.IndexedCollection.Count);
            pending.SetResult(provider.Queue); await refresh;
            Equal(1, Nodes(widget.RenderSnapshot("spotify.queue-workflow", 4).Root).Single(node => node.Id == queue.Id).IndexedCollection!.Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static async Task<SpotifyWidget> Start(SpotifyHarness provider)
    {
        var widget = new SpotifyWidget(provider);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await Until(() => widget.ViewState == SpotifyWidgetViewState.Ready);
        return widget;
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)
    { yield return node; foreach (var child in node.Children) foreach (var next in Nodes(child)) yield return next; }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 300; i++) { if (ready()) return; await Task.Delay(10); } throw new Exception("Music workflow did not settle."); }
    private static void True(bool value) { if (!value) throw new Exception("Music workflow assertion failed."); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
}
