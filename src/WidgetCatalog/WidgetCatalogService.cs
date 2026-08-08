using System.Text.Json;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetCatalog;

public sealed class WidgetCatalog
{
    private readonly string _root;
    private readonly string _packagesRoot;
    private readonly CatalogStateStore _stateStore;
    private readonly CatalogOperationLock _operationLock;
    private readonly WidgetCatalogOptions _options;

    public WidgetCatalog(string currentUserRoot, WidgetCatalogOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserRoot);
        _root = Path.GetFullPath(currentUserRoot);
        _packagesRoot = Path.Combine(_root, "packages");
        _options = options ?? new WidgetCatalogOptions();
        _options.Validate();
        _stateStore = new CatalogStateStore(_root);
        _operationLock = new CatalogOperationLock(_root);
    }

    public string Root => _root;

    public WidgetPackageInstaller CreateInstaller() => new(_root, _options);

    public async Task<InstalledWidgetVersion> InstallAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        return await CreateInstaller().InstallAsync(
            packagePath,
            (inspection, token) => PreparePackageInstallUnderLockAsync(inspection, token),
            cancellationToken);
    }

    public async Task<InstalledWidgetVersion> InstallAsync(
        Stream packageStream,
        CancellationToken cancellationToken = default)
    {
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        return await CreateInstaller().InstallAsync(
            packageStream,
            (inspection, token) => PreparePackageInstallUnderLockAsync(inspection, token),
            cancellationToken);
    }

    public async Task<WidgetCatalogSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var versions = DiscoverInstalledVersions();
        var state = await _stateStore.LoadAsync(cancellationToken);
        foreach (var pinned in state.Widgets.Where(entry => entry.ActiveVersion is not null))
        {
            if (!versions.Any(version =>
                    version.Id == pinned.Id &&
                    string.Equals(version.Version.ToString(), pinned.ActiveVersion, StringComparison.Ordinal)))
                throw new WidgetPackageException(
                    "active_version_missing",
                    $"Widget '{pinned.Id}' pins missing version {pinned.ActiveVersion}; reinstall that exact package version to repair it disabled.");
        }
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
                var hasState = stateById.TryGetValue(group.Key, out var saved);
                var active = orderedVersions[0];
                if (hasState && saved!.ActiveVersion is not null)
                {
                    active = orderedVersions.SingleOrDefault(item =>
                        string.Equals(item.Version.ToString(), saved.ActiveVersion, StringComparison.Ordinal))
                        ?? throw new WidgetPackageException(
                            "active_version_missing",
                            $"Widget '{group.Key}' pins missing version {saved.ActiveVersion}; reinstall it or select an installed version.");
                }
                return new CatalogWidget(
                    group.Key,
                    active.Manifest.Name,
                    hasState ? saved!.Enabled : false,
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
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
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

    /// <summary>
    /// Pins the installed version used by the host. Version changes require the
    /// widget to be disabled so review and activation remain separate actions.
    /// </summary>
    public async Task SetActiveVersionAsync(
        string widgetId,
        Version version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        ArgumentNullException.ThrowIfNull(version);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await SetActiveVersionUnderLockAsync(widgetId, version, cancellationToken);
    }

    public async Task<WidgetVersionChange> RollbackAsync(
        string widgetId,
        Version? requestedVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        var snapshot = await DiscoverAsync(cancellationToken);
        var widget = snapshot.Widgets.SingleOrDefault(candidate => candidate.Id == widgetId)
            ?? throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");
        if (widget.Enabled)
            throw new WidgetPackageException(
                "widget_enabled", $"Widget '{widgetId}' is enabled. Disable it before selecting a rollback version.");

        var target = requestedVersion ?? widget.Versions
            .Select(version => version.Version)
            .Where(version => version < widget.ActiveVersion.Version)
            .OrderByDescending(version => version)
            .FirstOrDefault()
            ?? throw new WidgetPackageException(
                "no_rollback_version",
                $"Widget '{widgetId}' has no installed version older than {widget.ActiveVersion.Version}.");
        if (target >= widget.ActiveVersion.Version)
            throw new WidgetPackageException(
                "invalid_rollback",
                $"Rollback target {target} must be older than active version {widget.ActiveVersion.Version}.");
        if (!widget.Versions.Any(version => version.Version == target))
            throw new KeyNotFoundException($"Widget '{widgetId}' version {target} is not installed.");

        await SetActiveVersionUnderLockAsync(widgetId, target, cancellationToken);
        return new WidgetVersionChange(widgetId, widget.ActiveVersion.Version, target);
    }

    private async Task SetActiveVersionUnderLockAsync(
        string widgetId,
        Version version,
        CancellationToken cancellationToken)
    {
        var canonicalVersion = version.ToString();
        var snapshot = await DiscoverAsync(cancellationToken);
        var widget = snapshot.Widgets.SingleOrDefault(candidate => candidate.Id == widgetId)
            ?? throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");
        if (!widget.Versions.Any(candidate => candidate.Version == version))
            throw new KeyNotFoundException($"Widget '{widgetId}' version {canonicalVersion} is not installed.");

        await _stateStore.MutateAsync(state =>
        {
            var entries = state.Widgets.ToList();
            var index = entries.FindIndex(entry => entry.Id == widgetId);
            if (index >= 0)
            {
                if (entries[index].Enabled)
                    throw new WidgetPackageException(
                        "widget_enabled",
                        $"Widget '{widgetId}' is enabled. Disable it before selecting another version.");
                entries[index] = entries[index] with { ActiveVersion = canonicalVersion };
            }
            else
            {
                entries.Add(new CatalogStateEntry
                {
                    Id = widgetId,
                    Enabled = false,
                    Order = entries.Count == 0 ? 0 : entries.Max(entry => entry.Order) + 1,
                    ActiveVersion = canonicalVersion,
                });
            }
            return state with { Version = 2, Widgets = entries.OrderBy(entry => entry.Order).ToArray() };
        }, cancellationToken);
    }

    private async Task PreparePackageInstallUnderLockAsync(
        WidgetPackageInspection inspection,
        CancellationToken cancellationToken)
    {
        var installed = DiscoverInstalledVersions()
            .Where(version => version.Id == inspection.Id)
            .OrderByDescending(version => version.Version)
            .ToArray();
        var reviewedVersion = installed.FirstOrDefault()?.Version.ToString();
        var incomingVersion = inspection.Version.ToString();

        await _stateStore.MutateAsync(state =>
        {
            var entries = state.Widgets.ToList();
            var index = entries.FindIndex(entry => entry.Id == inspection.Id);
            if (index < 0 && installed.Length == 0) return state;
            if (index >= 0)
            {
                var entry = entries[index];
                if (entry.ActiveVersion is not null &&
                    !installed.Any(version => string.Equals(
                        version.Version.ToString(), entry.ActiveVersion, StringComparison.Ordinal)))
                {
                    if (!string.Equals(entry.ActiveVersion, incomingVersion, StringComparison.Ordinal))
                        throw new WidgetPackageException(
                            "active_version_missing",
                            $"Widget '{inspection.Id}' pins missing version {entry.ActiveVersion}; reinstall that exact package version to repair it disabled.");
                    entries[index] = entry with { Enabled = false };
                }
                else
                {
                    if (installed.Length == 0)
                    {
                        entries[index] = entry with
                        {
                            Enabled = false,
                            ActiveVersion = incomingVersion,
                        };
                    }
                    else if (entry.Enabled)
                        throw new WidgetPackageException(
                            "widget_enabled",
                            $"Widget '{inspection.Id}' is enabled. Disable it before installing an update so unreviewed code cannot become active.");
                    else
                    {
                        entries[index] = entry with
                        {
                            Enabled = false,
                            ActiveVersion = entry.ActiveVersion ?? reviewedVersion ?? incomingVersion,
                        };
                    }
                }
            }
            else
            {
                if (reviewedVersion is null)
                    throw new WidgetPackageException("catalog_race", "Installed widget state changed during package publication.");
                entries.Add(new CatalogStateEntry
                {
                    Id = inspection.Id,
                    Enabled = false,
                    Order = entries.Count == 0 ? 0 : entries.Max(entry => entry.Order) + 1,
                    ActiveVersion = reviewedVersion,
                });
            }
            return state with { Version = 2, Widgets = entries.OrderBy(entry => entry.Order).ToArray() };
        }, cancellationToken);
    }

    /// <summary>Moves the listed installed IDs to the front in the supplied order and preserves the remaining relative order.</summary>
    public async Task SetOrderAsync(IReadOnlyList<string> widgetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widgetIds);
        if (widgetIds.Count != widgetIds.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Widget order cannot contain duplicate IDs.", nameof(widgetIds));

        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        var snapshot = await DiscoverAsync(cancellationToken);
        var installed = snapshot.Widgets.ToDictionary(widget => widget.Id, StringComparer.Ordinal);
        var missing = widgetIds.FirstOrDefault(id => !installed.ContainsKey(id));
        if (missing is not null) throw new KeyNotFoundException($"Widget '{missing}' is not installed.");

        var finalIds = widgetIds.Concat(snapshot.Widgets.Select(widget => widget.Id)
            .Where(id => !widgetIds.Contains(id, StringComparer.Ordinal))).ToArray();
        await _stateStore.MutateAsync(state =>
        {
            var saved = state.Widgets.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            var entries = finalIds.Select((id, order) =>
            {
                var hasSaved = saved.TryGetValue(id, out var entry);
                return new CatalogStateEntry
                {
                    Id = id,
                    Order = order,
                    Enabled = hasSaved ? entry!.Enabled : installed[id].Enabled,
                    ActiveVersion = hasSaved ? entry!.ActiveVersion : null,
                };
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
                var contentDigest = InstalledPackageIntegrity.Verify(
                    _root, versionDirectory, _options);
                result.Add(new InstalledWidgetVersion(
                    manifest.Id, version, versionDirectory, manifest, contentDigest));
            }
        }
        return result.OrderBy(item => item.Id, StringComparer.Ordinal)
            .ThenByDescending(item => item.Version)
            .ToArray();
    }
}
