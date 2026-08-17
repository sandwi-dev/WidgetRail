using System.Threading.Channels;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;

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
        await fixture.Registry.DrainNotificationsAsync(initial.Id);
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
        await fixture.Registry.DrainNotificationsAsync(replacement.Id);
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

    internal static async Task ManagedReplacementRetiresBeforeMutation()
    {
        var selected = Widget("selected", worker: 'a', catalog: 'a');
        var neighbor = Widget("neighbor", worker: 'b', catalog: 'b');
        await using var fixture = new RegistryFixture(Catalog(selected, neighbor));
        await fixture.SetLifecycleAsync(selected.Id, WidgetLifecycleState.Visible);
        await fixture.SetLifecycleAsync(neighbor.Id, WidgetLifecycleState.Visible);
        var oldSelected = fixture.Clients[0];
        var neighborClient = fixture.Clients[1];
        var operationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var operationRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerCancellation = new CancellationTokenSource();
        var replace = fixture.Registry.ReplaceAsync(
            selected.Id,
            async (configured, cancellationToken) =>
            {
                RegistryAssert.Equal(selected.Id, configured.Id);
                RegistryAssert.True(oldSelected.Disposed.IsCompleted,
                    "Host mutation began before the old generation retired.");
                operationEntered.TrySetResult();
                await operationRelease.Task.WaitAsync(cancellationToken);
                return "mutated";
            },
            callerCancellation.Token);
        await operationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        callerCancellation.Cancel();
        operationRelease.TrySetResult();
        var replacement = await replace.WaitAsync(TimeSpan.FromSeconds(2));
        using (replacement.Publication)
        {
            RegistryAssert.Equal("mutated", replacement.Result);
            RegistryAssert.Equal(WidgetLifecycleState.Visible, replacement.Publication.Value);
        }
        RegistryAssert.Equal(3, fixture.Clients.Count);
        RegistryAssert.Equal(1, oldSelected.DisposeCount);
        RegistryAssert.Equal(0, neighborClient.DisposeCount);
        RegistryAssert.SequenceEqual(
            [WidgetLifecycleState.Visible], fixture.Clients[2].LifecycleStates);
    }

    internal static async Task LocalDataManagementIsExactAndDocumentBlind()
    {
        var selected = Widget("local-selected", worker: 'c', catalog: 'c');
        var neighbor = Widget("local-neighbor", worker: 'd', catalog: 'd');
        await using var fixture = new RegistryFixture(Catalog(selected, neighbor));
        await fixture.SetLifecycleAsync(selected.Id, WidgetLifecycleState.Visible);
        await fixture.SetLifecycleAsync(neighbor.Id, WidgetLifecycleState.Visible);
        var backend = new RegistryPrivateStateBackend(selected, neighbor);
        var service = new BridgeWidgetLocalDataService(fixture.Registry, backend);

        var inspection = await service.InspectAsync(selected.Id, CancellationToken.None);
        RegistryAssert.True(inspection.Exists && inspection.ConfirmationToken is not null,
            "Exact selected state was not projected as a document-blind token.");
        var result = await service.ClearAsync(
            selected.Id, inspection.ConfirmationToken!, CancellationToken.None);
        RegistryAssert.Equal(PlatformWidgetLocalDataClearStatus.Cleared, result.Status);
        RegistryAssert.True(!backend.Exists(selected.PackageId),
            "Selected state survived a successful exact clear.");
        RegistryAssert.True(backend.Exists(neighbor.PackageId),
            "Neighbor state changed during selected clear.");
        RegistryAssert.Equal(1, fixture.Clients[0].DisposeCount);
        RegistryAssert.Equal(0, fixture.Clients[1].DisposeCount);
        RegistryAssert.Equal(3, fixture.Clients.Count);

        var staleInspection = await service.InspectAsync(neighbor.Id, CancellationToken.None);
        backend.Advance(neighbor.PackageId);
        var stale = await service.ClearAsync(
            neighbor.Id, staleInspection.ConfirmationToken!, CancellationToken.None);
        RegistryAssert.Equal(PlatformWidgetLocalDataClearStatus.Stale, stale.Status);
        RegistryAssert.True(backend.Exists(neighbor.PackageId),
            "A stale confirmation cleared current neighbor state.");
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

    internal static async Task LifecycleAndFirstSnapshotAreAtomic()
    {
        var configured = Widget("presentation-admission", worker: 'p', catalog: 'p');
        await using (var fixture = new RegistryFixture(Catalog(configured)))
        {
            using var admitted = await fixture.Registry.EstablishPresentationAsync(
                configured.Id,
                WidgetLifecycleState.Visible,
                CancellationToken.None,
                CancellationToken.None);
            RegistryAssert.Equal(1, fixture.Clients.Count);
            RegistryAssert.SequenceEqual(
                [WidgetLifecycleState.Visible], fixture.Clients[0].LifecycleStates);
            RegistryAssert.Equal(1L, admitted.Value.Snapshot.Sequence >> 32);
        }

        await using var failed = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 1) client.FailSnapshots = 1;
            });
        await RegistryAssert.ThrowsAsync<InvalidOperationException>(() =>
            failed.Registry.EstablishPresentationAsync(
                configured.Id,
                WidgetLifecycleState.Interactive,
                CancellationToken.None,
                CancellationToken.None));
        var retired = failed.Clients.Single();
        await retired.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.SequenceEqual(
            [WidgetLifecycleState.Interactive], retired.LifecycleStates);
        RegistryAssert.Equal(0, failed.Registry.RunningWorkerCount);
        RegistryAssert.Equal(0, failed.Registry.ResidencyBudget.ApplicationWorkers);

        using var recovered = await failed.Registry.EstablishPresentationAsync(
            configured.Id,
            WidgetLifecycleState.Interactive,
            CancellationToken.None,
            CancellationToken.None);
        RegistryAssert.Equal(2, failed.Clients.Count);
        RegistryAssert.Equal(2L, recovered.Value.Snapshot.Sequence >> 32);
        RegistryAssert.SequenceEqual(
            [WidgetLifecycleState.Interactive], failed.Clients[1].LifecycleStates);
        retired.RaiseInvalidated(72);
        RegistryAssert.Equal(0, failed.Invalidations.Count);
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
            await eventFixture.InvalidationPublicationCancelled.WaitAsync(TimeSpan.FromSeconds(2));
            await eventFixture.InvalidationPublicationCompleted.WaitAsync(TimeSpan.FromSeconds(2));
            using var replacementPublication = await replacementOperation.WaitAsync(
                TimeSpan.FromSeconds(2));
            await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
            RegistryAssert.Equal(2, eventFixture.Clients.Count);
            RegistryAssert.Equal(0, eventFixture.Invalidations.Count);
            RegistryAssert.Equal(1, old.DisposeCount);
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

    internal static async Task NotificationLaneBoundsAndBalancesAdmission()
    {
        var failures = 0;
        var accepted = 0;
        var released = 0;
        var firstReleased = 0;
        var pendingInvalidationReleased = 0;
        var coalescedReleased = 0;
        var fullReleased = 0;
        var closedReleased = 0;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lane = new BridgeClientNotificationLane(_ => failures++);

        BridgeClientNotificationAdmission Enqueue(
            BridgeClientNotificationKind kind,
            Func<CancellationToken, Task> publish,
            Action release)
        {
            var admission = lane.Enqueue(kind, publish, release, out var startPump);
            if (admission == BridgeClientNotificationAdmission.Accepted) accepted++;
            if (startPump) lane.StartPump();
            return admission;
        }

        var first = Enqueue(
            BridgeClientNotificationKind.Invalidation,
            async cancellationToken =>
            {
                RegistryAssert.True(!lane.IsGateHeldByCurrentThread,
                    "The lane invoked external publication while holding its gate.");
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            },
            () =>
            {
                RegistryAssert.True(!lane.IsGateHeldByCurrentThread,
                    "The lane released a registry admission while holding its gate.");
                firstReleased++;
                released++;
            });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.Accepted, first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var pending = Enqueue(
            BridgeClientNotificationKind.Invalidation,
            _ => Task.CompletedTask,
            () => { pendingInvalidationReleased++; released++; });
        var coalesced = Enqueue(
            BridgeClientNotificationKind.Invalidation,
            _ => Task.CompletedTask,
            () => { coalescedReleased++; released++; });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.Accepted, pending);
        RegistryAssert.Equal(BridgeClientNotificationAdmission.Coalesced, coalesced);
        for (var index = 0; index < BridgeClientNotificationLane.MaximumPendingFailures; index++)
        {
            RegistryAssert.Equal(
                BridgeClientNotificationAdmission.Accepted,
                Enqueue(
                    BridgeClientNotificationKind.Failure,
                    _ => Task.CompletedTask,
                    () => released++));
        }
        var full = Enqueue(
            BridgeClientNotificationKind.Failure,
            _ => Task.CompletedTask,
            () => { fullReleased++; released++; });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.RejectedFull, full);
        RegistryAssert.Equal(33, lane.PendingCount);
        RegistryAssert.Equal(1, lane.DroppedFailures);
        RegistryAssert.Equal(34, accepted);

        await lane.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(34, released);
        RegistryAssert.Equal(1, firstReleased);
        RegistryAssert.Equal(1, pendingInvalidationReleased);
        RegistryAssert.Equal(0, coalescedReleased);
        RegistryAssert.Equal(0, fullReleased);
        RegistryAssert.Equal(0, failures);
        RegistryAssert.Equal(0, lane.PendingCount);

        var closed = Enqueue(
            BridgeClientNotificationKind.Failure,
            _ => Task.CompletedTask,
            () => { closedReleased++; released++; });
        RegistryAssert.Equal(BridgeClientNotificationAdmission.RejectedClosed, closed);
        RegistryAssert.Equal(34, accepted);
        RegistryAssert.Equal(34, released);
        RegistryAssert.Equal(0, closedReleased);

        var orderingEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var orderingRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<int>();
        var orderingLane = new BridgeClientNotificationLane(_ => failures++);
        RegistryAssert.Equal(
            BridgeClientNotificationAdmission.Accepted,
            orderingLane.Enqueue(
                BridgeClientNotificationKind.Invalidation,
                async cancellationToken =>
                {
                    orderingEntered.TrySetResult();
                    await orderingRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    published.Add(1);
                },
                () => { },
                out var startOrderingPump));
        RegistryAssert.True(startOrderingPump);
        orderingLane.StartPump();
        await orderingEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(
            BridgeClientNotificationAdmission.Accepted,
            orderingLane.Enqueue(
                BridgeClientNotificationKind.Invalidation,
                _ =>
                {
                    published.Add(2);
                    return Task.CompletedTask;
                },
                () => { },
                out _));
        RegistryAssert.Equal(
            BridgeClientNotificationAdmission.Coalesced,
            orderingLane.Enqueue(
                BridgeClientNotificationKind.Invalidation,
                _ =>
                {
                    published.Add(3);
                    return Task.CompletedTask;
                },
                () => { },
                out _));
        orderingRelease.TrySetResult();
        await orderingLane.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.SequenceEqual([1, 3], published);
        await orderingLane.CloseAndDrainAsync();
    }

    internal static async Task NotificationBurstIsBoundedAndRetires()
    {
        var initial = Widget("notification-burst", worker: 'b', catalog: 'b');
        var replacement = initial with
        {
            WorkerFingerprint = Fingerprint('c'),
            CatalogFingerprint = Fingerprint('c'),
        };
        await using var fixture = new RegistryFixture(Catalog(initial));
        await fixture.SetLifecycleAsync(initial.Id, WidgetLifecycleState.Visible);
        fixture.BlockInvalidationPublication = true;
        var old = fixture.Clients.Single();
        old.RaiseInvalidated(1);
        await fixture.InvalidationPublicationEntered.WaitAsync(TimeSpan.FromSeconds(2));
        for (var revision = 2; revision <= 100; revision++) old.RaiseInvalidated(revision);
        for (var index = 0; index < 100; index++) old.RaiseActionFailed($"failure-{index}");

        var bounded = fixture.Registry.NotificationStatus(initial.Id);
        RegistryAssert.Equal(33, bounded.Pending);
        RegistryAssert.Equal(68, bounded.DroppedFailures);
        RegistryAssert.Equal(34, bounded.ActivePublications);
        RegistryAssert.True(!bounded.IsRetiring);

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(replacement), revision: 1));
        await fixture.InvalidationPublicationCancelled.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.SetLifecycleAsync(replacement.Id, WidgetLifecycleState.Visible);
        await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, old.DisposeCount);
        RegistryAssert.Equal(0, fixture.Invalidations.Count);
        RegistryAssert.Equal(0, fixture.ActionFailures.Count);
        var current = fixture.Registry.NotificationStatus(replacement.Id);
        RegistryAssert.Equal(0, current.Pending);
        RegistryAssert.Equal(0, current.ActivePublications);
        RegistryAssert.Equal(0, current.DroppedFailures);
    }

    internal static async Task CancelledRestartTransfersRetirement()
    {
        var configured = Widget("restart-cancel", worker: 'd', catalog: 'd');
        await using var fixture = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) =>
            {
                if (client.ClientGeneration == 1) client.BlockDispose = true;
            });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        fixture.BlockInvalidationPublication = true;
        var old = fixture.Clients.Single();
        old.RaiseInvalidated(1);
        await fixture.InvalidationPublicationEntered.WaitAsync(TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var restart = fixture.Registry.RestartAsync(configured.Id, cancellation.Token);
        await old.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.InvalidationPublicationCancelled.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        _ = await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => restart);

        var concurrent = fixture.Registry.SetLifecycleAsync(
            configured.Id,
            WidgetLifecycleState.Visible,
            CancellationToken.None,
            CancellationToken.None);
        RegistryAssert.True(!concurrent.IsCompleted,
            "Cancelled restart released its reserved generation before exact retirement completed.");
        old.ReleaseDispose();
        using var current = await concurrent.WaitAsync(TimeSpan.FromSeconds(2));
        await old.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, old.DisposeCount);
        RegistryAssert.Equal(2, fixture.Clients.Count);
        RegistryAssert.Equal(1, fixture.Registry.RunningWorkerCount);

        var terminal = new RegistryFixture(
            Catalog(configured),
            configure: (_, client) => client.BlockDispose = true);
        await terminal.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var terminalOld = terminal.Clients.Single();
        using var terminalCancellation = new CancellationTokenSource();
        var terminalRestart = terminal.Registry.RestartAsync(
            configured.Id, terminalCancellation.Token);
        await terminalOld.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        terminalCancellation.Cancel();
        _ = await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => terminalRestart);
        var firstDispose = terminal.Registry.DisposeAsync().AsTask();
        var secondDispose = terminal.Registry.DisposeAsync().AsTask();
        RegistryAssert.True(!firstDispose.IsCompleted && !secondDispose.IsCompleted,
            "Terminal registry disposal returned before transferred restart retirement.");
        terminalOld.ReleaseDispose();
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, terminalOld.DisposeCount);
        RegistryAssert.Equal(1, terminal.Clients.Count);
        await terminal.DisposeAsync();
    }

    internal static async Task ExternalRetirementStartsOutsideIdentityGate()
    {
        var configured = Widget("external-dispose", worker: 'e', catalog: 'e');
        await using var fixture = new RegistryFixture(Catalog(configured));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var client = fixture.Clients.Single();
        client.OnDisposeStarted = () => RegistryAssert.True(
            !fixture.Registry.IsGateHeldByCurrentThread,
            "External client disposal began under the registry identity gate.");

        RegistryAssert.True(fixture.Registry.ApplyCatalog(Catalog(), revision: 1));
        await client.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, client.DisposeCount);
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
                if (configured.Id == throwing.Id)
                    client.DisposeFailure = new OutOfMemoryException(
                        "synthetic fatal disposal failure");
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

    internal static async Task LocalPackageImportOriginIsExact()
    {
        var settings = Widget("settings", worker: 'a', catalog: 'a') with
        {
            PackageId = "widgetrail.firstparty.settings",
            PublisherId = "widgetrail.firstparty",
            InstanceId = "settings.default",
            RequiresAppContainer = false,
            DeclaredCapabilities = [],
        };
        await using var fixture = new RegistryFixture(Catalog(settings));
        await fixture.SetLifecycleAsync(settings.Id, WidgetLifecycleState.Interactive);
        var descriptor = settings.PublicDescriptor();
        var exact = new BridgeLocalWidgetPackageOrigin(
            settings.Id,
            settings.PackageId,
            settings.PublisherId,
            descriptor.InstanceId,
            descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration);
        using (fixture.Registry.AdmitLocalWidgetPackageImport(exact)) { }

        await fixture.SetLifecycleAsync(settings.Id, WidgetLifecycleState.Visible);
        _ = await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => Task.Run(() =>
        {
            using var refused = fixture.Registry.AdmitLocalWidgetPackageImport(exact);
        }));

        await fixture.SetLifecycleAsync(settings.Id, WidgetLifecycleState.Interactive);
        foreach (var forged in new[]
        {
            exact with { PackageId = "dev.example.settings" },
            exact with { RuntimeGeneration = new string('f', 64) },
            exact with { PresentationGeneration = new string('e', 64) },
        })
        {
            _ = await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => Task.Run(() =>
            {
                using var refused = fixture.Registry.AdmitLocalWidgetPackageImport(forged);
            }));
        }
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
        MemoryRequestMb = 64,
        ResidencyPolicy = residency ?? new WidgetResidencyPolicy(),
        WorkerFingerprint = Fingerprint(worker),
        CatalogFingerprint = Fingerprint(catalog),
    };

    private static string Fingerprint(char value) => new(value, 64);
}

