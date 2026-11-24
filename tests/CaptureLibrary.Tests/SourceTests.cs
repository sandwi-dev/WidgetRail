using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetBridge;

internal static partial class SourceTests
{
    internal static async Task CodecProbe(string image, string video, string output)
    {
        using var fixture = new Fixture(); fixture.Store.Dispose();
        fixture.Store = new(Path.Combine(fixture.Root, "index"), fixture.Public, WindowsCaptureImageCodec.CreateContextAsync,
            WindowsCaptureImageCodec.CreateThumbnailAsync, WindowsCaptureImageCodec.InspectAsync);
        File.Copy(Path.GetFullPath(image), Path.Combine(fixture.Provider, "image.png"));
        File.Copy(Path.GetFullPath(video), Path.Combine(fixture.Provider, "video.mp4"));
        var source = await fixture.Store.AddSourceAsync(fixture.Provider, "Synthetic codec probe", Owner, default);
        var screenshot = (await fixture.Store.LinkAsync(new(source.Id, "image.png"), Owner, default)).Item;
        var recording = (await fixture.Store.LinkAsync(new(source.Id, "video.mp4"), Owner, default)).Item;
        Check(screenshot.Media.IsWellFormed() && screenshot.Media.Width > 1 && screenshot.Media.Height > 1, "Real linked PNG did not decode");
        Check(recording.Media.IsWellFormed() && recording.Media.DurationSeconds > 5 && recording.Media.Properties is null, "Real linked MP4 requires fabricated encoder telemetry");
        var pngThumbnail = await fixture.Store.ThumbnailAsync(screenshot.Id, default);
        var videoThumbnail = await fixture.Store.ThumbnailAsync(recording.Id, default);
        Check(pngThumbnail.IsWellFormed() && videoThumbnail.IsWellFormed(), "Real linked thumbnails failed");
        await using var lease = await fixture.Store.OpenAsync(recording.Id, CaptureImageResolution.Original, default);
        Check(lease.Stream.Length == new FileInfo(video).Length && !Directory.EnumerateFiles(fixture.Public).Any(), "Real codec probe copied or changed original");
        await File.WriteAllTextAsync(Path.GetFullPath(output), JsonSerializer.Serialize(new
        {
            passed = true, screenshot = new { screenshot.Media.Width, screenshot.Media.Height },
            video = new { recording.Media.Width, recording.Media.Height, recording.Media.DurationSeconds },
            pngThumbnailBytes = pngThumbnail.Bytes.Length, videoThumbnailBytes = videoThumbnail.Bytes.Length, publicCopies = 0
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static readonly BrokerWidgetIdentity Owner = new("widgetrail.tests.sources", "widgetrail.tests", "first");
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static CaptureLibraryMutationReference Reference(CaptureLibraryItem item) => new(item.Id, item.Revision, item.MetadataRevision);
    private static async Task Reject(Func<Task> action, string code)
    { try { await action(); throw new Exception("Expected " + code); } catch (BrokerException e) when (e.Code == code) { } }
    internal static async Task LinkingAndReadOnly()
    {
        using var fixture = new Fixture();
        fixture.Write("Game/achievement.png");
        var source = await fixture.Store.AddSourceAsync(fixture.Provider, "Provider captures", Owner, default);
        var item = (await fixture.Store.LinkAsync(new(source.Id, "Game/achievement.png"), Owner, default)).Item;
        Check(item.SourceId == source.Id && item.Media.Origin == CaptureAttachmentOrigin.SavedLibrary, "Link lost ownership classification");
        Check(item.CapturedAtUnixMilliseconds is null && item.FileModifiedAtUnixMilliseconds < item.SavedAtUnixMilliseconds,
            "Filesystem time was mislabeled as actual capture or discovery time");
        Check(!Directory.EnumerateFiles(fixture.Public).Any(), "Link copied original media");
        Check((await fixture.Store.ListAsync(0, default)).Items.Count == 0, "Legacy list exposed linked originals as owned captures");
        Check((await fixture.Store.QueryAsync(new(new()), default)).Items.Any(i => i.Id == item.Id), "Indexed query missed linked capture");
        await using (var lease = await fixture.Store.OpenAsync(item.Id, CaptureImageResolution.Original, default))
        { Check(lease.Stream.Length == fixture.Bytes.Length, "Linked media cannot open"); }
        await Reject(() => fixture.Store.DeleteAsync(item.Id, default), "capture_library_read_only_source");
        var recycle = await fixture.Store.RecycleAsync(new([Reference(item)]), default);
        Check(recycle.Results[0].ErrorCode == "capture_library_read_only_source", "Recycle accepted provider-owned file");
        await fixture.Store.RemoveLinkAsync(Reference(item), default);
        await Reject(() => fixture.Store.LinkAsync(new(source.Id, "Game/achievement.png"), Owner, default), "capture_library_link_removed");
        Check(File.ReadAllBytes(Path.Combine(fixture.Provider, "Game/achievement.png")).SequenceEqual(fixture.Bytes), "Removing link altered provider original");
        await fixture.Store.RemoveSourceAsync(source.Id, Owner, default);
        Check((await fixture.Store.SourcesAsync(Owner, default)).Items.Count == 0, "Removed source still listed");
    }
    internal static async Task ScopeAndCompanion()
    {
        using var fixture = new Fixture(); fixture.Write("Game/first.png");
        var source = await fixture.Store.AddSourceAsync(fixture.Provider, "Provider captures", Owner, default);
        var stranger = new BrokerWidgetIdentity("widgetrail.tests.other", "widgetrail.tests", "first");
        await Reject(() => fixture.Store.AddSourceAsync(fixture.Provider, "Duplicate", stranger, default), "capture_library_source_overlap");
        await Reject(() => fixture.Store.AddSourceAsync(Path.Combine(fixture.Provider, "Game"), "Nested", Owner, default), "capture_library_source_overlap");
        await Reject(() => fixture.Store.AddSourceAsync(fixture.Public, "Owned storage", Owner, default), "capture_library_source_overlap");
        await Reject(() => fixture.Store.AddSourceAsync(fixture.Root, "All storage", Owner, default), "capture_library_source_overlap");
        Check(source.CanonicalFolderPath == Path.GetFullPath(fixture.Provider), "Chosen path is not available for provider configuration");
        await Reject(() => fixture.Store.LinkAsync(new(source.Id, "Game/first.png"), stranger, default), "permission_denied");
        await Reject(() => fixture.Store.LinkAsync(new(source.Id, "../outside.png"), Owner, default), "invalid_payload");
        await Reject(() => fixture.Store.LinkAsync(new(source.Id, "C:/outside.png"), Owner, default), "invalid_payload");
        Check((await fixture.Store.SourcesAsync(stranger, default)).Items.Count == 0, "Source leaked to another owner");
        var context = new CaptureSaveContext("test-event", [new(CaptureContextReferenceRole.Subject, "game", "123", "Game")]);
        await File.WriteAllTextAsync(Path.Combine(fixture.Provider, "Game/first.png.widgetrail-context.json"), JsonSerializer.Serialize(context, BrokerJson.StrictOptions));
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).Items.Any(i => i.ContextCount == 1));
        var item = (await fixture.Store.QueryAsync(new(new()), default)).Items.Single();
        var linked = await fixture.Store.LinkAsync(new(source.Id, "Game/first.png", context), Owner, default);
        Check(linked.Item.Id == item.Id && linked.Item.ContextCount == 1, "Companion replay duplicated context");
        var contexts = await fixture.Store.GetContextsAsync(new(item.Id), default);
        Check(contexts.Items[0].Origin.PackageId == Owner.PackageId, "Companion supplied authority instead of grant owner");
        await fixture.Store.RemoveSourceAsync(source.Id, Owner, default);
        Check(File.Exists(Path.Combine(fixture.Provider, "Game/first.png")), "Source removal deleted provider media");
        var reconnected = await fixture.Store.AddSourceAsync(fixture.Provider, "Provider captures", Owner, default);
        Check(reconnected.Id != source.Id, "Explicit reconnect reused a retired grant");
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).Items.Any(i => i.SourceId == reconnected.Id && i.ContextCount == 1));
    }
    internal static async Task OfflineAndLiveDiscovery()
    {
        using var fixture = new Fixture(); fixture.Write("first.png");
        var source = await fixture.Store.AddSourceAsync(fixture.Provider, "Provider", Owner, default);
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 1);
        fixture.Write("second.png");
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 2);
        fixture.Store.Dispose(); fixture.Store = fixture.CreateStore();
        fixture.Write("offline.png"); fixture.Store.StartSources();
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 3);
        var first = (await fixture.Store.QueryAsync(new(new()), default)).Items.First(i => i.FileName == "first.png");
        File.Delete(Path.Combine(fixture.Provider, "first.png"));
        await Eventually(async () => !(await fixture.Store.GetMetadataAsync(first.Id, default)).Available);
        fixture.Write("first.png");
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new() { AvailableOnly = true }), default)).Items.Any(item => item.FileName == "first.png"));
        Check((await fixture.Store.SourcesAsync(Owner, default)).Items.Single().Id == source.Id, "Grant identity changed on restart");
    }
    internal static async Task FolderMoveAndReplacementRecovery()
    {
        using var fixture = new Fixture();
        const string album = "Album_%";
        var available = new CaptureLibraryQuery { AvailableOnly = true };
        var unavailable = new CaptureLibraryQuery { UnavailableOnly = true };
        for (var i = 0; i < 80; i++) fixture.Write($"{album}/{i}.png");
        fixture.Write("Album_%2/keep.png");
        var store = fixture.Store;
        var source = await store.AddSourceAsync(fixture.Provider, "Photos", Owner, default);
        await Eventually(async () => (await store.QueryAsync(new(available), default)).TotalCount == 81);
        var original = (await store.LinkAsync(new(source.Id, album + "/0.png",
            new("original-game-link", [new(CaptureContextReferenceRole.Subject, "game", "original")])), Owner, default)).Item;
        await store.UpdateMetadataAsync(new(original.Id, original.MetadataRevision, "Original screenshot", true), default);
        var moved = Path.Combine(fixture.Root, "moved");
        Directory.Move(Path.Combine(fixture.Provider, album), moved);
        await Eventually(async () => (await store.QueryAsync(new(unavailable), default)).TotalCount == 80);
        var visible = await store.QueryAsync(new(available), default);
        Check(visible.TotalCount == 1 && visible.Items.Single().FileName == "keep.png", "Folder move matched an adjacent prefix");
        var missing = await store.QueryAsync(new(unavailable, 40, 20, visible.Revision), default);
        Check(missing.Items.Count == 20 && missing.Items.All(item => !item.Available) && missing.StoredBytes == 0, "Recovery paging exposed missing bytes or available media");
        Check((await store.GroupsAsync(new(unavailable), default)).TotalCount == 80 &&
            (await store.FindAsync(new(available, original.Id), default)).Index is null, "Counts or focus lookup included missing media");
        await Reject(() => store.QueryAsync(new(available with { UnavailableOnly = true }), default), "invalid_payload");
        Directory.Move(moved, Path.Combine(fixture.Provider, album));
        await Eventually(async () => (await store.QueryAsync(new(available), default)).TotalCount == 81);
        var path = Path.Combine(fixture.Provider, album, "0.png");
        var backup = Path.Combine(fixture.Root, "original.png");
        File.Move(path, backup);
        fixture.Write(album + "/0.png");
        await Reject(async () => { await using var lease = await store.OpenAsync(original.Id, CaptureImageResolution.Original, default); }, "capture_library_not_found");
        var fresh = (await store.LinkAsync(new(source.Id, album + "/0.png"), Owner, default)).Item;
        Check(fresh.Id != original.Id && !fresh.IsFavorite && fresh.Title is null && fresh.ContextCount == 0,
            "Replacement inherited original annotations or game link");
        var retained = await store.GetMetadataAsync(original.Id, default);
        Check(!retained.Available && retained.ContextCount == 1 && retained.IsFavorite, "Missing original lost its metadata");
        File.Delete(path); File.Move(backup, path);
        var restored = (await store.LinkAsync(new(source.Id, album + "/0.png"), Owner, default)).Item;
        Check(restored.Id == original.Id && restored.Title == "Original screenshot" && restored.IsFavorite && restored.ContextCount == 1,
            "Restoring original media lost annotations or links");
        Directory.Move(fixture.Provider, moved);
        await Eventually(async () => (await store.QueryAsync(new(available), default)).TotalCount == 0);
        await Reject(() => store.RemoveUnavailableAsync(Reference(restored), default), "capture_library_source_unavailable");
        Directory.Move(moved, fixture.Provider);
        await Eventually(async () => (await store.QueryAsync(new(available), default)).TotalCount == 81);
    }

    internal static async Task IdentityAndIndexRebuild()
    {
        using var fixture = new Fixture(); fixture.Write("file.png");
        var source = await fixture.Store.AddSourceAsync(fixture.Provider, "Provider", Owner, default);
        var original = (await fixture.Store.LinkAsync(new(source.Id, "file.png"), Owner, default)).Item;
        var path = Path.Combine(fixture.Provider, "file.png"); var modified = File.GetLastWriteTimeUtc(path);
        var changed = fixture.Bytes.ToArray(); changed[^1] ^= 1;
        File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, modified);
        var updated = (await fixture.Store.LinkAsync(new(source.Id, "file.png"), Owner, default)).Item;
        Check(updated.Revision != original.Revision && updated.FileModifiedAtUnixMilliseconds == original.FileModifiedAtUnixMilliseconds,
            "Same-length/mtime replacement did not advance file identity revision");
        await using (var lease = await fixture.Store.OpenAsync(updated.Id, CaptureImageResolution.Original, default))
        { var bytes = new byte[changed.Length]; await lease.Stream.ReadExactlyAsync(bytes); Check(bytes.SequenceEqual(changed), "Verified linked read returned stale content"); }
        await fixture.Store.RemoveLinkAsync(Reference(updated), default);
        fixture.Store.Dispose(); File.Delete(Path.Combine(fixture.Root, "index", "library.sqlite"));
        fixture.Store = fixture.CreateStore(); fixture.Store.StartSources();
        await Task.Delay(250);
        Check((await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 0, "Index rebuild resurrected explicitly hidden source entry");
        await Reject(() => fixture.Store.LinkAsync(new(source.Id, "file.png"), Owner, default), "capture_library_link_removed");
        await fixture.Store.RemoveSourceAsync(source.Id, Owner, default);
        var reconnected = await fixture.Store.AddSourceAsync(fixture.Provider, "Provider", Owner, default);
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 1);
        Check((await fixture.Store.QueryAsync(new(new()), default)).Items.Single().SourceId == reconnected.Id, "Explicit reconnect did not restore hidden file under new grant");
    }
    internal static async Task HistoricalGrants()
    {
        using var fixture = new Fixture();
        for (var index = 0; index < 70; index++)
        {
            var grant = await fixture.Store.AddSourceAsync(fixture.Provider, "Repeated selection", Owner, default);
            await fixture.Store.RemoveSourceAsync(grant.Id, Owner, default);
        }
        var metadata = Path.Combine(fixture.Root, "index");
        Check(!Directory.EnumerateFiles(metadata, "source-*.grant").Any(), "Completed source disconnects retain historical grants");
        fixture.Store.Dispose();
        // Model recovery after many interrupted disconnects before an active grant.
        for (var index = 0; index < 80; index++)
        {
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(metadata, "source-" + id + ".grant"), JsonSerializer.Serialize(new
                { Id = id, Label = "Retired", Directory = fixture.Provider, Owner, Enabled = false }));
        }
        var activeId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(metadata, "source-" + activeId + ".grant"), JsonSerializer.Serialize(new
            { Id = activeId, Label = "Active", Directory = fixture.Provider, Owner, Enabled = true }));
        fixture.Write("offline.png"); fixture.Store = fixture.CreateStore(); fixture.Store.StartSources();
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).Items.Any(i => i.SourceId == activeId));
        Check((await fixture.Store.SourcesAsync(Owner, default)).Items.Single().Id == activeId, "Historical grants hid a later active source");
        Check(Directory.EnumerateFiles(metadata, "source-*.grant").Count() == 1, "Recovered empty retired grants were not pruned");
    }
    internal static async Task LegacyMetadataEnvelope()
    {
        using var fixture = new Fixture(); fixture.Write("legacy.mp4");
        var source = new HostCaptureAttachment(new(Guid.NewGuid().ToString("N"), "video/mp4", 640, 360, fixture.Bytes.Length, 6, long.MaxValue)
            { Origin = CaptureAttachmentOrigin.SavedLibrary }, Path.Combine(fixture.Provider, "legacy.mp4"));
        var extended = await fixture.Store.SaveAsync(source, default);
        Check((await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 1, "Modern query lost an owned inspected recording");
        var legacy = await fixture.Store.ListAsync(0, default);
        Check(legacy.TotalCount == 0 && legacy.Items.Count == 0, "Legacy page returned metadata rejected by pre-114 clients");
        try { WindowCaptureCapabilityDomain.LegacyItem(extended); throw new Exception("Legacy save response returned new-only video metadata"); }
        catch (BrokerException error) when (error.Code == "capture_library_client_upgrade_required") { }
        var consent = new ConsentStore(Path.Combine(fixture.Root, "consent"));
        foreach (var permission in new[] { PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1 })
            await consent.SetDecisionAsync(Owner, permission, ConsentDecision.Grant);
        var simulator = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulator, simulator, captureLibrary: fixture.Store);
        await using var broker = new PlatformCapabilityBroker(Owner, [PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive); long sequence = 0;
        Task<JsonElement> Call(string capability, string operation, object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Owner, capability, operation, BrokerJson.ToElement(payload)));
        var opened = BrokerJson.ParsePayload<CaptureAttachment>(await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.open", new CaptureLibraryOpenRequest(extended.Id)));
        var current = BrokerJson.ParsePayload<CaptureLibraryItem>(await Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save-current", new CaptureLibrarySaveRequest(opened)));
        Check(current.Id == extended.Id && current.Media.DurationSeconds == 6 && current.Media.Properties is null,
            "Current save rejected deduplicated modern owned video");
        await Reject(() => Call(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save", new CaptureLibrarySaveRequest(opened)), "capture_library_client_upgrade_required");
        var compatible = await fixture.Store.SaveAsync(source with { Attachment = source.Attachment with { DurationSeconds = 5 } }, default);
        Check((await fixture.Store.ListAsync(0, default)).Items.Single().Id == compatible.Id, "Compatible legacy video was hidden with extended videos");
        _ = WindowCaptureCapabilityDomain.LegacyItem(compatible);
    }
    internal static async Task ReviewedOwnerLifecycle()
    {
        using var fixture = new Fixture(); fixture.Write("first.png");
        var source = await fixture.Store.AddSourceAsync(fixture.Provider, "Provider", Owner, default);
        var replacement = Owner with { PublisherId = "widgetrail.reviewed", InstanceId = "next" };
        var unrelated = Owner with { InstanceId = "unrelated" };
        var root = Path.Combine(fixture.Root, "index");
        Check((await fixture.Store.SourcesAsync(replacement, default)).Items.Count == 0, "Unreviewed instance inherited source access");
        await CaptureLibraryStore.KeepReviewedSourceAccessAsync(root, Owner, replacement, default);
        Check((await fixture.Store.SourcesAsync(replacement, default)).Items.Single().Id == source.Id, "Reviewed successor lost source grant");
        Check((await fixture.Store.SourcesAsync(unrelated, default)).Items.Count == 0, "Source access widened to another instance");
        await CaptureLibraryStore.RetireSourceOwnersAsync(root, [Owner], default);
        Check((await fixture.Store.SourcesAsync(Owner, default)).Items.Count == 0, "Retired installation retained source access");
        Check((await fixture.Store.SourcesAsync(replacement, default)).Items.Single().Id == source.Id, "Removing old installation revoked current successor");
        fixture.Write("after-update.png");
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 2);
        Check((await fixture.Store.QueryAsync(new(new()), default)).Items.All(i => i.Origin?.PublisherId == Owner.PublisherId), "Successor access rewrote original grant provenance");
        await CaptureLibraryStore.RetireSourceOwnersAsync(root, [replacement], default);
        Check((await fixture.Store.SourcesAsync(replacement, default)).Items.Count == 0, "Last owner retirement kept inaccessible active source");
        await Eventually(async () => (await fixture.Store.QueryAsync(new(new()), default)).TotalCount == 0);
        var fresh = await fixture.Store.AddSourceAsync(fixture.Provider, "Reinstalled", replacement, default);
        Check(fresh.Id != source.Id, "Reinstall silently regained retired grant");
        Check(File.Exists(Path.Combine(fixture.Provider, "first.png")), "Owner retirement deleted original media");
    }
    internal static async Task PickerLifetimeAndSdk()
    {
        using var cancel = new CancellationTokenSource();
        var choosing = CaptureLibrarySourceRequests.ChooseAsync(Owner, "Capture folder", cancel.Token);
        var old = CaptureLibrarySourceRequests.Pending!; cancel.Cancel();
        try { await choosing; throw new Exception("Picker ignored cancellation"); } catch (OperationCanceledException) { }
        var next = CaptureLibrarySourceRequests.ChooseAsync(Owner, "Capture folder", default);
        var current = CaptureLibrarySourceRequests.Pending!;
        CaptureLibrarySourceRequests.Complete(old.Id, Path.GetTempPath());
        Check(!next.IsCompleted, "Stale picker chose a new source");
        CaptureLibrarySourceRequests.Complete(current.Id, null); Check(await next is null, "Picker cancellation created a grant");
        var services = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibrarySourceCapabilities.Choose,
            (_, _) => ValueTask.FromResult(new CaptureLibrarySourceChoice(null))).Build();
        Check(await services.CaptureLibrary.ChooseSourceAsync("Capture folder") is null, "SDK did not preserve picker cancellation");
        Check(new CaptureAttachment(new string('a', 32), "video/mp4", 1920, 1080, 16 * 1024 * 1024, 60, long.MaxValue)
            { Origin = CaptureAttachmentOrigin.SavedLibrary }.IsWellFormed(), "Imported facts require invented encoder telemetry");
        Check(!new CaptureAttachment(new string('a', 32), "video/mp4", 1920, 1080, 16 * 1024 * 1024, 60, long.MaxValue).IsWellFormed(), "Native capture validation was relaxed");
    }
    internal static async Task Authority()
    {
        using var fixture = new Fixture();
        var consent = new ConsentStore(Path.Combine(fixture.Root, "consent"));
        var simulation = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: fixture.Store);
        await using var broker = new PlatformCapabilityBroker(Owner, [PlatformCapabilities.CaptureLibraryManageV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        long sequence = 0;
        Task<System.Text.Json.JsonElement> Choose() => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Owner,
            PlatformCapabilities.CaptureLibraryManageV1, "capture-library.choose-source", BrokerJson.ToElement(new CaptureLibraryChooseSourceRequest("Provider"))));
        try { await Choose(); throw new Exception("Unconsented source picker accepted"); } catch (BrokerException) { }
        Check(CaptureLibrarySourceRequests.Pending is null, "Unconsented request opened a source picker");
        await consent.SetDecisionAsync(Owner, PlatformCapabilities.CaptureLibraryManageV1, ConsentDecision.Grant);
        var choosing = Choose();
        await Eventually(() => Task.FromResult(CaptureLibrarySourceRequests.Pending is not null));
        var pending = CaptureLibrarySourceRequests.Pending!;
        broker.SetLifecycle(BrokerLifecycleState.Background);
        CaptureLibrarySourceRequests.Complete(pending.Id, fixture.Provider);
        var chosen = BrokerJson.ParsePayload<CaptureLibrarySourceChoice>(await choosing);
        Check(chosen.Source?.CanonicalFolderPath == fixture.Provider, "Admitted native picker lost its in-flight authorization");
        try { await Choose(); throw new Exception("Background source picker accepted"); } catch (BrokerException) { }
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        var revoked = Choose();
        await Eventually(() => Task.FromResult(CaptureLibrarySourceRequests.Pending is not null));
        await consent.SetDecisionAsync(Owner, PlatformCapabilities.CaptureLibraryManageV1, ConsentDecision.Deny);
        await broker.RefreshConsentAsync();
        try { await revoked; throw new Exception("Revoked source picker accepted"); } catch (BrokerException) { } catch (OperationCanceledException) { }
        Check(CaptureLibrarySourceRequests.Pending is null, "Permission revocation left source picker authority");
    }
    private static async Task Eventually(Func<Task<bool>> condition)
    { for (var i = 0; i < 70; i++) { if (await condition()) return; await Task.Delay(150); } throw new Exception("Source reconciliation timed out"); }
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "widgetrail-sources-" + Guid.NewGuid().ToString("N"));
        internal string Provider => Path.Combine(Root, "provider");
        internal string Public => Path.Combine(Root, "owned");
        internal byte[] Bytes { get; } = [137, 80, 78, 71, 13, 10, 26, 10, 0, 1, 2, 3];
        internal CaptureLibraryStore Store;
        internal Fixture() { Directory.CreateDirectory(Provider); Directory.CreateDirectory(Public); Store = CreateStore(); }
        internal CaptureLibraryStore CreateStore() => new(Path.Combine(Root, "index"), Public, null, null,
            (path, seed, _) => Task.FromResult(seed with { Width = 16, Height = 16, ByteLength = new FileInfo(path).Length }),
            (_, _) => Task.FromException<bool>(new Exception("Provider recycle must never be called")));
        internal void Write(string relative)
        { var path = Path.Combine(Provider, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, Bytes); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10)); }
        public void Dispose() { Store.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
