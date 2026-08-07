using System.Text.Json;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetCatalog;

public sealed class WidgetCatalog
{
    private readonly string _root;
    private readonly string _packagesRoot;
    private readonly CatalogStateStore _stateStore;
    private readonly WidgetCatalogOptions _options;

    public WidgetCatalog(string currentUserRoot, WidgetCatalogOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserRoot);
        _root = Path.GetFullPath(currentUserRoot);
        _packagesRoot = Path.Combine(_root, "packages");
        _options = options ?? new WidgetCatalogOptions();
        _options.Validate();
        _stateStore = new CatalogStateStore(_root);
    }

    public string Root => _root;

    public WidgetPackageInstaller CreateInstaller() => new(_root, _options);

    public async Task<WidgetCatalogSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var versions = DiscoverInstalledVersions();
        var state = await _stateStore.LoadAsync(cancellationToken);
        var stateById = state.Widgets.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var nextOrder = state.Widgets.Count == 0 ? 0 : state.Widgets.Max(entry => entry.Order) + 1;
        var newIdOrder = versions.Select(item => item.Id).Distinct(StringComparer.Ordinal)
            .Where(id => !stateById.ContainsKey(id))
            .Order(StringComparer.Ordinal)
            .Select((id, index) => (id, order: nextOrder + index))
            .ToDictionary(item => item.id, item => item.order, StringComparer.Ordinal);

        var widgets = versions
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedVersions = group.OrderByDescending(item => item.Version).ToArray();
                var active = orderedVersions[0];
                var hasState = stateById.TryGetValue(group.Key, out var saved);
                return new CatalogWidget(
                    group.Key,
                    active.Manifest.Name,
                    hasState ? saved!.Enabled : true,
                    hasState ? saved!.Order : newIdOrder[group.Key],
                    active,
                    orderedVersions);
            })
            .OrderBy(widget => widget.Order)
            .ThenBy(widget => widget.Id, StringComparer.Ordinal)
            .ToArray();

        return new WidgetCatalogSnapshot(widgets);
    }

    public async Task SetEnabledAsync(string widgetId, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        var snapshot = await DiscoverAsync(cancellationToken);
        if (!snapshot.Widgets.Any(widget => widget.Id == widgetId))
            throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");

        await _stateStore.MutateAsync(state =>
        {
            var entries = state.Widgets.ToList();
            var index = entries.FindIndex(entry => entry.Id == widgetId);
            if (index >= 0)
                entries[index] = entries[index] with { Enabled = enabled };
            else
                entries.Add(new CatalogStateEntry
                {
                    Id = widgetId,
                    Enabled = enabled,
                    Order = entries.Count == 0 ? 0 : entries.Max(entry => entry.Order) + 1,
                });
            return state with { Widgets = entries.OrderBy(entry => entry.Order).ToArray() };
        }, cancellationToken);
    }

    /// <summary>Moves the listed installed IDs to the front in the supplied order and preserves the remaining relative order.</summary>
    public async Task SetOrderAsync(IReadOnlyList<string> widgetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widgetIds);
        if (widgetIds.Count != widgetIds.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Widget order cannot contain duplicate IDs.", nameof(widgetIds));

        var snapshot = await DiscoverAsync(cancellationToken);
        var installed = snapshot.Widgets.ToDictionary(widget => widget.Id, StringComparer.Ordinal);
        var missing = widgetIds.FirstOrDefault(id => !installed.ContainsKey(id));
        if (missing is not null) throw new KeyNotFoundException($"Widget '{missing}' is not installed.");

        var finalIds = widgetIds.Concat(snapshot.Widgets.Select(widget => widget.Id)
            .Where(id => !widgetIds.Contains(id, StringComparer.Ordinal))).ToArray();
        await _stateStore.MutateAsync(state =>
        {
            var saved = state.Widgets.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            var entries = finalIds.Select((id, order) => new CatalogStateEntry
            {
                Id = id,
                Order = order,
                Enabled = saved.TryGetValue(id, out var entry) ? entry.Enabled : installed[id].Enabled,
            }).ToArray();
            return state with { Widgets = entries };
        }, cancellationToken);
    }

    private IReadOnlyList<InstalledWidgetVersion> DiscoverInstalledVersions()
    {
        if (!Directory.Exists(_packagesRoot)) return [];
        FileSystemSafety.EnsureNoReparsePoints(_root, _packagesRoot);
        var result = new List<InstalledWidgetVersion>();
        foreach (var idDirectory in Directory.EnumerateDirectories(_packagesRoot).Order(StringComparer.Ordinal))
        {
            FileSystemSafety.EnsureNoReparsePoints(_root, idDirectory);
            var directoryId = Path.GetFileName(idDirectory);
            foreach (var versionDirectory in Directory.EnumerateDirectories(idDirectory).Order(StringComparer.Ordinal))
            {
                FileSystemSafety.EnsureNoReparsePoints(_root, versionDirectory);
                var manifestPath = Path.Combine(versionDirectory, "manifest.json");
                if (!File.Exists(manifestPath))
                    throw new WidgetPackageException("missing_manifest", $"Installed widget is missing manifest.json: {versionDirectory}");
                FileSystemSafety.EnsureNoReparsePoints(_root, manifestPath);
                WidgetManifest manifest;
                try
                {
                    if (new FileInfo(manifestPath).Length > Math.Min(_options.MaximumEntryBytes, 1024 * 1024))
                        throw new WidgetPackageException("invalid_manifest", $"Installed manifest is too large: {manifestPath}");
                    manifest = ManifestJson.Deserialize(File.ReadAllBytes(manifestPath));
                }
                catch (JsonException exception)
                {
                    throw new WidgetPackageException("invalid_manifest", $"Installed manifest is invalid: {manifestPath}", exception);
                }
                var errors = WidgetManifestValidator.Validate(manifest);
                if (errors.Count != 0)
                    throw new WidgetPackageException("invalid_manifest", $"Installed manifest failed validation: {errors[0].Path}: {errors[0].Message}");
                if (!string.Equals(manifest.Id, directoryId, StringComparison.Ordinal))
                    throw new WidgetPackageException("identity_mismatch", $"Manifest ID '{manifest.Id}' does not match directory '{directoryId}'.");
                if (!Version.TryParse(manifest.Version, out var version) ||
                    !string.Equals(version.ToString(), Path.GetFileName(versionDirectory), StringComparison.Ordinal) ||
                    !string.Equals(manifest.Version, Path.GetFileName(versionDirectory), StringComparison.Ordinal))
                    throw new WidgetPackageException("identity_mismatch", $"Manifest version '{manifest.Version}' does not match its install directory.");
                var entrypointPath = Path.GetFullPath(Path.Combine(
                    versionDirectory,
                    manifest.Entrypoint.Assembly.Replace('/', Path.DirectorySeparatorChar)));
                if (!FileSystemSafety.IsWithin(versionDirectory, entrypointPath) || !File.Exists(entrypointPath))
                    throw new WidgetPackageException("missing_entrypoint", $"Installed widget entrypoint is missing: {entrypointPath}");
                FileSystemSafety.EnsureNoReparsePoints(_root, entrypointPath);
                result.Add(new InstalledWidgetVersion(manifest.Id, version, versionDirectory, manifest));
            }
        }
        return result.OrderBy(item => item.Id, StringComparer.Ordinal)
            .ThenByDescending(item => item.Version)
            .ToArray();
    }
}
