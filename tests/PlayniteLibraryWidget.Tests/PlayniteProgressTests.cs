using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod, Timeout(30_000)]
    public async Task TrackedChoicesSurviveRestartPerGameAndStayIndependentOfPinSelection()
    {
        var provider = new FakeHost(2);
        var widget = Create(provider, out TestApplicationService app);
        var first = new PlayniteAchievement("First", "", false, null, null, null, false) { Id = "first", Progress = new(2, 10) };
        var second = first with { Id = "second", Name = "Second" };
        first = first with { Progress = null };
        var data = new PlayniteAchievements(true, 2, 0, [first, second]) { ConnectionId = "profile" };
        async Task Start()
        {
            app.AchievementsHandler = (_, _) => ValueTask.FromResult(data);
            await Interactive(widget);
            await Ready(widget, provider);
            await widget.WhenWarmStateIdleAsync();
        }
        async Task Open(int index)
        {
            var game = PageNodes(widget, Snapshot(widget, 262001).Root)
                .Where(n => n.ActionId == PlayniteLibraryActions.DetailsOpen).ElementAt(index);
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
            await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
            await Bounded(widget.WhenDetailsIdleAsync(), "achievements");
        }
        async Task Choose(int index, string label, Func<bool> applied)
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-options");
            using var lease = await host.AcquireAsync(PlayniteAchievementBrowse.ListId, index, 1);
            var row = lease.Range.Items.Single();
            var action = row.Root.ContextActions.Single(a => a.Label == label);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(row.Key, ControllerButton.A,
                contextActionOwnerId: row.Root.Id, contextActionId: action.ActionId));
            await WaitFor(applied, label);
        }
        async Task Close() => await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        await Start();
        await Open(0);
        await Choose(0, "Track achievement", () => widget.RenderState.Value.DetailsExtras.TrackedAchievementId == first.Id);
        Assert.IsEmpty(Snapshot(widget, 262002).PinnedLayouts);
        await Close();
        await Open(1);
        Assert.IsNull(widget.RenderState.Value.DetailsExtras.TrackedAchievementId);
        await Choose(1, "Track achievement", () => widget.Organization.TrackedAchievements.Count == 2);
        await Background(widget);
        await WidgetTestHost.DestroyAsync(widget);
        widget = Create(provider, out app);
        await Start();
        await Open(0);
        Assert.AreEqual(first.Id, widget.RenderState.Value.DetailsExtras.TrackedAchievementId);
        await Choose(1, "Pin progress", () => widget.RenderState.Value.Progress.Target?.AchievementId == second.Id);
        var pin = WidgetTestHost.CreatePinnedLayoutHost(widget, "track-with-pin", 262003);
        Assert.IsTrue(await pin.SelectAsync("playnite.achievement-progress"));
        Assert.AreEqual(first.Id, widget.RenderState.Value.DetailsExtras.TrackedAchievementId);
        Assert.IsTrue(await pin.RevokeAsync());
        Assert.AreEqual(first.Id, widget.RenderState.Value.DetailsExtras.TrackedAchievementId);
        await Choose(1, "Track achievement", () => widget.RenderState.Value.DetailsExtras.TrackedAchievementId == second.Id);
        await Choose(1, "Stop tracking", () => widget.RenderState.Value.DetailsExtras.TrackedAchievementId is null);
        Assert.HasCount(1, widget.Organization.TrackedAchievements);
        await Close();
        await Open(1);
        Assert.AreEqual(second.Id, widget.RenderState.Value.DetailsExtras.TrackedAchievementId);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public void ShelfResolvesSavedChoiceOnlyAgainstOneVisibleMatchingProviderRecord()
    {
        var item = new PlayniteAchievement("Counter", "", false, null, null, null, false) { Id = "same", Progress = new(4, 10) };
        var data = new PlayniteAchievements(true, 1, 0, [item]) { ConnectionId = "one" };
        var choice = new TrackedAchievement("game", "one", data.Provider, "same");
        Assert.AreEqual(item, PlayingAwards.From("game", data, choice).Tracked?.Award);
        Assert.IsNull(PlayingAwards.From("another", data, choice).Tracked);
        Assert.IsNull(PlayingAwards.From("game", data with { ConnectionId = "two" }, choice).Tracked);
        Assert.IsNull(PlayingAwards.From("game", data with { Provider = PlayniteAchievementProvider.PlayniteAchievements }, choice).Tracked);
        Assert.IsNull(PlayingAwards.From("game", data with { Items = [item with { Hidden = true }] }, choice).Tracked);
        Assert.IsNull(PlayingAwards.From("game", data with { Items = [item, item] }, choice).Tracked);
    }

    [TestMethod]
    public async Task ProgressArtworkBelongsToTheExactCurrentTarget()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        await Interactive(widget); await Ready(widget, provider);
        var item = new PlayniteAchievement("Target", "", false, null, null, null, false)
            { Id = "exact", Progress = new(2, 10), Icon = "C:/Fixture/achievement.png" };
        var target = new PlayniteProgressTarget("profile", PlayniteAchievementProvider.PlayniteAchievements, "game", "Game", "exact", item);
        var model = PrivateField<WidgetModel<PlayniteLibraryRenderState>>(widget, "_model");
        model.Update(state => state with { Progress = new(target) });
        var icon = Nodes(Snapshot(widget, 205001).PinnedLayouts.Single().Root!).Single(node => node.Id == "playnite.progress.icon");
        Assert.IsNotNull(icon.ArtworkHandle);
        var source = await widget.OnResolveArtworkSourceAsync(new(icon.ArtworkHandle));
        Assert.AreEqual(item.Icon, ((WidgetFileArtwork)source!).Path);
        model.Update(state => state with { Progress = new(target with { ConnectionId = "other-profile" }) });
        Assert.IsNull(await widget.OnResolveArtworkSourceAsync(new(icon.ArtworkHandle)), "A previous profile's handle must not resolve through the new tracker.");
        model.Update(state => state with { Progress = new(target with { Initial = item with { Icon = null } }) });
        Assert.AreEqual(ViewNodeKind.Icon, Nodes(Snapshot(widget, 205002).PinnedLayouts.Single().Root!).Single(node => node.Id == "playnite.progress.icon").Kind);
        model.Update(state => state with { Progress = new() });
        Assert.IsNull(await widget.OnResolveArtworkSourceAsync(new(icon.ArtworkHandle)));
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public void TrackingAcceptsUnmeasuredAchievementsWhileProgressPinsRequireACounter()
    {
        var item = new PlayniteAchievement("Target", "Description", false, null, null, null, false)
            { Id = "exact", Progress = new(2, 10) };
        Assert.IsTrue(PlayniteAchievementPresentation.CanTrack(item));
        Assert.IsTrue(PlayniteAchievementPresentation.CanTrack(item with { Progress = null }));
        Assert.IsFalse(PlayniteAchievementPresentation.CanPinProgress(item with { Progress = null }));
        foreach (var rejected in new[] { item with { Unlocked = true }, item with { Hidden = true },
            item with { Id = null }, item with { Progress = new(0, 0) },
            item with { Progress = new(-1, 10) }, item with { Progress = new(10, 10) }, item with { Progress = new(11, 10) } })
        {
            Assert.IsFalse(PlayniteAchievementPresentation.CanTrack(rejected));
        }
    }

    [TestMethod]
    public async Task LiveUpdatesRefreshOnlySelectedDetailsAndRetireWithActiveLifetime()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        var changes = System.Threading.Channels.Channel.CreateUnbounded<PlayniteEventBatch>();
        var reads = 0; var polls = 0; var cancelled = 0; var baselines = 0;
        application.AchievementsHandler = (_, _) => { Interlocked.Increment(ref reads); return ValueTask.FromResult(new PlayniteAchievements(true, 0, 0, [])); };
        application.EventsHandler = async (after, token) =>
        {
            if (after < 0) { Interlocked.Increment(ref baselines); return new("profile", "process", true, 0, []); }
            Interlocked.Increment(ref polls);
            try { return await changes.Reader.ReadAsync(token); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
        };
        await Interactive(widget); await Ready(widget, provider);
        await WaitIndexed(() => polls == 1);
        var game = PageNodes(widget, Snapshot(widget, 203001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "initial selected achievements");
        var initialReads = reads;
        await changes.Writer.WriteAsync(new("profile", "process", false, 1, [new(1, "achievements-changed", "other-game", null, 1)]));
        await WaitIndexed(() => polls >= 2);
        Assert.AreEqual(initialReads, reads);
        await changes.Writer.WriteAsync(new("profile", "process", false, 2,
            [new(2, "achievements-changed", widget.RenderState.Value.DetailsItem!.Value.SavedId, null, 1)]));
        await WaitIndexed(() => reads == initialReads + 1);
        await WaitIndexed(() => polls >= 3);
        await Background(widget);
        await WaitIndexed(() => cancelled == 1);
        var before = baselines;
        await Interactive(widget);
        await WaitIndexed(() => baselines == before + 1);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task ProgressAdmissionDoesNotWaitForeverForAHostSelection()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(new PlayniteAchievements(true, 1, 0,
            [new("Target", "Description", false, null, null, null, false) { Id = "exact", Progress = new(2, 10) }]) { ConnectionId = "profile" });
        await Interactive(widget); await Ready(widget, provider);
        var game = PageNodes(widget, Snapshot(widget, 202001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "achievements");
        _ = Snapshot(widget, 202002);
        var query = PrivateField<PlayniteAchievementBrowse>(widget, "achievementBrowse");
        var track = widget.GetType().GetMethod("PinAchievementAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await (ValueTask)track.Invoke(widget, [query, query.All.Single(), CancellationToken.None])!;
            Assert.IsFalse(widget.RenderState.Value.Progress.Pending, "A broker receipt is not a promise of selection.");
            Assert.IsNotNull(widget.RenderState.Value.Progress.Target);
        }
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task ProgressPinIsPassiveAndCannotSubstituteForeignOrDuplicateAchievement()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        var achievement = new PlayniteAchievement("Target", "Description", false, null, null, null, false) { Id = "exact", Progress = new(2, 10) };
        var data = new PlayniteAchievements(true, 1, 0, [achievement]) { ConnectionId = "profile-one", Provider = PlayniteAchievementProvider.PlayniteAchievements };
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(data);
        await Interactive(widget); await Ready(widget, provider);
        Assert.AreEqual(0, Snapshot(widget, 200001).PinnedLayouts.Count);
        var model = PrivateField<WidgetModel<PlayniteLibraryRenderState>>(widget, "_model");
        model.Update(state => state with { Progress = new(new("profile-one", data.Provider, "saved-00000", "Game", "exact", achievement)) });
        _ = Snapshot(widget, 200002);
        var host = WidgetTestHost.CreatePinnedLayoutHost(widget, "launcher.test", 200003);
        Assert.IsTrue(await host.SelectAsync("playnite.achievement-progress"));
        var resource = PrivateField<WidgetQueryResource<PlayniteProgressTarget, PlayniteAchievements>>(widget, "progressData");
        await resource.WhenIdleAsync();
        var snapshot = Snapshot(widget, 200010);
        Assert.AreEqual(PinnedInteractionMode.Passive, snapshot.PinnedLayouts.Single().Interaction);
        Assert.IsTrue(Nodes(snapshot.PinnedLayouts.Single().Root!).Any(node => node.Text == "Target"));
        Assert.IsFalse(await host.RouteActionAsync(ControllerButton.A, "playnite.progress"));
        data = data with { ConnectionId = "other-profile", Items = [achievement with { Name = "Foreign secret" }] };
        var target = widget.RenderState.Value.Progress.Target!;
        var refresh = new TaskCompletionSource<PlayniteAchievements>(TaskCreationOptions.RunContinuationsAsynchronously);
        application.AchievementsHandler = (_, _) => new(refresh.Task);
        resource.EnsureLoaded(target, forceRefresh: true);
        Assert.IsTrue(Nodes(Snapshot(widget, 200011).PinnedLayouts.Single().Root!).Any(node => node.Text == "Target"),
            "A current value must not flash unavailable while an ordinary refresh is pending.");
        refresh.SetResult(data); await resource.WhenIdleAsync();
        var rejected = Snapshot(widget, 200011);
        Assert.IsFalse(Nodes(rejected.PinnedLayouts.Single().Root!).Any(node => node.Text == "Foreign secret"));
        Assert.IsTrue(Nodes(rejected.PinnedLayouts.Single().Root!).Any(node => node.Text == "Progress unavailable"));
        data = data with { ConnectionId = "profile-one", Items = [achievement, achievement] };
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(data);
        resource.EnsureLoaded(target, forceRefresh: true); await resource.WhenIdleAsync();
        Assert.IsTrue(Nodes(Snapshot(widget, 200012).PinnedLayouts.Single().Root!).Any(node => node.Text == "Progress unavailable"));
        Assert.IsTrue(await host.RevokeAsync());
        Assert.AreEqual(0, Snapshot(widget, 200013).PinnedLayouts.Count);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task ReplacingTrackedAchievementRetainsSelectionAndRejectsLatePreviousData()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        var first = new PlayniteAchievement("First", "", false, null, null, null, false) { Id = "first", Progress = new(2, 10) };
        var second = first with { Id = "second", Name = "Second", Progress = new(3, 20) };
        var data = new PlayniteAchievements(true, 2, 0, [first, second]) { ConnectionId = "profile" };
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(data);
        await Interactive(widget); await Ready(widget, provider);
        var game = PageNodes(widget, Snapshot(widget, 204001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "achievements");
        _ = Snapshot(widget, 204002);
        var query = PrivateField<PlayniteAchievementBrowse>(widget, "achievementBrowse");
        var track = widget.GetType().GetMethod("PinAchievementAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (ValueTask)track.Invoke(widget, [query, query.All.First(entry => entry.Item.Id == "first"), CancellationToken.None])!;
        var host = WidgetTestHost.CreatePinnedLayoutHost(widget, "launcher.test", 204003);
        Assert.IsTrue(await host.SelectAsync("playnite.achievement-progress"));
        var pin = PrivateField<PinnedLayoutHandle>(widget, "progressPin");
        var selection = pin.Selection;
        var resource = PrivateField<WidgetQueryResource<PlayniteProgressTarget, PlayniteAchievements>>(widget, "progressData");
        await resource.WhenIdleAsync();
        var late = new TaskCompletionSource<PlayniteAchievements>(TaskCreationOptions.RunContinuationsAsynchronously);
        application.AchievementsHandler = (_, _) => new(late.Task);
        resource.EnsureLoaded(widget.RenderState.Value.Progress.Target!, forceRefresh: true);
        await (ValueTask)track.Invoke(widget, [query, query.All.First(entry => entry.Item.Id == "second"), CancellationToken.None])!;
        Assert.IsTrue(pin.IsSelected);
        Assert.AreEqual(selection, pin.Selection, "Replacing content must not replace the host window selection.");
        Assert.AreEqual("second", widget.RenderState.Value.Progress.Target!.AchievementId);
        Assert.IsFalse(widget.RenderState.Value.Progress.Pending);
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(data);
        late.SetResult(data with { Items = [first with { Name = "Late previous title" }] });
        await resource.WhenIdleAsync();
        var nodes = Nodes(Snapshot(widget, 204004).PinnedLayouts.Single().Root!).ToArray();
        Assert.IsTrue(nodes.Any(node => node.Text == "Second"));
        Assert.IsFalse(nodes.Any(node => node.Text == "Late previous title"));
        Assert.IsTrue(await host.RevokeAsync());
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task ProviderRefreshDoesNotRunOnOpenOrReplayAfterLeavingWidget()
    {
        var provider = new FakeHost(1);
        var widget = Create(provider, out TestApplicationService application);
        application.AchievementsHandler = (_, _) => ValueTask.FromResult(new PlayniteAchievements(true, 0, 0, []) { CanRefresh = true });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        application.ProviderRefreshHandler = async (_, token) =>
        { requests++; started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return PlayniteProviderRefreshResult.Completed; };
        await Interactive(widget); await Ready(widget, provider);
        var game = PageNodes(widget, Snapshot(widget, 201001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
        await widget.OnActionAsync(new(PlayniteLibraryDetailsPresentation.SyncGameAction, "sync"));
        Assert.AreEqual(0, requests, "Other details tabs must not request provider synchronization.");
        await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
        await Bounded(widget.WhenDetailsIdleAsync(), "cached achievements");
        Assert.AreEqual(0, requests);
        var nodes = Nodes(Snapshot(widget, 201002).Root).ToArray();
        Assert.IsTrue(nodes.SelectMany(node => node.Shortcuts).Any(shortcut =>
            shortcut.Button == ControllerButton.Y && shortcut.ActionId == PlayniteLibraryDetailsPresentation.SyncGameAction));
        Assert.AreEqual("Sync this game", nodes.Single(node => node.ActionId == PlayniteLibraryDetailsPresentation.SyncGameAction).Text);
        await widget.OnActionAsync(new(PlayniteLibraryDetailsPresentation.SyncGameAction, "sync"));
        await Bounded(started.Task, "provider refresh admitted");
        await widget.OnActionAsync(new(PlayniteLibraryDetailsPresentation.SyncGameAction, "sync"));
        Assert.AreEqual(1, requests);
        await Background(widget); await Interactive(widget);
        Assert.IsFalse(widget.RenderState.Value.DetailsExtras.AchievementRefreshBusy);
        Assert.AreEqual(1, requests);
        await WidgetTestHost.DestroyAsync(widget);
    }
    [TestMethod, Timeout(30_000)]
    public async Task UnifiedRefreshSupersedesPreProviderReadAndKeepsCurrentProgress()
    {
        var host = new FakeHost(1);
        var widget = Create(host, out TestApplicationService application);
        var original = new PlayniteAchievements(true, 2, 0, []) { CanRefresh = true };
        var updated = original with { Unlocked = 1 };
        var stale = new TaskCompletionSource<PlayniteAchievements>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerDone = new TaskCompletionSource<PlayniteProviderRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        application.AchievementsHandler = (_, token) =>
        {
            var read = Interlocked.Increment(ref reads);
            if (read == 2)
            {
                token.Register(() => staleCanceled.TrySetResult());
                readStarted.TrySetResult(); return new(stale.Task);
            }
            return ValueTask.FromResult(read == 1 ? original : updated);
        };
        application.ProviderRefreshHandler = (_, _) => { providerStarted.TrySetResult(); return new(providerDone.Task); };
        await Interactive(widget); await Ready(widget, host);
        try
        {
            var game = PageNodes(widget, Snapshot(widget, 206001).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
            await widget.OnActionAsync(new(DetailAction + "tab.achievements", "tab"));
            await Bounded(widget.WhenDetailsIdleAsync(), "initial cache");
            await widget.OnActionAsync(new(PlayniteLibraryDetailsPresentation.RetryAchievementsAction, "retry"));
            await Bounded(readStarted.Task, "pre-provider cache read");
            await widget.OnActionAsync(new(PlayniteLibraryDetailsPresentation.SyncGameAction, "sync"));
            await Bounded(providerStarted.Task, "provider requested through Sync this game");
            providerDone.SetResult(PlayniteProviderRefreshResult.Completed);
            await Bounded(staleCanceled.Task, "pre-provider read retired");
            // The SDK serializes reads until the canceled provider releases its work.
            stale.SetResult(original);
            await Bounded(widget.WhenDetailsIdleAsync(), "fresh progress");
            Assert.AreEqual(1, widget.RenderState.Value.DetailsExtras.Achievements!.Unlocked);
            Assert.AreEqual(3, reads);
        }
        finally
        {
            stale.TrySetResult(original);
            providerDone.TrySetResult(PlayniteProviderRefreshResult.Completed);
            await WidgetTestHost.DestroyAsync(widget);
        }
    }

}
