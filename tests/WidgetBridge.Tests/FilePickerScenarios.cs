using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

internal static class FilePickerScenarios
{
    internal static async Task HostPipe()
    {
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-file-picker-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "selected.txt");
        await File.WriteAllTextAsync(path, "attachment");
        var identity = new BrokerWidgetIdentity("example.files", "example.publisher", "files.instance");
        var widget = new ConfiguredWidget { Id = "files", PackageId = identity.PackageId, PublisherId = identity.PublisherId,
            InstanceId = identity.InstanceId, Name = "Files fixture", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64) };
        var pipe = "files-picker-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await using var broker = new PlatformCapabilityBroker(identity, [], new ConsentStore(Path.Combine(root, "consent")),
                new SimulatedPlatformBrokerBackend(), [PlatformCapabilities.FilesReadV1]);
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            await using var server = new WidgetBridgeServer(pipe, new([widget]));
            var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
            await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
            long sequence = 0;
            Task<JsonElement> Call(string operation, object payload, CancellationToken token = default) => broker.ExecuteAsync(new(1, ++sequence,
                identity, PlatformCapabilities.FilesReadV1, operation, BrokerJson.ToElement(payload)), token);
            var choosing = Call("files.choose", new WidgetFileChoiceRequest("Attach files", false) { CommitLabel = "Attach" }, deadline.Token);
            var prompt = await Pending(session, deadline.Token);
            if (prompt.WidgetId != widget.Id || prompt.Title != "Attach files" || prompt.Multiple || prompt.CommitLabel != "Attach" ||
                !await session.IsFilePickerCurrentAsync(prompt.RequestId, deadline.Token)) throw new Exception("Picker owner/choice lost in host pipe.");
            // Native selection can finish after the host's input handoff; a new
            // request from Background must still be denied by the broker.
            broker.SetLifecycle(BrokerLifecycleState.Background);
            await session.CompleteFilePickerAsync(prompt.RequestId, [path], deadline.Token);
            var result = await choosing;
            var selected = BrokerJson.ParsePayload<WidgetFileSelection>(result).Files.Single();
            if (!selected.IsWellFormed() || selected.Name != "selected.txt" || selected.ByteLength != 10 ||
                result.GetRawText().Contains(JsonSerializer.Serialize(root)[1..^1], StringComparison.Ordinal))
                throw new Exception("Selected-file path leaked to widget or metadata changed.");
            var bytes = BrokerJson.ParsePayload<WidgetFileReadChunk>(await Call("files.read", new WidgetFileReadRequest(selected.Id, 0, 10), deadline.Token));
            if (!bytes.Bytes.SequenceEqual("attachment"u8.ToArray())) throw new Exception("Opaque selection did not retain original read access.");
            if (await session.IsFilePickerCurrentAsync(prompt.RequestId, deadline.Token)) throw new Exception("Completed picker authority survived.");
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var abandoned = Call("files.choose", new WidgetFileChoiceRequest(), cancel.Token);
            var stale = await Pending(session, deadline.Token);
            cancel.Cancel();
            try { await abandoned; throw new Exception("Cancelled choice completed."); }
            catch (Exception error) when (error is OperationCanceledException or BrokerException) { }
            if (await session.IsFilePickerCurrentAsync(stale.RequestId, deadline.Token)) throw new Exception("Cancelled picker remained current.");
            var next = Call("files.choose", new WidgetFileChoiceRequest(), deadline.Token);
            var current = await Pending(session, deadline.Token);
            await session.CompleteFilePickerAsync(stale.RequestId, [path], deadline.Token);
            if (next.IsCompleted || !await session.IsFilePickerCurrentAsync(current.RequestId, deadline.Token))
                throw new Exception("Stale completion redirected a later picker.");
            await session.CompleteFilePickerAsync(current.RequestId, [], deadline.Token);
            if (BrokerJson.ParsePayload<WidgetFileSelection>(await next).Files.Count != 0) throw new Exception("Cancel granted a selected file.");
            await Call("files.release", new WidgetFileIdReference(selected.Id), deadline.Token);
            var foreign = FilePickerRequests.ChooseAsync(identity with { PublisherId = "different.publisher" },
                new WidgetFileChoiceRequest(), () => true, deadline.Token);
            if ((await session.TakeFilePickerAsync(deadline.Token)).Prompt is not null || (await foreign).Length != 0)
                throw new Exception("A reused instance ID bypassed publisher identity checks.");
            await session.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static async Task<BridgeFilePickerPrompt> Pending(WidgetPresentationSession session, CancellationToken token)
    {
        while (true)
        {
            if ((await session.TakeFilePickerAsync(token)).Prompt is { } prompt) return prompt;
            await Task.Delay(10, token);
        }
    }
}
