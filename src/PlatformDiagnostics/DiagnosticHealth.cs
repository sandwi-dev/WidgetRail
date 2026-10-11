namespace WidgetRail.PlatformDiagnostics;

/// <summary>Current problems are distinct from recovered failures and absent host telemetry.</summary>
public sealed record DiagnosticHealth(int ServiceProblems, int StoppedFailures, int RecoveryRecords, bool ControllerProblem)
{
    public bool NeedsAttention => ServiceProblems != 0 || StoppedFailures != 0 || RecoveryRecords != 0 || ControllerProblem;

    public static DiagnosticHealth From(PlatformDiagnosticsSnapshot snapshot) => new(
        new[] { snapshot.Bridge, snapshot.Catalog, snapshot.Appearance, snapshot.Providers, snapshot.Consent }
            .Count(area => area.State != PlatformDiagnosticState.Healthy) +
        new[] { snapshot.Overlay, snapshot.Guide }.Count(area => area.State == PlatformDiagnosticState.Degraded),
        snapshot.Workers.Count(worker => worker.LastFailureCode is not null && !worker.IsRunning),
        snapshot.AuthorityRecoveries.Count,
        snapshot.Controllers.State is ControllerControlState.Failed or ControllerControlState.RecoveryRequired);
}
