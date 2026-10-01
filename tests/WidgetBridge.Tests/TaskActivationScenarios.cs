using WidgetRail.WidgetBridge;
using WidgetRail.WindowsWindowActivation;

internal static class TaskActivationScenarios
{
    internal static Task TicketsAndWire()
    {
        var tickets = new BridgeTaskActivationTickets();
        var ticket = new BridgeTaskActivationTickets.Ticket("widget", "fingerprint", "instance", "runtime", 1000,
            new TaskWindowTarget(42, 51, 1234, "TestWindow"), new(1, 1));
        var request = new BridgeTaskActivationRequest("widget", "runtime", 1);
        tickets.Offer(1, ticket, 1000);
        Check(tickets.Take(request, 3000) == ticket, "Boundary-fresh ticket must retain target identity.");
        Check(tickets.Take(request, 3000) is null, "Completion must not replay.");
        foreach (var invalid in new[] { request with { WidgetId = "other" }, request with { RuntimeGeneration = "other" } })
        {
            tickets.Offer(1, ticket, 1000);
            Check(tickets.Take(invalid, 1000) is null, "Cross-owner completion must fail.");
            Check(tickets.Take(request, 1000) is null, "Rejected attempt must consume ticket.");
        }
        tickets.Offer(1, ticket, 1000);
        Check(tickets.Take(request, 3001) is null, "Expired ticket must fail.");
        tickets.Offer(1, ticket, 999);
        Check(tickets.Count == 0, "Future tickets must not enter store.");
        for (var sequence = 1; sequence <= 65; sequence++) tickets.Offer(sequence, ticket, 1000);
        Check(tickets.Count == 64 && tickets.Take(request, 1000) is null, "Tickets must be bounded, oldest evicted.");
        tickets.Clear();
        tickets.Offer(1, ticket, 1000);
        var successes = 0;
        Parallel.For(0, 32, _ => { if (tickets.Take(request, 1000) is not null) Interlocked.Increment(ref successes); });
        Check(successes == 1, "Concurrent completion must execute once.");
        tickets.Offer(1, ticket, 1000);
        tickets.Offer(2, ticket with { InitiatedAt = 3001 }, 3001);
        Check(tickets.Count == 1, "New offers must prune expired tickets.");
        tickets.Clear();
        Check(tickets.Count == 0, "Session teardown must discard pending activation.");
        BridgeRequestKey Classify(object payload) => BridgeRequestClassifier.Classify(new()
            { Type = BridgeMessageTypes.CompleteTaskActivation, RequestId = 1, Payload = BridgeJson.ToElement(payload) });
        var key = Classify(request);
        Check(key.Kind == BridgeRequestKind.CompleteTaskActivation && key.WidgetId == "widget" && !key.IsIndependent,
            "Completion must use the widget serialized lane.");
        Check(Classify(request with { Sequence = 0 }).Kind == BridgeRequestKind.Malformed, "Sequence must be positive.");
        Check(Classify(request with { RuntimeGeneration = "" }).Kind == BridgeRequestKind.Malformed, "Runtime identity required.");
        Check(Classify(new { widgetId = "widget", runtimeGeneration = "runtime", sequence = 1, hwnd = 42 }).Kind == BridgeRequestKind.Malformed,
            "Completion cannot inject a target HWND.");
        var response = new BridgeTaskActivationResponse("Denied", "windows-denied", 123, "trace");
        Check(BridgeJson.FromElement<BridgeTaskActivationResponse>(BridgeJson.ToElement(response)) == response,
            "Completion wire must preserve outcome and broker diagnostics.");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
