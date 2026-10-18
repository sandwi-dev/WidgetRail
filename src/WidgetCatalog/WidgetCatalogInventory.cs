using System.Runtime.CompilerServices;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

/// <summary>Display-only inventory. Neither names nor directory presence grant execution authority.</summary>
public sealed record WidgetInventoryEntry(string Id, string Name, bool Enabled, int Order,
    Version SelectedVersion, IReadOnlyList<Version> Versions);
public sealed record WidgetInventorySnapshot(IReadOnlyList<WidgetInventoryEntry> Widgets);

public sealed partial class WidgetCatalog
{
    public async Task<WidgetInventorySnapshot> InspectInventoryAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var entries = ReadInventory(state, cancellationToken);
        return new(entries.Select(entry => { cancellationToken.ThrowIfCancellationRequested();
            return entry with { Name = ReadInventoryName(entry, cancellationToken) }; }).ToArray());
    }

    // This stream is the runtime admission path. Only enabled selections are
    // hashed; retained/disabled versions remain directory-only inventory.
    public async IAsyncEnumerable<CatalogWidget> DiscoverEnabledAsync(
        IReadOnlyList<string>? priorityWidgetIds = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var started = _timeProvider.GetTimestamp();
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(started) > _options.MaximumDiscoveryDuration)
                throw new WidgetPackageException("installed_discovery_time_limit", "Installed widget discovery exceeded its time limit.");
        }
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var entries = ReadInventory(state, cancellationToken);
        var priority = (priorityWidgetIds ?? []).Take(_options.MaximumInstalledWidgetIds)
            .Distinct(StringComparer.Ordinal).Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
        long bytes = 0;
        var files = 0;
        foreach (var entry in entries.Where(entry => entry.Enabled)
            .OrderBy(entry => priority.GetValueOrDefault(entry.Id, int.MaxValue)).ThenBy(entry => entry.Order))
        {
            CheckBudget();
            if (!entry.Versions.Contains(entry.SelectedVersion))
                throw new WidgetPackageException("active_version_missing", "An enabled widget's selected version is missing.");
            var verified = VerifyVersion(entry.Id, Path.Combine(_packagesRoot, entry.Id, entry.SelectedVersion.ToString()), CheckBudget);
            bytes = checked(bytes + verified.VerifiedTotalBytes); files = checked(files + verified.VerifiedEntryCount);
            if (bytes > _options.MaximumInstalledBytes || files > _options.MaximumInstalledEntries)
                throw new WidgetPackageException("installed_catalog_limit", "Enabled widget content exceeds the catalog limits.");
            await EnsureStateCurrentAsync(state, cancellationToken).ConfigureAwait(false);
            CheckBudget();
            yield return new(entry.Id, verified.Manifest.Name, true, entry.Order, verified, [verified]);
        }
        await EnsureStateCurrentAsync(state, cancellationToken).ConfigureAwait(false);
        CheckBudget();
    }

    private IReadOnlyList<WidgetInventoryEntry> ReadInventory(CatalogState state, CancellationToken token)
    {
        var directories = DiscoverRepairDirectories(token, repairBounds: false);
        var saved = state.Widgets.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var result = directories.GroupBy(entry => entry.Id, StringComparer.Ordinal).Select(group =>
        {
            var versions = group.Select(entry => entry.Version).OrderDescending().ToArray();
            saved.TryGetValue(group.Key, out var choice);
            return new WidgetInventoryEntry(group.Key, group.Key, choice?.Enabled == true, choice?.Order ?? int.MaxValue,
                choice?.ActiveVersion is { } selected ? Version.Parse(selected) : versions[0], versions);
        }).ToList();
        if (state.Widgets.Any(choice => choice.Enabled && result.All(entry => entry.Id != choice.Id)))
            throw new WidgetPackageException("active_version_missing", "An enabled widget's package directory is missing.");
        return result.OrderBy(entry => entry.Order).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
    }

    private async Task EnsureStateCurrentAsync(CatalogState expected, CancellationToken token)
    {
        var current = await _stateStore.LoadAsync(token).ConfigureAwait(false);
        if (expected.Version != current.Version || !expected.Widgets.SequenceEqual(current.Widgets))
            throw new WidgetPackageException("catalog_changed", "The installed selection changed during validation.");
    }

    private string ReadInventoryName(WidgetInventoryEntry entry, CancellationToken token)
    {
        var path = Path.Combine(_packagesRoot, entry.Id, entry.SelectedVersion.ToString(), "manifest.json");
        try
        {
            FileSystemSafety.EnsureNoReparsePoints(_root, path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var manifest = ManifestJson.Deserialize(BoundedFileReader.ReadAll(stream, (int)Math.Min(_options.MaximumEntryBytes, 1024L * 1024), token.ThrowIfCancellationRequested));
            return manifest.Id == entry.Id && manifest.Version == entry.SelectedVersion.ToString() &&
                !string.IsNullOrWhiteSpace(manifest.Name) && manifest.Name.Length <= 256 &&
                !manifest.Name.Any(char.IsControl) ? manifest.Name : entry.Id;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or WidgetPackageException or InvalidDataException)
        { return entry.Id; }
    }
}
