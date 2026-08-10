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
    private readonly Dictionary<string, AudioMixerSessionCommandPolicy> _sessionPending =
        new(StringComparer.Ordinal);
    private readonly AudioMixerOutputCommandPolicy _outputPending = new();
    private readonly AudioMixerInputCommandPolicy _inputPending = new();
    private readonly HashSet<Task> _commandWorkers = [];
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
                _status,
                _statusIsError);
        }

        return AudioMixerPresentation.Render(state);
    }

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
            normalized = _outputPending.ReconcileProvider(normalized, VolumesMatch);
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
            normalized = _inputPending.ReconcileProvider(normalized, VolumesMatch);
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

    private static bool IsRetryable(AudioOptionalSectionState state) => state is
        AudioOptionalSectionState.PermissionDenied or
        AudioOptionalSectionState.Revoked or
        AudioOptionalSectionState.Unavailable;

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
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
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
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
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
        long generation;
        CancellationToken runToken;
        var startWorker = false;
        lock (_stateLock)
        {
            if (_output is not { } output) return ValueTask.CompletedTask;
            _preferredFocusTarget = AudioMixerPreferredFocusTarget.MasterOutput;
            var desired = RoundVolume(requestedVolume);
            if (VolumesMatch(desired, output.Volume)) return ValueTask.CompletedTask;
            var admission = _outputPending.QueueVolume(output, desired);
            startWorker = admission.StartWorker;
            _output = admission.State;
            _status = $"Setting master output to {VolumePercent(desired)}%…";
            _statusIsError = false;
            generation = _runGeneration;
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
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
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
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
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
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
            runToken = _runLifetime?.Token ?? ActiveCancellationToken;
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

    private enum OptionalAudioSection
    {
        Devices,
        Input,
    }

}
