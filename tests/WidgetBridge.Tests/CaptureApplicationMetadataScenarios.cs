using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

internal static class CaptureApplicationMetadataScenarios
{
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
