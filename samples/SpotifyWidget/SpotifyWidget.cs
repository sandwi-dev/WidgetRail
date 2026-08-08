using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

public enum SpotifyWidgetViewState
{
    Initial,
    Loading,
    Unconfigured,
    Disconnected,
    Authorizing,
    Ready,
    PermissionDenied,
    ServiceUnavailable,
    Error,
}

/// <summary>
/// Controller-first Spotify community widget. All Spotify access crosses the
/// public typed SDK; the widget never receives OAuth tokens or a client secret.
/// </summary>
public sealed class SpotifyWidget : Widget
{
    private const string InputScope = "spotify.window";
    private const string SetupScope = "spotify.setup";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SetupRefreshTimeout = TimeSpan.FromSeconds(5);
    private static readonly WidgetSurfaceHints StandardSurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 760,
        PreferredHeight = 460,
        MinimumWidth = 620,
        MinimumHeight = 400,
    };
    private static readonly WidgetQuickActionCapability PlaybackControlAuthority = new(
        WidgetSpotifyCapabilities.PlaybackControlCapabilityId,
        WidgetSpotifyCapabilities.PlaybackControlOperationId);
    private static readonly IReadOnlyList<WidgetSpotifyAuthorizationScope> PlaybackScopes =
    [
        WidgetSpotifyAuthorizationScope.PlaybackStateRead,
        WidgetSpotifyAuthorizationScope.PlaybackStateControl,
    ];

    private readonly object _gate = new();
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private SpotifyWidgetViewState _viewState = SpotifyWidgetViewState.Initial;
    private WidgetSpotifyAuthorizationState _authorizationState =
        WidgetSpotifyAuthorizationState.Disconnected;
    private WidgetSpotifyPlaybackSummary? _playback;
    private WidgetSpotifyPlaybackOperation? _pendingOperation;
    private string _status = "Spotify loads when this widget becomes visible";
    private bool _showSetup;
    private long _activeGeneration;
    private Task? _pollTask;
    private Task? _progressTask;

    public SpotifyWidget(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public SpotifyWidgetViewState ViewState { get { lock (_gate) return _viewState; } }
    public string Status { get { lock (_gate) return _status; } }
    public WidgetSpotifyPlaybackSummary? Playback { get { lock (_gate) return _playback; } }

    public override WidgetView Render()
    {
        SpotifyWidgetViewState state;
        WidgetSpotifyPlaybackSummary? playback;
        WidgetSpotifyPlaybackOperation? pending;
        string status;
        bool showSetup;
        lock (_gate)
        {
            state = _viewState;
            playback = ProjectPlayback(_playback);
            pending = _pendingOperation;
            status = _status;
            showSetup = _showSetup;
        }

        if (showSetup) return RenderSetup(status);
        var header = Header(status, state);
        return state switch
        {
            SpotifyWidgetViewState.Unconfigured => RenderUnconfigured(header),
            SpotifyWidgetViewState.Disconnected => RenderDisconnected(header, false),
            SpotifyWidgetViewState.Authorizing => RenderAuthorizing(header),
            SpotifyWidgetViewState.PermissionDenied => RenderPermissionDenied(header),
            SpotifyWidgetViewState.ServiceUnavailable => RenderError(
                header, "Spotify service unavailable",
                "The trusted Spotify provider is not available. Try again after the host recovers."),
            SpotifyWidgetViewState.Error => RenderError(
                header, "Spotify could not be loaded",
                "The provider returned an unexpected error. Retry without leaving the overlay."),
            SpotifyWidgetViewState.Ready when playback is { IsAvailable: true, Item: not null } =>
                RenderNowPlaying(header, playback, pending),
            SpotifyWidgetViewState.Ready => RenderIdle(header),
            _ => RenderLoading(header),
        };
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        var generation = Interlocked.Increment(ref _activeGeneration);
        _pollTask = RunPeriodicUpdatesWhileActiveAsync(
            PollInterval,
            cancellationToken => PollAsync(generation, cancellationToken),
            tickImmediately: true,
            invalidateAfterTick: false);
        _progressTask = RunPeriodicUpdatesWhileActiveAsync(
            ProgressInterval,
            _ =>
            {
                lock (_gate)
                {
                    if (_viewState != SpotifyWidgetViewState.Ready ||
                        _playback?.IsPlaying != true) return ValueTask.CompletedTask;
                }
                Invalidate();
                return ValueTask.CompletedTask;
            },
            invalidateAfterTick: false);
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        Interlocked.Increment(ref _activeGeneration);
        var tasks = new[] { _pollTask, _progressTask }
            .Where(task => task is not null).Cast<Task>().ToArray();
        _pollTask = null;
        _progressTask = null;
        if (tasks.Length != 0)
            await Task.WhenAll(tasks).WaitAsync(transitionToken).ConfigureAwait(false);
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        if (previous is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive ||
            current is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive)
            Invalidate();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _actionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            switch (action.ActionId)
            {
                case "spotify.setup.open":
                    lock (_gate) _showSetup = true;
                    Invalidate();
                    return;
                case "spotify.setup.close":
                    lock (_gate) _showSetup = false;
                    Invalidate();
                    return;
                case "spotify.setup.done":
                    lock (_gate) _showSetup = false;
                    Invalidate();
                    if (!IsActive) return;
                    using (var refreshLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                               cancellationToken, ActiveCancellationToken))
                    {
                        refreshLifetime.CancelAfter(SetupRefreshTimeout);
                        try
                        {
                            await RefreshAsync(refreshLifetime.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (
                            refreshLifetime.IsCancellationRequested) { }
                        if (refreshLifetime.IsCancellationRequested &&
                            !cancellationToken.IsCancellationRequested &&
                            !ActiveCancellationToken.IsCancellationRequested)
                            SetState(Volatile.Read(ref _activeGeneration),
                                SpotifyWidgetViewState.ServiceUnavailable,
                                "Spotify configuration refresh timed out", null);
                    }
                    return;
                case "spotify.connect":
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return;
                case "spotify.disconnect":
                    await DisconnectAsync(cancellationToken).ConfigureAwait(false);
                    return;
                case "spotify.retry":
                case "spotify.refresh":
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                    return;
                case "spotify.play-toggle":
                    await ExecuteAsync(ResolveToggleOperation(), null, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                case "spotify.previous":
                    await ExecuteAsync(WidgetSpotifyPlaybackOperation.Previous, null,
                        cancellationToken).ConfigureAwait(false);
                    return;
                case "spotify.next":
                    await ExecuteAsync(WidgetSpotifyPlaybackOperation.Next, null,
                        cancellationToken).ConfigureAwait(false);
                    return;
                case "spotify.shuffle":
                    await ExecuteAsync(WidgetSpotifyPlaybackOperation.SetShuffle, null,
                        cancellationToken).ConfigureAwait(false);
                    return;
                case "spotify.repeat":
                    await ExecuteAsync(WidgetSpotifyPlaybackOperation.SetRepeat, null,
                        cancellationToken).ConfigureAwait(false);
                    return;
                case "spotify.seek":
                    if (action.RequestedValue is { } requested && double.IsFinite(requested))
                        await ExecuteAsync(WidgetSpotifyPlaybackOperation.Seek,
                            Math.Max(0, (long)Math.Round(requested)), cancellationToken)
                            .ConfigureAwait(false);
                    return;
            }
        }
        finally
        {
            _actionGate.Release();
        }
    }

    private async ValueTask PollAsync(long generation, CancellationToken cancellationToken)
    {
        if (generation != Volatile.Read(ref _activeGeneration)) return;
        await RefreshCoreAsync(generation, cancellationToken, loading: false).ConfigureAwait(false);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _activeGeneration);
        await RefreshCoreAsync(generation, cancellationToken, loading: true).ConfigureAwait(false);
    }

    private async Task RefreshCoreAsync(
        long generation,
        CancellationToken cancellationToken,
        bool loading)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _activeGeneration)) return;
            await RefreshCoreSingleAsync(generation, cancellationToken, loading)
                .ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreSingleAsync(
        long generation,
        CancellationToken cancellationToken,
        bool loading)
    {
        if (loading)
        {
            lock (_gate) _status = "Refreshing Spotify…";
            Invalidate();
        }
        try
        {
            var configuration = await HostServices.Spotify.GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!configuration.IsConfigured)
            {
                SetState(generation, SpotifyWidgetViewState.Unconfigured,
                    "Add your Spotify developer Client ID to continue", null);
                return;
            }

            var authorization = await HostServices.Spotify.GetAuthorizationAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_gate) _authorizationState = authorization.State;
            switch (authorization.State)
            {
                case WidgetSpotifyAuthorizationState.Unconfigured:
                    SetState(generation, SpotifyWidgetViewState.Unconfigured,
                        "Add your Spotify developer Client ID to continue", null);
                    return;
                case WidgetSpotifyAuthorizationState.Disconnected:
                case WidgetSpotifyAuthorizationState.ReauthorizationRequired:
                    SetState(generation, SpotifyWidgetViewState.Disconnected,
                        authorization.DisplayMessage ?? (authorization.State ==
                            WidgetSpotifyAuthorizationState.ReauthorizationRequired
                                ? "Spotify needs you to reconnect"
                                : "Connect your Spotify account when you are ready"), null);
                    return;
                case WidgetSpotifyAuthorizationState.Authorizing:
                    SetState(generation, SpotifyWidgetViewState.Authorizing,
                        "Finish signing in through your browser", null);
                    return;
            }

            var playback = await HostServices.Spotify.GetPlaybackAsync(cancellationToken)
                .ConfigureAwait(false);
            SetState(generation, SpotifyWidgetViewState.Ready,
                playback.IsAvailable ? "Live from Spotify" : "Connected · no active playback",
                playback);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            SetState(generation, SpotifyWidgetViewState.ServiceUnavailable,
                "Spotify provider unavailable", null);
        }
        catch (WidgetCapabilityException exception)
        {
            var permission = exception.ErrorCode is "permission_denied" or "capability_revoked";
            var disconnected = exception.ErrorCode is "authorization_expired" or
                "authorization_scope_required";
            SetState(generation,
                permission ? SpotifyWidgetViewState.PermissionDenied :
                disconnected ? SpotifyWidgetViewState.Disconnected : SpotifyWidgetViewState.Error,
                SafeMessage(exception, permission ? "Spotify permission is off" :
                    disconnected ? "Spotify needs you to reconnect" : "Spotify update failed"),
                disconnected ? null : Playback);
        }
        catch (Exception)
        {
            SetState(generation, SpotifyWidgetViewState.Error,
                "Spotify update failed", Playback);
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _viewState = SpotifyWidgetViewState.Authorizing;
            _status = "Opening Spotify sign-in…";
        }
        Invalidate();
        try
        {
            var authorization = await HostServices.Spotify.ConnectAsync(
                PlaybackScopes, cancellationToken).ConfigureAwait(false);
            lock (_gate) _authorizationState = authorization.State;
            if (authorization.State != WidgetSpotifyAuthorizationState.Connected)
            {
                SetState(Volatile.Read(ref _activeGeneration),
                    SpotifyWidgetViewState.Disconnected,
                    authorization.DisplayMessage ?? "Spotify connection was not completed", null);
                return;
            }
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityException exception)
        {
            SetState(Volatile.Read(ref _activeGeneration),
                exception.ErrorCode is "permission_denied" or "capability_revoked"
                    ? SpotifyWidgetViewState.PermissionDenied
                    : SpotifyWidgetViewState.Disconnected,
                SafeMessage(exception, "Spotify connection was not completed"), null);
        }
        catch (WidgetCapabilityUnavailableException)
        {
            SetState(Volatile.Read(ref _activeGeneration),
                SpotifyWidgetViewState.ServiceUnavailable, "Spotify provider unavailable", null);
        }
    }

    private async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await HostServices.Spotify.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            SetState(Volatile.Read(ref _activeGeneration), SpotifyWidgetViewState.Disconnected,
                "Disconnected from Spotify", null);
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandStatus(SafeMessage(exception, "Spotify could not disconnect"));
        }
    }

    private async Task ExecuteAsync(
        WidgetSpotifyPlaybackOperation operation,
        long? requestedPosition,
        CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaybackSummary? before;
        WidgetSpotifyPlaybackCommand? command;
        lock (_gate)
        {
            before = _playback;
            command = BuildCommand(before, operation, requestedPosition);
            if (command is null || _pendingOperation is not null)
            {
                _status = "That Spotify control is not available";
                Invalidate();
                return;
            }
            _pendingOperation = operation;
            _playback = ApplyOptimistic(before!, command);
            _status = OperationStatus(operation);
        }
        Invalidate();
        try
        {
            await HostServices.Spotify.ControlPlaybackAsync(command, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                _pendingOperation = null;
                _status = "Updated in Spotify";
            }
            Invalidate();
            await RefreshCoreAsync(Volatile.Read(ref _activeGeneration), cancellationToken,
                loading: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreOptimistic(before);
        }
        catch (WidgetCapabilityException exception)
        {
            RestoreOptimistic(before, SafeMessage(exception,
                exception.ErrorCode is "permission_denied" or "capability_revoked"
                    ? "Playback control permission is off"
                    : "Spotify rejected that control"));
        }
        catch (WidgetCapabilityUnavailableException)
        {
            RestoreOptimistic(before, "Spotify playback control is unavailable");
        }
    }

    private void RestoreOptimistic(WidgetSpotifyPlaybackSummary? playback, string? status = null)
    {
        lock (_gate)
        {
            _playback = playback;
            _pendingOperation = null;
            if (status is not null) _status = status;
        }
        Invalidate();
    }

    private WidgetSpotifyPlaybackOperation ResolveToggleOperation()
    {
        lock (_gate) return _playback?.IsPlaying == true
            ? WidgetSpotifyPlaybackOperation.Pause
            : WidgetSpotifyPlaybackOperation.Play;
    }

    private static WidgetSpotifyPlaybackCommand? BuildCommand(
        WidgetSpotifyPlaybackSummary? playback,
        WidgetSpotifyPlaybackOperation operation,
        long? requestedPosition)
    {
        if (playback is not { IsAvailable: true }) return null;
        var blocked = playback.DisallowedActions;
        return operation switch
        {
            WidgetSpotifyPlaybackOperation.Play when !blocked.Resuming => new(operation),
            WidgetSpotifyPlaybackOperation.Pause when !blocked.Pausing => new(operation),
            WidgetSpotifyPlaybackOperation.Next when !blocked.SkippingNext => new(operation),
            WidgetSpotifyPlaybackOperation.Previous when !blocked.SkippingPrevious => new(operation),
            WidgetSpotifyPlaybackOperation.Seek when !blocked.Seeking && requestedPosition is { } value =>
                new(operation, PositionMilliseconds: Math.Clamp(value, 0,
                    Math.Max(0, playback.DurationMilliseconds))),
            WidgetSpotifyPlaybackOperation.SetShuffle when !blocked.TogglingShuffle =>
                new(operation, Enabled: !playback.ShuffleState),
            WidgetSpotifyPlaybackOperation.SetRepeat => NextRepeatCommand(playback),
            _ => null,
        };
    }

    private static WidgetSpotifyPlaybackCommand? NextRepeatCommand(
        WidgetSpotifyPlaybackSummary playback)
    {
        var blocked = playback.DisallowedActions;
        var next = playback.RepeatState switch
        {
            WidgetSpotifyRepeatState.Off when !blocked.TogglingRepeatContext =>
                WidgetSpotifyRepeatState.Context,
            WidgetSpotifyRepeatState.Context when !blocked.TogglingRepeatTrack =>
                WidgetSpotifyRepeatState.Track,
            WidgetSpotifyRepeatState.Track => WidgetSpotifyRepeatState.Off,
            _ => (WidgetSpotifyRepeatState?)null,
        };
        return next is null ? null : new(
            WidgetSpotifyPlaybackOperation.SetRepeat, RepeatState: next);
    }

    private WidgetSpotifyPlaybackSummary ApplyOptimistic(
        WidgetSpotifyPlaybackSummary playback,
        WidgetSpotifyPlaybackCommand command)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var projected = ProjectPlayback(playback)!;
        return command.Operation switch
        {
            WidgetSpotifyPlaybackOperation.Play => projected with
            {
                IsPlaying = true,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.Pause => projected with
            {
                IsPlaying = false,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.Seek => projected with
            {
                ProgressMilliseconds = command.PositionMilliseconds!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.SetShuffle => projected with
            {
                ShuffleState = command.Enabled!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.SetRepeat => projected with
            {
                RepeatState = command.RepeatState!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            _ => projected,
        };
    }

    private WidgetSpotifyPlaybackSummary? ProjectPlayback(WidgetSpotifyPlaybackSummary? playback)
    {
        if (playback is not { IsAvailable: true, IsPlaying: true } ||
            playback.DurationMilliseconds <= 0) return playback;
        var elapsed = Math.Max(0, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() -
            playback.CapturedAtUnixMilliseconds);
        return playback with
        {
            ProgressMilliseconds = Math.Clamp(playback.ProgressMilliseconds + elapsed,
                0, playback.DurationMilliseconds),
            CapturedAtUnixMilliseconds = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
        };
    }

    private void SetState(
        long generation,
        SpotifyWidgetViewState state,
        string status,
        WidgetSpotifyPlaybackSummary? playback)
    {
        if (generation != Volatile.Read(ref _activeGeneration)) return;
        lock (_gate)
        {
            _viewState = state;
            _status = status;
            _playback = playback;
            _pendingOperation = null;
        }
        Invalidate();
    }

    private void SetCommandStatus(string status)
    {
        lock (_gate) _status = status;
        Invalidate();
    }

    private static string SafeMessage(Exception exception, string fallback)
    {
        var message = exception.Message?.Trim();
        return string.IsNullOrWhiteSpace(message) || message.Length > 160 ||
            message.Any(char.IsControl) ? fallback : message;
    }

    private static string OperationStatus(WidgetSpotifyPlaybackOperation operation) => operation switch
    {
        WidgetSpotifyPlaybackOperation.Play => "Resuming…",
        WidgetSpotifyPlaybackOperation.Pause => "Pausing…",
        WidgetSpotifyPlaybackOperation.Next => "Skipping forward…",
        WidgetSpotifyPlaybackOperation.Previous => "Going back…",
        WidgetSpotifyPlaybackOperation.Seek => "Seeking…",
        WidgetSpotifyPlaybackOperation.SetShuffle => "Updating shuffle…",
        WidgetSpotifyPlaybackOperation.SetRepeat => "Updating repeat…",
        _ => "Updating Spotify…",
    };

    private static StackElement Header(string status, SpotifyWidgetViewState state) =>
        UI.Stack("spotify.header",
                UI.Text("SPOTIFY", "spotify.eyebrow", "Spotify").Classes("spotify-eyebrow"),
                UI.Row("spotify.heading",
                        UI.Text("Music", "spotify.title", "Spotify music").Classes("spotify-title"),
                        UI.Text(status, "spotify.status", status).Classes("spotify-status",
                            state is SpotifyWidgetViewState.Error or
                                SpotifyWidgetViewState.PermissionDenied
                                ? "is-error" : state == SpotifyWidgetViewState.Ready
                                    ? "is-live" : "is-neutral"))
                    .Classes("spotify-heading"))
            .Classes("spotify-header");

    private static WidgetView RenderLoading(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Loading Spotify", "Checking your local account state…"))
            .InputScope(InputScope).Classes("spotify-widget"),
        Surface: StandardSurface);

    private static WidgetView RenderUnconfigured(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Settings, "Client ID required",
                    "Add your own Spotify developer Client ID. No client secret belongs in this widget.",
                    UI.Button("Setup instructions", "spotify.setup.open", "spotify.setup.open")
                        .Icon(WidgetGlyph.Settings, "Open Spotify setup instructions")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.setup.open", Surface: StandardSurface);

    private static WidgetView RenderDisconnected(StackElement header, bool reconnect) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, reconnect ? "Reconnect Spotify" : "Connect Spotify",
                    "Sign in with Spotify using Authorization Code with PKCE. Your token stays in the trusted host.",
                    UI.Row("spotify.connect-actions",
                            UI.Button(reconnect ? "Reconnect" : "Connect", "spotify.connect",
                                    "spotify.connect")
                                .Icon(WidgetGlyph.Play, "Connect Spotify account")
                                .FocusRight("spotify.setup.open").Classes("spotify-primary"),
                            UI.Button("Setup", "spotify.setup.open", "spotify.setup.open")
                                .Icon(WidgetGlyph.Settings, "Open setup instructions")
                                .FocusLeft("spotify.connect").Classes("spotify-secondary"))
                        .Classes("spotify-connect-actions")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.connect", Surface: StandardSurface);

    private static WidgetView RenderAuthorizing(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Finish in your browser",
                    "The overlay is waiting for Spotify. Canceling or closing the browser will leave you disconnected."))
            .InputScope(InputScope).Classes("spotify-widget"),
        Surface: StandardSurface);

    private static WidgetView RenderPermissionDenied(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Settings, "Spotify permission is off",
                    "Allow the declared Spotify capabilities in Settings, then retry.",
                    UI.Button("Retry", "spotify.retry", "spotify.retry")
                        .Icon(WidgetGlyph.Refresh, "Retry Spotify")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.retry", Surface: StandardSurface);

    private static WidgetView RenderError(StackElement header, string title, string detail) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, title, detail,
                    UI.Button("Try again", "spotify.retry", "spotify.retry")
                        .Icon(WidgetGlyph.Refresh, "Try Spotify again")
                        .Classes("spotify-primary")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.retry", Surface: StandardSurface);

    private static WidgetView RenderIdle(StackElement header) => new(
        UI.Stack("spotify.root", header,
                StateCard(WidgetGlyph.Music, "Nothing playing",
                    "Start playback in Spotify on one of your devices, then refresh here.",
                    UI.Row("spotify.idle-actions",
                            UI.Button("Refresh", "spotify.refresh", "spotify.refresh")
                                .Icon(WidgetGlyph.Refresh, "Refresh Spotify playback")
                                .FocusRight("spotify.disconnect").Classes("spotify-primary"),
                            UI.Button("Disconnect", "spotify.disconnect", "spotify.disconnect")
                                .FocusLeft("spotify.refresh").Classes("spotify-secondary"))
                        .Classes("spotify-connect-actions")))
            .InputScope(InputScope).Classes("spotify-widget"),
        InitialFocusId: "spotify.refresh", Surface: StandardSurface);

    private static WidgetView RenderSetup(string status)
    {
        var root = UI.Stack("spotify.setup-root",
                Header(status, SpotifyWidgetViewState.Unconfigured),
                UI.Stack("spotify.setup-card",
                        UI.Text("Connect your developer app", "spotify.setup-title",
                                "Spotify developer app setup")
                            .Classes("spotify-state-title"),
                        UI.Text("1. Create an app in the Spotify developer dashboard.",
                                "spotify.setup-step-1").Classes("spotify-setup-step"),
                        UI.Text($"2. Register this exact redirect URI: {WidgetSpotifyService.ExactRedirectUri}",
                                "spotify.setup-step-2").Classes("spotify-setup-step"),
                        UI.Text("3. Configure only its public Client ID; never paste a Client Secret.",
                                "spotify.setup-step-3").Classes("spotify-setup-step"),
                        UI.Text("CLI: dotnet run --project .\\tools\\GbarCli\\GbarCli.csproj -- config set org.gbar.samples.spotify client-id YOUR_CLIENT_ID --publisher org.gbar.samples",
                                "spotify.setup-command", "Client ID configuration command")
                            .Classes("spotify-setup-command"),
                        UI.Button("Done", "spotify.setup.done", "spotify.setup.done")
                            .Classes("spotify-primary"))
                    .Classes("spotify-setup-card"))
            .InputScope(SetupScope)
            .Shortcut(ControllerButton.B, "spotify.setup.close")
            .Classes("spotify-widget", "spotify-setup");
        return new WidgetView(root, "spotify.setup.done", ActiveInputScopeId: SetupScope,
            Surface: StandardSurface);
    }

    private static WidgetView RenderNowPlaying(
        StackElement header,
        WidgetSpotifyPlaybackSummary playback,
        WidgetSpotifyPlaybackOperation? pending)
    {
        var item = playback.Item!;
        var duration = Math.Max(1, playback.DurationMilliseconds);
        var position = Math.Clamp(playback.ProgressMilliseconds, 0, duration);
        var disallowed = playback.DisallowedActions;
        var toggleBlocked = playback.IsPlaying ? disallowed.Pausing : disallowed.Resuming;
        WidgetElement artwork = string.IsNullOrWhiteSpace(item.ArtworkUrl)
            ? UI.Icon(WidgetGlyph.Music, "spotify.artwork-placeholder", "No artwork")
                .Classes("spotify-artwork-placeholder")
            : UI.Image(item.ArtworkUrl, "spotify.artwork", $"Artwork for {item.Title}", ImageFit.Cover)
                .Classes("spotify-artwork");

        var previous = UI.Button("", "spotify.previous", "spotify.previous")
            .Icon(WidgetGlyph.Previous, disallowed.SkippingPrevious
                ? "Previous unavailable" : "Previous track")
            .Disabled(disallowed.SkippingPrevious || pending is not null)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Previous)
            .FocusRight("spotify.play-toggle").FocusDown("spotify.shuffle")
            .Classes("spotify-transport");
        var toggle = UI.Button("", "spotify.play-toggle", "spotify.play-toggle")
            .Icon(playback.IsPlaying ? WidgetGlyph.Pause : WidgetGlyph.Play,
                toggleBlocked ? "Play or pause unavailable" : playback.IsPlaying ? "Pause" : "Play")
            .Disabled(toggleBlocked || pending is not null)
            .Busy(pending is WidgetSpotifyPlaybackOperation.Play or
                WidgetSpotifyPlaybackOperation.Pause)
            .FocusLeft("spotify.previous").FocusRight("spotify.next")
            .FocusDown("spotify.seek").Classes("spotify-play",
                playback.IsPlaying ? "is-playing" : "is-paused");
        var next = UI.Button("", "spotify.next", "spotify.next")
            .Icon(WidgetGlyph.Next, disallowed.SkippingNext ? "Next unavailable" : "Next track")
            .Disabled(disallowed.SkippingNext || pending is not null)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Next)
            .FocusLeft("spotify.play-toggle").FocusDown("spotify.repeat")
            .Classes("spotify-transport");
        var shuffle = UI.Button("", "spotify.shuffle", "spotify.shuffle")
            .Icon(WidgetGlyph.Shuffle, playback.ShuffleState ? "Turn shuffle off" : "Turn shuffle on")
            .Selected(playback.ShuffleState)
            .Disabled(disallowed.TogglingShuffle || pending is not null)
            .Busy(pending == WidgetSpotifyPlaybackOperation.SetShuffle)
            .FocusUp("spotify.previous").FocusRight("spotify.seek")
            .Classes("spotify-secondary-action", playback.ShuffleState ? "is-active" : "is-inactive");
        var seek = UI.Slider(position, 0, duration, 5_000, "spotify.seek", "spotify.seek",
                "Playback position", $"{FormatTime(position)} of {FormatTime(duration)}")
            .Disabled(disallowed.Seeking || pending is not null)
            .Busy(pending == WidgetSpotifyPlaybackOperation.Seek)
            .FocusUp("spotify.play-toggle").FocusDown("spotify.refresh")
            .Classes("spotify-seek");
        var repeat = UI.Button("", "spotify.repeat", "spotify.repeat")
            .Icon(WidgetGlyph.Repeat, $"Repeat {playback.RepeatState.ToString().ToLowerInvariant()}")
            .Selected(playback.RepeatState != WidgetSpotifyRepeatState.Off)
            .Disabled(RepeatUnavailable(playback) || pending is not null)
            .Busy(pending == WidgetSpotifyPlaybackOperation.SetRepeat)
            .FocusUp("spotify.next").FocusLeft("spotify.seek")
            .Classes("spotify-secondary-action",
                playback.RepeatState == WidgetSpotifyRepeatState.Off ? "is-inactive" : "is-active");
        var refresh = UI.Button("Refresh", "spotify.refresh", "spotify.refresh")
            .Icon(WidgetGlyph.Refresh, "Refresh Spotify")
            .FocusUp("spotify.seek").FocusRight("spotify.disconnect")
            .Classes("spotify-text-action");
        var disconnect = UI.Button("Disconnect", "spotify.disconnect", "spotify.disconnect")
            .FocusUp("spotify.seek").FocusLeft("spotify.refresh")
            .Classes("spotify-text-action");

        var root = UI.Stack("spotify.root",
                header,
                UI.Row("spotify.media",
                        UI.Stack("spotify.artwork-frame", artwork).Classes("spotify-artwork-frame"),
                        UI.Stack("spotify.details",
                                UI.Text(item.Title, "spotify.track-title", $"Title {item.Title}")
                                    .Classes("spotify-track-title"),
                                UI.Text(item.Subtitle, "spotify.track-subtitle",
                                        $"Artist or show {item.Subtitle}")
                                    .Classes("spotify-track-subtitle"),
                                UI.Text(item.ContextName ?? "Spotify", "spotify.context",
                                        item.ContextName ?? "Spotify")
                                    .Classes("spotify-context"),
                                UI.Row("spotify.timeline",
                                        UI.Text(FormatTime(position), "spotify.position")
                                            .Classes("spotify-time"),
                                        UI.Progress(position, duration, "spotify.progress",
                                                $"{FormatTime(position)} of {FormatTime(duration)}")
                                            .Classes("spotify-progress"),
                                        UI.Text(FormatTime(duration), "spotify.duration")
                                            .Classes("spotify-time", "is-duration"))
                                    .Classes("spotify-timeline"))
                            .Classes("spotify-details"))
                    .Classes("spotify-media"),
                UI.Row("spotify.primary-controls", previous, toggle, next)
                    .Classes("spotify-primary-controls"),
                UI.Row("spotify.playback-options", shuffle, seek, repeat)
                    .Classes("spotify-playback-options"),
                UI.Row("spotify.footer",
                        UI.Text(playback.Attribution, "spotify.attribution", "Powered by Spotify")
                            .Classes("spotify-attribution"),
                        UI.Row("spotify.footer-actions", refresh, disconnect)
                            .Classes("spotify-footer-actions"))
                    .Classes("spotify-footer"))
            .InputScope(InputScope)
            .Classes("spotify-widget", "is-ready");
        if (!disallowed.SkippingPrevious)
            root = root.Shortcut(ControllerButton.LeftBumper, "spotify.previous");
        if (!toggleBlocked)
            root = root.Shortcut(ControllerButton.X, "spotify.play-toggle");
        if (!disallowed.SkippingNext)
            root = root.Shortcut(ControllerButton.RightBumper, "spotify.next");
        root = root.Shortcut(ControllerButton.Y, "spotify.refresh");

        var quickActions = new List<WidgetQuickAction>();
        if (!disallowed.SkippingPrevious)
            quickActions.Add(new(ControllerButton.LeftBumper, "spotify.previous", "Previous track",
                PlaybackControlAuthority));
        if (!toggleBlocked)
            quickActions.Add(new(ControllerButton.X, "spotify.play-toggle",
                playback.IsPlaying ? "Pause" : "Play", PlaybackControlAuthority));
        if (!disallowed.SkippingNext)
            quickActions.Add(new(ControllerButton.RightBumper, "spotify.next", "Next track",
                PlaybackControlAuthority));
        return new WidgetView(root, "spotify.play-toggle", quickActions,
            ActiveInputScopeId: InputScope, Surface: StandardSurface);
    }

    private static StackElement StateCard(
        WidgetGlyph glyph,
        string title,
        string detail,
        WidgetElement? action = null)
    {
        var children = new List<WidgetElement>
        {
            UI.Icon(glyph, "spotify.state-icon", title).Classes("spotify-state-icon"),
            UI.Text(title, "spotify.state-title", title).Classes("spotify-state-title"),
            UI.Text(detail, "spotify.state-detail", detail).Classes("spotify-state-detail"),
        };
        if (action is not null) children.Add(action);
        return UI.Stack("spotify.state-card", children.ToArray()).Classes("spotify-state-card");
    }

    private static bool RepeatUnavailable(WidgetSpotifyPlaybackSummary playback) =>
        playback.RepeatState switch
        {
            WidgetSpotifyRepeatState.Off => playback.DisallowedActions.TogglingRepeatContext,
            WidgetSpotifyRepeatState.Context => playback.DisallowedActions.TogglingRepeatTrack,
            _ => false,
        };

    public static string FormatTime(long milliseconds)
    {
        var totalSeconds = Math.Max(0, milliseconds / 1_000);
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1
            ? $"{(long)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }
}
