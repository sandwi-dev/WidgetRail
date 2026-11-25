using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private bool preferencesLoaded;
    private sealed record CapturePreferences(int Schema = 1, ScreenshotOptions? Image = null,
        RecordingOptions? Video = null, CapturePreparation? Preparation = null, bool IncludeCursor = false, bool ClipPreviews = true,
        int? ScreenshotDelaySeconds = null)
    {
        public bool IsValid() => Schema is 1 or 2 && (Image?.IsWellFormed() ?? true) &&
            (Video?.IsWellFormed() ?? true) && (Preparation?.IsWellFormed() ?? true) && ScreenshotDelaySeconds is null or (>= 0 and <= 30);
        public CapturePreparation PreparationFor(WindowCaptureKind kind)
        {
            var preparation = Preparation ?? new();
            // Earlier profiles stored one explicit preparation delay for both kinds.
            return kind == WindowCaptureKind.Screenshot
                ? preparation with { DelaySeconds = ScreenshotDelaySeconds ?? Preparation?.DelaySeconds ?? 0 }
                : preparation;
        }
        public CapturePreferences WithClipPreparation(CapturePreparation preparation) =>
            this with { Preparation = preparation, ScreenshotDelaySeconds = PreparationFor(WindowCaptureKind.Screenshot).DelaySeconds };
        public CaptureSessionRequest Request(WindowCaptureKind kind) => new()
        {
            Screenshot = kind == WindowCaptureKind.Screenshot ? Image ?? new() : null,
            Recording = kind == WindowCaptureKind.Video ? Video ?? RecommendedVideo() : null,
            Preparation = PreparationFor(kind), IncludeCursor = IncludeCursor,
            OutputPurpose = kind == WindowCaptureKind.Video ? CaptureOutputPurpose.LibraryRecording : CaptureOutputPurpose.TemporaryAttachment,
            MaximumBytes = kind == WindowCaptureKind.Video ? CaptureLimits.MaximumLibraryRecordingBytes : CaptureLimits.MaximumAttachmentBytes,
        };
    }

    private async Task LoadPreferencesAsync(CancellationToken token, bool showFailure = true)
    {
        if (preferencesLoaded) return;
        var saved = await HostServices.PrivateState.ReadJsonAsync(token);
        CapturePreferences? preferences;
        try { preferences = saved.Exists ? JsonSerializer.Deserialize<CapturePreferences>(saved.Json!) : new(); }
        catch (JsonException) { preferences = null; }
        if (preferences?.IsValid() != true) preferences = null;
        if(preferences is {Video:null})preferences=preferences with{Video=RecommendedVideo()};
        if(preferences?.Video is {Selection:not null} or {Encoding:not null})preferences=preferences with{Schema=2};
        model.Update(s => s with { Preferences = preferences, PreferencesRevision = saved.Revision,
            Message = preferences is null ? Explain("capture_preferences_invalid") : s.Message,
            PermissionRequired = preferences is null ? false : s.PermissionRequired });
        preferencesLoaded = true;
        if (preferences is null && showFailure) PublishFeedback(Explain("capture_preferences_invalid"));
        ApplyClipPreviewPreference(preferences?.ClipPreviews == true);
    }


    private async Task LoadSettingsAsync(CancellationToken token)
    {
        await LoadPreferencesAsync(token);
        token.ThrowIfCancellationRequested();
        if(!model.Value.Settings)return;
        recordingCatalog.EnsureLoaded();
    }

    private void ChangeSetting(string action)
    {
        var state = RecordingState(model.Value);
        if (!state.Settings) return;
        if(CurrentRecordingDraft(false,state) is not null)return;
        var value = action["capture-library.option.".Length..];
        if (value is "previews.on" or "previews.off")
        {
            if (state.Preferences is { } previewPreferences) SavePreferences(previewPreferences with { ClipPreviews = value == "previews.on" }, state);
            return;
        }
        if (state.Capabilities is not { } capabilities) return;
        var old = state.Preferences;
        if (old is null && value != "reset") return;
        var next = old ?? new();
        var image = next.Image ?? new(); var video = next.Video ?? RecommendedVideo(); var prep = next.Preparation ?? new();
        switch (value)
        {
            case "reset": next = new(Video: RecommendedVideo()); break;
            case "image.original": next = next with { Image = image with { MaximumWidth = null, MaximumHeight = null } }; break;
            case "image.1080": next = next with { Image = image with { MaximumWidth = 1920, MaximumHeight = 1080 } }; break;
            case "image.1440": next = next with { Image = image with { MaximumWidth = 2560, MaximumHeight = 1440 } }; break;
            case "image.2160": next = next with { Image = image with { MaximumWidth = 3840, MaximumHeight = 2160 } }; break;
            case "cursor.on" when capabilities.CursorSupported: next = next with { IncludeCursor = true }; break;
            case "cursor.off": next = next with { IncludeCursor = false }; break;
            case "countdown.on": next = next.WithClipPreparation(prep with { ShowCountdown = true }); break;
            case "countdown.off": next = next.WithClipPreparation(prep with { ShowCountdown = false }); break;
            default:
                if (value.StartsWith("duration.", StringComparison.Ordinal) && int.TryParse(value[9..], out var seconds) && Durations.Contains(seconds) && seconds <= capabilities.MaximumRecordingSeconds)
                    next = next with { Video = video with { MaximumDurationSeconds = seconds } };
                else if (value.StartsWith("delay.", StringComparison.Ordinal) && int.TryParse(value[6..], out var delay) && Delays.Contains(delay))
                    next = next.WithClipPreparation(prep with { DelaySeconds = delay });
                else if (value.StartsWith("image.delay.", StringComparison.Ordinal) && int.TryParse(value[12..], out var imageDelay) && Delays.Contains(imageDelay))
                    next = next with { ScreenshotDelaySeconds = imageDelay };
                else if (value.StartsWith("audio.", StringComparison.Ordinal) && Enum.TryParse<CaptureAudioMode>(value[6..], out var audio) && capabilities.AudioModes.Contains(audio))
                    next = next with { Video = video with { Audio = audio } };
                else if (value.StartsWith("format.", StringComparison.Ordinal) && Enum.TryParse<CaptureImageFormat>(value[7..], out var format) && capabilities.ImageFormats.Contains(format))
                    next = next with { Image = image with { Format = format } };
                else return;
                break;
        }
        SavePreferences(next, state);
    }
    private void SavePreferences(CapturePreferences next, State state,Action? completed=null) => Work(async token =>
        {
            try
            {
                // Older Capture packages validate Schema before reading video
                // settings. They must reject Auto/codec choices, not silently
                // reinterpret their legacy fallback numbers.
                if(next.Video is {Selection:not null} or {Encoding:not null})next=next with{Schema=2};
                var saved = await HostServices.PrivateState.WriteAsync(next, state.PreferencesRevision, cancellationToken: token);
                UpdateFeedback(s => s with { Preferences = next, PreferencesRevision = saved.Revision, Message = "Capture settings saved." });
                ApplyClipPreviewPreference(next.ClipPreviews);
                completed?.Invoke();
            }
            catch (WidgetCapabilityException error) when (error.ErrorCode == "state_conflict")
            {
                preferencesLoaded = false;
                await LoadPreferencesAsync(token);
                throw new WidgetCapabilityException("capture_preferences_conflict", "Settings changed elsewhere.");
            }
        });
    private static readonly int[] Durations = [5, 10, 15, 30, 60, 120];
    private static readonly int[] Delays = [0, 3, 5, 10];
    private static SelectOption Option(string key, string label, bool selected) => new(key, label, "capture-library.option." + key, selected);

    private const string SectionAction = "capture-library.section.";
    private bool HandleSettingsSection(string action)
    {
        if (!action.StartsWith(SectionAction, StringComparison.Ordinal)) return false;
        var parts = action[SectionAction.Length..].Split('.');
        if (parts.Length != 2 || !Enum.TryParse<SettingsSection>(parts[0], out var section) ||
            section == SettingsSection.None || !Enum.IsDefined(section) || parts[1] is not ("open" or "close" or "show")) return true;
        var focus = parts[1] == "show" ? CreateFocusRequest().FocusElement("capture-library.settings.sections",
            "capture-library.section." + section + ".header") : null;
        model.Update(value => value with { ExpandedSettings = parts[1] == "close" ? value.ExpandedSettings & ~section :
            value.ExpandedSettings | section, SettingsFocus = focus });
        return true;
    }

    private WidgetView RenderSettings(State state)
    {
        state=RecordingState(state);
        var pendingManual=CurrentRecordingDraft(false,state) is not null;
        var items = new List<WidgetElement> {
            UI.Button("Back to library", "capture-library.settings.back", "capture-library.settings.back"),
            UI.Button("Capture folders", "capture-library.sources.open", "capture-library.sources.open").Busy(state.Busy),
        };
        var heading = new List<WidgetElement> { UI.Text("Capture settings", "capture-library.settings.title").Classes("capture-title") };
        var pendingReplay = CurrentRecordingDraft(true, state) is not null;
        if (pendingManual || pendingReplay)
        {
            var where = pendingManual && pendingReplay ? "Recording and Replay" : pendingReplay ? "Replay" : "Recording";
            var manualIssue = CurrentRecordingDraft(false, state) is { } manualDraft ? DraftValidation(manualDraft, false).Issue : null;
            var replayIssue = CurrentRecordingDraft(true, state) is { } replayDraft ? DraftValidation(replayDraft, true).Issue : null;
            var issue = manualIssue ?? replayIssue;
            heading.Add(UI.Alert(issue is null ? "Unsaved changes" : (manualIssue is null ? "Check replay settings" : "Check recording settings"),
                issue ?? $"Save or cancel your changes in {where}.",
                AlertTone.Warning, "capture-library.settings.unsaved"));
            if (issue is not null)
            {
                var section = manualIssue is null ? SettingsSection.Replay : SettingsSection.Recording;
                heading.Add(UI.Button("Edit " + section.ToString().ToLowerInvariant() + " settings", SectionAction + section + ".show", "capture-library.settings.draft.open"));
            }
        }
        WidgetElement Section(SettingsSection section, string label, Func<IReadOnlyList<WidgetElement>> content) =>
            UI.Disclosure(SectionAction + section, label, state.ExpandedSettings.HasFlag(section), SectionAction + section + ".open",
                SectionAction + section + ".close", content).Classes("capture-settings-section");
        var replaySummary = state.Replay is { } replay ? replay.Settings.Enabled ? $"{replay.Settings.ReplayDurationSeconds} s buffer" : "Off" : "Checking…";
        items.Add(Section(SettingsSection.Replay, "Replay recording · " + replaySummary, () => ReplaySettings(state)));
        if (state.Replay is { Phase: CaptureReplayPhase.Error } failedReplay)
        {
            heading.Add(UI.Alert("Replay recording needs attention", state.ReplayNotice ?? ReplayIssue(failedReplay.Issue), AlertTone.Warning, "capture-library.settings.replay.issue"));
            heading.Add(UI.Button("Check replay recording", SectionAction + "Replay.show", "capture-library.settings.replay.attention"));
        }
        else if (state.ReplayNotice is { } notice)
            heading.Add(UI.Text(notice, "capture-library.settings.replay.notice").Classes("capture-help"));
        if (state.Preferences is { } galleryPreferences)
            items.Add(Section(SettingsSection.Gallery, $"Gallery · Previews {(galleryPreferences.ClipPreviews ? "on" : "off")}", () => [
                UI.SettingsField("capture-library.settings.previews.row", "Capture previews",
                    UI.Select("Capture previews", new SelectOption[] {
                        Option("previews.on", "On", galleryPreferences.ClipPreviews),
                        Option("previews.off", "Off", !galleryPreferences.ClipPreviews) }, "capture-library.settings.previews").Disabled(state.Busy||pendingManual),
                    "Preview screenshots and play muted clips after focus or hover settles.")]));
        if(state.Page is { } library) items.Add(Section(SettingsSection.Storage, $"Storage · {library.TotalCount:N0} captures", () => [
            UI.Text($"{library.TotalCount} saved captures · {library.StoredBytes / 1048576d:0.0} MiB","capture-library.settings.storage.usage").Classes("capture-help"),
            UI.Text("Pictures → WidgetRail → Captures. Saved captures have no fixed item or storage quota.","capture-library.settings.storage.location").Classes("capture-help"),
            UI.Button("Open folder","capture-library.folder","capture-library.settings.folder").Busy(state.Busy)]));
        if (state.Capabilities is { } capabilities && state.Preferences is { } preferences)
        {
            var image = preferences.Image ?? new(); var video = preferences.Video ?? RecommendedVideo(); var prep = preferences.Preparation ?? new();
            WidgetElement Field(string name, string label, IEnumerable<SelectOption> options, string? description = null) =>
                UI.SettingsField("capture-library.setting." + name + ".row", label,
                    UI.Select(label, options.ToArray(), "capture-library.setting." + name).Disabled(state.Busy||pendingManual), description);
            items.Add(Section(SettingsSection.Screenshots, $"Screenshots · {image.Format.ToString().ToUpperInvariant()}", () => [
                Field("resolution", "Resolution", [Option("image.original", "Original resolution", image.MaximumWidth is null),
                    Option("image.1080", "Up to 1080p", image.MaximumHeight == 1080), Option("image.1440", "Up to 1440p", image.MaximumHeight == 1440),
                    Option("image.2160", "Up to 4K", image.MaximumHeight == 2160)], "Keeps the window's proportions; never enlarges a smaller image."),
                Field("format", "Image format", capabilities.ImageFormats.Select(f => Option("format." + f, f.ToString().ToUpperInvariant(), image.Format == f)))]));
            items.Add(Section(SettingsSection.Recording, $"Recording · {video.MaximumDurationSeconds} s maximum", () =>
            {
                var fields = new List<WidgetElement>();

                if(capabilities.MaximumRecordingSeconds>0)
                {
                    fields.Add(RecordingFields("manual",state,video,state.Busy));
                    fields.Add(Field("duration", "Maximum duration", Durations.Where(d => d <= capabilities.MaximumRecordingSeconds).Select(d => Option("duration." + d, $"{d} seconds", d == video.MaximumDurationSeconds))));
                    fields.Add(UI.Text("Reopen WidgetRail to finish a clip early. Every recording stops at its maximum duration.",
                        "capture-library.settings.stop-help").Classes("capture-help"));
                    var audioChoices=capabilities.AudioModes.Select(a=>Option("audio."+a,
                        a==CaptureAudioMode.None?"No audio":a==CaptureAudioMode.Application?"Application audio":a.ToString(),video.Audio==a)).ToList();
                    if(!audioChoices.Any(option=>option.IsSelected))audioChoices.Insert(0,new("current",video.Audio+" (unavailable)","capture-library.option.current",true,IsDisabled:true));
                    fields.Add(Field("audio", "Audio", audioChoices,
                        "Records audio from the captured application. Microphone audio is not recorded. Only available modes are listed."));
                    fields.Add(UI.Text(video.Selection is not null?"Auto values resolve for the selected source. Each clip is limited to 2 GiB.":
                        $"Estimated recording size: {((long)video.Bitrate+(video.Audio==CaptureAudioMode.None?0:CaptureLimits.AudioBitrate))*video.MaximumDurationSeconds/8/1048576d:0.0} MiB + container overhead · limit 2 GiB.","capture-library.settings.estimate").Classes("capture-help"));
                }
                return fields;
            }));
            if (capabilities.RecordingProfiles.Count == 0)
                heading.Add(UI.Alert("Recording unavailable", "No compatible hardware recording options are currently available.",
                    AlertTone.Warning, "capture-library.settings.no-encoder"));
            items.Add(Section(SettingsSection.Preparation, "Preparation · " + (prep.ShowCountdown ? "Countdown on" : "Countdown off"), () =>
            {
                var preparation = new List<WidgetElement> {
                    Field("image.delay", "Screenshot delay", Delays.Select(d => Option("image.delay." + d, d == 0 ? "Immediately" : $"{d} seconds", preferences.PreparationFor(WindowCaptureKind.Screenshot).DelaySeconds == d))),
                    Field("delay", "Clip delay", Delays.Select(d => Option("delay." + d, d == 0 ? "Immediately" : $"{d} seconds", prep.DelaySeconds == d))),
                    Field("countdown", "Show countdown", [Option("countdown.on", "On", prep.ShowCountdown), Option("countdown.off", "Off", !prep.ShowCountdown)]),
                };
                if (capabilities.CursorSupported) preparation.Add(Field("cursor", "Include mouse pointer", [Option("cursor.on", "On", preferences.IncludeCursor), Option("cursor.off", "Off", !preferences.IncludeCursor)]));
                return preparation;
            }));
            if(video.Selection is null && video.Encoding is not null)
            {
                var validation=capabilities.Validate(preferences.Request(WindowCaptureKind.Video));
                if (!validation.Supported)
                {
                    heading.Add(UI.Alert("Check recording settings", Explain(validation.ErrorCode!), AlertTone.Warning, "capture-library.settings.validation"));
                    heading.Add(UI.Button("Open recording settings", SectionAction + "Recording.show", "capture-library.settings.validation.open"));
                }
            }
        }
        else if(recordingCatalog.Snapshot.Error is {} error)
        {
            heading.Add(UI.Alert("Recording options unavailable", error.Message, AlertTone.Warning, "capture-library.settings.loading"));
            heading.Add(UI.Button("Check recording options","capture-library.recording.refresh","capture-library.recording.refresh"));
            if(error.Code=="permission_denied" && !state.PermissionRequired)heading.Add(PermissionsButton());
        }
        else items.Add(UI.Text("Checking supported capture options…", "capture-library.settings.loading").Classes("capture-help"));
        if (state.Capabilities is not null) items.Add(UI.Button("Reset capture settings", "capture-library.option.reset", "capture-library.settings.reset").Disabled(state.Busy||pendingManual));
        if (state.Message is { } message && state.PermissionRequired)
        {
            heading.Add(UI.Alert("Permission needed", message, AlertTone.Warning, "capture-library.settings.status"));
            heading.Add(PermissionsButton());
        }
        else if (state.Message is { } result)
            heading.Add(UI.Text(result, "capture-library.settings.status").Classes("capture-help"));
        var root = UI.Grid("capture-library.settings.page", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            UI.Stack("capture-library.settings.header", heading.ToArray()),
            UI.VerticalScroll("capture-library.settings.scroll", UI.Stack("capture-library.settings.sections", items.ToArray()).RememberChildFocus("capture-library.settings.back")).InGrid(row:1)).InputScope("capture-library.settings.page")
            .Shortcut(ControllerButton.B, "capture-library.settings.back", label: "Library").Classes("capture-library", "capture-library-base");
        return new(ReplayShortcuts(root, state), "capture-library.settings.back",
            Surface: Surface(WidgetSurfaceAppearance.DesktopAcrylic)) { FocusGroupEntryRequest = state.SettingsFocus };
    }

    private static WidgetElement PermissionsButton() => UI.Button("Open permissions", "unused", "capture-library.permissions")
        .OpenIntent(WidgetIntentContracts.WidgetPermissions, JsonSerializer.SerializeToElement(new { packageId = "widgetrail.firstparty.capture-library" }),
            WidgetIntentPresentation.OpenWidget);

    private static string ArtworkKey(CaptureLibraryItem item)=>item.Id+"."+item.Revision.ToString("x",System.Globalization.CultureInfo.InvariantCulture);
}
