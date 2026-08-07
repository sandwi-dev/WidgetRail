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

/// <summary>
/// Event-driven, controller-first audio session mixer. The widget uses only the
/// typed host audio service: it never opens Core Audio objects or transport
/// handles itself.
/// </summary>
public sealed class AudioMixerWidget : Widget
{
    public const double VolumeStep = 0.05;

    private static readonly IReadOnlyList<WidgetQuickAction> SessionQuickActions =
    [
        new(ControllerButton.LeftBumper, "session.previous", "Previous audio session"),
        new(ControllerButton.RightBumper, "session.next", "Next audio session"),
    ];

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private IReadOnlyList<WidgetAudioSession> _sessions = [];
    private AudioMixerViewState _viewState = AudioMixerViewState.Initial;
    private string? _selectedSessionId;
    private int _selectedIndex;
    private string _status = "Audio sessions load when this widget becomes visible";
    private bool _statusIsError;
    private bool _controlBusy;
    private string? _pendingSessionId;
    private double? _pendingVolume;
    private bool? _pendingMuted;
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

    public int ActivationCount => Volatile.Read(ref _activationCount);
    public int FetchCount => Volatile.Read(ref _fetchCount);

    public override WidgetView Render()
    {
        AudioMixerViewState viewState;
        IReadOnlyList<WidgetAudioSession> sessions;
        WidgetAudioSession? selected;
        int selectedIndex;
        string status;
        bool statusIsError;
        bool controlBusy;
        lock (_stateLock)
        {
            viewState = _viewState;
            sessions = _sessions;
            selectedIndex = _selectedIndex;
            selected = SelectedSessionLocked();
            status = _status;
            statusIsError = _statusIsError;
            controlBusy = _controlBusy;
        }

        var header = UI.Stack("audio.header",
            UI.Text("CONTROL CENTER", "audio.eyebrow", "Control Center").Classes("audio-eyebrow"),
            UI.Text("Audio Mixer", "audio.title", "Audio Mixer").Classes("audio-title"),
            UI.Text(status, "audio.status", status).Classes(
                "audio-status",
                statusIsError ? "is-error" : viewState == AudioMixerViewState.Ready ? "is-live" : "is-neutral"))
            .Classes("audio-header");

        if (viewState != AudioMixerViewState.Ready || selected is null)
            return RenderNonSessionState(header, viewState);

        var hasMultipleSessions = sessions.Count > 1;
        var volumePercent = VolumePercent(selected.Volume);
        var previousButton = UI.Button("", "session.previous", "audio.session.previous")
            .Icon(WidgetGlyph.Previous, "Previous audio session")
            .Disabled(!hasMultipleSessions)
            .FocusRight("audio.session.next")
            .FocusDown("audio.volume.down")
            .Classes("audio-session-action");
        var nextButton = UI.Button("", "session.next", "audio.session.next")
            .Icon(WidgetGlyph.Next, "Next audio session")
            .Disabled(!hasMultipleSessions)
            .FocusLeft("audio.session.previous")
            .FocusDown("audio.volume.up")
            .Classes("audio-session-action");
        var volumeDown = UI.Button("−", "volume.down", "audio.volume.down")
            .Disabled(controlBusy || selected.Volume <= 0)
            .Busy(controlBusy)
            .FocusUp("audio.session.previous")
            .FocusLeft("audio.volume.up")
            .FocusRight("audio.mute")
            .Classes("audio-volume-action");
        var mute = UI.Button(selected.IsMuted ? "Unmute" : "Mute", "mute.toggle", "audio.mute")
            .Icon(selected.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Volume,
                selected.IsMuted ? $"Unmute {selected.DisplayName}" : $"Mute {selected.DisplayName}")
            .Selected(selected.IsMuted)
            .Busy(controlBusy)
            .Disabled(controlBusy)
            .FocusUp("audio.session.previous")
            .FocusLeft("audio.volume.down")
            .FocusRight("audio.volume.up")
            .Classes("audio-mute-action", selected.IsMuted ? "is-muted" : "is-audible");
        var volumeUp = UI.Button("+", "volume.up", "audio.volume.up")
            .Disabled(controlBusy || selected.Volume >= 1)
            .Busy(controlBusy)
            .FocusUp("audio.session.next")
            .FocusLeft("audio.mute")
            .FocusRight("audio.volume.down")
            .Classes("audio-volume-action");

        var root = UI.Stack("audio.root",
                header,
                UI.Stack("audio.session.card",
                    UI.Row("audio.session.switcher",
                        previousButton,
                        UI.Stack("audio.session.details",
                            UI.Text(selected.DisplayName, "audio.session.name", selected.DisplayName)
                                .Classes("audio-session-name"),
                            UI.Text($"Session {selectedIndex + 1} of {sessions.Count}", "audio.session.count",
                                    $"Audio session {selectedIndex + 1} of {sessions.Count}")
                                .Classes("audio-session-count"),
                            UI.Text(selected.IsActive ? "ACTIVE NOW" : "IDLE", "audio.session.state",
                                    selected.IsActive ? "Audio is active" : "Audio is idle")
                                .Classes("audio-session-state", selected.IsActive ? "is-active" : "is-idle"))
                            .Classes("audio-session-details"),
                        nextButton)
                        .Classes("audio-session-switcher"),
                    UI.Row("audio.volume.row",
                        UI.Progress(volumePercent, 100, "audio.volume.progress",
                                $"{selected.DisplayName} volume {volumePercent} percent")
                            .Classes("audio-volume-progress"),
                        UI.Text($"{volumePercent}%", "audio.volume.value", $"Volume {volumePercent} percent")
                            .Classes("audio-volume-value"))
                        .Classes("audio-volume-row"),
                    UI.Row("audio.controls", volumeDown, mute, volumeUp).Classes("audio-controls"),
                    UI.Text("LB/RB  SESSION     LT/RT  VOLUME     X  MUTE", "audio.shortcuts",
                            "Left and right bumper select a session. Left and right trigger change volume. X toggles mute.")
                        .Classes("audio-shortcuts"))
                    .Classes("audio-session-card"))
            .InputScope("audio-mixer")
            .Shortcut(ControllerButton.LeftBumper, "session.previous")
            .Shortcut(ControllerButton.RightBumper, "session.next")
            .Shortcut(ControllerButton.LeftTrigger, "volume.down")
            .Shortcut(ControllerButton.RightTrigger, "volume.up")
            .Shortcut(ControllerButton.X, "mute.toggle")
            .Classes("audio-mixer-widget", "has-sessions");

        return new WidgetView(root, InitialFocusId: "audio.mute", QuickActions: SessionQuickActions);
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
        switch (action.ActionId)
        {
            case "session.previous":
                SelectRelativeSession(-1);
                break;
            case "session.next":
                SelectRelativeSession(1);
                break;
            case "volume.down":
                await ChangeVolumeAsync(-VolumeStep, cancellationToken).ConfigureAwait(false);
                break;
            case "volume.up":
                await ChangeVolumeAsync(VolumeStep, cancellationToken).ConfigureAwait(false);
                break;
            case "mute.toggle":
                await ToggleMuteAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "retry":
                if (IsActive) StartActiveRun(ActiveCancellationToken);
                break;
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
        var root = UI.Stack("audio.root",
                header,
                UI.Stack("audio.state.card",
                    UI.Text(title, "audio.state.title", title).Classes("audio-state-title"),
                    UI.Text(help, "audio.state.help", help).Classes("audio-help", error ? "is-error" : "is-neutral"),
                    retry)
                    .Classes("audio-state-card"))
            .InputScope("audio-mixer")
            .Classes("audio-mixer-widget", error ? "has-error" : "has-state");
        return new WidgetView(root, InitialFocusId: "audio.retry");
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
        }
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
            _controlBusy = false;
            _pendingSessionId = null;
            _pendingVolume = null;
            _pendingMuted = null;
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
            await using var subscription = await HostServices.Audio
                .OpenSessionsSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _fetchCount);
            var sessions = await HostServices.Audio.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplySessions(sessions, generation);

