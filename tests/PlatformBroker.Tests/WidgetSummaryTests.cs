using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class WidgetSummaryTests
{
    internal static async Task Authority()
    {
        var store = new ConsentStore(Path.Combine(Path.GetTempPath(), "summary-authority-" + Guid.NewGuid().ToString("N")));
        var backend = new SimulatedPlatformBrokerBackend();
        var hub = new WidgetSummaryExchange();
        var author = new BrokerWidgetIdentity("example.author", "example.publisher", "author");
        var recipient = new BrokerWidgetIdentity("example.reader", "example.other", "reader");
        var stranger = recipient with { PackageId = "example.stranger" };
        const string publish = PlatformCapabilities.WidgetSummaryPublishV1;
        const string read = PlatformCapabilities.WidgetSummaryReadV1;
        PlatformCapabilityBroker Create(BrokerWidgetIdentity who, string capability, BrokerLifecycleState lifecycle)
        {
            var broker = new PlatformCapabilityBroker(who, [capability], store, backend, [], AppLibrarySavedIdIssuer.Shared, summaries: hub);
            broker.SetLifecycle(lifecycle);
            return broker;
        }
        int sequence = 0;
        await using var sender = Create(author, publish, BrokerLifecycleState.Background);
        await using var reader = Create(recipient, read, BrokerLifecycleState.Interactive);
        await using var other = Create(stranger, read, BrokerLifecycleState.Interactive);
        Task<JsonElement> Send(PlatformCapabilityBroker broker) => broker.ExecuteAsync(new(1, ++sequence, author, publish, "summary.publish",
            BrokerJson.ToElement(new PublishWidgetSummary(recipient.PackageId, sequence, BrokerJson.ToElement(new { detail = "In voice" })))));
        async Task<WidgetSummaryValue> Get(PlatformCapabilityBroker broker, BrokerWidgetIdentity who) =>
            BrokerJson.ParsePayload<WidgetSummaryValue>(await broker.ExecuteAsync(new(1, ++sequence, who, read, "summary.read",
                BrokerJson.ToElement(new ReadWidgetSummary(author.PackageId)))));
        async Task<bool> Authorize(string proof) => (await sender.ExecuteAsync(new(1, ++sequence, author, publish, "summary.check-reader",
            BrokerJson.ToElement(new CheckWidgetSummaryReader(proof))))).GetProperty("acknowledged").GetBoolean();
        try { await Send(sender); throw new Exception("Publication without permission"); }
        catch (BrokerException error) when (error.Code == "permission_denied") { }
        await store.SetDecisionAsync(author, publish, ConsentDecision.Grant);
        await store.SetDecisionAsync(recipient, read, ConsentDecision.Grant);
        await store.SetDecisionAsync(stranger, read, ConsentDecision.Grant);
        await using var changed = await reader.SubscribeAsync(read, "summary.changed");
        await Send(sender);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = await changed.ReadAsync(timeout.Token);
        var lateClear = await sender.ExecuteAsync(new(1, ++sequence, author, publish, "summary.publish",
            BrokerJson.ToElement(new PublishWidgetSummary(recipient.PackageId, 1, null))));
        Assert.True(!lateClear.GetProperty("acknowledged").GetBoolean());
        var result = await Get(reader, recipient);
        var proof = result.ReaderProof!;
        try { await Authorize(proof); throw new Exception("Background widget admitted an action"); }
        catch (BrokerException error) when (error.Code == "lifecycle_denied") { }
        sender.SetLifecycle(BrokerLifecycleState.Interactive);
        Assert.True(result.Value.HasValue && await Authorize(proof));
        Assert.Equal(author, hub.ActionDestination(recipient, proof));
        Assert.True(hub.ActionDestination(stranger, proof) is null);
        Assert.True((await Get(other, stranger)).Value is null);
        var token = Guid.NewGuid().ToString("N");
        async Task<bool> Stage(string reference) =>
            (await sender.ExecuteAsync(new(1, ++sequence, author, publish, "summary.select-pin",
                BrokerJson.ToElement(new WidgetSummaryPin(proof, reference, "call.stream"))))).GetProperty("accepted").GetBoolean();
        Assert.True(!await Stage(token));
        using (var pin = hub.BeginDelivery(recipient, author, proof, token, true, Guid.NewGuid().ToString("N")))
        {
            Assert.True(pin is not null && !await Stage(Guid.NewGuid().ToString("N")) && await Stage(token));
            Assert.Equal("call.stream", pin!.Complete());
            Assert.True(!await Stage(token));
        }
        Assert.True(!await Stage(token));
        await store.SetDecisionAsync(recipient, read, ConsentDecision.Deny);
        Assert.True(!await Authorize(proof));
        await reader.RefreshConsentAsync();
        Assert.True(changed.IsRevoked && hub.ActionDestination(recipient, proof) is null);
        await store.SetDecisionAsync(recipient, read, ConsentDecision.Grant);
        await reader.RefreshConsentAsync();
        var fresh = (await Get(reader, recipient)).ReaderProof!;
        Assert.True(fresh != proof && await Authorize(fresh));
        await store.SetDecisionAsync(author, publish, ConsentDecision.Deny);
        await sender.RefreshConsentAsync();
        Assert.True((await Get(reader, recipient)).Value is null);
        await store.SetDecisionAsync(author, publish, ConsentDecision.Grant);
        await sender.RefreshConsentAsync();
        await Send(sender);
        await using var replacement = Create(author, publish, BrokerLifecycleState.Background);
        Assert.True((await Get(reader, recipient)).Value is null);
        await Send(replacement);
        try { await Send(sender); throw new Exception("Retired publication admitted"); }
        catch (BrokerException error) when (error.Code == "stale_owner") { }
        await sender.DisposeAsync();
        Assert.True((await Get(reader, recipient)).Value.HasValue);
        replacement.SetLifecycle(BrokerLifecycleState.Destroying);
        Assert.True((await Get(reader, recipient)).Value is null);

        var scope = WidgetSummaryScope.ForProvider(author.PackageId);
        await using var provider = new PlatformCapabilityBroker(author, [], store, backend,
            [PlatformCapabilities.WidgetSummaryServiceV1], AppLibrarySavedIdIssuer.Shared, summaries: hub);
        provider.SetLifecycle(BrokerLifecycleState.Background);
        await using var scoped = Create(recipient, scope, BrokerLifecycleState.Interactive);
        async Task<WidgetSummaryValue> ScopedRead(string package) => BrokerJson.ParsePayload<WidgetSummaryValue>(
            await scoped.ExecuteAsync(new(1, ++sequence, recipient, scope, "summary.read", BrokerJson.ToElement(new ReadWidgetSummary(package)))));
        await provider.ExecuteAsync(new(1, ++sequence, author, PlatformCapabilities.WidgetSummaryServiceV1, "summary.publish",
            BrokerJson.ToElement(new PublishWidgetSummary(recipient.PackageId, 1, BrokerJson.ToElement(new { detail = "In voice" })))));
        try { await ScopedRead(author.PackageId); throw new Exception("Generic grant authorized a scoped read."); }
        catch (BrokerException error) when (error.Code == "permission_denied") { }
        await store.SetDecisionAsync(recipient, scope, ConsentDecision.Grant);
        var scopedValue = await ScopedRead(author.PackageId);
        Assert.True(scopedValue.ProviderRunning && scopedValue.Value.HasValue);
        try { await ScopedRead(stranger.PackageId); throw new Exception("Read escaped its provider."); }
        catch (BrokerException error) when (error.Code == "invalid_payload") { }
        await provider.RefreshConsentAsync();
        Assert.Equal(scopedValue.ReaderProof, (await ScopedRead(author.PackageId)).ReaderProof);
        await store.SetDecisionAsync(recipient, scope, ConsentDecision.Deny);
        await scoped.RefreshConsentAsync();
        Assert.True(hub.ActionDestination(recipient, scopedValue.ReaderProof!) is null);
    }
}
