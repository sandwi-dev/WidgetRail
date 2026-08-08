using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsMediaProvider;

/// <summary>
/// Lazy, event-driven GSMTC provider. It retains a bounded opaque-ID map and
/// publishes coalesced complete snapshots; no timers or media polling run.
/// </summary>
public sealed class WindowsMediaPlatformBackend : IMediaPlatformBrokerBackend, IAsyncDisposable
{
    internal const int MaximumSessions = 32;
    private const int MaximumRetainedOpaqueIds = 64;
    private readonly IWindowsMediaNativeAdapterFactory _factory;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _opaqueByNative = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nativeByOpaque = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private IWindowsMediaNativeAdapter? _adapter;
    private IReadOnlyList<MediaSessionSummary> _snapshot = [];
    private Task? _refreshLoop;
    private long _nextOpaqueId;
    private int _refreshRequested;
    private int _refreshRunning;
    private bool _started;
    private bool _disposed;

    public WindowsMediaPlatformBackend() : this(new WindowsMediaNativeAdapterFactory()) { }

    internal WindowsMediaPlatformBackend(IWindowsMediaNativeAdapterFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    public async Task<IReadOnlyList<MediaSessionSummary>> GetMediaSessionsAsync(
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate) return _snapshot.ToArray();
    }

    public async Task ControlMediaSessionAsync(
        string sessionId,
        MediaSessionCommand command,
        CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        IWindowsMediaNativeAdapter adapter;
        string nativeId;
        lock (_gate)
        {
            ThrowIfDisposed();
            adapter = _adapter ?? throw new BrokerException(
                "platform_unavailable", "Windows media sessions are unavailable.");
            if (!_nativeByOpaque.TryGetValue(sessionId, out nativeId!))
                throw new BrokerException("resource_not_found", "The media session no longer exists.");
        }
        var result = await adapter.ControlAsync(nativeId, ToNative(command), cancellationToken)
            .ConfigureAwait(false);
        switch (result)
        {
            case NativeMediaControlResult.Succeeded:
                await RefreshAsync(adapter, publish: true, cancellationToken).ConfigureAwait(false);
                return;
            case NativeMediaControlResult.NotFound:
                await RefreshAsync(adapter, publish: true, cancellationToken).ConfigureAwait(false);
                throw new BrokerException("resource_not_found", "The media session no longer exists.");
            case NativeMediaControlResult.NotSupported:
                throw new BrokerException("not_supported", "That media action is not supported.");
            default:
                throw new BrokerException("platform_unavailable", "Windows rejected the media action.");
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started) return;
        }
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_started) return;
            }
            var adapter = _factory.Create();
            adapter.StateChanged += OnNativeStateChanged;
            lock (_gate)
            {
                ThrowIfDisposed();
                _adapter = adapter;
            }
            try
            {
                await adapter.StartAsync(cancellationToken).ConfigureAwait(false);
                var native = await adapter.ReadSessionsAsync(cancellationToken).ConfigureAwait(false);
                var mapped = Map(native);
                lock (_gate)
                {
                    ThrowIfDisposed();
                    _adapter = adapter;
                    _snapshot = mapped;
                    _started = true;
                    if (Volatile.Read(ref _refreshRequested) != 0)
                        ScheduleRefreshLocked(adapter);
                }
            }
            catch (OperationCanceledException)
            {
                lock (_gate) if (ReferenceEquals(_adapter, adapter)) _adapter = null;
                adapter.StateChanged -= OnNativeStateChanged;
                await adapter.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                lock (_gate) if (ReferenceEquals(_adapter, adapter)) _adapter = null;
                adapter.StateChanged -= OnNativeStateChanged;
                await adapter.DisposeAsync().ConfigureAwait(false);
                throw new BrokerException(
                    "platform_unavailable", "Windows media sessions are unavailable.", exception);
            }
        }
        finally { _startGate.Release(); }
    }

    private void OnNativeStateChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _adapter)) return;
            Interlocked.Exchange(ref _refreshRequested, 1);
            if (_started) ScheduleRefreshLocked(_adapter!);
        }
    }

    private void ScheduleRefreshLocked(IWindowsMediaNativeAdapter adapter)
    {
        if (Interlocked.CompareExchange(ref _refreshRunning, 1, 0) != 0) return;
        _refreshLoop = Task.Run(() => RefreshLoopAsync(adapter, _lifetime.Token));
    }

    private async Task RefreshLoopAsync(
        IWindowsMediaNativeAdapter adapter, CancellationToken cancellationToken)
    {
        while (true)
        {
            while (Interlocked.Exchange(ref _refreshRequested, 0) != 0)
            {
                try { await RefreshAsync(adapter, publish: true, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _refreshRunning, 0);
                    return;
                }
                catch (Exception)
                {
                    // Session objects are ephemeral. Keep the last valid snapshot;
                    // the next native event schedules another reconciliation.
                }
            }
            Interlocked.Exchange(ref _refreshRunning, 0);
            if (Volatile.Read(ref _refreshRequested) == 0 ||
                Interlocked.CompareExchange(ref _refreshRunning, 1, 0) != 0) return;
        }
    }

    private async Task RefreshAsync(
        IWindowsMediaNativeAdapter adapter,
        bool publish,
        CancellationToken cancellationToken)
    {
        var mapped = Map(await adapter.ReadSessionsAsync(cancellationToken).ConfigureAwait(false));
        bool changed;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(adapter, _adapter)) return;
            changed = !_snapshot.SequenceEqual(mapped);
            _snapshot = mapped;
        }
        if (publish && changed)
            EventPublished?.Invoke(this, new BrokerPlatformEvent(
                PlatformCapabilities.MediaSessionsReadV1,
                PlatformCapabilities.MediaSessionsChanged,
                new MediaSessionsChangedEvent(mapped)));
    }

    private IReadOnlyList<MediaSessionSummary> Map(IReadOnlyList<NativeMediaSession> native)
    {
        var currentIds = native.Where(item => !string.IsNullOrWhiteSpace(item.NativeId))
            .Select(item => item.NativeId).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var mapped = new List<MediaSessionSummary>(Math.Min(native.Count, MaximumSessions));
        var currentAssigned = false;
        foreach (var item in native)
        {
            if (mapped.Count >= MaximumSessions || string.IsNullOrWhiteSpace(item.NativeId) ||
                item.NativeId.Length > 128 || !seen.Add(item.NativeId)) continue;
            var duration = Math.Clamp(item.DurationMilliseconds, 0,
                (long)TimeSpan.FromDays(7).TotalMilliseconds);
            var isCurrent = item.IsCurrent && !currentAssigned;
            currentAssigned |= isCurrent;
            mapped.Add(new MediaSessionSummary(
                OpaqueIdFor(item.NativeId),
                Sanitize(item.AppName, "Media app"),
                Sanitize(item.Title, "Unknown title"),
                Sanitize(item.Artist, "Unknown artist"),
                item.PlaybackStatus switch
                {
                    NativeMediaPlaybackStatus.Opened => MediaPlaybackStatus.Opened,
                    NativeMediaPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
                    NativeMediaPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
                    NativeMediaPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                    NativeMediaPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                    _ => MediaPlaybackStatus.Closed,
                },
                Math.Clamp(item.PositionMilliseconds, 0, duration),
                duration,
                Math.Max(0, item.CapturedAtUnixMilliseconds),
                double.IsFinite(item.PlaybackRate) ? Math.Clamp(item.PlaybackRate, 0, 16) : 1,
                isCurrent,
                item.CanPlay,
                item.CanPause,
                item.CanTogglePlayPause,
                item.CanPrevious,
                item.CanNext));
        }
        var result = mapped
            .OrderByDescending(item => item.IsCurrent)
            .ThenByDescending(item => item.PlaybackStatus == MediaPlaybackStatus.Playing)
            .ThenBy(item => item.AppName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        PruneOpaqueIds(currentIds);
        return result;
    }

    private string OpaqueIdFor(string nativeId)
    {
        lock (_gate)
        {
            if (_opaqueByNative.TryGetValue(nativeId, out var opaque)) return opaque;
            opaque = $"media-{++_nextOpaqueId:x}";
            _opaqueByNative[nativeId] = opaque;
            _nativeByOpaque[opaque] = nativeId;
            return opaque;
        }
    }

    private void PruneOpaqueIds(IReadOnlySet<string> current)
    {
        lock (_gate)
        {
            foreach (var nativeId in _opaqueByNative.Keys
                         .Where(id => !current.Contains(id)).ToArray())
            {
                var opaque = _opaqueByNative[nativeId];
                _opaqueByNative.Remove(nativeId);
                _nativeByOpaque.Remove(opaque);
            }
            if (_opaqueByNative.Count <= MaximumRetainedOpaqueIds) return;
            foreach (var nativeId in _opaqueByNative.Keys.Skip(MaximumRetainedOpaqueIds).ToArray())
            {
                var opaque = _opaqueByNative[nativeId];
                _opaqueByNative.Remove(nativeId);
                _nativeByOpaque.Remove(opaque);
            }
        }
    }

    private static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var clean = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return clean.Length switch
        {
            0 => fallback,
            <= 160 => clean,
            _ => clean[..160],
        };
    }

    private static NativeMediaCommand ToNative(MediaSessionCommand command) => command switch
    {
        MediaSessionCommand.Play => NativeMediaCommand.Play,
        MediaSessionCommand.Pause => NativeMediaCommand.Pause,
        MediaSessionCommand.TogglePlayPause => NativeMediaCommand.TogglePlayPause,
        MediaSessionCommand.Previous => NativeMediaCommand.Previous,
        MediaSessionCommand.Next => NativeMediaCommand.Next,
        _ => throw new BrokerException("invalid_payload", "Media session command is invalid."),
    };

    public async ValueTask DisposeAsync()
    {
        IWindowsMediaNativeAdapter? adapter;
        Task? refresh;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            adapter = _adapter;
            _adapter = null;
            refresh = _refreshLoop;
            if (adapter is not null) adapter.StateChanged -= OnNativeStateChanged;
            _opaqueByNative.Clear();
            _nativeByOpaque.Clear();
            _snapshot = [];
        }
        if (refresh is not null)
        {
            try { await refresh.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (adapter is not null) await adapter.DisposeAsync().ConfigureAwait(false);
        _startGate.Dispose();
        _lifetime.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsMediaPlatformBackend));
    }
}
