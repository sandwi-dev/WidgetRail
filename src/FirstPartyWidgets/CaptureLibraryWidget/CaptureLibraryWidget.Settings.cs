using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private bool preferencesLoaded;
    private sealed record CapturePreferences(int Schema = 1, ScreenshotOptions? Image = null,
        RecordingOptions? Video = null, CapturePreparation? Preparation = null, bool IncludeCursor = false)
    {
        public bool IsValid() => Schema == 1 && (Image?.IsWellFormed() ?? true) &&
            (Video?.IsWellFormed() ?? true) && (Preparation?.IsWellFormed() ?? true);
        public CaptureSessionRequest Request(WindowCaptureKind kind) => new()
        {
            Screenshot = kind == WindowCaptureKind.Screenshot ? Image ?? new() : null,
            Recording = kind == WindowCaptureKind.Video ? Video ?? new() : null,
            Preparation = Preparation ?? new(), IncludeCursor = IncludeCursor,
        };
    }

    private async Task LoadPreferencesAsync(CancellationToken token)
    {
        if (preferencesLoaded) return;
        var saved = await HostServices.PrivateState.ReadJsonAsync(token);
        CapturePreferences? preferences;
        try { preferences = saved.Exists ? JsonSerializer.Deserialize<CapturePreferences>(saved.Json!) : new(); }
        catch (JsonException) { preferences = null; }
        if (preferences?.IsValid() != true) preferences = null;
        model.Update(s => s with { Preferences = preferences, PreferencesRevision = saved.Revision,
            Message = preferences is null ? Explain("capture_preferences_invalid") : s.Message });
        preferencesLoaded = true;
    }

    private static RecordingOptions DefaultVideo(CaptureCapabilities capabilities)
    {
        var profile = capabilities.RecordingProfiles.FirstOrDefault(p => p.Encoder == CaptureEncoderMode.Hardware && p.MaximumHeight == 1080 && p.FramesPerSecond == 30)
            ?? capabilities.RecordingProfiles.OrderByDescending(p => p.Encoder == CaptureEncoderMode.Hardware).ThenBy(p => p.MaximumHeight).ThenBy(p => p.FramesPerSecond).FirstOrDefault();
        return profile is null ? new() : FromProfile(profile, new());
    }
    private static RecordingOptions FromProfile(CaptureRecordingProfile profile, RecordingOptions previous) => previous with
    {
        MaximumWidth = profile.MaximumWidth, MaximumHeight = profile.MaximumHeight,
        FramesPerSecond = profile.FramesPerSecond, Encoder = profile.Encoder,
        Bitrate = profile.MinimumBitrate + (profile.MaximumBitrate - profile.MinimumBitrate) / 2,
    };
    private async Task LoadSettingsAsync(CancellationToken token)
    {
        await LoadPreferencesAsync(token);
        var capabilities = await HostServices.Capture.GetCapabilitiesAsync(token);
        model.Update(s => s with { Capabilities = capabilities,
            Preferences = s.Preferences is { Video: null } preferences ? preferences with { Video = DefaultVideo(capabilities) } : s.Preferences });
    }

    private void ChangeSetting(string action)
    {
        var state = model.Value;
        if (!state.Settings || state.Capabilities is not { } capabilities) return;
        var value = action["capture-library.option.".Length..];
        var old = state.Preferences;
        if (old is null && value != "reset") return;
        var next = old ?? new();
        var image = next.Image ?? new(); var video = next.Video ?? DefaultVideo(capabilities); var prep = next.Preparation ?? new();
        switch (value)
        {
            case "reset": next = new(Video: DefaultVideo(capabilities)); break;
            case "image.original": next = next with { Image = image with { MaximumWidth = null, MaximumHeight = null } }; break;
            case "image.1080": next = next with { Image = image with { MaximumWidth = 1920, MaximumHeight = 1080 } }; break;
            case "image.1440": next = next with { Image = image with { MaximumWidth = 2560, MaximumHeight = 1440 } }; break;
            case "image.2160": next = next with { Image = image with { MaximumWidth = 3840, MaximumHeight = 2160 } }; break;
            case "stop.manual": next = next with { Video = video with { StopManually = true } }; break;
            case "stop.fixed": next = next with { Video = video with { StopManually = false } }; break;
            case "cursor.on" when capabilities.CursorSupported: next = next with { IncludeCursor = true }; break;
            case "cursor.off": next = next with { IncludeCursor = false }; break;
            case "countdown.on": next = next with { Preparation = prep with { ShowCountdown = true } }; break;
            case "countdown.off": next = next with { Preparation = prep with { ShowCountdown = false } }; break;
            default:
                if (value.StartsWith("profile.", StringComparison.Ordinal) && capabilities.RecordingProfiles.FirstOrDefault(p => p.Id == value[8..]) is { } profile)
                    next = next with { Video = FromProfile(profile, video) };
                else if (value.StartsWith("duration.", StringComparison.Ordinal) && int.TryParse(value[9..], out var seconds) && Durations.Contains(seconds) && seconds <= capabilities.MaximumRecordingSeconds)
                    next = next with { Video = video with { MaximumDurationSeconds = seconds } };
                else if (value.StartsWith("delay.", StringComparison.Ordinal) && int.TryParse(value[6..], out var delay) && Delays.Contains(delay))
                    next = next with { Preparation = prep with { DelaySeconds = delay } };
                else if (value.StartsWith("bitrate.", StringComparison.Ordinal) && int.TryParse(value[8..], out var bitrate) && capabilities.RecordingProfiles.Any(p => p.Supports(video with { Bitrate = bitrate })))
                    next = next with { Video = video with { Bitrate = bitrate } };
                else if (value.StartsWith("audio.", StringComparison.Ordinal) && Enum.TryParse<CaptureAudioMode>(value[6..], out var audio) && capabilities.AudioModes.Contains(audio))
                    next = next with { Video = video with { Audio = audio } };
                else if (value.StartsWith("format.", StringComparison.Ordinal) && Enum.TryParse<CaptureImageFormat>(value[7..], out var format) && capabilities.ImageFormats.Contains(format))
                    next = next with { Image = image with { Format = format } };
                else return;
                break;
        }
        Work(async token =>
        {
            try
            {
                var saved = await HostServices.PrivateState.WriteAsync(next, state.PreferencesRevision, cancellationToken: token);
                model.Update(s => s with { Preferences = next, PreferencesRevision = saved.Revision, Message = "Capture settings saved." });
            }
            catch (WidgetCapabilityException error) when (error.ErrorCode == "state_conflict")
            {
                preferencesLoaded = false;
                await LoadPreferencesAsync(token);
                throw new WidgetCapabilityException("capture_preferences_conflict", "Settings changed elsewhere.");
            }
        });
    }
    private static readonly int[] Durations = [5, 10, 15, 30, 60, 120];
    private static readonly int[] Delays = [0, 3, 5, 10];
    private static string ProfileLabel(CaptureRecordingProfile p) => $"{p.MaximumHeight}p · {p.FramesPerSecond} fps · {p.Encoder}";
    private static SelectOption Option(string key, string label, bool selected) => new(key, label, "capture-library.option." + key, selected);

    private WidgetView RenderSettings(State state)
    {
        var items = new List<WidgetElement> {
            UI.Text("Capture settings", "capture-library.settings.title").Classes("capture-title"),
            UI.Button("Back to library", "capture-library.settings.back", "capture-library.settings.back"),
        };
        if (state.Capabilities is { } capabilities && state.Preferences is { } preferences)
        {
            var image = preferences.Image ?? new(); var video = preferences.Video ?? DefaultVideo(capabilities); var prep = preferences.Preparation ?? new();
            WidgetElement Field(string name, string label, IEnumerable<SelectOption> options, string? description = null) =>
                UI.SettingsField("capture-library.setting." + name + ".row", label,
                    UI.Select(label, options.ToArray(), "capture-library.setting." + name).Busy(state.Busy), description);
            items.Add(UI.Stack("capture-library.settings.image", UI.Text("Screenshots", "capture-library.settings.image.title").Classes("capture-heading"),
                Field("resolution", "Resolution", [Option("image.original", "Original resolution", image.MaximumWidth is null),
                    Option("image.1080", "Up to 1080p", image.MaximumHeight == 1080), Option("image.1440", "Up to 1440p", image.MaximumHeight == 1440),
                    Option("image.2160", "Up to 4K", image.MaximumHeight == 2160)], "Keeps the window's proportions; never enlarges a smaller image."),
                Field("format", "Image format", capabilities.ImageFormats.Select(f => Option("format." + f, f.ToString().ToUpperInvariant(), image.Format == f))))
                .Classes("capture-settings-section"));
            var fields = new List<WidgetElement> { UI.Text("Recording", "capture-library.settings.video.title").Classes("capture-heading") };
            if (capabilities.RecordingProfiles.Count == 0) fields.Add(UI.Text("No supported video encoder is available on this PC.", "capture-library.settings.no-encoder").Classes("capture-help"));
            else
            {
                fields.Add(Field("profile", "Video quality", capabilities.RecordingProfiles.Select(p => Option("profile." + p.Id, ProfileLabel(p),
                    p.MaximumWidth == video.MaximumWidth && p.MaximumHeight == video.MaximumHeight && p.FramesPerSecond == video.FramesPerSecond && p.Encoder == video.Encoder)),
                    $"Selected: {video.MaximumHeight}p · {video.FramesPerSecond} fps · {video.Encoder}. Only available encoders are listed."));
                var profile = capabilities.RecordingProfiles.FirstOrDefault(p => p.Supports(video));
                if (profile is not null)
                {
                    var rates = new[] { profile.MinimumBitrate, profile.MinimumBitrate + (profile.MaximumBitrate - profile.MinimumBitrate) / 2, profile.MaximumBitrate, video.Bitrate }.Distinct().Order();
                    fields.Add(Field("bitrate", "Bitrate", rates.Select(rate => Option("bitrate." + rate, $"{rate / 1_000_000d:0.#} Mbps", rate == video.Bitrate)), "Higher bitrate retains more detail and uses more storage."));
                }
                fields.Add(Field("duration", "Maximum duration", Durations.Where(d => d <= capabilities.MaximumRecordingSeconds).Select(d => Option("duration." + d, $"{d} seconds", d == video.MaximumDurationSeconds))));
                fields.Add(Field("stop", "Stop recording", [Option("stop.fixed", "At maximum duration", !video.StopManually), Option("stop.manual", "When I reopen WidgetRail", video.StopManually)],
                    "Reopening always finishes the clip early. Maximum duration is a safety limit in either mode."));
                fields.Add(Field("audio", "Audio", capabilities.AudioModes.Select(a => Option("audio." + a, a == CaptureAudioMode.None ? "No audio" : a.ToString(), video.Audio == a)),
                    "Only validated audio modes are available."));
                fields.Add(UI.Text($"Estimated video size: {(long)video.Bitrate * video.MaximumDurationSeconds / 8 / 1048576d:0.0} MiB + container overhead · limit 128 MiB.", "capture-library.settings.estimate").Classes("capture-help"));
            }
            items.Add(UI.Stack("capture-library.settings.video", fields.ToArray()).Classes("capture-settings-section"));
            var preparation = new List<WidgetElement> {
                UI.Text("Preparation", "capture-library.settings.preparation.title").Classes("capture-heading"),
                Field("delay", "Time to get ready", Delays.Select(d => Option("delay." + d, d == 0 ? "Immediately" : $"{d} seconds", prep.DelaySeconds == d))),
                Field("countdown", "Show countdown", [Option("countdown.on", "On", prep.ShowCountdown), Option("countdown.off", "Off", !prep.ShowCountdown)]),
            };
            if (capabilities.CursorSupported) preparation.Add(Field("cursor", "Include mouse pointer", [Option("cursor.on", "On", preferences.IncludeCursor), Option("cursor.off", "Off", !preferences.IncludeCursor)]));
            items.Add(UI.Stack("capture-library.settings.preparation", preparation.ToArray()).Classes("capture-settings-section"));
            var validation = capabilities.Validate(preferences.Request(WindowCaptureKind.Video));
            if (!validation.Supported) items.Add(UI.Text(Explain(validation.ErrorCode!) + " Your saved quality has not been changed.", "capture-library.settings.validation").Classes("capture-help"));
        }
        else if (state.Busy) items.Add(UI.Text("Checking supported capture options…", "capture-library.settings.loading").Classes("capture-help"));
        if (state.Capabilities is not null) items.Add(UI.Button("Reset capture settings", "capture-library.option.reset", "capture-library.settings.reset").Busy(state.Busy));
        if (state.Message is { } message) items.Add(UI.Text(message, "capture-library.settings.status").Classes("capture-help"));
        return new(UI.VerticalScroll("capture-library.settings.page", items.ToArray()).InputScope("capture-library.settings.page")
            .Shortcut(ControllerButton.B, "capture-library.settings.back", label: "Library").Classes("capture-library"), "capture-library.settings.back");
    }

    public override async ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(WidgetArtworkHandle handle, CancellationToken cancellationToken = default)
    {
        if (model.Value.Page?.Items.Any(item => item.Id == handle.Value && item.Available && item.Media.ContentType == "image/png") != true) return null;
        try
        {
            var thumbnail = await HostServices.CaptureLibrary.GetThumbnailAsync(handle.Value, cancellationToken);
            return new(WidgetArtworkContentType.Png, thumbnail.Bytes);
        }
        catch (WidgetCapabilityException) { return null; }
    }
}
