using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class ContextTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(string code, Func<Task> action)
    {
        try { await action(); } catch (BrokerException error) when (error.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    private static CaptureSaveContext Context(string key, string eventId, long? time = 1000) => new(key,
        [new(CaptureContextReferenceRole.Subject, "example.subject", "subject-1", "Example subject"),
         new(CaptureContextReferenceRole.Event, "example.event", eventId, "Example event")], time);

    internal static async Task ExistingCapturesBySession()
    {
        using var fixture = new Fixture();
        var key = CaptureProcessInterval.InstanceId(22, 1000);
        var process = new CaptureProcessInterval(key, 300, 400);
        Task<CaptureLibraryItem> Save(CaptureProcessInterval? interval) => fixture.Store.SaveAsync(fixture.Source with
            { Attachment = fixture.Source.Attachment with { SourceProcess = interval } }, Fixture.Identity, default);
        var match = await Save(process);
        var another = await Save(process with { Instance = CaptureProcessInterval.InstanceId(22, 1001) });
        await Save(process with { From = 299 });
        await Save(process with { Through = 401 });
        await Save(null);
        var filter = new CaptureLibraryQuery { SourceIntervals = [process], AvailableOnly = true };
        var result = await fixture.Store.QueryAsync(new(filter),default);
        Check(result.TotalCount == 1 && result.Items.Single().Id == match.Id, "Exact process and the entire capture interval must match.");
        var editor = Fixture.Identity with { PackageId = "example.editor" };
        var context = Context("capture-project", "chapter-1");
        var request = new CaptureLibraryAttachContextRequest(match.Id,match.Revision,context);
        var attached = await fixture.Store.AttachSavedContextAsync(request,editor,default);
        var receiptBytes = new FileInfo(Directory.EnumerateFiles(fixture.Library,"association-*.json").Single()).Length;
        var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(process).Length;
        Console.WriteLine($"Fixture bytes: {evidenceBytes} for process interval JSON, {receiptBytes} for one two-reference association receipt.");
        Check(attached.Association.Origin.PackageId == editor.PackageId && attached.Item!.Origin!.PackageId == Fixture.Identity.PackageId,
            "Adding a project reference must preserve media ownership");
        Check((await fixture.Store.AttachSavedContextAsync(request,editor,default)).Outcome == CaptureContextSaveOutcome.AlreadySaved,
            "Retry duplicated an association.");
        await Reject("capture_library_context_conflict", () => fixture.Store.AttachSavedContextAsync(request with { CaptureId=another.Id,ContentRevision=another.Revision },editor,default));
        await Reject("capture_library_metadata_conflict", () => fixture.Store.AttachSavedContextAsync(request with {ContentRevision=match.Revision+1},editor,default));
        var unmatched = filter with { OriginPackageId=editor.PackageId,Reference=new("example.event","chapter-1"),ExcludeReference=true };
        Check((await fixture.Store.QueryAsync(new(unmatched),default)).TotalCount==0 &&
            (await fixture.Store.QueryAsync(new(unmatched with {OriginPackageId=Fixture.Identity.PackageId}),default)).TotalCount==1,
            "Reconciliation must exclude only the specified provider reference.");
    }

    internal static async Task UniqueSubjectLinks()
    {
        using var fixture = new Fixture();
        var original = await fixture.Store.SaveAsync(fixture.Source, Fixture.Identity, default);
        var old = Fixture.Identity with { PackageId = "example.games" };
        var current = old with { PublisherId = "new.publisher", InstanceId = "new.instance" };
        var game = new CaptureContextReference(CaptureContextReferenceRole.Subject, "library.game", "profile/game", "Original title")
            { HasRelatedAction = true };
        var achievement = new CaptureContextReference(CaptureContextReferenceRole.Event, "library.unlock", "achievement");
        await fixture.Store.AttachSavedContextAsync(new(original.Id, original.Revision, new("earned", [game, achievement], 1000)), old, default);
        var copy = await fixture.Store.SaveCopyAsync(new(original.Id, original.Revision, original.MetadataRevision), Fixture.Identity, default);
        var membership = new CaptureLibraryEnsureSubjectRequest(copy.Id, copy.Revision, "link-copy", game with { Label = "New title" }, true);
        var before = Directory.GetFiles(fixture.Library, "association-*.json").ToDictionary(p => p, File.ReadAllText);
        var result = await fixture.Store.EnsureSubjectAsync(membership, current, default);
        Check(result.Outcome == CaptureContextSaveOutcome.AlreadySaved &&
            Directory.GetFiles(fixture.Library, "association-*.json").Length == before.Count && before.All(pair => File.ReadAllText(pair.Key) == pair.Value),
            "Ensuring an inherited compound link must leave the original and copied receipt bytes unchanged");
        await Reject("capture_library_metadata_conflict", () => fixture.Store.EnsureSubjectAsync(membership with { ContentRevision = copy.Revision + 1 }, current, default));
        var other = membership with { IdempotencyKey = "second", Subject = game with { Id = "profile/other" } };
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => fixture.Store.EnsureSubjectAsync(
            other with { IdempotencyKey = "second-" + i }, i % 2 == 0 ? old : current, default))));
        Check(concurrent.Count(row => row.Outcome == CaptureContextSaveOutcome.Saved) == 1 &&
            concurrent.Count(row => row.Outcome == CaptureContextSaveOutcome.AlreadySaved) == 7,
            "Concurrent requests from two installations must only write one link to the same destination");
        Check((await fixture.Store.EnsureSubjectAsync(membership, Fixture.Identity, default)).Outcome == CaptureContextSaveOutcome.Saved,
            "A different package owns an independent destination namespace");
        var own = new CaptureLibraryEnsureSubjectRequest(copy.Id, copy.Revision, "standalone", game with { Id = "profile/standalone" });
        await fixture.Store.EnsureSubjectAsync(own, current, default);
        var link = (await fixture.Store.FindOwnedLinksAsync(new([copy.Id], new(own.Subject.Kind, own.Subject.Id)), current, default)).Items.Single();
        await fixture.Store.SetContextLinkedAsync(new(copy.Id, copy.Revision, link.Id, link.LinkRevision, false), current, default);
        fixture.Store.Dispose();
        File.Delete(Path.Combine(fixture.Library, "library.sqlite"));
        using var reopened = new CaptureLibraryStore(fixture.Library, fixture.Exports);
        Check((await reopened.EnsureSubjectAsync(membership, current, default)).Outcome == CaptureContextSaveOutcome.AlreadySaved &&
            (await reopened.EnsureSubjectAsync(own, current, default)).Outcome == CaptureContextSaveOutcome.Detached &&
            (await reopened.EnsureSubjectAsync(own with { RestoreDetached = true }, current, default)).Outcome == CaptureContextSaveOutcome.Saved,
            "Rebuilding the index must preserve inherited membership, automatic dismissal and explicit relink");
        Check((await reopened.GetContextsAsync(new(original.Id), default)).Items.Single().References.Count == 2,
            "The original must retain its subject and event");
        // Simulate copies made before the new write operation, with repeated references interleaved across receipt pages.
        for (var i = 0; i < 9; i++)
            await reopened.AttachSavedContextAsync(new(copy.Id, copy.Revision, new("legacy-" + i,
                [game with { Label = "Renamed game", HasRelatedAction = false }, achievement with { Id = "event-" + i }])), current, default);
        var destinations = new List<CaptureLibraryDestination>();
        var first = await reopened.GetDestinationsAsync(new(copy.Id, 0, 4), default);
        for (var offset = 0; offset < first.TotalCount; offset += 4)
        {
            var page = await reopened.GetDestinationsAsync(new(copy.Id, offset, 4, first.Revision), default);
            destinations.AddRange(page.Items);
        }
        Check(destinations.Count == first.TotalCount && destinations.DistinctBy(d => (d.PackageId, d.Kind, d.Id)).Count() == destinations.Count &&
            destinations.Single(d => d.PackageId == current.PackageId && d.Kind == game.Kind && d.Id == game.Id).HasRelatedAction &&
            destinations.Count(d => d.Kind == achievement.Kind) == 10 && destinations.Count(d => d.Kind == game.Kind && d.Id == game.Id) == 2,
            "Destinations must group across receipt pages and installations while retaining actionable and display-only references in each package namespace");
        var beyond = await reopened.GetDestinationsAsync(new(copy.Id, first.TotalCount + 1, 4, first.Revision), default);
        Check(beyond.Items.Count == 0 && beyond.TotalCount == first.TotalCount, "An empty trailing page lost the destination count");
    }

    internal static async Task GroupedDestinationUndo()
    {
        using var f = new Fixture();
        var original = await f.Store.SaveAsync(f.Source, Fixture.Identity, default);
        var old = Fixture.Identity with { PackageId = "widgetrail.samples.playnite-library" };
        var installed = old with { PublisherId = "new.publisher", InstanceId = "new.instance" };
        var game = new CaptureContextReference(CaptureContextReferenceRole.Subject, "playnite.game", "profile/silksong", "Hollow Knight: Silksong") { HasRelatedAction = true };
        var award = new CaptureContextReference(CaptureContextReferenceRole.Event, "playnite.achievement", "profile/unlocked", "Liberated") { HasRelatedAction = true };
        await f.Store.AttachSavedContextAsync(new(original.Id, original.Revision, new("old", [game])), old, default);
        await f.Store.AttachSavedContextAsync(new(original.Id, original.Revision, new("compound", [game, award], 1000)), installed, default);
        var copy = await f.Store.SaveCopyAsync(new(original.Id, original.Revision, original.MetadataRevision), Fixture.Identity, default);
        var current = new CaptureLibraryEnsureSubjectRequest(copy.Id, copy.Revision, "manual", game);
        var own = await f.Store.AttachSavedContextAsync(new(copy.Id, copy.Revision, new("manual", [game])), installed, default);
        var extra = await f.Store.AttachSavedContextAsync(new(copy.Id, copy.Revision, new("independent", [game])), Fixture.Identity, default);
        var reviewed = await f.Store.GetDestinationsAsync(new(copy.Id), default);
        var change = new CaptureDestinationChange(copy.Id, copy.Revision, old.PackageId, game.Kind, game.Id,
            Guid.NewGuid().ToString("N"), reviewed.Revision);
        var approved = false;
        var reviews = 0;
        Task<bool> Decide(BrokerWidgetIdentity actor, string capture, string destination, CancellationToken token)
        {
            Check(actor == installed && capture.Length > 0 && destination == game.Label, "Trusted review must describe the actual stored destination");
            reviews++;
            return Task.FromResult(approved);
        }
        Check(!await f.Store.RequestDestinationChangeAsync(change, installed, Decide, default) &&
            (await f.Store.GetContextsAsync(new(copy.Id), default)).TotalCount == 4 && reviews == 1, "Cancelling cross-owner unlink changed links");
        approved = true;
        // Abort after user-state writes and during the derived projection. Both DBs must roll back.
        void Sql(string statement)
        {
            using var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = Path.Combine(f.Library, "library.sqlite"), Pooling = false }.ToString());
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = statement; cmd.ExecuteNonQuery();
        }
        Sql("CREATE TRIGGER interrupt_group BEFORE UPDATE ON capture_associations BEGIN SELECT RAISE(ABORT,'fixture interruption'); END");
        try { await f.Store.ApplyReviewedDestinationChangeAsync(change, installed, default); throw new Exception("Expected interrupted write"); }
        catch (Microsoft.Data.Sqlite.SqliteException e) when (e.SqliteErrorCode == 19) { }
        Sql("DROP TRIGGER interrupt_group");
        var afterFailure = await f.Store.GetContextsAsync(new(copy.Id), default);
        Check(afterFailure.TotalCount == 4 && afterFailure.Revision == reviewed.Revision, "Interrupted group edit leaked a partial removal or revision");
        Check(await f.Store.RequestDestinationChangeAsync(change, installed, Decide, default) &&
            await f.Store.RequestDestinationChangeAsync(change, installed, Decide, default) && reviews == 2,
            "A committed retry should acknowledge only the same actor's exact reviewed change");
        var hidden = await f.Store.GetContextsAsync(new(copy.Id), default);
        Check(hidden.TotalCount == 2 && hidden.Items.Any(a => a.Id == extra.Association.Id) &&
            hidden.Items.Single(a => a.Origin.PackageId == old.PackageId).References.Single() == award,
            "Foreign package, achievement, or whole-receipt visibility was incorrect after grouped removal");
        Check((await f.Store.GetContextsAsync(new(original.Id), default)).TotalCount == 2, "Changing copied links touched their original");
        await Reject("capture_library_context_conflict", () => f.Store.SetContextLinkedAsync(
            new(copy.Id, copy.Revision, own.Association.Id, 0, true), installed, default));
        var descendant = await f.Store.SaveCopyAsync(new(copy.Id, copy.Revision, copy.MetadataRevision), Fixture.Identity, default);
        Check((await f.Store.GetContextsAsync(new(descendant.Id), default)).Items.Single(a => a.Origin.PackageId == old.PackageId).References.Single() == award,
            "Copying after unlink reintroduced a removed destination");
        f.Store.Dispose(); File.Delete(Path.Combine(f.Library, "library.sqlite"));
        using var rebuilt = new CaptureLibraryStore(f.Library, f.Exports);
        Check((await rebuilt.GetContextsAsync(new(copy.Id), default)).TotalCount == 2 &&
            (await rebuilt.EnsureSubjectAsync(current with { IdempotencyKey = "new-key" }, old, default)).Outcome == CaptureContextSaveOutcome.Detached,
            "Rebuild or an automatic match from a second installation resurrected the removed subject");
        await Reject("capture_library_context_conflict", () => rebuilt.ApplyReviewedDestinationChangeAsync(change with { Undo = true }, old, default));
        await rebuilt.SetContextLinkedAsync(new(copy.Id, copy.Revision, extra.Association.Id, 0, false), Fixture.Identity, default);
        await rebuilt.ApplyReviewedDestinationChangeAsync(change with { Undo = true }, installed, default);
        await rebuilt.ApplyReviewedDestinationChangeAsync(change with { Undo = true }, installed, default);
        var restored = await rebuilt.GetContextsAsync(new(copy.Id), default);
        Check(restored.TotalCount == 3 && restored.Items.All(a => a.Origin.PackageId == old.PackageId && a.References.Contains(game)) &&
            restored.Items.Any(a => a.References.Contains(award)), "Undo changed an unrelated dismissal or lost one of the original three links");
        await Reject("capture_library_context_conflict", () => rebuilt.ApplyReviewedDestinationChangeAsync(change, installed, default));

        var libraryManager = new BrokerWidgetIdentity("widgetrail.firstparty.capture-library", "widgetrail.firstparty", "capture-library.default");
        var originPage = await rebuilt.GetDestinationsAsync(new(original.Id), default);
        var fromLibrary = change with { CaptureId = original.Id, ContentRevision = original.Revision,
            OperationId = Guid.NewGuid().ToString("N"), ExpectedRevision = originPage.Revision };
        var impostor = libraryManager with { PublisherId = "unsigned.example" };
        Check(!await rebuilt.RequestDestinationChangeAsync(fromLibrary, impostor, (_, _, _, _) => Task.FromResult(false), default),
            "Matching only the manager package ID must not bypass foreign-link review");
        Task<bool> UnexpectedReview(BrokerWidgetIdentity _, string capture, string destination, CancellationToken token) =>
            throw new Exception("Capture Library direct unlink and Undo must not open a second dialog");
        Check(await rebuilt.RequestDestinationChangeAsync(fromLibrary, libraryManager, UnexpectedReview, default) &&
            await rebuilt.RequestDestinationChangeAsync(fromLibrary, libraryManager, UnexpectedReview, default) &&
            !(await rebuilt.GetDestinationsAsync(new(original.Id), default)).Items.Any(d => d.Id == game.Id),
            "Capture Library removes all matching game references and accepts a retry");
        Check(await rebuilt.RequestDestinationChangeAsync(fromLibrary with { Undo = true }, libraryManager, UnexpectedReview, default) &&
            (await rebuilt.GetContextsAsync(new(original.Id), default)).Items.Count(a => a.References.Contains(game)) == 2,
            "Capture Library Undo restores the exact contributing references");
    }

    internal static async Task ReversibleLinks()
    {
        using var fixture = new Fixture();
        var item = await fixture.Store.SaveAsync(fixture.Source, Fixture.Identity, default);
        var original = File.ReadAllBytes(fixture.MediaPath(item.Id));
        var editor = Fixture.Identity with { PackageId = "example.editor" };
        var subject = new CaptureContextReference(CaptureContextReferenceRole.Subject, "library.game", "game-one", "Example game");
        var context = new CaptureSaveContext("one-link", [subject]);
        var attach = new CaptureLibraryAttachContextRequest(item.Id, item.Revision, context);
        var plain = new CaptureLibraryQuery();
        var byLabel = new CaptureLibraryQuery { Search = "Example game" };
        var byOrigin = new CaptureLibraryQuery { OriginPackageId = editor.PackageId };
        Task<CaptureLibraryContinuityPage> Read(CaptureLibraryQuery filter) => fixture.Store.QueryContinuityAsync(new(filter), default);
        var plainBefore = await Read(plain);
        var labelBefore = await Read(byLabel);
        var originBefore = await Read(byOrigin);
        var saved = await fixture.Store.AttachSavedContextAsync(attach, editor, default);
        var plainAfter = await Read(plain);
        Check(plainBefore.OrderingRevision == plainAfter.OrderingRevision && plainBefore.Page.Revision != plainAfter.Page.Revision &&
            plainAfter.Page.Items.Single().ContextCount == 1, "Linking cleared selection despite an unchanged gallery order");
        Check(labelBefore.OrderingRevision != (await Read(byLabel)).OrderingRevision && originBefore.OrderingRevision != (await Read(byOrigin)).OrderingRevision,
            "A reference-sensitive query kept an obsolete position proof");
        var other = await fixture.Store.AttachSavedContextAsync(attach, Fixture.Identity, default);
        var lookup = new CaptureLibraryOwnedLinksRequest([item.Id], new(subject.Kind, subject.Id));
        Check((await fixture.Store.FindOwnedLinksAsync(lookup, editor, default)).Items.Single().Id == saved.Association.Id &&
            (await fixture.Store.FindOwnedLinksAsync(lookup, Fixture.Identity, default)).Items.Single().Id == other.Association.Id,
            "Listing receipt keys must resolve the authenticated caller's own links");
        var query = new CaptureLibraryQuery { OriginPackageId = editor.PackageId, Reference = new(subject.Kind, subject.Id) };
        var detach = new CaptureLibrarySetContextLinkedRequest(item.Id, item.Revision, saved.Association.Id, 0, false);
        await Reject("capture_library_context_conflict", () => fixture.Store.SetContextLinkedAsync(detach, Fixture.Identity, default));
        await Reject("capture_library_metadata_conflict", () => fixture.Store.SetContextLinkedAsync(detach with { ContentRevision = item.Revision + 1 }, editor, default));
        var removed = await fixture.Store.SetContextLinkedAsync(detach, editor, default);
        Check((await fixture.Store.FindOwnedLinksAsync(lookup, editor, default)).Items.Count == 0, "Detached links cannot offer an active menu action");
        Check(removed.Outcome == CaptureContextSaveOutcome.Detached && removed.Association.LinkRevision == 1, "Expected a new unlink revision");
        Check((await fixture.Store.SetContextLinkedAsync(detach, editor, default)).Association.LinkRevision == 1, "An unlink retry must be idempotent");
        Check((await fixture.Store.GetContextsAsync(new(item.Id), default)).Items.Single().Id == other.Association.Id,
            "Unlink changed an unrelated reference");
        fixture.Store.Dispose();
        File.Delete(Path.Combine(fixture.Library, "library.sqlite"));
        using var reopened = new CaptureLibraryStore(fixture.Library, fixture.Exports);
        Check((await reopened.AttachSavedContextAsync(attach, editor, default)).Outcome == CaptureContextSaveOutcome.Detached,
            "Rebuilding the index resurrected a dismissed link");
        Check((await reopened.QueryAsync(new(query), default)).TotalCount == 0 &&
            (await reopened.QueryAsync(new(query with { ExcludeReference = true }), default)).TotalCount == 1 &&
            (await reopened.QueryAsync(new(query with { ExcludeReference = true, ExcludeDetachedReference = true }), default)).TotalCount == 0,
            "A detached item must be selectable manually and absent from linked results and automatic reconciliation");
        var undo = detach with { ExpectedLinkRevision = removed.Association.LinkRevision, Linked = true };
        var restored = await reopened.SetContextLinkedAsync(undo, editor, default);
        Check(restored.Association.Id == saved.Association.Id && restored.Association.LinkRevision == 2 &&
            restored.Item?.ContextCount == 2, "Undo must restore the original association alongside unrelated links");
        Check((await reopened.SetContextLinkedAsync(undo, editor, default)).Association.LinkRevision == 2, "Undo retry changed the revision");
        await Reject("capture_library_context_conflict", () => reopened.SetContextLinkedAsync(detach, editor, default));
        Check((await reopened.QueryAsync(new(query), default)).TotalCount == 1 && original.SequenceEqual(File.ReadAllBytes(fixture.MediaPath(item.Id))),
            "A stale unlink changed the restored link or media");
        var copy = await reopened.SaveCopyAsync(new(item.Id, item.Revision, item.MetadataRevision), Fixture.Identity, default);
        var copiedLinks = await reopened.FindOwnedLinksAsync(new([copy.Id], query.Reference!), editor, default);
        var copiedLink = copiedLinks.Items.Single();
        Check(copiedLink.Id != saved.Association.Id, "A copied capture must have its own link identity");
        await reopened.SetContextLinkedAsync(new(copy.Id, copy.Revision, copiedLink.Id, copiedLink.LinkRevision, false), editor, default);
        Check((await reopened.QueryAsync(new(query), default)).Items.Single().Id == item.Id,
            "Removing an inherited link from the copy affected the original");
        var compound = await reopened.AttachSavedContextAsync(attach with { Context = new("event-and-game",
            [subject, new(CaptureContextReferenceRole.Event, "game.event", "achievement")]) }, editor, default);
        Check((await reopened.FindOwnedLinksAsync(lookup, editor, default)).Items.Count == 0, "An ambiguous reference offered unlink");
        await Reject("capture_library_context_conflict", () => reopened.SetContextLinkedAsync(
            new(item.Id, item.Revision, compound.Association.Id, compound.Association.LinkRevision, false), editor, default));
    }

    internal static async Task GroupedRuns()
    {
        using var fixture = new Fixture();
        var first = CaptureProcessInterval.InstanceId(22, 1000);
        var second = CaptureProcessInterval.InstanceId(22, 2000);
        Task<CaptureLibraryItem> Save(CaptureProcessInterval? interval, string window, string executable) =>
            fixture.Store.SaveAsync(fixture.Source with { Attachment = fixture.Source.Attachment with
                { SourceProcess = interval, SourceApplication = new("Game", window) { ProcessName = executable } } }, Fixture.Identity, default);
        var a = await Save(new(first, 20, 30), "Silksong", "player.exe");
        var b = await Save(new(first, 40, 50), "Credits", "player.exe");
        var c = await Save(new(second, 55, 60), "Game", "preferred.exe");
        var historical = await Save(null, "Legacy", "player.exe");
        await fixture.Store.SaveCopyAsync(new(historical.Id, historical.Revision, historical.MetadataRevision), Fixture.Identity, default);
        var request = new CaptureLibraryRunsRequest(new() { AvailableOnly = true }, Count: 1, SourceTerms: ["silksong"], PreferredProcessName: "PREFERRED.exe");
        var top = await fixture.Store.RunsAsync(request, default);
        Check(top.HasMore && top.Items.Single() is { MatchesPreferredProcess: true, Screenshots: 1 } && top.Items[0].Latest.Id == c.Id,
            "A preferred executable ranks a complete run first");
        var next = await fixture.Store.RunsAsync(request with { Offset = 1, Revision = top.Revision }, default);
        var run = next.Items.Single();
        Check(run.Latest.Id == b.Id && run.MatchesSource && run is { Screenshots: 2, Videos: 0, From: 20, Through: 50 } &&
            run.ProcessInstance == first, "A title match on an earlier capture must rank its whole run, separately from another launch");
        var members = await fixture.Store.QueryAsync(new(new() { SourceIntervals = [new(first, run.From, run.Through)] }), default);
        Check(members.Items.Select(value => value.Id).ToHashSet().SetEquals([a.Id,b.Id]), "The displayed run did not expand to its members");
        var legacy = await fixture.Store.RunsAsync(request with { Offset = 2, Count = 2, Revision = top.Revision }, default);
        Check(!legacy.HasMore && legacy.Items.Count == 2 && legacy.Items.All(value => value.ProcessInstance is null && value.Screenshots == 1) &&
            legacy.Items[0].Latest.Id != legacy.Items[1].Latest.Id, "Captures without process evidence were merged");
        await Save(new(second, 62, 70), "Game", "preferred.exe");
        await Reject("capture_library_query_changed", () => fixture.Store.RunsAsync(request with { Revision = top.Revision }, default));
    }

    internal static async Task AssociationsAndQueries()
    {
        using var f = new Fixture();
        var source = f.Source with { CapturedAtUnixMilliseconds = 2000 };
        var first = await f.Store.SaveWithContextAsync(source, Fixture.Identity, Context("artifact-1", "event-1"), default);
        Check(first.Outcome == CaptureContextSaveOutcome.Saved && first.Item is { ContextCount: 1 } && first.Association.Origin.PackageId == Fixture.Identity.PackageId,
            "Host origin and context count are stamped on a new save");
        Check(first.Association.EventAtUnixMilliseconds == 1000 && first.Association.CapturedAtUnixMilliseconds == 2000 && first.Association.RecordedAtUnixMilliseconds > 2000,
            "Event time, actual capture time and recording time remain distinct");
        var repeated = await f.Store.SaveWithContextAsync(source, Fixture.Identity, Context("artifact-1", "event-1") with
            { References = [new(CaptureContextReferenceRole.Event, "example.event", "event-1", "Translated label"), new(CaptureContextReferenceRole.Subject, "example.subject", "subject-1")] }, default);
        Check(repeated.Outcome == CaptureContextSaveOutcome.AlreadySaved && repeated.Association.Id == first.Association.Id && repeated.Item?.ContextCount == 1,
            "Matching semantic retry preserves one immutable receipt independent of label/order changes");
        await Reject("capture_library_context_conflict", () => f.Store.SaveWithContextAsync(source, Fixture.Identity, Context("artifact-1", "other-event"), default));
        for (var index = 2; index <= 22; index++)
        {
            var next = await f.Store.SaveWithContextAsync(source, Fixture.Identity, Context("artifact-" + index, "event-" + index), default);
            Check(next.Item?.Id == first.Item!.Id, "Identical media must not be duplicated for separate events");
        }
        Check(Directory.EnumerateFiles(f.Exports).Count() == 1, "Association saves created duplicate originals");
        var page = await f.Store.GetContextsAsync(new(first.Item!.Id), default);
        var rest = await f.Store.GetContextsAsync(new(first.Item.Id, 16, 16, page.Revision), default);
        Check(page.TotalCount == 22 && page.Items.Count == 16 && rest.Items.Count == 6 && !page.Items.Select(item => item.Id).Intersect(rest.Items.Select(item => item.Id)).Any(),
            "Context pagination is bounded and stable");
        var other = Fixture.Identity with { PackageId = "other.widget", InstanceId = "other.instance" };
        var foreign = await f.Store.SaveWithContextAsync(source, other, Context("artifact-1", "foreign-event"), default);
        Check(foreign.Association.Id != first.Association.Id && foreign.Item?.Id == first.Item.Id && foreign.Item.ContextCount == 23,
            "Idempotency is authenticated-origin scoped while media dedup stays shared");
        var byLabel = await f.Store.QueryAsync(new(new() { Search = "Example event" }), default);
        Check(byLabel.TotalCount == 1, "Generic context labels participate in library metadata search");
        var reconciliations = f.Store.Reconciliations;
        await f.Store.AttachContextAsync(first.Item.Id, Fixture.Identity, Context("attached-artifact", "attached-event"), default);
        await Task.Delay(150);
        var attached = await f.Store.QueryAsync(new(new()), default);
        Check(attached.Items.Single().ContextCount == 24 && f.Store.Reconciliations == reconciliations,
            "Trusted context attachment must not copy media or rescan all private sidecars");
        var match = await f.Store.QueryAsync(new(new() { OriginPackageId = Fixture.Identity.PackageId, Reference = new("example.event", "event-1", CaptureContextReferenceRole.Event) }), default);
        Check(match.TotalCount == 1 && match.Items.Single().ContextCount == 24, "Origin/reference query returns the associated capture");
        var crossed = await f.Store.QueryAsync(new(new() { OriginPackageId = Fixture.Identity.PackageId, Reference = new("example.event", "foreign-event") }), default);
        Check(crossed.TotalCount == 0, "Origin and reference must match the same association");
        var wrongRole = await f.Store.QueryAsync(new(new() { OriginPackageId = Fixture.Identity.PackageId, Reference = new("example.event", "event-1", CaptureContextReferenceRole.Subject) }), default);
        Check(wrongRole.TotalCount == 0, "Opaque role filtering must not infer provider semantics");
        await Reject("invalid_payload", () => f.Store.QueryAsync(new(new() { Reference = new("example.event", "event-1") }), default));
        await Reject("capture_library_query_changed", () => f.Store.GetContextsAsync(new(first.Item.Id, 0, 16, page.Revision), default));
    }

    internal static async Task RebuildRecoveryAndRetirement()
    {
        using var f = new Fixture();
        var context = Context("retained-artifact", "event");
        var saved = await f.Store.SaveWithContextAsync(f.Source, Fixture.Identity, context, default);
        var id = saved.Item!.Id; var original = f.MediaPath(id); var bytes = File.ReadAllBytes(original);
        Check(saved.Association.CapturedAtUnixMilliseconds is null, "Unknown capture time must not be invented from event/save times");
        f.Store.Dispose();
        var receipt = Directory.EnumerateFiles(f.Library, "association-*.json").Single();
        File.Move(receipt, receipt + ".pending"); // Simulate crash after original metadata publication.
        File.Delete(Path.Combine(f.Library, "library.sqlite"));
        using (var rebuilt = new CaptureLibraryStore(f.Library, f.Exports))
        {
            var page = await rebuilt.GetContextsAsync(new(id), default);
            Check(page.TotalCount == 1 && File.Exists(receipt) && !File.Exists(receipt + ".pending"), "Pending receipt recovers once after SQLite rebuild");
            Check(bytes.SequenceEqual(File.ReadAllBytes(original)), "Receipt recovery changed original bytes");
            var found = await rebuilt.FindSavedContextAsync(Fixture.Identity, context, default);
            Check(found is { Outcome: CaptureContextSaveOutcome.AlreadySaved } && found.Association.Id == saved.Association.Id,
                "Durable idempotency survives index loss");
            await rebuilt.DeleteAsync(id, default);
            var retired = await rebuilt.SaveWithContextAsync(f.Source, Fixture.Identity, context, default);
            Check(retired is { Outcome: CaptureContextSaveOutcome.Unavailable, Item: null } && !File.Exists(original) &&
                (await rebuilt.QueryAsync(new(new()), default)).TotalCount == 0, "Deleted original must not be automatically recreated by event replay");
        }
        File.WriteAllText(Path.Combine(f.Library, "library.sqlite"), "corrupt disposable fixture index");
        using var repaired = new CaptureLibraryStore(f.Library, f.Exports);
        var afterRestart = await repaired.FindSavedContextAsync(Fixture.Identity, context, default);
        Check(afterRestart?.Outcome == CaptureContextSaveOutcome.Unavailable && !File.Exists(original), "Retired receipt survives corrupt-index repair without resurrection");
    }

    internal static async Task AuthorityAndBounds()
    {
        using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
        var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
        foreach (var permission in new[] { PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1 })
            await consent.SetDecisionAsync(Fixture.Identity, permission, ConsentDecision.Grant);
        var simulation = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: f.Store);
        await using var broker = new PlatformCapabilityBroker(Fixture.Identity,
            [PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive); long sequence = 0;
        Task<JsonElement> Call(string capability, string operation, object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence,
            Fixture.Identity, capability, operation, BrokerJson.ToElement(payload)));
        var imported = BrokerJson.ParsePayload<CaptureAttachment>(await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.open", new CaptureLibraryOpenRequest(saved.Id)));
        var context = Context("broker-artifact", "broker-event");
        await Reject("invalid_payload", () => Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save-context",
            new { Attachment = imported, Context = new { context.IdempotencyKey, context.References, context.EventAtUnixMilliseconds, Origin = new { PackageId = "forged.widget" } } }));
        var result = BrokerJson.ParsePayload<CaptureLibraryContextSaveResult>(await Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save-context", new CaptureLibrarySaveWithContextRequest(imported, context)));
        Check(result.Association.Origin == new CaptureContextOrigin(Fixture.Identity.PackageId, Fixture.Identity.PublisherId, Fixture.Identity.InstanceId), "Origin must come from the authenticated broker");
        await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.release", new CaptureAttachmentRequest(imported.Id));
        var repeated = BrokerJson.ParsePayload<CaptureLibraryContextSaveResult>(await Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save-context", new CaptureLibrarySaveWithContextRequest(imported, context)));
        Check(repeated.Outcome == CaptureContextSaveOutcome.AlreadySaved, "Committed receipt retry must not require a still-live temporary attachment");
        var invalid = context with { References = [new(CaptureContextReferenceRole.Subject, "bad/kind", "id")] };
        await Reject("invalid_payload", () => f.Store.SaveWithContextAsync(f.Source, Fixture.Identity, invalid, default));
        var tooMany = context with { References = Enumerable.Range(0, 9).Select(i => new CaptureContextReference(CaptureContextReferenceRole.Related, "kind", i.ToString())).ToArray() };
        await Reject("invalid_payload", () => f.Store.SaveWithContextAsync(f.Source, Fixture.Identity, tooMany, default));
        await Reject("invalid_payload", () => f.Store.GetContextsAsync(new(saved.Id, Count: 17), default));
        broker.SetLifecycle(BrokerLifecycleState.Background);
        await Reject("lifecycle_denied", () => Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save-context", new CaptureLibrarySaveWithContextRequest(imported, context)));
        await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Deny); await broker.RefreshConsentAsync();
        await Reject("permission_denied", () => Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.contexts", new CaptureLibraryContextsRequest(saved.Id)));
    }
}
