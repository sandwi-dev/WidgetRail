using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class QueryTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static async Task Reject(string code, Func<Task> action)
    {
        try { await action(); }
        catch (BrokerException error) when (error.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    internal static async Task QueryAndMetadata()
    {
        using var f = new Fixture();
        var items = new List<CaptureLibraryItem>();
        for (var i = 0; i < 47; i++)
        {
            var source = f.Source with { Attachment = f.Source.Attachment with { SourceApplication = new("Game " + i, "Window") } };
            items.Add(await f.Store.SaveAsync(source, default));
        }
        var first = await f.Store.QueryAsync(new(new(), 0, 20), default);
        var second = await f.Store.QueryAsync(new(new(), 20, 20, first.Revision), default);
        Check(first.TotalCount == 47 && first.Items.Count == 20 && second.Items.Count == 20, "Query must cover the complete library in bounded ranges");
        Check(!first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)).Any(), "Stable ranges must not duplicate items");
        var located = await f.Store.FindAsync(new(new(), second.Items[7].Id, first.Revision), default);
        Check(located.Index == 27 && located.TotalCount == 47, "Find restores an anchor outside the first page");
        var item = items[0]; var path = f.MediaPath(item.Id); var bytes = File.ReadAllBytes(path); var stamp = File.GetLastWriteTimeUtc(path);
        var edited = await f.Store.UpdateMetadataAsync(new(item.Id, 0, "  Favorite boss fight  ", true), default);
        Check(edited.Title == "Favorite boss fight" && edited.IsFavorite && edited.MetadataRevision == 1 && edited.Revision == item.Revision, "Metadata and artwork revisions must remain separate");
        Check(bytes.SequenceEqual(File.ReadAllBytes(path)) && File.GetLastWriteTimeUtc(path) == stamp, "Editing metadata must not modify original media");
        await Reject("capture_library_query_changed", () => f.Store.QueryAsync(new(new(), 20, 20, first.Revision), default));
        await Reject("capture_library_metadata_conflict", () => f.Store.UpdateMetadataAsync(new(item.Id, 0, "Stale", false), default));
        var currentMetadata = await f.Store.GetMetadataAsync(item.Id, default);
        Check(currentMetadata.Title == edited.Title && currentMetadata.MetadataRevision == 1 && currentMetadata.FileName == Path.GetFileName(path),
            "Direct metadata lookup can refresh a preview independently of current filters");
        var favorite = await f.Store.QueryAsync(new(new() { FavoritesOnly = true, Search = "BOSS", Sort = CaptureLibrarySort.Title }), default);
        Check(favorite.TotalCount == 1 && favorite.Items.Single().Id == item.Id && favorite.Items[0].FileName == Path.GetFileName(path), "Search/favorite works across all pages");
        var facets = await f.Store.ApplicationsAsync(new(new() { Application = "Game 0" }, Limit: 4), default);
        Check(facets.Items.Count == 4 && facets.HasMore && facets.Items.All(item => item.Count == 1), "Application facet is bounded and ignores its own selected filter");
        var searchedApps = await f.Store.ApplicationsAsync(new(new(), "Game 30"), default);
        Check(searchedApps.Items.Single().Name == "Game 30" && !searchedApps.HasMore, "Application picker can search beyond its initial limit");
        var favoriteApps = await f.Store.ApplicationsAsync(new(new() { FavoritesOnly = true }), default);
        Check(favoriteApps.Items.Single().Name == "Game 0", "Application facet honors other library filters");
        var byApp = await f.Store.QueryAsync(new(new() { Application = "game 30", MediaKind = CaptureLibraryMediaKind.Screenshot }), default);
        Check(byApp.TotalCount == 1 && byApp.Items[0].Id == items[30].Id, "Application filter uses complete indexed library");
        Check((await f.Store.QueryAsync(new(new() { MediaKind = CaptureLibraryMediaKind.Video }), default)).TotalCount == 0, "Type filter excludes screenshots");
        Check((await f.Store.QueryAsync(new(new() { Search = "%' OR 1=1 --" }), default)).TotalCount == 0, "Search is literal and parameterized");
        Check((await f.Store.QueryAsync(new(new() { FromUnixMilliseconds = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds() }), default)).TotalCount == 0, "Date filter excludes out-of-range files");
        var revision = favorite.Revision;
        var noChange = await f.Store.UpdateMetadataAsync(new(item.Id, 1, edited.Title, true), default);
        Check(noChange.MetadataRevision == 1 && (await f.Store.QueryAsync(new(new(), Revision: revision), default)).Revision == revision, "No-op metadata edit must not invalidate queries");
        foreach (var sort in Enum.GetValues<CaptureLibrarySort>())
        {
            var sorted = await f.Store.QueryAsync(new(new() { Sort = sort }, 0, 64), default);
            Check(sorted.Items.Count == 47 && sorted.Items.Select(i => i.Id).Distinct().Count() == 47, "Every sort retains all unique items");
        }
        await Reject("invalid_payload", () => f.Store.QueryAsync(new(new(), Count: 65), default));
        await Reject("invalid_payload", () => f.Store.QueryAsync(new(new() { Search = new string('a', 161) }), default));
        await Reject("invalid_payload", () => f.Store.UpdateMetadataAsync(new(item.Id, 1, "bad\nlabel", false), default));
        var oldApi = await f.Store.ListAsync(0, default);
        Check(oldApi.Items.Count == 20 && oldApi.TotalCount == 47, "Legacy list semantics remain compatible");
        using var reopened = new CaptureLibraryStore(f.Library, f.Exports);
        var persisted = await reopened.QueryAsync(new(new() { FavoritesOnly = true }), default);
        Check(persisted.Items.Single().Title == edited.Title, "Metadata survives store recreation");
        var reads = reopened.MetadataReads; var reconciles = reopened.Reconciliations;
        for (var index = 0; index < 20; index++) await reopened.QueryAsync(new(new() { Sort = CaptureLibrarySort.Application }), default);
        Check(reopened.MetadataReads == reads && reopened.Reconciliations == reconciles, "Repeated indexed queries must not scan metadata or reconcile the directory");
    }

    internal static async Task EventsAndAuthority()
    {
        using var f = new Fixture();
        var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
        await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Grant);
        var simulation = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: f.Store);
        await using var broker = new PlatformCapabilityBroker(Fixture.Identity,
            [PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibraryManageV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await using var subscription = await broker.SubscribeAsync(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.changed");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var item = await f.Store.SaveAsync(f.Source, default);
        var envelope = await subscription.ReadAsync(deadline.Token);
        Check(envelope.EventType == "capture-library.changed", "Saving emits a bounded library invalidation");
        long sequence = 0;
        Task<JsonElement> Call(string capability, string operation, object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence,
            Fixture.Identity, capability, operation, BrokerJson.ToElement(payload)));
        var queried = BrokerJson.ParsePayload<CaptureLibraryQueryPage>(await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.query", new CaptureLibraryQueryRequest(new())));
        Check(queried.TotalCount == 1, "Read authority can query");
        await Reject("permission_denied", () => Call(PlatformCapabilities.CaptureLibraryManageV1, "capture-library.metadata", new CaptureLibraryMetadataRequest(item.Id, 0, "title", true)));
        await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryManageV1, ConsentDecision.Grant); await broker.RefreshConsentAsync();
        await Call(PlatformCapabilities.CaptureLibraryManageV1, "capture-library.metadata", new CaptureLibraryMetadataRequest(item.Id, 0, "title", true));
        await subscription.ReadAsync(deadline.Token);
        broker.SetLifecycle(BrokerLifecycleState.Background);
        await f.Store.UpdateMetadataAsync(new(item.Id, 1, "new title", true), default);
        await Task.Delay(180);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await subscription.ReadAsync(deadline.Token);
        await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Deny); await broker.RefreshConsentAsync();
        await Reject("capability_revoked", async () => { await subscription.ReadAsync(deadline.Token); });
        Check((await f.Store.QueryAsync(new(new()), default)).Items.Single().Title == "new title", "Revocation must not erase retained metadata");
    }
    internal static async Task BatchesAndRecycle()
    {
        using var f = new Fixture();
        var trash = Path.Combine(f.Root, "synthetic-recycle"); Directory.CreateDirectory(trash);
        var callbacks = 0;
        using var store = new CaptureLibraryStore(f.Library, f.Exports, null, null, recycle: (path, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (++callbacks == 2) return Task.FromResult(false);
            File.Move(path, Path.Combine(trash, Path.GetFileName(path)));
            return Task.FromResult(true);
        });
        var first = await store.SaveAsync(f.Source with { Attachment = f.Source.Attachment with { SourceApplication = new("Game A", "A") } }, default);
        var second = await store.SaveAsync(f.Source with { Attachment = f.Source.Attachment with { SourceApplication = new("Game B", "B") } }, default);
        var batch = await store.UpdateMetadataBatchAsync(new([
            new(first.Id, 0, "keep this title", true), new(second.Id, 9, "stale title", true)]), default);
        Check(batch.Results[0].ErrorCode is null && batch.Results[0].Item?.Title == "keep this title" &&
            batch.Results[1].ErrorCode == "capture_library_metadata_conflict", "Metadata batches report committed and conflicted items independently");
        first = batch.Results[0].Item!;
        var recycled = await store.RecycleAsync(new([new(first.Id, first.Revision, first.MetadataRevision), new(second.Id, second.Revision, second.MetadataRevision)]), default);
        Check(recycled.Results[0].ErrorCode is null && recycled.Results[1].ErrorCode == "capture_library_recycle_unavailable", "Recycle reports partial success without permanent fallback");
        var remaining = await store.QueryAsync(new(new()), default);
        Check(remaining.TotalCount == 1 && remaining.Items.Single().Id == second.Id && File.Exists(f.MediaPath(second.Id)), "Failed recycle must retain original and entry");
        Check(File.Exists(Path.Combine(f.Library, first.Id + ".recycled")), "Recovery metadata survives successful recycling");
        var restoredPath = Path.Combine(f.Exports, first.FileName!);
        File.Move(Path.Combine(trash, first.FileName!), restoredPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        CaptureLibraryQueryPage restored;
        do { await Task.Delay(20, timeout.Token); restored = await store.QueryAsync(new(new()), timeout.Token); } while (restored.TotalCount != 2);
        Check(restored.Items.Single(i => i.Id == first.Id) is { IsFavorite: true, Title: "keep this title" }, "Restoring a recycled original restores retained library metadata");
        var cancelled = new CancellationToken(true);
        var cancelResult = await store.UpdateMetadataBatchAsync(new([new(second.Id, 0, "not written", true)]), cancelled);
        Check(cancelResult.Results.Single().ErrorCode == "cancelled" && (await store.QueryAsync(new(new()), default)).Items.Single(i => i.Id == second.Id).Title is null,
            "Cancelled items are not mutated");
        await Reject("invalid_payload", () => store.RecycleAsync(new([new(second.Id, 0, 0), new(second.Id, 0, 0)]), default));
        using var unavailable = new CaptureLibraryStore(f.Library, f.Exports);
        var unsupported = await unavailable.RecycleAsync(new([new(second.Id, second.Revision, second.MetadataRevision)]), default);
        Check(unsupported.Results.Single().ErrorCode == "capture_library_recycle_unavailable" && File.Exists(f.MediaPath(second.Id)), "No recycler must fail closed without touching original");
    }

    internal static async Task LargeIndex()
    {
        using var f = new Fixture();
        var seed = await f.Store.SaveAsync(f.Source, default);
        f.Store.Dispose();
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(f.Library, seed.Id + ".json")))!;
        const int total = 3000;
        for (var index = 0; index < total; index++)
        {
            var id = (index + 1).ToString("x32"); var name = "WidgetRail-large-" + id + ".png";
            var value = metadata.DeepClone();
            var item = value["Item"]!; item["Id"] = id; item["Title"] = "Screenshot " + index; item["IsFavorite"] = index % 10 == 0;
            item["SavedAtUnixMilliseconds"] = new DateTimeOffset(1990, 1, 15, 12, 0, 0, TimeSpan.Zero).AddMonths(index % 360).AddMinutes(index).ToUnixTimeMilliseconds();
            item["Media"]!["Id"] = id;
            item["Media"]!["SourceApplication"] = System.Text.Json.JsonSerializer.SerializeToNode(new CaptureApplicationContext("Game " + index % 50, "Window"));
            item["Media"]!["SourceProcess"] = System.Text.Json.JsonSerializer.SerializeToNode(new CaptureProcessInterval(
                CaptureProcessInterval.InstanceId(42,(ulong)(1 + index % 150)),1000,1010));
            value["FileName"] = name;
            var original = Path.Combine(f.Exports, name); File.WriteAllBytes(original, f.Bytes);
            var stamp = File.GetLastWriteTimeUtc(original).Ticks; value["MediaStamp"] = stamp; item["Revision"] = stamp;
            File.WriteAllText(Path.Combine(f.Library, id + ".json"), value.ToJsonString());
        }
        using var store = new CaptureLibraryStore(f.Library, f.Exports);
        var initial = await store.QueryAsync(new(new()), default);
        Check(initial.TotalCount == total + 1, "Large index includes all captures");
        var applications = await store.ApplicationsAsync(new(new()), default);
        Check(applications.Items.Count == 51 && applications.Items.Single(item => item.Name.Length == 0).Count == 1,
            "Application picker includes the explicit unknown source group");
        var newestGroups = await store.GroupsAsync(new(new(), initial.Revision), default);
        var oldestGroups = await store.GroupsAsync(new(new() { Sort = CaptureLibrarySort.Oldest }, initial.Revision), default);
        Check(newestGroups.Items.Count == 256 && newestGroups.Items.Last().Key == "older" && newestGroups.Items.Sum(group => group.Count) == total + 1,
            "Newest month groups must form a bounded complete partition");
        Check(oldestGroups.Items.Count == 256 && oldestGroups.Items.Last().Key == "newer" && oldestGroups.Items.Sum(group => group.Count) == total + 1,
            "Oldest month groups must form a bounded complete partition");
        var filteredGroups = await store.GroupsAsync(new(new() { Application = "Game 40", FavoritesOnly = true }), default);
        Check(filteredGroups.TotalCount == 60 && filteredGroups.Items.Sum(group => group.Count) == 60, "Group counts honor the exact filtered query");
        await Reject("invalid_payload", () => store.GroupsAsync(new(new() { Sort = CaptureLibrarySort.Title }), default));
        var reads = store.MetadataReads; var reconciliations = store.Reconciliations;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < 24; index++)
        {
            var page = await store.QueryAsync(new(new() { Application = "Game 40", FavoritesOnly = true, Sort = CaptureLibrarySort.Oldest }, Count: 64), default);
            Check(page.TotalCount == 60 && page.Items.Count == 60, "Combined indexed filters return complete matches beyond the first page");
        }
        watch.Stop();
        Check(store.MetadataReads == reads && store.Reconciliations == reconciliations, "Large-library queries do not rescan metadata or decode media");
        Check(watch.Elapsed < TimeSpan.FromSeconds(15), "Large-index queries exceeded the bounded smoke budget");
        Console.WriteLine($"Large index: {total + 1:N0} captures, 24 filtered queries in {watch.Elapsed.TotalMilliseconds:N0} ms; metadata reads unchanged.");
        var sessionFilter = new CaptureLibraryQuery { AvailableOnly = true, SourceIntervals =
            Enumerable.Range(1,64).Select(i => new CaptureProcessInterval(CaptureProcessInterval.InstanceId(42,(ulong)i),999,1011)).ToArray(),
            OriginPackageId = "example.game", Reference = new("game","1"), ExcludeReference = true };
        watch.Restart();
        for (var i=0; i<24; i++)
            Check((await store.QueryAsync(new(sessionFilter,Count:8),default)).TotalCount==1280,"Session query dropped some exact matches.");
        watch.Stop();
        Check(store.MetadataReads == reads && store.Reconciliations == reconciliations,"Session lookup rescanned sidecars.");
        Console.WriteLine($"64 sessions across 3,001 captures: 24 warm lookups in {watch.Elapsed.TotalMilliseconds:N0} ms, no sidecar reads.");
        var contextOwner = Fixture.Identity with { PackageId = "widgetrail.samples.playnite-library" };
        var subject = new CaptureContextReference(CaptureContextReferenceRole.Subject, "playnite.game",
            Guid.NewGuid().ToString("D") + "/" + Guid.NewGuid().ToString("D"), "Example game") { HasRelatedAction = true };
        var db = Path.Combine(f.Library,"library.sqlite");
        var originalDbBytes = new FileInfo(db).Length;
        watch.Restart();
        for (var offset = 0; offset < 128; offset += 64)
        {
            var batch = await store.QueryAsync(new(new(),Offset:offset,Count:64),default);
            foreach (var item in batch.Items)
                await store.AttachSavedContextAsync(new(item.Id,item.Revision,new("game-link."+item.Id,[subject])),contextOwner,default);
        }
        watch.Stop();
        var receipts = Directory.GetFiles(f.Library,"association-*.json");
        Check(receipts.Length == 128, "Backfill should publish exactly one association per matched capture.");
        Console.WriteLine($"128 links: {watch.Elapsed.TotalMilliseconds:N0} ms; {receipts.Sum(path=>new FileInfo(path).Length):N0} receipt bytes, " +
            $"{new FileInfo(db).Length-originalDbBytes:N0} additional SQLite bytes.");
        store.Dispose();
        using var reopened = new CaptureLibraryStore(f.Library, f.Exports);
        watch.Restart();
        var linked = await reopened.QueryAsync(new(new(){OriginPackageId=contextOwner.PackageId,Reference=new(subject.Kind,subject.Id)},Count:4),default);
        Check(linked.TotalCount==128,"Reopening changed the link count.");
        Console.WriteLine($"Reopening the indexed 3,001-capture library with 128 links and reading four matches took {watch.Elapsed.TotalMilliseconds:N0} ms.");
    }

    internal static async Task IndexRebuildPreservesMetadata()
    {
        using var f = new Fixture();
        var trash = Path.Combine(f.Root, "recycle"); Directory.CreateDirectory(trash);
        string? recycledPath = null;
        var keep = await f.Store.SaveAsync(f.Source, default);
        var changed = await f.Store.UpdateMetadataAsync(new(keep.Id, 0, "Retained favorite", true), default);
        var bytes = File.ReadAllBytes(f.MediaPath(keep.Id));
        var second = await f.Store.SaveAsync(f.Source with { Attachment = f.Source.Attachment with { SourceApplication = new("Other game", "Window") } }, default);
        f.Store.Dispose();
        using (var recycler = new CaptureLibraryStore(f.Library, f.Exports, null, null, recycle: (path, _) =>
        {
            recycledPath = Path.Combine(trash, Path.GetFileName(path)); File.Move(path, recycledPath); return Task.FromResult(true);
        }))
        {
            var result = await recycler.RecycleAsync(new([new(second.Id, second.Revision, second.MetadataRevision)]), default);
            Check(result.Results.Single().ErrorCode is null, "Fixture recycles second capture");
        }
        var indexPath = Path.Combine(f.Library, "library.sqlite");
        File.Delete(indexPath); // Only this fixture's disposable index is removed.
        using (var rebuilt = new CaptureLibraryStore(f.Library, f.Exports))
        {
            var page = await rebuilt.QueryAsync(new(new()), default);
            Check(page.TotalCount == 1 && page.Items.Single() is { Title: "Retained favorite", IsFavorite: true, MetadataRevision: 1 },
                "Deleting only SQLite preserves custom metadata and does not revive recycled files");
            Check(bytes.SequenceEqual(File.ReadAllBytes(f.MediaPath(keep.Id))), "Index deletion never changes original bytes");
            File.Move(recycledPath!, Path.Combine(f.Exports, Path.GetFileName(recycledPath!)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while ((await rebuilt.QueryAsync(new(new()), timeout.Token)).TotalCount != 2) await Task.Delay(20, timeout.Token);
        }
        File.WriteAllText(indexPath, "deliberately corrupt fixture index");
        using (var repaired = new CaptureLibraryStore(f.Library, f.Exports))
        {
            var page = await repaired.QueryAsync(new(new()), default);
            Check(page.TotalCount == 2 && page.Items.Single(item => item.Id == keep.Id).Title == changed.Title && page.Items.Single(item => item.Id == keep.Id).IsFavorite,
                "Corrupt-index rebuild retains original metadata without duplicate restored rows");
            Check(bytes.SequenceEqual(File.ReadAllBytes(f.MediaPath(keep.Id))), "Corrupt-index repair preserves original bytes");
        }
    }

    internal static async Task ExplicitRefreshRejectsPreservedTimestampCorruption()
    {
        using var f = new Fixture(); var item = await f.Store.SaveAsync(f.Source, default);
        var metadata = Path.Combine(f.Library, item.Id + ".json");
        var stamp = File.GetLastWriteTimeUtc(metadata);
        File.WriteAllText(metadata, "not json"); File.SetLastWriteTimeUtc(metadata, stamp);
        var refreshed = await f.Store.ListAsync(0, default, refresh: true);
        Check(refreshed.Items.Single().Available == false && CaptureLibraryLimits.Valid(refreshed),
            "Explicit refresh must validate changed metadata even when timestamp was preserved");
        await f.Store.DeleteAsync(item.Id, default);
        Check((await f.Store.ListAsync(0, default)).TotalCount == 0, "Damaged metadata remains deletable after refresh");
    }

    internal static async Task UnavailableRemovalNeverDeletesRestoredOriginal()
    {
        using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
        var original = f.MediaPath(saved.Id); var bytes = File.ReadAllBytes(original);
        File.Delete(original);
        var missing = (await f.Store.ListAsync(0, default, refresh: true)).Items.Single();
        Check(!missing.Available, "Fixture must publish an unavailable row");
        File.WriteAllBytes(original, bytes);
        await Reject("capture_library_metadata_conflict", () => f.Store.RemoveUnavailableAsync(new(saved.Id, missing.Revision, missing.MetadataRevision), default));
        Check(File.Exists(original) && bytes.SequenceEqual(File.ReadAllBytes(original)), "Stale unavailable removal must never delete a restored original");
        File.Delete(original);
        var current = (await f.Store.ListAsync(0, default, refresh: true)).Items.Single();
        await f.Store.RemoveUnavailableAsync(new(current.Id, current.Revision, current.MetadataRevision), default);
        Check((await f.Store.QueryAsync(new(new()), default)).TotalCount == 0 && !File.Exists(Path.Combine(f.Library, current.Id + ".json")), "Still-missing entry can be removed without a media operation");
    }

    internal static async Task LegacyWireShapeRemainsClosed()
    {
        using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
        await f.Store.UpdateMetadataAsync(new(saved.Id, 0, "New metadata", true), default);
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
        var list = await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.list", new CaptureLibraryListRequest());
        var oldPage = list.Deserialize<LegacyPage>(BrokerJson.StrictOptions)!;
        Check(oldPage.Items.Single().Id == saved.Id && !list.GetProperty("items")[0].TryGetProperty("title", out _), "Legacy list response changed its closed item shape");
        var opened = BrokerJson.ParsePayload<CaptureAttachment>(await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.open", new CaptureLibraryOpenRequest(saved.Id)));
        var save = await Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save", new CaptureLibrarySaveRequest(opened));
        Check(save.Deserialize<LegacyItem>(BrokerJson.StrictOptions)!.Id == saved.Id && !save.TryGetProperty("isFavorite", out _), "Legacy save response changed its closed item shape");
        var currentSave = BrokerJson.ParsePayload<CaptureLibraryItem>(await Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save-current", new CaptureLibrarySaveRequest(opened)));
        Check(currentSave.Id == saved.Id && currentSave.Title == "New metadata" && currentSave.IsFavorite, "Modern save lost deduplicated metadata");
        var enriched = BrokerJson.ParsePayload<CaptureLibraryItem>(await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.get-metadata", new CaptureLibraryReference(saved.Id)));
        Check(enriched.Title == "New metadata" && enriched.IsFavorite, "Legacy projection must not erase stored enriched metadata");
    }
    private sealed record LegacyItem(string Id, CaptureAttachment Media, long SavedAtUnixMilliseconds, bool Available, long Revision);
    private sealed record LegacyPage(IReadOnlyList<LegacyItem> Items, int Offset, int TotalCount, long StoredBytes);

}
