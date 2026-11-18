using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using System.Text.Json;

internal static class BrokerTests
{
    internal static async Task EncodingCompatibility()
    {
        await using var f=new ReplayFixture();
        var consent=new ConsentStore(Path.Combine(f.Root,"encoding-consent"));
        string[] capabilities=[PlatformCapabilities.WindowCaptureV1,PlatformCapabilities.CaptureLibraryReadV1];
        await consent.SetDecisionsAsync(ReplayFixture.Actor,capabilities,ConsentDecision.Grant);
        var simulator=new SimulatedPlatformBrokerBackend();
        var backend=new CompositePlatformBrokerBackend(simulator,simulator,captureLibrary:f.Library){CaptureReplay=f.Service};
        await using var broker=new PlatformCapabilityBroker(ReplayFixture.Actor,capabilities,consent,backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        long sequence=0;
        Task<JsonElement> Read(string capability,string operation,object payload)=>broker.ExecuteAsync(new(1,++sequence,ReplayFixture.Actor,capability,operation,BrokerJson.ToElement(payload)));
        await using var legacy=await broker.SubscribeAsync(PlatformCapabilities.CaptureLibraryReadV1,"capture.replay.changed");
        await using var detailed=await broker.SubscribeAsync(PlatformCapabilities.CaptureLibraryReadV1,"capture.replay.encoding-changed");
        var requested=f.Configuration.Settings with {Recording=f.Configuration.Settings.Recording with {Selection=new(){Resolution=CaptureRecordingResolution.Source,FramesPerSecond=30}}};
        await Read(PlatformCapabilities.WindowCaptureV1,"capture.replay.configure",new CaptureReplaySettingsUpdate(0,requested){EncodingDetails=true});
        var oldStatus=(await Read(PlatformCapabilities.CaptureLibraryReadV1,"capture.replay.get",new{})).Deserialize<CaptureReplayStatus>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var newStatus=(await Read(PlatformCapabilities.CaptureLibraryReadV1,"capture.replay.get",new CaptureReplayReadRequest{EncodingDetails=true})).Deserialize<CaptureReplayStatus>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        PolicyTests.Check(oldStatus.Settings.Recording.Selection is null && oldStatus.Issue==CaptureReplayIssue.SettingsChanged &&
            newStatus.Settings.Recording.Selection==requested.Recording.Selection && newStatus.Revision==oldStatus.Revision,
            "Detailed and old readers diverged in authority or leaked unknown fields");
        await Reject(()=>Read(PlatformCapabilities.WindowCaptureV1,"capture.replay.configure",new CaptureReplaySettingsUpdate(newStatus.SettingsRevision,oldStatus.Settings)),"capture_configuration_requires_update");
        PolicyTests.Check(f.Configuration.Settings==requested,"Old settings editor erased advanced preferences");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var oldEvent=await legacy.ReadAsync(deadline.Token);var newEvent=await detailed.ReadAsync(deadline.Token);
        PolicyTests.Check(!oldEvent.Payload.GetRawText().Contains("selection",StringComparison.Ordinal) &&
            newEvent.Payload.GetRawText().Contains("selection",StringComparison.Ordinal),"Subscription detail boundary failed");
        await f.Service.PollHostAsync();
        var starting=f.Service.StartAsync(new object(),ReplayFixture.Actor);
        var command=(await f.Service.PollHostAsync()).Command!;
        var resolved=new CaptureRecordingConfiguration(new(5120,1440,30,12_000_000,Encoder:CaptureEncoderMode.Hardware,Audio:CaptureAudioMode.Application)
            {Encoding=new(CaptureVideoCodec.H264,CaptureVideoPixelFormat.Yuv4208,CaptureVideoRateControl.ConstantBitrate)},
            new(new string('a',32),"Fixture GPU",new string('b',32)),5120,1440);
        try { await f.Service.CompleteHostAsync(f.Started(command) with {Resolved=resolved with {Recording=resolved.Recording with {MaximumWidth=3840}}});throw new InvalidOperationException("Source downgrade admitted"); }
        catch(BrokerException error) when(error.Code=="invalid_capture_configuration") { }
        PolicyTests.Check(await f.Service.CompleteHostAsync(f.Started(command) with {Resolved=resolved}),"Valid exact source resolution rejected");
        PolicyTests.Check((await starting).Status.Resolved==resolved,"Resolved settings were not reported");
    }

    internal static async Task Policy()
    {
        await using var f = new ReplayFixture();
        var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
        string[] capabilities = [PlatformCapabilities.WindowCaptureV1, PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibrarySaveV1];
        await consent.SetDecisionsAsync(ReplayFixture.Actor, capabilities, ConsentDecision.Grant);
        var simulator = new SimulatedPlatformBrokerBackend();
        var backend = new CompositePlatformBrokerBackend(simulator, simulator, captureLibrary: f.Library) { CaptureReplay = f.Service };
        await using var broker = new PlatformCapabilityBroker(ReplayFixture.Actor, capabilities, consent, backend);
        var sequence = 0L;
        Task<System.Text.Json.JsonElement> Request(string capability, string operation, object payload) => broker.ExecuteAsync(new(
            1, Interlocked.Increment(ref sequence), ReplayFixture.Actor, capability, operation, BrokerJson.ToElement(payload)));
        broker.SetLifecycle(BrokerLifecycleState.Background);
        PolicyTests.Check((await Request(PlatformCapabilities.CaptureLibraryReadV1, "capture.replay.get", new { })).Deserialize<CaptureReplayStatus>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Phase == CaptureReplayPhase.Off,
            "Replay state was not a background read");
        await Reject(() => Request(PlatformCapabilities.WindowCaptureV1, "capture.replay.start", new { }), "lifecycle_denied");
        await using var subscription = await broker.SubscribeAsync(PlatformCapabilities.CaptureLibraryReadV1, "capture.replay.changed");
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var changed = await subscription.ReadAsync(deadline.Token);
        PolicyTests.Check(changed.EventType == "capture.replay.changed", "Replay watcher missed shared read event");
        var saving = Request(PlatformCapabilities.WindowCaptureV1, "capture.replay.save", new CaptureReplaySaveRequest(start.SessionId!, Guid.NewGuid().ToString("N")));
        var output = await f.WaitSave(); broker.SetLifecycle(BrokerLifecycleState.Background);
        await Task.Delay(30); PolicyTests.Check(!saving.IsCompleted, "Secondary save grant cancelled admitted replay on focus loss");
        await File.WriteAllBytesAsync(output.OutputPath!, ReplayFixture.Bytes); await f.Service.CompleteHostAsync(f.Encoded(output));
        PolicyTests.Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Deserialize<CaptureReplaySaveResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Outcome == CaptureReplayOutcome.Accepted,
            "Admitted replay completion did not survive background");
        await Reject(() => Request(PlatformCapabilities.WindowCaptureV1, "capture.replay.save", new CaptureReplaySaveRequest(start.SessionId!, Guid.NewGuid().ToString("N"))), "lifecycle_denied");
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await consent.SetDecisionAsync(ReplayFixture.Actor, PlatformCapabilities.CaptureLibrarySaveV1, ConsentDecision.Deny);
        await broker.RefreshConsentAsync();
        await Reject(() => Request(PlatformCapabilities.WindowCaptureV1, "capture.replay.save", new CaptureReplaySaveRequest(start.SessionId!, Guid.NewGuid().ToString("N"))));
        await broker.DisposeAsync();
        PolicyTests.Check(f.Service.Status.SessionId == start.SessionId, "Widget channel disposal stopped host recording");
    }
    private static async Task Reject(Func<Task<System.Text.Json.JsonElement>> action, string? code = null)
    {
        try { await action(); } catch (BrokerException error) when (code is null || error.Code == code) { return; }
        throw new InvalidOperationException("Unauthorized replay operation was accepted");
    }
}