internal sealed class RegistryPrivateStateBackend(
    ConfiguredWidget first,
    ConfiguredWidget second) : IPrivateStatePlatformBrokerBackend
{
    private readonly Dictionary<string, (bool Exists, long Revision)> _state = new()
    {
        [first.PackageId] = (true, 3),
        [second.PackageId] = (true, 7),
    };

    internal bool Exists(string packageId) => _state[packageId].Exists;
    internal void Advance(string packageId)
    {
        var current = _state[packageId];
        _state[packageId] = (current.Exists, current.Revision + 1);
    }

    public Task<PrivateStateSnapshotSummary> ReadPrivateStateAsync(
        BrokerWidgetIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _state[identity.PackageId];
        return Task.FromResult(new PrivateStateSnapshotSummary(
            current.Exists,
            current.Exists ? Convert.ToBase64String("{}"u8) : null,
            current.Revision));
    }

    public Task<PrivateStateMutationSummary> ClearPrivateStateAsync(
        BrokerWidgetIdentity identity,
        ClearPrivateStateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _state[identity.PackageId];
        if (request.ExpectedRevision != current.Revision)
            throw new BrokerException("private_state_conflict", "Synthetic conflict.");
        _state[identity.PackageId] = (false, current.Revision + 1);
        return Task.FromResult(new PrivateStateMutationSummary(current.Revision + 1));
    }
}

