using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetRuntime;

namespace WidgetRail.WidgetBridge;

internal readonly record struct BridgeAppearanceDiagnostic(
    bool IsConfigured,
    long Revision,
    int ErrorCount);

internal readonly record struct BridgeConsentDiagnostic(
    bool IsConfigured,
    long Revision,
    int DecisionCount,
    int DeniedCount);

internal sealed record BridgeDiagnosticsReadModel(
    BridgeClientRegistrySnapshot Registry,
    long CatalogDiagnosticRevision,
    int CatalogDiagnosticCount,
    bool CatalogRetainedLastGood,
    bool InstalledCatalogPending,
    BridgeAppearanceDiagnostic Appearance,
    bool ProvidersConfigured,
    BridgeArtworkMemorySnapshot Artwork);

internal interface IBridgeDiagnosticsSource
{
    BridgeDiagnosticsReadModel Capture();
    ValueTask<BridgeConsentDiagnostic> ReadConsentAsync(CancellationToken cancellationToken);
}

internal sealed class WidgetBridgeDiagnosticsSource(
    BridgeClientRegistry registry,
    BridgeCatalogMonitor? catalogMonitor,
    PlatformAppearanceService? appearance,
    ConsentStore? consentStore,
    bool providersConfigured,
    BridgeArtworkMemoryDiagnostics artwork) : IBridgeDiagnosticsSource
{
    public BridgeDiagnosticsReadModel Capture()
    {
        var registrySnapshot = registry.DiagnosticsSnapshot();
        var catalog = catalogMonitor?.DiagnosticsSnapshot() ?? new(
            registrySnapshot.CatalogRevision, 0, RetainedLastGood: false,
            InstalledCatalogPending: false);
        var appearanceErrors = appearance?.LastReloadDiagnostics.Count(item =>
            item.Severity == WidgetRail.WidgetStyling.WrssDiagnosticSeverity.Error) ?? 0;
        return new BridgeDiagnosticsReadModel(
            registrySnapshot,
            catalog.Revision,
            catalog.DiagnosticCount,
            catalog.RetainedLastGood,
            catalog.InstalledCatalogPending,
            new BridgeAppearanceDiagnostic(
                appearance is not null,
                appearance?.Current.Revision ?? 0,
                appearanceErrors),
            providersConfigured,
            artwork.Capture());
    }

    public async ValueTask<BridgeConsentDiagnostic> ReadConsentAsync(
        CancellationToken cancellationToken)
    {
        if (consentStore is null) return new(false, 0, 0, 0);
        var document = await consentStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new BridgeConsentDiagnostic(
            true,
            document.Revision,
            document.Entries.Count,
            document.Entries.Count(entry => entry.Decision == ConsentDecision.Deny));
    }
}

