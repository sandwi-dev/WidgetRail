using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAudioProvider;

/// <summary>
/// Event-driven Core Audio provider. A single dedicated MTA thread owns every native object;
/// callback threads only enqueue a coalesced invalidation.
/// </summary>
public sealed class WindowsAudioPlatformBackend : IAudioPlatformBrokerBackend, IAsyncDisposable
{
    private const int MaximumSessions = 128;
    private const int MaximumNativeKeyLength = 2048;
    private const int MaximumDisplayNameLength = 160;
    private readonly IWindowsAudioNativeAdapterFactory _factory;
    private readonly BlockingCollection<AudioCommand> _commands = new(128);
    private readonly Channel<AudioSessionsChangedEvent> _events = Channel.CreateBounded<AudioSessionsChangedEvent>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    private readonly Channel<AudioOutputChangedEvent> _outputEvents = Channel.CreateBounded<AudioOutputChangedEvent>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _threadExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _stateGate = new();
    private readonly object _startGate = new();
    private readonly Dictionary<string, string> _opaqueIds = new(StringComparer.Ordinal);
    private IReadOnlyList<AudioSessionSummary> _sessions = [];
    private IReadOnlyDictionary<string, string> _nativeKeysByOpaqueId =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private AudioOutputSummary? _output;
    private Thread? _ownerThread;
    private Task _eventPump = Task.CompletedTask;
    private int _started;
    private int _refreshQueued;
    private int _disposeStarted;
    private volatile bool _degraded;
    private volatile bool _ownerUnavailable;
    private volatile bool _snapshotUnavailable;
    private volatile bool _outputUnavailable = true;

    public WindowsAudioPlatformBackend(IWindowsAudioNativeAdapterFactory? factory = null)
    {
        _factory = factory ?? new CoreAudioNativeAdapterFactory();
    }

    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    /// <summary>True when Core Audio cannot currently provide a trustworthy snapshot.</summary>
    public bool IsDegraded => _degraded;

