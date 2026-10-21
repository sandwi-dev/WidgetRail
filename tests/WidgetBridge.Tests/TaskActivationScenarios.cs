using WidgetRail.WidgetBridge;
using WidgetRail.WindowsWindowActivation;
using System.IO.Pipes;

internal static class TaskActivationScenarios
{
    internal static async Task HostRejectionsThroughPipe()
    {
        var name = "host-task-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new WidgetBridgeServer(name, new([]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await pipe.ConnectAsync(deadline.Token);
            var channel = new BridgeFrameChannel(pipe, BridgeProtocol.DefaultMaximumMessageBytes);
            long id = 0;
            async Task<BridgeEnvelope> Send<T>(string type, T payload)
            {
                var current = ++id;
                await channel.WriteAsync(new() { RequestId = current, Type = type, Payload = BridgeJson.ToElement(payload) }, deadline.Token);
                while (true)
                {
                    var reply = await channel.ReadAsync(deadline.Token);
                    if (reply.RequestId == current) return reply;
                }
            }
            Check((await Send(BridgeMessageTypes.Hello, new BridgeHello("host-task-test"))).Type == BridgeMessageTypes.HelloAccepted,
                "Host handshake failed.");
            // Deliberately invalid identity: these cases never activate a user window.
            var request = new BridgeHostTaskActivationRequest(1, Environment.TickCount64 - 2001, 1, 1, 1, "MissingWindow");
            async Task Expect(BridgeHostTaskActivationRequest value, string code)
            {
                var reply = await Send(BridgeMessageTypes.ActivateHostTaskWindow, value);
                Check(reply.Type == BridgeMessageTypes.ActivateHostTaskWindow, "Host activation reply type changed.");
                var result = BridgeJson.FromElement<BridgeTaskActivationResponse>(reply.Payload);
                Check(result.Result == "Rejected" && result.Code == code && result.ProcessId == Environment.ProcessId,
                    "Host rejection or broker identity was lost across the pipe.");
            }
            await Expect(request, "host-activation-expired");
            await Expect(request with { InitiatedAt = Environment.TickCount64 }, "host-activation-replayed");
            await Expect(request with { Sequence = 2, InitiatedAt = Environment.TickCount64 + 60000 }, "host-activation-expired");
            await Expect(request with { Sequence = 3, InitiatedAt = Environment.TickCount64 }, "window-identity-changed");
            await Expect(request with { Sequence = 2, InitiatedAt = Environment.TickCount64 }, "host-activation-replayed");
            // The preexisting widget endpoint still needs its broker-issued ticket.
            var widget = await Send(BridgeMessageTypes.CompleteTaskActivation, new BridgeTaskActivationRequest("missing", "runtime", 3));
            Check(BridgeJson.FromElement<BridgeTaskActivationResponse>(widget.Payload).Code == "missing-expired-or-consumed-effect",
                "Host activation must not mint widget activation authority.");
            _ = await Send(BridgeMessageTypes.Stop, new BridgeEmptyPayload());
        }
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }

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
        var host = new BridgeHostTaskActivationRequest(1, 1000, 42, 51, 1234, "TestWindow");
        BridgeRequestKey ClassifyHost(BridgeHostTaskActivationRequest value) => BridgeRequestClassifier.Classify(new()
            { Type = BridgeMessageTypes.ActivateHostTaskWindow, RequestId = 2, Payload = BridgeJson.ToElement(value) });
        Check(BridgeJson.FromElement<BridgeHostTaskActivationRequest>(BridgeJson.ToElement(host)) == host,
            "Host activation must preserve the exact native identity.");
        var hostKey = ClassifyHost(host);
        Check(hostKey.Kind == BridgeRequestKind.ActivateHostTaskWindow && hostKey.WidgetId is null,
            "Host activation must not queue behind widget work.");
        foreach (var invalid in new[] { host with { Sequence = 0 }, host with { InitiatedAt = -1 },
            host with { Handle = 0 }, host with { ProcessId = 0 }, host with { ProcessCreated = 0 },
            host with { ClassName = "" }, host with { ClassName = "bad\0class" }, host with { ClassName = new string('x', 256) } })
            Check(ClassifyHost(invalid).Kind == BridgeRequestKind.Malformed, "Invalid host identity must fail before dispatch.");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