internal sealed class BridgeDiagnosticsProjection(
    IBridgeDiagnosticsSource source,
    Func<BridgeAuthorityRecoveryProjection> recovery)
{
    private long _revision;

    internal async ValueTask<PlatformDiagnosticsSnapshot> CreateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = source.Capture();
        var consentTask = ProjectConsentAsync(cancellationToken).AsTask();
        var recoveryTask = recovery().ListAsync(
            input.Registry.Catalog, cancellationToken).AsTask();
        var reads = Task.WhenAll(consentTask, recoveryTask);
        try
        {
            await reads.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = ObserveCompletionAsync(reads);
            throw;
        }

        var registry = input.Registry;
        var residency = registry.Residency;
        var workers = registry.Workers
            .Where(worker => IsToken(worker.Id, 128))
            .Take(PlatformDiagnosticsSnapshot.MaximumWorkers)
            .Select(worker => new PlatformWorkerDiagnostic(
                worker.Id,
                SafeLabel(worker.Name, $"Widget {worker.Id}"),
                worker.IsRunning,
                Math.Max(0, worker.Starts),
                worker.FailureCode is null ? null : SafeCode(worker.FailureCode),
                worker.CanRestart))
            .ToArray();

        var catalogWarnings = Math.Clamp(input.CatalogDiagnosticCount, 0, 64);
        var catalogRevision = Math.Max(0, registry.CatalogRevision);
        var diagnosticRevision = Math.Max(0, input.CatalogDiagnosticRevision);
        var catalogReconciliationPending = diagnosticRevision != catalogRevision;
        var catalogState = input.InstalledCatalogPending
            ? PlatformDiagnosticState.Unavailable
            : input.CatalogRetainedLastGood || catalogWarnings != 0 ||
            catalogReconciliationPending
            ? PlatformDiagnosticState.Degraded
            : PlatformDiagnosticState.Healthy;
        var catalogSummary = input.InstalledCatalogPending
            ? $"Revision {catalogRevision}; installed catalog validation is pending"
            : catalogReconciliationPending
            ? $"Revision {catalogRevision}; reload revision {diagnosticRevision} awaits reconciliation"
            : input.CatalogRetainedLastGood
            ? $"Revision {catalogRevision}; retained last good after a rejected reload"
            : catalogWarnings == 0
                ? $"Revision {catalogRevision}; {registry.Catalog.Widgets.Count} widgets validated"
                : $"Revision {catalogRevision}; {catalogWarnings} bounded warnings";
        var appearance = input.Appearance.IsConfigured
            ? input.Appearance.ErrorCount == 0
                ? Area("appearance", "Appearance", PlatformDiagnosticState.Healthy,
                    $"Revision {Math.Max(0, input.Appearance.Revision)}; active theme validated")
                : Area("appearance", "Appearance", PlatformDiagnosticState.Degraded,
                    $"Revision {Math.Max(0, input.Appearance.Revision)}; retained last good after " +
                    $"{Math.Clamp(input.Appearance.ErrorCount, 0, 64)} errors")
            : Area("appearance", "Appearance", PlatformDiagnosticState.Unavailable,
                "Appearance service is not configured");
        var applicationWorkers = residency.MaximumApplicationWorkers is { } configuredMaximum
            ? $"{Math.Max(0, residency.ApplicationWorkers)}/{Math.Max(0, configuredMaximum)} " +
              "(user-configured count limit)"
            : $"{Math.Max(0, residency.ApplicationWorkers)} " +
              "(no application-worker count limit)";
        var artwork = input.Artwork;

        return new PlatformDiagnosticsSnapshot(
            PlatformDiagnosticsSnapshot.CurrentSchemaVersion,
            Interlocked.Increment(ref _revision),
            Area("bridge", "Bridge", PlatformDiagnosticState.Healthy,
                $"Native host connected; application workers {applicationWorkers}; " +
                $"reported memory guidance " +
                $"{Math.Max(0, residency.ApplicationAdvisoryMemoryMb)} MiB; control plane " +
                $"{Math.Max(0, residency.ControlPlaneWorkers)} " +
                $"({Math.Max(0, residency.ControlPlaneAdvisoryMemoryMb)} MiB reported); " +
                $"artwork requests {artwork.Requests}, in-flight {artwork.InFlight}/" +
                $"{artwork.MaximumInFlight}, raw {artwork.RawBytes} bytes, Base64 " +
                $"{artwork.Base64Characters} chars, managed heap {artwork.ManagedHeapBytes} " +
                $"bytes, LOH {artwork.LargeObjectHeapBytes} bytes, allocation rate " +
                $"{artwork.AllocatedBytesPerSecond} B/s, Gen2 {artwork.Gen2Collections}, " +
                $"private {artwork.PrivateBytes} bytes, working set {artwork.WorkingSetBytes} bytes"),
            Area("catalog", "Widget catalog", catalogState, catalogSummary),
            appearance,
            input.ProvidersConfigured
                ? Area("providers", "Platform providers", PlatformDiagnosticState.Healthy,
                    "Audio and network providers are available on demand")
                : Area("providers", "Platform providers", PlatformDiagnosticState.Unavailable,
                    "Audio and network providers are not configured"),
            await consentTask.ConfigureAwait(false),
            Area("overlay", "Overlay host", PlatformDiagnosticState.Unavailable,
                "Host telemetry is not reported by this build"),
            Area("guide", "Guide input", PlatformDiagnosticState.Unavailable,
                "Host telemetry is not reported by this build"),
            workers)
        {
            AuthorityRecoveries = await recoveryTask.ConfigureAwait(false),
        };
    }

    private async ValueTask<PlatformDiagnosticArea> ProjectConsentAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var consent = await source.ReadConsentAsync(cancellationToken).ConfigureAwait(false);
            if (!consent.IsConfigured)
                return Area("consent", "Permissions", PlatformDiagnosticState.Unavailable,
                    "Permission service is not configured");
            if (consent.Revision < 0 || consent.DecisionCount < 0 || consent.DeniedCount < 0 ||
                consent.DeniedCount > consent.DecisionCount)
                return Area("consent", "Permissions", PlatformDiagnosticState.Degraded,
                    "Permission state is unavailable (invalid_state)");
            return Area("consent", "Permissions", PlatformDiagnosticState.Healthy,
                $"Revision {consent.Revision}; {consent.DecisionCount} decisions; " +
                $"{consent.DeniedCount} denied");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is BrokerException or
            IOException or UnauthorizedAccessException)
        {
            var code = exception is BrokerException broker ? broker.Code :
                exception is UnauthorizedAccessException ? "access_denied" :
                exception is IOException ? "io_error" : "unavailable";
            return Area("consent", "Permissions", PlatformDiagnosticState.Degraded,
                $"Permission state is unavailable ({SafeCode(code)})");
        }
    }

    private static PlatformDiagnosticArea Area(
        string id,
        string label,
        PlatformDiagnosticState state,
        string summary) => PlatformDiagnosticsSnapshot.Area(id, label, state, summary);

    internal static string SafeCode(string value)
    {
        var safe = new string(value.Take(64).Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-').ToArray());
        return safe.Length == 0 ? "unavailable" : safe;
    }

    internal static string SafeLabel(string? value, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Length <= 160 &&
            value.IndexOfAny(['\r', '\n']) < 0) return value;
        return fallback;
    }

    private static bool IsToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static async Task ObserveCompletionAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
}

