using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class CaptureDiscoveryPipeTests
{
    internal static async Task RunAsync()
    {
        using var temp = new TemporaryDirectory();
        var identity = new BrokerWidgetIdentity("capture.test", "capture.test", "capture.instance");
        var consent = new ConsentStore(temp.Path);
        await consent.SetDecisionAsync(identity, PlatformCapabilities.WindowCaptureV1, ConsentDecision.Grant);
        var options = new BrokerPipeTransportOptions { RequestTimeout = TimeSpan.FromMilliseconds(50) };
        var request = new BrokerRequestEnvelope(BrokerJson.ProtocolVersion, 1, identity,
            PlatformCapabilities.WindowCaptureV1, "capture.capabilities", BrokerJson.ToElement(new { }));
        foreach (var operation in new[] { "capture.capabilities", "capture.recording-capabilities", "capture.validate", "capture.start" })
            Check(BrokerPipeRequestTimeoutPolicy.Resolve(options, request with { Operation = operation }) == TimeSpan.FromSeconds(20));
        foreach (var operation in new[] { "capture.status", "capture.read", "capture.cancel", "capture.stop" })
            Check(BrokerPipeRequestTimeoutPolicy.Resolve(options, request with { Operation = operation }) == options.RequestTimeout);
        Check(BrokerPipeRequestTimeoutPolicy.Resolve(options, request with { CapabilityId = PlatformCapabilities.DisplaysReadV1 }) == options.RequestTimeout);
        var pipe = "wrail-cold-capture-" + Guid.NewGuid().ToString("N");
        await using var server = new BrokerPipeServer(pipe, identity, [PlatformCapabilities.WindowCaptureV1],
            consent, new SimulatedPlatformBrokerBackend(), options, new string('C', 64));
        var serving = server.RunAsync();
        await using var client = new BrokerPipeClient(pipe, identity, server.ChannelNonce, options);
        await client.ConnectAsync();
        server.SetLifecycle(BrokerLifecycleState.Interactive);
        using (var cancel = new CancellationTokenSource())
        {
            var pending = client.RequestAsync(PlatformCapabilities.WindowCaptureV1, "capture.capabilities", new { }, cancel.Token);
            await Until(() => WindowCaptureRegistry.Shared.NeedsEncodingCapabilities);
            cancel.Cancel();
            try { await pending; throw new Exception("Discovery ignored caller cancellation."); }
            catch (OperationCanceledException) { }
            await Until(() => !WindowCaptureRegistry.Shared.NeedsEncodingCapabilities);
        }
        var discovery = client.RequestAsync(PlatformCapabilities.WindowCaptureV1, "capture.capabilities", new { });
        await Until(() => WindowCaptureRegistry.Shared.NeedsEncodingCapabilities);
        await Task.Delay(200); // Exceeds the ordinary deadline on both pipe ends.
        Check(!discovery.IsCompleted);
        var capabilities = new CaptureCapabilities(true, [CaptureImageFormat.Png],
            [new("test",1280,720,15,1_000_000,6_000_000,CaptureEncoderMode.Software)],
            [CaptureAudioMode.None,CaptureAudioMode.Application],true,120);
        var targeted = client.RequestAsync(PlatformCapabilities.WindowCaptureV1, "capture.recording-capabilities", new CaptureRecordingQuery());
        await Until(() => WindowCaptureRegistry.Shared.RecordingDiscoveries.Count == 1);
        var probe = WindowCaptureRegistry.Shared.RecordingDiscoveries.Single();
        WindowCaptureRegistry.Shared.SetRecordingCapabilities(probe.Id, capabilities);
        Check((await targeted).Succeeded && !discovery.IsCompleted);
        WindowCaptureRegistry.Shared.SetCapabilities(capabilities);
        var response = await discovery.WaitAsync(TimeSpan.FromSeconds(2));
        Check(response.Succeeded);
        var received = BrokerJson.ParsePayload<CaptureCapabilities>(response.Payload!.Value);
        Check(received.IsWellFormed() && received.AudioModes.Contains(CaptureAudioMode.Application));
        await client.DisposeAsync(); await server.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(2));

        static void Check(bool condition) { if (!condition) throw new Exception("Cold capture discovery deadline contract failed."); }
        static async Task Until(Func<bool> ready)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!ready()) await Task.Delay(5, timeout.Token);
        }
    }
}
