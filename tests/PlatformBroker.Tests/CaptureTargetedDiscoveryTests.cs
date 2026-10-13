using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Capture;

internal static class CaptureTargetedDiscoveryTests
{
    internal static async Task RunAsync()
    {
        var calls = new List<(string Profile, bool Audio)>(); var audio = 0;
        var native = new CaptureEncoderDiscovery(() => true, (profile, _, withAudio) =>
        { calls.Add((profile.Id, withAudio)); return true; }, () => { ++audio; return true; });
        var first = await native.DiscoverAsync(new());
        Check(first.RecordingProfiles.Single().Id == "1080p30-hardware" && calls.Count == 2 && audio == 0);
        var recording = new RecordingOptions(1920,1080,30,7_500_000,Encoder: CaptureEncoderMode.Hardware);
        await Task.WhenAll(Enumerable.Range(0,8).Select(_ => native.DiscoverAsync(new(recording))));
        Check(calls.Count == 2 && audio == 0);
        var sound = await native.DiscoverAsync(new(recording with { Audio = CaptureAudioMode.Application }));
        Check(sound.AudioModes.Contains(CaptureAudioMode.Application) && calls.Count == 4 && audio == 1);
        var all = await native.DiscoverAsync();
        Check(all.RecordingProfiles.Count == 5 && calls.Count == 20 && audio == 1);
        await native.DiscoverAsync(); Check(calls.Count == 20 && audio == 1);
        var failed = new CaptureEncoderDiscovery(() => true, (profile, _, _) => profile.Encoder == CaptureEncoderMode.Software, () => throw new Exception("Unrequested audio probe"));
        Check((await failed.DiscoverAsync(new())).RecordingProfiles.Single().Encoder == CaptureEncoderMode.Software);
        Check((await failed.DiscoverAsync(new(recording))).RecordingProfiles.Count == 0);

        using var registry = new WindowCaptureRegistry();
        using var cancelled = new CancellationTokenSource();
        var waiting = registry.GetRecordingCapabilitiesAsync(new(), cancelled.Token);
        var shared = registry.GetRecordingCapabilitiesAsync(new(), default);
        var work = registry.RecordingDiscoveries.Single();
        cancelled.Cancel();
        try { await waiting; throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        Check(registry.RecordingDiscoveries.Single() == work);
        registry.SetRecordingCapabilities(work.Id, first);
        Check((await shared).RecordingProfiles.Count == 1);
        Check((await registry.GetRecordingCapabilitiesAsync(new(recording), default)).Validate(new() { Recording = recording }).Supported);
        Check(registry.RecordingDiscoveries.Count == 0 && !registry.NeedsEncodingCapabilities);
        var catalog = registry.GetCapabilitiesAsync(default);
        Check(!catalog.IsCompleted && registry.NeedsEncodingCapabilities);
        using var abandoned = new CancellationTokenSource();
        var abandonedTask = registry.GetRecordingCapabilitiesAsync(new(recording with { Audio = CaptureAudioMode.Application }), abandoned.Token);
        var old = registry.RecordingDiscoveries.Single(); abandoned.Cancel();
        try { await abandonedTask; } catch (OperationCanceledException) { }
        var replacement = registry.GetRecordingCapabilitiesAsync(old.Query, default);
        registry.SetRecordingCapabilities(old.Id, sound);
        Check(!replacement.IsCompleted);
        registry.SetRecordingCapabilities(registry.RecordingDiscoveries.Single().Id, sound);
        await replacement;
        registry.SetCapabilities(all); await catalog;
        Check(registry.RecordingDiscoveries.Count == 0);
        using var narrowAudio = new WindowCaptureRegistry();
        narrowAudio.SetCapabilities(all with { AudioModes = [CaptureAudioMode.None] });
        var audioRequest = narrowAudio.GetRecordingCapabilitiesAsync(new(recording with { Audio = CaptureAudioMode.Application }), default);
        narrowAudio.SetRecordingCapabilities(narrowAudio.RecordingDiscoveries.Single().Id, sound);
        Check((await audioRequest).AudioModes.Contains(CaptureAudioMode.Application));
        using var bounded = new WindowCaptureRegistry();
        var pending = Enumerable.Range(0,16).Select(i => bounded.GetRecordingCapabilitiesAsync(new(recording with { Bitrate = 3_000_000+i }), default)).ToArray();
        try { await bounded.GetRecordingCapabilitiesAsync(new(recording with { Bitrate = 4_000_000 }), default); throw new Exception("Unbounded discovery admitted"); }
        catch (BrokerException error) { Check(error.Code == "capture_busy"); }
        bounded.Dispose();
        foreach (var task in pending) { try { await task; throw new Exception("Retired discovery completed"); } catch (OperationCanceledException) { } }
    }
    private static void Check(bool value) { if (!value) throw new Exception("Targeted capture discovery contract failed"); }
}
