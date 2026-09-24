using System.Threading.Channels;

namespace WidgetRail.WidgetBridge;

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
    bool RetainedLastGood,
    bool InstalledCatalogPending)
{
    internal IReadOnlyList<BridgeCatalogWidgetRejection> WidgetRejections { get; init; } = [];
}

internal readonly record struct BridgeCatalogStateSnapshot(
    BridgeCatalog Catalog,
    long Revision);

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
    private readonly string? _settingsFilePath;
    private FileSystemWatcher? _settingsWatcher;
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
    private readonly Func<CancellationToken, Task<BridgeCatalogLoadResult>> _loadCatalog;
    private readonly Action<BridgeInstalledCatalogObservation>? _catalogLoadObserved;
    private readonly Func<CancellationToken, Task> _prepareWatchers;
    private readonly Action? _beforePublicationCheck;

    private BridgeCatalog _current;
    private IReadOnlyList<string> _lastDiagnostics;
    private long _revision;
    private bool _retainedLastGood;
    private bool _installedCatalogPending;
    private IReadOnlyList<BridgeCatalogWidgetRejection> _widgetRejections;
    private long _reloadDemandGeneration;
    private bool _pendingForceRevision;
    private FileSystemWatcher? _trustedWatcher;
    private FileSystemWatcher? _installedWatcher;
    private Task? _reloadWorker;
    private Task? _watcherSetupWorker;
    private bool _started;
    private bool _disposed;

    public BridgeCatalogMonitor(
        string trustedCatalogPath,
        string installedCatalogRoot,
        string workerHostExecutable,
        BridgeCatalog initialCatalog,
        IReadOnlyList<string>? initialDiagnostics = null)
        : this(
            trustedCatalogPath,
            installedCatalogRoot,
            workerHostExecutable,
            initialCatalog,
            initialDiagnostics,
            installedCatalogPending: false,
            loadCatalog: null,
            catalogLoadObserved: null,
            prepareWatchers: null,
            beforePublicationCheck: null,
            initialWidgetRejections: null)
    {
    }

    internal BridgeCatalogMonitor(
        string trustedCatalogPath,
        string installedCatalogRoot,
        string workerHostExecutable,
        BridgeCatalog initialCatalog,
        IReadOnlyList<string>? initialDiagnostics,
        bool installedCatalogPending,
        Func<CancellationToken, Task<BridgeCatalogLoadResult>>? loadCatalog,
        Action<BridgeInstalledCatalogObservation>? catalogLoadObserved = null,
        Func<CancellationToken, Task>? prepareWatchers = null,
        Action? beforePublicationCheck = null,
        IReadOnlyList<BridgeCatalogWidgetRejection>? initialWidgetRejections = null,
        string? settingsFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedCatalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedCatalogRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerHostExecutable);
        _trustedCatalogPath = Path.GetFullPath(trustedCatalogPath);
        _installedCatalogRoot = Path.GetFullPath(installedCatalogRoot);
        _workerHostExecutable = Path.GetFullPath(workerHostExecutable);
        _settingsFilePath = settingsFilePath;
        _current = (initialCatalog ?? throw new ArgumentNullException(nameof(initialCatalog)))
            .WithCompleteness(!installedCatalogPending);
        _lastDiagnostics = initialDiagnostics?.Take(64).ToArray() ?? [];
        _installedCatalogPending = installedCatalogPending;
        _widgetRejections = initialWidgetRejections?.Take(256).ToArray() ?? [];
        _catalogLoadObserved = catalogLoadObserved;
        _prepareWatchers = prepareWatchers ?? PrepareWatchersAsync;
        _beforePublicationCheck = beforePublicationCheck;
        _loadCatalog = loadCatalog ?? (cancellationToken =>
            BridgeCatalog.LoadWithInstalledObservedAsync(
                _trustedCatalogPath,
                _installedCatalogRoot,
                _workerHostExecutable,
                cancellationToken));
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
            return new(
                _revision,
                _lastDiagnostics.Count,
                _retainedLastGood,
                _installedCatalogPending)
            {
                WidgetRejections = _widgetRejections,
            };
    }

    internal BridgeCatalogStateSnapshot StateSnapshot()
    {
        lock (_stateGate) return new(_current, _revision);
    }

    internal string InstalledCatalogRoot => _installedCatalogRoot;
    internal BridgeCatalog LoadTrustedForManagement() =>
        BridgeCatalog.LoadTrusted(_trustedCatalogPath, _installedCatalogRoot);
    internal async Task<bool> IsBuiltInDisabledForManagementAsync(string packageId, CancellationToken cancellationToken)
    {
        if (_settingsFilePath is null) return false;
        var paths = new WidgetRail.PlatformSettings.PlatformSettingsPaths(Path.GetDirectoryName(_settingsFilePath)!);
        if (!string.Equals(paths.SettingsFile, _settingsFilePath, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var settings = await new WidgetRail.PlatformSettings.PlatformSettingsStore(paths)
                .LoadAsync(cancellationToken).ConfigureAwait(false);
            return !settings.BuiltInWidgets.IsEnabled(packageId);
        }
        catch (WidgetRail.PlatformSettings.PlatformSettingsException) { return false; }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        _reloadWorker = RunReloadWorkerAsync(_shutdown.Token);
        _watcherSetupWorker = RunWatcherSetupWorkerAsync(_shutdown.Token);
        // Close the load-to-watch race: a mutation made after the caller's
        // trusted-only startup but before both watchers become active is caught
        // by a complete semantic reload. Watcher creation itself is background
        // work and therefore cannot delay the trusted control pipe.
        SignalReload(forceRevision: false);
    }

    public Task<BridgeCatalogReloadResult> ReloadNowAsync(
        CancellationToken cancellationToken = default) =>
        ReloadDirectAsync(
            requestedForceRevision: false,
            cancellationToken);

    internal Task<BridgeCatalogReloadResult> ReloadAfterMutationAsync(
        CancellationToken cancellationToken = default) =>
        ReloadDirectAsync(
            requestedForceRevision: true,
            cancellationToken);

    private async Task<BridgeCatalogReloadResult> ReloadDirectAsync(
        bool requestedForceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var demand = AdmitGuaranteedReloadDemand(requestedForceRevision);
        using var cancellationHandoff = cancellationToken.Register(
            () => SignalReload(demand.ForceRevision));
        return await ReloadNowCoreAsync(
            requestedForceRevision,
            demand,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<BridgeCatalogReloadResult> ReloadNowCoreAsync(
        bool requestedForceRevision,
        ReloadDemand? admittedDemand,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource reloadCancellation;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            reloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _shutdown.Token);
        }
        using var disposeReloadCancellation = reloadCancellation;
        await _reloadGate.WaitAsync(reloadCancellation.Token).ConfigureAwait(false);
        var demand = admittedDemand ?? AdmitGuaranteedReloadDemand(requestedForceRevision);
        try
        {
            BridgeCatalogLoadResult? loaded = null;
            IReadOnlyList<string>? loadFailureWarnings = null;
            try
            {
                loaded = await _loadCatalog(reloadCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is BridgeCatalogException or WidgetRail.PlatformSettings.PlatformSettingsException or
                                                   IOException or UnauthorizedAccessException)
            {
                loadFailureWarnings =
                [
                    $"Widget catalog reload was rejected; retained the last-good revision ({SafeCode(exception)}).",
                ];
            }

            _beforePublicationCheck?.Invoke();
            BridgeCatalogChanged? changed = null;
            IReadOnlyList<string>? diagnosticsToPublish = null;
            BridgeInstalledCatalogObservation? installedCatalogObservationToPublish = null;
            BridgeCatalogReloadResult result;
            lock (_stateGate)
            {
                if (demand.Generation != _reloadDemandGeneration)
                    return new BridgeCatalogReloadResult(
                        false, true, _revision, _current,
                        ["Widget catalog reload result was superseded; retained the current revision."]);

                installedCatalogObservationToPublish = loaded?.InstalledCatalogObservation;
                if (loadFailureWarnings is not null)
                {
                    _lastDiagnostics = loadFailureWarnings;
                    _retainedLastGood = true;
                    _installedCatalogPending = false;
                    diagnosticsToPublish = loadFailureWarnings;
                    result = new BridgeCatalogReloadResult(
                        false, true, _revision, _current, loadFailureWarnings);
                }
                else if (!loaded!.InstalledCatalogValid)
                {
                    var incomplete = loaded.Catalog.WithCompleteness(false);
                    var warnings = loaded.Warnings.Concat(
                    ["Widget catalog reload failed closed to the trusted catalog; all community widgets were retired."])
                        .Take(64)
                        .ToArray();
                    _lastDiagnostics = warnings;
                    _retainedLastGood = false;
                    _installedCatalogPending = false;
                    _widgetRejections = loaded.WidgetRejections.Take(256).ToArray();
                    if (demand.ForceRevision || !_current.IsEquivalentTo(incomplete))
                    {
                        _current = incomplete;
                        checked { ++_revision; }
                        changed = new BridgeCatalogChanged(_revision, _current, warnings);
                        _pendingForceRevision = false;
                    }
                    diagnosticsToPublish = warnings;
                    result = new BridgeCatalogReloadResult(
                        changed is not null, false, _revision, _current, warnings);
                }
                else
                {
                    var complete = loaded.Catalog.WithCompleteness(true);
                    _lastDiagnostics = loaded.Warnings.Take(64).ToArray();
                    _retainedLastGood = false;
                    _widgetRejections = loaded.WidgetRejections.Take(256).ToArray();
                    if (demand.ForceRevision || _installedCatalogPending ||
                        !_current.IsEquivalentTo(complete))
                    {
                        _current = complete;
                        checked { ++_revision; }
                        changed = new BridgeCatalogChanged(
                            _revision, _current, loaded.Warnings);
                    }
                    _installedCatalogPending = false;
                    _pendingForceRevision = false;
                    if (loaded.Warnings.Count != 0)
                        diagnosticsToPublish = loaded.Warnings;
                    result = new BridgeCatalogReloadResult(
                        changed is not null, false, _revision, _current, loaded.Warnings);
                }
            }

            if (installedCatalogObservationToPublish is { } installedCatalogObservation)
                _catalogLoadObserved?.Invoke(installedCatalogObservation);
            if (diagnosticsToPublish is not null)
                Diagnostics?.Invoke(this, diagnosticsToPublish);
            if (changed is not null) Changed?.Invoke(this, changed);
            return result;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _shutdown.Cancel();
        _reloadSignals.Writer.TryComplete();
        if (_reloadWorker is not null)
        {
            try { await _reloadWorker.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        }
        if (_watcherSetupWorker is not null)
        {
            try { await _watcherSetupWorker.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        }
        _trustedWatcher?.Dispose();
        _installedWatcher?.Dispose();
        _settingsWatcher?.Dispose();
        await _reloadGate.WaitAsync().ConfigureAwait(false);
        _reloadGate.Release();
        _shutdown.Dispose();
        _reloadGate.Dispose();
    }

    private Task PrepareWatchersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_trustedWatcher is not null && _installedWatcher is not null &&
            (_settingsFilePath is null || _settingsWatcher is not null))
            return Task.CompletedTask;
        var trustedDirectory = Path.GetDirectoryName(_trustedCatalogPath)
            ?? throw new InvalidOperationException("Trusted catalog path has no parent directory.");
        Directory.CreateDirectory(_installedCatalogRoot);
        FileSystemWatcher? trusted = null;
        FileSystemWatcher? installed = null;
        try
        {
            trusted = CreateWatcher(
                trustedDirectory,
                Path.GetFileName(_trustedCatalogPath),
                includeSubdirectories: false,
                IsTrustedCatalogChange);
            installed = CreateWatcher(
                _installedCatalogRoot,
                "*",
                includeSubdirectories: true,
                IsInstalledCatalogChange);
            _trustedWatcher = trusted;
            _installedWatcher = installed;
            if (_settingsFilePath is not null)
            {
                var directory = Path.GetDirectoryName(_settingsFilePath)!;
                Directory.CreateDirectory(directory);
                _settingsWatcher = CreateWatcher(directory, Path.GetFileName(_settingsFilePath), false,
                    path => string.Equals(path, _settingsFilePath, StringComparison.OrdinalIgnoreCase));
            }
            return Task.CompletedTask;
        }
        catch
        {
            trusted?.Dispose();
            installed?.Dispose();
            throw;
        }
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
            if (accepts(args.FullPath)) SignalReload(forceRevision: false);
        };
        RenamedEventHandler onRenamed = (_, args) =>
        {
            if (accepts(args.FullPath) || accepts(args.OldFullPath))
                SignalReload(forceRevision: false);
        };
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Deleted += onChange;
        watcher.Renamed += onRenamed;
        watcher.Error += (_, _) => SignalReload(forceRevision: false);
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

    private void SignalReload(bool forceRevision)
    {
        lock (_stateGate)
        {
            if (_disposed) return;
            checked { ++_reloadDemandGeneration; }
            _pendingForceRevision |= forceRevision;
        }
        _reloadSignals.Writer.TryWrite(0);
    }

    internal Task<BridgeCatalogReloadResult> ReloadGuaranteedForTesting(
        bool forceRevision = false) =>
        ReloadNowCoreAsync(
            requestedForceRevision: false,
            AdmitGuaranteedReloadDemand(forceRevision),
            CancellationToken.None);

    private ReloadDemand AdmitGuaranteedReloadDemand(bool forceRevision)
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            checked { ++_reloadDemandGeneration; }
            _pendingForceRevision |= forceRevision;
            return new ReloadDemand(_reloadDemandGeneration, _pendingForceRevision);
        }
    }

    private ReloadDemand CurrentReloadDemand()
    {
        lock (_stateGate)
            return new ReloadDemand(_reloadDemandGeneration, _pendingForceRevision);
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
                var demand = CurrentReloadDemand();
                await ReloadNowCoreAsync(
                    requestedForceRevision: false,
                    demand,
                    cancellationToken).ConfigureAwait(false);
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

    private async Task RunWatcherSetupWorkerAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        var retryDelay = TimeSpan.FromMilliseconds(250);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _prepareWatchers(cancellationToken).ConfigureAwait(false);
                SignalReload(forceRevision: false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var diagnostics = new[]
                {
                    $"Widget catalog watcher setup is unavailable; background retry remains active ({SafeCode(exception)}).",
                };
                lock (_stateGate)
                {
                    if (_disposed) return;
                    _lastDiagnostics = diagnostics;
                    _retainedLastGood = true;
                }
                Diagnostics?.Invoke(this, diagnostics);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromMilliseconds(Math.Min(
                    retryDelay.TotalMilliseconds * 2,
                    30_000));
            }
        }
    }

    private readonly record struct ReloadDemand(long Generation, bool ForceRevision);

    private static string SafeCode(Exception exception) => exception switch
    {
        WidgetRail.WidgetCatalog.WidgetPackageException package => package.Code,
        BridgeCatalogException => "invalid_trusted_catalog",
        UnauthorizedAccessException => "access_denied",
        IOException => "io_error",
        _ => "unexpected_error",
    };
}
