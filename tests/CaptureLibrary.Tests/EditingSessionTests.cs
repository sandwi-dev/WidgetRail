using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class EditingSessionTests
{
    private static readonly CaptureEditRecipe Crop = new(new(0, 0, 0, 50, 60));
    private static CaptureLibraryStore Store(Fixture f) => new(f.Library, f.Exports, null, null,
        (path, previous, _) => Task.FromResult(previous with { Width = 50, Height = 60, ByteLength = new FileInfo(path).Length, Properties = null }));
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(string code, Func<Task> action)
    { try { await action(); } catch (BrokerException error) when (error.Code == code) { return; } throw new InvalidOperationException("Expected " + code); }
    private static Task Write(CaptureEditInput input, CaptureEditRecipe recipe, Stream output, IProgress<double>? progress, CancellationToken token) =>
        output.WriteAsync(new byte[] { 137,80,78,71,13,10,26,10,1,2,3,4,5 }, token).AsTask();

    internal static async Task RetainedSourceAndReceipt()
    {
        using var f = new Fixture(); using var store = Store(f); var original = await f.Store.SaveAsync(f.Source, default);
        using var session = await store.OpenEditSessionAsync(new CaptureLibraryMutationReference(original.Id, original.Revision, original.MetadataRevision), Fixture.Identity, () => true, default);
        Stream retained;
        using (var preview = session.AcquireInput()) retained = preview.Input.Stream;
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource(); var encodes = 0;
        CaptureEditCodec codec = async (input, recipe, output, progress, token) =>
        { Check(ReferenceEquals(retained, input.Stream), "Save reopened source instead of retaining verified preview handle"); ++encodes; entered.SetResult(); await release.Task.WaitAsync(token); await Write(input, recipe, output, progress, token); };
        var first = session.SaveAsync(Crop, codec, null, default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var repeated = session.SaveAsync(Crop, codec, null, default); Check(ReferenceEquals(first, repeated), "Repeated completion did not share one save");
        await Reject("capture_library_edit_already_saving", () => session.SaveAsync(new(new(1,0,0,50,60)), codec, null, default));
        release.SetResult(); var saved = await first;
        Check(saved.Id == session.ResultId && encodes == 1, "Save identity changed or encoder ran twice");
        Check((await store.RecoverEditResultAsync(session.ResultId, Fixture.Identity, default))?.Id == saved.Id, "Lost response could not recover exact committed result");
        await Reject("capture_library_edit_invalid_receipt", () => store.RecoverEditResultAsync(session.ResultId, Fixture.Identity with { InstanceId = "foreign" }, default));
        await store.DeleteAsync(saved.Id, default);
        await Reject("capture_library_edit_result_removed", () => store.RecoverEditResultAsync(session.ResultId, Fixture.Identity, default));
        Check((await session.SaveAsync(Crop, codec, null, default)).Id == saved.Id && encodes == 1, "Removed completed output was encoded again");
    }
    internal static async Task RecoverCommittedBeforeReceiptReply()
    {
        using var f = new Fixture(); using var store = Store(f); var original = await f.Store.SaveAsync(f.Source, default);
        using var session = await store.OpenEditSessionAsync(new CaptureLibraryMutationReference(original.Id, original.Revision, original.MetadataRevision), Fixture.Identity, () => true, default);
        var receipt = Path.Combine(f.Library, "edits", "receipt-" + session.ResultId + ".json"); Directory.CreateDirectory(receipt);
        try { await session.SaveAsync(Crop, Write, null, default); throw new InvalidOperationException("Blocked receipt publication unexpectedly completed"); }
        catch (IOException) { }
        Check(File.Exists(Path.Combine(f.Library, session.ResultId + ".json")), "Fixture did not interrupt after output commit");
        Directory.Delete(receipt);
        using var reopened = Store(f); await reopened.RecoverPendingEditsAsync(default);
        var recovered = await reopened.RecoverEditResultAsync(session.ResultId, Fixture.Identity, default);
        Check(recovered?.Id == session.ResultId && (await reopened.QueryAsync(new(new()), default)).TotalCount == 2,
            "Recovery lost committed output or generated a duplicate");
        Check(File.Exists(receipt), "Recovery did not finalize the durable receipt");
    }
    internal static async Task TemporarySourceAndRetirement()
    {
        using var f = new Fixture(); using var store = Store(f); using var registry = new WindowCaptureRegistry(); var owner = new object();
        var ticket = registry.Enqueue(owner, Fixture.Identity, WindowCaptureKind.Screenshot); var capture = registry.Take()!;
        await File.WriteAllBytesAsync(capture.OutputPath, f.Bytes); registry.Complete(new(ticket.RequestId, 100, 100, 0));
        var attachment = registry.Status(owner, ticket.RequestId).Attachment!;
        await Reject("capture_unavailable", () => { using var ignored = registry.AcquireEditLease(new object(), attachment); return Task.CompletedTask; });
        await Reject("capture_unavailable", () => { using var ignored = registry.AcquireEditLease(owner, attachment with { Width = 99 }); return Task.CompletedTask; });
        using var authenticated = registry.AcquireEditLease(owner, attachment);
        using var session = await store.OpenEditSessionAsync(authenticated, Fixture.Identity, () => true, default);
        authenticated.Dispose(); // Ownership transferred; disposing the wrapper must not close the session.
        Check(session.IsCurrent && (await store.QueryAsync(new(new()), default)).TotalCount == 0, "Temporary edit forced an original library save");
        var saved = await session.SaveAsync(Crop, Write, null, default);
        Check((await store.QueryAsync(new(new()), default)).TotalCount == 1 && saved.Id == session.ResultId && File.Exists(capture.OutputPath), "Temporary source was saved/removed without widget intent");
        session.Dispose();
        using var nextLease = registry.AcquireEditLease(owner, attachment);
        using var next = await store.OpenEditSessionAsync(nextLease, Fixture.Identity, () => true, default);
        var entered = new TaskCompletionSource(); var cancelled = next.SaveAsync(Crop, async (input, recipe, output, progress, token) =>
        { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }, null, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); registry.Discard(owner, attachment.Id);
        await Reject("capture_library_edit_source_retired", async () => await cancelled.WaitAsync(TimeSpan.FromSeconds(3)));
        next.Dispose(); Check(!File.Exists(capture.OutputPath), "Retired temporary original remained after its last editor lease closed");
    }
}