            await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!IsCurrentRun(generation, cancellationToken)) return;
                ApplySessions(change.Sessions, generation);
            }

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

    private void ApplySessions(IReadOnlyList<WidgetAudioSession>? incoming, long generation)
    {
        var normalized = NormalizeSessions(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            var previousId = _selectedSessionId;
            var previousIndex = _selectedIndex;

            if (_pendingSessionId is { } pendingId)
            {
                var pendingIndex = normalized.FindIndex(session =>
                    string.Equals(session.SessionId, pendingId, StringComparison.Ordinal));
                if (pendingIndex >= 0)
                {
                    var session = normalized[pendingIndex];
                    normalized[pendingIndex] = session with
                    {
                        Volume = _pendingVolume ?? session.Volume,
                        IsMuted = _pendingMuted ?? session.IsMuted,
                    };
                }
            }

            _sessions = normalized;
            if (normalized.Count == 0)
            {
                _selectedSessionId = null;
                _selectedIndex = 0;
                _viewState = AudioMixerViewState.Empty;
                _status = "Listening for application audio sessions";
                _statusIsError = false;
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
                _viewState = AudioMixerViewState.Ready;
                if (!_controlBusy)
                    _status = normalized.Count == 1
                        ? "1 audio session · live updates"
                        : $"{normalized.Count} audio sessions · live updates";
                _statusIsError = false;
            }
        }
        Invalidate();
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
                DisplayName = string.IsNullOrWhiteSpace(session.DisplayName)
                    ? "Unnamed application"
                    : session.DisplayName.Trim(),
                Volume = volume,
            });
        }
        return result;
    }

    private void SelectRelativeSession(int delta)
    {
        lock (_stateLock)
        {
            if (_sessions.Count <= 1 || _viewState != AudioMixerViewState.Ready) return;
            _selectedIndex = (_selectedIndex + delta) % _sessions.Count;
            if (_selectedIndex < 0) _selectedIndex += _sessions.Count;
            _selectedSessionId = _sessions[_selectedIndex].SessionId;
            _status = $"Selected {_sessions[_selectedIndex].DisplayName}";
            _statusIsError = false;
        }
        Invalidate();
    }

    private async ValueTask ChangeVolumeAsync(double delta, CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WidgetAudioSession? original;
            double desired;
            lock (_stateLock)
            {
                original = SelectedSessionLocked();
                if (original is null) return;
                desired = RoundVolume(original.Volume + delta);
                if (desired == original.Volume) return;
                ReplaceSessionLocked(original with { Volume = desired });
                _controlBusy = true;
                _pendingSessionId = original.SessionId;
                _pendingVolume = desired;
                _pendingMuted = null;
                _status = $"Setting {original.DisplayName} to {VolumePercent(desired)}%…";
                _statusIsError = false;
            }
            Invalidate();

            try
            {
                await HostServices.Audio.SetSessionVolumeAsync(
                    original.SessionId, desired, cancellationToken).ConfigureAwait(false);
                CompleteControl(original.SessionId,
                    $"{original.DisplayName} volume {VolumePercent(desired)}%", error: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RollbackControl(original, null);
                throw;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                RollbackControl(original, "Host audio control service unavailable");
            }
            catch (WidgetCapabilityException exception)
            {
                RollbackControl(original, MapControlFailure(exception.ErrorCode));
            }
            catch (Exception)
            {
                RollbackControl(original, "Volume change failed · previous value restored");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask ToggleMuteAsync(CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WidgetAudioSession? original;
            bool desired;
            lock (_stateLock)
            {
                original = SelectedSessionLocked();
                if (original is null) return;
                desired = !original.IsMuted;
                ReplaceSessionLocked(original with { IsMuted = desired });
                _controlBusy = true;
                _pendingSessionId = original.SessionId;
                _pendingVolume = null;
                _pendingMuted = desired;
                _status = desired
                    ? $"Muting {original.DisplayName}…"
                    : $"Unmuting {original.DisplayName}…";
                _statusIsError = false;
            }
            Invalidate();

            try
            {
                await HostServices.Audio.SetSessionMutedAsync(
                    original.SessionId, desired, cancellationToken).ConfigureAwait(false);
                CompleteControl(original.SessionId,
                    desired ? $"{original.DisplayName} muted" : $"{original.DisplayName} unmuted",
                    error: false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RollbackControl(original, null);
                throw;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                RollbackControl(original, "Host audio control service unavailable");
            }
            catch (WidgetCapabilityException exception)
            {
                RollbackControl(original, MapControlFailure(exception.ErrorCode));
            }
            catch (Exception)
            {
                RollbackControl(original, "Mute change failed · previous state restored");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private void CompleteControl(string sessionId, string status, bool error)
    {
        lock (_stateLock)
        {
            if (!string.Equals(_pendingSessionId, sessionId, StringComparison.Ordinal)) return;
            _controlBusy = false;
            _pendingSessionId = null;
            _pendingVolume = null;
            _pendingMuted = null;
            _status = status;
            _statusIsError = error;
        }
        Invalidate();
    }

    private void RollbackControl(WidgetAudioSession original, string? status)
    {
        lock (_stateLock)
        {
            if (string.Equals(_pendingSessionId, original.SessionId, StringComparison.Ordinal))
            {
                ReplaceSessionLocked(original);
                _controlBusy = false;
                _pendingSessionId = null;
                _pendingVolume = null;
                _pendingMuted = null;
                if (status is not null)
                {
                    _status = status;
                    _statusIsError = true;
                }
                else
                {
                    _status = _sessions.Count == 1
                        ? "1 audio session · live updates"
                        : $"{_sessions.Count} audio sessions · live updates";
                    _statusIsError = false;
                }
            }
        }
        Invalidate();
    }

    private WidgetAudioSession? SelectedSessionLocked()
    {
        if (_sessions.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _sessions.Count)
            return null;
        return _sessions[_selectedIndex];
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
            _selectedSessionId = null;
            _selectedIndex = 0;
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

    private static int VolumePercent(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 100, MidpointRounding.AwayFromZero);
}
