using System.Threading.Channels;

namespace GameBarAlternative.WidgetBridge;

public sealed record BridgeCatalogChanged(
    long Revision,
    BridgeCatalog Catalog,
    IReadOnlyList<string> Warnings);

public sealed record BridgeCatalogReloadResult(
    bool Published,
    bool RetainedLastGood,
    long Revision,
    BridgeCatalog Current,
    IReadOnlyList<string> Warnings);

internal readonly record struct BridgeCatalogDiagnosticSnapshot(
    long Revision,
    int DiagnosticCount,
    bool RetainedLastGood);

/// <summary>
/// Maintains one validated, last-good bridge catalog and publishes monotonically
/// increasing revisions. File-system notifications are hints only: every
/// publication is produced by a complete bounded reload and semantic comparison.
/// </summary>
public sealed class BridgeCatalogMonitor : IAsyncDisposable
{
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(175);

    private readonly string _trustedCatalogPath;
    private readonly string _installedCatalogRoot;
    private readonly string _workerHostExecutable;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly Channel<byte> _reloadSignals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly CancellationTokenSource _shutdown = new();

    private BridgeCatalog _current;
    private IReadOnlyList<string> _lastDiagnostics;
    private long _revision;
    private bool _retainedLastGood;
    private FileSystemWatcher? _trustedWatcher;
    private FileSystemWatcher? _installedWatcher;
    private Task? _reloadWorker;
    private bool _started;
    private bool _disposed;

