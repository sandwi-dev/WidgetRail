using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ConfiguredCaptureTests
{
    internal static async Task Contract()
    {
        var caps = new CaptureCapabilities(true, [CaptureImageFormat.Png],
            [new("software", 1280, 720, 15, 1_000_000, 6_000_000, CaptureEncoderMode.Software)], [CaptureAudioMode.None], true, 120);
        CaptureSessionRequest? requested = null;
        var stops = 0;
        var bytes = Enumerable.Range(0, 70000).Select(i => (byte)i).ToArray();
        var client = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetCaptureCapabilities.Capabilities, (_, _) => ValueTask.FromResult(caps))
            .WithHandler(WidgetCaptureCapabilities.Validate, (r, _) => ValueTask.FromResult(caps.Validate(r)))
            .WithHandler(WidgetCaptureCapabilities.Start, (r, _) => { requested = r; return ValueTask.FromResult(new WindowCaptureTicket(new string('a', 32))); })
            .WithHandler(WidgetCaptureCapabilities.Stop, (_, _) => { stops++; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
            .WithHandler(WidgetCaptureCapabilities.Read, (r, _) => {
                var count = Math.Min(CaptureLimits.ChunkBytes, bytes.Length - (int)r.Offset);
                return ValueTask.FromResult(new CaptureReadChunk(r.Offset, bytes.AsSpan((int)r.Offset, count).ToArray(), r.Offset + count == bytes.Length)); })
            .Build();
        Check((await client.Capture.GetCapabilitiesAsync()).IsWellFormed(), "Capabilities lost");
        var request = new CaptureSessionRequest { Recording = new(MaximumWidth: 640, MaximumHeight: 360, MaximumDurationSeconds: 10), Preparation = new(0) };
        Check((await client.Capture.ValidateAsync(request)).Supported && requested is null, "Validation started a recording");
        var withAudio = request with { Recording = request.Recording! with { Audio = CaptureAudioMode.Application } };
        Check(caps.Validate(withAudio).ErrorCode == "capture_audio_unsupported", "Unadvertised audio accepted");
        var audioCaps = caps with { AudioModes = [CaptureAudioMode.None, CaptureAudioMode.Application] };
        var silentBudget = (long)request.Recording!.Bitrate * request.Recording.MaximumDurationSeconds / 8 + 1024 * 1024;
        Check(audioCaps.Validate(request with { MaximumBytes = silentBudget }).Supported &&
            audioCaps.Validate(withAudio with { MaximumBytes = silentBudget }).ErrorCode == "capture_output_budget_too_small",
            "AAC bytes were not included in admission budget");
        Check(audioCaps.Validate(withAudio with { MaximumBytes = silentBudget + (long)CaptureLimits.AudioBitrate * request.Recording.MaximumDurationSeconds / 8 }).Supported,
            "Sufficient AAC budget was rejected");
        var ticket = await client.Capture.StartAsync(request);
        Check(requested == request, "SDK rewrote the requested options");
        await client.Capture.StopAsync(ticket); Check(stops == 1, "Stop not forwarded");
        var media = new CaptureAttachment(new string('b',32), "video/mp4", 640, 360, bytes.Length, 10, long.MaxValue) {
            Properties = new(1920,1080,15,150,0,CaptureEncoderMode.Software) };
        Check(media.IsWellFormed() && !(media with { Properties = null }).IsWellFormed(), "Legacy video bounds were silently broadened");
        using var target = new MemoryStream(); await client.Capture.CopyContentToAsync(media, target);
        Check(target.ToArray().SequenceEqual(bytes), "Streamed media changed or truncated");
        var snapshot = new WidgetView(UI.CapturedMedia(media, "preview")).CreateSnapshot("capture-test", 1);
        Check(snapshot.ProtocolVersion == 79 && ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 78 }).Count > 0, "Configured media was admitted to old hosts");
        foreach (var invalid in new[] {
            request with { Screenshot = new() }, request with { Preparation = new(-1) },
            request with { Recording = request.Recording! with { FramesPerSecond = 0 } },
            new CaptureSessionRequest { Screenshot = new(100, null) } })
            Check(!invalid.IsWellFormed(), "Malformed capture request accepted");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
