namespace GameBarAlternative.PlatformDiagnostics;

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
    public const int CurrentSchemaVersion = 1;
    public const int MaximumWorkers = 256;

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

public interface IPlatformDiagnosticsService
{
    ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
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
