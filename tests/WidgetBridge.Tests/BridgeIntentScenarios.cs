using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class BridgeIntentScenarios
{
    internal static async Task Admission()
    {
        var source = Source();
        var url = "https://example.com/guide";
        await using var fixture = Fixture([source], () => url);
        Check((await fixture.Registry.PrepareIntentAsync(new("source", 1, new(1, 1), Action()), default)).Kind == WidgetIntentLaunchKind.Rejected);
        Check(fixture.Clients.Count == 0);
        var request = await PrepareSource(fixture);
        Check((await fixture.Registry.PrepareIntentAsync(request, default)).Kind == WidgetIntentLaunchKind.ExternalBrowser);
        url = "https://example.com/changed";
        _ = await fixture.GetSnapshotAsync("source");
        Check((await fixture.Registry.PrepareIntentAsync(request, default)).Kind == WidgetIntentLaunchKind.Rejected);
        await using var undeclared = Fixture([source with { Intents = null }]);
        Check((await undeclared.Registry.PrepareIntentAsync(await PrepareSource(undeclared), default)).Kind == WidgetIntentLaunchKind.Rejected);
    }

    internal static async Task FallbackTickets()
    {
        long clock = 0;
        await using var fixture = Fixture([Source()], now: () => clock);
        var request = await PrepareSource(fixture);
        var prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        Check(prepared.Kind == WidgetIntentLaunchKind.ExternalBrowser && prepared.Destinations.Count == 0);
        var commit = new BridgeIntentCommitRequest("source", prepared.TicketId!);
        var first = await fixture.Registry.CommitIntentAsync(commit, default);
        Check(first.Accepted && first.ExternalUrl == "https://example.com/guide");
        Check(!(await fixture.Registry.CommitIntentAsync(commit, default)).Accepted);
        prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        await fixture.Registry.CancelIntentAsync(new("source", prepared.TicketId!));
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!), default)).Accepted);
        prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        clock = 60_001;
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!), default)).Accepted);
        for (var i = 0; i < 16; i++) Check((await fixture.Registry.PrepareIntentAsync(request, default)).TicketId is not null);
        Check((await fixture.Registry.PrepareIntentAsync(request, default)).Kind == WidgetIntentLaunchKind.Rejected);
        clock += 60_001;
        Check((await fixture.Registry.PrepareIntentAsync(request, default)).TicketId is not null);
    }

    internal static async Task WidgetDelivery()
    {
        await using var fixture = Fixture([Source(), Target("browser")]);
        var request = await PrepareSource(fixture);
        var prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        Check(prepared.Kind == WidgetIntentLaunchKind.Widget && prepared.Destinations.Single().WidgetId == "browser");
        Check(fixture.Clients.Count == 1); // Resolution must not start the handler.
        await fixture.SetLifecycleAsync("browser", WidgetLifecycleState.Interactive);
        var target = await fixture.GetSnapshotAsync("browser");
        await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Background); // Normal destination activation hides source.
        var commit = new BridgeIntentCommitRequest("source", prepared.TicketId!, "browser", target.WorkerRun);
        var result = await fixture.Registry.CommitIntentAsync(commit, default);
        Check(result.Accepted && result.ExternalUrl is null);
        var client = fixture.Clients.Single(c => c.WidgetId == "browser");
        Check(client.Intents.Count == 1);
        Check(!(await fixture.Registry.CommitIntentAsync(commit, default)).Accepted && client.Intents.Count == 1);
    }

    internal static async Task ReplacementsAndChoice()
    {
        await using var fixture = Fixture([Source(), Target("browser"), Target("reader")]);
        var request = await PrepareSource(fixture);
        var prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        Check(prepared.Kind == WidgetIntentLaunchKind.ChooseHandler && prepared.Destinations.Count == 2);
        await fixture.SetLifecycleAsync("reader", WidgetLifecycleState.Interactive);
        var target = await fixture.GetSnapshotAsync("reader");
        Check((await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "reader", target.WorkerRun), default)).Accepted);
        prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        var client = fixture.Clients.Single(c => c.WidgetId == "reader");
        await client.UnloadAsync(default);
        await fixture.SetLifecycleAsync("reader", WidgetLifecycleState.Interactive);
        _ = await fixture.GetSnapshotAsync("reader");
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "reader", target.WorkerRun), default)).Accepted);
        prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        await fixture.Clients.Single(c => c.WidgetId == "source").UnloadAsync(default);
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "reader", target.WorkerRun), default)).Accepted);
    }

    internal static async Task Cancellation()
    {
        await using var fixture = Fixture([Source(), Target("browser")]);
        var request = await PrepareSource(fixture);
        var prepared = await fixture.Registry.PrepareIntentAsync(request, default);
        await fixture.SetLifecycleAsync("browser", WidgetLifecycleState.Interactive);
        var target = await fixture.GetSnapshotAsync("browser");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Clients.Single(c => c.WidgetId == "browser").IntentHandler = async (_, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return true; };
        var pending = fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "browser", target.WorkerRun), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Registry.CancelIntentAsync(new("wrong-source", prepared.TicketId!));
        Check(!pending.IsCompleted);
        await fixture.Registry.CancelIntentAsync(new("source", prepared.TicketId!));
        Check(!(await pending).Accepted);
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "browser", target.WorkerRun), default)).Accepted);
    }

    internal static async Task FailureOffersFallback()
    {
        await using var fixture = Fixture([Source(), Target("browser")]);
        var prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
        await fixture.SetLifecycleAsync("browser", WidgetLifecycleState.Interactive);
        var target = await fixture.GetSnapshotAsync("browser");
        var client = fixture.Clients.Single(c => c.WidgetId == "browser");
        client.IntentHandler = (_, _) => Task.FromResult(false);
        var result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "browser", target.WorkerRun), default);
        Check(!result.Accepted && result.ExternalUrl is null && result.BrowserFallbackUrl == "https://example.com/guide");
        Check(client.Intents.Count == 1);
    }

    private static async Task<BridgeIntentPrepareRequest> PrepareSource(RegistryFixture fixture)
    {
        await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Interactive);
        var snapshot = await fixture.GetSnapshotAsync("source");
        return new("source", snapshot.Snapshot.Sequence, snapshot.WorkerRun!, Action());
    }
    private static WidgetActionEvent Action() => new("guide", "guide", ControllerButton.A, InputScopeId: "root");
    private static RegistryFixture Fixture(ConfiguredWidget[] widgets, Func<string>? url = null, Func<long>? now = null) =>
        new(new(widgets), configure: (configured, client) => client.SnapshotFactory = sequence =>
            new WidgetView(UI.Stack("root", configured.Id == "source"
                ? UI.Button("Guide", "guide", "guide").OpenIntent(WidgetIntentContracts.Web,
                    JsonSerializer.SerializeToElement(new { url = url?.Invoke() ?? "https://example.com/guide" }))
                : UI.Button("Target", "target", "target"))).CreateSnapshot(configured.InstanceId, sequence), intentNow: now);
    private static ConfiguredWidget Source() => Target("source") with { Intents = new() { Requests = [WidgetIntentContracts.Web] } };
    private static ConfiguredWidget Target(string id) => new()
    {
        Id = id, PackageId = "example." + id, PublisherId = "example.publisher", Name = id, InstanceId = id + ".instance",
        WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64),
        Intents = new() { Handles = [WidgetIntentContracts.Web] },
    };
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? expression = null)
    { if (!value) throw new Exception(expression); }
}
