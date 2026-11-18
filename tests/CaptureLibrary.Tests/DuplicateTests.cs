using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class DuplicateTests
{
    internal static async Task LinkedVideoCopyAndCancellation()
    {
        using var f = new Fixture();
        var provider = Directory.CreateDirectory(Path.Combine(f.Root, "provider")).FullName;
        var path = Path.Combine(provider, "long.mp4");
        var bytes = new byte[1024 * 1024]; bytes[0] = 42;
        await File.WriteAllBytesAsync(path, bytes);
        using var store = new CaptureLibraryStore(Path.Combine(f.Root, "linked-index"), Path.Combine(f.Root, "owned"), null, null,
            inspect: (file, media, _) => Task.FromResult(media with { Width = 1920, Height = 1080, DurationSeconds = 7200, ByteLength = new FileInfo(file).Length }));
        var source = await store.AddSourceAsync(provider, "External clips", Fixture.Identity, default);
        var original = (await store.LinkAsync(new(source.Id, "long.mp4"), Fixture.Identity, default)).Item;
        var copy = await store.SaveCopyAsync(new(original.Id, original.Revision, original.MetadataRevision), Fixture.Identity, default);
        Check(copy.SourceId is null && copy.Media.DurationSeconds == 7200 && File.ReadAllBytes(path).SequenceEqual(bytes), "Long linked copy was encoded, limited like a trim, or changed its original");
        await using var opened = await store.OpenAsync(copy.Id, CaptureImageResolution.Original, default);
        Check(opened.Stream.Length == bytes.Length, "Copied video cannot open directly");
        using var stopped = new CancellationTokenSource();
        var large = Path.Combine(provider, "large.mp4");
        await using (var file = File.Create(large)) file.SetLength(256 * 1024 * 1024);
        var next = (await store.LinkAsync(new(source.Id, "large.mp4"), Fixture.Identity, default)).Item;
        var saving = store.SaveCopyAsync(new(next.Id, next.Revision, next.MetadataRevision), Fixture.Identity, stopped.Token);
        stopped.Cancel();
        try { await saving; throw new Exception("Cancelled save completed"); } catch (OperationCanceledException) { }
        Check((await store.QueryAsync(new(new()), default)).TotalCount == 3, "Cancellation published an extra capture");
        var staging = Path.Combine(f.Root, "owned", ".widgetrail-edits");
        Check(!Directory.Exists(staging) || !Directory.EnumerateFiles(staging).Any(), "Cancelled save left partial output");
    }

    internal static async Task FilesAndContexts()
    {
        using var f = new Fixture();
        var context = new CaptureSaveContext("source.event", [new(CaptureContextReferenceRole.Event, "event", "unlock", "Achievement")], 1000);
        var original = (await f.Store.SaveWithContextAsync(f.Source, Fixture.Identity, context, default)).Item!;
        original = await f.Store.UpdateMetadataAsync(new(original.Id, original.MetadataRevision, "Boss fight", true), default);
        var reference = new CaptureLibraryMutationReference(original.Id, original.Revision, original.MetadataRevision);
        var copy = await f.Store.SaveCopyAsync(reference, Fixture.Identity, default);
        var again = await f.Store.SaveCopyAsync(reference, Fixture.Identity, default);
        Check(copy.Id != original.Id && again.Id != copy.Id && copy.SourceId is null, "Copies were deduplicated or remained linked");
        Check(File.ReadAllBytes(f.MediaPath(copy.Id)).SequenceEqual(f.Bytes) && File.ReadAllBytes(f.MediaPath(original.Id)).SequenceEqual(f.Bytes), "Copy modified original bytes");
        Check(copy.Title == "Boss fight (copy)" && !copy.IsFavorite && copy.ContextCount == 1, "Copy lost its title or related context");
        Check((await f.Store.QueryAsync(new(new()), default)).TotalCount == 3, "Saved copies were not indexed");
        await f.Store.RecoverPendingEditsAsync(default);
        Check((await f.Store.GetMetadataAsync(copy.Id, default)).ContextCount == 1, "Recovery lost a committed copy");
        var changed = await f.Store.UpdateMetadataAsync(new(original.Id, original.MetadataRevision, "Changed", true), default);
        try { await f.Store.SaveCopyAsync(reference, Fixture.Identity, default); throw new Exception("Stale copy accepted"); }
        catch (BrokerException error) when (error.Code == "capture_library_metadata_conflict") { }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await f.Store.SaveCopyAsync(new(changed.Id, changed.Revision, changed.MetadataRevision), Fixture.Identity, cancelled.Token); throw new Exception("Cancelled copy accepted"); }
        catch (OperationCanceledException) { }
        Check((await f.Store.QueryAsync(new(new()), default)).TotalCount == 3 && CaptureLibraryCopyRequests.Pending is null, "Failure published a partial copy or opened a picker");
    }

    internal static async Task SdkAndAdmission()
    {
        using var f = new Fixture();
        var original = await f.Store.SaveAsync(f.Source, default);
        var reference = new CaptureLibraryMutationReference(original.Id, original.Revision, original.MetadataRevision);
        var copy = await f.Store.SaveCopyAsync(reference, Fixture.Identity, default);
        var service = new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.SaveCopy, copy).Build();
        Check((await service.CaptureLibrary.SaveCopyAsync(reference)).Id == copy.Id, "SDK rejected independently named copy");
        var malformed = new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.SaveCopy, original).Build();
        try { await malformed.CaptureLibrary.SaveCopyAsync(reference); throw new Exception("SDK accepted original as copy"); }
        catch (WidgetCapabilityException) { }
        var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
        var simulation = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: f.Store);
        await using var broker = new PlatformCapabilityBroker(Fixture.Identity, [PlatformCapabilities.CaptureLibraryManageV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        long sequence = 0;
        Task<System.Text.Json.JsonElement> Save() => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Fixture.Identity,
            PlatformCapabilities.CaptureLibraryManageV1, "capture-library.save-copy", BrokerJson.ToElement(reference)));
        try { await Save(); throw new Exception("Unconsented copy accepted"); } catch (BrokerException) { }
        await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryManageV1, ConsentDecision.Grant);
        var saved = BrokerJson.ParsePayload<CaptureLibraryItem>(await Save());
        Check(saved.Id != original.Id && CaptureLibraryCopyRequests.Pending is null, "Save copy invoked folder picker");
        broker.SetLifecycle(BrokerLifecycleState.Background);
        try { await Save(); throw new Exception("Background copy admitted"); } catch (BrokerException) { }
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
}