    /// <summary>Diagnostic flag; construction and event subscription do not activate Core Audio.</summary>
    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public async Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
        CancellationToken cancellationToken)
    {
        var explicitRetry = IsStarted;
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (explicitRetry && _snapshotUnavailable && !_ownerUnavailable)
            await EnqueueRetryRefreshAsync(cancellationToken).ConfigureAwait(false);
        if (_snapshotUnavailable)
            throw new BrokerException(
                "platform_unavailable", "Windows audio is temporarily unavailable.");
        lock (_stateGate) return _sessions.ToArray();
    }

    public async Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken)
    {
        var explicitRetry = IsStarted;
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (explicitRetry && _outputUnavailable && !_ownerUnavailable)
            await EnqueueRetryRefreshAsync(cancellationToken).ConfigureAwait(false);
        if (_outputUnavailable)
            throw new BrokerException(
                "platform_unavailable", "Windows audio output is temporarily unavailable.");
        lock (_stateGate) return _output!;
    }

    private async Task EnqueueRetryRefreshAsync(CancellationToken cancellationToken)
    {
        var command = new RetryRefreshCommand(cancellationToken);
        try
        {
            if (!_commands.TryAdd(command)) return;
        }
        catch (InvalidOperationException)
        {
            ThrowIfDisposed();
            return;
        }
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAudioSessionVolumeAsync(
        string sessionId,
        double volume,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new BrokerException("invalid_payload", "Audio volume must be between zero and one.");
        await EnqueueControlAsync(
            new ControlCommand(sessionId, false, volume, null, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAudioSessionMutedAsync(
        string sessionId,
        bool isMuted,
        CancellationToken cancellationToken) =>
        await EnqueueControlAsync(
            new ControlCommand(sessionId, false, null, isMuted, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

    public async Task SetAudioOutputVolumeAsync(
        double volume,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new BrokerException("invalid_payload", "Audio output volume must be between zero and one.");
        await EnqueueControlAsync(
            new ControlCommand(null, true, volume, null, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAudioOutputMutedAsync(
        bool isMuted,
        CancellationToken cancellationToken) =>
        await EnqueueControlAsync(
            new ControlCommand(null, true, null, isMuted, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

    private async Task EnqueueControlAsync(ControlCommand command, CancellationToken cancellationToken)
    {
        EnsureStarted();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_ownerUnavailable)
            throw new BrokerException("platform_unavailable", "Windows audio is temporarily unavailable.");
        try
        {
            if (!_commands.TryAdd(command))
                throw new BrokerException("provider_busy", "The audio provider is busy.");
        }
        catch (InvalidOperationException exception)
        {
            throw new ObjectDisposedException(nameof(WindowsAudioPlatformBackend), exception);
        }
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OwnerThreadMain()
    {
        IWindowsAudioNativeAdapter? adapter = null;
        var apartmentInitialized = false;
        try
        {
            apartmentInitialized = CoreAudioInterop.InitializeMta();
            adapter = _factory.Create();
            adapter.StateChanged += OnNativeStateChanged;
            Refresh(adapter, publish: false);
            _ready.TrySetResult();

            foreach (var command in _commands.GetConsumingEnumerable())
            {
                switch (command)
                {
                    case RefreshCommand:
                        Interlocked.Exchange(ref _refreshQueued, 0);
                        Refresh(adapter, publish: true);
                        break;
                    case RetryRefreshCommand retry:
                        if (retry.CancellationToken.IsCancellationRequested)
                            retry.Completion.TrySetCanceled(retry.CancellationToken);
                        else
                        {
                            Refresh(adapter, publish: true);
                            retry.Completion.TrySetResult();
                        }
                        break;
                    case ControlCommand control:
                        ExecuteControl(adapter, control);
                        break;
                }
            }
        }
        catch (Exception)
        {
            _ownerUnavailable = true;
            SetDegradedState(publish: _ready.Task.IsCompleted);
            _ready.TrySetResult();
        }
        finally
        {
            try { _commands.CompleteAdding(); }
            catch (ObjectDisposedException) { }
            if (adapter is not null)
            {
                adapter.StateChanged -= OnNativeStateChanged;
                try { adapter.Dispose(); }
                catch { }
            }
            if (apartmentInitialized) CoreAudioInterop.Uninitialize();
            while (_commands.TryTake(out var pending))
                switch (pending)
                {
                    case ControlCommand control:
                        control.Completion.TrySetException(new ObjectDisposedException(nameof(WindowsAudioPlatformBackend)));
                        break;
                    case RetryRefreshCommand retry:
                        retry.Completion.TrySetResult();
                        break;
                }
            _events.Writer.TryComplete();
            _outputEvents.Writer.TryComplete();
            _ready.TrySetResult();
            _threadExited.TrySetResult();
        }
    }

    private void EnsureStarted()
    {
        if (Volatile.Read(ref _started) != 0) return;
        lock (_startGate)
        {
            ThrowIfDisposed();
            if (_started != 0) return;
            _eventPump = Task.WhenAll(DispatchEventsAsync(), DispatchOutputEventsAsync());
            _ownerThread = new Thread(OwnerThreadMain)
            {
                IsBackground = true,
                Name = "GameBarAlternative.CoreAudio.MTA",
            };
            Volatile.Write(ref _started, 1);
            _ownerThread.Start();
        }
    }

    private void OnNativeStateChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _disposeStarted) != 0) return;
        if (Interlocked.CompareExchange(ref _refreshQueued, 1, 0) != 0) return;
        try
        {
            if (!_commands.TryAdd(RefreshCommand.Instance))
                Interlocked.Exchange(ref _refreshQueued, 0);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
    }

    private void ExecuteControl(IWindowsAudioNativeAdapter adapter, ControlCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested)
        {
            command.Completion.TrySetCanceled(command.CancellationToken);
            return;
        }

        try
        {
            if (command.IsOutput)
            {
                var outputChanged = command.Volume is { } outputVolume
                    ? adapter.TrySetDefaultOutputVolume(outputVolume)
                    : adapter.TrySetDefaultOutputMuted(command.IsMuted!.Value);
                if (!outputChanged)
                {
                    Refresh(adapter, publish: true);
                    throw new BrokerException(
                        "platform_unavailable", "Windows audio output is temporarily unavailable.");
                }
                Refresh(adapter, publish: true);
                command.Completion.TrySetResult();
                return;
            }

            string nativeKey;
            lock (_stateGate)
            {
                if (!_nativeKeysByOpaqueId.TryGetValue(command.SessionId!, out nativeKey!))
                    throw new BrokerException("resource_not_found", "The audio session is no longer available.");
            }

            var changed = command.Volume is { } volume
                ? adapter.TrySetSessionVolume(nativeKey, volume)
                : adapter.TrySetSessionMuted(nativeKey, command.IsMuted!.Value);
            if (!changed)
            {
                if (adapter.IsDegraded)
                {
                    SetDegradedState(publish: true);
                    throw new BrokerException(
                        "platform_unavailable", "Windows audio is temporarily unavailable.");
                }
                throw new BrokerException("resource_not_found", "The audio session is no longer available.");
            }
            Refresh(adapter, publish: true);
            command.Completion.TrySetResult();
        }
        catch (BrokerException exception)
        {
            command.Completion.TrySetException(exception);
        }
        catch (Exception exception)
        {
            SetDegradedState(publish: true);
            command.Completion.TrySetException(new BrokerException(
                "platform_unavailable", "Windows audio is temporarily unavailable.", exception));
        }
    }

    private void Refresh(IWindowsAudioNativeAdapter adapter, bool publish)
    {
        try
        {
            var outputSnapshot = adapter.GetDefaultOutput();
            var output = outputSnapshot is not null && double.IsFinite(outputSnapshot.Volume)
                ? new AudioOutputSummary(Math.Clamp(outputSnapshot.Volume, 0, 1), outputSnapshot.IsMuted)
                : null;
            var snapshots = adapter.EnumerateSessions() ?? [];
            var seenNativeKeys = new HashSet<string>(StringComparer.Ordinal);
            var summaries = new List<AudioSessionSummary>(Math.Min(snapshots.Count, MaximumSessions));
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var snapshot in snapshots)
            {
                if (summaries.Count >= MaximumSessions) break;
                if (!IsValidSnapshot(snapshot) || !seenNativeKeys.Add(snapshot.NativeSessionKey)) continue;
                if (!_opaqueIds.TryGetValue(snapshot.NativeSessionKey, out var opaqueId))
                {
                    opaqueId = $"audio_{Guid.NewGuid():N}";
                    _opaqueIds.Add(snapshot.NativeSessionKey, opaqueId);
                }
                reverse.Add(opaqueId, snapshot.NativeSessionKey);
                summaries.Add(new AudioSessionSummary(
                    opaqueId,
                    SanitizeDisplayName(snapshot.DisplayName),
                    Math.Clamp(snapshot.Volume, 0, 1),
                    snapshot.IsMuted,
                    snapshot.IsActive));
            }
            foreach (var missing in _opaqueIds.Keys.Where(key => !seenNativeKeys.Contains(key)).ToArray())
                _opaqueIds.Remove(missing);

            summaries.Sort(static (left, right) =>
            {
                var active = right.IsActive.CompareTo(left.IsActive);
                return active != 0 ? active : string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
            var unavailable = adapter.IsDegraded;
            var outputUnavailable = adapter.IsOutputDegraded || output is null;
            var sessionsChanged = false;
            var outputChanged = false;
            lock (_stateGate)
            {
                sessionsChanged = !SessionsEqual(_sessions, summaries) ||
                    _snapshotUnavailable != unavailable;
                outputChanged = _output != output ||
                    _outputUnavailable != outputUnavailable;
                _sessions = summaries.ToArray();
                _nativeKeysByOpaqueId = reverse;
                _output = output;
            }
            _degraded = unavailable || outputUnavailable;
            _snapshotUnavailable = unavailable;
            _outputUnavailable = outputUnavailable;
            if (publish && sessionsChanged)
                _events.Writer.TryWrite(new AudioSessionsChangedEvent(
                    unavailable ? [] : summaries.ToArray(), !unavailable));
            if (publish && outputChanged)
                _outputEvents.Writer.TryWrite(new AudioOutputChangedEvent(
                    outputUnavailable ? null : output, !outputUnavailable));
        }
        catch
        {
            SetDegradedState(publish);
        }
    }

    private void SetDegradedState(bool publish)
    {
        bool sessionsChanged;
        bool outputChanged;
        lock (_stateGate)
        {
            sessionsChanged = _sessions.Count != 0 || !_snapshotUnavailable;
            outputChanged = _output is not null || !_outputUnavailable;
            _sessions = [];
            _nativeKeysByOpaqueId = new Dictionary<string, string>(StringComparer.Ordinal);
            _output = null;
        }
        _degraded = true;
        _snapshotUnavailable = true;
        _outputUnavailable = true;
        if (publish && sessionsChanged)
            _events.Writer.TryWrite(new AudioSessionsChangedEvent([], false));
        if (publish && outputChanged)
            _outputEvents.Writer.TryWrite(new AudioOutputChangedEvent(null, false));
    }

    private async Task DispatchEventsAsync()
    {
        await foreach (var change in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var platformEvent = new BrokerPlatformEvent(
                PlatformCapabilities.AudioSessionsReadV1,
                PlatformCapabilities.AudioSessionsChanged,
                change);
            var handlers = EventPublished;
            if (handlers is null) continue;
            foreach (EventHandler<BrokerPlatformEvent> handler in handlers.GetInvocationList())
            {
                try { handler(this, platformEvent); }
                catch { }
            }
        }
    }

    private async Task DispatchOutputEventsAsync()
    {
        await foreach (var change in _outputEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var platformEvent = new BrokerPlatformEvent(
                PlatformCapabilities.AudioOutputReadV1,
                PlatformCapabilities.AudioOutputChanged,
                change);
            var handlers = EventPublished;
            if (handlers is null) continue;
            foreach (EventHandler<BrokerPlatformEvent> handler in handlers.GetInvocationList())
            {
                try { handler(this, platformEvent); }
                catch { }
            }
        }
    }

    private static bool IsValidSnapshot(NativeAudioSessionSnapshot snapshot) =>
        !string.IsNullOrEmpty(snapshot.NativeSessionKey) &&
        snapshot.NativeSessionKey.Length <= MaximumNativeKeyLength &&
        double.IsFinite(snapshot.Volume);

    private static string SanitizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Application audio";
        if (LooksLikePath(value)) return "Application audio";
        var builder = new StringBuilder(Math.Min(value.Length, MaximumDisplayNameLength));
        var pendingSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length != 0;
                continue;
            }
            var runeLength = rune.Utf16SequenceLength;
            var requiredLength = builder.Length + runeLength + (pendingSpace ? 1 : 0);
            if (requiredLength > MaximumDisplayNameLength) break;
            if (pendingSpace) builder.Append(' ');
            builder.Append(rune.ToString());
            pendingSpace = false;
        }
        var sanitized = builder.ToString();
        if (sanitized.Length == 0) return "Application audio";
        return sanitized;
    }

    private static bool LooksLikePath(string value) =>
        value.StartsWith("\\\\", StringComparison.Ordinal) ||
        value[0] == '/' ||
        value.Contains(":\\", StringComparison.Ordinal) ||
        value.Contains(":/", StringComparison.Ordinal);

    private static bool SessionsEqual(
        IReadOnlyList<AudioSessionSummary> left,
        IReadOnlyList<AudioSessionSummary> right) => left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First == pair.Second);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(WindowsAudioPlatformBackend));
    }

    public async ValueTask DisposeAsync()
    {
        Task threadExited;
        Task eventPump;
        lock (_startGate)
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
            _commands.CompleteAdding();
            if (_started == 0)
            {
                _events.Writer.TryComplete();
                _outputEvents.Writer.TryComplete();
                _ready.TrySetResult();
                _threadExited.TrySetResult();
            }
            threadExited = _threadExited.Task;
            eventPump = _eventPump;
        }
        await threadExited.ConfigureAwait(false);
        await eventPump.ConfigureAwait(false);
        _commands.Dispose();
    }

    private abstract record AudioCommand;
    private sealed record RefreshCommand : AudioCommand
    {
        public static RefreshCommand Instance { get; } = new();
    }

    private sealed record RetryRefreshCommand(CancellationToken CancellationToken) : AudioCommand
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record ControlCommand(
        string? SessionId,
        bool IsOutput,
        double? Volume,
        bool? IsMuted,
        CancellationToken CancellationToken) : AudioCommand
    {
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
