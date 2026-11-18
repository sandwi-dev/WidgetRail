using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeIntentScenarios
{
    internal static Task FileContract()
    {
        var contract = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Files);
        Check(contract.Accepts(JsonSerializer.SerializeToElement(new { offerId = new string('a', 32) })));
        Check(!contract.Accepts(JsonSerializer.SerializeToElement(new { path = "C:/private.txt" })));
        Check(!contract.Accepts(JsonSerializer.SerializeToElement(new { offerId = new string('a', 32), path = "C:/private.txt" })));
        Check(!contract.Accepts(JsonSerializer.SerializeToElement(new { offerId = "short" })));
        var errors = new List<string>();
        WidgetIntentContracts.ValidateDeclarations(new() { Requests = [WidgetIntentContracts.Files], Handles = [new(WidgetIntentContracts.Files)] }, (_, code, _) => errors.Add(code));
        Check(errors.Count == 0);
        WidgetIntentContracts.ValidateDeclarations(new() { Requests = [WidgetIntentContracts.Files with { Version = 2 }] }, (_, code, _) => errors.Add(code));
        Check(errors.Contains("reserved_intent"));
        return Task.CompletedTask;
    }

    internal static async Task FileHandoff()
    {
        using var temporary = new SharedFileFixture();
        var path = temporary.Path;
        await File.WriteAllTextAsync(path, "test attachment");
        var source = Target("source") with { Intents = new() { Requests = [WidgetIntentContracts.Files] } };
        var target = Target("receiver") with { Intents = new() { Handles = [new(WidgetIntentContracts.Files)] } };
        var files = FileReferenceRegistry.Shared;
        using var sender = files.CreateOwner(FileIdentity(source));
        using var receiver = files.CreateOwner(FileIdentity(target));
        var reference = files.RegisterSelected(sender, [path], default).Files.Single();
        var offer = files.OfferFiles(sender, new([reference.Id]));
        await using var fixture = FileFixture(source, target, () => offer.Id);
        var origin = await PrepareSource(fixture);
        var prepared = await fixture.Registry.PrepareIntentAsync(origin, default);
        Check(prepared.TicketId is not null && prepared.Destinations.Single().WidgetId == "receiver");
        await fixture.SetLifecycleAsync("receiver", WidgetLifecycleState.Interactive);
        var frame = await fixture.GetSnapshotAsync("receiver");
        WidgetFileReference? received = null;
        fixture.Clients.Single(client => client.WidgetId == "receiver").IntentHandler = (request, _) =>
        {
            received = files.AcceptShare(receiver, request.Payload.GetProperty("offerId").GetString()!).Files.Single();
            return Task.FromResult(WidgetIntentResult.InteractionRequired);
        };
        await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Background);
        var commit = new BridgeIntentCommitRequest("source", prepared.TicketId!, "receiver", frame.WorkerRun);
        var completion = await fixture.Registry.CommitIntentAsync(commit, default);
        Check(completion.Accepted && completion.RequiresInteraction && received is not null);
        Check(!(await fixture.Registry.CommitIntentAsync(commit, default)).Accepted);
        sender.Dispose();
        Check((await files.ReadAsync(receiver, new(received!.Id, 0, 4), default)).Bytes.SequenceEqual("test"u8.ToArray()));
        files.Release(receiver, received.Id);
    }

    internal static async Task FileDiscoveryStartsOnlyChosenReceiver()
    {
        foreach (var available in new[] { false, true })
        {
            using var temporary = new SharedFileFixture();
            await File.WriteAllTextAsync(temporary.Path, "selected file");
            var source = Target("source") with { Intents = new() { Requests = [WidgetIntentContracts.Files] } };
            var selected = Target("selected") with { Intents = new() { Handles = [new(WidgetIntentContracts.Files) { HasDynamicAvailability = true }] } };
            var unused = Target("unused") with { Intents = selected.Intents };
            var files = FileReferenceRegistry.Shared;
            using var sender = files.CreateOwner(FileIdentity(source));
            using var receiver = files.CreateOwner(FileIdentity(selected));
            var reference = files.RegisterSelected(sender, [temporary.Path], default).Files.Single();
            var offer = files.OfferFiles(sender, new([reference.Id]));
            await using var fixture = new RegistryFixture(new([source, selected, unused, Target("unrelated")]), configure: (configured, client) =>
            {
                client.SnapshotFactory = sequence => new WidgetView(UI.Stack("root", configured.Id == "source"
                    ? UI.Button("Share", "guide", "guide").OpenIntent(WidgetIntentContracts.Files, JsonSerializer.SerializeToElement(new { offerId = offer.Id }))
                    : UI.Button("Compose", "target", "target"))).CreateSnapshot(configured.InstanceId, sequence);
                client.IntentQuery = (_, _, _) => Task.FromResult(available);
            });
            var prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
            Check(prepared.Kind == WidgetIntentLaunchKind.ChooseHandler && prepared.Destinations.Count == 2);
            Check(fixture.Clients.Count == 1 && fixture.Clients[0].WidgetId == "source");
            Check(fixture.Clients[0].AvailabilityQueries == 0);

            // Selection activates only this target. A declared dynamic check
            // still rejects before recipient file authority can be granted.
            await fixture.SetLifecycleAsync("selected", WidgetLifecycleState.Interactive);
            var frame = await fixture.GetSnapshotAsync("selected");
            var client = fixture.Clients.Single(item => item.WidgetId == "selected");
            WidgetFileReference? received = null;
            client.IntentHandler = (request, _) =>
            {
                received = files.AcceptShare(receiver, request.Payload.GetProperty("offerId").GetString()!).Files.Single();
                return Task.FromResult(WidgetIntentResult.InteractionRequired);
            };
            await fixture.SetLifecycleAsync("source", WidgetLifecycleState.Background);
            var result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "selected", frame.WorkerRun), default);
            Check(result.Accepted == available && client.AvailabilityQueries == 1);
            Check(client.Intents.Count == (available ? 1 : 0) && (received is not null) == available);
            Check(fixture.Clients.Count == 2 && fixture.Clients.All(item => item.WidgetId is "source" or "selected"));
            if (received is not null) files.Release(receiver, received.Id);
        }
    }

    internal static async Task FileCapacityPreservesDrafts()
    {
        using var temporary = new SharedFileFixture();
        await File.WriteAllTextAsync(temporary.Path, "draft attachment");
        var source = Target("source") with { Intents = new() { Requests = [WidgetIntentContracts.Files] } };
        var target = Target("receiver") with { Intents = new() { Handles = [new(WidgetIntentContracts.Files)] } };
        var files = FileReferenceRegistry.Shared;
        using var sender = files.CreateOwner(FileIdentity(source));
        using var receiver = files.CreateOwner(FileIdentity(target));
        var retained = Enumerable.Range(0, 32).Select(_ => files.RegisterSelected(receiver, [temporary.Path], default).Files.Single()).ToArray();
        var reference = files.RegisterSelected(sender, [temporary.Path], default).Files.Single();
        var offer = files.OfferFiles(sender, new([reference.Id]));
        await using var fixture = FileFixture(source, target, () => offer.Id);
        var prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
        await fixture.SetLifecycleAsync("receiver", WidgetLifecycleState.Interactive);
        var frame = await fixture.GetSnapshotAsync("receiver");
        var client = fixture.Clients.Single(item => item.WidgetId == "receiver");
        var result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "receiver", frame.WorkerRun), default);
        Check(!result.Accepted && result.Status == WidgetIntentStatus.Rejected && result.Failure == WidgetIntentFailure.FileCapacity);
        Check(client.Intents.Count == 0 && retained.All(file => files.Get(receiver, file.Id).Id == file.Id));
        Check((await files.ReadAsync(receiver, new(retained[0].Id, 0, 5), default)).Bytes.SequenceEqual("draft"u8.ToArray()));
        files.Release(receiver, retained[0].Id);
        offer = files.OfferFiles(sender, new([reference.Id]));
        prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
        client.IntentHandler = (request, _) =>
        {
            Check(files.AcceptShare(receiver, request.Payload.GetProperty("offerId").GetString()!).Files.Count == 1);
            return Task.FromResult(WidgetIntentResult.Accepted);
        };
        result = await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "receiver", frame.WorkerRun), default);
        Check(result.Accepted && result.Failure == WidgetIntentFailure.None);
    }

    internal static async Task FileRejectedAndCancelled()
    {
        using var temporary = new SharedFileFixture();
        var path = temporary.Path;
        await File.WriteAllTextAsync(path, "private bytes");
        var source = Target("source") with { Intents = new() { Requests = [WidgetIntentContracts.Files] } };
        var target = Target("receiver") with { Intents = new() { Handles = [new(WidgetIntentContracts.Files)] } };
        var files = FileReferenceRegistry.Shared;
        using var sender = files.CreateOwner(FileIdentity(source));
        using var receiver = files.CreateOwner(FileIdentity(target));
        using var foreign = files.CreateOwner(new("example.foreign", "example.publisher", "foreign.instance"));
        var reference = files.RegisterSelected(sender, [path], default).Files.Single();
        var foreignReference = files.RegisterSelected(foreign, [path], default).Files.Single();
        var offer = files.OfferFiles(foreign, new([foreignReference.Id]));
        await using var fixture = FileFixture(source, target, () => offer.Id);
        var prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
        await fixture.SetLifecycleAsync("receiver", WidgetLifecycleState.Interactive);
        var frame = await fixture.GetSnapshotAsync("receiver");
        var client = fixture.Clients.Single(item => item.WidgetId == "receiver");
        Check(!(await fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "receiver", frame.WorkerRun), default)).Accepted);
        Check(client.Intents.Count == 0); // A foreign offer never reaches receiver code.

        foreach (var cancel in new[] { false, true })
        {
            offer = files.OfferFiles(sender, new([reference.Id]));
            prepared = await fixture.Registry.PrepareIntentAsync(await PrepareSource(fixture), default);
            WidgetFileReference? provisional = null;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.IntentHandler = async (request, token) =>
            {
                provisional = files.AcceptShare(receiver, request.Payload.GetProperty("offerId").GetString()!).Files.Single();
                entered.TrySetResult();
                if (cancel) await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return WidgetIntentResult.Rejected;
            };
            var pending = fixture.Registry.CommitIntentAsync(new("source", prepared.TicketId!, "receiver", frame.WorkerRun), default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (cancel) await fixture.Registry.CancelIntentAsync(new("source", prepared.TicketId!));
            Check(!(await pending).Accepted && provisional is not null);
            try { files.Get(receiver, provisional!.Id); throw new Exception("Rejected transfer retained authority"); }
            catch (BrokerException) { }
        }
    }

    private static BrokerWidgetIdentity FileIdentity(ConfiguredWidget widget) => new(widget.PackageId, widget.PublisherId, widget.InstanceId);
    private sealed class SharedFileFixture : IDisposable
    {
        internal string Path { get; } = System.IO.Path.GetTempFileName();
        public void Dispose() => File.Delete(Path);
    }
    private static RegistryFixture FileFixture(ConfiguredWidget source, ConfiguredWidget target, Func<string> offer) =>
        new(new([source, target]), configure: (configured, client) => client.SnapshotFactory = sequence =>
            new WidgetView(UI.Stack("root", configured.Id == "source"
                ? UI.Button("Share", "guide", "guide") with { Intent = WidgetIntentRequest.Create(WidgetIntentContracts.Files,
                    JsonSerializer.SerializeToElement(new { offerId = offer() })) }
                : UI.Button("Compose", "target", "target"))).CreateSnapshot(configured.InstanceId, sequence));
}
