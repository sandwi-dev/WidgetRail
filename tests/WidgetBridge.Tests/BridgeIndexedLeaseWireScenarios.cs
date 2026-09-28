using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

internal static class BridgeIndexedLeaseWireScenarios
{
    private static readonly string LeaseId = Guid.NewGuid().ToString("N");
    private static readonly BridgeIndexedRangeRequest RangeRequest = new("widget", "instance", "runtime", "presentation",
        new("collection", new("source", 1, 0, 100), 0, 2, "range-demand"));
    private static readonly BridgeIndexedArtworkRequest ArtworkRequest = new("widget", "instance", "runtime", "presentation",
        new(LeaseId, "item"), "artwork", "artwork-demand");

    internal static Task ContractsAndClassification()
    {
        var finiteRange = BridgeJson.ToElement(RangeRequest.Range);
        Check(!finiteRange.TryGetProperty("kind", out _) && !finiteRange.GetProperty("source").TryGetProperty("discovery", out _),
            "Existing finite range wire shape must not acquire continuation-only fields.");
        var continuation = BridgeJson.ToElement(RangeRequest.Range with { Kind = IndexedCollectionRequestKind.Continue });
        Check(continuation.GetProperty("kind").GetString() == "continue", "Continuation commands must retain their explicit kind.");
        Check(BridgeJson.FromElement<IndexedCollectionRangeRequest>(finiteRange).Kind == IndexedCollectionRequestKind.Range,
            "An omitted request kind must retain the finite range default.");
        var lease = new BridgeIndexedLeaseRequest("widget", "instance", "runtime", "presentation", LeaseId);
        var input = new BridgeIndexedInputRequest("widget", "instance", "runtime", "presentation",
            new(new(LeaseId, "item"), ControllerButton.A), new("scope", 1, 2, 300));
        var cases = new (string Message, object Payload, BridgeRequestKind Kind, bool Independent)[]
        {
            (BridgeMessageTypes.AcquireIndexedRange, RangeRequest, BridgeRequestKind.AcquireIndexedRange, true),
            (BridgeMessageTypes.ReleaseIndexedLease, lease, BridgeRequestKind.ReleaseIndexedLease, true),
            (BridgeMessageTypes.IndexedInput, input, BridgeRequestKind.IndexedInput, false),
            (BridgeMessageTypes.ResolveIndexedArtwork, ArtworkRequest, BridgeRequestKind.ResolveIndexedArtwork, true),
            (BridgeMessageTypes.CancelIndexedArtwork, ArtworkRequest, BridgeRequestKind.CancelIndexedArtwork, true),
        };
        foreach (var test in cases)
        {
            var key = Classify(test.Message, test.Payload);
            Check(key.Kind == test.Kind && key.IsIndependent == test.Independent && key.WidgetId == "widget", "Lease request classification lost its scheduling authority.");
            var forged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(BridgeJson.ToElement(test.Payload).GetRawText())!;
            forged["privateProviderData"] = JsonSerializer.SerializeToElement("not allowed");
            Check(Classify(test.Message, forged).Kind == BridgeRequestKind.Malformed, "Unexpected private fields must fail closed.");
            forged.Remove("privateProviderData"); forged["runtimeGeneration"] = JsonSerializer.SerializeToElement("");
            Check(Classify(test.Message, forged).Kind == BridgeRequestKind.Malformed, "Missing generation must fail closed.");
        }
        Check(Classify(BridgeMessageTypes.IndexedInput, input with { Context = new("scope", 0) }).Kind == BridgeRequestKind.Malformed,
            "Input must have positive parent snapshot correlation.");
        Check(Classify(BridgeMessageTypes.ReleaseIndexedLease, lease with { LeaseId = "not-a-lease" }).Kind == BridgeRequestKind.Malformed,
            "Release must identify an exact opaque lease.");
        Check(Classify(BridgeMessageTypes.ResolveIndexedArtwork, ArtworkRequest with { DemandId = new string('a', 65) }).Kind == BridgeRequestKind.Malformed,
            "Artwork demand IDs are bounded independently from handles.");
        var response = new BridgeIndexedArtworkResponse("widget", "instance", "runtime", "presentation", ArtworkRequest.Item,
            "artwork", "artwork-demand", "image/png", new byte[] { 1, 2, 3 });
        var encoded = BridgeJson.ToElement(response);
        Check(encoded.GetProperty("contentBase64").GetString() == "AQID", "Encoded bytes must use base64 wire representation.");
        Check(BridgeJson.FromElement<BridgeIndexedArtworkResponse>(encoded).ContentBase64.Span.SequenceEqual(new byte[] { 1, 2, 3 }),
            "Artwork wire roundtrip must preserve bytes.");
        var unavailable = BridgeJson.ToElement(response with { ContentType = "", ContentBase64 = ReadOnlyMemory<byte>.Empty });
        Check(unavailable.GetProperty("contentType").GetString() == "" && unavailable.GetProperty("contentBase64").GetString() == "",
            "Unavailable artwork has explicit empty type and bytes.");
        var unhandled = BridgeJson.ToElement(new Dictionary<string, string?> { ["admission"] = null });
        Check(unhandled.TryGetProperty("admission", out var admission) && admission.ValueKind == JsonValueKind.Null,
            "Unhandled input must retain an explicit null admission field.");
        return Task.CompletedTask;
    }

    internal static async Task CancellationWaitsOnlyForExactAdmissionAndInputRetainsSerialOrdering()
    {
        await using var dispatcher = new BridgeRequestDispatcher(CancellationToken.None, error => throw error);
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = dispatcher.TryDispatch(1, Classify(BridgeMessageTypes.ResolveIndexedArtwork, ArtworkRequest), token =>
        { providerEntered.SetResult(); return releaseProvider.Task.WaitAsync(token); });
        var cancel = dispatcher.TryDispatch(2, Classify(BridgeMessageTypes.CancelIndexedArtwork, ArtworkRequest), _ =>
        {
            Check(providerEntered.Task.IsCompleted, "Cancel overtook the matching artwork admission.");
            Check(!releaseProvider.Task.IsCompleted, "Cancel incorrectly waited for provider completion.");
            cancelEntered.SetResult(); return Task.CompletedTask;
        });
        await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ordinary = dispatcher.TryDispatch(3, BridgeRequestKey.Widget(BridgeRequestKind.Action, "widget"), token => releaseAction.Task.WaitAsync(token));
        var input = new BridgeIndexedInputRequest("widget", "instance", "runtime", "presentation",
            new(new(LeaseId, "item"), ControllerButton.A), new("scope", 1));
        var inputEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var indexed = dispatcher.TryDispatch(4, Classify(BridgeMessageTypes.IndexedInput, input), _ =>
        { Check(releaseAction.Task.IsCompleted, "Indexed input bypassed preceding ordinary widget action."); inputEntered.SetResult(); return Task.CompletedTask; });
        Check(!inputEntered.Task.IsCompleted, "Input must remain in the normal serial widget lane.");
        releaseAction.SetResult(); await inputEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        releaseProvider.SetResult();
        await Task.WhenAll(read.Completion!, cancel.Completion!, ordinary.Completion!, indexed.Completion!).WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static BridgeRequestKey Classify(string message, object payload) =>
        BridgeRequestClassifier.Classify(new() { Type = message, RequestId = 1, Payload = BridgeJson.ToElement(payload) });
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
