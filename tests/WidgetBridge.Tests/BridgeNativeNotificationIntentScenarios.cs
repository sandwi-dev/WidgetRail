using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeIntentScenarios
{
    internal static async Task NativeNotificationIntents()
    {
        var source = Source() with { Intents = new() { Requests = [WidgetIntentContracts.Web],
            Handles = [new(WidgetIntentContracts.WebNotification)] } };
        var impostor = Target("other") with { Intents = new() { Handles = [new(WidgetIntentContracts.WebNotification)] } };
        await using var fixture = Fixture([source, impostor]);
        var frame = await PrepareSource(fixture);
        var identity = new BrokerWidgetIdentity(source.PackageId, source.PublisherId, source.InstanceId);
        var sessions = WebSessionRegistry.Shared;
        using var owner = sessions.CreateOwner(source.Id, identity, true);
        var disabled = await Open("disabled", false);
        var enabled = await Open("enabled", true);
        var notification = new WebNotificationIntentPayload(enabled.Id, "native-message-123");
        Check((await Prepare(notification with { SessionId = disabled.Id })).Kind == WidgetIntentLaunchKind.Rejected);
        Check((await Prepare(notification with { SessionId = new string('a', 32) })).Kind == WidgetIntentLaunchKind.Rejected);
        Check((await Prepare(notification with { Tag = "bad\ntag" })).Kind == WidgetIntentLaunchKind.Rejected);
        Check((await fixture.Registry.PrepareWebNotificationIntentAsync(new("other", notification), default)).Kind == WidgetIntentLaunchKind.Rejected);
        await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Background);
        var prepared = await Prepare(notification);
        Check(prepared.Kind == WidgetIntentLaunchKind.Widget && prepared.Destinations.Single().WidgetId == "source");
        Check(fixture.Clients.Count == 1); // An identically declared unrelated handler is never started or offered.
        await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Interactive);
        var target = await fixture.GetSnapshotAsync("source");
        Check((await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "source", target.WorkerRun), default)).Accepted);
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "source", target.WorkerRun), default)).Accepted);
        var command = new WidgetNotificationCommand("forged", new("forged", "Forged")
        { OpenIntent = WidgetIntentRequest.Create(WidgetIntentContracts.WebNotification, JsonSerializer.SerializeToElement(notification)) });
        Check(!fixture.Registry.RegisterNotificationIntent("source", source.WorkerFingerprint, frame.WorkerRun, command, out _));
        var authoredSource = source with { Intents = new() { Requests = [WidgetIntentContracts.Web, WidgetIntentContracts.WebNotification],
            Handles = [new(WidgetIntentContracts.WebNotification)] } };
        await using var authored = Fixture([authoredSource]);
        var authoredFrame = await PrepareSource(authored);
        Check(!authored.Registry.RegisterNotificationIntent("source", authoredSource.WorkerFingerprint, authoredFrame.WorkerRun, command, out _));
        prepared = await Prepare(notification);
        await owner.ExecuteAsync(new(WebSessionOperation.Close) { Session = enabled }, default);
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "source", target.WorkerRun), default)).Accepted);
        Check((await Prepare(notification)).Kind == WidgetIntentLaunchKind.Rejected);
        var replacement = await Open("replacement", true);
        prepared = await Prepare(notification with { SessionId = replacement.Id });
        using var nextOwner = sessions.CreateOwner(source.Id, identity, true);
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "source", target.WorkerRun), default)).Accepted);

        Task<WidgetIntentPreparation> Prepare(WebNotificationIntentPayload payload) =>
            fixture.Registry.PrepareWebNotificationIntentAsync(new("source", payload), default);
        async Task<WebSessionReference> Open(string key, bool native)
        {
            var open = owner.ExecuteAsync(new(WebSessionOperation.Open)
                { Definition = new(key, "Test session", WebSessionSource.Remote("https://example.com")) { NativeNotificationIntents = native } }, default).AsTask();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            HostWebSessionWork? work = null;
            while (work is null)
                work = (await sessions.TakeAsync(timeout.Token)).Work.FirstOrDefault(item => item.Command.Operation == WebSessionOperation.Open && item.Command.Definition?.Key == key);
            sessions.Complete(new(work.RequestId));
            return (await open).Session!;
        }
    }
}
