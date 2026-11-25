using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

[Flags]
internal enum CaptureSettingsSections { Screenshots = 1, Clips = 2, Folders = 4 }

internal sealed record PlayniteCaptureDraft(bool Open,IReadOnlyDictionary<string,JsonElement> Changes,string? Message=null,
    CaptureSettingsSections Expanded = CaptureSettingsSections.Screenshots, AlertTone FeedbackTone = AlertTone.Success);

public sealed partial class PlayniteLibraryWidget
{
    private const string Captures="playnite-library.captures";
    private const string CaptureWork=Captures+".work";
    private sealed record CaptureSetup(PlayniteCaptureSettings Settings,PlayniteCaptureIntegration? Integration,IReadOnlyList<CaptureLibrarySource>? Sources=null);

    private readonly WidgetResource<CaptureSetup> captureSetup;
    internal Task WhenCaptureSettingsIdleAsync(CancellationToken token=default)=>Task.WhenAll(captureSetup.WhenIdleAsync(token),Operations.WhenIdleAsync(CaptureWork,token));
    private void UpdateCaptureDraft(Func<PlayniteCaptureDraft,PlayniteCaptureDraft> update) => _model.Update(state => state with { CaptureSettings = update(state.CaptureSettings) });

    private WidgetResource<CaptureSetup> CreateCaptureSetup()=>CreateResource<CaptureSetup>(Captures+".data",new()
    {
        Load=async token=>
        {
            var settings=PlayniteCaptureSettings.Unavailable with{Availability="unavailable"};
            PlayniteCaptureIntegration? integration=null;
            try
            {
                settings=await _application.GetCaptureSettingsAsync(token);
                integration=await _application.GetCaptureIntegrationAsync(token);
                if(integration is not null&&settings.ConnectionId!=integration.ConnectionId)throw new PlayniteBridgeDataException("capture_connection_changed");
            }
            catch(Exception error)when(error is PlayniteBridgeTransportException or PlayniteBridgeDataException or PlayniteSetupException)
            {settings=PlayniteCaptureSettings.Unavailable with{Availability="unavailable"};integration=null;}
            IReadOnlyList<CaptureLibrarySource>? sources=null;
            try
            {
                var permissions=await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission],token);
                if(permissions.All(value=>value.State==WidgetPermissionState.Granted))sources=await HostServices.CaptureLibrary.GetSourcesAsync(token);
            }
            catch(Exception error)when(error is WidgetCapabilityException or WidgetCapabilityUnavailableException){ }
            return new(settings,integration,sources);
        },
        MapError=_=>new("capture_settings_unavailable","Capture settings could not be read. Check the Playnite connection and try again."),
        CacheDuration=TimeSpan.FromSeconds(15),Lifetime=WidgetOperationLifetime.Active,
    });

    private bool HandleCaptureSettings(WidgetActionEvent action)
    {
        if(!action.ActionId.StartsWith(Captures+".",StringComparison.Ordinal))return false;
        if(LifecycleState!=WidgetLifecycleState.Interactive)return true;
        var name=action.ActionId[(Captures.Length+1)..];
        if(name=="open")
        {
            UpdateCaptureDraft(_=>new(true,new Dictionary<string,JsonElement>()));
            captureSetup.Refresh();return true;
        }
        if(name=="close")
        {
            UpdateCaptureDraft(value=>value with{Open=false});
            captureSetup.Cancel();return true;
        }
        if (!_model.Value.CaptureSettings.Open) return true;
        if (name.StartsWith("section.", StringComparison.Ordinal))
        {
            var parts = name.Split('.');
            if (parts.Length == 3 && Enum.TryParse<CaptureSettingsSections>(parts[1], out var section) &&
                Enum.IsDefined(section) && parts[2] is "open" or "close")
                UpdateCaptureDraft(draft => draft with { Expanded = parts[2] == "open" ? draft.Expanded | section : draft.Expanded & ~section });
            return true;
        }
        if (Operations.IsBusy(CaptureWork)) return true;
        if(name=="refresh"){UpdateCaptureDraft(value=>value with{Changes=new Dictionary<string,JsonElement>(),Message=null});captureSetup.Refresh();return true;}
        if(captureSetup.Snapshot.Value is not { } current)return true;
        if(name.StartsWith("disconnect.",StringComparison.Ordinal)&&current.Sources?.SingleOrDefault(source=>name=="disconnect."+source.Id) is { } removing)
        {
            StartCaptureWork(async context=>
            {
                await HostServices.CaptureLibrary.RemoveSourceAsync(removing.Id,context.CancellationToken);
                var message="Folder disconnected. Original files were kept.";
                var tone=AlertTone.Success;
                if(current.Integration is { } integration&&integration.Roots.Contains(removing.CanonicalFolderPath,StringComparer.OrdinalIgnoreCase))
                    try{await _application.ConfigureCaptureIntegrationAsync(integration,removing.CanonicalFolderPath,false,context.CancellationToken);}
                    catch(Exception error)when(error is not(OutOfMemoryException or StackOverflowException or AccessViolationException))
                    {tone=AlertTone.Warning;message="Folder disconnected. Playnite metadata setup could not be updated; reconnect that profile to finish.";}
                CommitCaptureSetup(context,current,current with{Sources=current.Sources!.Where(source=>source.Id!=removing.Id).ToArray()},
                    ()=>UpdateCaptureDraft(value=>value with{Message=message, FeedbackTone=tone}));
            });return true;
        }
        if(current.Settings is not {Availability:"ready",Editing:false})return true;
        if(name=="save")
        {
            var patch=_model.Value.CaptureSettings.Changes.ToDictionary(pair=>pair.Key,pair=>pair.Value.Clone());
            if(patch.Count==0)return true;
            StartCaptureWork(async context=>
            {
                var updated=await _application.UpdateCaptureSettingsAsync(current.Settings,patch,context.CancellationToken);
                CommitCaptureSetup(context,current,current with{Settings=updated},
                    ()=>UpdateCaptureDraft(value=>value with{Changes=new Dictionary<string,JsonElement>(),Message="Capture settings saved."}));
            });return true;
        }
        if(name is "folder.screenshots" or "folder.recordings")
        {
            if(current.Integration is null)return true;
            var recording=name=="folder.recordings";
            StartCaptureWork(async context=>
            {
                var token=context.CancellationToken;
                var outcome=await HostServices.Permissions.RequestAsync([WidgetCaptureLibraryCapabilities.ReadPermission,WidgetCaptureLibraryCapabilities.ManagePermission],token);
                if(outcome!=WidgetPermissionOutcome.Granted)throw new WidgetCapabilityException("permission_denied","Folder access was not granted.");
                if(!CaptureSetupCurrent(context,current))return;
                var priorSources=await HostServices.CaptureLibrary.GetSourcesAsync(token);
                if(!CaptureSetupCurrent(context,current))return;
                var source=await HostServices.CaptureLibrary.ChooseSourceAsync(recording?"Achievement clips":"Achievement screenshots",token);
                if(source is null||!CaptureSetupCurrent(context,current))return;
                var field=recording?"recordingDirectory":"screenshotDirectory";
                PlayniteCaptureSettings updated;
                try
                {
                    updated=await _application.UpdateCaptureSettingsAsync(current.Settings,new Dictionary<string,JsonElement>{{field,JsonSerializer.SerializeToElement(source.CanonicalFolderPath)}},token);
                }
                catch(PlayniteCaptureSettingsRejectedException)
                {
                    using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    if(CaptureSetupCurrent(context,current)&&!priorSources.Any(prior=>prior.Id==source.Id))
                        await HostServices.CaptureLibrary.RemoveSourceAsync(source.Id,cleanup.Token);
                    throw;
                }
                catch
                {
                    // A disconnected HTTP response does not prove the provider
                    // failed to save. Read back before removing a new folder grant.
                    using var reconcile=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var observed=await _application.GetCaptureSettingsForConnectionAsync(current.Settings.ConnectionId!,reconcile.Token);
                    if(observed.ConnectionId!=current.Settings.ConnectionId||!observed.Values.TryGetValue(field,out var value)||
                        !string.Equals(value.GetString(),source.CanonicalFolderPath,StringComparison.OrdinalIgnoreCase))
                    {throw;} // Unknown outcome: retain the grant, never infer failure from another profile.
                    updated=observed;
                }
                if(!CaptureSetupCurrent(context,current))return;
                var integration=await _application.ConfigureCaptureIntegrationAsync(current.Integration,source.CanonicalFolderPath,true,token);
                if(!CaptureSetupCurrent(context,current))return;
                var sources=await HostServices.CaptureLibrary.GetSourcesAsync(token);
                CommitCaptureSetup(context,current,new(updated,integration,sources),
                    ()=>UpdateCaptureDraft(value=>value with{Message="Folder linked. Captures are kept in their original location."}));
            });return true;
        }
        if(name.StartsWith("set.",StringComparison.Ordinal))
        {
            var parts=name[4..].Split('.',2);
            if(parts.Length!=2||!current.Settings.Values.TryGetValue(parts[0],out var original))return true;
            JsonElement value;
            if(original.ValueKind is JsonValueKind.True or JsonValueKind.False&&parts[1] is "on" or "off")value=JsonSerializer.SerializeToElement(parts[1]=="on");
            else if(current.Settings.Options.TryGetValue(parts[0],out var choices)&&int.TryParse(parts[1],out var index)&&index>=0&&index<choices.Length)value=JsonSerializer.SerializeToElement(choices[index]);
            else return true;
            UpdateCaptureDraft(state=>state with{Changes=Change(state.Changes,parts[0],value,original),Message=null});return true;
        }
        if(name.StartsWith("number.",StringComparison.Ordinal)&&action.RequestedValue is { } requested&&double.IsFinite(requested))
        {
            var field=name[7..];
            var number=field switch{"screenshotDelaySeconds"=>Math.Clamp(requested,0,10),"recordingSeconds"=>Math.Clamp(Math.Round(requested),5,60),"recordingFps"=>Math.Clamp(Math.Round(requested),10,60),_=>double.NaN};
            if(double.IsFinite(number) && current.Settings.Values.TryGetValue(field, out var saved))
                UpdateCaptureDraft(state=>state with{Changes=Change(state.Changes,field,JsonSerializer.SerializeToElement(number),saved),Message=null});
        }
        return true;
    }
    private static IReadOnlyDictionary<string,JsonElement> Change(IReadOnlyDictionary<string,JsonElement> current,string key,JsonElement value,JsonElement saved)
    {
        var next = new Dictionary<string, JsonElement>(current);
        var restored = value.ValueKind == JsonValueKind.Number && saved.ValueKind == JsonValueKind.Number
            ? value.GetDouble() == saved.GetDouble() : value.ToString() == saved.ToString();
        if (restored) next.Remove(key);
        else next[key] = value;
        return next;
    }
    private bool CaptureSetupCurrent(WidgetOperationContext context,CaptureSetup expected)
    {lock(_gate)return context.IsCurrent&&ReferenceEquals(captureSetup.Snapshot.Value,expected);}
    private bool CommitCaptureSetup(WidgetOperationContext context,CaptureSetup expected,CaptureSetup next,Action? update=null)
    {
        lock(_gate)
        {
            if(!context.IsCurrent||!ReferenceEquals(captureSetup.Snapshot.Value,expected))return false;
            captureSetup.Publish(next);update?.Invoke();return true;
        }
    }
    private void StartCaptureWork(Func<WidgetOperationContext,Task> work)
    {
        var owner=captureSetup.Snapshot.Value;
        UpdateCaptureDraft(value=>value with{Message=null, FeedbackTone=AlertTone.Success});
        _=Operations.RunLatest(CaptureWork,async context=>
        {
            try{await work(context);}
            catch(OperationCanceledException)when(context.CancellationToken.IsCancellationRequested){ }
            catch(Exception error)when(error is not(OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                lock(_gate)if(context.IsCurrent&&ReferenceEquals(captureSetup.Snapshot.Value,owner))UpdateCaptureDraft(value=>value with{FeedbackTone=AlertTone.Danger,Message=error switch
                {
                    WidgetCapabilityUnavailableException=>"Update WidgetRail to link capture folders.",
                    WidgetCapabilityException{ErrorCode:"capture_library_source_overlap"}=>"Choose a separate folder for Playnite captures. This folder overlaps WidgetRail storage or another linked folder.",
                    PlayniteCaptureSettingsRejectedException{Code:"capture_settings_editing"}=>"Close Playnite’s settings window, then try again.",
                    PlayniteCaptureSettingsRejectedException{Code:"capture_settings_changed"}=>"Settings changed in Playnite. Refresh before saving again.",
                    _=>"Could not complete the change. Refresh to check the current settings; close Playnite’s settings window if it is open.",
                }});
            }
        },WidgetOperationLifetime.Widget);
    }
    private WidgetModal CaptureSettingsModal()
    {
        var snapshot=captureSetup.Snapshot;var data=snapshot.Value;var draft=_model.Value.CaptureSettings;
        var busy=captureSetup.IsBusy||Operations.IsBusy(CaptureWork);
        var items=new List<WidgetElement>();
        var feedback=new List<WidgetElement>();
        var header=new List<WidgetElement>();
        WidgetElement Section(CaptureSettingsSections section, string key, string label, Func<IReadOnlyList<WidgetElement>> content) =>
            UI.Disclosure(Captures+"."+key, label, draft.Expanded.HasFlag(section),
                Captures+".section."+section+".open", Captures+".section."+section+".close", content).Classes("playnite-capture-disclosure");
        if (draft.Message is { } message) feedback.Add(UI.Alert(draft.FeedbackTone switch {
            AlertTone.Danger => "Capture settings need attention", AlertTone.Warning => "Check capture settings", _ => "Capture settings updated" },
            message, draft.FeedbackTone, Captures+".message"));
        if(data?.Settings.Availability!="ready")
            feedback.Add(UI.Text(snapshot.Error?.Message??(busy?"Loading capture settings…":data?.Settings.Availability switch
            {
                "missing"=>"Install or enable PlayniteAchievements in Playnite to set up achievement captures.",
                "unavailable"=>"Playnite is unavailable. Linked folders can still be disconnected below.",
                _=>"Update WidgetRail for Playnite and PlayniteAchievements to use capture settings.",
            }),Captures+".unavailable"));
        else
        {
            var settings=data.Settings;
            items.Add(UI.Text("Folder choices and disconnects apply immediately. Use Save changes for capture options.",Captures+".save-help").Classes("playnite-ink-muted"));
            if(settings.Editing)feedback.Add(UI.Alert("Settings are open in Playnite","Close that settings window before editing captures here.",AlertTone.Info,Captures+".editing"));
            var enabled=!busy&&!settings.Editing;
            JsonElement Value(string key)=>draft.Changes.TryGetValue(key,out var changed)?changed:settings.Values.GetValueOrDefault(key);
            WidgetElement Choice(string key,string label)
            {
                var value=Value(key);var id=Captures+"."+key;
                var values=settings.Options.GetValueOrDefault(key);
                if (values is null)
                {
                    var on = value.ValueKind == JsonValueKind.True;
                    return UI.Switch(label, on, Captures + ".set." + key + (on ? ".off" : ".on"), id, !enabled)
                        .Classes("playnite-capture-toggle");
                }
                var options = values.Select((text, index) => new SelectOption(id + "." + index, text,
                    Captures + ".set." + key + "." + index, value.ValueKind == JsonValueKind.String && value.GetString() == text)).ToArray();
                return UI.Select(label, options, id, label).Disabled(!enabled).Classes("playnite-capture-choice");
            }
            WidgetElement Number(string key,string label,string unit,double minimum,double maximum,double step)
            {
                var value=Value(key);var number=value.ValueKind==JsonValueKind.Number?value.GetDouble():minimum;
                var display = $"{number:0.#} {unit}";
                var id = Captures+"."+key;
                return UI.Stack(id+".group",
                    UI.Text($"{label} · {display}", id+".label"),
                    UI.Slider(number,minimum,maximum,step,Captures+".number."+key,id,label,display)
                        .RequireControllerActivation().Disabled(!enabled).Classes("playnite-capture-slider")).Classes("playnite-capture-setting");
            }
            WidgetElement Group(string key,string label,params WidgetElement[] content)=>
                UI.Stack(Captures+"."+key+".options",
                    new[]{UI.Text(label,Captures+"."+key+".heading").Classes("playnite-capture-group-title")}.Concat(content).ToArray())
                    .Classes("playnite-capture-option-group");
            WidgetElement Folder(bool recording)
            {
                var key=recording?"recordingDirectory":"screenshotDirectory";var value=Value(key);var path=value.ValueKind==JsonValueKind.String?value.GetString():null;
                var location = string.IsNullOrWhiteSpace(path)
                    ? recording ? "Uses the screenshot folder" : "Choose a folder to save and link screenshots for games without their own folder."
                    : path;
                return UI.Stack(Captures+"."+key+".group",UI.Text(location,Captures+"."+key+".path").Classes("playnite-library-setup-path"),
                    UI.Button(recording?"Choose clip folder…":"Choose screenshot folder…",Captures+".folder."+(recording?"recordings":"screenshots"),Captures+"."+key+".choose").Disabled(!enabled||data.Integration is null));
            }
            bool On(string key) => Value(key).ValueKind == JsonValueKind.True;
            bool HasPath(string key) => Value(key) is { ValueKind: JsonValueKind.String } value && !string.IsNullOrWhiteSpace(value.GetString());
            var imageFolder = HasPath("screenshotDirectory");
            var clipFolder = HasPath("recordingDirectory");
            var missingImages = On("screenshotsEnabled") && !imageFolder;
            var missingClips = On("recordingsEnabled") && !clipFolder && !imageFolder;
            if (missingImages || missingClips)
            {
                var title = missingImages && missingClips ? "Screenshots and clips need a default folder" :
                    missingImages ? "Screenshots need a default folder" : "Clips need a default folder";
                var warning = UI.Row(Captures+".folder-warning",
                    UI.Icon(WidgetGlyph.Warning, Captures+".warning.icon", "Warning"),
                    UI.Stack(Captures+".warning.copy",
                        UI.Text(title, Captures+".warning.title").Classes("playnite-capture-group-title"),
                        UI.Text("For games without their own capture folder.", Captures+".warning.detail").Classes("playnite-ink-muted")));
                var choices = new List<ButtonElement>();
                if (missingImages) choices.Add(UI.Button("Choose screenshot folder…", Captures+".folder.screenshots", Captures+".warning.screenshots"));
                if (missingClips) choices.Add(UI.Button("Choose clip folder…", Captures+".folder.recordings", Captures+".warning.clips"));
                feedback.Add(UI.ResponsiveGrid(Captures+".folder-warning.bar", 350, 2, warning,
                    UI.Row(Captures+".folder-warning.actions", choices.Select(button => button.Disabled(!enabled || data.Integration is null)).ToArray()))
                    .Classes("playnite-capture-warning"));
            }
            string Summary(string label, bool on, string destination) => $"{label} · {(on ? "On" : "Off")}" + (on ? " · "+destination : "");
            items.Add(Section(CaptureSettingsSections.Screenshots,"screenshots",Summary("Screenshots", On("screenshotsEnabled"),
                imageFolder ? "Folder set" : "No default folder"), () => [Choice("screenshotsEnabled","Save achievement screenshots"),
                Group("screenshot.versions","Versions to save",UI.ResponsiveGrid(Captures+".screenshot.variants",200,3,Choice("screenshotClean","Clean"),Choice("screenshotWithToast","With notification"),Choice("screenshotFramed","Framed"))),
                Group("screenshot.picture","Picture and timing",UI.ResponsiveGrid(Captures+".screenshot.quality",240,2,Choice("screenshotResolution","Resolution"),Number("screenshotDelaySeconds","Capture delay","s",0,10,.5))),
                Group("screenshot.location","Save location",Folder(false))]));
            items.Add(Section(CaptureSettingsSections.Clips,"recordings",Summary("Video clips", On("recordingsEnabled"),
                clipFolder ? "Folder set" : imageFolder ? "Uses screenshot folder" : "No default folder"), () => [Choice("recordingsEnabled","Save achievement clips"),
                Group("recording.versions","Versions to save",UI.ResponsiveGrid(Captures+".recording.variants",200,3,Choice("recordingClean","Clean"),Choice("recordingWithToast","With notification"),Choice("recordingFramed","Framed"))),
                Group("recording.picture","Picture and timing",
                    UI.ResponsiveGrid(Captures+".recording.quality",240,2,Choice("recordingResolution","Resolution"),Choice("recordingQuality","Quality")),
                    UI.ResponsiveGrid(Captures+".recording.timing",240,2,Number("recordingSeconds","Time before unlock","s",5,60,5),Number("recordingFps","Frame rate","fps",10,60,10))),
                Group("recording.sound","Audio",UI.ResponsiveGrid(Captures+".recording.audio",220,3,Choice("recordingAudio","Record audio"),Choice("recordingMicrophone","Include microphone"),Choice("recordingAudioSource","Audio source"))),
                Group("recording.location","Save location",Folder(true))]));
            header.Add(UI.Button("Save changes",Captures+".save",Captures+".save").Disabled(!enabled||draft.Changes.Count==0).Classes("playnite-capture-save"));
        }
        if(data?.Sources is {Count:>0} sources)
        {
            items.Add(Section(CaptureSettingsSections.Folders,"sources",$"Linked folders · {sources.Count}", () =>
                sources.Select(source => (WidgetElement)UI.Stack(Captures+".source."+source.Id,
                    UI.Text(source.CanonicalFolderPath,Captures+".source."+source.Id+".path").Classes("playnite-library-setup-path"),
                    UI.Button("Disconnect",Captures+".disconnect."+source.Id,Captures+".source."+source.Id+".disconnect").Disabled(busy))).ToArray()));
        }
        items.Add(UI.Button("Refresh",Captures+".refresh",Captures+".refresh").Disabled(busy));
        if (draft.Changes.Count > 0 && data?.Settings.Availability == "ready")
            header.Add(UI.Text("Unsaved changes", Captures+".unsaved").Classes("playnite-capture-unsaved"));
        header.Add(UI.Button("Close", Captures+".close", Captures+".modal.close"));
        var body = UI.Grid(Captures+".content", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            UI.Stack(Captures+".feedback",feedback.ToArray()),
            UI.VerticalScroll(Captures+".settings-scroll",UI.Stack(Captures+".sections",items.ToArray()).Classes("playnite-capture-content")).InGrid(row:1));
        return new(Captures+".modal","Achievement captures",body,Captures+".modal.close",Captures+".close")
        { ScrollContent = false, StyleClass = "playnite-capture-settings-dialog", HeaderActions = UI.Row(Captures+".header-actions",header.ToArray()).Classes("playnite-capture-header-actions") };
    }
}


