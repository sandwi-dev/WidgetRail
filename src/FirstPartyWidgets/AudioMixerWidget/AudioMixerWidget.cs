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
    private readonly SemaphoreSlim _outputCommandGate = new(1, 1);
    private IReadOnlyList<WidgetAudioSession> _sessions = [];
    private WidgetAudioOutput? _output;
    private AudioMixerViewState _viewState = AudioMixerViewState.Initial;
    private string? _selectedSessionId;
    private int _selectedIndex;
    private string _status = "Audio sessions load when this widget becomes visible";
    private bool _statusIsError;
    private bool _controlBusy;
    private bool _outputControlBusy;
    private string? _pendingSessionId;
    private double? _pendingVolume;
    private bool? _pendingMuted;
    private double? _pendingOutputVolume;
    private bool? _pendingOutputMuted;
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

    public int ActivationCount => Volatile.Read(ref _activationCount);
    public int FetchCount => Volatile.Read(ref _fetchCount);

    public override WidgetView Render()
    {
        AudioMixerViewState viewState;
        IReadOnlyList<WidgetAudioSession> sessions;
        WidgetAudioSession? selected;
        WidgetAudioOutput? output;
        int selectedIndex;
        string status;
        bool statusIsError;
        bool controlBusy;
        bool outputControlBusy;
        lock (_stateLock)
        {
            viewState = _viewState;
            sessions = _sessions;
            selectedIndex = _selectedIndex;
            selected = SelectedSessionLocked();
            output = _output;
            status = _status;
            statusIsError = _statusIsError;
            controlBusy = _controlBusy;
            outputControlBusy = _outputControlBusy;
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

        var masterPercent = VolumePercent(output.Volume);
        var masterDown = UI.Button("−", "output.volume.down", "audio.master.volume.down")
            .Disabled(outputControlBusy || output.Volume <= 0)
            .Busy(outputControlBusy)
            .FocusLeft("audio.master.volume.up")
            .FocusRight("audio.master.mute")
            .FocusDown(selected is null ? "audio.retry" : "audio.session.previous")
            .Classes("audio-volume-action", "audio-master-action");
        var masterMute = UI.Button(output.IsMuted ? "Unmute" : "Mute", "output.mute.toggle", "audio.master.mute")
            .Icon(output.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Volume,
                output.IsMuted ? "Unmute master output" : "Mute master output")
            .Selected(output.IsMuted)
            .Disabled(outputControlBusy)
            .Busy(outputControlBusy)
            .FocusLeft("audio.master.volume.down")
            .FocusRight("audio.master.volume.up")
            .FocusDown(selected is null ? "audio.retry" : "audio.mute")
            .Classes("audio-mute-action", "audio-master-mute", output.IsMuted ? "is-muted" : "is-audible");
        var masterUp = UI.Button("+", "output.volume.up", "audio.master.volume.up")
            .Disabled(outputControlBusy || output.Volume >= 1)
            .Busy(outputControlBusy)
            .FocusLeft("audio.master.mute")
            .FocusRight("audio.master.volume.down")
            .FocusDown(selected is null ? "audio.retry" : "audio.session.next")
            .Classes("audio-volume-action", "audio-master-action");
        var masterCard = UI.Stack("audio.master.card",
                UI.Row("audio.master.heading",
                    UI.Text("MASTER OUTPUT", "audio.master.label", "Master output").Classes("audio-master-label"),
                    UI.Text(output.IsMuted ? "MUTED" : "LIVE", "audio.master.state",
                        output.IsMuted ? "Master output muted" : "Master output audible")
                        .Classes("audio-master-state", output.IsMuted ? "is-muted" : "is-live"))
                    .Classes("audio-master-heading"),
                UI.Row("audio.master.volume.row",
                    UI.Progress(masterPercent, 100, "audio.master.volume.progress",
                            $"Master output volume {masterPercent} percent")
                        .Classes("audio-volume-progress", "audio-master-progress"),
                    UI.Text($"{masterPercent}%", "audio.master.volume.value",
                            $"Master output volume {masterPercent} percent")
                        .Classes("audio-volume-value"))
                    .Classes("audio-volume-row"),
                UI.Row("audio.master.controls", masterDown, masterMute, masterUp)
                    .Classes("audio-controls", "audio-master-controls"))
            .Classes("audio-master-card");

        if (selected is null)
        {
            var retry = UI.Button("Check again", "retry", "audio.retry")
                .Icon(WidgetGlyph.Refresh, "Check for application audio")
                .FocusUp("audio.master.mute")
                .Classes("audio-retry-action");
            var emptyRoot = UI.Stack("audio.root", header, masterCard,
                    UI.Stack("audio.state.card",
                        UI.Text("No application audio yet", "audio.state.title", "No application audio yet")
                            .Classes("audio-state-title"),
                        UI.Text("Start playback in an application. New sessions appear here automatically.",
                                "audio.state.help", "Start playback to create an application audio session")
                            .Classes("audio-help", "is-neutral"),
                        retry).Classes("audio-state-card"))
                .InputScope("audio-mixer")
                .Classes("audio-mixer-widget", "has-master", "has-state");
            return new WidgetView(emptyRoot, InitialFocusId: "audio.master.mute");
        }

        var hasMultipleSessions = sessions.Count > 1;
        var volumePercent = VolumePercent(selected.Volume);
        var previousButton = UI.Button("", "session.previous", "audio.session.previous")
            .Icon(WidgetGlyph.Previous, "Previous audio session")
            .Disabled(!hasMultipleSessions)
            .FocusRight("audio.session.next")
            .FocusDown("audio.volume.down")
            .FocusUp("audio.master.volume.down")
            .Classes("audio-session-action");
        var nextButton = UI.Button("", "session.next", "audio.session.next")
            .Icon(WidgetGlyph.Next, "Next audio session")
            .Disabled(!hasMultipleSessions)
            .FocusLeft("audio.session.previous")
            .FocusDown("audio.volume.up")
            .FocusUp("audio.master.volume.up")
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
                masterCard,
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

        return new WidgetView(root, InitialFocusId: "audio.master.mute", QuickActions: SessionQuickActions);
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
            case "output.volume.down":
                await ChangeOutputVolumeAsync(-VolumeStep, cancellationToken).ConfigureAwait(false);
                break;
            case "output.volume.up":
                await ChangeOutputVolumeAsync(VolumeStep, cancellationToken).ConfigureAwait(false);
                break;
            case "output.mute.toggle":
                await ToggleOutputMuteAsync(cancellationToken).ConfigureAwait(false);
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
            _outputControlBusy = false;
            _pendingSessionId = null;
            _pendingVolume = null;
            _pendingMuted = null;
            _pendingOutputVolume = null;
            _pendingOutputMuted = null;
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
            await Task.WhenAny(sessionObserver, outputObserver).ConfigureAwait(false);
            observers.Cancel();
            try { await Task.WhenAll(sessionObserver, outputObserver).ConfigureAwait(false); }
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

    private void ApplyOutput(WidgetAudioOutput incoming, long generation)
    {
        var normalized = new WidgetAudioOutput(
            double.IsFinite(incoming.Volume) ? Math.Clamp(incoming.Volume, 0, 1) : 0,
            incoming.IsMuted);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            if (_pendingOutputVolume is { } pendingVolume)
                normalized = normalized with { Volume = pendingVolume };
            if (_pendingOutputMuted is { } pendingMuted)
                normalized = normalized with { IsMuted = pendingMuted };
            _output = normalized;
            UpdateHealthyStateLocked();
        }
        Invalidate();
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
        if (!_controlBusy && !_outputControlBusy)
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

    private async ValueTask ChangeOutputVolumeAsync(double delta, CancellationToken cancellationToken)
    {
        await _outputCommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WidgetAudioOutput? original;
            double desired;
            lock (_stateLock)
            {
                original = _output;
                if (original is null) return;
                desired = RoundVolume(original.Volume + delta);
                if (desired == original.Volume) return;
                _output = original with { Volume = desired };
                _outputControlBusy = true;
                _pendingOutputVolume = desired;
                _pendingOutputMuted = null;
                _status = $"Setting master output to {VolumePercent(desired)}%…";
                _statusIsError = false;
            }
            Invalidate();
            try
            {
                await HostServices.Audio.SetOutputVolumeAsync(desired, cancellationToken)
                    .ConfigureAwait(false);
                CompleteOutputControl($"Master output volume {VolumePercent(desired)}%");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RollbackOutputControl(original, null);
                throw;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                RollbackOutputControl(original, "Host master-output control unavailable");
            }
            catch (WidgetCapabilityException exception)
            {
                RollbackOutputControl(original, MapControlFailure(exception.ErrorCode));
            }
            catch (Exception)
            {
                RollbackOutputControl(original, "Master volume change failed · previous value restored");
            }
        }
        finally
        {
            _outputCommandGate.Release();
        }
    }

    private async ValueTask ToggleOutputMuteAsync(CancellationToken cancellationToken)
    {
        await _outputCommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WidgetAudioOutput? original;
            bool desired;
            lock (_stateLock)
            {
                original = _output;
                if (original is null) return;
                desired = !original.IsMuted;
                _output = original with { IsMuted = desired };
                _outputControlBusy = true;
                _pendingOutputVolume = null;
                _pendingOutputMuted = desired;
                _status = desired ? "Muting master output…" : "Unmuting master output…";
                _statusIsError = false;
            }
            Invalidate();
            try
            {
                await HostServices.Audio.SetOutputMutedAsync(desired, cancellationToken)
                    .ConfigureAwait(false);
                CompleteOutputControl(desired ? "Master output muted" : "Master output unmuted");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RollbackOutputControl(original, null);
                throw;
            }
            catch (WidgetCapabilityUnavailableException)
            {
                RollbackOutputControl(original, "Host master-output control unavailable");
            }
            catch (WidgetCapabilityException exception)
            {
                RollbackOutputControl(original, MapControlFailure(exception.ErrorCode));
            }
            catch (Exception)
            {
                RollbackOutputControl(original, "Master mute change failed · previous state restored");
            }
        }
        finally
        {
            _outputCommandGate.Release();
        }
    }

    private void CompleteOutputControl(string status)
    {
        lock (_stateLock)
        {
            if (!_outputControlBusy) return;
            _outputControlBusy = false;
            _pendingOutputVolume = null;
            _pendingOutputMuted = null;
            _status = status;
            _statusIsError = false;
        }
        Invalidate();
    }

    private void RollbackOutputControl(WidgetAudioOutput original, string? status)
    {
        lock (_stateLock)
        {
            if (_outputControlBusy)
            {
                _output = original;
                _outputControlBusy = false;
                _pendingOutputVolume = null;
                _pendingOutputMuted = null;
                if (status is null) UpdateHealthyStateLocked();
                else
                {
                    _status = status;
                    _statusIsError = true;
                }
            }
        }
        Invalidate();
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
            _output = null;
            _selectedSessionId = null;
            _selectedIndex = 0;
            _controlBusy = false;
            _outputControlBusy = false;
            _pendingSessionId = null;
            _pendingVolume = null;
            _pendingMuted = null;
            _pendingOutputVolume = null;
            _pendingOutputMuted = null;
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

    private static int VolumePercent(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 100, MidpointRounding.AwayFromZero);
}
