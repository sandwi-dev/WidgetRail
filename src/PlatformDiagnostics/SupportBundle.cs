using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WidgetRail.PlatformDiagnostics;

/// <summary>
/// Deliberately reconstructs an allowlisted report. Never serialize the snapshot
/// wholesale: summaries, display names and recovery tokens are not export data.
/// No logs, settings files or widget content are read.
/// </summary>
public static class SupportBundle
{
    public static async ValueTask<SupportBundleExportResult> ExportAsync(
        PlatformDiagnosticsSnapshot snapshot, string directory, string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        PlatformDiagnosticsPipeServer.ValidateSnapshot(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var name = $"WidgetRail-support-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
        var target = Path.Combine(directory, name);
        var temporary = target + ".partial";
        try
        {
            Directory.CreateDirectory(directory);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var report = archive.CreateEntry("diagnostics.json", CompressionLevel.Fastest);
                    await using (var stream = report.Open())
                    {
                        using var json = new Utf8JsonWriter(stream, new() { Indented = true });
                        WriteReport(json, snapshot, applicationVersion, now);
                        await json.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    await using var readme = archive.CreateEntry("README.txt").Open();
                    await readme.WriteAsync(Encoding.UTF8.GetBytes(
                        "WidgetRail support report. Review before sharing.\n" +
                        "Includes app/OS versions, service states, installed widget identifiers, worker counts, " +
                        "failure categories/timestamps and controller readiness.\n" +
                        "Excludes raw logs, paths, account names, credentials, settings, URLs, chat, captures " +
                        "and recovery tokens. Nothing has been uploaded.\n" +
                        "A failure timestamp is when the running Bridge recorded it. Missing timestamps are unknown.\n" +
                        "Unreported host telemetry is not evidence of a failed service.\n"), cancellationToken).ConfigureAwait(false);
                }
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target);
            return new(SupportBundleExportStatus.Saved, name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(SupportBundleExportStatus.Failed);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void WriteReport(Utf8JsonWriter json, PlatformDiagnosticsSnapshot snapshot,
        string version, DateTimeOffset now)
    {
        json.WriteStartObject();
        json.WriteNumber("formatVersion", 1);
        json.WriteString("createdAtUtc", now);
        json.WriteString("applicationVersion", SafeVersion(version));
        json.WriteString("osVersion", Environment.OSVersion.Version.ToString());
        json.WriteNumber("snapshotRevision", snapshot.Revision);
        json.WriteStartObject("services");
        // Fixed keys: neither IDs nor summaries can inject arbitrary content.
        json.WriteString("bridge", snapshot.Bridge.State.ToString());
        json.WriteString("catalog", snapshot.Catalog.State.ToString());
        json.WriteString("appearance", snapshot.Appearance.State.ToString());
        json.WriteString("providers", snapshot.Providers.State.ToString());
        json.WriteString("permissions", snapshot.Consent.State.ToString());
        json.WriteString("overlay", snapshot.Overlay.State.ToString());
        json.WriteString("guide", snapshot.Guide.State.ToString());
        json.WriteEndObject();
        json.WriteStartObject("controllers");
        json.WriteString("state", snapshot.Controllers.State.ToString());
        json.WriteBoolean("hidHideReady", snapshot.Controllers.HidHideReady);
        json.WriteBoolean("viGEmBusReady", snapshot.Controllers.ViGEmBusReady);
        json.WriteBoolean("inputReady", snapshot.Controllers.InputReady);
        json.WriteEndObject();
        json.WriteNumber("pendingAuthorityRecoveries", snapshot.AuthorityRecoveries.Count);
        json.WriteStartArray("workers");
        foreach (var worker in snapshot.Workers)
        {
            json.WriteStartObject();
            json.WriteString("widgetId", worker.WidgetId);
            json.WriteBoolean("running", worker.IsRunning);
            json.WriteNumber("starts", worker.Starts);
            json.WriteBoolean("canRestart", worker.CanRestart);
            if (worker.LastFailureCode is { } code)
            {
                json.WriteString("failureCategory", FailureCategory(code));
                if (worker.LastFailureAtUtc is { } timestamp) json.WriteString("failureAtUtc", timestamp);
            }
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    // Failure codes can originate in workers. Export only recognized categories.
    public static string FailureCategory(string code) => code switch
    {
        "unexpected-exit" or "UnexpectedExit" or "ProcessExited" => "unexpected-exit",
        "timeout" or "Timeout" or "request-timeout" or "RequestTimedOut" => "timeout",
        "protocol-error" or "protocol-violation" or "ProtocolViolation" => "protocol-error",
        "transport-failure" or "TransportFailure" => "transport-failure",
        "ConnectionFailed" => "connection-failed",
        "request-cancelled" or "admission-failure" or "runtime-failure" or "lifecycle-failure" => code,
        "start-failed" or "StartFailed" => "start-failed",
        "restart-limit" or "RestartLimitExceeded" => "restart-limit",
        _ => "other",
    };

    public static string FailureDescription(string code) => FailureCategory(code) switch
    {
        "unexpected-exit" => "The widget closed unexpectedly",
        "timeout" => "The widget did not respond in time",
        "protocol-error" => "The widget returned an invalid response",
        "transport-failure" or "connection-failed" => "The connection to the widget was lost",
        "start-failed" or "admission-failure" => "The widget could not start",
        "restart-limit" => "The widget failed repeatedly",
        "request-cancelled" => "A widget operation was cancelled",
        _ => "A widget operation failed",
    };

    private static string SafeVersion(string value) => value.Length is > 0 and <= 80 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+') ? value : "unknown";
}
