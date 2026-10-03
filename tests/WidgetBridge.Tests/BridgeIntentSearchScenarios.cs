using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeIntentScenarios
{
    private static WidgetIntentRequest SearchRequest() => WidgetIntentRequest.Create(WidgetIntentContracts.VideoSearch,
        JsonSerializer.SerializeToElement(new { provider = "youtube", query = "boss guide" })) with
    { ReportsResult = true, Fallback = WidgetIntentRequest.Create(WidgetIntentContracts.Web,
        JsonSerializer.SerializeToElement(new { url = "https://www.youtube.com/results?search_query=boss%20guide" })) };

    private static RegistryFixture SearchFixture(bool receiver, Func<bool>? available = null,
        Func<WidgetIntentRequest>? request = null, Action<RegistryTestClient>? configure = null)
    {
        var widgets = new List<ConfiguredWidget> { Source() with { Intents = new() { Requests = [WidgetIntentContracts.VideoSearch, WidgetIntentContracts.Web] } } };
        if (receiver) widgets.Add(Target("youtube") with { Intents = new() { Handles = [new(WidgetIntentContracts.VideoSearch) { HasDynamicAvailability = true }] } });
        return new(new(widgets), configure: (entry, client) =>
        {
            client.SnapshotFactory = sequence => new WidgetView(UI.Stack("root", UI.Button("Guide", "guide", "guide") with
                { Intent = entry.Id == "source" ? request?.Invoke() ?? SearchRequest() : null })).CreateSnapshot(entry.InstanceId, sequence);
            client.IntentQuery = (_, feedback, _) => Task.FromResult(feedback is null ? available?.Invoke() ?? true : true);
            configure?.Invoke(client);
        });
    }

    internal static async Task SearchUnavailableFeedback()
    {
        foreach (var receiver in new[] { false, true })
        {
            await using var fixture = SearchFixture(receiver, () => false);
            var request = await PrepareSource(fixture);
            var prepared = await fixture.Registry.PrepareIntentAsync(request, default);
            Check(prepared.Kind == WidgetIntentLaunchKind.ExternalBrowser);
            var source = fixture.Clients.Single(c => c.WidgetId == "source");
            Check(source.IntentFeedback.Single().Status == WidgetIntentStatus.Unavailable);
            if (receiver)
            {
                var target = fixture.Clients.Single(c => c.WidgetId == "youtube");
                Check(target.IsRunning && target.AvailabilityQueries == 1 && target.Intents.Count == 0);
            }
            var result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!), default);
            Check(result.Accepted && result.ExternalUrl!.Contains("search_query="));
            Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!), default)).Accepted);
            Check(source.IntentFeedback.Count == 1);
        }
    }

    internal static async Task SearchRecheckAndRejectedFeedback()
    {
        foreach (var rejected in new[] { false, true })
        {
            var available = true;
            await using var fixture = SearchFixture(true, () => available);
            var prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
            Check(prepared.Kind == WidgetIntentLaunchKind.Widget);
            await fixture.SetLifecycleAsync("youtube", WidgetLifecycleState.Interactive);
            var target = await fixture.GetSnapshotAsync("youtube");
            await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Background);
            var receiver = fixture.Clients.Single(c => c.WidgetId == "youtube");
            if (rejected) receiver.IntentHandler = (_, _) => Task.FromResult(WidgetIntentResult.Rejected);
            else available = false;
            var result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "youtube", target.WorkerRun), default);
            Check(!result.Accepted && result.FollowUp?.Kind == WidgetIntentLaunchKind.ExternalBrowser);
            Check(receiver.Intents.Count == (rejected ? 1 : 0));
            var source = fixture.Clients.Single(c => c.WidgetId == "source");
            Check(source.IntentFeedback.Single().Status == (rejected ? WidgetIntentStatus.Rejected : WidgetIntentStatus.Unavailable));
            Check((await fixture.Registry.CommitIntentAsync(new("source", result.FollowUp!.TicketId!), default)).Accepted);
        }
    }

    internal static async Task SearchSuccessFailureAndCancellation()
    {
        foreach (var mode in new[] { "accepted", "failed", "cancelled", "stale-target", "stale-source" })
        {
            await using var fixture = SearchFixture(true);
            var prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
            var source = fixture.Clients.Single(c => c.WidgetId == "source");
            if (mode == "cancelled")
            {
                await fixture.Registry.CancelIntentAsync(new("source", prepared.TicketId!));
                await fixture.Registry.CancelIntentAsync(new("source", prepared.TicketId!));
                Check(source.IntentFeedback.Single().Status == WidgetIntentStatus.Cancelled);
                continue;
            }
            await fixture.SetLifecycleAsync("youtube", WidgetLifecycleState.Interactive);
            var target = await fixture.GetSnapshotAsync("youtube");
            var receiver = fixture.Clients.Single(c => c.WidgetId == "youtube");
            if (mode == "failed") receiver.IntentHandler = (_, _) => throw new IOException("Lost acknowledgement");
            if (mode == "stale-target") await receiver.UnloadAsync(default);
            if (mode == "stale-source") await source.UnloadAsync(default);
            var result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "youtube", target.WorkerRun), default);
            Check(result.FollowUp is null && result.Accepted == (mode == "accepted"));
            if (mode == "stale-source") Check(source.IntentFeedback.Count == 0);
            else Check(source.IntentFeedback.Single().Status == (mode == "accepted" ? WidgetIntentStatus.Accepted : WidgetIntentStatus.Failed));
        }
    }

    internal static async Task SearchFallbackAuthority()
    {
        var current = SearchRequest();
        await using var fixture = SearchFixture(true, request: () => current);
        var prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
        await fixture.SetLifecycleAsync("youtube", WidgetLifecycleState.Interactive);
        var target = await fixture.GetSnapshotAsync("youtube");
        fixture.Clients.Single(c => c.WidgetId == "youtube").IntentHandler = (_, _) => Task.FromResult(WidgetIntentResult.Rejected);
        current = current with { Fallback = current.Fallback! with { Payload = JsonSerializer.SerializeToElement(new { url = "https://example.com/changed" }) } };
        _ = await fixture.GetSnapshotAsync("source");
        var result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "youtube", target.WorkerRun), default);
        Check(!result.Accepted && result.FollowUp is null);
    }
}
