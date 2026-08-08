using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.AudioMixer;

public enum AudioMixerViewState
{
    Initial,
    Loading,
    Ready,
    Empty,
    PermissionDenied,
    LifecycleDenied,
    ChannelClosed,
    ServiceUnavailable,
    Error,
}

public enum AudioOptionalSectionState
{
    Initial,
    Loading,
    Healthy,
    Empty,
    PermissionDenied,
    Revoked,
    Unavailable,
}

/// <summary>
/// Event-driven, controller-first audio session mixer. The widget uses only the
/// typed host audio service: it never opens Core Audio objects or transport
/// handles itself.
/// </summary>
public sealed class AudioMixerWidget : Widget
{
    public const double VolumeStep = 0.05;
    private static readonly TimeSpan ConfirmationDelay = TimeSpan.FromMilliseconds(650);
    private static readonly WidgetSurfaceHints CompactSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 520,
        PreferredHeight = 520,
        MinimumWidth = 320,
        MinimumHeight = 360,
    };

    private readonly object _stateLock = new();
    private IReadOnlyList<WidgetAudioSession> _sessions = [];
    private IReadOnlyDictionary<string, SessionControlIds> _sessionControls =
        new Dictionary<string, SessionControlIds>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, SessionActionTarget> _sessionActions =
        new Dictionary<string, SessionActionTarget>(StringComparer.Ordinal);
    private WidgetAudioOutput? _output;
    private IReadOnlyList<WidgetAudioDevice> _devices = [];
    private WidgetAudioInput? _input;
    private AudioOptionalSectionState _deviceState = AudioOptionalSectionState.Initial;
    private AudioOptionalSectionState _inputState = AudioOptionalSectionState.Initial;
    private AudioMixerViewState _viewState = AudioMixerViewState.Initial;
    private string? _selectedSessionId;
    private int _selectedIndex;
    private PreferredFocusTarget _preferredFocusTarget = PreferredFocusTarget.MasterOutput;
    private string _status = "Audio sessions load when this widget becomes visible";
    private bool _statusIsError;
    private readonly Dictionary<string, SessionPendingState> _sessionPending =
        new(StringComparer.Ordinal);
    private readonly OutputPendingState _outputPending = new();
    private readonly OutputPendingState _inputPending = new();
    private readonly SemaphoreSlim _deviceRetrySignal = new(0, 1);
    private readonly SemaphoreSlim _inputRetrySignal = new(0, 1);
    private CancellationTokenSource? _deviceAttemptLifetime;
    private CancellationTokenSource? _inputAttemptLifetime;
    private CancellationTokenSource? _runLifetime;
    private long _runGeneration;
    private int _activationCount;
    private int _fetchCount;

    public AudioMixerViewState ViewState
    {
        get { lock (_stateLock) return _viewState; }
    }

    public string? SelectedSessionId
    {
        get { lock (_stateLock) return _selectedSessionId; }
    }

    public IReadOnlyList<WidgetAudioSession> Sessions
    {
        get { lock (_stateLock) return _sessions.ToArray(); }
    }

    public WidgetAudioOutput? Output
    {
        get { lock (_stateLock) return _output; }
    }

    public IReadOnlyList<WidgetAudioDevice> Devices
    {
        get { lock (_stateLock) return _devices.ToArray(); }
    }

    public WidgetAudioInput? Input
    {
        get { lock (_stateLock) return _input; }
    }

    public AudioOptionalSectionState DeviceState
    {
        get { lock (_stateLock) return _deviceState; }
    }

    public AudioOptionalSectionState InputState
    {
        get { lock (_stateLock) return _inputState; }
    }

    public int ActivationCount => Volatile.Read(ref _activationCount);
    public int FetchCount => Volatile.Read(ref _fetchCount);

    public override WidgetView Render()
    {
        AudioMixerViewState viewState;
        IReadOnlyList<WidgetAudioSession> sessions;
        IReadOnlyDictionary<string, SessionControlIds> controlsBySessionId;
        IReadOnlyDictionary<string, SessionPendingView> pendingBySessionId;
        WidgetAudioOutput? output;
        IReadOnlyList<WidgetAudioDevice> devices;
        WidgetAudioInput? input;
        AudioOptionalSectionState deviceState;
        AudioOptionalSectionState inputState;
        PreferredFocusTarget preferredFocusTarget;
        string? selectedSessionId;
        string status;
        bool statusIsError;
        lock (_stateLock)
        {
            viewState = _viewState;
            sessions = _sessions;
            controlsBySessionId = _sessionControls;
            output = _output;
            devices = _devices;
            input = _input;
            deviceState = _deviceState;
            inputState = _inputState;
            preferredFocusTarget = _preferredFocusTarget;
            selectedSessionId = _selectedSessionId;
            status = _status;
            statusIsError = _statusIsError;
            pendingBySessionId = _sessionPending.ToDictionary(
                pair => pair.Key,
                pair => new SessionPendingView(pair.Value.VolumeIsPending, pair.Value.MuteIsPending),
                StringComparer.Ordinal);
        }

        var header = UI.Stack("audio.header",
            UI.Text("CONTROL CENTER", "audio.eyebrow", "Control Center").Classes("audio-eyebrow"),
            UI.Text("Audio Mixer", "audio.title", "Audio Mixer").Classes("audio-title"),
            UI.Text(status, "audio.status", status).Classes(
                "audio-status",
                statusIsError ? "is-error" : viewState == AudioMixerViewState.Ready ? "is-live" : "is-neutral"))
            .Classes("audio-header");

        if (viewState is not (AudioMixerViewState.Ready or AudioMixerViewState.Empty) || output is null)
            return RenderNonSessionState(header, viewState);

        var sessionControls = sessions.Select(session => controlsBySessionId[session.SessionId]).ToArray();
        var firstControls = sessionControls.FirstOrDefault();
        var deviceRetryId = IsRetryable(deviceState) ? "audio.devices.retry" : null;
        var inputFocusId = input is not null
            ? "audio.input.volume.slider"
            : IsRetryable(inputState) ? "audio.input.retry" : null;
        var firstSessionFocusId = firstControls?.VolumeSlider;
        var firstFocusAfterMaster = deviceRetryId ?? inputFocusId ?? firstSessionFocusId ?? "audio.retry";
        var initialFocusId = preferredFocusTarget switch
        {
            PreferredFocusTarget.Microphone when input is not null => "audio.input.volume.slider",
            PreferredFocusTarget.Microphone when inputFocusId is not null => inputFocusId,
            PreferredFocusTarget.Microphone when firstSessionFocusId is not null => firstSessionFocusId,
            PreferredFocusTarget.Session when selectedSessionId is not null &&
                controlsBySessionId.TryGetValue(selectedSessionId, out var selectedControls) =>
                selectedControls.VolumeSlider,
            _ => "audio.master.volume.slider",
        };

        var masterPercent = VolumePercent(output.Volume);
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
            .Classes("audio-volume-slider", "audio-master-slider");

        masterSlider = masterSlider.FocusDown(firstFocusAfterMaster);
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
        var defaultOutput = devices.FirstOrDefault(device =>
            device.Direction == WidgetAudioDeviceDirection.Output && device.IsDefault);
        var defaultInput = devices.FirstOrDefault(device =>
            device.Direction == WidgetAudioDeviceDirection.Input && device.IsDefault);
        if (defaultOutput is not null || defaultInput is not null)
        {
            supplemental.Add(UI.Stack("audio.devices.card",
                    DeviceRow("output", WidgetGlyph.Volume, "OUTPUT",
                        defaultOutput?.DisplayName ?? "No default output"),
                    DeviceRow("input", WidgetGlyph.Microphone, "MICROPHONE",
                        defaultInput?.DisplayName ?? "No default microphone"))
                .Classes("audio-device-card"));
        }
        else if (IsRetryable(deviceState))
        {
            supplemental.Add(RenderOptionalState(
                "devices", "DEVICE NAMES", OptionalStateCopy(deviceState, "device names"),
                "devices.retry", "Retry device names", "audio.master.volume.slider",
                inputFocusId ?? firstSessionFocusId ?? "audio.retry"));
        }
        else if (deviceState is AudioOptionalSectionState.Loading or AudioOptionalSectionState.Empty)
        {
            supplemental.Add(RenderOptionalInfo(
                "devices", "DEVICE NAMES", OptionalStateCopy(deviceState, "device names")));
        }

        if (input is not null)
        {
            var inputPercent = VolumePercent(input.Volume);
            var inputSlider = UI.Slider(
                    input.Volume, 0, 1, VolumeStep,
                    "input.volume.set", "audio.input.volume.slider",
                    $"Microphone volume, {(input.IsMuted ? "muted" : "live")}. Press A to {(input.IsMuted ? "unmute" : "mute")}",
                    accessibilityValue: $"{inputPercent}%",
                    activationAction: "input.mute.toggle")
                .FocusUp(deviceRetryId ?? "audio.master.volume.slider")
                .Classes("audio-volume-slider", "audio-input-slider");
            inputSlider = inputSlider.FocusDown(
                firstControls?.VolumeSlider ?? "audio.retry");
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
        else if (IsRetryable(inputState))
        {
            supplemental.Add(RenderOptionalState(
                "input", "MICROPHONE", OptionalStateCopy(inputState, "microphone controls"),
                "input.retry", "Retry microphone", deviceRetryId ?? "audio.master.volume.slider",
                firstSessionFocusId ?? "audio.retry"));
        }
        else if (inputState is AudioOptionalSectionState.Loading or AudioOptionalSectionState.Empty)
        {
            supplemental.Add(RenderOptionalInfo(
                "input", "MICROPHONE", OptionalStateCopy(inputState, "microphone")));
        }

        if (sessions.Count == 0)
        {
            var retry = UI.Button("Check again", "retry", "audio.retry")
                .Icon(WidgetGlyph.Refresh, "Check for application audio")
                .FocusUp(inputFocusId ?? deviceRetryId ?? "audio.master.volume.slider")
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
            return new WidgetView(emptyRoot, InitialFocusId: initialFocusId, Surface: CompactSurface);
        }

        var sessionRows = new WidgetElement[sessions.Count];
        for (var index = 0; index < sessions.Count; index++)
        {
            var previous = index == 0 ? null : sessionControls[index - 1];
            var next = index + 1 == sessions.Count ? null : sessionControls[index + 1];
            sessionRows[index] = RenderSessionRow(
                sessions[index], sessionControls[index], previous, next,
                inputFocusId ?? deviceRetryId ?? "audio.master.volume.slider",
                pendingBySessionId.TryGetValue(sessions[index].SessionId, out var pending)
                    ? pending
                    : default);
        }

        var rootChildren = new List<WidgetElement> { header, masterCard };
        rootChildren.AddRange(supplemental);
        rootChildren.Add(UI.Row("audio.sessions.heading",
                    UI.Text("APPLICATIONS", "audio.sessions.label", "Application volume mixer")
                        .Classes("audio-sessions-label"),
                    UI.Text($"{sessions.Count} apps", "audio.sessions.count",
                            $"{sessions.Count} application audio sessions")
                        .Classes("audio-sessions-count"))
                    .Classes("audio-sessions-heading"));
        rootChildren.Add(UI.Stack("audio.sessions.list", sessionRows)
            .Classes("audio-session-list"));
        var root = UI.VerticalScroll("audio.root", rootChildren.ToArray())
            .InputScope("audio-mixer")
            .Classes("audio-mixer-widget", "has-sessions");

        return new WidgetView(root, InitialFocusId: initialFocusId, Surface: CompactSurface);
    }

    private static StackElement RenderSessionRow(
        WidgetAudioSession session,
        SessionControlIds controls,
        SessionControlIds? previous,
        SessionControlIds? next,
        string firstFocusUp,
        SessionPendingView pending)
    {
        var isPending = pending.Volume || pending.Mute;
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

        if (next is not null)
        {
            slider = slider.FocusDown(next.VolumeSlider);
        }

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

    private static RowElement DeviceRow(
        string suffix, WidgetGlyph glyph, string label, string displayName) =>
        UI.Row($"audio.devices.{suffix}",
                UI.Icon(glyph, $"audio.devices.{suffix}.icon", label)
                    .Classes("audio-device-icon"),
                UI.Stack($"audio.devices.{suffix}.copy",
                    UI.Text(label, $"audio.devices.{suffix}.label", label)
                        .Classes("audio-device-label"),
                    UI.Text(displayName, $"audio.devices.{suffix}.name", displayName)
                        .Classes("audio-device-name"))
                    .Classes("audio-device-copy"))
            .Classes("audio-device-row");

    private static StackElement RenderOptionalState(
        string suffix,
        string label,
        string copy,
        string action,
        string buttonLabel,
        string focusUp,
        string focusDown) =>
        UI.Stack($"audio.{suffix}.state.card",
                UI.Text(label, $"audio.{suffix}.state.label", label)
                    .Classes("audio-device-label"),
                UI.Text(copy, $"audio.{suffix}.state.help", copy)
                    .Classes("audio-help", "is-neutral"),
                UI.Button(buttonLabel, action, $"audio.{suffix}.retry")
                    .Icon(WidgetGlyph.Refresh, buttonLabel)
                    .FocusUp(focusUp)
                    .FocusDown(focusDown)
                    .Classes("audio-retry-action"))
            .Classes("audio-state-card", $"audio-{suffix}-state");

    private static StackElement RenderOptionalInfo(string suffix, string label, string copy) =>
        UI.Stack($"audio.{suffix}.state.card",
                UI.Text(label, $"audio.{suffix}.state.label", label)
                    .Classes("audio-device-label"),
                UI.Text(copy, $"audio.{suffix}.state.help", copy)
                    .Classes("audio-help", "is-neutral"))
            .Classes("audio-state-card", $"audio-{suffix}-state", "is-informational");

    private static bool IsRetryable(AudioOptionalSectionState state) => state is
        AudioOptionalSectionState.PermissionDenied or
        AudioOptionalSectionState.Revoked or
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

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationCount);
        StartActiveRun(activeLifetime);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (TryResolveSessionAction(action.ActionId, out var sessionId, out var sessionAction))
        {
            switch (sessionAction)
            {
                case SessionAction.SetVolume when action.RequestedValue is { } requestedVolume:
                    await SetVolumeAsync(sessionId, requestedVolume, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionAction.ToggleMute:
                    await ToggleMuteAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    break;
            }
            return;
        }

        switch (action.ActionId)
        {
            case "output.volume.set" when action.RequestedValue is { } requestedVolume:
                await SetOutputVolumeAsync(requestedVolume, cancellationToken).ConfigureAwait(false);
                break;
            case "output.mute.toggle":
                await ToggleOutputMuteAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "input.volume.set" when action.RequestedValue is { } requestedInputVolume:
                await SetInputVolumeAsync(requestedInputVolume, cancellationToken).ConfigureAwait(false);
                break;
            case "input.mute.toggle":
                await ToggleInputMuteAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "devices.retry":
                RetryOptionalSection(OptionalAudioSection.Devices);
                break;
            case "input.retry":
                RetryOptionalSection(OptionalAudioSection.Input);
                break;
            case "retry":
                if (IsActive) StartActiveRun(ActiveCancellationToken);
                break;
        }
    }

    public override ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        RememberFocusedControl(input);
        return base.OnControllerInputAsync(input, cancellationToken);
    }

    private void RememberFocusedControl(ControllerInputEvent input)
    {
        if (input.Context != ControllerInputContext.OpenWidget ||
            !string.Equals(input.ActiveInputScopeId, "audio-mixer", StringComparison.Ordinal) ||
            input.FocusedElementId is not { } focusedId)
            return;

        lock (_stateLock)
        {
            if (focusedId == "audio.master.volume.slider")
            {
                _preferredFocusTarget = PreferredFocusTarget.MasterOutput;
                return;
            }
            if (focusedId == "audio.input.volume.slider" && _input is not null)
            {
                _preferredFocusTarget = PreferredFocusTarget.Microphone;
                return;
            }
            foreach (var pair in _sessionControls)
            {
                if (!string.Equals(pair.Value.VolumeSlider, focusedId, StringComparison.Ordinal))
                    continue;
                SelectSessionLocked(pair.Key);
                return;
            }
        }
    }

    private WidgetView RenderNonSessionState(StackElement header, AudioMixerViewState state)
    {
        var (title, help, buttonLabel, error) = state switch
        {
            AudioMixerViewState.Initial => (
                "Ready when you are",
                "Open the widget to load application audio sessions.",
                "Load audio sessions",
                false),
            AudioMixerViewState.Loading => (
                "Finding application audio",
                "The host audio provider is loading one current snapshot.",
                "Loading…",
                false),
            AudioMixerViewState.Empty => (
                "No application audio yet",
                "Start playback in an application. New sessions appear here automatically.",
                "Check again",
                false),
            AudioMixerViewState.PermissionDenied => (
                "Audio access is off",
                "Grant read access in Settings → Permissions, then try again.",
                "Try again",
                true),
            AudioMixerViewState.LifecycleDenied => (
                "Audio paused by lifecycle",
                "Return this widget to the foreground before requesting audio sessions.",
                "Try again",
                true),
            AudioMixerViewState.ChannelClosed => (
                "Audio service disconnected",
                "The protected host channel closed. Reopen or retry the widget.",
                "Reconnect",
                true),
            AudioMixerViewState.ServiceUnavailable => (
                "Audio service unavailable",
                "This worker was not given the host audio service.",
                "Try again",
                true),
            _ => (
                "Audio could not be loaded",
                "The provider returned an unexpected error. No system details were exposed.",
                "Try again",
                true),
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
                    retry)
                    .Classes("audio-state-card"))
            .InputScope("audio-mixer")
            .Classes("audio-mixer-widget", error ? "has-error" : "has-state");
        return new WidgetView(root, InitialFocusId: "audio.retry", Surface: CompactSurface);
    }

    private void StartActiveRun(CancellationToken activeLifetime)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long generation;
        lock (_stateLock)
        {
            previous = _runLifetime;
            current = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
            _runLifetime = current;
            generation = ++_runGeneration;
            _viewState = AudioMixerViewState.Loading;
            _status = "Loading audio sessions…";
            _statusIsError = false;
            // Optional data is capability-scoped and may have been revoked
            // while the worker was backgrounded. Never display a prior
            // lifecycle generation while the fresh grants are being checked.
            _devices = [];
            _input = null;
            _deviceState = AudioOptionalSectionState.Loading;
            _inputState = AudioOptionalSectionState.Loading;
        }
        DrainSignal(_deviceRetrySignal);
        DrainSignal(_inputRetrySignal);
        previous?.Cancel();
        previous?.Dispose();
        Invalidate();
        _ = ObserveAudioAsync(generation, current.Token);
    }

    private void StopActiveRun()
    {
        CancellationTokenSource? lifetime;
        lock (_stateLock)
        {
            ++_runGeneration;
            lifetime = _runLifetime;
            _runLifetime = null;
            if (_sessions.Count != 0)
            {
                var restored = _sessions.ToArray();
                for (var index = 0; index < restored.Length; index++)
                {
                    if (!_sessionPending.TryGetValue(restored[index].SessionId, out var pending) ||
                        !pending.HasAuthoritative)
                        continue;
                    restored[index] = restored[index] with
                    {
                        Volume = pending.AuthoritativeVolume,
                        IsMuted = pending.AuthoritativeMuted,
                    };
                }
                _sessions = restored;
            }
            if (_output is { } output && _outputPending.HasAuthoritative)
                _output = output with
                {
                    Volume = _outputPending.AuthoritativeVolume,
                    IsMuted = _outputPending.AuthoritativeMuted,
                };
            if (_input is { } input && _inputPending.HasAuthoritative)
                _input = input with
                {
                    Volume = _inputPending.AuthoritativeVolume,
                    IsMuted = _inputPending.AuthoritativeMuted,
                };
            _sessionPending.Clear();
            _outputPending.Reset();
            _inputPending.Reset();
        }
        lifetime?.Cancel();
        lifetime?.Dispose();
    }

    private async Task ObserveAudioAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            // Establish and acknowledge the coalesced event buffer before the
            // current snapshot request. A full event received during the GET is
            // applied afterward, closing the classic subscribe-after-fetch gap.
            await using var sessionSubscription = await HostServices.Audio
                .OpenSessionsSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            await using var outputSubscription = await HostServices.Audio
                .OpenOutputSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _fetchCount);
            var output = await HostServices.Audio.GetOutputAsync(cancellationToken).ConfigureAwait(false);
            var sessions = await HostServices.Audio.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplyOutput(output, generation);
            ApplySessions(sessions, generation);

            using var observers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sessionObserver = ObserveSessionChangesAsync(
                sessionSubscription, generation, observers.Token);
            var outputObserver = ObserveOutputChangesAsync(
                outputSubscription, generation, observers.Token);
            var auxiliaryObservers = new[]
            {
                ObserveOptionalDevicesAsync(generation, observers.Token),
                ObserveOptionalInputAsync(generation, observers.Token),
            };
            await Task.WhenAny(sessionObserver, outputObserver).ConfigureAwait(false);
            observers.Cancel();
            try
            {
                await Task.WhenAll(auxiliaryObservers.Append(sessionObserver).Append(outputObserver))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (observers.IsCancellationRequested) { }

            if (IsCurrentRun(generation, cancellationToken))
                SetProviderError(AudioMixerViewState.ChannelClosed,
                    "Audio service channel closed", generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leaving Visible/Interactive or replacing a retry run is normal.
        }
        catch (WidgetCapabilityUnavailableException)
        {
            SetProviderError(AudioMixerViewState.ServiceUnavailable,
                "Host audio service unavailable", generation);
        }
        catch (WidgetCapabilityException exception)
        {
            var (state, status) = MapCapabilityFailure(exception.ErrorCode);
            SetProviderError(state, status, generation);
        }
        catch (Exception)
        {
            SetProviderError(AudioMixerViewState.Error,
                "Audio provider returned an unexpected error", generation);
        }
    }

    private async Task ObserveOptionalDevicesAsync(long generation, CancellationToken cancellationToken)
    {
        while (IsCurrentRun(generation, cancellationToken))
        {
            SetOptionalLoading(OptionalAudioSection.Devices, generation);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetOptionalAttempt(OptionalAudioSection.Devices, attempt, generation);
            try
            {
                await using var subscription = await HostServices.Audio
                    .OpenDevicesSubscriptionAsync(attempt.Token).ConfigureAwait(false);
                ApplyDevices(await HostServices.Audio.GetDevicesAsync(attempt.Token)
                    .ConfigureAwait(false), generation);
                await ObserveDeviceChangesAsync(subscription, generation, attempt.Token)
                    .ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                    SetOptionalFailure(OptionalAudioSection.Devices,
                        AudioOptionalSectionState.Unavailable, generation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                // Explicit section retry replaces only this attempt.
            }
            catch (Exception exception)
            {
                SetOptionalFailure(OptionalAudioSection.Devices,
                    MapOptionalFailure(exception), generation);
            }
            finally
            {
                ClearOptionalAttempt(OptionalAudioSection.Devices, attempt, generation);
            }
            await _deviceRetrySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ObserveOptionalInputAsync(long generation, CancellationToken cancellationToken)
    {
        while (IsCurrentRun(generation, cancellationToken))
        {
            SetOptionalLoading(OptionalAudioSection.Input, generation);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetOptionalAttempt(OptionalAudioSection.Input, attempt, generation);
            try
            {
                await using var subscription = await HostServices.Audio
                    .OpenInputSubscriptionAsync(attempt.Token).ConfigureAwait(false);
                ApplyInput(await HostServices.Audio.GetInputAsync(attempt.Token)
                    .ConfigureAwait(false), generation);
                await ObserveInputChangesAsync(subscription, generation, attempt.Token)
                    .ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                    SetOptionalFailure(OptionalAudioSection.Input,
                        AudioOptionalSectionState.Unavailable, generation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                // Explicit section retry replaces only this attempt.
            }
            catch (Exception exception)
            {
                SetOptionalFailure(OptionalAudioSection.Input,
                    MapOptionalFailure(exception), generation);
            }
            finally
            {
                ClearOptionalAttempt(OptionalAudioSection.Input, attempt, generation);
            }
            await _inputRetrySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ObserveSessionChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioSessionsChanged> subscription,
        long generation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, cancellationToken)) return;
            if (!change.IsAvailable)
            {
                SetProviderError(AudioMixerViewState.ServiceUnavailable,
                    "Windows audio session provider unavailable", generation);
                continue;
            }
            ApplySessions(change.Sessions, generation);
        }
    }

    private async Task ObserveOutputChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioOutputChanged> subscription,
        long generation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, cancellationToken)) return;
            if (!change.IsAvailable || change.Output is null)
            {
                SetProviderError(AudioMixerViewState.ServiceUnavailable,
                    "Windows master output unavailable", generation);
                continue;
            }
            ApplyOutput(change.Output, generation);
        }
    }

    private async Task ObserveDeviceChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioDevicesChanged> subscription,
        long generation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, cancellationToken)) return;
            if (!change.IsAvailable)
                SetOptionalFailure(OptionalAudioSection.Devices,
                    AudioOptionalSectionState.Unavailable, generation);
            else
                ApplyDevices(change.Devices, generation);
        }
    }

    private async Task ObserveInputChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioInputChanged> subscription,
        long generation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, cancellationToken)) return;
            if (!change.IsAvailable)
                SetOptionalFailure(OptionalAudioSection.Input,
                    AudioOptionalSectionState.Unavailable, generation);
            else if (change.Input is null)
                ClearInput(generation, AudioOptionalSectionState.Empty);
            else
                ApplyInput(change.Input, generation);
        }
    }

    private void ApplyOutput(WidgetAudioOutput incoming, long generation)
    {
        var normalized = new WidgetAudioOutput(
            double.IsFinite(incoming.Volume) ? Math.Clamp(incoming.Volume, 0, 1) : 0,
            incoming.IsMuted);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _outputPending.AuthoritativeVolume = normalized.Volume;
            _outputPending.AuthoritativeMuted = normalized.IsMuted;
            _outputPending.HasAuthoritative = true;
            if (_outputPending.VolumeTarget is { } targetVolume)
            {
                if (VolumesMatch(normalized.Volume, targetVolume))
                    _outputPending.ConfirmVolumeTarget();
                else
                    normalized = normalized with { Volume = targetVolume };
            }
            if (_outputPending.MuteTarget is { } targetMuted)
            {
                if (normalized.IsMuted == targetMuted)
                    _outputPending.ConfirmMuteTarget();
                else
                    normalized = normalized with { IsMuted = targetMuted };
            }
            _output = normalized;
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void ApplyDevices(IReadOnlyList<WidgetAudioDevice>? incoming, long generation)
    {
        var devices = (incoming ?? [])
            .Where(device => device is not null && !string.IsNullOrWhiteSpace(device.DeviceId) &&
                Enum.IsDefined(device.Direction))
            .DistinctBy(device => device.DeviceId, StringComparer.Ordinal)
            .Select(device => device with { DisplayName = NormalizeDisplayName(device.DisplayName) })
            .Take(128)
            .ToArray();
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _devices = devices;
            _deviceState = devices.Length == 0
                ? AudioOptionalSectionState.Empty
                : AudioOptionalSectionState.Healthy;
        }
        Invalidate();
    }

    private void ClearDevices(long generation, AudioOptionalSectionState state)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _devices = [];
            _deviceState = state;
        }
        Invalidate();
    }

    private void ApplyInput(WidgetAudioInput incoming, long generation)
    {
        var normalized = new WidgetAudioInput(
            double.IsFinite(incoming.Volume) ? Math.Clamp(incoming.Volume, 0, 1) : 0,
            incoming.IsMuted);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _inputPending.AuthoritativeVolume = normalized.Volume;
            _inputPending.AuthoritativeMuted = normalized.IsMuted;
            _inputPending.HasAuthoritative = true;
            if (_inputPending.VolumeTarget is { } volume)
            {
                if (VolumesMatch(normalized.Volume, volume)) _inputPending.ConfirmVolumeTarget();
                else normalized = normalized with { Volume = volume };
            }
            if (_inputPending.MuteTarget is { } muted)
            {
                if (normalized.IsMuted == muted) _inputPending.ConfirmMuteTarget();
                else normalized = normalized with { IsMuted = muted };
            }
            _input = normalized;
            _inputState = AudioOptionalSectionState.Healthy;
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void ClearInput(long generation, AudioOptionalSectionState state)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _input = null;
            _inputState = state;
            _inputPending.Reset();
        }
        Invalidate();
    }

    private void SetOptionalLoading(OptionalAudioSection section, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            if (section == OptionalAudioSection.Devices)
                _deviceState = AudioOptionalSectionState.Loading;
            else
                _inputState = AudioOptionalSectionState.Loading;
        }
        Invalidate();
    }

    private void SetOptionalFailure(
        OptionalAudioSection section,
        AudioOptionalSectionState state,
        long generation)
    {
        if (!IsRetryable(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (section == OptionalAudioSection.Devices)
            ClearDevices(generation, state);
        else
            ClearInput(generation, state);
    }

    private void RetryOptionalSection(OptionalAudioSection section)
    {
        SemaphoreSlim signal;
        CancellationTokenSource? attempt;
        lock (_stateLock)
        {
            if (_runLifetime is null) return;
            if (section == OptionalAudioSection.Devices)
            {
                if (!IsRetryable(_deviceState)) return;
                _deviceState = AudioOptionalSectionState.Loading;
                signal = _deviceRetrySignal;
                attempt = _deviceAttemptLifetime;
            }
            else
            {
                if (!IsRetryable(_inputState)) return;
                _inputState = AudioOptionalSectionState.Loading;
                signal = _inputRetrySignal;
                attempt = _inputAttemptLifetime;
            }
        }
        try { signal.Release(); }
        catch (SemaphoreFullException) { }
        attempt?.Cancel();
        Invalidate();
    }

    private void SetOptionalAttempt(
        OptionalAudioSection section,
        CancellationTokenSource attempt,
        long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            if (section == OptionalAudioSection.Devices)
                _deviceAttemptLifetime = attempt;
            else
                _inputAttemptLifetime = attempt;
        }
    }

    private void ClearOptionalAttempt(
        OptionalAudioSection section,
        CancellationTokenSource attempt,
        long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            if (section == OptionalAudioSection.Devices &&
                ReferenceEquals(_deviceAttemptLifetime, attempt))
                _deviceAttemptLifetime = null;
            else if (section == OptionalAudioSection.Input &&
                     ReferenceEquals(_inputAttemptLifetime, attempt))
                _inputAttemptLifetime = null;
        }
    }

    private static AudioOptionalSectionState MapOptionalFailure(Exception exception)
    {
        if (exception is not WidgetCapabilityException capability)
            return AudioOptionalSectionState.Unavailable;
        return capability.ErrorCode switch
        {
            "permission_denied" => AudioOptionalSectionState.PermissionDenied,
            "capability_revoked" => AudioOptionalSectionState.Revoked,
            _ => AudioOptionalSectionState.Unavailable,
        };
    }

    private static void DrainSignal(SemaphoreSlim signal)
    {
        while (signal.Wait(0)) { }
    }

    private void ApplySessions(IReadOnlyList<WidgetAudioSession>? incoming, long generation)
    {
        var normalized = NormalizeSessions(incoming);
        var (controls, actions) = BuildSessionRouting(normalized);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            var previousId = _selectedSessionId;
            var previousIndex = _selectedIndex;

            var presentIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < normalized.Count; index++)
            {
                var session = normalized[index];
                presentIds.Add(session.SessionId);
                if (!_sessionPending.TryGetValue(session.SessionId, out var pending))
                {
                    pending = new SessionPendingState();
                    _sessionPending.Add(session.SessionId, pending);
                }
                pending.AuthoritativeVolume = session.Volume;
                pending.AuthoritativeMuted = session.IsMuted;
                pending.HasAuthoritative = true;
                if (pending.VolumeTarget is { } targetVolume)
                {
                    if (VolumesMatch(session.Volume, targetVolume)) pending.ConfirmVolumeTarget();
                    else session = session with { Volume = targetVolume };
                }
                if (pending.MuteTarget is { } targetMuted)
                {
                    if (session.IsMuted == targetMuted) pending.ConfirmMuteTarget();
                    else session = session with { IsMuted = targetMuted };
                }
                normalized[index] = session;
            }

            foreach (var staleId in _sessionPending.Keys
                         .Where(id => !presentIds.Contains(id) && !_sessionPending[id].IsSending)
                         .ToArray())
                _sessionPending.Remove(staleId);

            _sessions = normalized;
            _sessionControls = controls;
            _sessionActions = actions;
            if (normalized.Count == 0)
            {
                _selectedSessionId = null;
                _selectedIndex = 0;
            }
            else
            {
                var retained = previousId is null
                    ? -1
                    : normalized.FindIndex(session =>
                        string.Equals(session.SessionId, previousId, StringComparison.Ordinal));
                _selectedIndex = retained >= 0
                    ? retained
                    : Math.Clamp(previousIndex, 0, normalized.Count - 1);
                _selectedSessionId = normalized[_selectedIndex].SessionId;
            }
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void UpdateHealthyStateLocked()
    {
        if (_output is null) return;
        _viewState = _sessions.Count == 0 ? AudioMixerViewState.Empty : AudioMixerViewState.Ready;
        if (!_sessionPending.Values.Any(state => state.VolumeIsPending || state.MuteIsPending) &&
            !_outputPending.VolumeIsPending && !_outputPending.MuteIsPending &&
            !_inputPending.VolumeIsPending && !_inputPending.MuteIsPending)
            _status = _sessions.Count switch
            {
                0 => "Master output live · listening for application audio",
                1 => "Master output · 1 audio session · live updates",
                _ => $"Master output · {_sessions.Count} audio sessions · live updates",
            };
        _statusIsError = false;
    }

    private static List<WidgetAudioSession> NormalizeSessions(
        IReadOnlyList<WidgetAudioSession>? sessions)
    {
        if (sessions is null) return [];
        var result = new List<WidgetAudioSession>(sessions.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            if (session is null || string.IsNullOrWhiteSpace(session.SessionId) ||
                !ids.Add(session.SessionId))
                continue;
            var volume = double.IsFinite(session.Volume) ? Math.Clamp(session.Volume, 0, 1) : 0;
            result.Add(session with
            {
                DisplayName = NormalizeDisplayName(session.DisplayName),
                Volume = volume,
            });
        }
        return result;
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Unnamed application";
        var trimmed = displayName.Trim();
        const int maximumRunes = 160;
        var builder = new StringBuilder(Math.Min(trimmed.Length, maximumRunes + 1));
        var count = 0;
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (count == maximumRunes)
            {
                builder.Append('…');
                break;
            }
            builder.Append(rune.ToString());
            count++;
        }
        return builder.ToString();
    }

    private static (
        IReadOnlyDictionary<string, SessionControlIds> Controls,
        IReadOnlyDictionary<string, SessionActionTarget> Actions)
        BuildSessionRouting(IReadOnlyList<WidgetAudioSession> sessions)
    {
        var controls = new Dictionary<string, SessionControlIds>(sessions.Count, StringComparer.Ordinal);
        var actions = new Dictionary<string, SessionActionTarget>(sessions.Count * 3, StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            var ids = SessionControlIds.For(session);
            controls.Add(session.SessionId, ids);
            actions.Add(ids.VolumeSet, new SessionActionTarget(session.SessionId, SessionAction.SetVolume));
            actions.Add(ids.Mute, new SessionActionTarget(session.SessionId, SessionAction.ToggleMute));
        }
        return (controls, actions);
    }

    private ValueTask SetVolumeAsync(
        string sessionId,
        double requestedVolume,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SliderMath.IsValidRequestedValue(requestedVolume, 0, 1, VolumeStep))
            return ValueTask.CompletedTask;
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            var session = FindSessionLocked(sessionId);
            if (session is null) return ValueTask.CompletedTask;
            SelectSessionLocked(session.SessionId);
            var desired = RoundVolume(requestedVolume);
            if (VolumesMatch(desired, session.Volume)) return ValueTask.CompletedTask;
            var pending = GetSessionPendingLocked(session);
            pending.VolumeTarget = desired;
            pending.VolumeRevision++;
            pending.VolumeAwaitingConfirmation = false;
            if (!pending.VolumeWorkerRunning)
            {
                pending.VolumeWorkerRunning = true;
                startWorker = true;
            }
            ReplaceSessionLocked(session with { Volume = desired });
            _status = $"Setting {session.DisplayName} to {VolumePercent(desired)}%…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) _ = RunSessionVolumeQueueAsync(sessionId, generation, runToken);
        return ValueTask.CompletedTask;
    }

    private ValueTask ToggleMuteAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            var session = FindSessionLocked(sessionId);
            if (session is null) return ValueTask.CompletedTask;
            SelectSessionLocked(session.SessionId);
            var desired = !session.IsMuted;
            var pending = GetSessionPendingLocked(session);
            pending.MuteTarget = desired;
            pending.MuteRevision++;
            pending.MuteAwaitingConfirmation = false;
            if (!pending.MuteWorkerRunning)
            {
                pending.MuteWorkerRunning = true;
                startWorker = true;
            }
            ReplaceSessionLocked(session with { IsMuted = desired });
            _status = desired ? $"Muting {session.DisplayName}…" : $"Unmuting {session.DisplayName}…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) _ = RunSessionMuteQueueAsync(sessionId, generation, runToken);
        return ValueTask.CompletedTask;
    }

    private ValueTask SetOutputVolumeAsync(double requestedVolume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SliderMath.IsValidRequestedValue(requestedVolume, 0, 1, VolumeStep))
            return ValueTask.CompletedTask;
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            if (_output is not { } output) return ValueTask.CompletedTask;
            _preferredFocusTarget = PreferredFocusTarget.MasterOutput;
            var desired = RoundVolume(requestedVolume);
            if (VolumesMatch(desired, output.Volume)) return ValueTask.CompletedTask;
            _outputPending.VolumeTarget = desired;
            _outputPending.VolumeRevision++;
            _outputPending.VolumeAwaitingConfirmation = false;
            if (!_outputPending.VolumeWorkerRunning)
            {
                _outputPending.VolumeWorkerRunning = true;
                startWorker = true;
            }
            _output = output with { Volume = desired };
            _status = $"Setting master output to {VolumePercent(desired)}%…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) _ = RunOutputVolumeQueueAsync(generation, runToken);
        return ValueTask.CompletedTask;
    }

    private ValueTask ToggleOutputMuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            if (_output is not { } output) return ValueTask.CompletedTask;
            _preferredFocusTarget = PreferredFocusTarget.MasterOutput;
            var desired = !output.IsMuted;
            _outputPending.MuteTarget = desired;
            _outputPending.MuteRevision++;
            _outputPending.MuteAwaitingConfirmation = false;
            if (!_outputPending.MuteWorkerRunning)
            {
                _outputPending.MuteWorkerRunning = true;
                startWorker = true;
            }
            _output = output with { IsMuted = desired };
            _status = desired ? "Muting master output…" : "Unmuting master output…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) _ = RunOutputMuteQueueAsync(generation, runToken);
        return ValueTask.CompletedTask;
    }

    private ValueTask SetInputVolumeAsync(double requestedVolume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SliderMath.IsValidRequestedValue(requestedVolume, 0, 1, VolumeStep))
            return ValueTask.CompletedTask;
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            if (_input is not { } input) return ValueTask.CompletedTask;
            _preferredFocusTarget = PreferredFocusTarget.Microphone;
            var desired = RoundVolume(requestedVolume);
            if (VolumesMatch(desired, input.Volume)) return ValueTask.CompletedTask;
            _inputPending.VolumeTarget = desired;
            _inputPending.VolumeRevision++;
            _inputPending.VolumeAwaitingConfirmation = false;
            if (!_inputPending.VolumeWorkerRunning)
            {
                _inputPending.VolumeWorkerRunning = true;
                startWorker = true;
            }
            _input = input with { Volume = desired };
            _status = $"Setting microphone to {VolumePercent(desired)}%…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) _ = RunInputVolumeQueueAsync(generation, runToken);
        return ValueTask.CompletedTask;
    }

    private ValueTask ToggleInputMuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            if (_input is not { } input) return ValueTask.CompletedTask;
            _preferredFocusTarget = PreferredFocusTarget.Microphone;
            var desired = !input.IsMuted;
            _inputPending.MuteTarget = desired;
            _inputPending.MuteRevision++;
            _inputPending.MuteAwaitingConfirmation = false;
            if (!_inputPending.MuteWorkerRunning)
            {
                _inputPending.MuteWorkerRunning = true;
                startWorker = true;
            }
            _input = input with { IsMuted = desired };
            _status = desired ? "Muting microphone…" : "Unmuting microphone…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) _ = RunInputMuteQueueAsync(generation, runToken);
        return ValueTask.CompletedTask;
    }

    private async Task RunSessionVolumeQueueAsync(
        string sessionId, long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            double target;
            long revision;
            lock (_stateLock)
            {
                _sessionPending.TryGetValue(sessionId, out var pending);
                if (_runGeneration != generation || pending?.VolumeTarget is not { } pendingTarget)
                {
                    if (pending is not null) pending.FinishVolumeWorker();
                    return;
                }
                target = pendingTarget;
                revision = pending.VolumeRevision;
            }
            try
            {
                await HostServices.Audio.SetSessionVolumeAsync(sessionId, target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelSessionVolumeWorker(sessionId, generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackSessionVolume(sessionId, generation, revision,
                        "Host audio control service unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackSessionVolume(sessionId, generation, revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackSessionVolume(sessionId, generation, revision,
                        "Volume change failed · previous value restored")) return;
                continue;
            }

            var reconcile = false;
            lock (_stateLock)
            {
                if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending))
                    return;
                if (pending.VolumeTarget is null)
                {
                    pending.FinishVolumeWorker();
                    return;
                }
                if (pending.VolumeRevision != revision) continue;
                pending.FinishVolumeWorker();
                pending.VolumeAwaitingConfirmation = true;
                _status = $"{FindSessionLocked(sessionId)?.DisplayName ?? "Application"} volume {VolumePercent(target)}%";
                _statusIsError = false;
                reconcile = true;
            }
            Invalidate();
            if (reconcile) _ = ConfirmSessionVolumeAsync(sessionId, generation, revision, target, cancellationToken);
            return;
        }
    }

    private async Task RunSessionMuteQueueAsync(
        string sessionId, long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            bool target;
            long revision;
            lock (_stateLock)
            {
                _sessionPending.TryGetValue(sessionId, out var pending);
                if (_runGeneration != generation || pending?.MuteTarget is not { } pendingTarget)
                {
                    if (pending is not null) pending.FinishMuteWorker();
                    return;
                }
                target = pendingTarget;
                revision = pending.MuteRevision;
            }
            try
            {
                await HostServices.Audio.SetSessionMutedAsync(sessionId, target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelSessionMuteWorker(sessionId, generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackSessionMute(sessionId, generation, revision,
                        "Host audio control service unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackSessionMute(sessionId, generation, revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackSessionMute(sessionId, generation, revision,
                        "Mute change failed · previous state restored")) return;
                continue;
            }

            var reconcile = false;
            lock (_stateLock)
            {
                if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending))
                    return;
                if (pending.MuteTarget is null)
                {
                    pending.FinishMuteWorker();
                    return;
                }
                if (pending.MuteRevision != revision) continue;
                pending.FinishMuteWorker();
                pending.MuteAwaitingConfirmation = true;
                _status = target
                    ? $"{FindSessionLocked(sessionId)?.DisplayName ?? "Application"} muted"
                    : $"{FindSessionLocked(sessionId)?.DisplayName ?? "Application"} unmuted";
                _statusIsError = false;
                reconcile = true;
            }
            Invalidate();
            if (reconcile) _ = ConfirmSessionMuteAsync(sessionId, generation, revision, target, cancellationToken);
            return;
        }
    }

    private async Task RunOutputVolumeQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            double target;
            long revision;
            lock (_stateLock)
            {
                if (_runGeneration != generation || _outputPending.VolumeTarget is not { } pendingTarget)
                {
                    _outputPending.FinishVolumeWorker();
                    return;
                }
                target = pendingTarget;
                revision = _outputPending.VolumeRevision;
            }
            try
            {
                await HostServices.Audio.SetOutputVolumeAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelOutputVolumeWorker(generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackOutputVolume(generation, revision,
                        "Host master-output control unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackOutputVolume(generation, revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackOutputVolume(generation, revision,
                        "Master volume change failed · previous value restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                if (_outputPending.VolumeTarget is null)
                {
                    _outputPending.FinishVolumeWorker();
                    return;
                }
                if (_outputPending.VolumeRevision != revision) continue;
                _outputPending.FinishVolumeWorker();
                _outputPending.VolumeAwaitingConfirmation = true;
                _status = $"Master output volume {VolumePercent(target)}%";
                _statusIsError = false;
            }
            Invalidate();
            _ = ConfirmOutputVolumeAsync(generation, revision, target, cancellationToken);
            return;
        }
    }

    private async Task RunOutputMuteQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            bool target;
            long revision;
            lock (_stateLock)
            {
                if (_runGeneration != generation || _outputPending.MuteTarget is not { } pendingTarget)
                {
                    _outputPending.FinishMuteWorker();
                    return;
                }
                target = pendingTarget;
                revision = _outputPending.MuteRevision;
            }
            try
            {
                await HostServices.Audio.SetOutputMutedAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelOutputMuteWorker(generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackOutputMute(generation, revision,
                        "Host master-output control unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackOutputMute(generation, revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackOutputMute(generation, revision,
                        "Master mute change failed · previous state restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                if (_outputPending.MuteTarget is null)
                {
                    _outputPending.FinishMuteWorker();
                    return;
                }
                if (_outputPending.MuteRevision != revision) continue;
                _outputPending.FinishMuteWorker();
                _outputPending.MuteAwaitingConfirmation = true;
                _status = target ? "Master output muted" : "Master output unmuted";
                _statusIsError = false;
            }
            Invalidate();
            _ = ConfirmOutputMuteAsync(generation, revision, target, cancellationToken);
            return;
        }
    }

    private async Task RunInputVolumeQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            double target;
            long revision;
            lock (_stateLock)
            {
                if (_runGeneration != generation || _inputPending.VolumeTarget is not { } pendingTarget)
                {
                    _inputPending.FinishVolumeWorker();
                    return;
                }
                target = pendingTarget;
                revision = _inputPending.VolumeRevision;
            }
            try
            {
                await HostServices.Audio.SetInputVolumeAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelInputVolumeWorker(generation);
                return;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackInputVolume(generation, revision, MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackInputVolume(generation, revision,
                        "Microphone level change failed · previous value restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                if (_inputPending.VolumeTarget is null)
                {
                    _inputPending.FinishVolumeWorker();
                    return;
                }
                if (_inputPending.VolumeRevision != revision) continue;
                _inputPending.FinishVolumeWorker();
                _inputPending.VolumeAwaitingConfirmation = true;
                _status = $"Microphone level {VolumePercent(target)}%";
                _statusIsError = false;
            }
            Invalidate();
            _ = ConfirmInputVolumeAsync(generation, revision, target, cancellationToken);
            return;
        }
    }

    private async Task RunInputMuteQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            bool target;
            long revision;
            lock (_stateLock)
            {
                if (_runGeneration != generation || _inputPending.MuteTarget is not { } pendingTarget)
                {
                    _inputPending.FinishMuteWorker();
                    return;
                }
                target = pendingTarget;
                revision = _inputPending.MuteRevision;
            }
            try
            {
                await HostServices.Audio.SetInputMutedAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelInputMuteWorker(generation);
                return;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackInputMute(generation, revision, MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackInputMute(generation, revision,
                        "Microphone mute change failed · previous state restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                if (_inputPending.MuteTarget is null)
                {
                    _inputPending.FinishMuteWorker();
                    return;
                }
                if (_inputPending.MuteRevision != revision) continue;
                _inputPending.FinishMuteWorker();
                _inputPending.MuteAwaitingConfirmation = true;
                _status = target ? "Microphone muted" : "Microphone live";
                _statusIsError = false;
            }
            Invalidate();
            _ = ConfirmInputMuteAsync(generation, revision, target, cancellationToken);
            return;
        }
    }

    private async Task ConfirmSessionVolumeAsync(
        string sessionId,
        long generation,
        long revision,
        double target,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ConfirmationDelay, cancellationToken).ConfigureAwait(false);
            var sessions = NormalizeSessions(
                await HostServices.Audio.GetSessionsAsync(cancellationToken).ConfigureAwait(false));
            var authoritative = sessions.FirstOrDefault(session =>
                string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));
            lock (_stateLock)
            {
                if (_runGeneration != generation ||
                    !_sessionPending.TryGetValue(sessionId, out var pending) ||
                    pending.VolumeRevision != revision || pending.VolumeTarget is null)
                    return;
                var current = FindSessionLocked(sessionId);
                if (authoritative is null || current is null)
                {
                    pending.ConfirmVolumeTarget();
                    _status = "Application audio session ended before its volume could be confirmed";
                    _statusIsError = true;
                }
                else
                {
                    pending.AuthoritativeVolume = authoritative.Volume;
                    pending.ConfirmVolumeTarget();
                    ReplaceSessionLocked(current with { Volume = authoritative.Volume });
                    var matched = VolumesMatch(authoritative.Volume, target);
                    _status = matched
                        ? $"{current.DisplayName} volume {VolumePercent(authoritative.Volume)}%"
                        : $"{current.DisplayName} volume was not applied · current value restored";
                    _statusIsError = !matched;
                }
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            TryRollbackSessionVolume(sessionId, generation, revision,
                "Volume confirmation failed · current value restored");
        }
    }

    private async Task ConfirmSessionMuteAsync(
        string sessionId,
        long generation,
        long revision,
        bool target,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ConfirmationDelay, cancellationToken).ConfigureAwait(false);
            var sessions = NormalizeSessions(
                await HostServices.Audio.GetSessionsAsync(cancellationToken).ConfigureAwait(false));
            var authoritative = sessions.FirstOrDefault(session =>
                string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));
            lock (_stateLock)
            {
                if (_runGeneration != generation ||
                    !_sessionPending.TryGetValue(sessionId, out var pending) ||
                    pending.MuteRevision != revision || pending.MuteTarget is null)
                    return;
                var current = FindSessionLocked(sessionId);
                if (authoritative is null || current is null)
                {
                    pending.ConfirmMuteTarget();
                    _status = "Application audio session ended before mute could be confirmed";
                    _statusIsError = true;
                }
                else
                {
                    pending.AuthoritativeMuted = authoritative.IsMuted;
                    pending.ConfirmMuteTarget();
                    ReplaceSessionLocked(current with { IsMuted = authoritative.IsMuted });
                    var matched = authoritative.IsMuted == target;
                    _status = matched
                        ? target ? $"{current.DisplayName} muted" : $"{current.DisplayName} unmuted"
                        : $"{current.DisplayName} mute was not applied · current state restored";
                    _statusIsError = !matched;
                }
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            TryRollbackSessionMute(sessionId, generation, revision,
                "Mute confirmation failed · current state restored");
        }
    }

    private async Task ConfirmOutputVolumeAsync(
        long generation, long revision, double target, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ConfirmationDelay, cancellationToken).ConfigureAwait(false);
            var authoritative = await HostServices.Audio.GetOutputAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (_runGeneration != generation || _outputPending.VolumeRevision != revision ||
                    _outputPending.VolumeTarget is null)
                    return;
                var normalized = double.IsFinite(authoritative.Volume)
                    ? Math.Clamp(authoritative.Volume, 0, 1)
                    : 0;
                _outputPending.AuthoritativeVolume = normalized;
                _outputPending.ConfirmVolumeTarget();
                if (_output is { } current) _output = current with { Volume = normalized };
                var matched = VolumesMatch(normalized, target);
                _status = matched
                    ? $"Master output volume {VolumePercent(normalized)}%"
                    : "Master volume was not applied · current value restored";
                _statusIsError = !matched;
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            TryRollbackOutputVolume(generation, revision,
                "Master volume confirmation failed · current value restored");
        }
    }

    private async Task ConfirmOutputMuteAsync(
        long generation, long revision, bool target, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ConfirmationDelay, cancellationToken).ConfigureAwait(false);
            var authoritative = await HostServices.Audio.GetOutputAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (_runGeneration != generation || _outputPending.MuteRevision != revision ||
                    _outputPending.MuteTarget is null)
                    return;
                _outputPending.AuthoritativeMuted = authoritative.IsMuted;
                _outputPending.ConfirmMuteTarget();
                if (_output is { } current) _output = current with { IsMuted = authoritative.IsMuted };
                var matched = authoritative.IsMuted == target;
                _status = matched
                    ? target ? "Master output muted" : "Master output unmuted"
                    : "Master mute was not applied · current state restored";
                _statusIsError = !matched;
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            TryRollbackOutputMute(generation, revision,
                "Master mute confirmation failed · current state restored");
        }
    }

    private async Task ConfirmInputVolumeAsync(
        long generation, long revision, double target, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ConfirmationDelay, cancellationToken).ConfigureAwait(false);
            var authoritative = await HostServices.Audio.GetInputAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (_runGeneration != generation || _inputPending.VolumeRevision != revision ||
                    _inputPending.VolumeTarget is null) return;
                var normalized = double.IsFinite(authoritative.Volume)
                    ? Math.Clamp(authoritative.Volume, 0, 1) : 0;
                _inputPending.AuthoritativeVolume = normalized;
                _inputPending.ConfirmVolumeTarget();
                if (_input is { } current) _input = current with { Volume = normalized };
                var matched = VolumesMatch(normalized, target);
                _status = matched
                    ? $"Microphone level {VolumePercent(normalized)}%"
                    : "Microphone level was not applied · current value restored";
                _statusIsError = !matched;
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            TryRollbackInputVolume(generation, revision,
                "Microphone confirmation failed · current value restored");
        }
    }

    private async Task ConfirmInputMuteAsync(
        long generation, long revision, bool target, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ConfirmationDelay, cancellationToken).ConfigureAwait(false);
            var authoritative = await HostServices.Audio.GetInputAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (_runGeneration != generation || _inputPending.MuteRevision != revision ||
                    _inputPending.MuteTarget is null) return;
                _inputPending.AuthoritativeMuted = authoritative.IsMuted;
                _inputPending.ConfirmMuteTarget();
                if (_input is { } current) _input = current with { IsMuted = authoritative.IsMuted };
                var matched = authoritative.IsMuted == target;
                _status = matched
                    ? target ? "Microphone muted" : "Microphone live"
                    : "Microphone mute was not applied · current state restored";
                _statusIsError = !matched;
            }
            Invalidate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            TryRollbackInputMute(generation, revision,
                "Microphone mute confirmation failed · current state restored");
        }
    }

    private bool TryRollbackSessionVolume(
        string sessionId, long generation, long failedRevision, string? status)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending))
                return true;
            if (pending.VolumeRevision != failedRevision) return false;
            pending.FinishVolumeWorker();
            if (pending.VolumeTarget is null) return true;
            pending.ConfirmVolumeTarget();
            if (pending.HasAuthoritative && FindSessionLocked(sessionId) is { } current)
                ReplaceSessionLocked(current with { Volume = pending.AuthoritativeVolume });
            SetControlStatusLocked(status);
            changed = true;
        }
        if (changed) Invalidate();
        return true;
    }

    private bool TryRollbackSessionMute(
        string sessionId, long generation, long failedRevision, string? status)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending))
                return true;
            if (pending.MuteRevision != failedRevision) return false;
            pending.FinishMuteWorker();
            if (pending.MuteTarget is null) return true;
            pending.ConfirmMuteTarget();
            if (pending.HasAuthoritative && FindSessionLocked(sessionId) is { } current)
                ReplaceSessionLocked(current with { IsMuted = pending.AuthoritativeMuted });
            SetControlStatusLocked(status);
            changed = true;
        }
        if (changed) Invalidate();
        return true;
    }

    private bool TryRollbackOutputVolume(
        long generation, long failedRevision, string? status)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_runGeneration != generation) return true;
            if (_outputPending.VolumeRevision != failedRevision) return false;
            _outputPending.FinishVolumeWorker();
            if (_outputPending.VolumeTarget is null) return true;
            _outputPending.ConfirmVolumeTarget();
            if (_outputPending.HasAuthoritative && _output is { } current)
                _output = current with { Volume = _outputPending.AuthoritativeVolume };
            SetControlStatusLocked(status);
            changed = true;
        }
        if (changed) Invalidate();
        return true;
    }

    private bool TryRollbackOutputMute(
        long generation, long failedRevision, string? status)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_runGeneration != generation) return true;
            if (_outputPending.MuteRevision != failedRevision) return false;
            _outputPending.FinishMuteWorker();
            if (_outputPending.MuteTarget is null) return true;
            _outputPending.ConfirmMuteTarget();
            if (_outputPending.HasAuthoritative && _output is { } current)
                _output = current with { IsMuted = _outputPending.AuthoritativeMuted };
            SetControlStatusLocked(status);
            changed = true;
        }
        if (changed) Invalidate();
        return true;
    }

    private bool TryRollbackInputVolume(long generation, long failedRevision, string? status)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_runGeneration != generation) return true;
            if (_inputPending.VolumeRevision != failedRevision) return false;
            _inputPending.FinishVolumeWorker();
            if (_inputPending.VolumeTarget is null) return true;
            _inputPending.ConfirmVolumeTarget();
            if (_inputPending.HasAuthoritative && _input is { } current)
                _input = current with { Volume = _inputPending.AuthoritativeVolume };
            SetControlStatusLocked(status);
            changed = true;
        }
        if (changed) Invalidate();
        return true;
    }

    private bool TryRollbackInputMute(long generation, long failedRevision, string? status)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_runGeneration != generation) return true;
            if (_inputPending.MuteRevision != failedRevision) return false;
            _inputPending.FinishMuteWorker();
            if (_inputPending.MuteTarget is null) return true;
            _inputPending.ConfirmMuteTarget();
            if (_inputPending.HasAuthoritative && _input is { } current)
                _input = current with { IsMuted = _inputPending.AuthoritativeMuted };
            SetControlStatusLocked(status);
            changed = true;
        }
        if (changed) Invalidate();
        return true;
    }

    private void CancelSessionVolumeWorker(string sessionId, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending)) return;
            pending.FinishVolumeWorker();
            pending.ConfirmVolumeTarget();
            if (pending.HasAuthoritative && FindSessionLocked(sessionId) is { } current)
                ReplaceSessionLocked(current with { Volume = pending.AuthoritativeVolume });
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void CancelSessionMuteWorker(string sessionId, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending)) return;
            pending.FinishMuteWorker();
            pending.ConfirmMuteTarget();
            if (pending.HasAuthoritative && FindSessionLocked(sessionId) is { } current)
                ReplaceSessionLocked(current with { IsMuted = pending.AuthoritativeMuted });
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void CancelOutputVolumeWorker(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _outputPending.FinishVolumeWorker();
            _outputPending.ConfirmVolumeTarget();
            if (_outputPending.HasAuthoritative && _output is { } current)
                _output = current with { Volume = _outputPending.AuthoritativeVolume };
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void CancelOutputMuteWorker(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _outputPending.FinishMuteWorker();
            _outputPending.ConfirmMuteTarget();
            if (_outputPending.HasAuthoritative && _output is { } current)
                _output = current with { IsMuted = _outputPending.AuthoritativeMuted };
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void CancelInputVolumeWorker(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _inputPending.FinishVolumeWorker();
            _inputPending.ConfirmVolumeTarget();
            if (_inputPending.HasAuthoritative && _input is { } current)
                _input = current with { Volume = _inputPending.AuthoritativeVolume };
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void CancelInputMuteWorker(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _inputPending.FinishMuteWorker();
            _inputPending.ConfirmMuteTarget();
            if (_inputPending.HasAuthoritative && _input is { } current)
                _input = current with { IsMuted = _inputPending.AuthoritativeMuted };
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private SessionPendingState GetSessionPendingLocked(WidgetAudioSession session)
    {
        if (_sessionPending.TryGetValue(session.SessionId, out var pending)) return pending;
        pending = new SessionPendingState
        {
            HasAuthoritative = true,
            AuthoritativeVolume = session.Volume,
            AuthoritativeMuted = session.IsMuted,
        };
        _sessionPending.Add(session.SessionId, pending);
        return pending;
    }

    private void SetControlStatusLocked(string? status)
    {
        if (status is null) UpdateHealthyStateLocked();
        else
        {
            _status = status;
            _statusIsError = true;
        }
    }

    private WidgetAudioSession? FindSessionLocked(string sessionId) =>
        _sessions.FirstOrDefault(session =>
            string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));

    private void SelectSessionLocked(string sessionId)
    {
        var index = -1;
        for (var candidate = 0; candidate < _sessions.Count; candidate++)
        {
            if (!string.Equals(_sessions[candidate].SessionId, sessionId, StringComparison.Ordinal)) continue;
            index = candidate;
            break;
        }
        if (index < 0) return;
        _selectedIndex = index;
        _selectedSessionId = sessionId;
        _preferredFocusTarget = PreferredFocusTarget.Session;
    }

    private bool TryResolveSessionAction(
        string actionId,
        out string sessionId,
        out SessionAction action)
    {
        lock (_stateLock)
        {
            if (_sessionActions.TryGetValue(actionId, out var target))
            {
                sessionId = target.SessionId;
                action = target.Action;
                return true;
            }
        }
        sessionId = string.Empty;
        action = default;
        return false;
    }

    private void ReplaceSessionLocked(WidgetAudioSession replacement)
    {
        var copy = _sessions.ToArray();
        var index = Array.FindIndex(copy, session =>
            string.Equals(session.SessionId, replacement.SessionId, StringComparison.Ordinal));
        if (index < 0) return;
        copy[index] = replacement;
        _sessions = copy;
    }

    private bool IsCurrentRun(long generation, CancellationToken cancellationToken)
    {
        lock (_stateLock)
            return _runGeneration == generation && !cancellationToken.IsCancellationRequested;
    }

    private void SetProviderError(AudioMixerViewState state, string status, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _sessions = [];
            _sessionControls = new Dictionary<string, SessionControlIds>(StringComparer.Ordinal);
            _sessionActions = new Dictionary<string, SessionActionTarget>(StringComparer.Ordinal);
            _output = null;
            _devices = [];
            _input = null;
            _selectedSessionId = null;
            _selectedIndex = 0;
            _preferredFocusTarget = PreferredFocusTarget.MasterOutput;
            _sessionPending.Clear();
            _outputPending.Reset();
            _inputPending.Reset();
            _viewState = state;
            _status = status;
            _statusIsError = true;
        }
        Invalidate();
    }

    private static (AudioMixerViewState State, string Status) MapCapabilityFailure(string errorCode) =>
        errorCode switch
        {
            "permission_denied" => (AudioMixerViewState.PermissionDenied, "Audio read permission denied"),
            "lifecycle_denied" => (AudioMixerViewState.LifecycleDenied, "Audio request denied by widget lifecycle"),
            "channel_closed" => (AudioMixerViewState.ChannelClosed, "Audio service channel closed"),
            "platform_unavailable" or "provider_unavailable" =>
                (AudioMixerViewState.ServiceUnavailable, "Windows audio provider unavailable"),
            _ => (AudioMixerViewState.Error, "Audio provider request failed"),
        };

    private static string MapControlFailure(string errorCode) => errorCode switch
    {
        "permission_denied" => "Audio control permission denied · previous state restored",
        "lifecycle_denied" => "Audio control paused by lifecycle · previous state restored",
        "channel_closed" => "Audio service disconnected · previous state restored",
        _ => "Audio control failed · previous state restored",
    };

    private static double RoundVolume(double value) =>
        Math.Clamp(Math.Round(value / VolumeStep, MidpointRounding.AwayFromZero) * VolumeStep, 0, 1);

    private static bool VolumesMatch(double left, double right) =>
        Math.Abs(left - right) <= 0.001;

    private static int VolumePercent(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 100, MidpointRounding.AwayFromZero);

    private enum SessionAction
    {
        SetVolume,
        ToggleMute,
    }

    private sealed record SessionActionTarget(string SessionId, SessionAction Action);

    private readonly record struct SessionPendingView(bool Volume, bool Mute);

    private class OutputPendingState
    {
        public bool HasAuthoritative { get; set; }
        public double AuthoritativeVolume { get; set; }
        public bool AuthoritativeMuted { get; set; }
        public double? VolumeTarget { get; set; }
        public bool? MuteTarget { get; set; }
        public long VolumeRevision { get; set; }
        public long MuteRevision { get; set; }
        public bool VolumeWorkerRunning { get; set; }
        public bool MuteWorkerRunning { get; set; }
        public bool VolumeAwaitingConfirmation { get; set; }
        public bool MuteAwaitingConfirmation { get; set; }
        public bool VolumeIsPending => VolumeTarget is not null;
        public bool MuteIsPending => MuteTarget is not null;
        public bool IsSending => VolumeWorkerRunning || MuteWorkerRunning;

        public void ConfirmVolumeTarget()
        {
            VolumeTarget = null;
            VolumeAwaitingConfirmation = false;
        }

        public void ConfirmMuteTarget()
        {
            MuteTarget = null;
            MuteAwaitingConfirmation = false;
        }

        public void FinishVolumeWorker() => VolumeWorkerRunning = false;

        public void FinishMuteWorker() => MuteWorkerRunning = false;

        public void Reset()
        {
            HasAuthoritative = false;
            AuthoritativeVolume = 0;
            AuthoritativeMuted = false;
            VolumeTarget = null;
            MuteTarget = null;
            VolumeWorkerRunning = false;
            MuteWorkerRunning = false;
            VolumeAwaitingConfirmation = false;
            MuteAwaitingConfirmation = false;
            VolumeRevision++;
            MuteRevision++;
        }
    }

    private sealed class SessionPendingState : OutputPendingState { }

    private enum PreferredFocusTarget
    {
        MasterOutput,
        Microphone,
        Session,
    }

    private enum OptionalAudioSection
    {
        Devices,
        Input,
    }

    private sealed class SessionControlIds
    {
        private SessionControlIds(string prefix)
        {
            Row = $"{prefix}.row";
            Heading = $"{prefix}.heading";
            Details = $"{prefix}.details";
            Name = $"{prefix}.name";
            Position = $"{prefix}.position";
            State = $"{prefix}.state";
            Value = $"{prefix}.volume.value";
            Controls = $"{prefix}.controls";
            VolumeSlider = $"{prefix}.volume.slider";
            VolumeSet = $"{prefix}.volume.set";
            Mute = $"{prefix}.mute";
            MuteIcon = $"{prefix}.mute.icon";
        }

        public string Row { get; }
        public string Heading { get; }
        public string Details { get; }
        public string Name { get; }
        public string Position { get; }
        public string State { get; }
        public string Value { get; }
        public string Controls { get; }
        public string VolumeSlider { get; }
        public string VolumeSet { get; }
        public string Mute { get; }
        public string MuteIcon { get; }

        public static SessionControlIds For(WidgetAudioSession session) => For(session.SessionId);

        private static SessionControlIds For(string sessionId)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
            return new SessionControlIds($"audio.session.{Convert.ToHexString(hash).ToLowerInvariant()}");
        }
    }
}
