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
