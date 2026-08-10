using System.Threading.Channels;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

internal static class BridgeClientRegistryScenarios
{
    internal static async Task CatalogReplacementAndRemovalOwnGenerations()
    {
        var initial = Widget("alpha", worker: 'a', catalog: 'a');
        await using var fixture = new RegistryFixture(Catalog(initial));

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        var first = fixture.Clients.Single();
        first.RaiseInvalidated(7);
        first.RaiseActionFailed("old-action");
        first.RaiseFailure();
        RegistryAssert.Equal(1, fixture.Invalidations.Count);
        RegistryAssert.Equal(1, fixture.ActionFailures.Count);
        RegistryAssert.Equal(1, fixture.Failures.Count);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        var presentationOnly = initial with
        {
            Name = "Renamed alpha",
            CatalogFingerprint = Fingerprint('b'),
        };
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(presentationOnly), revision: 1));
        RegistryAssert.Equal(1, fixture.Clients.Count);
        var compatible = fixture.Registry.DiagnosticsSnapshot();
        RegistryAssert.Equal("Renamed alpha", compatible.Workers.Single().Name);
        RegistryAssert.Equal(1L, compatible.CatalogRevision);

        var replacement = presentationOnly with
        {
            WorkerFingerprint = Fingerprint('c'),
            CatalogFingerprint = Fingerprint('c'),
        };
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(replacement), revision: 2));
        await first.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, first.DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        first.RaiseInvalidated(8);
        first.RaiseActionFailed("stale-action");
        first.RaiseFailure();
        RegistryAssert.Equal(1, fixture.Invalidations.Count);
        RegistryAssert.Equal(1, fixture.ActionFailures.Count);
        RegistryAssert.Equal(1, fixture.Failures.Count);

        await fixture.SetLifecycleAsync(replacement.Id, WidgetLifecycleState.Interactive);
        var second = fixture.Clients[1];
        second.RaiseInvalidated(9);
        RegistryAssert.Equal(2, fixture.Invalidations.Count);
        RegistryAssert.Equal(Fingerprint('c')[..32].ToLowerInvariant(),
            fixture.Registry.CatalogSnapshot().Catalog.Widgets.Single().RuntimeGeneration);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(), revision: 3));
        await second.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, second.DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        second.RaiseInvalidated(10);
        RegistryAssert.Equal(2, fixture.Invalidations.Count);
    }

    internal static async Task IdleUnloadCancellationAndReplacementAreOwned()
    {
        var delay = new ManualRegistryDelay();
        var initial = Widget(
            "idle",
            worker: 'd',
            catalog: 'd',
            residency: new WidgetResidencyPolicy
            {
                Mode = WidgetResidencyPolicies.UnloadAfterIdle,
                IdleSeconds = WidgetResidencyPolicies.MinimumIdleSeconds,
            });
        await using var fixture = new RegistryFixture(Catalog(initial), delay: delay.InvokeAsync);

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        _ = await fixture.GetSnapshotAsync(initial.Id);
        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Background);
        var cancelledByVisibility = await delay.NextAsync();

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        await cancelledByVisibility.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancelledByVisibility.Release();

        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Background);
        var cancelledByReplacement = await delay.NextAsync();
        var replacement = initial with
        {
            WorkerFingerprint = Fingerprint('e'),
            CatalogFingerprint = Fingerprint('e'),
        };
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
        await cancelledByReplacement.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.True(!fixture.Clients[0].Disposed.IsCompleted,
            "Retirement completed before its tracked idle-unload task drained.");

        cancelledByReplacement.Release();
        await fixture.Clients[0].Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(0, fixture.Clients[0].UnloadCount);
        RegistryAssert.Equal(1, fixture.Clients[0].DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
    }

    internal static async Task RestartRestoresLifecycleAndResetsGeneration()
    {
        var configured = Widget("restart", worker: 'f', catalog: 'f');
        await using var fixture = new RegistryFixture(Catalog(configured));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var old = fixture.Clients.Single();
        var oldSnapshot = await fixture.GetSnapshotAsync(configured.Id);

        var restored = await fixture.RestartAsync(configured.Id);
        RegistryAssert.Equal(WidgetLifecycleState.Visible, restored);
        await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, old.DisposeCount);
        RegistryAssert.Equal(2, fixture.Clients.Count);
        var current = fixture.Clients[1];
        RegistryAssert.SequenceEqual([WidgetLifecycleState.Visible], current.LifecycleStates);
        RegistryAssert.Equal(1, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        var currentSnapshot = await fixture.GetSnapshotAsync(configured.Id);
        RegistryAssert.True(oldSnapshot.Snapshot.Sequence != currentSnapshot.Snapshot.Sequence,
            "Restart reused the retired generation's cached snapshot.");
        old.RaiseInvalidated(90);
        RegistryAssert.Equal(0, fixture.Invalidations.Count);
    }

    internal static async Task RestartReservationAndRestoreFailureAreClosed()
    {
        var configured = Widget("restart-race", worker: '6', catalog: '6');
        await using (var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 1) client.BlockDispose = true;
            }))
        {
            await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
            var first = fixture.Clients.Single();
            var restart = fixture.Registry.RestartAsync(configured.Id, CancellationToken.None);
            await first.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
            var concurrentSnapshot = fixture.Registry.GetSnapshotAsync(
                configured.Id, CancellationToken.None, CancellationToken.None);
            RegistryAssert.Equal(1, fixture.Clients.Count);
            RegistryAssert.True(!restart.IsCompleted && !concurrentSnapshot.IsCompleted,
                "A concurrent operation created a competing generation during restart retirement.");

            first.ReleaseDispose();
            using var restartPublication = await restart.WaitAsync(TimeSpan.FromSeconds(2));
            using var snapshotPublication = await concurrentSnapshot.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.Equal(2, fixture.Clients.Count);
            RegistryAssert.Equal(WidgetLifecycleState.Visible, restartPublication.Value);
            RegistryAssert.Equal(2L, snapshotPublication.Value.Snapshot.Sequence >> 32);
            RegistryAssert.Equal(1, first.DisposeCount);
        }

        await using var failedRestore = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 2) client.FailLifecycleTransitions = 1;
            });
        await failedRestore.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        await RegistryAssert.ThrowsAsync<InvalidOperationException>(() =>
            failedRestore.RestartAsync(configured.Id));
        RegistryAssert.Equal(2, failedRestore.Clients.Count);
        RegistryAssert.Equal(1, failedRestore.Clients[0].DisposeCount);
        RegistryAssert.Equal(1, failedRestore.Clients[1].DisposeCount);
        RegistryAssert.Equal(0, failedRestore.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, failedRestore.Registry.ResidencyBudget.ApplicationWorkers);

        await failedRestore.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        RegistryAssert.Equal(3, failedRestore.Clients.Count);
        RegistryAssert.Equal(1, failedRestore.Registry.RunningWorkerCount);
        RegistryAssert.Equal(1, failedRestore.Registry.ResidencyBudget.ApplicationWorkers);
    }

    internal static async Task PublicationAdmissionSerializesReplacement()
    {
        var initial = Widget("publication", worker: '7', catalog: '7');
        var replacement = initial with
        {
            WorkerFingerprint = Fingerprint('8'),
            CatalogFingerprint = Fingerprint('8'),
        };

        await using (var eventFixture = new RegistryFixture(Catalog(initial)))
        {
            await eventFixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
            eventFixture.BlockInvalidationPublication = true;
            var old = eventFixture.Clients.Single();
            old.RaiseInvalidated(41);
            await eventFixture.InvalidationPublicationEntered.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.True(eventFixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
            var replacementOperation = eventFixture.Registry.SetLifecycleAsync(
                replacement.Id,
                WidgetLifecycleState.Visible,
                CancellationToken.None,
                CancellationToken.None);
            RegistryAssert.Equal(1, eventFixture.Clients.Count);
            RegistryAssert.True(!old.Disposed.IsCompleted && !replacementOperation.IsCompleted,
                "Replacement passed an admitted old-generation event publication.");

            eventFixture.ReleaseInvalidationPublication();
            await eventFixture.InvalidationPublicationCompleted.WaitAsync(TimeSpan.FromSeconds(2));
            using var replacementPublication = await replacementOperation.WaitAsync(
                TimeSpan.FromSeconds(2));
            await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.Equal(2, eventFixture.Clients.Count);
            RegistryAssert.Equal(1, eventFixture.Invalidations.Count);
        }

        await using var resultFixture = new RegistryFixture(Catalog(initial));
        await resultFixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        var resultPublication = await resultFixture.Registry.GetSnapshotAsync(
            initial.Id, CancellationToken.None, CancellationToken.None);
        var resultOld = resultFixture.Clients.Single();
        RegistryAssert.True(resultFixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
        var resultReplacement = resultFixture.Registry.SetLifecycleAsync(
            replacement.Id,
            WidgetLifecycleState.Visible,
            CancellationToken.None,
            CancellationToken.None);
        RegistryAssert.True(!resultOld.Disposed.IsCompleted && !resultReplacement.IsCompleted,
            "Replacement passed an admitted old-generation snapshot result.");
        RegistryAssert.Equal(1, resultFixture.Clients.Count);

        resultPublication.Dispose();
        using var resultReplacementPublication = await resultReplacement.WaitAsync(
            TimeSpan.FromSeconds(2));
        await resultOld.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(2, resultFixture.Clients.Count);
    }

    internal static async Task BudgetRefusalAndFailedStartReleaseReservations()
    {
        var first = Widget("first", worker: '1', catalog: '1');
        var second = Widget("second", worker: '2', catalog: '2');
        var failed = Widget("failed", worker: '3', catalog: '3');
        await using var fixture = new RegistryFixture(
            Catalog(first, second, failed),
            options: new WorkerResidencyBudgetOptions
            {
                MaximumApplicationWorkers = 1,
                MaximumApplicationMemoryMb = 64,
            },
            configure: (configured, client) =>
            {
                if (configured.Id == failed.Id) client.FailStartsAfterReservation = 1;
            });

        await fixture.SetLifecycleAsync(first.Id, WidgetLifecycleState.Visible);
        await RegistryAssert.ThrowsAsync<WidgetProcessAdmissionException>(() =>
            fixture.SetLifecycleAsync(second.Id, WidgetLifecycleState.Visible));
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(second, failed), revision: 1));
        await fixture.Clients.Single(client => client.WidgetId == first.Id).Disposed
            .WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        await fixture.SetLifecycleAsync(second.Id, WidgetLifecycleState.Visible);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(failed), revision: 2));
        await fixture.Clients.Single(client => client.WidgetId == second.Id).Disposed
            .WaitAsync(TimeSpan.FromSeconds(2));
        await RegistryAssert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.SetLifecycleAsync(failed.Id, WidgetLifecycleState.Visible));
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        await fixture.SetLifecycleAsync(failed.Id, WidgetLifecycleState.Visible);
        RegistryAssert.Equal(1, fixture.Registry.ResidencyBudget.ApplicationWorkers);
    }

    internal static async Task TerminalDisposalSerializesWithConcurrentOperation()
    {
        var configured = Widget("blocked", worker: '4', catalog: '4');
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.BlockSnapshots = true);

        var snapshot = fixture.Registry.GetSnapshotAsync(
            configured.Id, CancellationToken.None, CancellationToken.None);
        var client = fixture.Clients.Single();
        await client.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var firstDispose = fixture.Registry.DisposeAsync().AsTask();
        var secondDispose = fixture.Registry.DisposeAsync().AsTask();
        RegistryAssert.True(!client.Disposed.IsCompleted,
            "Terminal disposal bypassed the in-flight operation gate.");
        RegistryAssert.True(!firstDispose.IsCompleted && !secondDispose.IsCompleted,
            "A concurrent disposer returned before the shared terminal boundary.");

        client.ReleaseSnapshot();
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => snapshot);
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, client.DisposeCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
        client.RaiseInvalidated(11);
        client.RaiseActionFailed("stale");
        client.RaiseFailure();
        RegistryAssert.Equal(0, fixture.Invalidations.Count);
        RegistryAssert.Equal(0, fixture.ActionFailures.Count);
        RegistryAssert.Equal(0, fixture.Failures.Count);
    }

    internal static async Task RetirementFailuresAreObservedAndDrained()
    {
        var throwing = Widget("throwing", worker: '9', catalog: '9');
        var healthy = Widget("healthy", worker: 'a', catalog: 'a');
        var fixture = new RegistryFixture(
            Catalog(throwing, healthy),
            configure: (configured, client) =>
            {
                if (configured.Id == throwing.Id) client.ThrowOnDispose = true;
            });
        await fixture.SetLifecycleAsync(throwing.Id, WidgetLifecycleState.Visible);
        await fixture.SetLifecycleAsync(healthy.Id, WidgetLifecycleState.Visible);
        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(), revision: 1));
        await Task.WhenAll(fixture.Clients.Select(client => client.Disposed))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var firstDispose = fixture.Registry.DisposeAsync().AsTask();
        var secondDispose = fixture.Registry.DisposeAsync().AsTask();
        _ = await RegistryAssert.ThrowsAsync<AggregateException>(() => firstDispose);
        _ = await RegistryAssert.ThrowsAsync<AggregateException>(() => secondDispose);
        RegistryAssert.SequenceEqual([1, 1], fixture.Clients.Select(client => client.DisposeCount));
        RegistryAssert.Equal(0, fixture.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, fixture.Registry.ResidencyBudget.ApplicationWorkers);
    }

    private static BridgeCatalog Catalog(params ConfiguredWidget[] widgets) => new(widgets);

    private static ConfiguredWidget Widget(
        string id,
        char worker,
        char catalog,
        WidgetResidencyPolicy? residency = null) => new()
    {
        Id = id,
        PackageId = $"dev.example.{id}",
        PublisherId = "dev.example",
        Name = id,
        InstanceId = $"{id}.instance",
        WorkerExecutable = Environment.ProcessPath!,
        MemoryLimitMb = 64,
        ResidencyPolicy = residency ?? new WidgetResidencyPolicy(),
        WorkerFingerprint = Fingerprint(worker),
        CatalogFingerprint = Fingerprint(catalog),
    };

    private static string Fingerprint(char value) => new(value, 64);
}

