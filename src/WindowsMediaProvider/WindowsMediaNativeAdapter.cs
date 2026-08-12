using Windows.Media.Control;
using System.Diagnostics;

namespace GameBarAlternative.WindowsMediaProvider;

internal sealed class WindowsMediaNativeAdapterFactory : IWindowsMediaNativeAdapterFactory
{
    public IWindowsMediaNativeAdapter Create() => new WindowsMediaNativeAdapter();
}

/// <summary>
/// Owns GSMTC WinRT objects and events. Source AUMIDs remain private native keys;
/// only sanitized snapshots cross into the broker provider.
/// </summary>
internal sealed class WindowsMediaNativeAdapter : IWindowsMediaNativeAdapter
{
    private readonly object _gate = new();
    private readonly HashSet<GlobalSystemMediaTransportControlsSession> _observed =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, string> _tokenBySession =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessionByToken =
        new(StringComparer.Ordinal);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private long _nextNativeToken;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                .AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Windows media-session access is unavailable.", exception);
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_manager is not null) return;
            _manager = manager;
            manager.SessionsChanged += OnManagerChanged;
            manager.CurrentSessionChanged += OnManagerChanged;
            ReconcileObservedLocked(manager);
        }
    }

    public async Task<IReadOnlyList<NativeMediaSession>> ReadSessionsAsync(
        CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSessionManager manager;
        (GlobalSystemMediaTransportControlsSession Session, string Token)[] sessions;
        GlobalSystemMediaTransportControlsSession? current;
        lock (_gate)
        {
            ThrowIfDisposed();
            manager = _manager ?? throw new InvalidOperationException("Media provider has not started.");
            ReconcileObservedLocked(manager);
            current = manager.GetCurrentSession();
            sessions = manager.GetSessions()
                .OrderByDescending(session => ReferenceEquals(session, current) || session.Equals(current))
                .Take(32)
                .Select(session => (Session: session, Token: TokenForLocked(session))).ToArray();
        }

        var result = new List<NativeMediaSession>(sessions.Length);
        var artworkBudget = Stopwatch.StartNew();
        foreach (var entry in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = entry.Session;
            var sourceAppId = session.SourceAppUserModelId;
            try
            {
                var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken)
                    .ConfigureAwait(false);
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                var duration = ClampMilliseconds(timeline.EndTime - timeline.StartTime);
                var position = Math.Clamp(
                    ClampMilliseconds(timeline.Position - timeline.StartTime), 0, duration);
                var controls = playback.Controls;
                var artwork = artworkBudget.Elapsed < TimeSpan.FromMilliseconds(600)
                    ? await MediaArtworkReader.TryReadAsync(
                        media?.Thumbnail, cancellationToken).ConfigureAwait(false)
                    : null;
                result.Add(new NativeMediaSession(
                    entry.Token,
                    FriendlyAppName(sourceAppId),
                    Sanitize(media?.Title, "Unknown title"),
                    Sanitize(media?.Artist, "Unknown artist"),
                    MapStatus(playback.PlaybackStatus),
                    position,
                    duration,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    playback.PlaybackRate is { } rate && double.IsFinite(rate) && rate >= 0
                        ? Math.Min(rate, 16) : 1,
                    ReferenceEquals(session, current) || session.Equals(current),
                    controls.IsPlayEnabled,
                    controls.IsPauseEnabled,
                    controls.IsPlayPauseToggleEnabled,
                    controls.IsPreviousEnabled,
                    controls.IsNextEnabled,
                    artwork));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // A session can disappear between GetSessions and property retrieval.
                // Omit that one transient object; a native change event reconciles it.
            }
        }
        return result;
    }

    public async Task<NativeMediaControlResult> ControlAsync(
        string nativeId,
        NativeMediaCommand command,
        CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate)
        {
            ThrowIfDisposed();
            _sessionByToken.TryGetValue(nativeId, out session);
        }
        if (session is null) return NativeMediaControlResult.NotFound;
        try
        {
            var controls = session.GetPlaybackInfo().Controls;
            if (!IsEnabled(controls, command)) return NativeMediaControlResult.NotSupported;
            var succeeded = command switch
            {
                NativeMediaCommand.Play =>
                    await session.TryPlayAsync().AsTask(cancellationToken).ConfigureAwait(false),
                NativeMediaCommand.Pause =>
                    await session.TryPauseAsync().AsTask(cancellationToken).ConfigureAwait(false),
                NativeMediaCommand.TogglePlayPause =>
                    await session.TryTogglePlayPauseAsync().AsTask(cancellationToken).ConfigureAwait(false),
                NativeMediaCommand.Previous =>
                    await session.TrySkipPreviousAsync().AsTask(cancellationToken).ConfigureAwait(false),
                NativeMediaCommand.Next =>
                    await session.TrySkipNextAsync().AsTask(cancellationToken).ConfigureAwait(false),
                _ => false,
            };
            return succeeded ? NativeMediaControlResult.Succeeded : NativeMediaControlResult.Unavailable;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return NativeMediaControlResult.Unavailable; }
    }

    private static bool IsEnabled(
        GlobalSystemMediaTransportControlsSessionPlaybackControls controls,
        NativeMediaCommand command) => command switch
    {
        NativeMediaCommand.Play => controls.IsPlayEnabled,
        NativeMediaCommand.Pause => controls.IsPauseEnabled,
        NativeMediaCommand.TogglePlayPause => controls.IsPlayPauseToggleEnabled,
        NativeMediaCommand.Previous => controls.IsPreviousEnabled,
        NativeMediaCommand.Next => controls.IsNextEnabled,
        _ => false,
    };

    private void OnManagerChanged(
        GlobalSystemMediaTransportControlsSessionManager sender, object args)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _manager)) return;
            ReconcileObservedLocked(sender);
        }
        RaiseChanged();
    }

    private void OnSessionChanged(
        GlobalSystemMediaTransportControlsSession sender, object args) => RaiseChanged();

    private void ReconcileObservedLocked(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var current = new HashSet<GlobalSystemMediaTransportControlsSession>(
            manager.GetSessions(), ReferenceEqualityComparer.Instance);
        foreach (var removed in _observed.Where(session => !current.Contains(session)).ToArray())
        {
            UnsubscribeObserved(removed);
            _observed.Remove(removed);
            if (_tokenBySession.Remove(removed, out var token)) _sessionByToken.Remove(token);
        }
        foreach (var added in current.Where(session => !_observed.Contains(session)))
        {
            added.MediaPropertiesChanged += OnSessionChanged;
            added.PlaybackInfoChanged += OnSessionChanged;
            added.TimelinePropertiesChanged += OnSessionChanged;
            _observed.Add(added);
            _ = TokenForLocked(added);
        }
    }

    private string TokenForLocked(GlobalSystemMediaTransportControlsSession session)
    {
        if (_tokenBySession.TryGetValue(session, out var token)) return token;
        token = $"gsm-session-{++_nextNativeToken:x}";
        _tokenBySession.Add(session, token);
        _sessionByToken.Add(token, session);
        return token;
    }

    private void RaiseChanged()
    {
        lock (_gate) if (_disposed) return;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UnsubscribeObserved(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged -= OnSessionChanged;
        session.PlaybackInfoChanged -= OnSessionChanged;
        session.TimelinePropertiesChanged -= OnSessionChanged;
    }

    private static NativeMediaPlaybackStatus MapStatus(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => NativeMediaPlaybackStatus.Closed,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => NativeMediaPlaybackStatus.Opened,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => NativeMediaPlaybackStatus.Changing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => NativeMediaPlaybackStatus.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => NativeMediaPlaybackStatus.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => NativeMediaPlaybackStatus.Paused,
        _ => NativeMediaPlaybackStatus.Closed,
    };

    private static long ClampMilliseconds(TimeSpan value) =>
        (long)Math.Clamp(value.TotalMilliseconds, 0, TimeSpan.FromDays(7).TotalMilliseconds);

    internal static string FriendlyAppName(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId)) return "Media app";
        var candidate = sourceAppUserModelId;
        var bang = candidate.LastIndexOf('!');
        if (bang >= 0 && bang + 1 < candidate.Length) candidate = candidate[(bang + 1)..];
        candidate = Path.GetFileNameWithoutExtension(candidate);
        return Sanitize(candidate, "Media app");
    }

    internal static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var clean = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        if (clean.Length == 0) return fallback;
        return clean.Length <= 160 ? clean : clean[..160];
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            if (_manager is not null)
            {
                _manager.SessionsChanged -= OnManagerChanged;
                _manager.CurrentSessionChanged -= OnManagerChanged;
            }
            foreach (var session in _observed) UnsubscribeObserved(session);
            _observed.Clear();
            _tokenBySession.Clear();
            _sessionByToken.Clear();
            _manager = null;
        }
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsMediaNativeAdapter));
    }
}
