namespace WidgetRail.PlatformDiagnostics;

public enum PlatformDiagnosticState
{
    Healthy,
    Degraded,
    Unavailable,
}

public sealed record PlatformDiagnosticArea(
    string Id,
    string Label,
    PlatformDiagnosticState State,
    string Summary);

public sealed record PlatformWorkerDiagnostic(
    string WidgetId,
    string WidgetName,
    bool IsRunning,
    int Starts,
    string? LastFailureCode,
    bool CanRestart);

/// <summary>
/// Sanitized host-owned recovery state. Raw profile names, SIDs, filesystem
/// paths, security descriptors, and object identities must never be projected
/// into this contract.
/// </summary>
public enum PlatformAuthorityRecoveryState
{
    Pending,
    Blocked,
    Unavailable,
}

public sealed record PlatformAuthorityRecoveryDiagnostic(
    string RecoveryId,
    string DisplayName,
    PlatformAuthorityRecoveryState State,
    string StatusCode,
    bool CanRetry,
    string? ConfirmationToken);

public enum PlatformAuthorityRecoveryRetryStatus
{
    Recovered,
    StillPending,
    Stale,
    Unavailable,
    Refused,
}

/// <summary>
/// Closed retry result. The bounded code is intentionally the only diagnostic
/// text so an authority implementation cannot return raw recovery evidence.
/// </summary>
public sealed record PlatformAuthorityRecoveryRetryResult(
    PlatformAuthorityRecoveryRetryStatus Status,
    string Code)
{
    public static PlatformAuthorityRecoveryRetryResult Refused(string code) =>
        new(PlatformAuthorityRecoveryRetryStatus.Refused, code);
}

public sealed record PlatformWidgetLocalDataInspection(
    string WidgetId,
    string DisplayName,
    bool Exists,
    string StatusCode,
    string? ConfirmationToken);

public enum PlatformWidgetLocalDataClearStatus
{
    Cleared,
    NoState,
    Stale,
    ClearFailed,
    RestartFailed,
    Unavailable,
    Refused,
}

public sealed record PlatformWidgetLocalDataClearResult(
    PlatformWidgetLocalDataClearStatus Status,
    string Code);

public sealed record PlatformWidgetPackageUninstallInspection(
    string WidgetId,
    string DisplayName,
    string PublisherId,
    string ActiveVersion,
    int VersionCount,
    bool CanUninstall,
    string StatusCode,
    string? ConfirmationToken);

public enum PlatformWidgetPackageUninstallStatus
{
    Uninstalled,
    CleanupPending,
    Stale,
    Resident,
    RecoveryPending,
    Unavailable,
    Refused,
}

public sealed record PlatformWidgetPackageUninstallResult(
    PlatformWidgetPackageUninstallStatus Status,
    string Code);

public enum ControllerControlState
{
    Unavailable, Off, Starting, Active, WaitingForController, RecoveryRequired, Stopping,
}

public sealed record ControllerControlStatus(
    ControllerControlState State,
    bool HidHideReady,
    bool ViGEmBusReady,
    bool InputReady)
{
    public bool CanEnable => HidHideReady && ViGEmBusReady && InputReady &&
        State != ControllerControlState.RecoveryRequired;
    public static ControllerControlStatus Unavailable { get; } =
        new(ControllerControlState.Unavailable, false, false, false);
}

public sealed record ControllerControlResult(bool Accepted, ControllerControlStatus Status);

