using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

internal static class CaptureApplicationMetadataScenarios
{
    internal static async Task DiscoveryThroughHostPipe()
    {
        var pipe = "capture-discovery-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new WidgetBridgeServer(pipe, new([]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        var registry = WindowCaptureRegistry.Shared;
        var pending = registry.GetCapabilitiesAsync(deadline.Token);
        var next = await session.TakeCaptureAsync(deadline.Token);
        if (next.Request is not null || !next.NeedsCapabilities || !next.NeedsEncodingCapabilities)
            throw new Exception("Capability demand did not cross the frontend pipe independently of capture.");
        var basic = new CaptureCapabilities(true, [CaptureImageFormat.Png], [], [CaptureAudioMode.None], true, 120);
        await session.PublishCaptureCapabilitiesAsync(basic, deadline.Token, encodingProbed: false);
        if (pending.IsCompleted) throw new Exception("Image-only support falsely completed an encoding query.");
        var full = basic with { RecordingProfiles = [new("hardware",1920,1080,30,3_000_000,12_000_000,CaptureEncoderMode.Hardware)] };
        var targeted = registry.GetRecordingCapabilitiesAsync(new(), deadline.Token);
        next = await session.TakeCaptureAsync(deadline.Token);
        var discovery = next.RecordingDiscoveries.Single();
        if (discovery.Query.Recording is not null) throw new Exception("Recommended query changed across host pipe.");
        await session.PublishCaptureCapabilitiesAsync(full, deadline.Token, discoveryId: discovery.Id);
        if ((await targeted).RecordingProfiles.Count != 1 || pending.IsCompleted)
            throw new Exception("Targeted discovery must complete independently of the full catalog.");
        await session.PublishCaptureCapabilitiesAsync(full, deadline.Token);
        if ((await pending).RecordingProfiles.Single().Encoder != CaptureEncoderMode.Hardware)
            throw new Exception("Probed encoder capabilities were lost across the host pipe.");
        next = await session.TakeCaptureAsync(deadline.Token);
        if (next.NeedsCapabilities || next.NeedsEncodingCapabilities) throw new Exception("Completed discovery still requests native work.");
        await session.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }
    internal static async Task ThroughHostPipe()
    {
        var pipe = "capture-app-context-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var target = new NativeWindowPreviewTarget("ABC", 42, "123", "GameClass");
        var backend = new SimulatedPlatformBrokerBackend { TaskWindows =
        [
            new("other", "ChatGPT", "ChatGPT", false) { PreviewTarget = target with { Handle = "DEF", ProcessId = 55 } },
            new("captured", "007 First Light", "007FirstLight", false) { PreviewTarget = target },
        ] };
        await using var server = new WidgetBridgeServer(pipe, new([]), platformBackend: backend);
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        var owner = new object();
        var captures = WindowCaptureRegistry.Shared;
        try
        {
            foreach (var kind in new[] { WindowCaptureKind.Screenshot, WindowCaptureKind.Video })
            {
                var ticket = captures.Enqueue(owner, new("example.capture", "example.publisher", "capture.instance"), kind);
                var pending = captures.Take()!;
                captures.Recording(ticket.RequestId);
                var bytes = new byte[32];
                if (kind == WindowCaptureKind.Screenshot) new byte[] {137,80,78,71,13,10,26,10}.CopyTo(bytes,0);
                else "ftyp"u8.CopyTo(bytes.AsSpan(4));
                await File.WriteAllBytesAsync(pending.OutputPath, bytes, deadline.Token);
                var sourceTarget = kind == WindowCaptureKind.Screenshot ? target : target with { ProcessCreated = "999" };
                var accepted = await session.CompleteCaptureAsync(new(ticket.RequestId, 640, 360,
                    kind == WindowCaptureKind.Video ? 5 : 0, Target: sourceTarget,
                    SourceApplication: new("Ignored caller label", "Ignored")), deadline.Token);
                var ready = captures.Status(owner, ticket.RequestId);
                if (!accepted || ready.Phase != WindowCapturePhase.Ready) throw new Exception("Capture did not complete.");
                var expected = kind == WindowCaptureKind.Screenshot ? new CaptureApplicationContext("007 First Light", "007FirstLight") : null;
                if (ready.Attachment!.SourceApplication != expected) throw new Exception("Capture metadata did not match the exact provider target.");
                captures.Discard(owner, ready.Attachment.Id);
            }
        }
        finally { captures.Retire(owner); }
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
