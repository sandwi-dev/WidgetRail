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
    private readonly TimeProvider _timeProvider;

    public WidgetCatalog(string currentUserRoot, WidgetCatalogOptions? options = null)
        : this(currentUserRoot, options, TimeProvider.System)
    {
    }

    internal WidgetCatalog(
        string currentUserRoot,
        WidgetCatalogOptions? options,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserRoot);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _root = Path.GetFullPath(currentUserRoot);
        _packagesRoot = Path.Combine(_root, "packages");
        _options = options ?? new WidgetCatalogOptions();
        _options.Validate();
        _timeProvider = timeProvider;
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
        TryCleanupRetiredTrees();
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
        TryCleanupRetiredTrees();
        return await CreateInstaller().InstallAsync(
            packageStream,
            (inspection, token) => PreparePackageInstallUnderLockAsync(inspection, token),
            cancellationToken);
    }

    public async Task<WidgetCatalogSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var versions = DiscoverInstalledVersions(cancellationToken);
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

    /// <summary>
    /// Removes every immutable version of one disabled widget. The package
    /// tree is first retired from discovery with an atomic directory move;
    /// cancellation is not observed after that commit point. Locked retired
    /// files are reported as pending and retried by a later package mutation.
    /// </summary>
    public async Task<WidgetUninstallResult> UninstallAsync(
        string widgetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        var snapshot = await DiscoverAsync(cancellationToken);
        var widget = snapshot.Widgets.SingleOrDefault(candidate => candidate.Id == widgetId)
            ?? throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");
        if (widget.Enabled)
            throw new WidgetPackageException(
                "widget_enabled", $"Widget '{widgetId}' is enabled. Disable it before uninstalling it.");

        var packageDirectory = Path.Combine(_packagesRoot, widget.Id);
        if (!Directory.Exists(packageDirectory))
            throw new WidgetPackageException(
                "package_not_found", $"Widget '{widgetId}' has no installed package directory.");
        FileSystemSafety.EnsureTreeContainsNoReparsePoints(_root, packageDirectory);
        if (widget.Versions.Any(version =>
                !FileSystemSafety.IsWithin(packageDirectory, version.InstallPath)))
            throw new WidgetPackageException(
                "path_escape", $"Widget '{widgetId}' contains an install path outside its package directory.");

        var stagingRoot = Path.Combine(_root, "staging");
        Directory.CreateDirectory(stagingRoot);
        FileSystemSafety.EnsureNoReparsePoints(_root, stagingRoot);
        TryCleanupRetiredTrees();
        var retiredDirectory = Path.Combine(stagingRoot, $".uninstall-{Guid.NewGuid():N}");
        var priorState = await _stateStore.LoadAsync(cancellationToken);
        var stateMutated = false;
        try
        {
            await _stateStore.MutateAsync(
                state => RemoveStateEntry(state, widgetId), cancellationToken)
                .ConfigureAwait(false);
            stateMutated = true;
            Directory.Move(packageDirectory, retiredDirectory);
        }
        catch
        {
            if (stateMutated)
            {
                await _stateStore.MutateAsync(
                    _ => priorState, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            throw;
        }

        // The widget is no longer discoverable after the move. Finish bounded
        // physical cleanup without accepting cancellation at this commit point.
        var cleanupPending = !await TryDeleteRetiredTreeAsync(retiredDirectory)
            .ConfigureAwait(false);
        try
        {
            var packageParent = Path.GetDirectoryName(packageDirectory)!;
            if (Directory.Exists(packageParent) &&
                !Directory.EnumerateFileSystemEntries(packageParent).Any())
                Directory.Delete(packageParent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            cleanupPending = true;
        }

        return new WidgetUninstallResult(
            widgetId,
            widget.Versions.Select(version => version.Version).OrderDescending().ToArray(),
            cleanupPending);
    }

    private static CatalogState RemoveStateEntry(CatalogState state, string widgetId)
    {
        var entries = state.Widgets.Where(entry => entry.Id != widgetId)
            .OrderBy(entry => entry.Order)
            .Select((entry, order) => entry with { Order = order })
            .ToArray();
        return state with { Version = 2, Widgets = entries };
    }

    private static async Task<bool> TryDeleteRetiredTreeAsync(string retiredDirectory)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (true)
        {
            try
            {
                Directory.Delete(retiredDirectory, recursive: true);
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException &&
                DateTime.UtcNow < deadline)
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _ = exception;
                return false;
            }
        }
    }

    private void TryCleanupRetiredTrees()
    {
        var stagingRoot = Path.Combine(_root, "staging");
        if (!Directory.Exists(stagingRoot)) return;
        try
        {
            FileSystemSafety.EnsureNoReparsePoints(_root, stagingRoot);
            foreach (var retired in Directory.EnumerateDirectories(
                         stagingRoot, ".uninstall-*", SearchOption.TopDirectoryOnly).Take(16))
            {
                if (!FileSystemSafety.IsWithin(stagingRoot, retired)) continue;
                try
                {
                    FileSystemSafety.EnsureTreeContainsNoReparsePoints(stagingRoot, retired);
                    Directory.Delete(retired, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or
                    UnauthorizedAccessException or WidgetPackageException)
                {
                    // A disabled worker may still hold a package briefly. The
                    // next install/uninstall operation will retry this bounded sweep.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or WidgetPackageException)
        {
            // Staging cleanup is maintenance; it must not block a safe package mutation.
        }
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
        var allInstalled = DiscoverInstalledVersions(cancellationToken);
        var installed = allInstalled
            .Where(version => version.Id == inspection.Id)
            .OrderByDescending(version => version.Version)
            .ToArray();
        EnforceProspectiveInstallLimits(allInstalled, installed, inspection);
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

    private IReadOnlyList<InstalledWidgetVersion> DiscoverInstalledVersions(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_packagesRoot)) return [];
        var started = _timeProvider.GetTimestamp();
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(started) > _options.MaximumDiscoveryDuration)
                throw new WidgetPackageException(
                    "installed_discovery_time_limit",
                    "Installed widget discovery exceeded its time limit.");
        }

        CheckBudget();
        FileSystemSafety.EnsureNoReparsePoints(_root, _packagesRoot);
        var result = new List<InstalledWidgetVersion>();
        long aggregateBytes = 0;
        var aggregateEntries = 0;
        var idDirectories = EnumerateBoundedDirectories(
            _packagesRoot,
            _options.MaximumInstalledWidgetIds,
            "installed_widget_id_limit",
            "Installed widget ID count exceeds the catalog limit.");
        foreach (var idDirectory in idDirectories)
        {
            CheckBudget();
            FileSystemSafety.EnsureNoReparsePoints(_root, idDirectory);
            var directoryId = Path.GetFileName(idDirectory);
            var versionDirectories = EnumerateBoundedDirectories(
                idDirectory,
                _options.MaximumVersionsPerWidget,
                "installed_widget_version_limit",
                "Installed widget version count exceeds the per-widget limit.");
            foreach (var versionDirectory in versionDirectories)
            {
                CheckBudget();
                if (result.Count == _options.MaximumInstalledVersions)
                    throw new WidgetPackageException(
                        "installed_version_limit",
                        "Installed widget version count exceeds the catalog limit.");
                FileSystemSafety.EnsureNoReparsePoints(_root, versionDirectory);
                var manifestPath = Path.Combine(versionDirectory, "manifest.json");
                if (!File.Exists(manifestPath))
                    throw new WidgetPackageException("missing_manifest", $"Installed widget is missing manifest.json: {versionDirectory}");
                FileSystemSafety.EnsureNoReparsePoints(_root, manifestPath);
                var verification = InstalledPackageIntegrity.Verify(
                    _root, versionDirectory, _options);
                try
                {
                    aggregateEntries = checked(aggregateEntries + verification.EntryCount);
                    aggregateBytes = checked(aggregateBytes + verification.TotalBytes);
                }
                catch (OverflowException exception)
                {
                    throw new WidgetPackageException(
                        "installed_catalog_limit",
                        "Installed widget catalog accounting overflowed its limits.",
                        exception);
                }
                if (aggregateEntries > _options.MaximumInstalledEntries)
                    throw new WidgetPackageException(
                        "installed_entry_limit",
                        "Installed widget file count exceeds the catalog limit.");
                if (aggregateBytes > _options.MaximumInstalledBytes)
                    throw new WidgetPackageException(
                        "installed_byte_limit",
                        "Installed widget bytes exceed the catalog limit.");
                CheckBudget();
                var manifest = verification.Manifest;
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
                result.Add(new InstalledWidgetVersion(
                    manifest.Id, version, versionDirectory, manifest, verification.ContentDigest)
                {
                    VerifiedGbssDigests = verification.GbssDigests,
                    VerifiedEntryCount = verification.EntryCount,
                    VerifiedTotalBytes = verification.TotalBytes,
                });
            }
        }
        return result.OrderBy(item => item.Id, StringComparer.Ordinal)
            .ThenByDescending(item => item.Version)
            .ToArray();
    }

    private string[] EnumerateBoundedDirectories(
        string directory,
        int maximum,
        string limitCode,
        string limitMessage)
    {
        var entries = Directory.EnumerateFileSystemEntries(directory)
            .Take(checked(maximum + 1))
            .ToArray();
        if (entries.Length > maximum)
            throw new WidgetPackageException(limitCode, limitMessage);
        if (entries.Any(path => !Directory.Exists(path)))
            throw new WidgetPackageException(
                "invalid_catalog_entry",
                "Installed widget catalog contains an unexpected filesystem entry.");
        return entries.Order(StringComparer.Ordinal).ToArray();
    }

    private void EnforceProspectiveInstallLimits(
        IReadOnlyList<InstalledWidgetVersion> allInstalled,
        IReadOnlyList<InstalledWidgetVersion> sameId,
        WidgetPackageInspection inspection)
    {
        if (sameId.Any(version => version.Version == inspection.Version)) return;
        if (sameId.Count == 0 &&
            allInstalled.Select(version => version.Id).Distinct(StringComparer.Ordinal).Count() ==
            _options.MaximumInstalledWidgetIds)
            throw new WidgetPackageException(
                "installed_widget_id_limit",
                "Installing this package would exceed the installed widget ID limit.");
        if (sameId.Count == _options.MaximumVersionsPerWidget)
            throw new WidgetPackageException(
                "installed_widget_version_limit",
                "Installing this package would exceed the per-widget version limit.");
        if (allInstalled.Count == _options.MaximumInstalledVersions)
            throw new WidgetPackageException(
                "installed_version_limit",
                "Installing this package would exceed the installed version limit.");

        try
        {
            var entries = checked(allInstalled.Sum(version => version.VerifiedEntryCount) +
                                  inspection.EntryCount + 1);
            if (entries > _options.MaximumInstalledEntries)
                throw new WidgetPackageException(
                    "installed_entry_limit",
                    "Installing this package would exceed the installed file limit.");
            var bytes = checked(allInstalled.Sum(version => version.VerifiedTotalBytes) +
                                inspection.TotalUncompressedBytes +
                                InstalledPackageIntegrity.MaximumMetadataBytes);
            if (bytes > _options.MaximumInstalledBytes)
                throw new WidgetPackageException(
                    "installed_byte_limit",
                    "Installing this package would exceed the installed byte limit.");
        }
        catch (OverflowException exception)
        {
            throw new WidgetPackageException(
                "installed_catalog_limit",
                "Installed widget catalog accounting overflowed its limits.",
                exception);
        }
    }
}