internal sealed class BridgeAuthorityRecoveryProjection(
    IAppContainerAuthorityRecoveryService service)
{
    internal async ValueTask<IReadOnlyList<PlatformAuthorityRecoveryDiagnostic>> ListAsync(
        BridgeCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspection = Task.Run(() => ProjectPending(catalog, cancellationToken),
            CancellationToken.None);
        try
        {
            return await inspection.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = ObserveCompletionAsync(inspection);
            throw;
        }
    }

    internal IReadOnlyList<PlatformAuthorityRecoveryDiagnostic> ProjectPending(
        BridgeCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        IReadOnlyList<AppContainerAuthorityRecoveryCandidate> candidates;
        try
        {
            candidates = service.ListPending(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            AppContainerAuthorityJournalException or
            PlatformNotSupportedException or
            IOException or
            UnauthorizedAccessException)
        {
            return [Unavailable("recovery_state_unavailable")];
        }

        if (candidates is null || candidates.Any(candidate => !IsValid(candidate)) ||
            candidates.Select(candidate => candidate.ConfirmationToken)
                .Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            return [Unavailable("recovery_state_invalid")];

        var displayNamesByProfile = catalog.Widgets
            .Select(descriptor => catalog.GetConfigured(descriptor.Id))
            .Where(configured => configured.RequiresAppContainer && configured.IsolationKey is not null)
            .GroupBy(
                configured => WindowsAppContainer.ProfileNameFor(configured.IsolationKey!),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => AuthorityRecoveryDisplayName(group.First()),
                StringComparer.Ordinal);

        return candidates
            .OrderBy(candidate => candidate.ConfirmationToken, StringComparer.Ordinal)
            .Take(PlatformDiagnosticsSnapshot.MaximumAuthorityRecoveries)
            .Select(candidate => new PlatformAuthorityRecoveryDiagnostic(
                RecoveryId(candidate.ConfirmationToken),
                displayNamesByProfile.GetValueOrDefault(
                    candidate.ProfileName,
                    candidate.IsLegacy
                        ? "Legacy community widget recovery"
                        : "Community widget recovery"),
                PlatformAuthorityRecoveryState.Pending,
                candidate.IsLegacy ? "legacy_pending_recovery" : "pending_recovery",
                CanRetry: true,
                candidate.ConfirmationToken))
            .ToArray();
    }

    internal async ValueTask<PlatformAuthorityRecoveryRetryResult> RetryAsync(
        string confirmationToken,
        CancellationToken cancellationToken)
    {
        if (!IsConfirmationToken(confirmationToken))
            return PlatformAuthorityRecoveryRetryResult.Refused("retry_refused");
        cancellationToken.ThrowIfCancellationRequested();
        var commitGate = new AppContainerAuthorityRecoveryCommitGate(cancellationToken);
        try
        {
            var recoveryTask = Task.Run(
                () => service.Retry(confirmationToken, cancellationToken, commitGate),
                CancellationToken.None);
            try
            {
                await recoveryTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await Task.Yield();
                if (commitGate.CommitWon)
                {
                    _ = ObserveCompletionAsync(recoveryTask);
                    return new(PlatformAuthorityRecoveryRetryStatus.Recovered, "recovered");
                }
                _ = ObserveCompletionAsync(recoveryTask);
                throw;
            }
            return new(PlatformAuthorityRecoveryRetryStatus.Recovered, "recovered");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppContainerAuthorityRecoveryException exception)
        {
            return exception.Code switch
            {
                "stale_confirmation" => new(
                    PlatformAuthorityRecoveryRetryStatus.Stale, "stale_confirmation"),
                "recovery_not_verified" => new(
                    PlatformAuthorityRecoveryRetryStatus.StillPending, "recovery_not_verified"),
                _ => PlatformAuthorityRecoveryRetryResult.Refused("retry_refused"),
            };
        }
        catch (Exception exception) when (exception is
            AppContainerAuthorityJournalException or
            PlatformNotSupportedException or
            IOException or
            UnauthorizedAccessException)
        {
            return new(PlatformAuthorityRecoveryRetryStatus.Unavailable,
                "recovery_state_unavailable");
        }
    }

    private static PlatformAuthorityRecoveryDiagnostic Unavailable(string code) => new(
        RecoveryId(code),
        "Authority recovery unavailable",
        PlatformAuthorityRecoveryState.Unavailable,
        code,
        CanRetry: false,
        ConfirmationToken: null);

    private static bool IsValid(AppContainerAuthorityRecoveryCandidate candidate) =>
        candidate is not null && IsConfirmationToken(candidate.ConfirmationToken) &&
        !string.IsNullOrWhiteSpace(candidate.ProfileName) && candidate.ProfileName.Length <= 256 &&
        candidate.TargetCount is >= 1 and <= 2_049;

    private static bool IsConfirmationToken(string? value) =>
        value is { Length: PlatformDiagnosticsSnapshot.ConfirmationTokenLength or
            PlatformDiagnosticsSnapshot.LegacyConfirmationTokenLength } &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string RecoveryId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private static string AuthorityRecoveryDisplayName(ConfiguredWidget configured)
    {
        var generation = configured.AuthorityGeneration is { Length: > 0 }
            ? configured.AuthorityGeneration
            : null;
        var candidate = generation is null ? configured.Name : $"{configured.Name} {generation}";
        if (IsSafeAuthorityRecoveryLabel(candidate)) return candidate;
        return generation is not null && IsSafeAuthorityRecoveryLabel(generation)
            ? $"Community widget {generation}"
            : "Community widget recovery";
    }

    private static bool IsSafeAuthorityRecoveryLabel(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 160 &&
        !value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) &&
        value.All(character =>
            !char.IsControl(character) && character is not '\\' and not '/' and not ':');

    private static async Task ObserveCompletionAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
}