internal sealed class RegistryFixture : IAsyncDisposable
{
    private readonly Action<ConfiguredWidget, RegistryTestClient>? _configure;
    private readonly TaskCompletionSource _invalidationPublicationEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _invalidationPublicationRelease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _invalidationPublicationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal RegistryFixture(
        BridgeCatalog catalog,
        WorkerResidencyBudgetOptions? options = null,
        Action<ConfiguredWidget, RegistryTestClient>? configure = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _configure = configure;
        Registry = new BridgeClientRegistry(
            catalog,
            options ?? new WorkerResidencyBudgetOptions(),
            CreateClient,
            async item =>
            {
                Invalidations.Add(item);
                if (!BlockInvalidationPublication) return;
                _invalidationPublicationEntered.TrySetResult();
                try
                {
                    await _invalidationPublicationRelease.Task.ConfigureAwait(false);
                }
                finally
                {
                    _invalidationPublicationCompleted.TrySetResult();
                }
            },
            item =>
            {
                ActionFailures.Add(item);
                return Task.CompletedTask;
            },
            item =>
            {
                Failures.Add(item);
                return Task.CompletedTask;
            },
            delay);
    }

    internal BridgeClientRegistry Registry { get; }
    internal List<RegistryTestClient> Clients { get; } = [];
    internal List<BridgeClientInvalidation> Invalidations { get; } = [];
    internal List<BridgeClientActionFailure> ActionFailures { get; } = [];
    internal List<BridgeClientRuntimeFailure> Failures { get; } = [];
    internal bool BlockInvalidationPublication { get; set; }
    internal Task InvalidationPublicationEntered => _invalidationPublicationEntered.Task;
    internal Task InvalidationPublicationCompleted => _invalidationPublicationCompleted.Task;

