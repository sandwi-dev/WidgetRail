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

/// <summary>
/// Event-driven, controller-first audio session mixer. The widget uses only the
/// typed host audio service: it never opens Core Audio objects or transport
/// handles itself.
/// </summary>
public sealed class AudioMixerWidget : Widget
{
    public const double VolumeStep = 0.05;
    private static readonly WidgetSurfaceHints CompactSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 520,
        PreferredHeight = 520,
        MinimumWidth = 320,
        MinimumHeight = 360,
    };

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _outputCommandGate = new(1, 1);
    private IReadOnlyList<WidgetAudioSession> _sessions = [];
    private IReadOnlyDictionary<string, SessionControlIds> _sessionControls =
        new Dictionary<string, SessionControlIds>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, SessionActionTarget> _sessionActions =
        new Dictionary<string, SessionActionTarget>(StringComparer.Ordinal);
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
        IReadOnlyDictionary<string, SessionControlIds> controlsBySessionId;
        WidgetAudioOutput? output;
        string status;
        bool statusIsError;
        bool controlBusy;
        string? pendingSessionId;
        bool outputControlBusy;
        lock (_stateLock)
        {
            viewState = _viewState;
            sessions = _sessions;
            controlsBySessionId = _sessionControls;
            output = _output;
            status = _status;
            statusIsError = _statusIsError;
            controlBusy = _controlBusy;
            pendingSessionId = _pendingSessionId;
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

        var sessionControls = sessions.Select(session => controlsBySessionId[session.SessionId]).ToArray();
        var firstControls = sessionControls.FirstOrDefault();

        var masterPercent = VolumePercent(output.Volume);
        var masterDown = UI.Button("−", "output.volume.down", "audio.master.volume.down")
            .Disabled(outputControlBusy || output.Volume <= 0)
            .Busy(outputControlBusy)
            .FocusLeft("audio.master.volume.up")
            .FocusRight("audio.master.mute")
            .Classes("audio-volume-action", "audio-master-action");
        var masterMute = UI.Button(output.IsMuted ? "Unmute" : "Mute", "output.mute.toggle", "audio.master.mute")
            .Icon(output.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Volume,
                output.IsMuted ? "Unmute master output" : "Mute master output")
            .Selected(output.IsMuted)
            .Disabled(outputControlBusy)
            .Busy(outputControlBusy)
            .FocusLeft("audio.master.volume.down")
            .FocusRight("audio.master.volume.up")
            .Classes("audio-mute-action", "audio-master-mute", output.IsMuted ? "is-muted" : "is-audible");
        var masterUp = UI.Button("+", "output.volume.up", "audio.master.volume.up")
            .Disabled(outputControlBusy || output.Volume >= 1)
            .Busy(outputControlBusy)
            .FocusLeft("audio.master.mute")
            .FocusRight("audio.master.volume.down")
            .Classes("audio-volume-action", "audio-master-action");

        if (firstControls is not null)
        {
            masterDown = masterDown.FocusDown(firstControls.VolumeDown);
            masterMute = masterMute.FocusDown(firstControls.Mute);
            masterUp = masterUp.FocusDown(firstControls.VolumeUp);
        }
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

        if (sessions.Count == 0)
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
            return new WidgetView(emptyRoot, InitialFocusId: "audio.master.mute", Surface: CompactSurface);
        }

        var sessionRows = new WidgetElement[sessions.Count];
        for (var index = 0; index < sessions.Count; index++)
        {
            var previous = index == 0 ? null : sessionControls[index - 1];
            var next = index + 1 == sessions.Count ? null : sessionControls[index + 1];
            sessionRows[index] = RenderSessionRow(
                sessions[index], index, sessions.Count, sessionControls[index], previous, next,
                controlBusy, pendingSessionId);
        }

        var root = UI.Stack("audio.root",
                header,
                masterCard,
                UI.Row("audio.sessions.heading",
                    UI.Text("APPLICATIONS", "audio.sessions.label", "Application volume mixer")
                        .Classes("audio-sessions-label"),
                    UI.Text($"{sessions.Count} apps", "audio.sessions.count",
                            $"{sessions.Count} application audio sessions")
                        .Classes("audio-sessions-count"))
                    .Classes("audio-sessions-heading"),
                UI.VerticalScroll("audio.sessions.scroll", sessionRows)
                    .Classes("audio-session-list"))
            .InputScope("audio-mixer")
            .Classes("audio-mixer-widget", "has-sessions");

        return new WidgetView(root, InitialFocusId: "audio.master.mute", Surface: CompactSurface);
    }

    private static StackElement RenderSessionRow(
        WidgetAudioSession session,
        int index,
        int count,
        SessionControlIds controls,
        SessionControlIds? previous,
        SessionControlIds? next,
        bool controlBusy,
        string? pendingSessionId)
    {
        var isPending = controlBusy &&
            string.Equals(session.SessionId, pendingSessionId, StringComparison.Ordinal);
        var percent = VolumePercent(session.Volume);
        var volumeDown = UI.Button("−", controls.VolumeDown, controls.VolumeDown)
            .Disabled(controlBusy || session.Volume <= 0)
            .Busy(isPending)
            .FocusLeft(controls.VolumeUp)
            .FocusRight(controls.Mute)
            .FocusUp(previous?.VolumeDown ?? "audio.master.volume.down")
            .Classes("audio-volume-action", "audio-session-volume-action");
        var mute = UI.Button(session.IsMuted ? "Unmute" : "Mute", controls.Mute, controls.Mute)
            .Icon(session.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Volume,
                session.IsMuted ? $"Unmute {session.DisplayName}" : $"Mute {session.DisplayName}")
            .Selected(session.IsMuted)
            .Busy(isPending)
            .Disabled(controlBusy)
            .FocusLeft(controls.VolumeDown)
            .FocusRight(controls.VolumeUp)
            .FocusUp(previous?.Mute ?? "audio.master.mute")
            .Classes("audio-mute-action", "audio-session-mute-action",
                session.IsMuted ? "is-muted" : "is-audible");
        var volumeUp = UI.Button("+", controls.VolumeUp, controls.VolumeUp)
            .Disabled(controlBusy || session.Volume >= 1)
            .Busy(isPending)
            .FocusLeft(controls.Mute)
            .FocusRight(controls.VolumeDown)
            .FocusUp(previous?.VolumeUp ?? "audio.master.volume.up")
            .Classes("audio-volume-action", "audio-session-volume-action");

        if (next is not null)
        {
            volumeDown = volumeDown.FocusDown(next.VolumeDown);
            mute = mute.FocusDown(next.Mute);
            volumeUp = volumeUp.FocusDown(next.VolumeUp);
        }

        return UI.Stack(controls.Row,
                UI.Row(controls.Heading,
                    UI.Stack(controls.Details,
                        UI.Text(session.DisplayName, controls.Name, session.DisplayName)
                            .Classes("audio-session-name"),
                        UI.Text($"APP {index + 1} OF {count}", controls.Position,
                                $"Application audio session {index + 1} of {count}")
                            .Classes("audio-session-count"))
                        .Classes("audio-session-details"),
                    UI.Text(session.IsActive ? "ACTIVE" : "IDLE", controls.State,
                            session.IsActive ? $"{session.DisplayName} audio is active" : $"{session.DisplayName} audio is idle")
                        .Classes("audio-session-state", session.IsActive ? "is-active" : "is-idle"))
                    .Classes("audio-session-heading"),
                UI.Row(controls.VolumeRow,
                    UI.Progress(percent, 100, controls.Progress,
                            $"{session.DisplayName} volume {percent} percent")
                        .Classes("audio-volume-progress"),
                    UI.Text($"{percent}%", controls.Value, $"{session.DisplayName} volume {percent} percent")
                        .Classes("audio-volume-value"))
                    .Classes("audio-volume-row"),
                UI.Row(controls.Controls, volumeDown, mute, volumeUp)
                    .Classes("audio-controls", "audio-session-controls"))
            .Classes("audio-session-card", isPending ? "is-pending" : "is-ready");
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
                case SessionAction.VolumeDown:
                    await ChangeVolumeAsync(sessionId, -VolumeStep, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionAction.VolumeUp:
                    await ChangeVolumeAsync(sessionId, VolumeStep, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionAction.ToggleMute:
                    await ToggleMuteAsync(sessionId, cancellationToken).ConfigureAwait(false);
                    break;
            }
            return;
        }

        switch (action.ActionId)
        {
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
        var (controls, actions) = BuildSessionRouting(normalized);
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
            actions.Add(ids.VolumeDown, new SessionActionTarget(session.SessionId, SessionAction.VolumeDown));
            actions.Add(ids.Mute, new SessionActionTarget(session.SessionId, SessionAction.ToggleMute));
            actions.Add(ids.VolumeUp, new SessionActionTarget(session.SessionId, SessionAction.VolumeUp));
        }
        return (controls, actions);
    }

    private async ValueTask ChangeVolumeAsync(
        string sessionId,
        double delta,
        CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WidgetAudioSession? original;
            double desired;
            lock (_stateLock)
            {
                original = FindSessionLocked(sessionId);
                if (original is null) return;
                SelectSessionLocked(original.SessionId);
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

    private async ValueTask ToggleMuteAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WidgetAudioSession? original;
            bool desired;
            lock (_stateLock)
            {
                original = FindSessionLocked(sessionId);
                if (original is null) return;
                SelectSessionLocked(original.SessionId);
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

    private enum SessionAction
    {
        VolumeDown,
        VolumeUp,
        ToggleMute,
    }

    private sealed record SessionActionTarget(string SessionId, SessionAction Action);

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
            VolumeRow = $"{prefix}.volume.row";
            Progress = $"{prefix}.volume.progress";
            Value = $"{prefix}.volume.value";
            Controls = $"{prefix}.controls";
            VolumeDown = $"{prefix}.volume.down";
            Mute = $"{prefix}.mute";
            VolumeUp = $"{prefix}.volume.up";
        }

        public string Row { get; }
        public string Heading { get; }
        public string Details { get; }
        public string Name { get; }
        public string Position { get; }
        public string State { get; }
        public string VolumeRow { get; }
        public string Progress { get; }
        public string Value { get; }
        public string Controls { get; }
        public string VolumeDown { get; }
        public string Mute { get; }
        public string VolumeUp { get; }

        public static SessionControlIds For(WidgetAudioSession session) => For(session.SessionId);

        private static SessionControlIds For(string sessionId)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
            return new SessionControlIds($"audio.session.{Convert.ToHexString(hash).ToLowerInvariant()}");
        }
    }
}
