using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsActivityProvider;

/// <summary>
/// Lazily starts when the broker forwards the first authorized read, then stays
/// event-driven until backend disposal; it performs no polling. Native handles
/// and process data remain in this trusted process, while consumers receive only
/// opaque IDs and bounded display names.
/// </summary>
public sealed class WindowsActivityPlatformBackend : IActivityPlatformBrokerBackend, IAsyncDisposable
{
    internal const int MaximumActivities = 16;
    private static readonly HashSet<string> ExcludedProcessKeys = new(StringComparer.Ordinal)
    {
        "OVERLAYHOST",
        "WIDGETBRIDGE",
        "WIDGETWORKERHOST",
        "YTMUSICWIDGET.WORKER",
        "SETTINGSWIDGET.WORKER",
        "AUDIOMIXERWIDGET.WORKER",
        "NETWORKCONTROLSWIDGET.WORKER",
        "RECENTAPPSWIDGET.WORKER",
        "DWM",
        "CSRSS",
        "WINLOGON",
        "LOGONUI",
        "LOCKAPP",
    };

    private readonly IWindowsActivityNativeAdapter _native;
    private readonly Channel<NativeActivityEvent> _events = Channel.CreateBounded<NativeActivityEvent>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });
    private readonly object _gate = new();
    private readonly object _startGate = new();
    private readonly List<ActivityEntry> _activities = [];
    private readonly Task _eventPump;
    private IDisposable? _subscription;
    private int _started;
    private int _disposed;

    public WindowsActivityPlatformBackend() : this(new WindowsActivityNativeAdapter())
    {
    }

    internal WindowsActivityPlatformBackend(IWindowsActivityNativeAdapter native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _eventPump = Task.Run(PumpAsync);
    }

    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        PruneUnavailable();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<RecentActivitySummary>>(SnapshotLocked());
    }

    private void EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _started) != 0) return;
        lock (_startGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_started != 0) return;
            try
            {
                _subscription = _native.Start(OnNativeEvent);
            }
            catch (Exception exception) when (exception is not BrokerException)
            {
                throw new BrokerException(
                    "platform_unavailable", "Windows foreground activity is unavailable.", exception);
            }
            Volatile.Write(ref _started, 1);
            var foreground = _native.GetCurrentForegroundWindow();
            if (foreground != 0) ObserveForeground(foreground, publish: false);
        }
    }

    private void OnNativeEvent(NativeActivityEvent nativeEvent)
    {
        if (Volatile.Read(ref _disposed) == 0) _events.Writer.TryWrite(nativeEvent);
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var nativeEvent in _events.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (nativeEvent.Kind == NativeActivityEventKind.Foreground)
                    ObserveForeground(nativeEvent.Window, publish: true);
                else
                    RemoveWindow(nativeEvent.Window);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ObserveForeground(nint window, bool publish)
    {
        var candidate = _native.InspectWindow(window);
        if (candidate is null || IsExcluded(candidate)) return;
        lock (_gate)
        {
            var index = _activities.FindIndex(item => item.ProcessKey == candidate.ProcessKey);
            ActivityEntry entry;
            if (index >= 0 && _activities[index].ProcessId == candidate.ProcessId)
            {
                entry = _activities[index] with
                {
                    Window = candidate.Window,
                    ProcessId = candidate.ProcessId,
                    DisplayName = candidate.DisplayName,
                };
                _activities.RemoveAt(index);
            }
            else
            {
                // A process key identifies an application, not one process
                // lifetime. Rotate the opaque observation token so a stale
                // process lifetime is never represented as the replacement.
                if (index >= 0) _activities.RemoveAt(index);
                entry = new ActivityEntry(
                    "activity-" + Guid.NewGuid().ToString("N"),
                    candidate.ProcessKey,
                    candidate.Window,
                    candidate.ProcessId,
                    candidate.DisplayName,
                    RecentActivityKind.Application);
            }
            _activities.Insert(0, entry);
            if (_activities.Count > MaximumActivities)
                _activities.RemoveRange(MaximumActivities, _activities.Count - MaximumActivities);
        }
        if (publish) Publish();
    }

    private static bool IsExcluded(NativeActivityCandidate candidate) =>
        ExcludedProcessKeys.Contains(candidate.ProcessKey) ||
        candidate.ProcessKey.EndsWith("WIDGET.WORKER", StringComparison.Ordinal) ||
        candidate.DisplayName.StartsWith("Game Bar Alternative", StringComparison.OrdinalIgnoreCase);

    private void PruneUnavailable()
    {
        ActivityEntry[] copy;
        lock (_gate) copy = _activities.ToArray();
        var unavailable = copy.Where(entry =>
            !_native.IsWindowAvailable(entry.Window, entry.ProcessId))
            .Select(entry => entry.ActivityId).ToHashSet(StringComparer.Ordinal);
        if (unavailable.Count == 0) return;
        lock (_gate) _activities.RemoveAll(item => unavailable.Contains(item.ActivityId));
    }

    private void RemoveWindow(nint window)
    {
        var changed = false;
        lock (_gate) changed = _activities.RemoveAll(item => item.Window == window) != 0;
        if (changed) Publish();
    }

    private RecentActivitySummary[] SnapshotLocked() => _activities
        .Select((entry, index) => new RecentActivitySummary(
            entry.ActivityId,
            entry.DisplayName,
            entry.Kind,
            IsRunning: true,
            IsMostRecent: index == 0))
        .ToArray();

    private void Publish()
    {
        RecentActivitySummary[] snapshot;
        lock (_gate) snapshot = SnapshotLocked();
        EventPublished?.Invoke(this, new BrokerPlatformEvent(
            PlatformCapabilities.RecentActivityReadV1,
            PlatformCapabilities.RecentActivitiesChanged,
            new RecentActivitiesChangedEvent(snapshot)));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _subscription?.Dispose();
        _subscription = null;
        _events.Writer.TryComplete();
        await _eventPump.ConfigureAwait(false);
        _native.Dispose();
    }

    private sealed record ActivityEntry(
        string ActivityId,
        string ProcessKey,
        nint Window,
        uint ProcessId,
        string DisplayName,
        RecentActivityKind Kind);
}
