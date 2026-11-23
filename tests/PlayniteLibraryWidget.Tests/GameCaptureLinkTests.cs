using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Playnite;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public async Task OlderLinkedCaptureSupportsViewerUnlinkUndoAndReturnOnLastPage()
    {
        const string prefix = "playnite-library.related-captures";
        var game = Guid.NewGuid();
        var profile = Guid.NewGuid().ToString("D");
        var subject = new CaptureContextReference(CaptureContextReferenceRole.Subject, CaptureRelatedIdentity.GameKind,
            CaptureRelatedIdentity.Game(profile, game), "Game");
        var items = Enumerable.Range(1,13).Select(i =>
        {
            var id = i.ToString("x32");
            return new CaptureLibraryItem(id, new(id, "image/png", 80, 80, 120, 0, long.MaxValue), 500) { ContextCount = 1, Revision = i };
        }).ToArray();
        var last = items[^1];
        bool linked = true;
        var groupRevision = 1;
        string? removedOperation = null;
        var provider = new FakeHost(1) { ItemFactory = i => Item(i) with { SavedId = game.ToString("D") } };
        provider.ConfigureServices = builder => builder
            .WithHandler(WidgetPermissionCapabilities.Check, (request, _) => ValueTask.FromResult(new WidgetPermissionStatuses(
                request.CapabilityIds.Select(id => new WidgetPermissionStatus(id, WidgetPermissionState.Granted)).ToArray())))
            .WithHandler(WidgetCaptureLibraryCapabilities.Query, (request, _) =>
            {
                Assert.IsFalse(request.Query.ExcludeReference);
                var available = items.Where(item => item != last || linked).ToArray();
                return ValueTask.FromResult(new CaptureLibraryQueryPage(available.Skip(request.Offset).Take(request.Count).ToArray(),
                    request.Offset, available.Length, 120 * available.Length, groupRevision.ToString()));
            })
            .WithHandler(WidgetCaptureLibraryCapabilities.Runs, (_, _) =>
                ValueTask.FromException<CaptureLibraryRunsPage>(new WidgetCapabilityException("unsupported_operation", "Individual previews")))
            .WithHandler(WidgetCaptureLibraryCapabilities.ChangeDestination, (request, _) =>
            {
                Assert.AreEqual(last.Id, request.CaptureId);
                Assert.AreEqual(subject.Id, request.DestinationId);
                Assert.AreEqual(last.Revision, request.ContentRevision);
                if (request.Undo) Assert.AreEqual(removedOperation, request.OperationId);
                else { Assert.AreEqual(request.ExpectedRevision, groupRevision.ToString()); removedOperation = request.OperationId; }
                linked = request.Undo;
                groupRevision++;
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            });
        var widget = Create(provider, out TestApplicationService app);
        app.CaptureIntegrationHandler = _ => ValueTask.FromResult<PlayniteCaptureIntegration?>(new(profile, [], [], "binding"));
        IEnumerable<ViewNode> Current() => Nodes(Snapshot(widget, 9230).Root);
        async Task Activate(string id)
        {
            await WaitFor(() => Current().Any(node => node.Id == id), id);
            var action = Current().Single(node => node.Id == id);
            await widget.OnActionAsync(new(action.ActionId!, action.Id));
        }
        try
        {
            await Interactive(widget); await Ready(widget, provider);
            var tile = PageNodes(widget, Snapshot(widget, 9229).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
            await widget.OnActionAsync(new(tile.ActionId!, tile.Id));
            await widget.OnActionAsync(new(DetailAction + "tab.captures", "tab"));
            var target = prefix + ".item." + last.Id;
            await Activate(prefix + ".linked");
            await Activate(prefix + ".picker.next");
            await WaitFor(() => Current().Any(node => node.Id == target), "Last page");
            var finalPage = Snapshot(widget, 9231);
            Assert.AreEqual(target, finalPage.FocusGroupEntryRequest?.TargetId);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(finalPage).Count);
            await Activate(target);
            Assert.AreEqual(last.Id, Current().Single(node => node.Id == prefix + ".player").MediaPlayer!.Gallery!.Single().Id);
            await widget.OnActionAsync(new(prefix + ".viewer.back", prefix + ".viewer"));
            var oldAction = Current().Single(node => node.Id == target).ContextActions.Single().ActionId;
            await widget.OnActionAsync(new(oldAction,target));
            await WaitFor(() => Current().Any(node => node.Text == "No captures on this page."), "Last item removed");
            Assert.AreEqual(prefix + ".undo", Snapshot(widget, 9232).FocusGroupEntryRequest?.TargetId);
            await Activate(prefix + ".undo");
            await WaitFor(() => Current().Any(node => node.Id == target), "Restored last item");
            Assert.AreEqual(target, Snapshot(widget, 9233).FocusGroupEntryRequest?.TargetId);
            await widget.OnActionAsync(new(oldAction, target));
            Assert.IsTrue(linked);
            Assert.AreEqual(3, groupRevision, "A superseded unlink must not change the restored destination.");
            await Activate(prefix + ".picker.previous");
            await WaitFor(() => Current().Any(node => node.Text == "1–12 of 13"), "Previous page");
            await Activate(prefix + ".picker.back");
            await WaitFor(() => Current().Any(node => node.Id == prefix + ".linked"), "Back to timeline");
            Assert.AreEqual(prefix + ".linked", Snapshot(widget, 9231).InitialFocusId);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task VirtualizedGameChooserCanRetryAnInterruptedWrite()
    {
        var games = Enumerable.Range(0, 13).Select(i => Item(i) with { SavedId = Guid.NewGuid().ToString("D") }).ToArray();
        var profile = Guid.NewGuid().ToString("D");
        var destination = games[^1];
        var media = new[] { "first.exe", "second.exe" }.Select((process, index) =>
        {
            var id = (index + 1).ToString("x32");
            return new CaptureLibraryItem(id, new(id, "image/png", 80, 80, 100, 0, long.MaxValue)
                { SourceApplication = new("Player", "Game") { ProcessName = process } }, 500) { Revision = 1 };
        }).ToArray();
        var receipts = new Dictionary<string, CaptureLibraryAssociation>();
        var firstCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new FakeHost(games.Length) { ItemFactory = index => games[index] };
        provider.ConfigureServices = builder => builder
            .WithHandler(WidgetPermissionCapabilities.Check, (request, _) => ValueTask.FromResult(new WidgetPermissionStatuses(
                request.CapabilityIds.Select(id => new WidgetPermissionStatus(id, WidgetPermissionState.Granted)).ToArray())))
            .WithHandler(WidgetCaptureLibraryCapabilities.GetMetadata, (request, _) => ValueTask.FromResult(media.Single(item => item.Id == request.Id)))
            .WithHandler(WidgetCaptureLibraryCapabilities.EnsureSubject, async (request, token) =>
            {
                var reference = request.Subject;
                Assert.AreEqual(CaptureRelatedIdentity.Game(profile, Guid.Parse(destination.SavedId)), reference.Id);
                var exists = receipts.TryGetValue(request.IdempotencyKey, out var receipt);
                if (!exists) receipts.Add(request.IdempotencyKey, receipt = new(Guid.NewGuid().ToString("N"), request.CaptureId,
                    new("widgetrail.samples.playnite-library", "example.publisher", "example.instance"), [reference], null, null, 500));
                if (++calls == 1)
                {
                    firstCommitted.SetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                return new CaptureLibrarySubjectResult(exists ? CaptureContextSaveOutcome.AlreadySaved : CaptureContextSaveOutcome.Saved);
            });
        var notice = new TaskCompletionSource<WidgetNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup = provider.ConfigureServices;
        provider.ConfigureServices = builder =>
        {
            setup(builder);
            builder.WithHandler(WidgetNotificationCapabilities.Publish, (request, _) =>
            {
                if (request.Notification is { } message) notice.TrySetResult(message);
                return ValueTask.FromResult(new WidgetNotificationReceipt(true));
            });
        };
        var widget = Create(provider, out TestApplicationService app);
        app.DetailsHandler = (id, _) => ValueTask.FromResult<PlayniteGameDetails?>(new(games.Single(g => g.SavedId == id)));
        app.CaptureIntegrationHandler = _ => ValueTask.FromResult<PlayniteCaptureIntegration?>(new(profile, [], [], "binding"));
        var offer = WidgetIntentRequest.Create(WidgetIntentContracts.CaptureAssociation, System.Text.Json.JsonSerializer.SerializeToElement(
            new { captures = media.Select(item => new { id = item.Id, revision = item.Revision }) }));
        try
        {
            await Interactive(widget); await Ready(widget, provider);
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(offer));
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "capture-game-tests");
            const string results = "playnite-library.capture-game.results";
            IEnumerable<ViewNode> Current() => Nodes(host.PublishSnapshot().Root);
            await WaitFor(() => Current().Any(n => n.Id == results), "Virtual results");
            var range = await host.AcquireAsync(results, 0, games.Length);
            var index = range.Range.Items.ToList().FindIndex(row => row.Key == destination.SavedId);
            range.Dispose();
            Assert.IsTrue(index >= 0);
            using var lease = await host.AcquireAsync(results, index, 1);
            Assert.AreEqual(destination.SavedId, lease.Range.Items.Single().Key);
            Assert.AreEqual(0, calls, "Realizing and focusing a game must not write a link.");
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(destination.SavedId, ControllerButton.A));
            await Bounded(firstCommitted.Task, "First receipt written before acknowledgement");
            await Background(widget);
            Assert.AreEqual(1, calls, "A background chooser continued the batch");
            await Interactive(widget);
            await WaitFor(() => Current().Any(node => node.Id == "playnite-library.capture-game.retry-link"), "Retry remaining");
            var retry = Current().Single(node => node.Id == "playnite-library.capture-game.retry-link");
            await widget.OnActionAsync(new(retry.ActionId!, retry.Id));
            await Bounded(notice.Task, "Retried handoff posted its banner");
            Assert.AreEqual(WidgetNotificationRetention.BannerOnly, notice.Task.Result.Retention);
            StringAssert.Contains(notice.Task.Result.Body!, "1 linked, 1 already linked");
            Assert.IsNull(widget.RenderState.Value.GameChooser);
            CollectionAssert.AreEquivalent(media.Select(item => item.Id).ToArray(), receipts.Values.Select(link => link.CaptureId).ToArray());
            Assert.AreEqual(3, calls);
            Assert.AreEqual(0, widget.Organization.CaptureProcesses.Count, "A mixed-source selection taught an arbitrary executable");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReverseCaptureOfferRequiresExplicitActivationAndCurrentProfile(bool switchProfile)
    {
        var profile = Guid.NewGuid().ToString("D");
        var game = Guid.NewGuid().ToString("D");
        var visibleGame = Item(1) with { SavedId = game };
        var provider = new FakeHost(1) { ItemFactory = _ => visibleGame };
        var items = Enumerable.Range(1, 3).Select(i =>
        {
            var id = i.ToString("x32");
            return new CaptureLibraryItem(id, new(id, "image/png", 100, 100, 120, 0, long.MaxValue), 500) { Revision = 1 };
        }).ToArray();
        var written = new System.Collections.Concurrent.ConcurrentBag<string>();
        var granted = true;
        provider.ConfigureServices = builder => builder
            .WithHandler(WidgetPermissionCapabilities.Check, (request, _) => ValueTask.FromResult(new WidgetPermissionStatuses(
                request.CapabilityIds.Select(id => new WidgetPermissionStatus(id, granted ? WidgetPermissionState.Granted : WidgetPermissionState.Denied)).ToArray())))
            .WithHandler(WidgetCaptureLibraryCapabilities.GetMetadata, (request, _) => ValueTask.FromResult(items.Single(item => item.Id == request.Id)))
            .WithHandler(WidgetCaptureLibraryCapabilities.EnsureSubject, (request, _) =>
            {
                Assert.AreEqual(CaptureRelatedIdentity.Game(profile, Guid.Parse(game)), request.Subject.Id);
                written.Add(request.CaptureId);
                return ValueTask.FromResult(new CaptureLibrarySubjectResult(CaptureContextSaveOutcome.Saved));
            });
        var widget = Create(provider, out TestApplicationService app);
        app.DetailsHandler = (_, _) => ValueTask.FromResult<PlayniteGameDetails?>(new(visibleGame));
        app.CaptureIntegrationHandler = _ => ValueTask.FromResult<PlayniteCaptureIntegration?>(new(profile, [], [], "binding"));
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(new { captures = items.Select(item => new { id = item.Id, revision = item.Revision }) });
        var offer = WidgetIntentRequest.Create(WidgetIntentContracts.CaptureAssociation, payload, WidgetIntentPresentation.OpenWidget);
        try
        {
            await Interactive(widget); await Ready(widget, provider);
            granted = false;
            Assert.IsFalse(await widget.IsIntentAvailableAsync(offer));
            granted = true;
            var connected = app.CaptureIntegrationHandler;
            app.CaptureIntegrationHandler = _ => ValueTask.FromResult<PlayniteCaptureIntegration?>(null);
            Assert.IsFalse(await widget.IsIntentAvailableAsync(offer));
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(offer));
            Assert.IsTrue(written.IsEmpty);
            app.CaptureIntegrationHandler = connected;
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(offer));
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(offer));
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "capture-offer-tests");
            IEnumerable<ViewNode> Current() => Nodes(host.PublishSnapshot().Root);
            const string results = "playnite-library.capture-game.results";
            await WaitFor(() => Current().Any(node => node.Id == results), "Choose game");
            var search = Current().Single(node => node.Id == "playnite-library.capture-game.search");
            var suggestions = await widget.OnSuggestTextAsync(new(search.Id, search.TextEntrySuggestions!.Key, Guid.NewGuid().ToString("N"), 1, "Game"), default);
            Assert.AreEqual(visibleGame.Presentation.DisplayName, suggestions.Items.Single().Value);
            using var previous = await host.AcquireAsync(results, 0, 1);
            await widget.OnActionAsync(new(search.ActionId!, search.Id) { CommittedText = suggestions.Items[0].Value });
            var filtered = host.PublishSnapshot();
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(filtered).Count);
            Assert.AreNotEqual(WidgetOperationAdmission.Enqueued, previous.RouteAction(game, ControllerButton.A));
            Assert.IsTrue(written.IsEmpty);
            using var choice = await host.AcquireAsync(results, 0, 1);
            if (switchProfile) profile = Guid.NewGuid().ToString("D");
            items[1] = items[1] with { Revision = 2 };
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, choice.RouteAction(game, ControllerButton.A));
            await WaitFor(() => Current().Any(node => node.Text?.StartsWith(switchProfile ? "Playnite or its permissions changed" :
                $"2 linked to {visibleGame.Presentation.DisplayName}. 1 could not be linked.", StringComparison.Ordinal) == true), "Link outcome");
            CollectionAssert.AreEquivalent(switchProfile ? Array.Empty<string>() : new[] { items[0].Id, items[2].Id }, written.ToArray());
            if (!switchProfile)
            {
                Assert.AreEqual("playnite-library.capture-game.retry-link", host.PublishSnapshot().FocusGroupEntryRequest?.TargetId);
                Assert.AreNotEqual(WidgetOperationAdmission.Enqueued, choice.RouteAction(game, ControllerButton.A));
                Assert.AreEqual(2, written.Count, "A stale row cannot retry a different set");
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task ReviewCaptureRunUsesReviewedSelectionAndRevision(bool libraryChanged, bool partialFailure)
    {
        const string prefix = "playnite-library.related-captures";
        var game = Guid.NewGuid();
        var profile = Guid.NewGuid().ToString("D");
        var interval = new CaptureProcessInterval(new string('c', 64), 100, 200);
        var media = Enumerable.Range(1, 258).Select(index =>
        {
            var id = index.ToString("x32");
            return new CaptureLibraryItem(id, new(id, "image/png", 100, 100, 120, 0, long.MaxValue)
            {
                SourceProcess = interval,
                SourceApplication = new("Test player", "Test game") { ProcessName = "test-player.exe" }
            }, 200) { Revision = 1 };
        }).ToArray();
        var linked = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        var revision = "a";
        var provider = new FakeHost(1) { ItemFactory = i => Item(i) with { SavedId = game.ToString("D") } };
        provider.ConfigureServices = builder => builder
            .WithHandler(WidgetPermissionCapabilities.Check, (request, _) => ValueTask.FromResult(new WidgetPermissionStatuses(
                request.CapabilityIds.Select(id => new WidgetPermissionStatus(id, WidgetPermissionState.Granted)).ToArray())))
            .WithHandler(WidgetCaptureLibraryCapabilities.Runs, (request, _) =>
            {
                var remaining = media.Where(item => request.Query.ExcludeReference != linked.ContainsKey(item.Id)).ToArray();
                Assert.AreEqual(CaptureRelatedIdentity.Game(profile, game), request.Query.Reference!.Id);
                return ValueTask.FromResult(new CaptureLibraryRunsPage([
                    new(remaining[0], interval.Instance, interval.From, interval.Through, remaining.Length, 0, true, false)
                ], 0, false, revision));
            })
            .WithHandler(WidgetCaptureLibraryCapabilities.Query, (request, _) =>
            {
                var available = media.Where(item => request.Query.ExcludeReference != linked.ContainsKey(item.Id)).ToArray();
                if (request.Query.SourceIntervals is not null)
                {
                    Assert.AreEqual(interval, request.Query.SourceIntervals.Single());
                    if (request.Revision != revision) throw new WidgetCapabilityException("stale_revision", "Library changed");
                }
                return ValueTask.FromResult(new CaptureLibraryQueryPage(available.Skip(request.Offset).Take(request.Count).ToArray(),
                    request.Offset, available.Length, available.Length * 120, revision));
            })
            .WithHandler(WidgetCaptureLibraryCapabilities.EnsureSubject, (request, _) =>
            {
                var item = media.Single(item => item.Id == request.CaptureId);
                if (partialFailure && !linked.IsEmpty)
                    return ValueTask.FromResult(new CaptureLibrarySubjectResult(CaptureContextSaveOutcome.Unavailable));
                linked.TryAdd(item.Id, true);
                revision = "b";
                return ValueTask.FromResult(new CaptureLibrarySubjectResult(CaptureContextSaveOutcome.Saved));
            });
        var widget = Create(provider, out TestApplicationService app);
        app.CaptureIntegrationHandler = _ => ValueTask.FromResult<PlayniteCaptureIntegration?>(new(profile, [], [], "binding"));
        try
        {
            await Interactive(widget);
            await Ready(widget, provider);
            var tile = PageNodes(widget, Snapshot(widget, 9000).Root).First(node => node.ActionId == PlayniteLibraryActions.DetailsOpen);
            await widget.OnActionAsync(new(tile.ActionId!, tile.Id));
            await widget.OnActionAsync(new(DetailAction + "tab.captures", "tab"));
            IEnumerable<ViewNode> Current() => Nodes(Snapshot(widget, 9001).Root);
            await WaitFor(() => Current().Any(node => node.Id == prefix + ".link"), "Link captures");
            await widget.OnActionAsync(new(prefix + ".link", prefix + ".link"));
            var first = prefix + ".picker.item." + media[0].Id;
            await WaitFor(() => Current().Any(node => node.Id == first), "Run is available");
            var session = Current().Single(node => node.Id == first);
            await widget.OnActionAsync(new(session.ActionId!, session.Id));
            var review = Snapshot(widget, 9002);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(review).Count, string.Join(", ", ViewSnapshotValidator.Validate(review)));
            var rows = Current().Single(node => node.Id == prefix + ".review.rows");
            var original = Current().Single(node => node.Id == prefix + ".review.submit");
            Assert.AreEqual(256, rows.IndexedSelection!.Count);
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "review-captures");
            using var lease = await host.AcquireAsync(rows.Id, 0, 2);
            var removed = lease.Range.Items[0];
            Assert.IsNotNull(lease.RouteSelection(removed.Key, new(rows.IndexedSelection.Epoch, IndexedSelectionGesture.Toggle)));
            await WaitFor(() => Current().Single(node => node.Id == rows.Id).IndexedSelection!.Count == 255, "Deselection");
            await widget.OnActionAsync(new(original.ActionId!, original.Id));
            Assert.IsTrue(linked.IsEmpty, "Stale commit was accepted");
            if (libraryChanged) revision = "c";
            var confirm = Current().Single(node => node.Id == original.Id);
            await widget.OnActionAsync(new(confirm.ActionId!, confirm.Id));
            var expectedFeedback = libraryChanged ? "The session changed" : partialFailure ? "1 linked." : "255 captures linked.";
            await WaitFor(() => Current().Any(node => node.Text?.StartsWith(expectedFeedback, StringComparison.Ordinal) == true), expectedFeedback);
            Assert.AreEqual(libraryChanged ? 0 : partialFailure ? 1 : 255, linked.Count);
            Assert.IsFalse(linked.ContainsKey(removed.Key));
            Assert.IsFalse(linked.ContainsKey(media[256].Id));
            if (!libraryChanged && !partialFailure)
            {
                Assert.AreEqual(new PlayniteCaptureProcess(CaptureRelatedIdentity.Game(profile, game), "test-player.exe"),
                    widget.Organization.CaptureProcesses.Single());
                Assert.IsTrue(Current().Any(node => node.Text == "Latest session"));
                Assert.IsTrue(Current().Any(node => node.Text?.Contains("255 screenshots") == true));
            }
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task GameCapturesAttachExactSessionsAndRequireSelectionForOtherMedia()
    {
        var game = Guid.NewGuid(); var profile = Guid.NewGuid().ToString("D");
        var provider = new FakeHost(1) { ItemFactory = i => Item(i) with { SavedId = game.ToString("D") } };
        var interval = new CaptureProcessInterval(new string('b',64),100,200);
        var captures = Enumerable.Range(1,2).Select(i => new CaptureLibraryItem(i.ToString("x32"),
            new(i.ToString("x32"),"image/png",100,100,120,0,long.MaxValue) { SourceProcess = i==1 ? interval : null },500)
            { Revision = i }).ToArray();
        var links = new System.Collections.Concurrent.ConcurrentDictionary<string,CaptureLibraryAssociation>();
        var detached = new System.Collections.Concurrent.ConcurrentDictionary<string,CaptureLibraryAssociation>();
        var groupRevision = 1;
        string? removedOperation = null;
        provider.ConfigureServices = builder => builder
            .WithHandler(WidgetPermissionCapabilities.Check,(request,_) => ValueTask.FromResult(new WidgetPermissionStatuses(
                request.CapabilityIds.Select(id => new WidgetPermissionStatus(id,WidgetPermissionState.Granted)).ToArray())))
            .WithHandler(WidgetCaptureLibraryCapabilities.Runs,(_,_) =>
                ValueTask.FromException<CaptureLibraryRunsPage>(new WidgetCapabilityException("unsupported_operation", "Older broker")))
            .WithHandler(WidgetCaptureLibraryCapabilities.Query,(request,_) =>
            {
                var items = captures.Where(item => (request.Query.SourceIntervals is null || item.Media.SourceProcess == interval) &&
                    request.Query.ExcludeReference != links.ContainsKey(item.Id) &&
                    (!request.Query.ExcludeDetachedReference || !detached.ContainsKey(item.Id)))
                    .Select(item => item with { ContextCount = links.ContainsKey(item.Id) ? 1 : 0 }).ToArray();
                return ValueTask.FromResult(new CaptureLibraryQueryPage(items,0,items.Length,items.Length*120,groupRevision.ToString()));
            })
            .WithHandler(WidgetCaptureLibraryCapabilities.EnsureSubject,(request,_) =>
            {
                var item = captures.Single(c => c.Id == request.CaptureId);
                var subject = request.Subject;
                Assert.AreEqual(item.Revision,request.ContentRevision);
                Assert.AreEqual(CaptureRelatedIdentity.Game(profile,game),subject.Id);
                if (detached.TryGetValue(item.Id, out var removed))
                {
                    if (!request.RestoreDetached) return ValueTask.FromResult(new CaptureLibrarySubjectResult(CaptureContextSaveOutcome.Detached));
                    Assert.IsTrue(detached.TryRemove(item.Id, out var _));
                    links[item.Id] = removed with { LinkRevision = removed.LinkRevision + 1 };
                }
                links.GetOrAdd(item.Id, _ => new(Guid.NewGuid().ToString("N"),item.Id,
                    new("widgetrail.samples.playnite-library","example.publisher","example.instance"), [subject],null,null,500));
                return ValueTask.FromResult(new CaptureLibrarySubjectResult(CaptureContextSaveOutcome.Saved));
            })
            .WithHandler(WidgetCaptureLibraryCapabilities.ChangeDestination,(request,token) =>
            {
                var source = request.Undo ? detached : links;
                if (request.Undo) Assert.AreEqual(removedOperation, request.OperationId);
                else { Assert.AreEqual(request.ExpectedRevision, groupRevision.ToString()); removedOperation = request.OperationId; }
                Assert.IsTrue(source.TryRemove(request.CaptureId, out var previous));
                var changed = previous with { LinkRevision = previous.LinkRevision + 1 };
                (request.Undo ? links : detached)[request.CaptureId] = changed;
                groupRevision++;
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            });
        var widget = Create(provider,out TestApplicationService app);
        app.CaptureIntegrationHandler = _ => ValueTask.FromResult<PlayniteCaptureIntegration?>(new(profile,[],[],"binding") { Sessions = [interval] });
        const string prefix = "playnite-library.related-captures";
        try
        {
            await Interactive(widget); await Ready(widget,provider);
            var tile = PageNodes(widget,Snapshot(widget,5000).Root).First(node => node.ActionId==PlayniteLibraryActions.DetailsOpen);
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen,tile.Id));
            await widget.OnActionAsync(new(DetailAction+"tab.captures","tab"));
            await WaitFor(() => links.Count==1 && Nodes(Snapshot(widget,5001).Root).Any(n=>n.Id==prefix+".link"),"automatic capture link");
            await widget.OnActionAsync(new(prefix+".link",prefix+".link"));
            await WaitFor(() => Nodes(Snapshot(widget,5002).Root).Any(n=>n.Id==prefix+".picker.item."+captures[1].Id),"manual capture choices");
            var picker = Snapshot(widget,5003);
            Assert.AreEqual(0,ViewSnapshotValidator.Validate(picker).Count,string.Join(", ",ViewSnapshotValidator.Validate(picker)));
            Assert.AreEqual(ViewNodeKind.ModalLayer, picker.Root.Kind);
            Assert.AreEqual("playnite-library.presentation", picker.Root.Children[0].Id,
                "Replacing Details with its picker must keep the originating list mounted.");
            var option = Nodes(picker.Root).Single(n=>n.Id==prefix+".picker.item."+captures[1].Id);
            await widget.OnActionAsync(new(option.ActionId!,option.Id));
            await WaitFor(() => links.Count==2 && Nodes(Snapshot(widget,5004).Root).Any(n=>n.Text=="Capture linked."),"explicit capture link");
            var card = Nodes(Snapshot(widget,5005).Root).Single(n=>n.Id==prefix+".item."+captures[0].Id);
            await widget.OnActionAsync(new(card.ActionId!,card.Id));
            var viewer = Snapshot(widget,5006);
            Assert.AreEqual(0,ViewSnapshotValidator.Validate(viewer).Count,string.Join(", ",ViewSnapshotValidator.Validate(viewer)));
            Assert.AreEqual(2,Nodes(viewer.Root).Single(n=>n.Id==prefix+".player").MediaPlayer!.Gallery!.Count);
            await widget.OnActionAsync(new(prefix+".viewer.back",prefix+".viewer"));
            Assert.IsTrue(Nodes(Snapshot(widget,5007).Root).Any(n=>n.Id==card.Id));
            var remove = card.ContextActions.Single(c => c.Label == "Unlink from this game");
            await widget.OnActionAsync(new(remove.ActionId, card.Id));
            await WaitFor(() => !links.ContainsKey(captures[0].Id) && Nodes(Snapshot(widget,5008).Root).Any(n => n.Id == prefix + ".undo"), "unlink");
            var undo = Nodes(Snapshot(widget,5009).Root).Single(n => n.Id == prefix + ".undo");
            Assert.AreEqual(undo.Id, Snapshot(widget,5009).FocusGroupEntryRequest?.TargetId);
            await widget.OnActionAsync(new(undo.ActionId!, undo.Id));
            await WaitFor(() => links.Count == 2 && Nodes(Snapshot(widget,5010).Root).Any(n => n.Text == "Capture linked again."), "undo");
            await WaitFor(() => Snapshot(widget,5011).FocusGroupEntryRequest?.TargetId == card.Id, "focus on restored capture");
            await widget.OnActionAsync(new(remove.ActionId,card.Id));
            Assert.AreEqual(2,links.Count,"An action from before Undo cannot remove the restored occurrence.");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }
}
