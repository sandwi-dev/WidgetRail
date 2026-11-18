using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class CaptureEncodingSelectionTests
{
    internal static Task Constraints()
    {
        var adapter = new CaptureEncodingAdapter(new string('a',32), "Fixture GPU", new string('b',32));
        var encoding = new CaptureEncodingOptions(CaptureVideoCodec.H264, CaptureVideoPixelFormat.Yuv4208, CaptureVideoRateControl.ConstantBitrate);
        var options = new RecordingOptions(5120,1440,30,12_000_000,Encoder:CaptureEncoderMode.Hardware) { Encoding = encoding };
        var actual = new CaptureRecordingConfiguration(options,adapter,5120,1440);
        Check(actual.IsWellFormed() && new CaptureReplaySettings { Recording = options }.IsWellFormed(), "Ultrawide inside pixel budget rejected");
        var source = new CaptureRecordingSelection { Resolution = CaptureRecordingResolution.Source, FramesPerSecond = 30 };
        Check(source.Allows(actual), "Native ultrawide not accepted");
        Check(!source.Allows(actual with { Recording = options with { MaximumWidth = 3840 } }), "Source silently downscaled");
        Check(!source.Allows(actual with { Recording = options with { FramesPerSecond = 60 } }), "Explicit frame rate overridden");
        Check(!(source with { Codec = CaptureVideoCodec.Hevc }).Allows(actual), "Explicit codec downgraded");
        Check(!(source with { PixelFormat = CaptureVideoPixelFormat.Yuv42010 }).Allows(actual), "Explicit format downgraded");
        Check(!new CaptureReplaySettings { Recording = options with { MaximumHeight = 2160 } }.IsWellFormed(), "Pixel budget widened");
        Check(!(options with { Selection = source }).IsWellFormed(), "Requested and resolved state mixed");
        Check(!new CaptureRecordingSelection { RateControl = CaptureVideoRateControl.Quality, Bitrate = 12_000_000 }.IsWellFormed(), "Quality pretended to have bitrate cap");
        Check(source.Allows(actual with { SourceWidth = 5119 }), "Odd source padding rejected");
        var legacy = new RecordingOptions();
        var json = JsonSerializer.SerializeToElement(legacy, BrokerJson.StrictOptions);
        Check(!json.TryGetProperty("selection", out _) && !json.TryGetProperty("encoding", out _), "Legacy request gained unknown members");
        var advanced = new CaptureReplayStatus(2,1,new() { Recording = options },CaptureReplayPhase.Recording)
            { SessionId = new string('c',32), Resolved = actual };
        var projected = CaptureReplayProjection.ForReader(advanced,false);
        Check(projected.IsWellFormed() && projected.Resolved is null && projected.Settings.Recording.Encoding is null &&
            projected.Settings.Recording.MaximumWidth <= 3840 && projected.Issue == CaptureReplayIssue.SettingsChanged,
            "Legacy reader was allowed to misinterpret advanced settings");
        Check(ReferenceEquals(CaptureReplayProjection.ForReader(advanced,true),advanced), "Detailed reader lost authoritative status");
        var automatic=legacy with{Encoder=CaptureEncoderMode.Hardware,Selection=new(){Resolution=CaptureRecordingResolution.Source}};
        var candidates=CaptureRecordingResolver.Candidates(automatic,adapter,5120,1440).ToArray();
        Check(candidates.All(value=>value.MaximumWidth==5120&&value.MaximumHeight==1440),"Source candidate silently fell back to a smaller frame");
        var hevc=candidates.Single(value=>value.Encoding?.Codec==CaptureVideoCodec.Hevc);
        Check(hevc.EditingSupported==false&&!CaptureRecordingResolver.AllowedByAutomaticPolicy(automatic,hevc),"Auto silently gave up editing support");
        Check(CaptureRecordingResolver.AllowedByAutomaticPolicy(automatic with{Selection=automatic.Selection! with{Codec=CaptureVideoCodec.Hevc}},hevc),"Explicit limited-format choice was discarded");
        Check(!CaptureRecordingResolver.ConsumerCompatible(CaptureVideoCodec.Av1,CaptureVideoPixelFormat.Yuv4208),"AV1 advertised despite known production consumer failures");
        var autoOptions=automatic with{Selection=new()};
        var preserved=CaptureRecordingResolver.Candidates(autoOptions,adapter,2816,1230).First();
        Check(preserved.MaximumWidth==2816&&preserved.MaximumHeight==1230&&preserved.FramesPerSecond==30&&preserved.MaximumBitrate==12_000_000,
            "Auto resampled an ultrawide source below its measured area budget");
        var scaled=CaptureRecordingResolver.Candidates(autoOptions,adapter,5120,1440).First();
        Check((long)scaled.MaximumWidth*scaled.MaximumHeight<=CaptureRecordingResolver.AutomaticPixelBudget&&scaled.MaximumWidth>2560&&
            Math.Abs(scaled.MaximumWidth/(double)scaled.MaximumHeight-5120d/1440)<.01,"Auto imposed a 16:9 box instead of preserving aspect within area");
        var sixty=CaptureRecordingResolver.Candidates(autoOptions with{Selection=new(){FramesPerSecond=60}},adapter,2560,1440).First();
        Check(sixty.FramesPerSecond==60&&sixty.MaximumBitrate==24_000_000,"Explicit60FPS constraint was replaced by measured30FPS default");
        return Task.CompletedTask;
    }

    internal static async Task CacheEpoch()
    {
        var discovery = new CaptureEncoderDiscovery(()=>true,(_,_,_)=>true,()=>true);
        var firstEpoch = new string('a',32); var secondEpoch = new string('b',32);
        var encoding = new CaptureEncodingOptions(CaptureVideoCodec.H264,CaptureVideoPixelFormat.Yuv4208,CaptureVideoRateControl.ConstantBitrate);
        var first = new CaptureRecordingProfile("fixture",1280,720,30,6_000_000,6_000_000,CaptureEncoderMode.Hardware)
            { Encoding = encoding, Adapter = new(new string('c',32),"Fixture GPU",firstEpoch) };
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var calls = 0;
        bool Probe(CaptureRecordingProfile _, int bitrate, bool audio)
        {
            var count = Interlocked.Increment(ref calls);
            if (count == 1) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            return true;
        }
        discovery.ObserveEnvironment(firstEpoch);
        var old = discovery.VerifyEncodingAsync(first,false,Probe);
        Check(entered.Wait(TimeSpan.FromSeconds(5)), "Probe did not enter");
        discovery.ObserveEnvironment(secondEpoch);
        var next = first with { Adapter = first.Adapter with { EnvironmentId = secondEpoch } };
        var current = discovery.VerifyEncodingAsync(next,false,Probe);
        release.Set();
        Check(!await old && await current && calls == 2, "Old driver probe survived epoch change");
        Check(await discovery.VerifyEncodingAsync(next,false,Probe) && calls == 2, "Verified current probe repeated");
        Check(!await discovery.VerifyEncodingAsync(first,false,Probe) && calls == 2, "Stale adapter requested native work");
        var changedGeometry = next with { MaximumWidth = 1920 };
        Check(await discovery.VerifyEncodingAsync(changedGeometry,false,Probe) && calls == 3, "Profile ID aliased different geometry");
        var supported=false;var guardedCalls=0;
        var unavailable=new CaptureEncoderDiscovery(()=>supported,(_,_,_)=>true,()=>true);
        unavailable.ObserveEnvironment(firstEpoch);
        bool GuardedProbe(CaptureRecordingProfile _,int bitrate,bool audio) {++guardedCalls;return true;}
        Check(!await unavailable.VerifyEncodingAsync(first,false,GuardedProbe)&&guardedCalls==0,"Unsupported capture platform started an encoder probe");
        supported=true;unavailable.ObserveEnvironment(secondEpoch);
        Check(await unavailable.VerifyEncodingAsync(next,false,GuardedProbe)&&guardedCalls==1,"Driver epoch retained stale platform availability");
    }
    internal static async Task BrokerFreshness()
    {
        var clock=new Clock();using var registry=new WindowCaptureRegistry(clock);
        var profile=new CaptureRecordingProfile("legacy",1280,720,30,3_000_000,12_000_000,CaptureEncoderMode.Hardware);
        var capabilities=new CaptureCapabilities(true,[CaptureImageFormat.Png],[profile],[CaptureAudioMode.None],true,120);
        registry.SetCapabilities(capabilities);
        Check(ReferenceEquals(await registry.GetCapabilitiesAsync(default),registry.Capabilities),"Warm catalog not reused");
        clock.Now+=TimeSpan.FromMinutes(2);
        var refreshed=registry.GetCapabilitiesAsync(default);
        Check(!refreshed.IsCompleted && registry.NeedsEncodingCapabilities,"Expired catalog suppressed environment observation");
        registry.SetCapabilities(capabilities);await refreshed;
        var options=new RecordingOptions(1280,720,30,6_000_000,Encoder:CaptureEncoderMode.Hardware){Selection=new()};
        var advanced=registry.GetRecordingCapabilitiesAsync(new(options),default);
        Check(!advanced.IsCompleted,"Legacy catalog satisfied an advanced request");
        registry.SetRecordingCapabilities(registry.RecordingDiscoveries.Single().Id,capabilities with{RecordingProfiles=[]});await advanced;
        var observedAgain=registry.GetRecordingCapabilitiesAsync(new(options),default);
        Check(!observedAgain.IsCompleted,"Advanced read skipped current driver observation");
        registry.SetRecordingCapabilities(registry.RecordingDiscoveries.Single().Id,capabilities with{RecordingProfiles=[]});await observedAgain;
    }
    private sealed class Clock:TimeProvider { internal DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
    private static void Check(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
}