public sealed record PlatformDiagnosticsSnapshot(
    int SchemaVersion,
    long Revision,
    PlatformDiagnosticArea Bridge,
    PlatformDiagnosticArea Catalog,
    PlatformDiagnosticArea Appearance,
    PlatformDiagnosticArea Providers,
    PlatformDiagnosticArea Consent,
    PlatformDiagnosticArea Overlay,
    PlatformDiagnosticArea Guide,
    IReadOnlyList<PlatformWorkerDiagnostic> Workers)
{
    public const int CurrentSchemaVersion = 3;
    public const int MaximumWorkers = 256;
    public const int MaximumAuthorityRecoveries = 64;
    public const int RecoveryIdLength = 32;
    public const int ConfirmationTokenLength = 32;
    public const int LegacyConfirmationTokenLength = 64;

    public ControllerControlStatus Controllers { get; init; } = ControllerControlStatus.Unavailable;

    /// <summary>
    /// Bounded sanitized authority-recovery projection. This additive property
    /// preserves source compatibility for schema-aware snapshot producers.
    /// </summary>
    public IReadOnlyList<PlatformAuthorityRecoveryDiagnostic> AuthorityRecoveries
        { get; init; } = [];

    /// <summary>
    /// Optional aggregate artwork/process diagnostic payload. Absence means the
    /// producing Bridge does not report these metrics; zero is a measured value.
    /// </summary>
    public static PlatformDiagnosticsSnapshot Unavailable(string summary = "Runtime diagnostics are unavailable") =>
        new(
            CurrentSchemaVersion,
            0,
            Area("bridge", "Bridge", PlatformDiagnosticState.Unavailable, summary),
            Area("catalog", "Widget catalog", PlatformDiagnosticState.Unavailable, summary),
            Area("appearance", "Appearance", PlatformDiagnosticState.Unavailable, summary),
            Area("providers", "Platform providers", PlatformDiagnosticState.Unavailable, summary),
            Area("consent", "Permissions", PlatformDiagnosticState.Unavailable, summary),
            Area("overlay", "Overlay host", PlatformDiagnosticState.Unavailable,
                "Host telemetry is not reported by this build"),
            Area("guide", "Guide input", PlatformDiagnosticState.Unavailable,
                "Host telemetry is not reported by this build"),
            []);

    public static PlatformDiagnosticArea Area(
        string id,
        string label,
        PlatformDiagnosticState state,
        string summary) => new(id, label, state, summary);
}

public sealed record ApplicationControlResult(bool Accepted);

public interface IPlatformDiagnosticsService
{
    ValueTask<ApplicationControlResult> RequestApplicationControlAsync(
        bool restart, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ApplicationControlResult(false));
    ValueTask<ControllerControlResult> SetExclusiveControlAsync(
        bool enabled, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ControllerControlResult(false, ControllerControlStatus.Unavailable));

    ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    ValueTask<PlatformAuthorityRecoveryRetryResult> RetryAuthorityRecoveryAsync(
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (confirmationToken is null ||
            confirmationToken.Length is not (
                PlatformDiagnosticsSnapshot.ConfirmationTokenLength or
                PlatformDiagnosticsSnapshot.LegacyConfirmationTokenLength) ||
            !confirmationToken.All(character =>
                character is >= '0' and <= '9' or >= 'A' and <= 'F'))
            throw new ArgumentException(
                "Authority recovery confirmation token is invalid.",
                nameof(confirmationToken));
        return ValueTask.FromResult(
            PlatformAuthorityRecoveryRetryResult.Refused("retry_unsupported"));
    }

    ValueTask<PlatformWidgetLocalDataInspection> InspectWidgetLocalDataAsync(
        string widgetId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PlatformWidgetLocalDataInspection(
            widgetId, widgetId, false, "inspection_unsupported", null));

    ValueTask<PlatformWidgetLocalDataClearResult> ClearWidgetLocalDataAsync(
        string widgetId,
        string confirmationToken,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PlatformWidgetLocalDataClearResult(
            PlatformWidgetLocalDataClearStatus.Refused, "clear_unsupported"));

    ValueTask<PlatformWidgetPackageUninstallInspection> InspectWidgetPackageUninstallAsync(
        string widgetId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PlatformWidgetPackageUninstallInspection(
            widgetId, widgetId, string.Empty, string.Empty, 0, false,
            "inspection_unsupported", null));

    ValueTask<PlatformWidgetPackageUninstallResult> UninstallWidgetPackageAsync(
        string widgetId,
        string publisherId,
        string activeVersion,
        string confirmationToken,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PlatformWidgetPackageUninstallResult(
            PlatformWidgetPackageUninstallStatus.Refused, "uninstall_unsupported"));
}

public sealed class UnavailablePlatformDiagnosticsService : IPlatformDiagnosticsService
{
    public static UnavailablePlatformDiagnosticsService Instance { get; } = new();

    private UnavailablePlatformDiagnosticsService() { }

    public ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(PlatformDiagnosticsSnapshot.Unavailable());
    }
}