    public ValueTask DisposeAsync() => Registry.DisposeAsync();

    internal async Task SetLifecycleAsync(string widgetId, WidgetLifecycleState state)
    {
        using var publication = await Registry.SetLifecycleAsync(
            widgetId, state, CancellationToken.None, CancellationToken.None);
    }

    internal async Task<BridgeClientSnapshot> GetSnapshotAsync(string widgetId)
    {
        using var publication = await Registry.GetSnapshotAsync(
            widgetId, CancellationToken.None, CancellationToken.None);
        return publication.Value;
    }

    internal async Task<WidgetLifecycleState> RestartAsync(string widgetId)
    {
        using var publication = await Registry.RestartAsync(
            widgetId, CancellationToken.None);
        return publication.Value;
    }

    internal void ReleaseInvalidationPublication() =>
        _invalidationPublicationRelease.TrySetResult();

    private IBridgeWidgetClient CreateClient(
        ConfiguredWidget configured,
        Func<IDisposable> reserve)
    {
        var client = new RegistryTestClient(
            configured.Id,
            configured.InstanceId,
            Clients.Count + 1,
            reserve);
        _configure?.Invoke(configured, client);
        Clients.Add(client);
        return client;
    }
}

internal sealed class RegistryTestClient(
    string widgetId,
    string instanceId,
    int clientGeneration,
    Func<IDisposable> reserve) : IBridgeWidgetClient
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _snapshotEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _snapshotRelease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeRelease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private IDisposable? _reservation;
    private int _running;
    private int _starts;
    private int _disposeCount;
    private int _unloadCount;
    private long _snapshotSequence;

    public event EventHandler<long>? Invalidated;
    public event EventHandler<WidgetActionFailure>? ActionFailed;
    public event EventHandler<WidgetFailure>? Failed;

    internal string WidgetId { get; } = widgetId;
    internal int ClientGeneration { get; } = clientGeneration;
    internal bool BlockSnapshots { get; set; }
    internal bool BlockDispose { get; set; }
    internal bool ThrowOnDispose { get; set; }
    internal int FailStartsAfterReservation { get; set; }
    internal int FailLifecycleTransitions { get; set; }
    internal Task SnapshotEntered => _snapshotEntered.Task;
    internal Task Disposed => _disposed.Task;
    internal Task DisposeEntered => _disposeEntered.Task;
    internal int DisposeCount => Volatile.Read(ref _disposeCount);
    internal int UnloadCount => Volatile.Read(ref _unloadCount);
    internal List<WidgetLifecycleState> LifecycleStates { get; } = [];
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public int Starts => Volatile.Read(ref _starts);

    public async Task<ViewSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        if (BlockSnapshots)
        {
            _snapshotEntered.TrySetResult();
            await _snapshotRelease.Task.ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = Interlocked.Increment(ref _snapshotSequence) +
            ((long)ClientGeneration << 32);
        return new ViewSnapshot
        {
            Sequence = sequence,
            WidgetInstanceId = instanceId,
            ActiveInputScopeId = "root",
            Root = new ViewNode
            {
                Id = "root",
                Kind = ViewNodeKind.Stack,
                InputScopeId = "root",
            },
        };
    }

    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (state != WidgetLifecycleState.Background) EnsureStarted();
        if (FailLifecycleTransitions > 0)
        {
            FailLifecycleTransitions--;
            throw new InvalidOperationException("synthetic lifecycle restore failure");
        }
        lock (_gate) LifecycleStates.Add(state);
        return Task.CompletedTask;
    }

    public Task<WidgetOperationAdmission> AdmitActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        return Task.FromResult(WidgetOperationAdmission.Enqueued);
    }

    public Task<bool> SendControllerInputAsync(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority? authority,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted();
        return Task.FromResult(true);
    }

    public Task UnloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _unloadCount);
        Stop();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Increment(ref _disposeCount) == 1)
        {
            Stop();
            _disposeEntered.TrySetResult();
            try
            {
                if (BlockDispose)
                    await _disposeRelease.Task.ConfigureAwait(false);
                if (ThrowOnDispose)
                    throw new ApplicationException("synthetic disposal failure");
            }
            finally
            {
                _disposed.TrySetResult();
            }
        }
    }

    internal void ReleaseSnapshot() => _snapshotRelease.TrySetResult();
    internal void ReleaseDispose() => _disposeRelease.TrySetResult();
    internal void RaiseInvalidated(long revision) => Invalidated?.Invoke(this, revision);
    internal void RaiseActionFailed(string actionId) => ActionFailed?.Invoke(
        this, new WidgetActionFailure(actionId, "source", "failed"));
    internal void RaiseFailure() => Failed?.Invoke(
        this,
        new WidgetFailure(
            WidgetFailureReason.ProcessExited,
            17,
            null,
            RestartsUsed: 0,
            CanRestart: true));

    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_running != 0) return;
            var lease = reserve();
            if (FailStartsAfterReservation > 0)
            {
                FailStartsAfterReservation--;
                lease.Dispose();
                throw new InvalidOperationException("synthetic failed start");
            }
            _reservation = lease;
            _running = 1;
            _starts++;
        }
    }

    private void Stop()
    {
        IDisposable? reservation;
        lock (_gate)
        {
            _running = 0;
            reservation = _reservation;
            _reservation = null;
        }
        reservation?.Dispose();
    }
}

internal sealed class ManualRegistryDelay
{
    private readonly Channel<ManualRegistryDelayCall> _calls =
        Channel.CreateUnbounded<ManualRegistryDelayCall>();

    internal Task InvokeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var call = new ManualRegistryDelayCall(cancellationToken);
        if (!_calls.Writer.TryWrite(call))
            throw new InvalidOperationException("Unable to publish manual delay call.");
        return call.WaitAsync();
    }

    internal ValueTask<ManualRegistryDelayCall> NextAsync() =>
        _calls.Reader.ReadAsync();
}

internal sealed class ManualRegistryDelayCall
{
    private readonly TaskCompletionSource _release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _registration;

    internal ManualRegistryDelayCall(CancellationToken cancellationToken)
    {
        _registration = cancellationToken.Register(
            () => CancellationObserved.TrySetResult());
    }

    internal TaskCompletionSource CancellationObserved { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal async Task WaitAsync()
    {
        try { await _release.Task.ConfigureAwait(false); }
        finally { _registration.Dispose(); }
    }

    internal void Release() => _release.TrySetResult();
}

internal static class RegistryAssert
{
    internal static void True(bool condition, string message = "Expected true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