    public BridgeCatalogMonitor(
        string trustedCatalogPath,
        string installedCatalogRoot,
        string workerHostExecutable,
        BridgeCatalog initialCatalog,
        IReadOnlyList<string>? initialDiagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedCatalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedCatalogRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerHostExecutable);
        _trustedCatalogPath = Path.GetFullPath(trustedCatalogPath);
        _installedCatalogRoot = Path.GetFullPath(installedCatalogRoot);
        _workerHostExecutable = Path.GetFullPath(workerHostExecutable);
        _current = initialCatalog ?? throw new ArgumentNullException(nameof(initialCatalog));
        _lastDiagnostics = initialDiagnostics?.Take(64).ToArray() ?? [];
    }

    public event EventHandler<BridgeCatalogChanged>? Changed;
    public event EventHandler<IReadOnlyList<string>>? Diagnostics;

    public BridgeCatalog Current
    {
        get { lock (_stateGate) return _current; }
    }

    public long Revision
    {
        get { lock (_stateGate) return _revision; }
    }

    public IReadOnlyList<string> LastDiagnostics
    {
        get { lock (_stateGate) return _lastDiagnostics; }
    }

    public bool RetainedLastGood
    {
        get { lock (_stateGate) return _retainedLastGood; }
    }

    internal BridgeCatalogDiagnosticSnapshot DiagnosticsSnapshot()
    {
        lock (_stateGate)
            return new(_revision, _lastDiagnostics.Count, _retainedLastGood);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;

        var trustedDirectory = Path.GetDirectoryName(_trustedCatalogPath)
            ?? throw new InvalidOperationException("Trusted catalog path has no parent directory.");
        Directory.CreateDirectory(_installedCatalogRoot);
        _trustedWatcher = CreateWatcher(
            trustedDirectory,
            Path.GetFileName(_trustedCatalogPath),
            includeSubdirectories: false,
            IsTrustedCatalogChange);
        _installedWatcher = CreateWatcher(
            _installedCatalogRoot,
            "*",
            includeSubdirectories: true,
            IsInstalledCatalogChange);
        _started = true;
        _reloadWorker = RunReloadWorkerAsync(_shutdown.Token);
        // Close the load-to-watch race: a mutation made after the caller's
        // initial load but before both watchers became active is caught by one
        // complete semantic reload.
        SignalReload();
    }

    public async Task<BridgeCatalogReloadResult> ReloadNowAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BridgeCatalogLoadResult loaded;
            try
            {
                loaded = await BridgeCatalog.LoadWithInstalledAsync(
                    _trustedCatalogPath,
                    _installedCatalogRoot,
                    _workerHostExecutable,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is BridgeCatalogException or
                                                   IOException or UnauthorizedAccessException)
            {
                var warnings = new[]
                {
                    $"Widget catalog reload was rejected; retained the last-good revision ({SafeCode(exception)}).",
                };
                Diagnostics?.Invoke(this, warnings);
                lock (_stateGate)
                {
                    _lastDiagnostics = warnings;
                    _retainedLastGood = true;
                    return new BridgeCatalogReloadResult(false, true, _revision, _current, warnings);
                }
            }

            if (!loaded.InstalledCatalogValid)
            {
                var warnings = loaded.Warnings.Concat(
                ["Widget catalog reload failed closed to the trusted catalog; all community widgets were retired."])
                    .Take(64)
                    .ToArray();
                BridgeCatalogChanged? failClosedChange = null;
                lock (_stateGate)
                {
                    _lastDiagnostics = warnings;
                    _retainedLastGood = false;
                    if (!_current.IsEquivalentTo(loaded.Catalog))
                    {
                        _current = loaded.Catalog;
                        checked { ++_revision; }
                        failClosedChange = new BridgeCatalogChanged(_revision, _current, warnings);
                    }
                }

                Diagnostics?.Invoke(this, warnings);
                if (failClosedChange is not null) Changed?.Invoke(this, failClosedChange);
                lock (_stateGate)
                    return new BridgeCatalogReloadResult(
                        failClosedChange is not null, false, _revision, _current, warnings);
            }

            BridgeCatalogChanged? changed = null;
            lock (_stateGate)
            {
                _lastDiagnostics = loaded.Warnings.Take(64).ToArray();
                _retainedLastGood = false;
                if (!_current.IsEquivalentTo(loaded.Catalog))
                {
                    _current = loaded.Catalog;
                    checked { ++_revision; }
                    changed = new BridgeCatalogChanged(_revision, _current, loaded.Warnings);
                }
            }

            if (loaded.Warnings.Count != 0) Diagnostics?.Invoke(this, loaded.Warnings);
            if (changed is not null) Changed?.Invoke(this, changed);
            lock (_stateGate)
                return new BridgeCatalogReloadResult(
                    changed is not null, false, _revision, _current, loaded.Warnings);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _trustedWatcher?.Dispose();
        _installedWatcher?.Dispose();
        _shutdown.Cancel();
        _reloadSignals.Writer.TryComplete();
        if (_reloadWorker is not null)
        {
            try { await _reloadWorker.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        }
        _shutdown.Dispose();
        _reloadGate.Dispose();
    }

    private FileSystemWatcher CreateWatcher(
        string directory,
        string filter,
        bool includeSubdirectories,
        Func<string, bool> accepts)
    {
        var watcher = new FileSystemWatcher(directory, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 16 * 1024,
        };
        FileSystemEventHandler onChange = (_, args) =>
        {
            if (accepts(args.FullPath)) SignalReload();
        };
        RenamedEventHandler onRenamed = (_, args) =>
        {
            if (accepts(args.FullPath) || accepts(args.OldFullPath)) SignalReload();
        };
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Deleted += onChange;
        watcher.Renamed += onRenamed;
        watcher.Error += (_, _) => SignalReload();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private bool IsTrustedCatalogChange(string path) =>
        string.Equals(Path.GetFullPath(path), _trustedCatalogPath, StringComparison.OrdinalIgnoreCase);

    private bool IsInstalledCatalogChange(string path)
    {
        var relative = Path.GetRelativePath(_installedCatalogRoot, Path.GetFullPath(path));
        if (relative.StartsWith("staging" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            relative.Equals("staging", StringComparison.OrdinalIgnoreCase) ||
            relative.Equals(".catalog-state.lock", StringComparison.OrdinalIgnoreCase) ||
            (relative.StartsWith(".catalog-state.", StringComparison.OrdinalIgnoreCase) &&
             relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
            return false;
        return relative.Equals("catalog-state.json", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("packages" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               relative.Equals("packages", StringComparison.OrdinalIgnoreCase);
    }

    private void SignalReload()
    {
        if (!_disposed) _reloadSignals.Writer.TryWrite(0);
    }

    private async Task RunReloadWorkerAsync(CancellationToken cancellationToken)
    {
        while (await _reloadSignals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_reloadSignals.Reader.TryRead(out _)) { }
            await Task.Delay(ChangeDebounce, cancellationToken).ConfigureAwait(false);
            while (_reloadSignals.Reader.TryRead(out _)) { }
            try
            {
                await ReloadNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var diagnostics = new[]
                {
                    $"Widget catalog reload failed unexpectedly ({SafeCode(exception)}).",
                };
                lock (_stateGate)
                {
                    _lastDiagnostics = diagnostics;
                    _retainedLastGood = true;
                }
                Diagnostics?.Invoke(this, diagnostics);
            }
        }
    }

    private static string SafeCode(Exception exception) => exception switch
    {
        GameBarAlternative.WidgetCatalog.WidgetPackageException package => package.Code,
        BridgeCatalogException => "invalid_trusted_catalog",
        UnauthorizedAccessException => "access_denied",
        IOException => "io_error",
        _ => "unexpected_error",
    };
}
