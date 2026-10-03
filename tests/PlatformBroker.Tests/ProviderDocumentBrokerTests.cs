using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class ProviderDocumentBrokerTests
{
    internal static async Task MultilinePipeAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wrail-document-pipe-" + Guid.NewGuid().ToString("N"));
        var identity = new BrokerWidgetIdentity("example.help", "example.publisher", "document.instance");
        var consent = new ConsentStore(folder);
        var capability = PlatformCapabilities.ProviderDocumentsV1;
        await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
        var pipe = "wrail-document-" + Guid.NewGuid().ToString("N");
        var options = new BrokerPipeTransportOptions { RequestTimeout = TimeSpan.FromSeconds(1) };
        await using var server = new BrokerPipeServer(pipe, identity, [capability], consent,
            new SimulatedPlatformBrokerBackend(), options, new string('D', 64));
        var running = server.RunAsync();
        await using var client = new BrokerPipeClient(pipe, identity, server.ChannelNonce, options);
        try
        {
            await client.ConnectAsync();
            server.SetLifecycle(BrokerLifecycleState.Interactive);
            const string html = "<style>\r\n\t.card { color: red; }\r\n</style>\n<div>Guide 日本語 🎮</div>";
            var response = await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentCreate, new ProviderDocumentContent([html]));
            Check(response.Succeeded);
            var reference = BrokerJson.ParsePayload<ProviderDocumentReference>(response.Payload!.Value);
            Check(ProviderDocumentRegistry.Shared.Resolve(identity, reference).Single() == html);
            Check((await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentDiscard, reference)).Succeeded);
            server.SetLifecycle(BrokerLifecycleState.Background);
            response = await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentCreate, new ProviderDocumentContent([html]));
            Check(response.Succeeded);
            var invalid = await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentCreate, new { htmlUtf8Base64 = new[] { "not base64!" } });
            Check(!invalid.Succeeded && invalid.ErrorCode == "invalid_payload");
            var invalidUtf8 = await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentCreate, new { htmlUtf8Base64 = new[] { "/w==" } });
            Check(!invalidUtf8.Succeeded && invalidUtf8.ErrorCode == "invalid_payload");
            var invalidControl = await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentCreate,
                new { htmlUtf8Base64 = new[] { Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<p>\0</p>")) } });
            Check(!invalidControl.Succeeded && invalidControl.ErrorCode == "invalid_payload");
            var legacy = await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentCreate, new { html = new[] { "<p>Earlier SDK</p>" } });
            Check(legacy.Succeeded);
            // The maximum decoded document still fits in the existing authenticated envelope.
            var maximum = new string('x', 64 * 1024);
            response = await client.RequestAsync(capability, PlatformCapabilities.ProviderDocumentCreate, new ProviderDocumentContent([maximum]));
            Check(response.Succeeded);
            reference = BrokerJson.ParsePayload<ProviderDocumentReference>(response.Payload!.Value);
            Check(ProviderDocumentRegistry.Shared.Resolve(identity, reference).Single() == maximum);
            Check(!ProviderDocumentContent.IsValid([new string('界', 22 * 1024)]));
            Check(!ProviderDocumentContent.IsValid(["\ud800"]));
            try { BrokerJson.ToElement(new { metadata = "still\ninvalid" }); }
            catch (BrokerException error) when (error.Code == "invalid_backend_data") { return; }
            throw new InvalidOperationException("Document transport weakened metadata validation.");
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
            try { await running; } catch (BrokerException) { }
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    internal static async Task AttributionLifecycleAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wrail-attribution-tests-" + Guid.NewGuid().ToString("N"));
        var identity = new BrokerWidgetIdentity("example.help", "example.publisher", "attribution.instance");
        var consent = new ConsentStore(folder);
        string[] caps = [PlatformCapabilities.ProviderDocumentsV1];
        foreach (var cap in caps) await consent.SetDecisionAsync(identity, cap, ConsentDecision.Grant);
        var simulator = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulator, simulator);
        await using var broker = new PlatformCapabilityBroker(identity, caps, consent, backend);
        long sequence = 0;
        Task<System.Text.Json.JsonElement> Call(string operation, object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion,
            ++sequence, identity, caps[0], operation, BrokerJson.ToElement(payload)));
        var question = new ProviderDocumentContent(["<div>Document</div>"]);
        try
        {
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            var wire = await Call(PlatformCapabilities.ProviderDocumentCreate, question);
            var answer = BrokerJson.ParsePayload<ProviderDocumentReference>(wire);
            Check(answer.IsWellFormed() && !wire.GetRawText().Contains("<div>"));
            Check(ProviderDocumentRegistry.Shared.Resolve(identity, answer).Count == 1);
            broker.SetLifecycle(BrokerLifecycleState.Background);
            _ = await Call(PlatformCapabilities.ProviderDocumentDiscard, answer);
            Reject(answer);
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            answer = BrokerJson.ParsePayload<ProviderDocumentReference>(await Call(PlatformCapabilities.ProviderDocumentCreate, question));
            await consent.SetDecisionAsync(identity, caps[0], ConsentDecision.Deny);
            await broker.RefreshConsentAsync(); Reject(answer);
            await consent.SetDecisionAsync(identity, caps[0], ConsentDecision.Grant);
            await broker.RefreshConsentAsync();
            answer = BrokerJson.ParsePayload<ProviderDocumentReference>(await Call(PlatformCapabilities.ProviderDocumentCreate, question));
            Check(ProviderDocumentRegistry.Shared.Resolve(identity, answer).Count == 1);
            await broker.DisposeAsync(); Reject(answer);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
        void Reject(ProviderDocumentReference reference)
        {
            try { ProviderDocumentRegistry.Shared.Resolve(identity, reference); }
            catch (BrokerException) { return; }
            throw new InvalidOperationException("Retired provider markup was still available.");
        }
    }

    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Document authority regression."); }
}
