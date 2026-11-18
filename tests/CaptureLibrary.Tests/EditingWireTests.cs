using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class EditingWireTests
{
    private static readonly HostCaptureEditRecipe Crop = new(new(0, 0, 0, 50, 60));
    internal static async Task CompletionAndCancel()
    {
        foreach (var save in new[] { false, true })
        {
            using var f = new Fixture(); using var store = Store(f);
            var original = await f.Store.SaveAsync(f.Source, default);
            CaptureLibraryEditRequests.Configure(Write, (_, _) => Task.CompletedTask, (input, _) => Task.FromResult(new CaptureEditPreview(new Uri(input.Path).AbsoluteUri)));
            var pending = CaptureLibraryEditRequests.RunAsync(Fixture.Identity, new(Saved: Reference(original)), store, WindowCaptureRegistry.Shared, new object(), default);
            var request = await Ready();
            Check(CaptureLibraryEditRequests.Status(request.Id).Stage == HostCaptureEditStage.Editing, "Editor admission missing.");
            Check(!CaptureLibraryEditRequests.Complete(request.Id, new(new(0, 0, 0, 100, 100))), "No-op edit accepted.");
            if (save)
            {
                Check(CaptureLibraryEditRequests.Complete(request.Id, Crop), "Save was not accepted.");
                var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Check(result.Outcome == CaptureLibraryEditOutcome.Saved && result.Item?.Id == request.ResultId && result.Item.Id != original.Id, "Saved transaction lost exact result identity.");
                var cancelled = CaptureLibraryEditRequests.Cancel(request.Id);
                Check(cancelled.Stage == HostCaptureEditStage.Saved && cancelled.Item?.Id == result.Item!.Id, "Late cancel hid committed copy.");
                Check(!CaptureLibraryEditRequests.Complete(request.Id, Crop), "Completed editor accepted another encode.");
            }
            else
            {
                Check(CaptureLibraryEditRequests.Cancel(request.Id).Stage == HostCaptureEditStage.Cancelled, "Explicit cancel not terminal.");
                Check((await pending).Outcome == CaptureLibraryEditOutcome.Cancelled, "Explicit cancel did not reach SDK result.");
            }
            Check((await store.QueryAsync(new(new()), default)).TotalCount == (save ? 2 : 1), "Editing changed original or created unwanted copy.");
        }
    }
    internal static async Task CancelDuringProgress()
    {
        using var f = new Fixture(); using var store = Store(f);
        var original = await f.Store.SaveAsync(f.Source, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CaptureLibraryEditRequests.Configure(async (input, recipe, output, progress, token) =>
        {
            await Write(input, recipe, output, progress, token); progress!.Report(.5); entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }, (_, _) => Task.CompletedTask, (input, _) => Task.FromResult(new CaptureEditPreview(new Uri(input.Path).AbsoluteUri)));
        var pending = CaptureLibraryEditRequests.RunAsync(Fixture.Identity, new(Saved: Reference(original)), store, WindowCaptureRegistry.Shared, new object(), default);
        var request = await Ready(); Check(CaptureLibraryEditRequests.Complete(request.Id, Crop), "Save not accepted.");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(CaptureLibraryEditRequests.Complete(request.Id, Crop), "Idempotent same-recipe admission failed.");
        Check(!CaptureLibraryEditRequests.Complete(request.Id, new(new(1, 0, 0, 50, 60))), "Changed duplicate recipe accepted.");
        Check(CaptureLibraryEditRequests.Status(request.Id) is { Stage: HostCaptureEditStage.Saving, Progress: .5 }, "Progress missing.");
        await Task.WhenAll(Task.Run(() => CaptureLibraryEditRequests.Cancel(request.Id)), Task.Run(() => CaptureLibraryEditRequests.Status(request.Id))).WaitAsync(TimeSpan.FromSeconds(3));
        Check((await pending.WaitAsync(TimeSpan.FromSeconds(3))).Outcome == CaptureLibraryEditOutcome.Cancelled, "Cancel stalled or fabricated a saved result.");
        Check((await store.QueryAsync(new(new()), default)).TotalCount == 1, "Cancelled encode published a copy.");
    }
    internal static async Task DualConsent()
    {
        foreach (var revoke in new[] { PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1 })
        {
            using var f = new Fixture(); using var store = Store(f);
            var original = await f.Store.SaveAsync(f.Source, default);
            var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
            await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibrarySaveV1, ConsentDecision.Grant);
            var simulation = new SimulatedPlatformBrokerBackend();
            await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: store);
            await using var broker = new PlatformCapabilityBroker(Fixture.Identity,
                [PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1], consent, backend);
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            long sequence = 0;
            Task<JsonElement> Invoke() => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Fixture.Identity,
                PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.edit-copy", BrokerJson.ToElement(new CaptureLibraryEditRequest(Saved: Reference(original)))));
            await Reject("permission_denied", Invoke);
            Check(CaptureLibraryEditRequests.Pending is null, "Missing read consent still opened editor.");
            await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Grant); await broker.RefreshConsentAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CaptureLibraryEditRequests.Configure(async (input, recipe, output, progress, token) =>
            { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }, (_, _) => Task.CompletedTask,
                (input, _) => Task.FromResult(new CaptureEditPreview(new Uri(input.Path).AbsoluteUri)));
            var pending = Invoke(); var request = await Ready();
            Check(CaptureLibraryEditRequests.SourcePermission(request.Id) == PlatformCapabilities.CaptureLibraryReadV1, "Saved source consent route changed.");
            CaptureLibraryEditRequests.Complete(request.Id, Crop); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await consent.SetDecisionAsync(Fixture.Identity, revoke, ConsentDecision.Deny); await broker.RefreshConsentAsync();
            await Reject("capability_revoked", async () => await pending.WaitAsync(TimeSpan.FromSeconds(3)));
            Check((await store.QueryAsync(new(new()), default)).TotalCount == 1, "Revocation committed an edit.");
        }
    }
    private static CaptureLibraryStore Store(Fixture f) => new(f.Library, f.Exports, null, null,
        (path, previous, _) => Task.FromResult(previous with { Width = 50, Height = 60, ByteLength = new FileInfo(path).Length, Properties = null }));
    private static CaptureLibraryMutationReference Reference(CaptureLibraryItem item) => new(item.Id, item.Revision, item.MetadataRevision);
    private static Task Write(CaptureEditInput input, CaptureEditRecipe recipe, Stream output, IProgress<double>? progress, CancellationToken token) =>
        output.WriteAsync(new byte[] { 137,80,78,71,13,10,26,10,1,2,3,4,5 }, token).AsTask();
    private static async Task<HostCaptureEditRequest> Ready()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (CaptureLibraryEditRequests.Pending is null) await Task.Delay(10, timeout.Token);
        return CaptureLibraryEditRequests.Pending!;
    }
    private static async Task Reject(string code, Func<Task> action)
    { try { await action(); } catch (BrokerException error) when (error.Code == code) { return; } throw new InvalidOperationException("Expected " + code); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
