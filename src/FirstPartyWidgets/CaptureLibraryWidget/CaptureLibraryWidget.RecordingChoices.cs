using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private sealed record RecordingDraft(long Revision,RecordingOptions Recording);
    private sealed record RecordingChoicesState(RecordingDraft? Manual=null,RecordingDraft? Replay=null,bool ManualAdvanced=false,bool ReplayAdvanced=false);
    private WidgetModel<RecordingChoicesState> recordingChoices=null!;
    private WidgetResource<CaptureCapabilities> recordingCatalog=null!;
    private WidgetQueryResource<RecordingDraft,CaptureCapabilities> manualRecordingCheck=null!,replayRecordingCheck=null!;
    private static RecordingOptions RecommendedVideo()=>new(Encoder:CaptureEncoderMode.Hardware){Selection=new()};
    private void InitializeRecordingChoices()
    {
        recordingChoices=CreateModel(new RecordingChoicesState());
        recordingCatalog=CreateResource<CaptureCapabilities>("capture-library.recording.catalog",new()
        {
            Load=token=>HostServices.Capture.GetRecordingChoicesAsync(RecommendedVideo(),token),
            MapError=RecordingError,CacheDuration=TimeSpan.FromMinutes(1),RetainLastGoodValue=false,
        });
        manualRecordingCheck=Check("manual");replayRecordingCheck=Check("replay");
        WidgetQueryResource<RecordingDraft,CaptureCapabilities> Check(string scope)=>CreateQueryResource<RecordingDraft,CaptureCapabilities>("capture-library.recording."+scope+".check",new()
        {
            Load=(draft,token)=>HostServices.Capture.GetRecordingCapabilitiesAsync(draft.Recording,token),
            MapError=RecordingError,CacheDuration=TimeSpan.FromMinutes(1),RetainLastGoodValue=false,
        });
    }
    private static WidgetResourceError RecordingError(Exception error)=>error is WidgetCapabilityException capability?
        new(capability.ErrorCode,Explain(capability.ErrorCode)):new("capture_encoding_unavailable","Recording options could not be checked. Try again.");
    private State RecordingState(State state)=>state with{Capabilities=recordingCatalog.Snapshot.Value??state.Capabilities};
    private static CaptureRecordingSelection SelectionOf(RecordingOptions value)=>value.Selection??new()
    {
        Resolution=CaptureRecordingResolution.Bounded,MaximumWidth=value.MaximumWidth,MaximumHeight=value.MaximumHeight,
        FramesPerSecond=value.FramesPerSecond,Bitrate=value.Encoding?.RateControl==CaptureVideoRateControl.Quality?null:value.Bitrate,
        Codec=value.Encoding?.Codec??CaptureVideoCodec.H264,PixelFormat=value.Encoding?.PixelFormat??CaptureVideoPixelFormat.Yuv4208,
        RateControl=value.Encoding?.RateControl??CaptureVideoRateControl.Auto,Quality=value.Encoding?.Quality,
    };
    private static string RecordingAction(string scope,long revision,string action)=>$"capture-library.recording.{scope}.{revision.ToString(CultureInfo.InvariantCulture)}.{action}";
    private RecordingDraft? CurrentRecordingDraft(bool replay,State state)
    {
        var draft=replay?recordingChoices.Value.Replay:recordingChoices.Value.Manual;
        return draft?.Revision==(replay?state.Replay?.SettingsRevision:state.PreferencesRevision)?draft:null;
    }
    private bool HandleRecordingChoiceAction(WidgetActionEvent action)
    {
        if(action.ActionId=="capture-library.settings.back")
        {
            recordingCatalog.Cancel();manualRecordingCheck.Reset();replayRecordingCheck.Reset();
            recordingChoices.Update(value=>value with{Manual=null,Replay=null});return false;
        }
        const string prefix="capture-library.recording.";
        if(!action.ActionId.StartsWith(prefix,StringComparison.Ordinal))return false;
        var state=RecordingState(model.Value);
        if(!state.Settings || state.Busy || Operations.IsBusy(ReplayOperation))return true;
        if(action.ActionId==prefix+"refresh") {recordingCatalog.Refresh();return true;}
        var parts=action.ActionId[prefix.Length..].Split('.',3);
        if(parts.Length!=3 || parts[0] is not ("manual" or "replay") || !long.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out var revision))return true;
        var replay=parts[0]=="replay";
        if(replay && (state.Replay is not {} status || revision!=status.SettingsRevision) ||
            !replay && (state.Preferences is null || revision!=state.PreferencesRevision))return true;
        var check=replay?replayRecordingCheck:manualRecordingCheck;
        var draft=CurrentRecordingDraft(replay,state);
        var current=replay?state.Replay!.Settings.Recording:state.Preferences!.Video??RecommendedVideo();
        var option=parts[2];
        if(option is "advanced.on" or "advanced.off")
        {recordingChoices.Update(value=>replay?value with{ReplayAdvanced=option.EndsWith("on",StringComparison.Ordinal)}:value with{ManualAdvanced=option.EndsWith("on",StringComparison.Ordinal)});return true;}
        if(option=="cancel")
        {ClearRecordingDraft(replay);check.Reset();return true;}
        if(replay&&ReplaySettingsLocked(state.Replay!))return true;
        if(option=="retry" && draft is not null) {check.EnsureLoaded(draft,forceRefresh:true);return true;}
        if(option=="save")
        {
            if(draft is null)return true;
            var verified=check.GetSnapshot(draft);
            if(verified.Status!=WidgetResourceStatus.Ready || verified.Value is not {} capabilities ||
                !capabilities.Validate(new(){Recording=replay?draft.Recording with{MaximumDurationSeconds=5}:draft.Recording}).Supported)return true;
            if(replay)
            {
                var expected=state.Replay!;
                ReplayCommand(async token=>
                {
                    await RequirePermissionsAsync([WidgetCaptureCapabilities.Permission],token);
                    var result=await HostServices.Capture.Replay.ConfigureAsync(expected.Settings with{Recording=draft.Recording},revision,token);
                    token.ThrowIfCancellationRequested();ApplyReplay(result.Status);
                    if(result.Outcome==CaptureReplayOutcome.Accepted)ClearRecordingDraft(true,draft);
                    model.Update(value=>value with{ReplayNotice=result.Outcome==CaptureReplayOutcome.Accepted?"Recording settings saved.":ReplaySettingsIssue(result.Issue)});
                });
            }
            else SavePreferences(state.Preferences! with{Video=draft.Recording},state,()=>ClearRecordingDraft(false,draft));
            return true;
        }
        var recording=draft?.Recording??current;var selection=SelectionOf(recording);
        if(option=="recommended")selection=new();
        else if(option=="resolution.auto")selection=selection with{Resolution=CaptureRecordingResolution.Auto,MaximumWidth=null,MaximumHeight=null};
        else if(option=="resolution.source")selection=selection with{Resolution=CaptureRecordingResolution.Source,MaximumWidth=null,MaximumHeight=null};
        else if(option.StartsWith("resolution.",StringComparison.Ordinal))
        {
            var size=option[11..].Split('x');
            if(size.Length!=2 || !int.TryParse(size[0],out var width) || !int.TryParse(size[1],out var height) ||
                !AvailableProfiles(state).Any(profile=>profile.MaximumWidth==width&&profile.MaximumHeight==height))return true;
            selection=selection with{Resolution=CaptureRecordingResolution.Bounded,MaximumWidth=width,MaximumHeight=height};
        }
        else if(option=="fps.auto")selection=selection with{FramesPerSecond=null};
        else if(option.StartsWith("fps.",StringComparison.Ordinal)&&int.TryParse(option[4..],out var fps)&&AvailableProfiles(state).Any(profile=>profile.FramesPerSecond==fps))selection=selection with{FramesPerSecond=fps};
        else if(option.StartsWith("codec.",StringComparison.Ordinal)&&Enum.TryParse<CaptureVideoCodec>(option[6..],out var codec)&&
            (codec==CaptureVideoCodec.Auto||AvailableProfiles(state).Any(profile=>profile.Encoding?.Codec==codec)))selection=selection with{Codec=codec};
        else if(option.StartsWith("format.",StringComparison.Ordinal)&&Enum.TryParse<CaptureVideoPixelFormat>(option[7..],out var format)&&
            (format==CaptureVideoPixelFormat.Auto||AvailableProfiles(state).Any(profile=>profile.Encoding?.PixelFormat==format)))selection=selection with{PixelFormat=format};
        else if(option.StartsWith("rate.",StringComparison.Ordinal)&&Enum.TryParse<CaptureVideoRateControl>(option[5..],out var rate)&&
            (rate==CaptureVideoRateControl.Auto||AvailableProfiles(state).Any(profile=>profile.Encoding?.RateControl==rate)))
            selection=selection with{RateControl=rate,Quality=rate==CaptureVideoRateControl.Quality?selection.Quality??75:null,Bitrate=rate==CaptureVideoRateControl.Quality?null:selection.Bitrate};
        else if(option=="bitrate")
        {
            if(string.IsNullOrWhiteSpace(action.CommittedText))selection=selection with{Bitrate=null};
            else if(double.TryParse(action.CommittedText,NumberStyles.Float,CultureInfo.InvariantCulture,out var mbps)&&double.IsFinite(mbps)&&mbps is >=.1 and <=100)
                selection=selection with{Bitrate=(int)Math.Round(mbps*1_000_000),Quality=null,RateControl=selection.RateControl==CaptureVideoRateControl.Quality?CaptureVideoRateControl.Auto:selection.RateControl};
            else
            {
                model.Update(value=>replay?value with{ReplayNotice="Enter a bitrate from 0.1 to 100 Mbps, or leave it empty for Auto."}:
                    value with{Message="Enter a bitrate from 0.1 to 100 Mbps, or leave it empty for Auto.",PermissionRequired=false});
                PublishFeedback("Enter a bitrate from 0.1 to 100 Mbps, or leave it empty for Auto.");
                return true;
            }
        }
        else if(option=="quality" && selection.RateControl==CaptureVideoRateControl.Quality &&
            int.TryParse(action.CommittedText,NumberStyles.None,CultureInfo.InvariantCulture,out var quality)&&quality is >=0 and <=100)
            selection=selection with{Quality=quality,Bitrate=null};
        else return true;
        var next=recording with{Selection=selection,Encoding=null,Encoder=CaptureEncoderMode.Hardware};
        if(!next.IsWellFormed())return true;
        if (selection == SelectionOf(current))
        {
            ClearRecordingDraft(replay);
            check.Reset();
            return true;
        }
        var selected=new RecordingDraft(revision,next);
        recordingChoices.Update(value=>replay?value with{Replay=selected}:value with{Manual=selected});
        check.EnsureLoaded(selected);
        return true;
    }
    private void ClearRecordingDraft(bool replay,RecordingDraft? expected=null)=>recordingChoices.Update(value=>replay?
        expected is null||value.Replay==expected?value with{Replay=null}:value:
        expected is null||value.Manual==expected?value with{Manual=null}:value);
    private static IReadOnlyList<CaptureRecordingProfile> AvailableProfiles(State state)=>state.Capabilities?.RecordingProfiles??[];
    private WidgetElement RecordingFields(string scope,State state,RecordingOptions saved,bool locked)
    {
        var replay=scope=="replay";var revision=replay?state.Replay!.SettingsRevision:state.PreferencesRevision;
        var draft=CurrentRecordingDraft(replay,state);var recording=draft?.Recording??saved;var selection=SelectionOf(recording);
        var profiles=AvailableProfiles(state);var id="capture-library.recording."+scope;
        string Action(string value)=>RecordingAction(scope,revision,value);
        SelectOption Choice(string key,string label,bool selected)=>new(key,label,Action(key),selected);
        WidgetElement Field(string key,string label,IEnumerable<SelectOption> choices,string? help=null)=>
            UI.SettingsField(id+"."+key+".row",label,UI.Select(label,choices.ToArray(),id+"."+key).Disabled(locked),help);
        var items=new List<WidgetElement>();
        var sizes=new List<SelectOption>{Choice("resolution.auto","Auto (recommended)",selection.Resolution==CaptureRecordingResolution.Auto),Choice("resolution.source","Source size",selection.Resolution==CaptureRecordingResolution.Source)};
        foreach(var size in profiles.Select(profile=>(profile.MaximumWidth,profile.MaximumHeight)).Distinct().OrderBy(value=>value.MaximumHeight).ThenBy(value=>value.MaximumWidth))
            sizes.Add(Choice($"resolution.{size.MaximumWidth}x{size.MaximumHeight}",$"Up to {size.MaximumWidth} × {size.MaximumHeight}",selection.Resolution==CaptureRecordingResolution.Bounded&&selection.MaximumWidth==size.MaximumWidth&&selection.MaximumHeight==size.MaximumHeight));
        if(!sizes.Any(value=>value.IsSelected))sizes.Insert(0,new("current",$"{selection.MaximumWidth} × {selection.MaximumHeight} (current)",Action("current"),true,IsDisabled:true));
        items.Add(Field("resolution","Resolution",sizes,"Source size is checked when recording starts. Smaller windows are never enlarged."));
        var fps=new List<SelectOption>{Choice("fps.auto","Auto (recommended)",selection.FramesPerSecond is null)};
        fps.AddRange(profiles.Select(profile=>profile.FramesPerSecond).Distinct().Order().Select(value=>Choice("fps."+value,value+" fps",selection.FramesPerSecond==value)));
        if(!fps.Any(value=>value.IsSelected))fps.Add(new("current",selection.FramesPerSecond+" fps (current)",Action("current"),true,IsDisabled:true));
        items.Add(Field("fps","Frame rate",fps));
        var codecs=new[]{CaptureVideoCodec.Auto}.Concat(profiles.Where(profile=>profile.Encoding is not null).Select(profile=>profile.Encoding!.Codec)).Append(selection.Codec).Distinct();
        items.Add(Field("codec","Video format",codecs.Select(value=>Choice("codec."+value,CodecLabel(value),selection.Codec==value) with{IsDisabled=value!=CaptureVideoCodec.Auto&&!profiles.Any(profile=>profile.Encoding?.Codec==value)})));
        var advanced=replay?recordingChoices.Value.ReplayAdvanced:recordingChoices.Value.ManualAdvanced;
        items.Add(UI.Disclosure(id+".advanced","Advanced",advanced,Action("advanced.on"),Action("advanced.off"),()=>
        {
            var fields=new List<WidgetElement>();
            var formats=new[]{CaptureVideoPixelFormat.Auto}.Concat(profiles.Where(profile=>profile.Encoding is not null).Select(profile=>profile.Encoding!.PixelFormat)).Append(selection.PixelFormat).Distinct();
            fields.Add(Field("format","Color format",formats.Select(value=>Choice("format."+value,FormatLabel(value),selection.PixelFormat==value) with{IsDisabled=value!=CaptureVideoPixelFormat.Auto&&!profiles.Any(profile=>profile.Encoding?.PixelFormat==value)}),"10-bit encoding does not turn an 8-bit source into HDR."));
            var rates=new[]{CaptureVideoRateControl.Auto}.Concat(profiles.Where(profile=>profile.Encoding is not null).Select(profile=>profile.Encoding!.RateControl)).Append(selection.RateControl).Distinct();
            fields.Add(Field("rate","Rate control",rates.Select(value=>Choice("rate."+value,RateLabel(value),selection.RateControl==value) with{IsDisabled=value!=CaptureVideoRateControl.Auto&&!profiles.Any(profile=>profile.Encoding?.RateControl==value)})));
            if(selection.RateControl!=CaptureVideoRateControl.Quality)
                fields.Add(UI.SettingsField(id+".bitrate.row","Bitrate (Mbps)",UI.TextEntry(selection.Bitrate is {} bitrate?(bitrate/1_000_000d).ToString("0.###",CultureInfo.InvariantCulture):"",
                    "Auto",Action("bitrate"),id+".bitrate",12).Disabled(locked),"Leave empty for Auto. Higher rates retain more detail but reduce replay time within the memory limit."));
            else if(profiles.Any(profile=>profile.Encoding?.RateControl==CaptureVideoRateControl.Quality))
                fields.Add(UI.SettingsField(id+".quality.row","Quality",UI.TextEntry((selection.Quality??75).ToString(CultureInfo.InvariantCulture),
                    "0–100",Action("quality"),id+".quality",3).Disabled(locked),"This encoder-specific quality level is checked before saving. It does not promise a bitrate ceiling."));
            return fields;
        }));
        var editingProfiles=profiles.Where(profile=>profile.Encoding is {} encoding&&
            (selection.Codec==CaptureVideoCodec.Auto||encoding.Codec==selection.Codec)&&
            (selection.PixelFormat==CaptureVideoPixelFormat.Auto||encoding.PixelFormat==selection.PixelFormat)).ToArray();
        if((selection.Codec!=CaptureVideoCodec.Auto||selection.PixelFormat!=CaptureVideoPixelFormat.Auto)&&
            editingProfiles.Length>0&&editingProfiles.All(profile=>profile.EditingSupported==false))
            items.Add(UI.Text("Trimming is unavailable for this format. Choose H.264 to keep editing support.",id+".editing").Classes("capture-help"));
        if(recording.Selection is null)items.Add(UI.Text(recording.Encoder==CaptureEncoderMode.Software?
            "Your saved software recording settings are unchanged. New Auto choices use hardware encoding.":"Your saved explicit settings are unchanged.",id+".legacy").Classes("capture-help"));
        if(selection!=new CaptureRecordingSelection())items.Add(UI.Button("Reset video choices to Auto",Action("recommended"),id+".recommended").Disabled(locked));
        if(draft is not null)
        {
            var snapshot=(replay?replayRecordingCheck:manualRecordingCheck).GetSnapshot(draft);
            var valid=snapshot.Status==WidgetResourceStatus.Ready&&snapshot.Value?.Validate(new(){Recording=replay?draft.Recording with{MaximumDurationSeconds=5}:draft.Recording}).Supported==true;
            var text=snapshot.Error?.Message??(snapshot.Status is WidgetResourceStatus.Loading or WidgetResourceStatus.NotLoaded?"Checking this combination…":valid?"This combination is available. Source size is checked at start.":"This combination isn't available. Choose another format, resolution or frame rate.");
            items.Add(UI.Text(text,id+".validation").Classes("capture-help"));
            items.Add(UI.Row(id+".actions",UI.Button("Save changes",Action("save"),id+".save").Disabled(locked||!valid).Classes("capture-primary"),
                UI.Button("Cancel",Action("cancel"),id+".cancel").Disabled(state.Busy||Operations.IsBusy(ReplayOperation))));
            if(snapshot.Status==WidgetResourceStatus.Error)items.Add(UI.Button("Check again",Action("retry"),id+".retry").Disabled(locked));
        }
        return UI.Stack(id,items.ToArray());
    }
    private static string CodecLabel(CaptureVideoCodec value)=>value switch{CaptureVideoCodec.Auto=>"Auto (recommended)",CaptureVideoCodec.H264=>"H.264",CaptureVideoCodec.Hevc=>"HEVC",_=>"AV1"};
    private static string FormatLabel(CaptureVideoPixelFormat value)=>value switch{CaptureVideoPixelFormat.Auto=>"Auto (recommended)",CaptureVideoPixelFormat.Yuv4208=>"8-bit · 4:2:0",CaptureVideoPixelFormat.Yuv42010=>"10-bit · 4:2:0",CaptureVideoPixelFormat.Yuv4448=>"8-bit · 4:4:4",_=>"10-bit · 4:4:4"};
    private static string RateLabel(CaptureVideoRateControl value)=>value switch{CaptureVideoRateControl.Auto=>"Auto (recommended)",CaptureVideoRateControl.ConstantBitrate=>"Constant bitrate",CaptureVideoRateControl.VariableBitrate=>"Variable bitrate",_=>"Quality"};
    private static string ResolvedRecordingLabel(CaptureRecordingConfiguration value,bool running)=>$"{(running?"Using":"Selected")} {value.Recording.MaximumWidth} × {value.Recording.MaximumHeight} · {value.Recording.FramesPerSecond} fps · {CodecLabel(value.Recording.Encoding!.Codec)} · {FormatLabel(value.Recording.Encoding.PixelFormat)} · {value.Recording.Bitrate/1_000_000d:0.#} Mbps · {value.Adapter.Name}";
}
