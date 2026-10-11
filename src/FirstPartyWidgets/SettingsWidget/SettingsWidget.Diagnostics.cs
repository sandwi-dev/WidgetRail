using WidgetRail.PlatformDiagnostics;

namespace WidgetRail.FirstPartyWidgets.Settings;

public sealed partial class SettingsWidget
{
    private bool _diagnosticDetails;
    private string? _supportExportFile;

    // The ordinary operation gate serializes export with Settings mutations.
    // Busy remains focusable, matching all other long-running Settings actions.
    private async Task ExportSupportBundleAsync(CancellationToken cancellationToken)
    {
        SetOperation("Saving support report…", busy: true, error: false);
        try
        {
            var result = await _diagnosticsService.ExportSupportBundleAsync(cancellationToken).ConfigureAwait(false);
            if (result.Status == SupportBundleExportStatus.Saved)
            {
                lock (_stateLock) _supportExportFile = result.FileName;
                SetOperation("Support report saved in Documents → WidgetRail → Support. Nothing was uploaded.", false, false);
            }
            else SetOperation(result.Status == SupportBundleExportStatus.Unavailable
                ? "Support export is unavailable. Refresh diagnostics and try again."
                : "Could not save the report. Check Documents access and available disk space, then try again.", false, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOperation("Support export cancelled.", false, false);
            throw;
        }
        catch (PlatformDiagnosticsException)
        {
            SetOperation("Could not confirm the export. Check Documents → WidgetRail → Support before trying again.", false, true);
        }
    }
}
