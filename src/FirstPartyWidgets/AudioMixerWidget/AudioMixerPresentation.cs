using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.AudioMixer;

internal enum AudioMixerPreferredFocusTarget
{
    MasterOutput,
    Microphone,
    Session,
    OutputDevice,
    InputDevice,
    Spatial,
}

internal sealed record AudioMixerSessionPresentation(
    WidgetAudioSession Session,
    AudioMixerSessionControlIds Controls,
    bool VolumePending,
    bool MutePending);

internal sealed record AudioMixerPresentationState(
    AudioMixerViewState ViewState,
    IReadOnlyList<AudioMixerSessionPresentation> Sessions,
    WidgetAudioOutput? Output,
    IReadOnlyList<WidgetAudioDevice> Devices,
    WidgetAudioInput? Input,
    AudioOptionalSectionState DeviceState,
    AudioOptionalSectionState InputState,
    AudioMixerPreferredFocusTarget PreferredFocusTarget,
    string? SelectedSessionId,
    string Status,
    bool StatusIsError,
    bool DeviceSwitchPending = false,
    WidgetAudioSpatial? Spatial = null,
    AudioOptionalSectionState SpatialState = AudioOptionalSectionState.Initial,
    bool SpatialPending = false,
    string? SpatialFeedback = null);

