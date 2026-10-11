using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class WindowCaptureTests
{
    internal static async Task ReservationAsync()
    {
        using var registry = new WindowCaptureRegistry(maximumBytes: 4096);
        registry.SetCapabilities(new(true,[CaptureImageFormat.Png],[],[CaptureAudioMode.None],true,120));
        var owner = new object(); var identity = new BrokerWidgetIdentity("example.capture","example","instance");
        var request = new CaptureSessionRequest { Screenshot = new(), MaximumBytes = 2048 };
        var first = registry.EnqueueConfigured(owner,identity,request); var work = registry.Take()!;
        var bytes = new byte[2048]; new byte[] {137,80,78,71,13,10,26,10}.CopyTo(bytes,0);
        await File.WriteAllBytesAsync(work.OutputPath,bytes);
        registry.Complete(new(first.RequestId,1,1,0) { Properties = new(1,1) });
        Check(registry.Status(owner,first.RequestId).Phase == WindowCapturePhase.Ready);
        Reject(() => registry.EnqueueConfigured(owner,identity,request with { MaximumBytes = 3072 }));
        Check(File.Exists(work.OutputPath));
        var second = registry.EnqueueConfigured(owner,identity,request); registry.Take();
        var sourcePath = Path.Combine(Path.GetTempPath(),"capture-reservation-"+Guid.NewGuid().ToString("N")+".png");
        try
        {
            await File.WriteAllBytesAsync(sourcePath,bytes.AsMemory(0,512).ToArray());
            await using var source = new CaptureLibraryReadLease(new(Guid.NewGuid().ToString("N"),"image/png",1,1,512,0,long.MaxValue),
                new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read));
            try { await registry.ImportAsync(owner,identity,source,default); throw new Exception("Import consumed reserved recording budget"); }
            catch (BrokerException error) { Check(error.Code == "capture_limit"); }
        }
        finally { File.Delete(sourcePath); }
        registry.Cancel(owner,second.RequestId);
        var replacement = registry.EnqueueConfigured(owner,identity,request);
        Check(registry.Take()!.RequestId == replacement.RequestId);
        registry.Cancel(owner,replacement.RequestId);
    }
    internal static async Task DiscoveryAsync()
    {
        using var registry = new WindowCaptureRegistry();
        Check(!registry.NeedsCapabilities && !registry.NeedsEncodingCapabilities);
        var image = registry.GetCapabilitiesAsync(default, includeEncoding: false);
        Check(registry.NeedsCapabilities && !registry.NeedsEncodingCapabilities && !image.IsCompleted);
        var basic = new CaptureCapabilities(true, [CaptureImageFormat.Png], [], [CaptureAudioMode.None], true, 120);
        registry.SetCapabilities(basic, encodingProbed: false);
        Check((await image).ImageFormats.Count == 1 && !registry.NeedsEncodingCapabilities);
        var video = registry.GetCapabilitiesAsync(default);
        Check(registry.NeedsEncodingCapabilities && !video.IsCompleted);
        var complete = basic with { RecordingProfiles = [new("hardware",1280,720,15,1_000_000,6_000_000,CaptureEncoderMode.Hardware)] };
        registry.SetCapabilities(complete);
        Check((await video).RecordingProfiles.Count == 1);
        registry.SetCapabilities(basic, encodingProbed: false);
        Check((await registry.GetCapabilitiesAsync(default)).RecordingProfiles.Count == 1);
        using var cancelled = new WindowCaptureRegistry(); using var cancellation = new CancellationTokenSource();
        var pending = cancelled.GetCapabilitiesAsync(cancellation.Token);
        cancellation.Cancel();
        try { await pending; throw new Exception("Cancelled discovery completed"); } catch (OperationCanceledException) { }
        Check(!cancelled.NeedsCapabilities && !cancelled.NeedsEncodingCapabilities);
        var shutdown = cancelled.GetCapabilitiesAsync(default);
        cancelled.Dispose();
        try { await shutdown; throw new Exception("Retired discovery completed"); } catch (OperationCanceledException) { }
    }
    internal static Task ConfiguredAsync()
    {
        using var registry = new WindowCaptureRegistry();
        var owner = new object(); var other = new object();
        var identity = new BrokerWidgetIdentity("example.capture", "example", "instance");
        var options = new CaptureSessionRequest { Recording = new(MaximumDurationSeconds: 12), Preparation = new(0) };
        Check(!registry.Capabilities.Available);
        Reject(() => registry.EnqueueConfigured(owner, identity, options));
        var capabilities = new CaptureCapabilities(true, [CaptureImageFormat.Png],
            [new("software", 1280, 720, 15, 1_000_000, 6_000_000, CaptureEncoderMode.Software)], [CaptureAudioMode.None], true, 120);
        registry.SetCapabilities(capabilities);
        Check(capabilities.Validate(options).Supported);
        Check(!capabilities.Validate(options with { Recording = options.Recording! with { FramesPerSecond = 60 } }).Supported);
        Check(!capabilities.Validate(options with { Recording = options.Recording! with { Encoder = CaptureEncoderMode.Hardware } }).Supported);
        Check(!capabilities.Validate(options with { Recording = options.Recording! with { Audio = CaptureAudioMode.System } }).Supported);
        Check(!capabilities.Validate(options with { MaximumBytes = 1024 }).Supported);
        var ticket = registry.EnqueueConfigured(owner, identity, options);
        var request = registry.Take()!;
        Check(request.Options == options);
        Reject(() => registry.Stop(owner, ticket.RequestId)); // no recording exists yet
        registry.Recording(ticket.RequestId);
        Reject(() => registry.Stop(other, ticket.RequestId));
        registry.Stop(owner, ticket.RequestId);
        Check(registry.IsCurrent(ticket.RequestId) && registry.IsStopRequested(ticket.RequestId));
        File.WriteAllBytes(request.OutputPath, [0, 0, 0, 20, 102, 116, 121, 112, 0, 0, 0, 0, 0, 0, 0, 0]);
        registry.Complete(new(ticket.RequestId, 1280, 720, 2) { Properties = new(1280, 720, 15, 30, 0, CaptureEncoderMode.Software) });
        var status = registry.Status(owner, ticket.RequestId);
        Check(status.Phase == WindowCapturePhase.Ready && status.StopRequested && status.Attachment!.Properties!.EncodedFrames == 30);
        registry.Stop(owner, ticket.RequestId); // lost-reply retry is harmless
        registry.Discard(owner, status.Attachment!.Id);
        var imageRequest = new CaptureSessionRequest { Screenshot = new(), Preparation = new(1), IncludeCursor = true };
        var image = registry.EnqueueConfigured(owner, identity, imageRequest);
        var imageWork = registry.Take()!;
        File.WriteAllBytes(imageWork.OutputPath, [137,80,78,71,13,10,26,10,0,0,0,0,0,0,0,0]);
        registry.Complete(new(image.RequestId, 640, 360, 0) { Properties = new(1280, 720) });
        Check(registry.Status(owner, image.RequestId).Phase == WindowCapturePhase.Failed); // original silently downscaled
        var cancel = registry.EnqueueConfigured(owner, identity, options);
        registry.Take(); registry.Recording(cancel.RequestId); registry.Cancel(owner, cancel.RequestId);
        Check(!registry.IsCurrent(cancel.RequestId) && registry.Status(owner, cancel.RequestId).Attachment is null);
        PlatformCapabilities.TryGet(PlatformCapabilities.WindowCaptureV1, out var capability);
        Check(!capability.AllowsBackgroundForOperation("capture.start") && capability.AllowsBackgroundForOperation("capture.stop"));
        return Task.CompletedTask;
    }
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
