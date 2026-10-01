using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SpotifyDiscoveredTests
{
    internal static async Task PrefixAndReturn()
    {
        var provider = SpotifyHarness.Ready();
        var template = provider.Playlists.Items[0];
        provider.Playlists = new(Enumerable.Range(0, 36).Select(index => template with
        {
            PlaylistId = "playlist-" + index, Name = "Playlist " + index, Uri = "spotify:playlist:playlist-" + index,
        }).ToArray(), 0, 12, 36);
        var widget = await Start(provider, "spotify.nav.playlists");
        try
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.discovery");
            var empty = Find(host.CurrentSnapshot.Root, "spotify.playlists.scroll");
            Equal(0, empty.Children.Count); Equal(0, empty.IndexedCollection!.Count); Equal(0, provider.PlaylistCalls);
            await host.ContinueAsync(empty.Id); host.PublishSnapshot();
            var source = Find(host.CurrentSnapshot.Root, empty.Id).IndexedCollection!;
            Equal(12, source.Count);
            using var row = await host.AcquireAsync(empty.Id, 11, 1);
            await host.ContinueAsync(empty.Id); host.PublishSnapshot();
            Equal(24, Find(host.CurrentSnapshot.Root, empty.Id).IndexedCollection!.Count);
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, row.RouteAction(row.Range.Items[0].Key, ControllerButton.A));
            await Until(() => widget.RenderSnapshot("spotify.discovery", 100).ActiveInputScopeId == "spotify.playlist.detail");
            host.PublishSnapshot();
            await host.ContinueAsync("spotify.playlist.detail.scroll"); host.PublishSnapshot();
            Equal(1, Find(host.CurrentSnapshot.Root, "spotify.playlist.detail.scroll").IndexedCollection!.Count);
            var scope = host.CurrentSnapshot.ActiveInputScopeId;
            var back = Descendants(host.CurrentSnapshot.Root).SelectMany(node => node.Shortcuts).Single(shortcut => shortcut.Button == ControllerButton.B);
            await widget.OnActionAsync(new(back.ActionId, "spotify.root", ControllerButton.B, InputScopeId: scope));
            host.PublishSnapshot();
            var target = host.CurrentSnapshot.FocusGroupEntryRequest?.IndexedItem;
            True(target is not null && target.Index == 11 && target.ItemKey == row.Range.Items[0].Key && target.QueryGeneration == source.QueryGeneration);
            Equal(2, provider.PlaylistCalls);
            Equal(24, Find(host.CurrentSnapshot.Root, empty.Id).IndexedCollection!.Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task Search()
    {
        var provider = SpotifyHarness.Ready();
        provider.SearchHandler = (query, kind, offset, count, _) => ValueTask.FromResult(new SpotifySearchPage(
            Enumerable.Range(0, offset == 0 ? count : 2).Select(index => new SpotifySearchItem(kind,
                "id-" + index, query + index, "Artist", null, "spotify:track:repeat", "https://open.spotify.com/track/repeat", index != 1)).ToArray(),
            offset, count, offset == 0 ? 10000 : 12));
        var widget = await Start(provider, "spotify.nav.search");
        try
        {
            Equal(0, provider.SearchCalls);
            await widget.OnActionAsync(new WidgetActionEvent("spotify.search.query", "spotify.search.query") { CommittedText = "first" });
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.search.discovery");
            await host.ContinueAsync("spotify.search.scroll"); host.PublishSnapshot();
            using var old = await host.AcquireAsync("spotify.search.scroll", 0, 2);
            True(old.Range.Items[0].Key != old.Range.Items[1].Key);
            Equal<WidgetOperationAdmission?>(null, old.RouteAction(old.Range.Items[1].Key, ControllerButton.A));
            await host.ContinueAsync("spotify.search.scroll"); host.PublishSnapshot();
            var source = Find(host.CurrentSnapshot.Root, "spotify.search.scroll").IndexedCollection!;
            Equal(12, source.Count); True(!source.Discovery!.HasMore);
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, old.RouteAction(old.Range.Items[0].Key, ControllerButton.A));
            await Until(() => provider.StartedPlayback.Count == 1);
            Equal("spotify:track:repeat", provider.StartedPlayback[0].ItemUris![0]);
            await widget.OnActionAsync(new WidgetActionEvent("spotify.search.query", "spotify.search.query") { CommittedText = "replacement" });
            host.PublishSnapshot();
            Equal<WidgetOperationAdmission?>(null, old.RouteAction(old.Range.Items[0].Key, ControllerButton.A));
            Equal(0, Find(host.CurrentSnapshot.Root, "spotify.search.scroll").IndexedCollection!.Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task DetailRefreshAndOccurrences()
    {
        var provider = SpotifyHarness.Ready();
        var item = provider.PlaylistDetail.Items[0];
        provider.PlaylistDetail = new(Enumerable.Range(0, 29).Select(index => item with
        {
            Title = "Occurrence " + index, Uri = index is 0 or 12 ? item.Uri : "spotify:track:item-" + index,
        }).ToArray(), 0, 12, 29);
        await using var scene = await Scene.Create(provider);
        await scene.Open(0);
        await scene.Demand(Scene.Detail);
        using var first = await scene.Host.AcquireAsync(Scene.Detail, 0, 12);
        var initial = scene.Source(Scene.Detail);
        await scene.Demand(Scene.Detail);
        using var duplicate = await scene.Host.AcquireAsync(Scene.Detail, 12, 1);
        True(first.Range.Items[0].Key != duplicate.Range.Items[0].Key);
        await scene.Demand(Scene.Detail);
        Equal(29, scene.Source(Scene.Detail).Count);
        True(!scene.Source(Scene.Detail).Discovery!.HasMore);
        True(first.Range.Items.All(row => row.Root.Focus?.Up is null && row.Root.Focus?.Down is null));
        Equal(Scene.Detail, Find(scene.Host.CurrentSnapshot.Root, "spotify.playlist.play.shared").Focus?.Down);
        var priorCalls = provider.PlaylistDetailCalls;
        using (var original = await scene.Host.AcquireAsync(Scene.Detail, 0, 1)) Equal(first.Range.Items[0].Key, original.Range.Items[0].Key);
        Equal(priorCalls, provider.PlaylistDetailCalls);
        await scene.Widget.OnActionAsync(new("spotify.refresh", "spotify.refresh")); scene.Host.PublishSnapshot();
        True(scene.Source(Scene.Detail).QueryGeneration > initial.QueryGeneration);
        Equal<WidgetOperationAdmission?>(null, first.RouteAction(first.Range.Items[0].Key, ControllerButton.A));
        await scene.Demand(Scene.Detail);
        Equal(12, scene.Source(Scene.Detail).Count);
    }

    internal static async Task RetrySparseAndCap()
    {
        var provider = SpotifyHarness.Ready();
        provider.Playlists = provider.Playlists with { Items = [], Total = 24, HasAuthoritativeWindow = false };
        await using var scene = await Scene.Create(provider, load: false, maximumItems: 24);
        await scene.Demand(Scene.Library);
        Equal(0, scene.Source(Scene.Library).Count); True(scene.Source(Scene.Library).Discovery!.HasMore);
        var template = SpotifyHarness.Ready().Playlists.Items[0];
        provider.Playlists = new(Enumerable.Range(0, 36).Select(index => template with
        { PlaylistId = "p-" + index, Uri = "spotify:playlist:p-" + index }).ToArray(), 0, 12, 36);
        await scene.Demand(Scene.Library); Equal(12, scene.Source(Scene.Library).Count);
        await scene.Demand(Scene.Library); Equal(24, scene.Source(Scene.Library).Count);
        // Last provider page exhausted, despite filtering the initial remote page.
        True(!scene.Source(Scene.Library).Discovery!.HasMore);
        await scene.Open(0);
        provider.PlaylistDetailError = new SpotifyApplicationException("offline", "Offline");
        await scene.Demand(Scene.Detail);
        Equal(DiscoveredCollectionStatus.Failed, scene.Source(Scene.Detail).Discovery!.Status);
        var failedCalls = provider.PlaylistDetailCalls;
        await scene.Demand(Scene.Detail); Equal(failedCalls, provider.PlaylistDetailCalls);
        provider.PlaylistDetailError = null;
        await scene.Demand(Scene.Detail, retry: true); Equal(1, scene.Source(Scene.Detail).Count);

        await scene.Back();
        await scene.Widget.OnActionAsync(new("spotify.refresh", "spotify.refresh")); scene.Host.PublishSnapshot();
        await scene.Demand(Scene.Library); await scene.Demand(Scene.Library);
        Equal(DiscoveredCollectionStatus.LimitReached, scene.Source(Scene.Library).Discovery!.Status);
        True(scene.Source(Scene.Library).Discovery!.HasMore);
        var calls = provider.PlaylistCalls;
        await scene.Demand(Scene.Library); Equal(calls, provider.PlaylistCalls);
        using var retained = await scene.Host.AcquireAsync(Scene.Library, 0, 1); True(retained.Range.Items.Count == 1);
    }

    internal static async Task DetailCacheAndSetupReturn()
    {
        var provider = SpotifyHarness.Ready();
        provider.Playlists = provider.Playlists with { Items = [provider.Playlists.Items[0] with { SnapshotId = "stable" }] };
        await using var scene = await Scene.Create(provider);
        await scene.Open(0); await scene.Demand(Scene.Detail);
        using (var track = await scene.Host.AcquireAsync(Scene.Detail, 0, 1))
        {
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, track.RouteAction(track.Range.Items[0].Key, ControllerButton.Y));
            await Until(() => scene.Widget.RenderSnapshot("spotify.setup-wait", 1).ActiveInputScopeId == "spotify.setup");
            scene.Host.PublishSnapshot(); await scene.Back();
            var target = scene.Host.CurrentSnapshot.FocusGroupEntryRequest?.IndexedItem;
            True(target is not null && target.CollectionId == Scene.Detail && target.Index == 0 && target.ItemKey == track.Range.Items[0].Key);
        }
        await scene.Back(); await scene.Open(0); await scene.Demand(Scene.Detail);
        Equal(2, provider.PlaylistMetadataCalls); Equal(1, provider.PlaylistDetailCalls);
        await scene.Widget.OnActionAsync(new("spotify.refresh", "spotify.refresh")); scene.Host.PublishSnapshot();
        await scene.Demand(Scene.Detail);
        Equal(3, provider.PlaylistMetadataCalls); Equal(2, provider.PlaylistDetailCalls);
    }

    internal static async Task LateDetailCannotReopen()
    {
        var provider = SpotifyHarness.Ready();
        var late = new TaskCompletionSource<SpotifyPlaylistItemsPageSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.PlaylistDetailCompletion = late; provider.IgnorePlaylistDetailCancellation = true;
        await using var scene = await Scene.Create(provider);
        await scene.Open(0);
        var pending = scene.Host.ContinueAsync(Scene.Detail).AsTask();
        await Until(() => provider.PlaylistDetailCalls == 1);
        await scene.Back();
        late.SetResult(provider.PlaylistDetail);
        try { await pending; } catch (Exception error) when (error is ArgumentException or OperationCanceledException) { }
        scene.Host.PublishSnapshot();
        True(Descendants(scene.Host.CurrentSnapshot.Root).Any(node => node.Id == Scene.Library));
        True(!Descendants(scene.Host.CurrentSnapshot.Root).Any(node => node.Id == Scene.Detail));
        Equal(0, provider.StartedPlayback.Count);
    }

    internal static async Task LibraryBatchesAndSingleFlight()
    {
        var provider = SpotifyHarness.Ready(); var template = provider.Playlists.Items[0];
        provider.Playlists = new(Enumerable.Range(0, 29).Select(index => template with
        { PlaylistId = "page-" + index, Uri = "spotify:playlist:page-" + index }).ToArray(), 0, 12, 29);
        await using var scene = await Scene.Create(provider);
        var initial = scene.Source(Scene.Library);
        using var prefix = await scene.Host.AcquireAsync(Scene.Library, 11, 1);
        provider.PlaylistCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = scene.Host.ContinueAsync(Scene.Library).AsTask();
        await Until(() => provider.PlaylistCalls == 2);
        scene.Host.PublishSnapshot();
        Equal(12, scene.Source(Scene.Library).Count);
        Equal(DiscoveredCollectionStatus.Loading, scene.Source(Scene.Library).Discovery!.Status);
        var duplicateDemand = scene.Host.ContinueAsync(Scene.Library).AsTask();
        Equal(2, provider.PlaylistCalls);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, prefix.RouteAction(prefix.Range.Items[0].Key, ControllerButton.X));
        await Until(() => provider.Commands.Count == 1);
        provider.PlaylistCompletion.SetResult(provider.Playlists);
        await Task.WhenAll(pending, duplicateDemand); provider.PlaylistCompletion = null;
        scene.Host.PublishSnapshot(); Equal(24, scene.Source(Scene.Library).Count);
        await scene.Demand(Scene.Library); Equal(29, scene.Source(Scene.Library).Count);
        Equal(initial.QueryGeneration, scene.Source(Scene.Library).QueryGeneration);
        True(!scene.Source(Scene.Library).Discovery!.HasMore);
        Equal(3, provider.PlaylistCalls);
        using var reverse = await scene.Host.AcquireAsync(Scene.Library, 0, 1);
        Equal(3, provider.PlaylistCalls); True(reverse.Range.Items[0].Key.Length > 0);
    }

    internal static async Task SearchKindsMenuAndSections()
    {
        var provider = SpotifyHarness.Ready(); var widget = await Start(provider, "spotify.nav.search");
        try
        {
            await widget.OnActionAsync(new WidgetActionEvent("spotify.search.query", "spotify.search.query") { CommittedText = "night" });
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.search-menu");
            await host.ContinueAsync("spotify.search.scroll"); host.PublishSnapshot();
            using var row = await host.AcquireAsync("spotify.search.scroll", 0, 1);
            var item = row.Range.Items[0]; var menu = item.Root.ContextActions.Single();
            Equal(ControllerButton.Menu, item.Root.ContextMenuButton);
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, row.RouteAction(item.Key, ControllerButton.A,
                contextActionOwnerId: item.Root.Id, contextActionId: menu.ActionId));
            await Until(() => provider.QueuedUris.Count == 1);
            Equal(1, provider.SearchCalls); Equal(0, provider.PlaylistCalls); Equal(0, provider.QueueCalls);
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, row.RouteAction(item.Key, ControllerButton.X));
            await Until(() => provider.Commands.Count == 1);
            long lastRequest = 0;
            foreach (var direction in new[] { "next", "previous" })
                for (var index = 0; index < 4; ++index)
                {
                    await widget.OnActionAsync(new("spotify.nav." + direction + "-section", "spotify.root")); host.PublishSnapshot();
                    var entry = host.CurrentSnapshot.FocusGroupEntryRequest!;
                    True(entry.RequestId > lastRequest); lastRequest = entry.RequestId;
                    _ = Find(host.CurrentSnapshot.Root, entry.GroupId);
                    Equal(0, ViewSnapshotValidator.Validate(host.CurrentSnapshot).Count);
                }
            Equal(1, provider.SearchCalls);
            foreach (var kind in new[] { SpotifySearchKind.Album, SpotifySearchKind.Artist, SpotifySearchKind.Playlist })
            {
                await widget.OnActionAsync(new("spotify.search.type." + kind, "spotify.search.type")); host.PublishSnapshot();
                await host.ContinueAsync("spotify.search.scroll"); host.PublishSnapshot();
                using var context = await host.AcquireAsync("spotify.search.scroll", 0, 1);
                Equal(0, context.Range.Items[0].Root.ContextActions.Count);
                var expected = provider.StartedPlayback.Count + 1;
                Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, context.RouteAction(context.Range.Items[0].Key, ControllerButton.A));
                await Until(() => provider.StartedPlayback.Count == expected);
                True(provider.StartedPlayback[^1].ContextUri!.StartsWith("spotify:" + kind.ToString().ToLowerInvariant() + ":", StringComparison.Ordinal));
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task LateSearchAndLifecycle()
    {
        var provider = SpotifyHarness.Ready();
        var late = new TaskCompletionSource<SpotifySearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.SearchHandler = (query, kind, offset, limit, _) => query == "old" ? new(late.Task) : ValueTask.FromResult(new SpotifySearchPage(
            [new(kind, "new", "New result", "Artist", null, "spotify:track:new", "https://open.spotify.com/track/new", true)], offset, limit, 1));
        var widget = await Start(provider, "spotify.nav.search");
        try
        {
            await widget.OnActionAsync(new WidgetActionEvent("spotify.search.query", "spotify.search.query") { CommittedText = "old" });
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.late-search");
            var pending = host.ContinueAsync("spotify.search.scroll").AsTask();
            await Until(() => provider.SearchCalls == 1);
            await widget.OnActionAsync(new WidgetActionEvent("spotify.search.query", "spotify.search.query") { CommittedText = "new" }); host.PublishSnapshot();
            await host.ContinueAsync("spotify.search.scroll"); host.PublishSnapshot();
            late.SetResult(new([], 0, 10, 0));
            try { await pending; } catch (Exception error) when (error is ArgumentException or OperationCanceledException) { }
            host.PublishSnapshot(); Equal(1, Find(host.CurrentSnapshot.Root, "spotify.search.scroll").IndexedCollection!.Count);
            using var current = await host.AcquireAsync("spotify.search.scroll", 0, 1);
            True(current.Range.Items[0].Root.AccessibilityLabel!.Contains("New result", StringComparison.Ordinal));
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); host.PublishSnapshot();
            Equal("new", Find(host.CurrentSnapshot.Root, "spotify.search.query").TextEntryValue);
            Equal(1, Find(host.CurrentSnapshot.Root, "spotify.search.scroll").IndexedCollection!.Count);
            Equal(2, provider.SearchCalls);
        }
        finally { late.TrySetResult(new([], 0, 10, 0)); await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task ProductionHistory()
    {
        var provider = SpotifyHarness.Ready(); var template = provider.Playlists.Items[0];
        provider.Playlists = new(Enumerable.Range(0, 144).Select(index => template with
        { PlaylistId = "history-" + index, Uri = "spotify:playlist:history-" + index }).ToArray(), 0, 24, 144);
        var widget = new SpotifyWidget(provider);
        try
        {
            await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => widget.ViewState == SpotifyWidgetViewState.Ready);
            await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.playlists"));
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.history");
            for (var page = 0; page < 6; ++page)
            {
                await host.ContinueAsync(Scene.Library); host.PublishSnapshot();
                var collection = Find(host.CurrentSnapshot.Root, Scene.Library);
                Equal((page + 1) * 24, collection.IndexedCollection!.Count); Equal(0, collection.Children.Count);
                True(SnapshotJson.Serialize(host.CurrentSnapshot).Length < 100_000);
            }
            Equal(24, provider.LastPlaylistLimit); Equal(6, provider.PlaylistCalls);
            using var reverse = await host.AcquireAsync(Scene.Library, 0, 1);
            Equal(SpotifyCollectionIdentity.Playlist("history-0").Value, reverse.Range.Items[0].Key);
            Equal(6, provider.PlaylistCalls);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task SupersededSelectionAndRefresh()
    {
        var provider = SpotifyHarness.Ready(); var template = provider.Playlists.Items[0];
        provider.Playlists = new([template with { PlaylistId = "first", Name = "First", Uri = "spotify:playlist:first" },
            template with { PlaylistId = "second", Name = "Second", Uri = "spotify:playlist:second" }], 0, 12, 2);
        var late = new TaskCompletionSource<SpotifyPlaylistItemsPageSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.PlaylistDetailHandler = (request, _) => request.PlaylistId == "first" ? new(late.Task) : ValueTask.FromResult(provider.PlaylistDetail);
        await using var scene = await Scene.Create(provider);
        await scene.Open(0); var old = scene.Host.ContinueAsync(Scene.Detail).AsTask();
        await Until(() => provider.PlaylistDetailCalls == 1);
        await scene.Back(); await scene.Open(1); await scene.Demand(Scene.Detail);
        var second = scene.Source(Scene.Detail);
        late.SetResult(provider.PlaylistDetail);
        try { await old; } catch (Exception error) when (error is ArgumentException or OperationCanceledException) { }
        scene.Host.PublishSnapshot();
        Equal(second.QueryGeneration, scene.Source(Scene.Detail).QueryGeneration);
        True(Descendants(scene.Host.CurrentSnapshot.Root).Any(node => node.Text == "Second"));

        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = new TaskCompletionSource<SpotifyPlaybackSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.PlaybackHandler = _ => { refreshStarted.TrySetResult(); return new(refresh.Task); };
        var refreshing = scene.Widget.OnActionAsync(new("spotify.refresh", "spotify.refresh")).AsTask();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await scene.Back(); await scene.Open(0); await scene.Demand(Scene.Detail);
        var current = scene.Source(Scene.Detail); var calls = provider.PlaylistDetailCalls;
        refresh.SetResult(provider.Playback); await refreshing; provider.PlaybackHandler = null; scene.Host.PublishSnapshot();
        Equal(current, scene.Source(Scene.Detail)); Equal(calls, provider.PlaylistDetailCalls);
        True(Descendants(scene.Host.CurrentSnapshot.Root).Any(node => node.Text == "First"));
    }

    internal static async Task DetailDemandLifecycle()
    {
        var provider = SpotifyHarness.Ready();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.PlaylistDetailHandler = async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
            return provider.PlaylistDetail;
        };
        await using var scene = await Scene.Create(provider); await scene.Open(0);
        var source = scene.Source(Scene.Detail);
        using var demand = new CancellationTokenSource();
        var pending = scene.Host.ContinueAsync(Scene.Detail, cancellationToken: demand.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // A native surface cancels its demand when hidden. Logical metadata/query
        // can remain cached while the widget is inactive.
        demand.Cancel(); await WidgetTestHost.SetLifecycleStateAsync(scene.Widget, WidgetLifecycleState.Background);
        try { await pending; } catch (OperationCanceledException) { }
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        provider.PlaylistDetailHandler = null;
        await WidgetTestHost.SetLifecycleStateAsync(scene.Widget, WidgetLifecycleState.Interactive); scene.Host.PublishSnapshot();
        await scene.Demand(Scene.Detail); Equal(source.QueryGeneration, scene.Source(Scene.Detail).QueryGeneration);
        Equal(1, scene.Source(Scene.Detail).Count); Equal(2, provider.PlaylistDetailCalls);
    }

    internal sealed class Scene(SpotifyWidget widget, WidgetIndexedCollectionTestHost host) : IAsyncDisposable
    {
        internal const string Library = "spotify.playlists.scroll";
        internal const string Detail = "spotify.playlist.detail.scroll";
        internal SpotifyWidget Widget => widget;
        internal WidgetIndexedCollectionTestHost Host => host;
        internal static async Task<Scene> Create(SpotifyHarness provider, bool load = true, int maximumItems = 128)
        {
            var widget = new SpotifyWidget(provider, TimeProvider.System, SpotifyRuntimeDiagnostics.None, 12, maximumItems);
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => widget.ViewState == SpotifyWidgetViewState.Ready);
            await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.playlists"));
            var scene = new Scene(widget, WidgetTestHost.CreateIndexedCollectionHost(widget, "spotify.discovered-scene"));
            if (load) await scene.Demand(Library);
            return scene;
        }
        internal IndexedCollectionDescriptor Source(string id) => Find(host.CurrentSnapshot.Root, id).IndexedCollection!;
        internal async Task Demand(string id, bool retry = false) { await host.ContinueAsync(id, retry); host.PublishSnapshot(); }
        internal async Task Open(int index)
        {
            using var range = await host.AcquireAsync(Library, index, 1);
            Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, range.RouteAction(range.Range.Items[0].Key, ControllerButton.A));
            await Until(() => widget.RenderSnapshot("spotify.scope-wait", 1).ActiveInputScopeId == "spotify.playlist.detail");
            host.PublishSnapshot();
        }
        internal async Task Back()
        {
            var scope = host.CurrentSnapshot.ActiveInputScopeId;
            var back = Descendants(host.CurrentSnapshot.Root).SelectMany(node => node.Shortcuts).Single(shortcut => shortcut.Button == ControllerButton.B);
            await widget.OnActionAsync(new(back.ActionId, "spotify.root", ControllerButton.B, InputScopeId: scope)); host.PublishSnapshot();
        }
        public async ValueTask DisposeAsync() { host.Dispose(); await WidgetTestHost.DestroyAsync(widget); }
    }

    private static async Task<SpotifyWidget> Start(SpotifyHarness provider, string route)
    {
        var widget = new SpotifyWidget(provider, TimeProvider.System, SpotifyRuntimeDiagnostics.None, 12, 128);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await Until(() => widget.ViewState == SpotifyWidgetViewState.Ready);
        await widget.OnActionAsync(new(route, route));
        return widget;
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var index = 0; index < 300; ++index) { if (condition()) return; await Task.Delay(10); }
        throw new InvalidOperationException("Spotify discovered condition did not settle.");
    }
    private static IEnumerable<ViewNode> Descendants(ViewNode node)
    { yield return node; foreach (var child in node.Children) foreach (var item in Descendants(child)) yield return item; }
    private static ViewNode Find(ViewNode root, string id) => Descendants(root).Single(node => node.Id == id);
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Spotify discovered assertion failed."); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
}
