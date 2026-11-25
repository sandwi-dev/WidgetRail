using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    private static CaptureCapabilities RecordingCapabilities(CaptureRecordingQuery query)
    {
        var options=query.Recording??new(Encoder:CaptureEncoderMode.Hardware);
        var selection=options.Selection;
        CaptureRecordingProfile Profile(int width,int height,int fps,int bitrate)=>new($"profile-{width}-{height}-{fps}",width,height,fps,bitrate,bitrate,CaptureEncoderMode.Hardware)
        {Encoding=new(CaptureVideoCodec.H264,CaptureVideoPixelFormat.Yuv4208,CaptureVideoRateControl.ConstantBitrate),
            Adapter=new(new string('a',32),"Fixture GPU",new string('b',32)),EditingSupported=true};
        var profiles=query.DiscoverAlternatives&&selection==new CaptureRecordingSelection()?
            new[]{Profile(1280,720,15,3_000_000),Profile(1920,1080,30,12_000_000),Profile(2560,1440,60,24_000_000)}:
            new[]{Profile(selection?.MaximumWidth??1920,selection?.MaximumHeight??1080,selection?.FramesPerSecond??30,selection?.Bitrate??12_000_000)};
        return new(true,[CaptureImageFormat.Png],profiles,[CaptureAudioMode.None],true,120);
    }

    internal static async Task RecordingAutoAndDraftQualification()
    {
        await using var f=new GalleryFixture();await f.Start();await f.Action("capture-library.settings"); await f.Action("capture-library.section.Recording.open");
        await f.Until(()=>f.Node("capture-library.recording.manual.codec") is not null&&!f.Busy);
        Check(f.EncodingQueries.Count==1&&f.EncodingQueries[0].DiscoverAlternatives&&f.EncodingQueries[0].Recording?.Selection==new CaptureRecordingSelection(),"Settings opened duplicate or legacy encoder surveys");
        Check(f.Node("capture-library.recording.manual.resolution")!.SelectOptions.Single(value=>value.IsSelected).Label=="Auto (recommended)","New manual preferences did not default to Auto");
        var codec=f.Node("capture-library.recording.manual.codec")!.SelectOptions;
        Check(codec.All(value=>!value.Label.Contains("AV1",StringComparison.Ordinal)),"Unqualified codec exposed from enum");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.EncodingOverride=async(query,token)=>{entered.TrySetResult();await release.Task.WaitAsync(token);return RecordingCapabilities(query);};
        await f.Action(f.Node("capture-library.recording.manual.fps")!.SelectOptions.Single(value=>value.Label=="60 fps").ActionId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(f.Node("capture-library.settings.unsaved") is not null,"Unvalidated recording changes need a warning");
        Check(f.Node("capture-library.recording.manual.save")?.IsDisabled==true,"Unqualified draft could be saved");
        release.SetResult();await f.Until(()=>f.Node("capture-library.recording.manual.save") is {} save&&save.IsDisabled!=true);
        await f.Action(f.Node("capture-library.recording.manual.save")!.ActionId!);
        await f.Until(()=>!f.Busy&&f.Node("capture-library.recording.manual.save") is null);
        using(var stored=System.Text.Json.JsonDocument.Parse(f.PrivateState.Json!))
            Check(stored.RootElement.GetProperty("Schema").GetInt32()==2,"Advanced private settings remained readable as legacy numeric defaults");
        Check(f.Node("capture-library.recording.manual.fps")!.SelectOptions.Single(value=>value.IsSelected).Label=="60 fps"&&
            f.Node("capture-library.recording.manual.resolution")!.SelectOptions.Single(value=>value.IsSelected).Label=="Auto (recommended)","One manual choice overwrote independent Auto constraints");
        Check(ViewSnapshotValidator.Validate(f.View()).Count==0,"Recording settings declaration invalid");
        await using var restored=new GalleryFixture(privateState:new(f.PrivateState.Json!,f.PrivateState.Revision));
        await restored.Start();await restored.Action("capture-library.settings"); await restored.Action("capture-library.section.Recording.open");
        await restored.Until(()=>restored.Node("capture-library.recording.manual.fps") is not null&&!restored.Busy);
        Check(restored.Node("capture-library.recording.manual.fps")!.SelectOptions.Single(value=>value.IsSelected).Label=="60 fps",
            "Schema-two Auto constraints did not round trip");
    }

    internal static async Task RecordingDraftRejectsLateAndExternalChanges()
    {
        await using var f=new GalleryFixture();f.Replay=f.Replay with{Settings=f.Replay.Settings with{Recording=f.Replay.Settings.Recording with{Audio=CaptureAudioMode.None}}};
        await f.Start();await f.Action("capture-library.settings"); await f.Action("capture-library.section.Replay.open");await f.Until(()=>f.Node("capture-library.recording.replay.fps") is not null&&!f.Busy);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.EncodingOverride=async(query,_)=>{entered.TrySetResult();await release.Task;return RecordingCapabilities(query);};
        await f.Action(f.Node("capture-library.recording.replay.fps")!.SelectOptions.Single(value=>value.Label=="60 fps").ActionId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var staleSave=f.Node("capture-library.recording.replay.save")!.ActionId!;
        f.NotifyReplay(f.Replay with{Revision=2,SettingsRevision=1});
        release.SetResult();await f.Until(()=>f.Node("capture-library.recording.replay.save") is null);
        await f.Action(staleSave);Check(f.ReplayConfigurations==0,"Old encoder reply overwrote externally revised settings");
        await f.Action("capture-library.settings.back");
        Check(f.ReplayStarts==0&&f.ReplayConfigurations==0,"Closing a draft started recording or persisted preferences");
    }
}
