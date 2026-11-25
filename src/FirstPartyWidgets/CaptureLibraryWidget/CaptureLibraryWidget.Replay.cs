using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private const string ReplayOperation="capture-library.replay.command";
    private void WatchReplay()=>Operations.RunLatest("capture-library.replay.watch",async context=>
    {
        try
        {
            await using var changes=await HostServices.Capture.Replay.OpenChangesSubscriptionAsync(context.CancellationToken);
            var initial=await HostServices.Capture.Replay.GetAsync(context.CancellationToken);
            if(context.IsCurrent)ApplyReplay(initial);
            await foreach(var status in changes.ReadAllAsync(context.CancellationToken))
            {
                if(!context.IsCurrent)return;
                if(status.IsWellFormed())ApplyReplay(status);
            }
        }
        catch(OperationCanceledException) { }
        catch(WidgetCapabilityException error)
        {
            if(context.IsCurrent)model.Update(state=>state with{Replay=null,ReplayNotice=error.ErrorCode=="permission_denied"?
                null:"Replay status is unavailable. Open Capture settings to try again."});
        }
    },WidgetOperationLifetime.Active);

    private void ApplyReplay(CaptureReplayStatus status)
    {
        var change = model.Update(state=>state.Replay is {} current&&current.Revision>status.Revision?state:
            state with{Replay=status,ReplaySavedSeconds=state.Replay?.SessionId==status.SessionId?state.ReplaySavedSeconds:null});
        if (showApprovals && change.Current.Replay?.SettingsRevision is { } revision && revision != change.Previous.Replay?.SettingsRevision)
            approvedApplications.EnsureLoaded(revision);
    }
    private static string ReplayAction(string command,CaptureReplayStatus? status)=>"capture-library.replay."+command+"."+(status?.SessionId??"none");
    private static string ReplaySettingAction(CaptureReplayStatus status,string value)=>"capture-library.replay.setting."+
        status.SettingsRevision.ToString(CultureInfo.InvariantCulture)+"."+value;

    private bool HandleReplayAction(WidgetActionEvent action)
    {
        if(!action.ActionId.StartsWith("capture-library.replay.",StringComparison.Ordinal))return false;
        var state=RecordingState(model.Value);var status=state.Replay;
        if(Operations.IsBusy(ReplayOperation))return true;
        if(action.ActionId=="capture-library.replay.refresh") { WatchReplay();return true; }
        if(action.ActionId=="capture-library.replay.start"&&CurrentRecordingDraft(true,state) is null&&(status is null||status.Phase is CaptureReplayPhase.Off or CaptureReplayPhase.Waiting or CaptureReplayPhase.Error))
        {
            ReplayCommand(async token=>
            {
                await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ReadPermission,WidgetCaptureCapabilities.Permission],token);
                var result=await HostServices.Capture.Replay.StartAsync(token);token.ThrowIfCancellationRequested();ApplyReplay(result.Status);
                model.Update(value=>value with{ReplayNotice=result.Outcome is CaptureReplayOutcome.Accepted or CaptureReplayOutcome.Cancelled?null:ReplaySettingsIssue(result.Issue)});
                WatchReplay();
            });return true;
        }
        if(status is null)return true;
        if(action.ActionId==ReplayAction("stop",status))
        {
            ReplayCommand(async token=>
            {
                var result=await HostServices.Capture.Replay.StopAsync(status.SessionId,token);token.ThrowIfCancellationRequested();ApplyReplay(result.Status);
                model.Update(value=>value with{ReplayNotice=result.Outcome==CaptureReplayOutcome.Accepted?null:
                    !result.Status.Settings.Enabled && result.Issue==CaptureReplayIssue.StorageUnavailable?
                    (result.Status.Phase==CaptureReplayPhase.Stopping?"Recording is stopping":"Recording is off")+
                    ", but the off setting could not be saved. Check available disk space before restarting WidgetRail.":ReplaySettingsIssue(result.Issue)});
            });return true;
        }
        if(action.ActionId==ReplayAction("save",status)&&status.SessionId is {} session&&CanSaveReplay(status))
        {
            ReplayCommand(async token=>
            {
                await RequirePermissionsAsync([WidgetCaptureCapabilities.Permission,WidgetCaptureLibraryCapabilities.SavePermission],token);
                var result=await HostServices.Capture.Replay.SaveAsync(session,token);
                token.ThrowIfCancellationRequested();
                model.Update(value=>value with{ReplayNotice=result.Outcome==CaptureReplayOutcome.Accepted?null:ReplayIssue(result.Issue),
                    ReplaySavedSeconds=result.Outcome==CaptureReplayOutcome.Accepted&&value.Replay?.SessionId==session?
                        result.ActualDurationSeconds:value.ReplaySavedSeconds});
                if(result.Outcome==CaptureReplayOutcome.Accepted)
                { PublishFeedback(WidgetRail.Internal.CaptureReplayFeedback.Saved(result)); QueueGalleryRefresh(); }
                else PublishFeedback(ReplayIssue(result.Issue));
            });return true;
        }
        if(!state.Settings||ReplaySettingsLocked(status)||CurrentRecordingDraft(true,state) is not null)return true;
        var prefix=ReplaySettingAction(status,"");
        if(!action.ActionId.StartsWith(prefix,StringComparison.Ordinal))return true;
        var option=action.ActionId[prefix.Length..];var next=status.Settings;
        switch(option)
        {
            case "auto.on":next=next with{AutoStart=true};break;
            case "auto.off":next=next with{AutoStart=false};break;
            case "audio.on":next=next with{Recording=next.Recording with{Audio=CaptureAudioMode.Application}};break;
            case "audio.off":next=next with{Recording=next.Recording with{Audio=CaptureAudioMode.None}};break;
            case "cursor.on":next=next with{IncludeCursor=true};break;
            case "cursor.off":next=next with{IncludeCursor=false};break;
            default:
                if(option.StartsWith("duration.",StringComparison.Ordinal)&&int.TryParse(option[9..],out var duration)&&ReplayDurations.Contains(duration))
                    next=next with{ReplayDurationSeconds=duration};
                else if(option.StartsWith("memory.",StringComparison.Ordinal)&&int.TryParse(option[7..],out var memory)&&ReplayBudgets.Contains(memory))
                    next=next with{MemoryBudgetBytes=memory*1048576L};
                else return true;
                break;
        }
        if(!next.IsWellFormed())return true;
        ReplayCommand(async token=>
        {
            await RequirePermissionsAsync([WidgetCaptureCapabilities.Permission],token);
            var result=await HostServices.Capture.Replay.ConfigureAsync(next,status.SettingsRevision,token);token.ThrowIfCancellationRequested();ApplyReplay(result.Status);
            model.Update(value=>value with{ReplayNotice=result.Outcome==CaptureReplayOutcome.Accepted?null:ReplaySettingsIssue(result.Issue)});
        });return true;
    }

    private void ReplayCommand(Func<CancellationToken,Task> command)
    {
        model.Update(state=>state with{ReplayNotice=null});
        Operations.RunLatest(ReplayOperation,async context=>
        {
            try{await command(context.CancellationToken);}
            catch(OperationCanceledException) { }
            catch(WidgetCapabilityException error)
            {if(context.IsCurrent)model.Update(state=>state with{ReplayNotice=Explain(error.ErrorCode)});}
        },WidgetOperationLifetime.Widget);
    }
    private static bool CanSaveReplay(CaptureReplayStatus status) => TrayReplayState.From(status).CanSave;
    private ContainerElement ReplayShortcuts(ContainerElement root, State state)
    {
        if (state.Busy || Operations.IsBusy(ReplayOperation)) return root;
        var status = state.Replay;
        if (status is not null && CanSaveReplay(status))
            root = root.Shortcut(ControllerButton.RightBumper, ReplayAction("save", status), label: "Save replay");
        if (status is not null && (status.Settings.Enabled || ReplaySettingsLocked(status)))
        {
            if (status.Phase != CaptureReplayPhase.Stopping)
                root = root.Shortcut(ControllerButton.LeftBumper, ReplayAction("stop", status), label: "Stop recording");
        }
        else if (CurrentRecordingDraft(true, RecordingState(state)) is null)
            root = root.Shortcut(ControllerButton.LeftBumper, "capture-library.replay.start", label: "Start recording");
        return root;
    }
    private static bool ReplaySettingsLocked(CaptureReplayStatus status)=>status.Saving||status.Phase is CaptureReplayPhase.Starting or CaptureReplayPhase.Recording or CaptureReplayPhase.Paused or CaptureReplayPhase.Stopping;
    private static readonly int[] ReplayDurations=[15,30,60,120];
    private static readonly int[] ReplayBudgets=[32,64,128,256];
    private static string ReplaySettingsIssue(CaptureReplayIssue issue)=>issue==CaptureReplayIssue.StorageUnavailable?
        "Replay settings could not be saved. Check available disk space.":ReplayIssue(issue);
    private static string ReplayIssue(CaptureReplayIssue issue)=>issue switch
    {
        CaptureReplayIssue.None or CaptureReplayIssue.Cancelled=>"Recording was not started.",
        CaptureReplayIssue.TargetUnavailable=>"Return to the application you want to record, reopen WidgetRail and try again.",
        CaptureReplayIssue.HardwareEncoderUnavailable=>"Replay recording needs a supported hardware video encoder.",
        CaptureReplayIssue.ConfigurationUnavailable=>"These settings aren't available through the current recording and editing pipeline. Choose an available format or a smaller resolution.",
        CaptureReplayIssue.AudioUnavailable=>"Application audio is unavailable. Choose No audio to record video only.",
        CaptureReplayIssue.KeyframesUnavailable or CaptureReplayIssue.OutputOrderUnsupported=>"This encoder could not produce a usable replay buffer. Try another video quality.",
        CaptureReplayIssue.BufferNotReady=>"There is not enough recorded video yet. Try again in a moment.",
        CaptureReplayIssue.SaveInProgress=>"A replay is already being saved.",
        CaptureReplayIssue.RecordingActive=>"Stop recording before changing replay settings.",
        CaptureReplayIssue.StorageUnavailable=>"The replay could not be saved. Check that the capture folder is available and has free space.",
        CaptureReplayIssue.SessionChanged=>"The recording changed. Try again with the current recording.",
        CaptureReplayIssue.SettingsChanged=>"Settings changed elsewhere. The latest settings are shown.",
        CaptureReplayIssue.PermissionRevoked=>"Recording access was removed. Start recording to grant access again.",
        _=>"Replay recording is unavailable. Try starting it again.",
    };
    private WidgetElement ReplayToolbar(State state)
    {
        var status=state.Replay;var busy=Operations.IsBusy(ReplayOperation);var draft=state.Settings&&CurrentRecordingDraft(true,state) is not null;
        var label=status is null?"Replay unavailable":status.Phase switch
        {
            CaptureReplayPhase.Recording=>$"Recording · {status.TargetName??"Application"}",
            CaptureReplayPhase.Paused=>$"Paused · {status.TargetName??"Application"}",
            CaptureReplayPhase.Waiting=>status.Settings.AutoStart?"Waiting for a single approved application":"Recording is waiting for you to start it",CaptureReplayPhase.Starting=>"Starting recording…",
            CaptureReplayPhase.Stopping=>"Stopping recording…",CaptureReplayPhase.Error=>ReplaySettingsIssue(status.Issue),_=>"Replay recording is off",
        };
        var live=status?.Phase==CaptureReplayPhase.Recording;
        var controls=new List<WidgetElement>{
            UI.Icon(CaptureIcon("record",WidgetGlyph.Play),"capture-library.replay.indicator",live?"Recording":"Replay status")
                .Classes("capture-replay-indicator",live?"capture-replay-live":"capture-replay-idle"),
            UI.Text(label,"capture-library.replay.status").Classes("capture-help","capture-replay-status")};
        if(status?.Phase is CaptureReplayPhase.Recording or CaptureReplayPhase.Paused)
        {
            var receipt=state.ReplaySavedSeconds is {} saved?$"Saved {saved:0.#}s · ":"";
            controls.Add(UI.Text(status.Saving?"Saving…":receipt+$"{status.BufferedDurationSeconds:0}s ready",
                "capture-library.replay.buffer").Classes("capture-replay-buffer"));
        }
        if(status is not null && (status.Settings.Enabled || ReplaySettingsLocked(status)))
        {
            var hints = !state.Busy && (state.Settings || gallery.Selection?.IsActive != true);
            var canStop = !busy && status.Phase != CaptureReplayPhase.Stopping;
            controls.Add(HintedAction("Stop", ReplayAction("stop", status), canStop && hints ? ControllerButton.LeftBumper : null,
                busy, id: "capture-library.replay.stop").Disabled(!canStop).AddClasses("capture-replay-command"));
            if(status.Phase is CaptureReplayPhase.Error or CaptureReplayPhase.Waiting)controls.Add(UI.Button(status.Phase==CaptureReplayPhase.Error?"Try again":"Start now","capture-library.replay.start","capture-library.replay.start").Disabled(busy||draft));
            if(status.Saving||status.Phase is CaptureReplayPhase.Recording or CaptureReplayPhase.Paused)
            {
                var canSave = !busy && CanSaveReplay(status);
                controls.Add(HintedAction(status.Saving ? "Saving…" : "Save replay", ReplayAction("save", status),
                    canSave && hints ? ControllerButton.RightBumper : null, busy, id: "capture-library.replay.save")
                    .Disabled(!canSave).AddClasses("capture-primary", "capture-replay-command"));
            }
        }
        else controls.Add(UI.Button("Start recording","capture-library.replay.start","capture-library.replay.start").Disabled(busy||draft).Classes("capture-primary"));
        var row=UI.Row("capture-library.replay.controls",controls.ToArray()).Classes("capture-replay-controls");
        var content=new List<WidgetElement>{row};
        if (status is { Issue: CaptureReplayIssue.AudioUnavailable, Phase: CaptureReplayPhase.Recording or CaptureReplayPhase.Paused })
            content.Add(UI.Text("Reconnecting application audio. Replay is still recording video.",
                "capture-library.replay.audio-warning").Classes("capture-replay-warning"));
        if(state.ReplayNotice is {} notice)content.Add(UI.Text(notice,"capture-library.replay.notice").Classes("capture-help","capture-replay-warning"));
        return UI.Stack("capture-library.replay.panel",content.ToArray()).Classes("capture-replay-deck");
    }
    private WidgetElement ReplaySettings(State state)
    {
        var items=new List<WidgetElement>{UI.Text("Replay recording","capture-library.replay.heading").Classes("capture-heading"),ReplayToolbar(state)};
        if(state.Replay is {} status)
        {
            var settings=status.Settings;var locked=Operations.IsBusy(ReplayOperation)||ReplaySettingsLocked(status);
            var busy=locked||CurrentRecordingDraft(true,state) is not null;
            if(status.Resolved is {} resolved)
            {
                items.Add(UI.Text(ResolvedRecordingLabel(resolved,status.Phase!=CaptureReplayPhase.Starting),"capture-library.replay.using").Classes("capture-help"));
                if(resolved.EditingSupported==false)items.Add(UI.Text("Trimming is unavailable for the current recording format.","capture-library.replay.editing").Classes("capture-help"));
            }
            if(ReplaySettingsLocked(status))items.Add(UI.Text("Stop recording before changing replay settings.","capture-library.replay.settings-locked").Classes("capture-help"));
            SelectOption Choice(string key,string label,bool selected)=>new(key,label,ReplaySettingAction(status,key),selected);
            WidgetElement Field(string id,string label,SelectOption[] choices,string? help=null)=>UI.SettingsField("capture-library.replay."+id+".row",label,
                UI.Select(label,choices,"capture-library.replay."+id).Disabled(busy),help);
            items.Add(Field("duration","Keep the last",ReplayDurations.Append(settings.ReplayDurationSeconds).Distinct().Order().Select(seconds=>Choice("duration."+seconds,seconds+" seconds",settings.ReplayDurationSeconds==seconds)).ToArray(),
                "The saved length can be shorter while the buffer fills or reaches its memory limit."));
            var memory=ReplayBudgets.Select(mib=>Choice("memory."+mib,mib+" MiB",settings.MemoryBudgetBytes==mib*1048576L)).ToList();
            if(!memory.Any(option=>option.IsSelected))memory.Insert(0,new("current",$"{settings.MemoryBudgetBytes/1048576d:0.#} MiB","capture-library.replay.current",true,IsDisabled:true));
            items.Add(Field("memory","Buffer memory",memory.ToArray(),"Limit for recorded video and audio, including a save in progress. Capture and encoding use additional memory."));
            items.Add(Field("auto","Start automatically",[Choice("auto.off","Off",!settings.AutoStart),Choice("auto.on","Approved applications",settings.AutoStart)],
                "Replay starts automatically when one approved application has a single eligible window. Use Start now to choose a window."));
            items.Add(UI.Button("Approved applications", "capture-library.approvals.open", "capture-library.approvals.open")
                .Disabled(state.Busy || CurrentRecordingDraft(true, state) is not null));
            if(state.Capabilities is not null)items.Add(RecordingFields("replay",state,settings.Recording,locked));
            var applicationAudio=state.Capabilities?.AudioModes.Contains(CaptureAudioMode.Application)!=false;
            items.Add(Field("audio","Audio",[Choice("audio.on",applicationAudio?"Application audio":"Application audio (unavailable)",settings.Recording.Audio==CaptureAudioMode.Application) with{IsDisabled=!applicationAudio},
                Choice("audio.off","No audio",settings.Recording.Audio==CaptureAudioMode.None)]));
            items.Add(Field("cursor","Mouse cursor",[Choice("cursor.on","Include",settings.IncludeCursor),Choice("cursor.off","Hide",!settings.IncludeCursor)]));
            items.Add(UI.Text("Recording continues when Capture Library closes. Save replay keeps a clip in the capture folder; Stop ends recording.","capture-library.replay.help").Classes("capture-help"));
        }
        else items.Add(UI.Button("Refresh recording status","capture-library.replay.refresh","capture-library.replay.refresh"));
        return UI.Stack("capture-library.settings.replay",items.ToArray()).Classes("capture-settings-section");
    }
}
