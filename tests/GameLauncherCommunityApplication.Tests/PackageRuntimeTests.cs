using WidgetRail.Samples.GameLauncher;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WindowsAppLibraryProvider;
using System.Text.Json;

namespace GameLauncherCommunityApplication.Tests;

[TestClass]
public sealed class PackageRuntimeTests
{
    private static readonly WidgetAppLibraryQuery InstalledGames = new(
        InstalledOnly: true,
        Kind: WidgetAppLibraryKind.Game,
        Sort: WidgetAppLibrarySortOrder.DisplayName);

    [TestMethod, Timeout(30_000)]
    public async Task PackageServiceOwnsTenThousandItemSearchArtworkAndLaunch()
    {
        using var directory = new TestDirectory();
        var source = new PackageSource(10_000);
        await using var service = Service(directory.Path, source);

        var first = await service.QueryAsync(
            InstalledGames, null, null, 64, refresh: true, CancellationToken.None);
        Assert.AreEqual(64, first.Items.Count);
        Assert.IsNotNull(first.After);
        Assert.AreEqual(WidgetAppLibrarySourceHealth.Healthy,
            first.Sources.Single().Health);
        Assert.IsTrue(first.Items.All(item => item.Presentation.Metadata is not null));
        Assert.IsTrue(first.Items.All(item => item.Presentation.Artwork.Items.Count == 3));
        Assert.IsTrue(first.Items.All(item => item.Presentation.Artwork.Items.All(artwork =>
            !artwork.Handle.Contains("stable", StringComparison.OrdinalIgnoreCase) &&
            !artwork.Handle.Contains("source", StringComparison.OrdinalIgnoreCase))));

        var artwork = first.Items[0].Presentation.Artwork.Find(
            WidgetAppLibraryArtworkRole.Tile)!;
        var firstContent = await service.ResolveArtworkAsync(artwork, CancellationToken.None);
        var cachedContent = await service.ResolveArtworkAsync(artwork, CancellationToken.None);
        Assert.AreEqual(PackageSource.TinyPng, firstContent);
        Assert.AreEqual(firstContent, cachedContent);
        Assert.AreEqual(1, source.ArtworkLoads);

        var observed = new List<WidgetAppLibraryItem>(10_000);
        var page = first;
        while (true)
        {
            observed.AddRange(page.Items);
            if (page.After is null) break;
            page = await service.QueryAsync(
                InstalledGames,
                new WidgetCollectionCursor(page.After),
                WidgetCursorDirection.After,
                64,
                refresh: false,
                CancellationToken.None);
        }
        Assert.AreEqual(10_000, observed.Count);
        Assert.AreEqual(10_000, observed.Select(item => item.SavedId)
            .Distinct(StringComparer.Ordinal).Count());

        var search = await service.QueryAsync(
            InstalledGames with { SearchText = "Game 09999" },
            null, null, 64, refresh: false, CancellationToken.None);
        Assert.AreEqual("Game 09999", search.Items.Single().Presentation.DisplayName);

        var target = search.Items.Single();
        var launch = await service.LaunchObservedAsync(
            target.AppId, WidgetAppLaunchOverlayBehavior.KeepOpen,
            CancellationToken.None);
        Assert.AreEqual(WidgetAppLaunchObservationState.Running, launch.State);
        Assert.AreEqual("stable-009999", source.LastLaunchedStableIdentity);

        source.FailLaunch = true;
        var failure = await Assert.ThrowsExactlyAsync<WidgetCapabilityException>(async () =>
            await service.LaunchObservedAsync(
                target.AppId, WidgetAppLaunchOverlayBehavior.KeepOpen,
                CancellationToken.None));
        Assert.AreEqual("app_not_found", failure.ErrorCode);

        source.Health = GameLibrarySourceHealth.Degraded;
        var retained = await service.QueryAsync(
            InstalledGames, null, null, 64, refresh: true, CancellationToken.None);
        Assert.AreEqual(64, retained.Items.Count,
            "A degraded source must retain its last-good package catalog.");
        Assert.AreEqual(WidgetAppLibrarySourceHealth.Degraded,
            retained.Sources.Single().Health);
        Assert.AreEqual("source_degraded", retained.Sources.Single().StatusCode);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PackageServicePowersCollectionsDetailsBackAndRestartState()
    {
        using var directory = new TestDirectory();
        var source = new PackageSource(3) { ArtworkContent = null };
        await using var service = Service(directory.Path, source);
        var widget = WidgetTestHost.Attach(
            new GameLauncherWidget(service),
            new WidgetTestHostServicesBuilder().Build());

        await WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Interactive);
        await widget.WhenLibraryIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));

        var library = Snapshot(widget, 1);
        var collectionActions = Nodes(library.Root).Where(node =>
                node.ActionId?.StartsWith(
                    GameLauncherCollectionPolicy.ActionPrefix,
                    StringComparison.Ordinal) == true)
            .ToArray();
        Assert.IsGreaterThanOrEqualTo(2, collectionActions.Length);
        var tile = Nodes(library.Root).First(node =>
            node.ActionId == "game-launcher.launch" &&
            node.CollectionItemKey is not null);
        var tileArtwork = Nodes(tile).Single(node =>
            node.Id.EndsWith(".artwork", StringComparison.Ordinal));
        Assert.IsNull(tileArtwork.ArtworkHandle,
            "A package-owned failed artwork demand must not leak an unusable host handle.");
        Assert.AreEqual(WidgetGlyph.Play, tileArtwork.Glyph);

        await widget.OnActionAsync(new("game-launcher.details.open", tile.Id));
        var details = Snapshot(widget, 2);
        Assert.IsNotNull(Nodes(details.Root).SingleOrDefault(node =>
            node.Id == "game-launcher.details.title"));
        await widget.OnActionAsync(new(
            "game-launcher.favorite", "game-launcher.details.launch"));
        var back = details.Root.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.B);
        await widget.OnActionAsync(new(
            back.ActionId!,
            "game-launcher.details.launch",
            ControllerButton.B,
            ControllerEventPhase.Pressed,
            InputScopeId: details.ActiveInputScopeId));
        var returned = Snapshot(widget, 3);
        Assert.IsNull(Nodes(returned.Root).SingleOrDefault(node =>
            node.Id == "game-launcher.details.title"));

        var favorite = Nodes(returned.Root).Single(node =>
            node.ActionId == GameLauncherCollectionPolicy.ActionPrefix + "favorites");
        await widget.OnActionAsync(new(favorite.ActionId!, favorite.Id));
        await widget.WhenLibraryIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(1, widget.Collection.Items.Count);
        Assert.AreEqual(widget.Organization.FavoriteSavedIds.Single(),
            widget.Collection.Items.Single().Value.SavedId);

        await WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background);
        await WidgetTestHost.DestroyAsync(widget);

        await using var restarted = Service(directory.Path, new PackageSource(3));
        var state = await restarted.ReadStateAsync(CancellationToken.None);
        Assert.IsTrue(state.Exists);
        CollectionAssert.AreEqual(widget.Organization.FavoriteSavedIds.ToArray(),
            state.Value!.FavoriteSavedIds.ToArray());
        Assert.AreEqual(1, state.Value.FavoriteSavedIds.Count);
        var current = await restarted.QueryAsync(
            InstalledGames, null, null, 3, refresh: true, CancellationToken.None);
        Assert.AreEqual(tile.CollectionItemKey,
            GameLauncherIdentity.Key(current.Items[0].SavedId).Value);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PackageUpdateReplacementAndReinstallRetainUserStateAndSavedIdentity()
    {
        using var directory = new TestDirectory();
        string savedId;
        await using (var original = Service(directory.Path, new PackageSource(1)))
        {
            var item = (await original.QueryAsync(
                InstalledGames, null, null, 1, refresh: true,
                CancellationToken.None)).Items.Single();
            savedId = item.SavedId;
            var state = GameLauncherPrivateState.Empty with
            {
                Items = [new(savedId, item.Presentation.DisplayName,
                    item.Presentation.Source.DisplayName)],
                FavoriteSavedIds = [savedId],
            };
            var write = await original.WriteStateAsync(
                state, 0, CancellationToken.None);
            Assert.AreEqual(1L, write.Revision);
        }

        await using (var updated = Service(
                         directory.Path, new PackageSource(1, "Updated Game")))
        {
            var item = (await updated.QueryAsync(
                InstalledGames, null, null, 1, refresh: true,
                CancellationToken.None)).Items.Single();
            Assert.AreEqual(savedId, item.SavedId,
                "A package update must not rotate durable saved identity.");
            Assert.AreEqual("Updated Game 00000", item.Presentation.DisplayName);
            var retained = await updated.ReadStateAsync(CancellationToken.None);
            Assert.AreEqual(savedId, retained.Value!.FavoriteSavedIds.Single());
        }

        await using (var reinstalled = Service(
                         directory.Path, new PackageSource(1, "Reinstalled Game")))
        {
            var item = (await reinstalled.QueryAsync(
                InstalledGames, null, null, 1, refresh: true,
                CancellationToken.None)).Items.Single();
            Assert.AreEqual(savedId, item.SavedId,
                "A package replacement/reinstall must reuse package-owned state.");
            var retained = await reinstalled.ReadStateAsync(CancellationToken.None);
            Assert.AreEqual(1, retained.Value!.FavoriteSavedIds.Count);
            Assert.AreEqual(savedId, retained.Value.FavoriteSavedIds.Single());
        }
    }

    [TestMethod, Timeout(30_000)]
    public async Task PlayniteConnectionRouteNeverPublishesTokenAndRejectsLateCompletion()
    {
        using var directory = new TestDirectory();
        await using var service = Service(directory.Path, new PackageSource(1));
        var client = new FakePlayniteBridgeClient();
        var widget = WidgetTestHost.Attach(
            new GameLauncherWidget(service, client),
            new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await widget.WhenLibraryIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));

        client.ProbeResult = new(PlayniteBridgeConnectionKind.NotConfigured,
            "credential_missing");
        await widget.OnActionAsync(new(
            GameLauncherWidget.PlayniteOpenActionId,
            GameLauncherWidget.PlayniteOpenActionId));
        var setup = Snapshot(widget, 20);
        Assert.AreEqual("game-launcher.playnite.token", setup.InitialFocusId);
        var entry = Nodes(setup.Root).Single(node =>
            node.Id == "game-launcher.playnite.token");
        Assert.AreEqual(TextEntryInputKind.Sensitive, entry.TextEntryInputKind);
        Assert.AreEqual(string.Empty, entry.TextEntryValue);
        Assert.IsNull(entry.AccessibilityValue);

        const string sentinel = "fake-playnite-route-token";
        client.ProbeResult = new(PlayniteBridgeConnectionKind.Connected, "connected");
        await widget.OnActionAsync(new(
            GameLauncherWidget.PlayniteSaveActionId,
            "game-launcher.playnite.token") { CommittedText = sentinel });
        var connected = Snapshot(widget, 21);
        Assert.AreEqual(sentinel, client.LastSavedToken);
        Assert.IsFalse(JsonSerializer.Serialize(connected).Contains(
            sentinel, StringComparison.Ordinal));
        Assert.IsNotNull(Nodes(connected.Root).SingleOrDefault(node =>
            node.Id == "game-launcher.playnite.status"));

        client.PendingProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = widget.OnActionAsync(new(
            GameLauncherWidget.PlayniteRefreshActionId,
            GameLauncherWidget.PlayniteRefreshActionId)).AsTask();
        await client.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await widget.OnActionAsync(new(
            GameLauncherWidget.PlayniteBackActionId,
            GameLauncherWidget.PlayniteBackActionId));
        client.PendingProbe.SetResult(
            new(PlayniteBridgeConnectionKind.Malformed, "malformed_response"));
        await late.WaitAsync(TimeSpan.FromSeconds(2));
        var library = Snapshot(widget, 22);
        Assert.IsNull(Nodes(library.Root).SingleOrDefault(node =>
            node.Id == "game-launcher.playnite.root"));
        Assert.AreEqual(1, client.MaximumConcurrentOperations);
    }

    private static GameLauncherApplicationService Service(
        string root,
        PackageSource source)
    {
        Directory.CreateDirectory(root);
        var provider = new WindowsAppLibraryProvider([source], InlineSta.Instance);
        return new(
            provider,
            new GameLauncherSavedIdIssuer(Path.Combine(root, "saved-id.key")),
            new GameLauncherStateFileStore(Path.Combine(root, "organization.json")));
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in Nodes(child))
            yield return node;
    }

    private static ViewSnapshot Snapshot(GameLauncherWidget widget, long sequence)
    {
        try { return widget.RenderSnapshot("package-runtime", sequence); }
        catch (ProtocolValidationException exception)
        {
            Assert.Fail(string.Join(Environment.NewLine, exception.Errors.Select(error =>
                $"{error.Path}: {error.Code}: {error.Message}")));
            throw;
        }
    }

    private sealed class PackageSource(
        int count,
        string displayPrefix = "Game") : IGameLibrarySource
    {
        internal const string TinyPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+XcF7WQAAAABJRU5ErkJggg==";
        private readonly IReadOnlyList<GameLibrarySourceItem> _items =
            Enumerable.Range(0, count).Select(index => new GameLibrarySourceItem(
                "package-test", "Package Fixture", $"stable-{index:D6}",
                $"{displayPrefix} {index:D5}", WindowsAppLibraryKind.Game,
                Installed: true, Available: true,
                GameLibrarySourceActions.Launch | GameLibrarySourceActions.Artwork,
                $"art-{index:D6}", $"item-{index:D6}"))
            .ToArray();
        private long _revision;

        public string SourceIdentity => "package-test";
        public string Attribution => "Package Fixture";
        public GameLibrarySourceSnapshot Snapshot { get; private set; } = new(
            "package-test", "Package Fixture", 0,
            GameLibrarySourceHealth.Unavailable, []);
        public bool RequiresStaArtwork => false;
        internal GameLibrarySourceHealth Health { get; set; } =
            GameLibrarySourceHealth.Healthy;
        internal bool FailLaunch { get; set; }
        internal int ArtworkLoads { get; private set; }
        internal string? LastLaunchedStableIdentity { get; private set; }
        internal string? ArtworkContent { get; set; } = TinyPng;

        public GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Snapshot = new(SourceIdentity, Attribution, ++_revision, Health, _items);
            return Snapshot;
        }

        public GameLibrarySourceItem? ResolveExact(
            GameLibrarySourceItem item,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _items.SingleOrDefault(candidate =>
                string.Equals(candidate.StableIdentity, item.StableIdentity,
                    StringComparison.Ordinal));
        }

        public GameLibraryLaunchResult Launch(
            GameLibrarySourceItem exactItem,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailLaunch)
                throw new BrokerException("app_not_found", "The app is unavailable.");
            LastLaunchedStableIdentity = exactItem.StableIdentity;
            return new(
                AppLibraryLaunchObservationState.Running,
                GameLibraryLaunchEvidence.LauncherStarted |
                GameLibraryLaunchEvidence.Running);
        }

        public string? LoadArtwork(
            GameLibrarySourceItem exactItem,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArtworkLoads++;
            return ArtworkContent;
        }

        public void Dispose() { }
    }

    private sealed class FakePlayniteBridgeClient : IPlayniteBridgeClient
    {
        private int _active;
        internal PlayniteBridgeConnectionResult ProbeResult { get; set; } =
            new(PlayniteBridgeConnectionKind.Connected, "connected");
        internal TaskCompletionSource<PlayniteBridgeConnectionResult>? PendingProbe { get; set; }
        internal TaskCompletionSource ProbeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? LastSavedToken { get; private set; }
        internal int MaximumConcurrentOperations { get; private set; }

        public async ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrentOperations = Math.Max(MaximumConcurrentOperations, active);
            ProbeStarted.TrySetResult();
            try
            {
                return PendingProbe is null
                    ? ProbeResult
                    : await PendingProbe.Task.ConfigureAwait(false);
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public ValueTask SaveCredentialAsync(
            string token, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSavedToken = token;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteCredentialAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSavedToken = null;
            return ValueTask.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class InlineSta : IShellStaExecutor
    {
        internal static InlineSta Instance { get; } = new();
        public Task<T> RunAsync<T>(
            Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }

    private sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wrail-game-launcher-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
