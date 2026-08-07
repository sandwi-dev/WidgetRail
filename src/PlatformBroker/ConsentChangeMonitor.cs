using System.Threading.Channels;

namespace GameBarAlternative.PlatformBroker;

/// <summary>
/// Coalesces cross-process consent file changes into broker reconciliation.
/// Filesystem paths never cross the broker protocol boundary.
/// </summary>
internal sealed class ConsentChangeMonitor : IAsyncDisposable
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(40);
    private readonly ConsentStore _store;
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly Action _revoke;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<byte> _signals = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly FileSystemWatcher _watcher;
    private readonly Task _pump;
    private int _reliabilityLost;
    private int _disposed;

    public ConsentChangeMonitor(
        ConsentStore store,
        Func<CancellationToken, Task> refresh,
        Action revoke)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _revoke = revoke ?? throw new ArgumentNullException(nameof(revoke));
        _store.PrepareForMonitoring();
        _watcher = new FileSystemWatcher(_store.RootDirectory)
        {
            Filter = "*",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = false,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
        _pump = PumpAsync(_lifetime.Token);
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (IsConsentDocument(eventArgs.FullPath)) Signal();
    }

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        if (IsConsentDocument(eventArgs.FullPath) || IsConsentDocument(eventArgs.OldFullPath))
            Signal();
    }

    private void OnError(object sender, ErrorEventArgs eventArgs)
    {
        Interlocked.Exchange(ref _reliabilityLost, 1);
        Signal();
    }

    private bool IsConsentDocument(string path) =>
        string.Equals(path, _store.DocumentFile, StringComparison.OrdinalIgnoreCase);

    private void Signal() => _signals.Writer.TryWrite(0);

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _signals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_signals.Reader.TryRead(out _)) { }
                await Task.Delay(CoalesceWindow, cancellationToken).ConfigureAwait(false);
                while (_signals.Reader.TryRead(out _)) { }

                if (Interlocked.Exchange(ref _reliabilityLost, 0) != 0)
                {
                    _revoke();
                    continue;
                }

                try { await _refresh(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    // A missing, malformed, or unreadable consent document is
                    // never allowed to preserve an existing capability grant.
                    _revoke();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        _lifetime.Cancel();
        _signals.Writer.TryComplete();
        try { await _pump.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException) { }
        _lifetime.Dispose();
    }
}
