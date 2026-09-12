using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.AudioMixer;

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
    internal const string DecreaseOutputVolumeActionId = "output.volume.decrease";
    internal const string IncreaseOutputVolumeActionId = "output.volume.increase";
    private static readonly TimeSpan ConfirmationDelay = TimeSpan.FromMilliseconds(650);
    private readonly object _stateLock = new();
    private IReadOnlyList<WidgetAudioSession> _sessions = [];
    private IReadOnlyDictionary<string, AudioMixerSessionControlIds> _sessionControls =
        new Dictionary<string, AudioMixerSessionControlIds>(StringComparer.Ordinal);
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
    private AudioMixerPreferredFocusTarget _preferredFocusTarget = AudioMixerPreferredFocusTarget.MasterOutput;
    private string _status = "Audio sessions load when this widget becomes visible";
    private bool _statusIsError;
    private bool _deviceSwitchPending;
    private WidgetAudioSpatial? _spatial;
    private AudioOptionalSectionState _spatialState = AudioOptionalSectionState.Initial;
    private bool _spatialPending;
    private string? _spatialFeedback;
    private readonly WidgetTimedMutation _spatialToastExpiry;
    private long _spatialToastGeneration;
    private string? _deviceFeedback;
    private bool _deviceFeedbackError;
    private WidgetAudioDevice? _deviceFeedbackTarget;
    private readonly Dictionary<string, AudioMixerSessionCommandPolicy> _sessionPending =
        new(StringComparer.Ordinal);
    private readonly AudioMixerOutputCommandPolicy _outputPending = new();
    private readonly AudioMixerInputCommandPolicy _inputPending = new();
    private readonly HashSet<Task> _commandWorkers = [];
    private AudioMixerProviderSession? _providerSession;
    private long _runGeneration;
    private int _activationCount;
    private int _fetchCount;

    public AudioMixerWidget() : this(TimeProvider.System) { }

    internal AudioMixerWidget(TimeProvider timeProvider)
    {
        _spatialToastExpiry = CreateTimedMutation(timeProvider: timeProvider);
    }

    internal Task SpatialToastExpiryTask => _spatialToastExpiry.WhenIdleAsync();

    // Called under the state lock. Snapshot updates never renew the deadline.
    private void SetSpatialFeedbackLocked(string? message)
    {
        _spatialFeedback = message;
        var generation = ++_spatialToastGeneration;
        _spatialToastExpiry.Cancel();
        if (message is null) return;
        _spatialToastExpiry.ScheduleLatest(UI.DefaultToastDuration, () =>
        {
            lock (_stateLock)
            {
                if (generation != _spatialToastGeneration) return;
                _spatialFeedback = null;
            }
            Invalidate();
        });
    }

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

    internal async Task DrainCommandWorkersAsync()
    {
        while (true)
        {
            Task[] workers;
            lock (_stateLock) workers = _commandWorkers.ToArray();
            if (workers.Length == 0) return;
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    public override WidgetView Render()
    {
        AudioMixerPresentationState state;
        lock (_stateLock)
        {
            var sessions = new AudioMixerSessionPresentation[_sessions.Count];
            for (var index = 0; index < _sessions.Count; index++)
            {
                var session = _sessions[index];
                var pending = _sessionPending.TryGetValue(session.SessionId, out var value)
                    ? value
                    : null;
                sessions[index] = new AudioMixerSessionPresentation(
                    session,
                    _sessionControls[session.SessionId],
                    pending?.VolumeIsPending ?? false,
                    pending?.MuteIsPending ?? false);
            }

            state = new AudioMixerPresentationState(
                _viewState,
                sessions,
                _output,
                _devices.ToArray(),
                _input,
                _deviceState,
                _inputState,
                _preferredFocusTarget,
                _selectedSessionId,
                _deviceFeedback ?? _status,
                _deviceFeedback is not null ? _deviceFeedbackError : _statusIsError,
                _deviceSwitchPending, _spatial, _spatialState, _spatialPending, _spatialFeedback);
        }

        return AudioMixerPresentation.Render(state);
    }

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationCount);
        await StartActiveRunAsync(activeLifetime).ConfigureAwait(false);
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        await StopActiveRunAsync().ConfigureAwait(false);
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        await StopActiveRunAsync().ConfigureAwait(false);
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_stateLock)
        {
            if (_deviceSwitchPending || _spatialPending) return;
            _deviceFeedback = null;
            _deviceFeedbackTarget = null;
        }
        if (action.ActionId == "spatial.retry")
        {
            lock (_stateLock) { SetSpatialFeedbackLocked(null); _providerSession?.Retry(AudioMixerProviderSection.Spatial); }
            return;
        }
        if (action.ActionId.StartsWith("spatial.set.", StringComparison.Ordinal))
        {
            await SetSpatialAsync(action.ActionId, cancellationToken).ConfigureAwait(false);
            return;
        }
        foreach (var (prefix, direction) in new[]
        {
            ("device.output.", WidgetAudioDeviceDirection.Output),
            ("device.input.", WidgetAudioDeviceDirection.Input),
        })
        {
            if (!action.ActionId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            await SwitchDeviceAsync(action.ActionId[prefix.Length..], direction, cancellationToken).ConfigureAwait(false);
            return;
        }
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
            case DecreaseOutputVolumeActionId:
                await AdjustOutputVolumeAsync(-VolumeStep, cancellationToken).ConfigureAwait(false);
                break;
            case IncreaseOutputVolumeActionId:
                await AdjustOutputVolumeAsync(VolumeStep, cancellationToken).ConfigureAwait(false);
                break;
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
                RetryOptionalSection(AudioMixerProviderSection.Devices);
                break;
            case "input.retry":
                RetryOptionalSection(AudioMixerProviderSection.Input);
                break;
            case "retry":
                if (IsActive)
                    await StartActiveRunAsync(ActiveCancellationToken).ConfigureAwait(false);
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

    private async ValueTask SetSpatialAsync(string actionId, CancellationToken cancellationToken)
    {
        WidgetAudioSpatial spatial;
        WidgetAudioSpatialFormat format;
        long generation;
        lock (_stateLock)
        {
            if (_spatial is null || _spatialPending || _deviceSwitchPending ||
                !_devices.Any(device => device.IsDefault && device.Direction == WidgetAudioDeviceDirection.Output && device.DeviceId == _spatial.DeviceId)) return;
            spatial = _spatial;
            var selected = spatial.Formats.FirstOrDefault(value => value.FormatId != "other" &&
                actionId == $"spatial.set.{spatial.DeviceId}.{value.FormatId}");
            if (selected is null) return;
            format = selected;
            generation = _runGeneration;
            _spatialPending = true;
            SetSpatialFeedbackLocked(null);
            _preferredFocusTarget = AudioMixerPreferredFocusTarget.Spatial;
        }
        Invalidate();
        try
        {
            await DrainCommandWorkersAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await HostServices.Audio.SetSpatialFormatAsync(spatial.DeviceId, format.FormatId, cancellationToken).ConfigureAwait(false);
            var confirmed = await HostServices.Audio.GetSpatialAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (generation != _runGeneration) return;
                ApplySpatialLocked(confirmed);
                SetSpatialFeedbackLocked(_spatial?.DeviceId == spatial.DeviceId && _spatial.SelectedFormatId == format.FormatId
                    ? null : "The output changed. Check the current spatial sound setting.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The setter may have changed Windows before a timeout/failure.
            // Re-query once, never retry the mutation; failure stays local.
            try
            {
                var current = await HostServices.Audio.GetSpatialAsync(cancellationToken).ConfigureAwait(false);
                lock (_stateLock) if (generation == _runGeneration) ApplySpatialLocked(current);
            }
            catch (Exception refreshError) when (refreshError is not OutOfMemoryException) { }
            lock (_stateLock)
                if (generation == _runGeneration)
                    SetSpatialFeedbackLocked(SpatialFailureMessage(error));
        }
        finally
        {
            lock (_stateLock) if (generation == _runGeneration) _spatialPending = false;
            Invalidate();
        }
    }

    internal static string SpatialFailureMessage(Exception error) =>
        (error as WidgetCapabilityException)?.ErrorCode switch
        {
            "permission_denied" or "capability_revoked" => "Allow spatial sound control in Audio Mixer permissions, then try again.",
            "spatial_license_required" => "This format isn't licensed for this output. Set it up in its audio app, or choose Windows Sonic.",
            "spatial_license_expired" => "The license for this format has expired. Check its audio app, or choose another format.",
            "spatial_access_denied" => "Windows didn't allow this change. Choose the format in Windows sound settings.",
            "spatial_not_supported" => "This format isn't available for this output. Choose another format.",
            "resource_not_found" => "The output device changed or disconnected. Choose a format for the current output.",
            "spatial_timeout" or "spatial_unconfirmed" => "Windows hasn't confirmed the change. Check the current setting before trying again.",
            _ => "Spatial sound couldn't be changed. Your other audio controls are still available.",
        };

    private void ApplySpatialLocked(WidgetAudioSpatial spatial)
    {
        // Optional provider data must never throw during Render.
        if (spatial is null || string.IsNullOrWhiteSpace(spatial.DeviceId) || spatial.DeviceId.Length > 64 ||
            spatial.DeviceId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')) ||
            spatial.Formats is null || spatial.Formats.Count is < 1 or > 16 ||
            spatial.Formats.Any(value => value is null || string.IsNullOrWhiteSpace(value.FormatId) ||
                value.FormatId.Length > 40 || value.FormatId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
                string.IsNullOrWhiteSpace(value.DisplayName) || value.DisplayName.Length > 100 || value.DisplayName.Any(char.IsControl)) ||
            spatial.Formats.Select(value => value.FormatId).Distinct(StringComparer.Ordinal).Count() != spatial.Formats.Count ||
            !spatial.Formats.Any(value => value.FormatId == spatial.SelectedFormatId))
        {
            _spatial = null;
            _spatialState = AudioOptionalSectionState.Unavailable;
            return;
        }
        _spatial = spatial with { Formats = spatial.Formats.ToArray() };
        _spatialState = AudioOptionalSectionState.Healthy;
    }

    private async ValueTask SwitchDeviceAsync(string deviceId, WidgetAudioDeviceDirection direction,
        CancellationToken cancellationToken)
    {
        AudioMixerProviderSession? session;
        long generation;
        WidgetAudioDevice? device;
        lock (_stateLock)
        {
            device = _devices.FirstOrDefault(value => value.DeviceId == deviceId && value.Direction == direction);
            if (device is null || _deviceSwitchPending) return;
            session = _providerSession;
            if (session is null) return;
            generation = _runGeneration;
            _deviceSwitchPending = true;
            _deviceFeedback = $"Switching to {device.DisplayName}…";
            _deviceFeedbackError = false;
            _preferredFocusTarget = direction == WidgetAudioDeviceDirection.Output
                ? AudioMixerPreferredFocusTarget.OutputDevice : AudioMixerPreferredFocusTarget.InputDevice;
        }
        Invalidate();
        try
        {
            // Finish already admitted slider changes before changing their target.
            await DrainCommandWorkersAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (generation != _runGeneration) return;
                _outputPending.Reset();
                _inputPending.Reset();
            }
            if (direction == WidgetAudioDeviceDirection.Output)
                await HostServices.Audio.SetDefaultOutputDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
            else
                await HostServices.Audio.SetDefaultInputDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
            ApplyDevices(await HostServices.Audio.GetDevicesAsync(cancellationToken).ConfigureAwait(false), session);
            try
            {
                if (direction == WidgetAudioDeviceDirection.Output)
                {
                    ApplyOutput(await HostServices.Audio.GetOutputAsync(cancellationToken).ConfigureAwait(false), session);
                    ApplySessions(await HostServices.Audio.GetSessionsAsync(cancellationToken).ConfigureAwait(false), session);
                }
                else
                    ApplyInput(await HostServices.Audio.GetInputAsync(cancellationToken).ConfigureAwait(false), session);
            }
            catch (WidgetCapabilityException)
            {
                // Optional control/read permissions are independent of choosing
                // a device. Their subscriptions own availability feedback.
            }
            lock (_stateLock)
                if (generation == _runGeneration)
                {
                    var confirmed = _devices.FirstOrDefault(value => value.DeviceId == deviceId && value.IsDefault);
                    _deviceFeedbackTarget = confirmed;
                    _deviceFeedback = confirmed is not null ? $"Using {confirmed.DisplayName}" : "The default device changed. Please choose it again.";
                    _deviceFeedbackError = confirmed is null;
                }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_stateLock) if (generation == _runGeneration) _deviceFeedback = null;
        }
        catch (WidgetCapabilityException exception)
        {
            lock (_stateLock)
                if (generation == _runGeneration)
                {
                    _deviceFeedback = exception.ErrorCode switch
                    {
                        "permission_denied" => "Allow Audio Mixer to change speakers and microphone in widget permissions, then try again.",
                        "resource_not_found" => "That device is no longer connected. Choose another device.",
                        _ => "Couldn't switch the audio device. Check that it's connected and try again.",
                    };
                    _deviceFeedbackError = true;
                }
        }
        finally
        {
            lock (_stateLock)
                if (generation == _runGeneration) _deviceSwitchPending = false;
            Invalidate();
        }
    }

    private void RememberFocusedControl(ControllerInputEvent input)
    {
        if (input.Context != ControllerInputContext.OpenWidget ||
            !string.Equals(input.ActiveInputScopeId, "audio-mixer", StringComparison.Ordinal) ||
            input.FocusedElementId is not { } focusedId)
            return;

        lock (_stateLock)
        {
            if (focusedId is "audio.spatial.select" or "audio.spatial.retry")
            {
                _preferredFocusTarget = AudioMixerPreferredFocusTarget.Spatial;
                return;
            }
            if (focusedId is "audio.devices.output.select" or "audio.devices.input.select")
            {
                _preferredFocusTarget = focusedId == "audio.devices.output.select"
                    ? AudioMixerPreferredFocusTarget.OutputDevice : AudioMixerPreferredFocusTarget.InputDevice;
                return;
            }
            if (focusedId == "audio.master.volume.slider")
            {
                _preferredFocusTarget = AudioMixerPreferredFocusTarget.MasterOutput;
                return;
            }
            if (focusedId == "audio.input.volume.slider" && _input is not null)
            {
                _preferredFocusTarget = AudioMixerPreferredFocusTarget.Microphone;
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

    private async ValueTask StartActiveRunAsync(CancellationToken activeLifetime)
    {
        AudioMixerProviderSession? previous;
        AudioMixerProviderSession current;
        lock (_stateLock)
        {
            previous = _providerSession;
            current = new AudioMixerProviderSession(
                HostServices.Audio,
                activeLifetime,
                ApplyProviderObservation);
            _providerSession = current;
            ++_runGeneration;
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
            _spatial = null;
            _spatialState = AudioOptionalSectionState.Loading;
            _spatialPending = false;
            SetSpatialFeedbackLocked(null);
        }
        Invalidate();
        if (previous is not null)
            await previous.StopAsync().ConfigureAwait(false);

        var started = false;
        lock (_stateLock)
        {
            if (ReferenceEquals(_providerSession, current) && !activeLifetime.IsCancellationRequested)
            {
                current.Start();
                started = true;
            }
        }
        if (!started)
            await current.StopAsync().ConfigureAwait(false);
        else
            await current.InitialPublication.WaitAsync(activeLifetime).ConfigureAwait(false);
    }

    private async ValueTask StopActiveRunAsync()
    {
        AudioMixerProviderSession? session;
        lock (_stateLock)
        {
            ++_runGeneration;
            _deviceSwitchPending = false;
            _spatialPending = false;
            _spatial = null;
            _spatialState = AudioOptionalSectionState.Initial;
            SetSpatialFeedbackLocked(null);
            _deviceFeedback = null;
            _deviceFeedbackTarget = null;
            session = _providerSession;
            _providerSession = null;
            if (_sessions.Count != 0)
            {
                var restored = _sessions.ToArray();
                for (var index = 0; index < restored.Length; index++)
                {
                    if (_sessionPending.TryGetValue(restored[index].SessionId, out var pending))
                        restored[index] = pending.RestoreAndReset(restored[index]);
                }
                _sessions = restored;
            }
            if (_output is { } output) _output = _outputPending.RestoreAndReset(output);
            else _outputPending.Reset();
            if (_input is { } input) _input = _inputPending.RestoreAndReset(input);
            else _inputPending.Reset();
            _sessionPending.Clear();
        }
        if (session is not null)
            await session.StopAsync().ConfigureAwait(false);
    }

    private void ApplyProviderObservation(
        AudioMixerProviderSession session,
        AudioMixerProviderObservation observation)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, session)) return;
        }

        switch (observation.Kind)
        {
            case AudioMixerProviderObservationKind.RequiredFetchStarted:
                Interlocked.Increment(ref _fetchCount);
                break;
            case AudioMixerProviderObservationKind.RequiredSnapshot:
                ApplyRequiredSnapshot(observation.Output!, observation.Sessions, session);
                break;
            case AudioMixerProviderObservationKind.SessionsChanged:
                ApplySessions(observation.Sessions, session);
                break;
            case AudioMixerProviderObservationKind.OutputChanged:
                ApplyOutput(observation.Output!, session);
                break;
            case AudioMixerProviderObservationKind.DevicesLoading:
                SetOptionalLoading(AudioMixerProviderSection.Devices, session);
                break;
            case AudioMixerProviderObservationKind.DevicesChanged:
                ApplyDevices(observation.Devices, session);
                break;
            case AudioMixerProviderObservationKind.DevicesFailed:
                SetOptionalFailure(AudioMixerProviderSection.Devices,
                    observation.OptionalState, session);
                break;
            case AudioMixerProviderObservationKind.SpatialChanged:
                lock (_stateLock) ApplySpatialLocked(observation.Spatial!);
                Invalidate();
                break;
            case AudioMixerProviderObservationKind.SpatialFailed:
                lock (_stateLock) { _spatial = null; _spatialState = observation.OptionalState; }
                Invalidate();
                break;
            case AudioMixerProviderObservationKind.InputLoading:
                SetOptionalLoading(AudioMixerProviderSection.Input, session);
                break;
            case AudioMixerProviderObservationKind.InputChanged:
                ApplyInput(observation.Input!, session);
                break;
            case AudioMixerProviderObservationKind.InputCleared:
                ClearInput(session, observation.OptionalState);
                break;
            case AudioMixerProviderObservationKind.InputFailed:
                SetOptionalFailure(AudioMixerProviderSection.Input,
                    observation.OptionalState, session);
                break;
            case AudioMixerProviderObservationKind.RequiredFailed:
                SetProviderError(observation.ViewState, observation.Status!, session);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(observation));
        }
    }

    private void ApplyRequiredSnapshot(
        WidgetAudioOutput incomingOutput,
        IReadOnlyList<WidgetAudioSession>? incomingSessions,
        AudioMixerProviderSession providerSession)
    {
        var output = NormalizeOutput(incomingOutput);
        var sessions = NormalizeSessions(incomingSessions);
        var (controls, actions) = BuildSessionRouting(sessions);
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, providerSession)) return;
            _output = _outputPending.ReconcileProvider(output, VolumesMatch);
            ApplySessionsLocked(sessions, controls, actions);
        }
        Invalidate();
    }

    private void ApplyOutput(WidgetAudioOutput incoming, AudioMixerProviderSession session)
    {
        var normalized = NormalizeOutput(incoming);
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, session)) return;
            normalized = _outputPending.ReconcileProvider(normalized, VolumesMatch);
            _output = normalized;
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private static WidgetAudioOutput NormalizeOutput(WidgetAudioOutput incoming) =>
        new(double.IsFinite(incoming.Volume) ? Math.Clamp(incoming.Volume, 0, 1) : 0,
            incoming.IsMuted);

    private void ApplyDevices(
        IReadOnlyList<WidgetAudioDevice>? incoming,
        AudioMixerProviderSession session)
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
            if (!ReferenceEquals(_providerSession, session)) return;
            _devices = devices;
            if (!_deviceSwitchPending && _deviceFeedbackTarget is { } prior &&
                !devices.Any(device => device.DeviceId == prior.DeviceId && device.IsDefault && device.DisplayName == prior.DisplayName))
            {
                _deviceFeedback = null;
                _deviceFeedbackTarget = null;
            }
            _deviceState = devices.Length == 0
                ? AudioOptionalSectionState.Empty
                : AudioOptionalSectionState.Healthy;
        }
        Invalidate();
    }

    private void ClearDevices(AudioMixerProviderSession session, AudioOptionalSectionState state)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, session)) return;
            _devices = [];
            _deviceState = state;
        }
        Invalidate();
    }

    private void ApplyInput(WidgetAudioInput incoming, AudioMixerProviderSession session)
    {
        var normalized = new WidgetAudioInput(
            double.IsFinite(incoming.Volume) ? Math.Clamp(incoming.Volume, 0, 1) : 0,
            incoming.IsMuted);
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, session)) return;
            normalized = _inputPending.ReconcileProvider(normalized, VolumesMatch);
            _input = normalized;
            _inputState = AudioOptionalSectionState.Healthy;
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private void ClearInput(AudioMixerProviderSession session, AudioOptionalSectionState state)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, session)) return;
            _input = null;
            _inputState = state;
            _inputPending.Reset();
        }
        Invalidate();
    }

    private void SetOptionalLoading(
        AudioMixerProviderSection section,
        AudioMixerProviderSession session)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, session)) return;
            if (section == AudioMixerProviderSection.Devices)
                _deviceState = AudioOptionalSectionState.Loading;
            else
                _inputState = AudioOptionalSectionState.Loading;
        }
        Invalidate();
    }

    private void SetOptionalFailure(
        AudioMixerProviderSection section,
        AudioOptionalSectionState state,
        AudioMixerProviderSession session)
    {
        if (!IsRetryable(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (section == AudioMixerProviderSection.Devices)
            ClearDevices(session, state);
        else
            ClearInput(session, state);
    }

    private void RetryOptionalSection(AudioMixerProviderSection section)
    {
        AudioMixerProviderSession? session;
        lock (_stateLock)
        {
            session = _providerSession;
            if (session is null) return;
            if (section == AudioMixerProviderSection.Devices)
            {
                if (!IsRetryable(_deviceState)) return;
            }
            else
            {
                if (!IsRetryable(_inputState)) return;
            }
        }
        session.Retry(section);
    }

    private static bool IsRetryable(AudioOptionalSectionState state) => state is
        AudioOptionalSectionState.PermissionDenied or
        AudioOptionalSectionState.Revoked or
        AudioOptionalSectionState.Unavailable;

    private void ApplySessions(
        IReadOnlyList<WidgetAudioSession>? incoming,
        AudioMixerProviderSession providerSession)
    {
        var normalized = NormalizeSessions(incoming);
        var (controls, actions) = BuildSessionRouting(normalized);
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, providerSession)) return;
            ApplySessionsLocked(normalized, controls, actions);
        }
        Invalidate();
    }

    private void ApplySessionsLocked(
        List<WidgetAudioSession> normalized,
        IReadOnlyDictionary<string, AudioMixerSessionControlIds> controls,
        IReadOnlyDictionary<string, SessionActionTarget> actions)
    {
        var previousId = _selectedSessionId;
        var previousIndex = _selectedIndex;

        var presentIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < normalized.Count; index++)
        {
            var session = normalized[index];
            presentIds.Add(session.SessionId);
            if (!_sessionPending.TryGetValue(session.SessionId, out var pending))
            {
                pending = new AudioMixerSessionCommandPolicy(session);
                _sessionPending.Add(session.SessionId, pending);
            }
            session = pending.ReconcileProvider(session, VolumesMatch);
            normalized[index] = session;
        }

        foreach (var staleId in _sessionPending.Keys
                     .Where(id => !presentIds.Contains(id) && _sessionPending[id].CanDiscard)
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
        IReadOnlyDictionary<string, AudioMixerSessionControlIds> Controls,
        IReadOnlyDictionary<string, SessionActionTarget> Actions)
        BuildSessionRouting(IReadOnlyList<WidgetAudioSession> sessions)
    {
        var controls = new Dictionary<string, AudioMixerSessionControlIds>(sessions.Count, StringComparer.Ordinal);
        var actions = new Dictionary<string, SessionActionTarget>(sessions.Count * 3, StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            var ids = AudioMixerSessionControlIds.For(session);
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
            var admission = pending.QueueVolume(session, desired);
            startWorker = admission.StartWorker;
            ReplaceSessionLocked(admission.State);
            _status = $"Setting {session.DisplayName} to {VolumePercent(desired)}%…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _providerSession?.CancellationToken ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker)
            TrackCommandWorker(RunSessionVolumeQueueAsync(sessionId, generation, runToken));
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
            var admission = pending.QueueMute(session, desired);
            startWorker = admission.StartWorker;
            ReplaceSessionLocked(admission.State);
            _status = desired ? $"Muting {session.DisplayName}…" : $"Unmuting {session.DisplayName}…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _providerSession?.CancellationToken ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker)
            TrackCommandWorker(RunSessionMuteQueueAsync(sessionId, generation, runToken));
        return ValueTask.CompletedTask;
    }

    private ValueTask SetOutputVolumeAsync(double requestedVolume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SliderMath.IsValidRequestedValue(requestedVolume, 0, 1, VolumeStep))
            return ValueTask.CompletedTask;
        return QueueOutputVolumeAsync(requestedVolume, relative: false);
    }

    private ValueTask AdjustOutputVolumeAsync(double delta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return QueueOutputVolumeAsync(delta, relative: true);
    }

    private ValueTask QueueOutputVolumeAsync(double value, bool relative)
    {
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            if (_output is not { } output) return ValueTask.CompletedTask;
            _preferredFocusTarget = AudioMixerPreferredFocusTarget.MasterOutput;
            var desired = relative
                ? Math.Round(
                    Math.Clamp(output.Volume + value, 0, 1),
                    6,
                    MidpointRounding.AwayFromZero)
                : RoundVolume(value);
            if (VolumesMatch(desired, output.Volume)) return ValueTask.CompletedTask;
            var admission = _outputPending.QueueVolume(output, desired);
            startWorker = admission.StartWorker;
            _output = admission.State;
            _status = $"Setting master output to {VolumePercent(desired)}%…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _providerSession?.CancellationToken ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) TrackCommandWorker(RunOutputVolumeQueueAsync(generation, runToken));
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
            _preferredFocusTarget = AudioMixerPreferredFocusTarget.MasterOutput;
            var desired = !output.IsMuted;
            var admission = _outputPending.QueueMute(output, desired);
            startWorker = admission.StartWorker;
            _output = admission.State;
            _status = desired ? "Muting master output…" : "Unmuting master output…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _providerSession?.CancellationToken ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) TrackCommandWorker(RunOutputMuteQueueAsync(generation, runToken));
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
            _preferredFocusTarget = AudioMixerPreferredFocusTarget.Microphone;
            var desired = RoundVolume(requestedVolume);
            if (VolumesMatch(desired, input.Volume)) return ValueTask.CompletedTask;
            var admission = _inputPending.QueueVolume(input, desired);
            startWorker = admission.StartWorker;
            _input = admission.State;
            _status = $"Setting microphone to {VolumePercent(desired)}%…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _providerSession?.CancellationToken ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) TrackCommandWorker(RunInputVolumeQueueAsync(generation, runToken));
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
            _preferredFocusTarget = AudioMixerPreferredFocusTarget.Microphone;
            var desired = !input.IsMuted;
            var admission = _inputPending.QueueMute(input, desired);
            startWorker = admission.StartWorker;
            _input = admission.State;
            _status = desired ? "Muting microphone…" : "Unmuting microphone…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _providerSession?.CancellationToken ?? ActiveCancellationToken;
        }
        Invalidate();
        if (startWorker) TrackCommandWorker(RunInputMuteQueueAsync(generation, runToken));
        return ValueTask.CompletedTask;
    }

    private void TrackCommandWorker(Task worker)
    {
        lock (_stateLock) _commandWorkers.Add(worker);
        _ = ObserveCommandWorkerAsync(worker);
    }

    private async Task ObserveCommandWorkerAsync(Task worker)
    {
        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Command workers sanitize their operation failures. This final
            // observer still consumes any unexpected terminal fault so a
            // detached task can never become an unobserved process fault.
        }
        finally
        {
            lock (_stateLock) _commandWorkers.Remove(worker);
        }
    }

    private async Task RunSessionVolumeQueueAsync(
        string sessionId, long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            AudioMixerCommandWork<double> work;
            lock (_stateLock)
            {
                _sessionPending.TryGetValue(sessionId, out var pending);
                if (_runGeneration != generation || pending is null ||
                    !pending.TryBeginVolumeWork(out work)) return;
            }
            try
            {
                await HostServices.Audio.SetSessionVolumeAsync(sessionId, work.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelSessionVolumeWorker(sessionId, generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackSessionVolume(sessionId, generation, work.Revision,
                        "Host audio control service unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackSessionVolume(sessionId, generation, work.Revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackSessionVolume(sessionId, generation, work.Revision,
                        "Volume change failed · previous value restored")) return;
                continue;
            }

            var reconcile = false;
            lock (_stateLock)
            {
                if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending))
                    return;
                var acknowledgement = pending.AcknowledgeVolume(work.Revision);
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NoPendingCommand) return;
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NewerRevision) continue;
                _status = $"{FindSessionLocked(sessionId)?.DisplayName ?? "Application"} volume {VolumePercent(work.Target)}%";
                _statusIsError = false;
                reconcile = acknowledgement.StartConfirmation;
            }
            Invalidate();
            if (reconcile)
                TrackCommandWorker(ConfirmSessionVolumeAsync(
                    sessionId, generation, work.Revision, work.Target, cancellationToken));
            return;
        }
    }

    private async Task RunSessionMuteQueueAsync(
        string sessionId, long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            AudioMixerCommandWork<bool> work;
            lock (_stateLock)
            {
                _sessionPending.TryGetValue(sessionId, out var pending);
                if (_runGeneration != generation || pending is null ||
                    !pending.TryBeginMuteWork(out work)) return;
            }
            try
            {
                await HostServices.Audio.SetSessionMutedAsync(sessionId, work.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelSessionMuteWorker(sessionId, generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackSessionMute(sessionId, generation, work.Revision,
                        "Host audio control service unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackSessionMute(sessionId, generation, work.Revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackSessionMute(sessionId, generation, work.Revision,
                        "Mute change failed · previous state restored")) return;
                continue;
            }

            var reconcile = false;
            lock (_stateLock)
            {
                if (_runGeneration != generation || !_sessionPending.TryGetValue(sessionId, out var pending))
                    return;
                var acknowledgement = pending.AcknowledgeMute(work.Revision);
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NoPendingCommand) return;
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NewerRevision) continue;
                _status = work.Target
                    ? $"{FindSessionLocked(sessionId)?.DisplayName ?? "Application"} muted"
                    : $"{FindSessionLocked(sessionId)?.DisplayName ?? "Application"} unmuted";
                _statusIsError = false;
                reconcile = acknowledgement.StartConfirmation;
            }
            Invalidate();
            if (reconcile)
                TrackCommandWorker(ConfirmSessionMuteAsync(
                    sessionId, generation, work.Revision, work.Target, cancellationToken));
            return;
        }
    }

    private async Task RunOutputVolumeQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            AudioMixerCommandWork<double> work;
            lock (_stateLock)
            {
                if (_runGeneration != generation ||
                    !_outputPending.TryBeginVolumeWork(out work)) return;
            }
            try
            {
                await HostServices.Audio.SetOutputVolumeAsync(work.Target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelOutputVolumeWorker(generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackOutputVolume(generation, work.Revision,
                        "Host master-output control unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackOutputVolume(generation, work.Revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackOutputVolume(generation, work.Revision,
                        "Master volume change failed · previous value restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                var acknowledgement = _outputPending.AcknowledgeVolume(work.Revision);
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NoPendingCommand) return;
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NewerRevision) continue;
                _status = $"Master output volume {VolumePercent(work.Target)}%";
                _statusIsError = false;
            }
            Invalidate();
            TrackCommandWorker(ConfirmOutputVolumeAsync(
                generation, work.Revision, work.Target, cancellationToken));
            return;
        }
    }

    private async Task RunOutputMuteQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            AudioMixerCommandWork<bool> work;
            lock (_stateLock)
            {
                if (_runGeneration != generation ||
                    !_outputPending.TryBeginMuteWork(out work)) return;
            }
            try
            {
                await HostServices.Audio.SetOutputMutedAsync(work.Target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelOutputMuteWorker(generation);
                return;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                if (TryRollbackOutputMute(generation, work.Revision,
                        "Host master-output control unavailable")) return;
                continue;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackOutputMute(generation, work.Revision,
                        MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackOutputMute(generation, work.Revision,
                        "Master mute change failed · previous state restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                var acknowledgement = _outputPending.AcknowledgeMute(work.Revision);
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NoPendingCommand) return;
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NewerRevision) continue;
                _status = work.Target ? "Master output muted" : "Master output unmuted";
                _statusIsError = false;
            }
            Invalidate();
            TrackCommandWorker(ConfirmOutputMuteAsync(
                generation, work.Revision, work.Target, cancellationToken));
            return;
        }
    }

    private async Task RunInputVolumeQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            AudioMixerCommandWork<double> work;
            lock (_stateLock)
            {
                if (_runGeneration != generation ||
                    !_inputPending.TryBeginVolumeWork(out work)) return;
            }
            try
            {
                await HostServices.Audio.SetInputVolumeAsync(work.Target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelInputVolumeWorker(generation);
                return;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackInputVolume(
                        generation, work.Revision, MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackInputVolume(generation, work.Revision,
                        "Microphone level change failed · previous value restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                var acknowledgement = _inputPending.AcknowledgeVolume(work.Revision);
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NoPendingCommand) return;
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NewerRevision) continue;
                _status = $"Microphone level {VolumePercent(work.Target)}%";
                _statusIsError = false;
            }
            Invalidate();
            TrackCommandWorker(ConfirmInputVolumeAsync(
                generation, work.Revision, work.Target, cancellationToken));
            return;
        }
    }

    private async Task RunInputMuteQueueAsync(long generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            AudioMixerCommandWork<bool> work;
            lock (_stateLock)
            {
                if (_runGeneration != generation ||
                    !_inputPending.TryBeginMuteWork(out work)) return;
            }
            try
            {
                await HostServices.Audio.SetInputMutedAsync(work.Target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelInputMuteWorker(generation);
                return;
            }
            catch (WidgetCapabilityException exception)
            {
                if (TryRollbackInputMute(
                        generation, work.Revision, MapControlFailure(exception.ErrorCode))) return;
                continue;
            }
            catch (Exception)
            {
                if (TryRollbackInputMute(generation, work.Revision,
                        "Microphone mute change failed · previous state restored")) return;
                continue;
            }
            lock (_stateLock)
            {
                if (_runGeneration != generation) return;
                var acknowledgement = _inputPending.AcknowledgeMute(work.Revision);
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NoPendingCommand) return;
                if (acknowledgement.Kind == AudioMixerCommandTransitionKind.NewerRevision) continue;
                _status = work.Target ? "Microphone muted" : "Microphone live";
                _statusIsError = false;
            }
            Invalidate();
            TrackCommandWorker(ConfirmInputMuteAsync(
                generation, work.Revision, work.Target, cancellationToken));
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
                    !_sessionPending.TryGetValue(sessionId, out var pending)) return;
                var current = FindSessionLocked(sessionId);
                if (authoritative is null || current is null)
                {
                    if (!pending.AbandonVolumeConfirmation(revision)) return;
                    _status = "Application audio session ended before its volume could be confirmed";
                    _statusIsError = true;
                }
                else
                {
                    var transition = pending.ConfirmVolume(
                        revision, current, authoritative.Volume, VolumesMatch);
                    if (transition.Kind != AudioMixerCommandTransitionKind.Applied) return;
                    ReplaceSessionLocked(transition.State);
                    _status = transition.Matched
                        ? $"{current.DisplayName} volume {VolumePercent(authoritative.Volume)}%"
                        : $"{current.DisplayName} volume was not applied · current value restored";
                    _statusIsError = !transition.Matched;
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
                    !_sessionPending.TryGetValue(sessionId, out var pending)) return;
                var current = FindSessionLocked(sessionId);
                if (authoritative is null || current is null)
                {
                    if (!pending.AbandonMuteConfirmation(revision)) return;
                    _status = "Application audio session ended before mute could be confirmed";
                    _statusIsError = true;
                }
                else
                {
                    var transition = pending.ConfirmMute(
                        revision, current, authoritative.IsMuted);
                    if (transition.Kind != AudioMixerCommandTransitionKind.Applied) return;
                    ReplaceSessionLocked(transition.State);
                    _status = transition.Matched
                        ? target ? $"{current.DisplayName} muted" : $"{current.DisplayName} unmuted"
                        : $"{current.DisplayName} mute was not applied · current state restored";
                    _statusIsError = !transition.Matched;
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
                if (_runGeneration != generation || _output is not { } current) return;
                var normalized = double.IsFinite(authoritative.Volume)
                    ? Math.Clamp(authoritative.Volume, 0, 1)
                    : 0;
                var transition = _outputPending.ConfirmVolume(
                    revision, current, normalized, VolumesMatch);
                if (transition.Kind != AudioMixerCommandTransitionKind.Applied) return;
                _output = transition.State;
                _status = transition.Matched
                    ? $"Master output volume {VolumePercent(normalized)}%"
                    : "Master volume was not applied · current value restored";
                _statusIsError = !transition.Matched;
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
                if (_runGeneration != generation || _output is not { } current) return;
                var transition = _outputPending.ConfirmMute(
                    revision, current, authoritative.IsMuted);
                if (transition.Kind != AudioMixerCommandTransitionKind.Applied) return;
                _output = transition.State;
                _status = transition.Matched
                    ? target ? "Master output muted" : "Master output unmuted"
                    : "Master mute was not applied · current state restored";
                _statusIsError = !transition.Matched;
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
                if (_runGeneration != generation || _input is not { } current) return;
                var normalized = double.IsFinite(authoritative.Volume)
                    ? Math.Clamp(authoritative.Volume, 0, 1) : 0;
                var transition = _inputPending.ConfirmVolume(
                    revision, current, normalized, VolumesMatch);
                if (transition.Kind != AudioMixerCommandTransitionKind.Applied) return;
                _input = transition.State;
                _status = transition.Matched
                    ? $"Microphone level {VolumePercent(normalized)}%"
                    : "Microphone level was not applied · current value restored";
                _statusIsError = !transition.Matched;
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
                if (_runGeneration != generation || _input is not { } current) return;
                var transition = _inputPending.ConfirmMute(
                    revision, current, authoritative.IsMuted);
                if (transition.Kind != AudioMixerCommandTransitionKind.Applied) return;
                _input = transition.State;
                _status = transition.Matched
                    ? target ? "Microphone muted" : "Microphone live"
                    : "Microphone mute was not applied · current state restored";
                _statusIsError = !transition.Matched;
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
        => ApplyCommandFailure<double>(
            generation,
            status,
            () => _sessionPending.TryGetValue(sessionId, out var pending)
                ? pending.FailVolume(failedRevision)
                : null,
            authoritative =>
            {
                if (FindSessionLocked(sessionId) is { } current)
                    ReplaceSessionLocked(current with { Volume = authoritative });
            });

    private bool TryRollbackSessionMute(
        string sessionId, long generation, long failedRevision, string? status)
        => ApplyCommandFailure<bool>(
            generation,
            status,
            () => _sessionPending.TryGetValue(sessionId, out var pending)
                ? pending.FailMute(failedRevision)
                : null,
            authoritative =>
            {
                if (FindSessionLocked(sessionId) is { } current)
                    ReplaceSessionLocked(current with { IsMuted = authoritative });
            });

    private bool TryRollbackOutputVolume(
        long generation, long failedRevision, string? status)
        => ApplyCommandFailure<double>(
            generation,
            status,
            () => _outputPending.FailVolume(failedRevision),
            authoritative =>
            {
                if (_output is { } current) _output = current with { Volume = authoritative };
            });

    private bool TryRollbackOutputMute(
        long generation, long failedRevision, string? status)
        => ApplyCommandFailure<bool>(
            generation,
            status,
            () => _outputPending.FailMute(failedRevision),
            authoritative =>
            {
                if (_output is { } current) _output = current with { IsMuted = authoritative };
            });

    private bool TryRollbackInputVolume(long generation, long failedRevision, string? status)
        => ApplyCommandFailure<double>(
            generation,
            status,
            () => _inputPending.FailVolume(failedRevision),
            authoritative =>
            {
                if (_input is { } current) _input = current with { Volume = authoritative };
            });

    private bool TryRollbackInputMute(long generation, long failedRevision, string? status)
        => ApplyCommandFailure<bool>(
            generation,
            status,
            () => _inputPending.FailMute(failedRevision),
            authoritative =>
            {
                if (_input is { } current) _input = current with { IsMuted = authoritative };
            });

    private bool ApplyCommandFailure<TValue>(
        long generation,
        string? status,
        Func<AudioMixerCommandTerminal<TValue>?> transitionFactory,
        Action<TValue> restore)
    {
        var changed = false;
        lock (_stateLock)
        {
            if (_runGeneration != generation) return true;
            var transition = transitionFactory();
            if (transition is null) return true;
            if (transition.Value.Kind == AudioMixerCommandTransitionKind.NewerRevision)
                return false;
            if (transition.Value.Kind == AudioMixerCommandTransitionKind.NoPendingCommand)
                return true;
            if (transition.Value.HasAuthoritative) restore(transition.Value.Authoritative);
            SetControlStatusLocked(status);
            changed = true;
        }
        if (changed) Invalidate();
        return true;
    }

    private void CancelSessionVolumeWorker(string sessionId, long generation)
        => ApplyCommandCancellation<double>(
            generation,
            () => _sessionPending.TryGetValue(sessionId, out var pending)
                ? pending.CancelVolume()
                : null,
            authoritative =>
            {
                if (FindSessionLocked(sessionId) is { } current)
                    ReplaceSessionLocked(current with { Volume = authoritative });
            });

    private void CancelSessionMuteWorker(string sessionId, long generation)
        => ApplyCommandCancellation<bool>(
            generation,
            () => _sessionPending.TryGetValue(sessionId, out var pending)
                ? pending.CancelMute()
                : null,
            authoritative =>
            {
                if (FindSessionLocked(sessionId) is { } current)
                    ReplaceSessionLocked(current with { IsMuted = authoritative });
            });

    private void CancelOutputVolumeWorker(long generation)
        => ApplyCommandCancellation<double>(
            generation,
            () => _outputPending.CancelVolume(),
            authoritative =>
            {
                if (_output is { } current) _output = current with { Volume = authoritative };
            });

    private void CancelOutputMuteWorker(long generation)
        => ApplyCommandCancellation<bool>(
            generation,
            () => _outputPending.CancelMute(),
            authoritative =>
            {
                if (_output is { } current) _output = current with { IsMuted = authoritative };
            });

    private void CancelInputVolumeWorker(long generation)
        => ApplyCommandCancellation<double>(
            generation,
            () => _inputPending.CancelVolume(),
            authoritative =>
            {
                if (_input is { } current) _input = current with { Volume = authoritative };
            });

    private void CancelInputMuteWorker(long generation)
        => ApplyCommandCancellation<bool>(
            generation,
            () => _inputPending.CancelMute(),
            authoritative =>
            {
                if (_input is { } current) _input = current with { IsMuted = authoritative };
            });

    private void ApplyCommandCancellation<TValue>(
        long generation,
        Func<AudioMixerCommandTerminal<TValue>?> transitionFactory,
        Action<TValue> restore)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            var transition = transitionFactory();
            if (transition is null) return;
            if (transition.Value.HasAuthoritative) restore(transition.Value.Authoritative);
            UpdateHealthyStateLocked();
        }
        Invalidate();
    }

    private AudioMixerSessionCommandPolicy GetSessionPendingLocked(WidgetAudioSession session)
    {
        if (_sessionPending.TryGetValue(session.SessionId, out var pending)) return pending;
        pending = new AudioMixerSessionCommandPolicy(session);
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
        _preferredFocusTarget = AudioMixerPreferredFocusTarget.Session;
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

    private void SetProviderError(
        AudioMixerViewState state,
        string status,
        AudioMixerProviderSession session)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(_providerSession, session)) return;
            _sessions = [];
            _sessionControls = new Dictionary<string, AudioMixerSessionControlIds>(StringComparer.Ordinal);
            _sessionActions = new Dictionary<string, SessionActionTarget>(StringComparer.Ordinal);
            _output = null;
            _devices = [];
            _input = null;
            _selectedSessionId = null;
            _selectedIndex = 0;
            _preferredFocusTarget = AudioMixerPreferredFocusTarget.MasterOutput;
            _sessionPending.Clear();
            _outputPending.Reset();
            _inputPending.Reset();
            _viewState = state;
            _status = status;
            _statusIsError = true;
        }
        Invalidate();
    }

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

}
