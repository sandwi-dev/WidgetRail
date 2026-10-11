using System.IO.Compression;
using System.Text.Json;
using WidgetRail.PlatformDiagnostics;

internal static class SupportBundleScenarios
{
    internal static async Task ExportPrivacyAndFailureAsync(PlatformDiagnosticsSnapshot snapshot)
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-support-test-" + Guid.NewGuid().ToString("N"));
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        snapshot = snapshot with
        {
            Bridge = snapshot.Bridge with { Summary = "private-chat-url-key" },
            Workers = [new("test.widget", "private-account-name", false, 2, "private-token", true) { LastFailureAtUtc = at }],
            AuthorityRecoveries = [new(new string('A', 32), "Private recovery", PlatformAuthorityRecoveryState.Pending,
                "private_status", true, new string('B', 32))],
        };
        try
        {
            var result = await SupportBundle.ExportAsync(snapshot, root, "0.1.0-preview.29");
            Check(result.Status == SupportBundleExportStatus.Saved, "Export failed");
            using (var zip = ZipFile.OpenRead(Path.Combine(root, result.FileName!)))
            {
                Check(zip.Entries.Count == 2, "Unexpected attachment exported");
                using var reader = new StreamReader(zip.GetEntry("diagnostics.json")!.Open());
                var text = await reader.ReadToEndAsync();
                Check(!text.Contains("private", StringComparison.OrdinalIgnoreCase) && !text.Contains(new string('B', 32)),
                    "Private fields escaped the export allowlist");
                using var document = JsonDocument.Parse(text);
                var worker = document.RootElement.GetProperty("workers")[0];
                Check(worker.GetProperty("failureCategory").GetString() == "other", "Unknown failure code was copied");
                Check(worker.GetProperty("failureAtUtc").GetDateTimeOffset() == at, "Failure time changed during export");
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await SupportBundle.ExportAsync(snapshot, root, "1.0", cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Check(Directory.GetFiles(root).Length == 1, "Cancelled export left files");
            var blocked = Path.Combine(root, "not-a-directory");
            await File.WriteAllTextAsync(blocked, "unchanged");
            Check((await SupportBundle.ExportAsync(snapshot, blocked, "1.0")).Status == SupportBundleExportStatus.Failed,
                "I/O failure reported success");
            Check(await File.ReadAllTextAsync(blocked) == "unchanged", "Export overwrote an existing file");
            Check(Directory.GetFiles(root, "*.partial").Length == 0, "Partial export leaked");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    internal static Task Health(PlatformDiagnosticsSnapshot snapshot)
    {
        var healthy = PlatformDiagnosticsSnapshot.Area("service", "Service", PlatformDiagnosticState.Healthy, "Healthy");
        snapshot = snapshot with { Bridge = healthy, Catalog = healthy, Appearance = healthy, Providers = healthy, Consent = healthy,
            Overlay = healthy with { State = PlatformDiagnosticState.Unavailable }, Guide = healthy with { State = PlatformDiagnosticState.Unavailable },
            Workers = [new("test.widget", "Widget", true, 2, "unexpected-exit", true)], AuthorityRecoveries = [] };
        Check(!DiagnosticHealth.From(snapshot).NeedsAttention, "Recovered failure or unreported telemetry marked unhealthy");
        Check(DiagnosticHealth.From(snapshot with { Workers = [snapshot.Workers[0] with { IsRunning = false }] }).NeedsAttention,
            "Stopped failure hidden");
        Check(DiagnosticHealth.From(snapshot with { Bridge = healthy with { State = PlatformDiagnosticState.Unavailable } }).NeedsAttention,
            "Unavailable Bridge hidden");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
