using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

namespace WidgetRail.WrailCli;

internal static class InspectCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(args, [], ["--json"]);
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException("Usage: wrail inspect <file.wrwidget> [--json]");
        var path = Path.GetFullPath(parsed.Positionals[0]);
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".wrwidget", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Supply an existing local .wrwidget package.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CliUsageException("Widget package cannot be a reparse point.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Validation does not create the catalog or extract/execute payloads.
        // Hash and inspect the same locked file, including full-trust packages.
        var inspection = await new CatalogService(Path.Combine(Path.GetTempPath(), "wrail-inspection-unused"))
            .CreateInstaller().ValidateAsync(stream, cancellationToken);
        stream.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        var manifest = inspection.Manifest;
        var fullTrust = WidgetManifestTrust.Resolve(manifest) == WidgetExecutionTrust.FullTrustCurrentUser;
        var report = new
        {
            schemaVersion = 1, manifest.Id, manifest.Name, manifest.Publisher, manifest.Version,
            sha256 = digest,
            executionModel = fullTrust ? "full-trust-current-user" : "appcontainer",
            manifest.HostApi, manifest.Architectures, manifest.Entrypoint,
            manifest.Permissions, manifest.OptionalPermissions,
            manifest.PinningSupported, manifest.FullWidgetPinningSupported,
            files = inspection.EntryCount, expandedBytes = inspection.TotalUncompressedBytes,
        };
        if (parsed.HasFlag("--json"))
            await output.WriteLineAsync(JsonSerializer.Serialize(report, DiagnosticJson.Options));
        else
        {
            await output.WriteLineAsync($"Widget: {manifest.Name} ({manifest.Id})");
            await output.WriteLineAsync($"Publisher: {manifest.Publisher}\nVersion: {manifest.Version}\nSHA-256: {digest}");
            await output.WriteLineAsync("Execution: " + (fullTrust
                ? "Full trust — runs as your Windows user, outside AppContainer."
                : "AppContainer — host-brokered widget permissions."));
            await output.WriteLineAsync($"Host API: minimum {manifest.HostApi.Minimum}, maximum major {manifest.HostApi.MaximumMajor}");
            await output.WriteLineAsync($"Architectures: {string.Join(", ", manifest.Architectures)}");
            await output.WriteLineAsync($"Required permissions: {Join(manifest.Permissions)}");
            await output.WriteLineAsync($"Optional permissions: {Join(manifest.OptionalPermissions)}");
            await output.WriteLineAsync($"Pinned layouts: {manifest.PinningSupported}; full widget pinning: {manifest.FullWidgetPinningSupported}");
            await output.WriteLineAsync($"Files: {inspection.EntryCount}; expanded bytes: {inspection.TotalUncompressedBytes}");
            await output.WriteLineAsync("Package structure validated. No widget code was run; runtime behavior and publisher identity are not verified.");
        }
        return 0;
    }

    private static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "none" : string.Join(", ", values);
}

internal static class DiagnosticJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
