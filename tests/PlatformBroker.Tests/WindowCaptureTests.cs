using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class WindowCaptureTests
{
    internal static async Task ForegroundAuthorityAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wrail-foreground-capture-" + Guid.NewGuid().ToString("N"));
        var identity = new BrokerWidgetIdentity("example.capture", "example.publisher", "foreground.instance");
        var consent = new ConsentStore(folder);
        await consent.SetDecisionAsync(identity, PlatformCapabilities.WindowCaptureV1, ConsentDecision.Grant);
        var simulator = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulator, simulator);
        await using var broker = new PlatformCapabilityBroker(identity, [PlatformCapabilities.WindowCaptureV1], consent, backend);
        long sequence = 0;
        Task<JsonElement> Call(string operation, object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion,
            ++sequence, identity, PlatformCapabilities.WindowCaptureV1, operation, BrokerJson.ToElement(payload)));
        try
        {
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            try { await Call(PlatformCapabilities.WindowCaptureRequest, new { kind = WindowCaptureKind.Video, windowId = "retired-window-selection" }); throw new Exception("Retired selected-window payload was accepted."); }
            catch (BrokerException) { }
            var ticket = BrokerJson.ParsePayload<WidgetRail.WidgetProtocol.WindowCaptureTicket>(await Call(
                PlatformCapabilities.WindowCaptureRequest, new WidgetRail.WidgetProtocol.WindowCaptureRequest(WindowCaptureKind.Video)));
            var pending = WindowCaptureRegistry.Shared.Take()!;
            Check(pending.RequestId == ticket.RequestId && pending.Kind == WindowCaptureKind.Video);
            broker.SetLifecycle(BrokerLifecycleState.Background);
            await Call(PlatformCapabilities.WindowCaptureCancel, ticket);
            try { await Call(PlatformCapabilities.WindowCaptureRequest, new WidgetRail.WidgetProtocol.WindowCaptureRequest(WindowCaptureKind.Video)); throw new Exception("Background capture initiation was accepted."); }
            catch (BrokerException) { }
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            await consent.SetDecisionAsync(identity, PlatformCapabilities.WindowCaptureV1, ConsentDecision.Deny);
            await broker.RefreshConsentAsync();
            try { await Call(PlatformCapabilities.WindowCaptureRequest, new WidgetRail.WidgetProtocol.WindowCaptureRequest(WindowCaptureKind.Video)); throw new Exception("Denied capture was accepted."); }
            catch (BrokerException) { }
        }
        finally
        {
            await broker.DisposeAsync();
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    internal static Task MetadataAsync()
    {
        var target = new NativeWindowPreviewTarget("ABC", 42, "0000000000000123", "GameClass");
        var game = new TaskWindowSummary("game", "007 First Light", "007FirstLight", false) { PreviewTarget = target };
        var other = new TaskWindowSummary("other", "ChatGPT", "ChatGPT", false) { PreviewTarget = target with { Handle = "DEF", ProcessId = 88 } };
        var expected = new CaptureApplicationContext("007 First Light", "007FirstLight");
        Check(CaptureApplicationMetadata.Resolve(target, [other, game]) == expected);
        Check(CaptureApplicationMetadata.Resolve(target, [game, other]) == expected);
        Check(CaptureApplicationMetadata.Resolve(target with { Handle = "000ABC" }, [game]) == expected);
        foreach (var replaced in new[] { target with { ProcessId = 43 }, target with { ProcessCreated = "124" }, target with { ClassName = "Replaced" } })
            Check(CaptureApplicationMetadata.Resolve(target, [game with { PreviewTarget = replaced }]) is null);
        Check(CaptureApplicationMetadata.Resolve(target, [other]) is null);
        Check(CaptureApplicationMetadata.Resolve(target, [game, game]) is null);
        Check(CaptureApplicationMetadata.Resolve(target, [game with { ApplicationName = new string('x',121) }]) is null);
        Check(CaptureApplicationMetadata.Resolve(target, [game with { Title = "invalid\ntitle" }]) is null);
        return Task.CompletedTask;
    }

    internal static Task RunAsync()
    {
        var clock = new TestClock();
        using var registry = new WindowCaptureRegistry(clock);
        var owner = new object(); var other = new object();
        var identity = new BrokerWidgetIdentity("example.capture", "example", "instance");
        var ticket = registry.Enqueue(owner, identity, WindowCaptureKind.Screenshot);
        Check(registry.Status(owner, ticket.RequestId).Phase == WindowCapturePhase.Queued);
        Reject(() => registry.Status(other, ticket.RequestId));
        Reject(() => registry.Enqueue(other, identity, WindowCaptureKind.Video));
        var request = registry.Take()!;
        Check(registry.Take() == request); // Lost take reply does not consume authority.
        Check(request.Kind == WindowCaptureKind.Screenshot);
        registry.Recording(ticket.RequestId);
        var bytes = new byte[CaptureLimits.ChunkBytes + 8];
        new byte[] {137,80,78,71,13,10,26,10}.CopyTo(bytes,0);
        File.WriteAllBytes(request.OutputPath, bytes);
        registry.Complete(new(ticket.RequestId, 640, 360, 0, SourceApplication: new("007 First Light", "007FirstLight")));
        var ready = registry.Status(owner, ticket.RequestId);
        Check(ready.Phase == WindowCapturePhase.Ready && ready.Attachment!.IsWellFormed());
        Check(ready.Attachment!.SourceApplication == new CaptureApplicationContext("007 First Light", "007FirstLight"));
        Reject(() => registry.ResolveOwned(owner, ready.Attachment with { SourceApplication = new("Another game", "Other") }));
        Check(!(ready.Attachment with { SourceApplication = new(new string('x',121), "") }).IsWellFormed());
        Check(!(ready.Attachment with { SourceApplication = new("bad\nname", "") }).IsWellFormed());
        var json = JsonSerializer.Serialize(ready);
        Check(JsonSerializer.Deserialize<WindowCaptureStatus>(json)!.Attachment == ready.Attachment);
        Check(!json.Contains(request.OutputPath) && !json.Contains("ProcessId") && !json.Contains("Handle"));
        var first = registry.Read(owner, new(ready.Attachment!.Id, 0));
        Check(first.Bytes.Length == CaptureLimits.ChunkBytes && !first.EndOfFile);
        Check(registry.Read(owner,new(ready.Attachment.Id,first.Bytes.Length)).EndOfFile);
        Reject(() => registry.Read(other,new(ready.Attachment.Id,0)));
        Reject(() => registry.Read(owner,new(ready.Attachment.Id,-1)));
        Reject(() => registry.Resolve(identity with { InstanceId = "other" }, ready.Attachment.Id));
        Check(registry.ResolveOwned(owner, ready.Attachment).Attachment == ready.Attachment);
        Reject(() => registry.ResolveOwned(other, ready.Attachment));
        Reject(() => registry.ResolveOwned(owner, ready.Attachment with { Width = 32 }));
        registry.Discard(owner, ready.Attachment.Id);
        Check(!File.Exists(request.OutputPath));
        Reject(() => registry.Read(owner,new(ready.Attachment.Id,0)));
        var failed = registry.Enqueue(owner,identity,WindowCaptureKind.Screenshot);
        var bad = registry.Take()!; File.WriteAllBytes(bad.OutputPath,new byte[20]);
        registry.Complete(new(failed.RequestId,640,360,0));
        Check(registry.Status(owner,failed.RequestId).Phase == WindowCapturePhase.Failed && !File.Exists(bad.OutputPath));
        var expired = registry.Enqueue(other,identity,WindowCaptureKind.Video);
        clock.Now += TimeSpan.FromMinutes(3);
        Check(!registry.IsCurrent(expired.RequestId));
        Reject(() => registry.Status(other,expired.RequestId));
        var retired = registry.Enqueue(owner,identity,WindowCaptureKind.Video);
        registry.Take(); registry.Retire(owner);
        Check(!registry.IsCurrent(retired.RequestId));
        var foreground = registry.Enqueue(owner, identity, WindowCaptureKind.Screenshot);
        Check(registry.Take()!.RequestId == foreground.RequestId);
        Reject(() => registry.Cancel(other, foreground.RequestId));
        registry.Cancel(owner, foreground.RequestId);
        Check(!registry.IsCurrent(foreground.RequestId));
        Check(PlatformCapabilities.TryGet(PlatformCapabilities.WindowCaptureV1, out var capability));
        Check(!capability.AllowsBackgroundForOperation(PlatformCapabilities.WindowCaptureRequest));
        Check(capability.AllowsBackgroundForOperation(PlatformCapabilities.WindowCaptureStatus));
        Check(capability.AllowsBackgroundForOperation(PlatformCapabilities.WindowCaptureRead));
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if(!value) throw new Exception("Capture ownership/bounds regression."); }
    private static void Reject(Action action) { try {action();} catch(BrokerException){return;} throw new Exception("Unauthorized capture operation was accepted."); }
    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
