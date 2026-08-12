using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetRuntime;

internal static class BridgeDiagnosticsScenarios
{
    internal static async Task ProjectionIsBoundedSanitizedAndReadOnly()
    {
        const string token = "0123456789ABCDEF0123456789ABCDEF";
        const string isolationKey = "publisher.example:widget.example";
        var profileName = WindowsAppContainer.ProfileNameFor(isolationKey);
        var catalog = Catalog(isolationKey, "Tools: Audio", "2.1.0");
        var recoveryService = new ControlledRecoveryService(
        [
            new AppContainerAuthorityRecoveryCandidate(token, profileName, 3, IsLegacy: false),
        ]);
        var source = new ControlledDiagnosticsSource(Model(catalog) with
        {
            CatalogDiagnosticCount = 3,
            CatalogRetainedLastGood = true,
            Appearance = new(true, 7, 2),
            ProvidersConfigured = false,
        })
        {
            Consent = _ => ValueTask.FromException<BridgeConsentDiagnostic>(
                new IOException("C:\\private\\consent.json")),
        };
        var recovery = new BridgeAuthorityRecoveryProjection(recoveryService);
        var projection = new BridgeDiagnosticsProjection(source, () => recovery);

        var first = await projection.CreateAsync(CancellationToken.None);
        var second = await projection.CreateAsync(CancellationToken.None);
        Equal(1L, first.Revision, "The first diagnostic revision was not deterministic.");
        Equal(2L, second.Revision, "The second diagnostic revision did not advance once.");
        Equal(PlatformDiagnosticState.Degraded, first.Catalog.State,
            "Retained catalog state was not projected as degraded.");
        Equal(PlatformDiagnosticState.Degraded, first.Appearance.State,
            "Appearance errors were not projected as degraded.");
        Equal(PlatformDiagnosticState.Degraded, first.Consent.State,
            "A consent read failure replaced the complete diagnostic snapshot.");
        Contains("io_error", first.Consent.Summary,
            "The consent failure was not reduced to a safe code.");
        Equal(PlatformDiagnosticState.Unavailable, first.Providers.State,
            "An absent provider backend was not projected as unavailable.");
        Equal(1, first.Workers.Count,
            "Malformed worker input was not omitted from the bounded projection.");
        Equal("Safe widget", first.Workers[0].WidgetName,
            "The safe worker projection changed unexpectedly.");
        var authority = first.AuthorityRecoveries.Single();
        Equal("Community widget 2.1.0", authority.DisplayName,
            "Unsafe catalog text escaped the recovery-label fallback.");
        Equal(token, authority.ConfirmationToken,
            "The exact host-owned confirmation token was not retained privately.");
        var serialized = JsonSerializer.Serialize(first);
        Require(!serialized.Contains(profileName, StringComparison.Ordinal),
            "The private AppContainer profile escaped diagnostics.");
        Require(!serialized.Contains("C:\\private", StringComparison.OrdinalIgnoreCase),
            "A private filesystem path escaped diagnostics.");
        Equal(2, source.Captures, "Snapshot projection did not use one source capture per revision.");
        Equal(2, source.ConsentReads, "Consent projection performed an unexpected number of reads.");
        Equal(2, recoveryService.ListCalls, "Recovery projection performed an unexpected number of reads.");
        Equal(0, recoveryService.RetryCalls, "Read-only diagnostics invoked recovery mutation.");
    }