internal sealed class RegistryFixture : IAsyncDisposable
{
    private readonly Action<ConfiguredWidget, RegistryTestClient>? _configure;
    private readonly TaskCompletionSource _invalidationPublicationEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _invalidationPublicationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _invalidationPublicationCancelled = new(
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
            async (item, cancellationToken) =>
            {
                if (BlockInvalidationPublication)
                {
                    _invalidationPublicationEntered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _invalidationPublicationCancelled.TrySetResult();
                        throw;
                    }
                    finally
                    {
                        _invalidationPublicationCompleted.TrySetResult();
                    }
                }
                Invalidations.Add(item);
            },
            (item, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ActionFailures.Add(item);
                return Task.CompletedTask;
            },
            (item, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
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
    internal Task InvalidationPublicationCancelled => _invalidationPublicationCancelled.Task;

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
    internal Exception? DisposeFailure { get; set; }
    internal int FailStartsAfterReservation { get; set; }
    internal int FailLifecycleTransitions { get; set; }
    internal int FailSnapshots { get; set; }
    internal Action? OnDisposeStarted { get; set; }
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
        if (FailSnapshots > 0)
        {
            FailSnapshots--;
            throw new InvalidOperationException("synthetic first snapshot failure");
        }
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
            OnDisposeStarted?.Invoke();
            Stop();
            _disposeEntered.TrySetResult();
            try
            {
                if (BlockDispose)
                    await _disposeRelease.Task.ConfigureAwait(false);
                if (DisposeFailure is { } failure)
                    throw failure;
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