internal static class AudioMixerPresentation
{
    private const double VolumeStep = AudioMixerWidget.VolumeStep;
    private static readonly WidgetQuickActionCapability DashboardOutputVolume = new(
        WidgetAudioCapabilities.SetOutputVolume.CapabilityId,
        WidgetAudioCapabilities.SetOutputVolume.OperationId);
    private static readonly WidgetQuickActionCapability DashboardOutputMute = new(
        WidgetAudioCapabilities.SetOutputMuted.CapabilityId,
        WidgetAudioCapabilities.SetOutputMuted.OperationId);
    private static readonly WidgetSurfaceHints CompactSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 520,
        PreferredHeight = 580,
        MinimumWidth = 320,
        MinimumHeight = 360,
    };

    internal static WidgetView Render(AudioMixerPresentationState state)
    {
        var view = RenderContent(state);
        var children = new List<WidgetElement> { view.Root };
        if (state.SpatialFeedback is { Length: > 0 } feedback)
            children.Add(UI.Toast("Spatial sound couldn't be changed", feedback,
                ToastTone.Danger, "audio.spatial.toast").Classes("audio-toast"));
        return view with { Root = UI.Stack("audio.shell", children.ToArray()).Classes("audio-shell"), ActiveInputScopeId = "audio-mixer" };
    }

    private static WidgetView RenderContent(AudioMixerPresentationState state)
    {
        var header = UI.Stack("audio.header",
            UI.Text("CONTROL CENTER", "audio.eyebrow", "Control Center").Classes("audio-eyebrow"),
            UI.Text("Audio Mixer", "audio.title", "Audio Mixer").Classes("audio-title"),
            UI.Text(state.Status, "audio.status", state.Status).Classes(
                "audio-status",
                state.StatusIsError ? "is-error" : state.ViewState == AudioMixerViewState.Ready ? "is-live" : "is-neutral"))
            .Classes("audio-header");

        if (state.ViewState is not (AudioMixerViewState.Ready or AudioMixerViewState.Empty) ||
            state.Output is null)
            return RenderNonSessionState(header, state.ViewState);

        var sessionControls = state.Sessions.Select(session => session.Controls).ToArray();
        var controlsBySessionId = state.Sessions.ToDictionary(
            item => item.Session.SessionId,
            item => item.Controls,
            StringComparer.Ordinal);
        var firstControls = sessionControls.FirstOrDefault();
        var deviceRetryId = IsRetryable(state.DeviceState) ? "audio.devices.retry" : null;
        var inputFocusId = state.Input is not null
            ? "audio.input.volume.slider"
            : IsRetryable(state.InputState) ? "audio.input.retry" : null;
        var firstSessionFocusId = firstControls?.VolumeSlider;
        var outputDeviceId = state.Devices.Any(device => device.Direction == WidgetAudioDeviceDirection.Output)
            ? "audio.devices.output.select" : null;
        var inputDeviceId = state.Devices.Any(device => device.Direction == WidgetAudioDeviceDirection.Input)
            ? "audio.devices.input.select" : null;
        var currentOutputId = state.Devices.FirstOrDefault(device => device.Direction == WidgetAudioDeviceDirection.Output && device.IsDefault)?.DeviceId;
        var spatial = state.Spatial?.DeviceId == currentOutputId ? state.Spatial : null;
        var spatialId = outputDeviceId is null ? null : spatial is not null ? "audio.spatial.select" : "audio.spatial.retry";
        var lastDeviceId = inputDeviceId ?? spatialId ?? outputDeviceId ?? deviceRetryId;
        var firstFocusAfterMaster = outputDeviceId ?? inputDeviceId ?? deviceRetryId ?? inputFocusId ?? firstSessionFocusId ?? "audio.retry";
        var initialFocusId = state.PreferredFocusTarget switch
        {
            AudioMixerPreferredFocusTarget.Spatial when spatialId is not null => spatialId,
            AudioMixerPreferredFocusTarget.OutputDevice when outputDeviceId is not null => outputDeviceId,
            AudioMixerPreferredFocusTarget.InputDevice when inputDeviceId is not null => inputDeviceId,
            AudioMixerPreferredFocusTarget.Microphone when state.Input is not null => "audio.input.volume.slider",
            AudioMixerPreferredFocusTarget.Microphone when inputFocusId is not null => inputFocusId,
            AudioMixerPreferredFocusTarget.Microphone when firstSessionFocusId is not null => firstSessionFocusId,
            AudioMixerPreferredFocusTarget.Session when state.SelectedSessionId is not null &&
                controlsBySessionId.TryGetValue(state.SelectedSessionId, out var selectedControls) =>
                selectedControls.VolumeSlider,
            _ => "audio.master.volume.slider",
        };

        var output = state.Output;
        var masterPercent = VolumePercent(output.Volume);
        var quickActions = MasterQuickActions(output, masterPercent);
        var masterMute = UI.Icon(
                output.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Volume,
                "audio.master.mute.icon",
                output.IsMuted ? "Master output muted" : "Master output audible")
            .Classes("audio-mute-icon", "audio-master-mute", output.IsMuted ? "is-muted" : "is-audible");
        var masterSlider = UI.Slider(
                output.Volume, 0, 1, VolumeStep,
                "output.volume.set",
                "audio.master.volume.slider",
                $"Master output volume, {(output.IsMuted ? "muted" : "audible")}. Press A to {(output.IsMuted ? "unmute" : "mute")}",
                accessibilityValue: $"{masterPercent}%",
                activationAction: "output.mute.toggle")
            .Busy(state.DeviceSwitchPending)
            .Classes("audio-volume-slider", "audio-master-slider")
            .FocusDown(firstFocusAfterMaster);
        var masterCard = UI.Stack("audio.master.card",
                UI.Row("audio.master.heading",
                    UI.Text("MASTER OUTPUT", "audio.master.label", "Master output").Classes("audio-master-label"),
                    UI.Text(output.IsMuted ? "MUTED" : "LIVE", "audio.master.state",
                        output.IsMuted ? "Master output muted" : "Master output audible")
                        .Classes("audio-master-state", output.IsMuted ? "is-muted" : "is-live"))
                    .Classes("audio-master-heading"),
                UI.Row("audio.master.controls",
                    masterMute,
                    masterSlider,
                    UI.Text($"{masterPercent}%", "audio.master.volume.value",
                            $"Master output volume {masterPercent} percent")
                        .Classes("audio-volume-value"))
                    .Classes("audio-volume-control-row", "audio-master-controls"))
            .Classes("audio-master-card");

        var supplemental = new List<WidgetElement>();
        if (state.Devices.Count > 0)
        {
            supplemental.Add(UI.Stack("audio.devices.card",
                    DeviceSelector(state, WidgetAudioDeviceDirection.Output, "Output device",
                        "audio.master.volume.slider", spatialId ?? inputDeviceId ?? inputFocusId ?? firstSessionFocusId ?? "audio.retry"),
                    SpatialControl(state, spatial, outputDeviceId,
                        inputDeviceId ?? inputFocusId ?? firstSessionFocusId ?? "audio.retry"),
                    DeviceSelector(state, WidgetAudioDeviceDirection.Input, "Microphone",
                        spatialId ?? outputDeviceId ?? "audio.master.volume.slider", inputFocusId ?? firstSessionFocusId ?? "audio.retry"))
                .Classes("audio-device-card"));
        }
        else if (IsRetryable(state.DeviceState))
        {
            supplemental.Add(RenderOptionalState(
                "devices", "DEVICE NAMES", OptionalStateCopy(state.DeviceState, "device names"),
                "devices.retry", "Retry device names", "audio.master.volume.slider",
                inputFocusId ?? firstSessionFocusId ?? "audio.retry"));
        }
        else if (state.DeviceState is AudioOptionalSectionState.Loading or AudioOptionalSectionState.Empty)
        {
            supplemental.Add(RenderOptionalInfo(
                "devices", "DEVICE NAMES", OptionalStateCopy(state.DeviceState, "device names")));
        }

        if (state.Input is { } input)
        {
            var inputPercent = VolumePercent(input.Volume);
            var inputSlider = UI.Slider(
                    input.Volume, 0, 1, VolumeStep,
                    "input.volume.set", "audio.input.volume.slider",
                    $"Microphone volume, {(input.IsMuted ? "muted" : "live")}. Press A to {(input.IsMuted ? "unmute" : "mute")}",
                    accessibilityValue: $"{inputPercent}%",
                    activationAction: "input.mute.toggle")
                .Busy(state.DeviceSwitchPending)
                .FocusUp(lastDeviceId ?? "audio.master.volume.slider")
                .Classes("audio-volume-slider", "audio-input-slider")
                .FocusDown(firstControls?.VolumeSlider ?? "audio.retry");
            supplemental.Add(UI.Stack("audio.input.card",
                    UI.Row("audio.input.heading",
                        UI.Text("MICROPHONE LEVEL", "audio.input.label", "Microphone input level")
                            .Classes("audio-master-label"),
                        UI.Text(input.IsMuted ? "MUTED" : "LIVE", "audio.input.state",
                                input.IsMuted ? "Microphone muted" : "Microphone live")
                            .Classes("audio-master-state", input.IsMuted ? "is-muted" : "is-live"))
                        .Classes("audio-master-heading"),
                    UI.Row("audio.input.controls",
                        UI.Icon(input.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Microphone,
                                "audio.input.mute.icon", input.IsMuted ? "Microphone muted" : "Microphone live")
                            .Classes("audio-mute-icon", input.IsMuted ? "is-muted" : "is-audible"),
                        inputSlider,
                        UI.Text($"{inputPercent}%", "audio.input.volume.value", $"Microphone volume {inputPercent} percent")
                            .Classes("audio-volume-value"))
                        .Classes("audio-volume-control-row"))
                .Classes("audio-master-card", "audio-input-card"));
        }
        else if (IsRetryable(state.InputState))
        {
            supplemental.Add(RenderOptionalState(
                "input", "MICROPHONE", OptionalStateCopy(state.InputState, "microphone controls"),
                "input.retry", "Retry microphone", lastDeviceId ?? "audio.master.volume.slider",
                firstSessionFocusId ?? "audio.retry"));
        }
        else if (state.InputState is AudioOptionalSectionState.Loading or AudioOptionalSectionState.Empty)
        {
            supplemental.Add(RenderOptionalInfo(
                "input", "MICROPHONE", OptionalStateCopy(state.InputState, "microphone")));
        }

        if (state.Sessions.Count == 0)
        {
            var retry = UI.Button("Check again", "retry", "audio.retry")
                .Icon(WidgetGlyph.Refresh, "Check for application audio")
                .FocusUp(inputFocusId ?? lastDeviceId ?? "audio.master.volume.slider")
                .Classes("audio-retry-action");
            var emptyChildren = new List<WidgetElement> { header, masterCard };
            emptyChildren.AddRange(supplemental);
            emptyChildren.Add(UI.Stack("audio.state.card",
                        UI.Text("No application audio yet", "audio.state.title", "No application audio yet")
                            .Classes("audio-state-title"),
                        UI.Text("Start playback in an application. New sessions appear here automatically.",
                                "audio.state.help", "Start playback to create an application audio session")
                            .Classes("audio-help", "is-neutral"),
                        retry).Classes("audio-state-card"));
            var emptyRoot = UI.VerticalScroll("audio.root", emptyChildren.ToArray())
                .InputScope("audio-mixer")
                .Classes("audio-mixer-widget", "has-master", "has-state");
            return new WidgetView(emptyRoot, InitialFocusId: initialFocusId,
                QuickActions: quickActions, Surface: CompactSurface);
        }

        var sessionRows = new WidgetElement[state.Sessions.Count];
        for (var index = 0; index < state.Sessions.Count; index++)
        {
            var previous = index == 0 ? null : sessionControls[index - 1];
            var next = index + 1 == state.Sessions.Count ? null : sessionControls[index + 1];
            sessionRows[index] = RenderSessionRow(
                state.Sessions[index], previous, next,
                inputFocusId ?? lastDeviceId ?? "audio.master.volume.slider");
        }

        var rootChildren = new List<WidgetElement> { header, masterCard };
        rootChildren.AddRange(supplemental);
        rootChildren.Add(UI.Row("audio.sessions.heading",
                    UI.Text("APPLICATIONS", "audio.sessions.label", "Application volume mixer")
                        .Classes("audio-sessions-label"),
                    UI.Text($"{state.Sessions.Count} apps", "audio.sessions.count",
                            $"{state.Sessions.Count} application audio sessions")
                        .Classes("audio-sessions-count"))
                    .Classes("audio-sessions-heading"));
        rootChildren.Add(UI.Stack("audio.sessions.list", sessionRows)
            .Classes("audio-session-list"));
        var root = UI.VerticalScroll("audio.root", rootChildren.ToArray())
            .InputScope("audio-mixer")
            .Classes("audio-mixer-widget", "has-sessions");
        return new WidgetView(root, InitialFocusId: initialFocusId,
            QuickActions: quickActions, Surface: CompactSurface);
    }

    private static IReadOnlyList<WidgetQuickAction> MasterQuickActions(
        WidgetAudioOutput output,
        int currentPercent)
    {
        var lowerPercent = VolumePercent(Math.Clamp(output.Volume - VolumeStep, 0, 1));
        var higherPercent = VolumePercent(Math.Clamp(output.Volume + VolumeStep, 0, 1));
        return
        [
            new(ControllerButton.LeftBumper,
                AudioMixerWidget.DecreaseOutputVolumeActionId,
                lowerPercent == currentPercent
                    ? $"Master output is at minimum ({currentPercent}%)"
                    : $"Lower master output: {currentPercent}% to {lowerPercent}%",
                DashboardOutputVolume),
            new(ControllerButton.X,
                "output.mute.toggle",
                output.IsMuted
                    ? $"Unmute master output at {currentPercent}%"
                    : $"Mute master output at {currentPercent}%",
                DashboardOutputMute),
            new(ControllerButton.RightBumper,
                AudioMixerWidget.IncreaseOutputVolumeActionId,
                higherPercent == currentPercent
                    ? $"Master output is at maximum ({currentPercent}%)"
                    : $"Raise master output: {currentPercent}% to {higherPercent}%",
                DashboardOutputVolume),
        ];
    }

    private static StackElement RenderSessionRow(
        AudioMixerSessionPresentation item,
        AudioMixerSessionControlIds? previous,
        AudioMixerSessionControlIds? next,
        string firstFocusUp)
    {
        var session = item.Session;
        var controls = item.Controls;
        var isPending = item.MutePending;
        var percent = VolumePercent(session.Volume);
        var mute = UI.Icon(
                session.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Volume,
                controls.MuteIcon,
                session.IsMuted ? $"{session.DisplayName} muted" : $"{session.DisplayName} audible")
            .Classes("audio-mute-icon", "audio-session-mute",
                session.IsMuted ? "is-muted" : "is-audible");
        var slider = UI.Slider(
                session.Volume, 0, 1, VolumeStep,
                controls.VolumeSet,
                controls.VolumeSlider,
                $"{session.DisplayName} volume, {(session.IsMuted ? "muted" : "audible")}. Press A to {(session.IsMuted ? "unmute" : "mute")}",
                accessibilityValue: $"{percent}%",
                activationAction: controls.Mute)
            .FocusUp(previous?.VolumeSlider ?? firstFocusUp)
            .Classes("audio-volume-slider", "audio-session-slider");
        if (next is not null) slider = slider.FocusDown(next.VolumeSlider);

        return UI.Stack(controls.Row,
                UI.Row(controls.Heading,
                    UI.Text(session.DisplayName, controls.Name, session.DisplayName)
                        .Classes("audio-session-name"),
                    UI.Text(session.IsActive ? "ACTIVE" : "IDLE", controls.State,
                            session.IsActive ? $"{session.DisplayName} audio is active" : $"{session.DisplayName} audio is idle")
                        .Classes("audio-session-state", session.IsActive ? "is-active" : "is-idle"))
                    .Classes("audio-session-heading"),
                UI.Row(controls.Controls,
                    mute,
                    slider,
                    UI.Text($"{percent}%", controls.Value, $"{session.DisplayName} volume {percent} percent")
                        .Classes("audio-volume-value"))
                    .Classes("audio-volume-control-row", "audio-session-controls"))
            .Classes("audio-session-card", isPending ? "is-pending" : "is-ready");
    }

    private static WidgetElement SpatialControl(AudioMixerPresentationState state, WidgetAudioSpatial? spatial,
        string? outputId, string down)
    {
        if (outputId is null) return UI.Text("Connect an output device to configure spatial sound.", "audio.spatial.unavailable").Classes("audio-help");
        var children = new List<WidgetElement>();
        if (spatial is not null)
        {
            children.Add(UI.Select("Spatial sound", spatial.Formats.Select(format => new SelectOption(
                format.FormatId, format.DisplayName, $"spatial.set.{spatial.DeviceId}.{format.FormatId}",
                IsSelected: format.FormatId == spatial.SelectedFormatId,
                IsDisabled: format.FormatId == "other" || (!spatial.IsSupported && format.FormatId != "off"),
                IsBusy: state.SpatialPending || state.DeviceSwitchPending)).ToArray(), "audio.spatial.select")
                .FocusUp(outputId).FocusDown(down).Classes("audio-device-selector"));
            if (spatial.ActiveFormatId != spatial.SelectedFormatId)
            {
                var active = spatial.Formats.FirstOrDefault(format => format.FormatId == spatial.ActiveFormatId)?.DisplayName ?? "another format";
                children.Add(UI.Text($"Currently active: {active}", "audio.spatial.active").Classes("audio-help"));
            }
            if (!spatial.IsSupported) children.Add(UI.Text("Spatial sound isn't supported by this output.", "audio.spatial.unsupported").Classes("audio-help"));
        }
        else
        {
            var copy = state.SpatialState is AudioOptionalSectionState.PermissionDenied or AudioOptionalSectionState.Revoked
                ? "Allow spatial sound information in Audio Mixer permissions, then check again."
                : state.SpatialState is AudioOptionalSectionState.Initial or AudioOptionalSectionState.Loading
                    ? "Loading spatial sound…"
                    : "Spatial sound information is unavailable. Other audio controls still work.";
            children.Add(UI.Text(copy, "audio.spatial.help").Classes("audio-help"));
            children.Add(UI.Button("Check spatial sound", "spatial.retry", "audio.spatial.retry")
                .FocusUp(outputId).FocusDown(down).Classes("audio-retry-action"));
        }
        return UI.Stack("audio.spatial.card", children.ToArray()).Classes("audio-device-copy");
    }

    private static WidgetElement DeviceSelector(AudioMixerPresentationState state,
        WidgetAudioDeviceDirection direction, string label, string up, string down)
    {
        var suffix = direction == WidgetAudioDeviceDirection.Output ? "output" : "input";
        var devices = state.Devices.Where(device => device.Direction == direction).ToArray();
        var selected = devices.FirstOrDefault(device => device.IsDefault)?.DeviceId;
        var options = devices.Take(selected is null ? 127 : 128).Select(device => new SelectOption(
            device.DeviceId, device.DisplayName, $"device.{suffix}.{device.DeviceId}",
            IsSelected: device.DeviceId == selected, IsBusy: state.DeviceSwitchPending || state.SpatialPending)).ToList();
        if (selected is null)
            options.Insert(0, new SelectOption($"device.{suffix}.none", devices.Length == 0 ? "No device connected" : "Choose a device",
                $"device.{suffix}.none", IsSelected: true, IsDisabled: true));
        return UI.Select(label, options, $"audio.devices.{suffix}.select", label)
            .Disabled(devices.Length == 0).FocusUp(up).FocusDown(down)
            .Classes("audio-device-selector");
    }

    private static StackElement RenderOptionalState(
        string suffix, string label, string copy, string action, string buttonLabel,
        string focusUp, string focusDown) =>
        UI.Stack($"audio.{suffix}.state.card",
                UI.Text(label, $"audio.{suffix}.state.label", label).Classes("audio-device-label"),
                UI.Text(copy, $"audio.{suffix}.state.help", copy).Classes("audio-help", "is-neutral"),
                UI.Button(buttonLabel, action, $"audio.{suffix}.retry")
                    .Icon(WidgetGlyph.Refresh, buttonLabel)
                    .FocusUp(focusUp)
                    .FocusDown(focusDown)
                    .Classes("audio-retry-action"))
            .Classes("audio-state-card", $"audio-{suffix}-state");

    private static StackElement RenderOptionalInfo(string suffix, string label, string copy) =>
        UI.Stack($"audio.{suffix}.state.card",
                UI.Text(label, $"audio.{suffix}.state.label", label).Classes("audio-device-label"),
                UI.Text(copy, $"audio.{suffix}.state.help", copy).Classes("audio-help", "is-neutral"))
            .Classes("audio-state-card", $"audio-{suffix}-state", "is-informational");

    private static WidgetView RenderNonSessionState(StackElement header, AudioMixerViewState state)
    {
        var (title, help, buttonLabel, error) = state switch
        {
            AudioMixerViewState.Initial => ("Ready when you are", "Open the widget to load application audio sessions.", "Load audio sessions", false),
            AudioMixerViewState.Loading => ("Finding application audio", "The host audio provider is loading one current snapshot.", "Loading…", false),
            AudioMixerViewState.Empty => ("No application audio yet", "Start playback in an application. New sessions appear here automatically.", "Check again", false),
            AudioMixerViewState.PermissionDenied => ("Audio access is off", "Grant read access in Settings → Permissions, then try again.", "Try again", true),
            AudioMixerViewState.LifecycleDenied => ("Audio paused by lifecycle", "Return this widget to the foreground before requesting audio sessions.", "Try again", true),
            AudioMixerViewState.ChannelClosed => ("Audio service disconnected", "The protected host channel closed. Reopen or retry the widget.", "Reconnect", true),
            AudioMixerViewState.ServiceUnavailable => ("Audio service unavailable", "This worker was not given the host audio service.", "Try again", true),
            _ => ("Audio could not be loaded", "The provider returned an unexpected error. No system details were exposed.", "Try again", true),
        };
        var retry = UI.Button(buttonLabel, "retry", "audio.retry")
            .Icon(WidgetGlyph.Refresh, buttonLabel)
            .Busy(state == AudioMixerViewState.Loading)
            .Disabled(state == AudioMixerViewState.Loading)
            .Classes("audio-retry-action");
        var root = UI.VerticalScroll("audio.root",
                header,
                UI.Stack("audio.state.card",
                    UI.Text(title, "audio.state.title", title).Classes("audio-state-title"),
                    UI.Text(help, "audio.state.help", help).Classes("audio-help", error ? "is-error" : "is-neutral"),
                    retry).Classes("audio-state-card"))
            .InputScope("audio-mixer")
            .Classes("audio-mixer-widget", error ? "has-error" : "has-state");
        return new WidgetView(root, InitialFocusId: "audio.retry", Surface: CompactSurface);
    }

    private static bool IsRetryable(AudioOptionalSectionState state) => state is
        AudioOptionalSectionState.PermissionDenied or AudioOptionalSectionState.Revoked or
        AudioOptionalSectionState.Unavailable;

    private static string OptionalStateCopy(AudioOptionalSectionState state, string subject) => state switch
    {
        AudioOptionalSectionState.Loading => $"Loading {subject}…",
        AudioOptionalSectionState.Empty => subject == "microphone"
            ? "No default microphone is currently available."
            : "No default audio device names are currently available.",
        AudioOptionalSectionState.PermissionDenied =>
            $"Access to {subject} is off. Grant it in Settings, then retry this section.",
        AudioOptionalSectionState.Revoked =>
            $"Access to {subject} was revoked. Review Permissions, then retry this section.",
        _ => $"{subject[..1].ToUpperInvariant()}{subject[1..]} are temporarily unavailable. Retry this section.",
    };

    private static int VolumePercent(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 100, MidpointRounding.AwayFromZero);
}
