using System.Text.Json;
using Microsoft.Data.Sqlite;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class HiddenCaptureTests
{
    private static readonly BrokerWidgetIdentity Owner = new("widgetrail.tests.hidden", "widgetrail.tests", "one");
    private static CaptureLibraryMutationReference Ref(CaptureLibraryItem item) => new(item.Id, item.Revision, item.MetadataRevision);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action, string code)
    { try { await action(); } catch (BrokerException error) when (error.Code == code) { return; } throw new Exception("Expected " + code); }

    internal static async Task RebuildAndRestore()
    {
        using var f = new Fixture(); var item = await f.Add();
        item = await f.Store.UpdateMetadataAsync(new(item.Id, item.MetadataRevision, "Remember this", true), default);
        await using var playback = await f.Store.OpenAsync(item.Id, CaptureImageResolution.Original, default);
        await f.Store.RemoveLinkAsync(Ref(item), default);
        Check(playback.IsCurrent && playback.Stream.Length == 12, "Hide revoked an authorized retained original");
        Check((await f.Query()).TotalCount == 0, "Hidden entry stayed in visible discovery");
        var hidden = (await f.Query(true)).Items.Single();
        Check(hidden.MetadataRevision > item.MetadataRevision && hidden.Title == item.Title && hidden.IsFavorite, "Hide lost metadata/revision");
        Check((await f.Store.GroupsAsync(new(new() { HiddenOnly = true }), default)).TotalCount == 1, "Hidden groups differ from query");
        Check((await f.Store.FindAsync(new(new(), item.Id), default)).Index is null &&
            (await f.Store.FindAsync(new(new() { HiddenOnly = true }, item.Id), default)).Index == 0, "Hidden find leaked into default discovery");
        Check((await f.Store.FoldersAsync(new(new() { HiddenOnly = true }), default)).Items.Single(x => x.SourceId == f.Source!.Id).Count == 1,
            "Hidden folder facet lost its source");
        await Reject(() => f.Store.UnhideAsync(Ref(item), default), "capture_library_metadata_conflict");
        playback.Dispose(); f.Store.Dispose();
        File.Delete(Path.Combine(f.Index, "library.sqlite")); File.Delete(Path.Combine(f.Index, item.Id + ".json"));
        f.Store = f.Create();
        hidden = (await f.Query(true)).Items.Single();
        Check((await f.Query()).TotalCount == 0 && hidden.Title == "Remember this", "Canonical snapshot did not survive index/metadata loss");
        Check((await f.Store.GetMetadataAsync(item.Id, default)).Id == item.Id, "Known-ID read incorrectly treats hide as permission revocation");
        var restored = await f.Store.UnhideAsync(Ref(hidden), default);
        Check(restored.MetadataRevision > hidden.MetadataRevision && (await f.Query()).TotalCount == 1 && (await f.Query(true)).TotalCount == 0,
            "Unhide did not publish one visible entry");
        Check(File.ReadAllBytes(f.Original).SequenceEqual(f.Bytes), "Hide/Unhide changed original bytes");
    }

    internal static async Task PhysicalIdentity()
    {
        using var f = new Fixture(); var item = await f.Add();
        await f.Store.RemoveLinkAsync(Ref(item), default);
        File.AppendAllText(f.Original, "same file edit");
        await Reject(() => f.Store.LinkAsync(new(f.Source!.Id, "capture.png"), Owner, default), "capture_library_link_removed");
        Check((await f.Query()).TotalCount == 0 && (await f.Query(true)).Items.Single().Revision != item.Revision, "Editing same physical file removed its hide");
        File.Move(f.Original, Path.Combine(f.Root, "previous.png")); File.WriteAllBytes(f.Original, f.Bytes);
        using (var lockedMetadata = new FileStream(Path.Combine(f.Index, item.Id + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Reject(async () => { await using var unexpected = await f.Store.OpenAsync(item.Id, CaptureImageResolution.Original, default); }, "capture_library_io_failed");
            Check((await f.Query()).TotalCount == 0 && (await f.Query(true)).TotalCount == 1, "Replacement cleanup failure lost the canonical exclusion");
        }
        await Reject(async () => { await using var unexpected = await f.Store.OpenAsync(item.Id, CaptureImageResolution.Original, default); }, "capture_library_not_found");
        var replacement = (await f.Store.LinkAsync(new(f.Source!.Id, "capture.png"), Owner, default)).Item;
        Check(replacement.Id != item.Id && (await f.Query()).TotalCount == 1 && (await f.Query(true)).TotalCount == 0,
            "Replacement physical file inherited old hide");
    }

    internal static async Task LegacyMigration()
    {
        using var f = new Fixture(); var item = await f.Add(); f.Store.Dispose();
        File.WriteAllText(Path.Combine(f.Index, item.Id + ".unlinked"), "");
        File.Delete(Path.Combine(f.Index, item.Id + ".json")); File.Delete(Path.Combine(f.Index, "library.sqlite"));
        f.Store = f.Create();
        Check((await f.Query()).TotalCount == 0 && (await f.Query(true)).TotalCount == 0, "Snapshot-less legacy entry fabricated media");
        await Reject(() => f.Store.LinkAsync(new(f.Source!.Id, "capture.png"), Owner, default), "capture_library_link_removed");
        Check(!File.Exists(Path.Combine(f.Index, item.Id + ".unlinked")) && (await f.Query(true)).TotalCount == 1, "Legacy migration/enrichment failed");
        File.Move(f.Original, Path.Combine(f.Root, "previous.png")); File.WriteAllBytes(f.Original, f.Bytes);
        await Reject(() => f.Store.LinkAsync(new(f.Source!.Id, "capture.png"), Owner, default), "capture_library_link_removed");
        Check((await f.Query()).TotalCount == 0, "Legacy unknown identity silently stopped suppressing its path");
        await f.Store.UnhideAsync(Ref((await f.Query(true)).Items.Single()), default);
        Check((await f.Query()).TotalCount == 1, "Explicit legacy Unhide did not restore discovery");
    }

    internal static async Task MissingRevokedAndCancellation()
    {
        using var f = new Fixture(); var item = await f.Add();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await f.Store.RemoveLinkAsync(Ref(item), cancelled.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        Check((await f.Query()).TotalCount == 1, "Cancelled hide committed");
        await f.Store.RemoveLinkAsync(Ref(item), default);
        File.Delete(f.Original);
        var missing = await f.Store.GetMetadataAsync(item.Id, default);
        Check(!missing.Available, "Missing hidden original was marked available");
        await f.Store.UnhideAsync(Ref(missing), default);
        Check((await f.Query()).TotalCount == 1, "Missing authorized entry could not be unhidden");
        await f.Store.RemoveLinkAsync(Ref((await f.Query()).Items.Single()), default);
        await f.Store.RemoveSourceAsync(f.Source!.Id, Owner, default);
        Check((await f.Query(true)).TotalCount == 0, "Revoked source leaked hidden metadata");
        await Reject(() => f.Store.GetMetadataAsync(item.Id, default), "capture_library_source_unavailable");
    }

    internal static async Task CorruptionAndWire()
    {
        Check(!JsonSerializer.Serialize(new CaptureLibraryQuery()).Contains("HiddenOnly", StringComparison.Ordinal), "Default wire leaks new field to old readers");
        Check(JsonSerializer.Serialize(new CaptureLibraryQuery { HiddenOnly = true }).Contains("HiddenOnly", StringComparison.Ordinal), "Explicit hidden query omitted");
        using var f = new Fixture(); var item = await f.Add(); await f.Store.RemoveLinkAsync(Ref(item), default);
        var owned = await f.Store.SaveAsync(new(item.Media, f.Original), default);
        f.Store.Dispose(); var canonical = Path.Combine(f.Index, "library-user-state.sqlite");
        File.WriteAllText(canonical, "not a database"); var damaged = File.ReadAllBytes(canonical); f.Store = f.Create();
        Check((await f.Query()).Items.Single().Id == owned.Id, "Corrupt canonical state resurrected linked media or blocked owned media");
        Check((await f.Store.ListAsync(0, default, false)).TotalCount == 1, "Owned listing stopped with linked exclusions unavailable");
        await using (var retained = await f.Store.OpenAsync(owned.Id, CaptureImageResolution.Original, default))
            Check(retained.Stream.Length == f.Bytes.Length, "Owned original became inaccessible after exclusion corruption");
        await Reject(() => f.Query(true), "capture_library_user_state_unavailable");
        await Reject(() => f.Store.GetMetadataAsync(item.Id, default), "capture_library_user_state_unavailable");
        Check(File.ReadAllBytes(canonical).SequenceEqual(damaged), "Corrupt canonical store was replaced by an empty database");
        f.Store.Dispose(); File.Delete(canonical); f.Store = f.Create();
        Check((await f.Query()).Items.Single().Id == owned.Id && !File.Exists(canonical), "Missing initialized canonical store was recreated empty");
        await Reject(() => f.Query(true), "capture_library_user_state_unavailable");
    }

    internal static async Task FavoriteContinuity()
    {
        using var f = new Fixture(); var item = await f.Add();
        Task<CaptureLibraryContinuityPage> Read(CaptureLibraryQuery? query = null, string? revision = null) =>
            f.Store.QueryContinuityAsync(new(query ?? new(), Revision: revision), default);
        var before = await Read();
        var favorite = await f.Store.UpdateMetadataAsync(new(item.Id, item.MetadataRevision, item.Title, true), default);
        var after = await Read();
        Check(before.OrderingRevision == after.OrderingRevision && before.Page.Revision != after.Page.Revision && after.Page.Items.Single().IsFavorite,
            "Favorite-only refresh lost order proof or failed to advance range revision");
        await Reject(() => Read(revision: before.Page.Revision), "capture_library_query_changed");
        var filteredBefore = await Read(new() { FavoritesOnly = true });
        var cleared = await f.Store.UpdateMetadataAsync(new(favorite.Id, favorite.MetadataRevision, favorite.Title, false), default);
        var filteredAfter = await Read(new() { FavoritesOnly = true });
        Check(filteredBefore.OrderingRevision != filteredAfter.OrderingRevision && filteredAfter.Page.TotalCount == 0,
            "Favorites membership change incorrectly retained order proof");
        before = await Read();
        var renamed = await f.Store.UpdateMetadataAsync(new(cleared.Id, cleared.MetadataRevision, "New searchable title", false), default);
        after = await Read(); Check(before.OrderingRevision != after.OrderingRevision, "Title/search change retained order proof");
        before = after; await f.Store.RemoveLinkAsync(Ref(renamed), default); after = await Read();
        Check(before.OrderingRevision != after.OrderingRevision && after.Page.TotalCount == 0, "Hide retained order proof");
        var hidden = (await f.Query(true)).Items.Single(); await f.Store.UnhideAsync(Ref(hidden), default);
        var restored = await Read(); Check(restored.OrderingRevision != after.OrderingRevision && restored.Page.TotalCount == 1, "Unhide retained order proof");
        f.Store.Dispose(); f.Store = f.Create();
        Check((await Read()).OrderingRevision == restored.OrderingRevision, "Unchanged restart gratuitously discarded order proof");
        Check(!JsonSerializer.Serialize(restored.Page).Contains("OrderingRevision", StringComparison.Ordinal), "Old query envelope acquired new response property");
    }

    internal static async Task InterruptedPublication()
    {
        using var f = new Fixture(); var item = await f.Add();
        var pending = Path.Combine(f.Index, item.Id + ".json.pending");
        Directory.CreateDirectory(pending);
        try { await f.Store.RemoveLinkAsync(Ref(item), default); throw new Exception("Expected metadata publication failure"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        Check((await f.Query()).TotalCount == 0 && (await f.Query(true)).Items.Single().MetadataRevision > item.MetadataRevision,
            "Sidecar failure resurrected a durably committed hide or retained stale metadata revision");
        Directory.Delete(pending);
        await f.Store.UnhideAsync(Ref((await f.Query(true)).Items.Single()), default);
        Check((await f.Query()).TotalCount == 1, "Recoverable publication failure prevented explicit restoration");
    }

    internal static async Task BrokerAdmission()
    {
        using var f = new Fixture(); var item = await f.Add(); await f.Store.RemoveLinkAsync(Ref(item), default);
        var hidden = (await f.Query(true)).Items.Single();
        var consent = new ConsentStore(Path.Combine(f.Root, "consent")); var simulation = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary:f.Store);
        await using var broker = new PlatformCapabilityBroker(Owner, [PlatformCapabilities.CaptureLibraryReadV1,PlatformCapabilities.CaptureLibraryManageV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive); long sequence = 0;
        Task<JsonElement> Call(string capability, string operation, object request) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Owner,
            capability, operation, BrokerJson.ToElement(request)));
        var rejected = false;
        try { await Call(PlatformCapabilities.CaptureLibraryManageV1, "capture-library.unhide", Ref(hidden)); }
        catch (BrokerException) { rejected = true; }
        Check(rejected && (await f.Query()).TotalCount == 0, "Unapproved restore mutated canonical state");
        await consent.SetDecisionAsync(Owner, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Grant);
        var read = BrokerJson.ParsePayload<CaptureLibraryContinuityPage>(await Call(PlatformCapabilities.CaptureLibraryReadV1,
            "capture-library.query-continuity", new CaptureLibraryQueryRequest(new() { HiddenOnly = true })));
        Check(read.Page.Items.Single().Id == hidden.Id, "Registered bounded hidden query did not reach broker storage");
        await consent.SetDecisionAsync(Owner, PlatformCapabilities.CaptureLibraryManageV1, ConsentDecision.Grant);
        var restored = BrokerJson.ParsePayload<CaptureLibraryItem>(await Call(PlatformCapabilities.CaptureLibraryManageV1, "capture-library.unhide", Ref(hidden)));
        Check(restored.MetadataRevision > hidden.MetadataRevision && (await f.Query()).TotalCount == 1, "Registered Manage restore did not publish exact item");
    }

    internal static async Task DamagedSnapshotIsolation()
    {
        using var f = new Fixture(); var item = await f.Add(); await f.Store.RemoveLinkAsync(Ref(item), default);
        var owned = await f.Store.SaveAsync(new(item.Media, f.Original), default); f.Store.Dispose();
        using (var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(f.Index,"library-user-state.sqlite"),Pooling=false }.ToString()))
        {
            database.Open(); using var command = database.CreateCommand(); command.CommandText="UPDATE hidden SET snapshot='not valid JSON'"; command.ExecuteNonQuery();
        }
        f.Store = f.Create();
        Check((await f.Query()).Items.Single().Id == owned.Id, "Malformed exclusion snapshot blocked owned media or resurrected linked media");
        await Reject(() => f.Query(true), "capture_library_user_state_unavailable");
        Check((await f.Query()).Items.Single().Id == owned.Id, "Corrupt snapshot failure was silently cleared on next operation");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "wrail-hidden-" + Guid.NewGuid().ToString("N"));
        internal string Index => Path.Combine(Root, "index");
        internal string Original => Path.Combine(Root, "source", "capture.png");
        internal readonly byte[] Bytes = [137,80,78,71,13,10,26,10,1,2,3,4];
        internal CaptureLibraryStore Store;
        internal CaptureLibrarySource? Source;
        internal Fixture() { Directory.CreateDirectory(Path.Combine(Root, "source")); Store = Create(); }
        internal CaptureLibraryStore Create() => new(Index, Path.Combine(Root, "owned"), null, null,
            (path, seed, _) => Task.FromResult(seed with { Width = 16, Height = 16, ByteLength = new FileInfo(path).Length }));
        internal async Task<CaptureLibraryItem> Add()
        {
            File.WriteAllBytes(Original, Bytes);
            Source = await Store.AddSourceAsync(Path.Combine(Root, "source"), "Hidden test", Owner, default);
            return (await Store.LinkAsync(new(Source.Id, "capture.png"), Owner, default)).Item;
        }
        internal Task<CaptureLibraryQueryPage> Query(bool hidden = false) => Store.QueryAsync(new(new() { HiddenOnly = hidden }), default);
        public void Dispose() { Store.Dispose(); Directory.Delete(Root, true); }
    }
}
