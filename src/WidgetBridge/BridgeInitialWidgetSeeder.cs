using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

namespace WidgetRail.WidgetBridge;

/// <summary>Turns a shipping bundle into ordinary, independently managed packages once.</summary>
internal static class BridgeInitialWidgetSeeder
{
    private sealed record Index(int SchemaVersion, IReadOnlyList<Entry> Widgets);
    private sealed record Entry(string Id, string Package, string Sha256);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static async Task SeedAsync(string installationRoot, string installedRoot,
        Action<string> warning, CancellationToken token)
    {
        var root = Path.GetFullPath(installationRoot);
        var indexPath = Path.Combine(root, "initial-widgets.json");
        if (!File.Exists(indexPath)) return;
        try
        {
            SafePath(root); SafePath(indexPath);
            await using var input = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is < 2 or > 65536)
                throw new InvalidDataException("Initial widget index exceeds its bound.");
            var index = await JsonSerializer.DeserializeAsync<Index>(input, Json, token).ConfigureAwait(false);
            if (index is not { SchemaVersion: 1, Widgets: not null } || index.Widgets.Count > 64 ||
                index.Widgets.Any(item => item is null || !WidgetManifestValidator.IsValidPackageIdentity(item.Id) ||
                    item.Id is "widgetrail.firstparty.settings" or "widgetrail.firstparty.marketplace" ||
                    item.Package != $"initial-widgets/{item.Id}.wrwidget" || item.Sha256 is not { Length: 64 } || !item.Sha256.All(Uri.IsHexDigit)) ||
                index.Widgets.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != index.Widgets.Count)
                throw new InvalidDataException("Initial widget index is invalid.");
            var catalog = new CatalogService(installedRoot);
            var receipts = (await catalog.InitialPackageReceiptsAsync(token).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in index.Widgets.Where(entry => !receipts.Contains(entry.Id)))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var directory = Path.Combine(root, "initial-widgets");
                    var path = Path.Combine(directory, entry.Id + ".wrwidget");
                    SafePath(directory); SafePath(path);
                    // Hash and install the same write-locked bytes; no check/use
                    // path reopen or publisher-label trust is involved.
                    await using var archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (archive.Length is < 1 or > 536870912)
                        throw new InvalidDataException("Initial widget archive exceeds its bound.");
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(archive, token).ConfigureAwait(false));
                    if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Initial widget archive does not match the bundle.");
                    archive.Position = 0;
                    await catalog.SeedInitialPackageAsync(archive, entry.Id, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or WidgetPackageException)
                {
                    warning($"Initial widget '{entry.Id}' could not be installed. Install it from Catalogue to retry.");
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or WidgetPackageException)
        {
            warning("Initial widget setup is unavailable. Settings and Catalogue remain available.");
        }
    }

    private static void SafePath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Initial widget paths must not use reparse points.");
    }
}