    internal static async Task PartialFailureMalformedInputAndDeadlineAreClosed()
    {
        var catalog = Catalog();
        var invalidRecovery = new ControlledRecoveryService(
        [
            new AppContainerAuthorityRecoveryCandidate(
                "invalid-lowercase-token", "profile", 0, IsLegacy: false),
        ]);
        var source = new ControlledDiagnosticsSource(Model(catalog) with
        {
            CatalogDiagnosticRevision = 9,
        })
        {
            Consent = _ => ValueTask.FromResult(new BridgeConsentDiagnostic(
                true, -1, 1, 2)),
        };
        var recovery = new BridgeAuthorityRecoveryProjection(invalidRecovery);
        var projection = new BridgeDiagnosticsProjection(source, () => recovery);
        var malformed = await projection.CreateAsync(CancellationToken.None);
        Equal(PlatformDiagnosticState.Degraded, malformed.Consent.State,
            "Malformed consent state did not fail closed locally.");
        Equal(PlatformDiagnosticState.Degraded, malformed.Catalog.State,
            "A stale monitor/catalog revision was not exposed as pending reconciliation.");
        Contains("reload revision 9 awaits reconciliation", malformed.Catalog.Summary,
            "Stale catalog diagnostics were attributed to the wrong committed revision.");
        Equal("recovery_state_invalid", malformed.AuthorityRecoveries.Single().StatusCode,
            "Malformed recovery state was partially projected.");
        Require(malformed.AuthorityRecoveries.Single().ConfirmationToken is null,
            "Malformed recovery state retained an authorizing token.");

        var blocked = new TaskCompletionSource<BridgeConsentDiagnostic>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.Consent = async _ =>
        {
            started.TrySetResult();
            try { return await blocked.Task.ConfigureAwait(false); }
            finally { completed.TrySetResult(); }
        };
        using var deadline = new CancellationTokenSource();
        var cancelled = projection.CreateAsync(deadline.Token).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        deadline.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => cancelled);
        blocked.TrySetResult(new(true, 8, 2, 0));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        source.Consent = _ => ValueTask.FromResult(new BridgeConsentDiagnostic(true, 9, 2, 0));
        var after = await projection.CreateAsync(CancellationToken.None);
        Equal(2L, after.Revision,
            "A canceled diagnostic attempt consumed or duplicated a committed revision.");
    }

    internal static async Task RecoveryRetryIsExactBoundedAndCancellationSafe()
    {
        const string token = "0123456789ABCDEF0123456789ABCDEF";
        var candidates = Enumerable.Range(1, 70)
            .Select(index => new AppContainerAuthorityRecoveryCandidate(
                index.ToString("X32"), $"unknown-{index}", index, IsLegacy: false))
            .ToArray();
        var service = new ControlledRecoveryService(candidates);
        var recovery = new BridgeAuthorityRecoveryProjection(service);
        var projected = recovery.ProjectPending(Catalog());
        Equal(PlatformDiagnosticsSnapshot.MaximumAuthorityRecoveries, projected.Count,
            "Recovery projection exceeded its public bound.");
        Require(projected.All(item => item.DisplayName == "Community widget recovery"),
            "A stale catalog mapping exposed an untrusted profile as display text.");
        Require(projected.SequenceEqual(projected.OrderBy(item => item.ConfirmationToken,
                StringComparer.Ordinal)),
            "Recovery projection order was not deterministic.");

        var refused = await recovery.RetryAsync("lowercase", CancellationToken.None);
        Equal(PlatformAuthorityRecoveryRetryStatus.Refused, refused.Status,
            "Malformed retry authority was not refused before the service boundary.");
        Equal(0, service.RetryCalls, "Malformed retry authority reached the recovery service.");

        var result = await recovery.RetryAsync(token, CancellationToken.None);
        Equal(PlatformAuthorityRecoveryRetryStatus.Recovered, result.Status,
            "Verified recovery did not report success.");
        Equal(token, service.LastRetriedToken,
            "Recovery did not forward the exact host-owned confirmation token.");

        service.RetryFailure = new AppContainerAuthorityRecoveryException("stale_confirmation");
        result = await recovery.RetryAsync(token, CancellationToken.None);
        Equal(PlatformAuthorityRecoveryRetryStatus.Stale, result.Status,
            "Stale recovery authority was not distinguished.");
        service.RetryFailure = new AppContainerAuthorityRecoveryException("recovery_not_verified");
        result = await recovery.RetryAsync(token, CancellationToken.None);
        Equal(PlatformAuthorityRecoveryRetryStatus.StillPending, result.Status,
            "Failed verification did not retain pending state.");
        service.RetryFailure = new UnauthorizedAccessException("private principal");
        result = await recovery.RetryAsync(token, CancellationToken.None);
        Equal(PlatformAuthorityRecoveryRetryStatus.Unavailable, result.Status,
            "Unavailable journal authority was not sanitized.");

        var blocking = new BlockingRecoveryService();
        var cancelProjection = new BridgeAuthorityRecoveryProjection(blocking);
        using var cancellation = new CancellationTokenSource();
        var cancelled = cancelProjection.RetryAsync(token, cancellation.Token).AsTask();
        await blocking.Started.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => cancelled);
        blocking.Release();
        await blocking.Completed.WaitAsync(TimeSpan.FromSeconds(1));
        Require(!blocking.Committed,
            "A canceled recovery committed its journal-clear decision.");

        using var commitCancellation = new CancellationTokenSource();
        var winner = new CommitWinningRecoveryService(commitCancellation);
        result = await new BridgeAuthorityRecoveryProjection(winner)
            .RetryAsync(token, commitCancellation.Token);
        Equal(PlatformAuthorityRecoveryRetryStatus.Recovered, result.Status,
            "A commit-winning recovery was incorrectly reported as canceled.");
        Require(winner.Committed,
            "Verified recovery did not win the atomic commit decision.");
    }

    private static BridgeDiagnosticsReadModel Model(BridgeCatalog catalog) => new(
        new BridgeClientRegistrySnapshot(
            catalog,
            4,
            [
                new BridgeClientWorkerStatus("safe-widget", "Safe widget", true, 2, null, false),
                new BridgeClientWorkerStatus("unsafe widget", "bad\r\nname", false, -1,
                    "C:\\private\\failure", true),
            ],
            new WorkerResidencyBudgetSnapshot(1, 48, 8, 1, 32)),
        CatalogDiagnosticRevision: 4,
        CatalogDiagnosticCount: 0,
        CatalogRetainedLastGood: false,
        new BridgeAppearanceDiagnostic(false, 0, 0),
        ProvidersConfigured: true);

    private static BridgeCatalog Catalog(
        string? isolationKey = null,
        string name = "Community widget",
        string? generation = null)
    {
        var settings = Candidate("settings", "Settings") with
        {
            PackageId = "org.gbar.firstparty.settings",
            PublisherId = "org.gbar.firstparty",
        };
        if (isolationKey is null) return new BridgeCatalog([settings]);
        return new BridgeCatalog(
        [
            settings,
            Candidate("widget.example", name) with
            {
                PackageId = "widget.example",
                PublisherId = "publisher.example",
                InstanceId = "widget.example@2.1.0",
                RequiresAppContainer = true,
                IsolationKey = isolationKey,
                AuthorityGeneration = generation,
                WorkerFingerprint = new string('C', 64),
                CatalogFingerprint = new string('D', 64),
            },
        ]);
    }

    private static ConfiguredWidget Candidate(string id, string name) => new()
    {
        Id = id,
        PackageId = id,
        PublisherId = "test.publisher",
        Name = name,
        InstanceId = id,
        WorkerExecutable = Environment.ProcessPath ??
            throw new InvalidOperationException("Test process path is unavailable."),
        WorkerFingerprint = new string('A', 64),
        CatalogFingerprint = new string('B', 64),
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static void Contains(string expected, string actual, string message)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message} Missing '{expected}' in '{actual}'.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class ControlledDiagnosticsSource(BridgeDiagnosticsReadModel model)
        : IBridgeDiagnosticsSource
    {
        internal BridgeDiagnosticsReadModel Model { get; set; } = model;
        internal Func<CancellationToken, ValueTask<BridgeConsentDiagnostic>> Consent { get; set; } =
            _ => ValueTask.FromResult(new BridgeConsentDiagnostic(true, 1, 1, 0));
        internal int Captures { get; private set; }
        internal int ConsentReads { get; private set; }

        public BridgeDiagnosticsReadModel Capture()
        {
            Captures++;
            return Model;
        }

        public ValueTask<BridgeConsentDiagnostic> ReadConsentAsync(
            CancellationToken cancellationToken)
        {
            ConsentReads++;
            return Consent(cancellationToken);
        }
    }

    private sealed class ControlledRecoveryService(
        IReadOnlyList<AppContainerAuthorityRecoveryCandidate> candidates)
        : IAppContainerAuthorityRecoveryService
    {
        internal Exception? ListFailure { get; set; }
        internal Exception? RetryFailure { get; set; }
        internal int ListCalls { get; private set; }
        internal int RetryCalls { get; private set; }
        internal string? LastRetriedToken { get; private set; }

        public IReadOnlyList<AppContainerAuthorityRecoveryCandidate> ListPending(
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (ListFailure is not null) throw ListFailure;
            return candidates;
        }

        public void Retry(
            string confirmationToken,
            CancellationToken cancellationToken = default,
            AppContainerAuthorityRecoveryCommitGate? commitGate = null)
        {
            RetryCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            LastRetriedToken = confirmationToken;
            if (RetryFailure is not null) throw RetryFailure;
            commitGate?.Commit(() => { });
        }
    }

    private sealed class BlockingRecoveryService : IAppContainerAuthorityRecoveryService
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Started => _started.Task;
        internal Task Completed => _completed.Task;
        internal bool Committed { get; private set; }

        public IReadOnlyList<AppContainerAuthorityRecoveryCandidate> ListPending(
            CancellationToken cancellationToken = default) => [];

        public void Retry(
            string confirmationToken,
            CancellationToken cancellationToken = default,
            AppContainerAuthorityRecoveryCommitGate? commitGate = null)
        {
            _started.TrySetResult();
            try
            {
                _release.Wait();
                (commitGate ?? new AppContainerAuthorityRecoveryCommitGate(cancellationToken))
                    .Commit(() => Committed = true);
            }
            finally { _completed.TrySetResult(); }
        }

        internal void Release() => _release.Set();
    }

    private sealed class CommitWinningRecoveryService(CancellationTokenSource cancellation)
        : IAppContainerAuthorityRecoveryService
    {
        internal bool Committed { get; private set; }

        public IReadOnlyList<AppContainerAuthorityRecoveryCandidate> ListPending(
            CancellationToken cancellationToken = default) => [];

        public void Retry(
            string confirmationToken,
            CancellationToken cancellationToken = default,
            AppContainerAuthorityRecoveryCommitGate? commitGate = null)
        {
            (commitGate ?? new AppContainerAuthorityRecoveryCommitGate(cancellationToken))
                .Commit(() =>
                {
                    Committed = true;
                    cancellation.Cancel();
                });
        }
    }
}
