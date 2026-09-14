using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

public sealed class WidgetCatalog
{
    private readonly string _root;
    private readonly string _packagesRoot;
    private readonly CatalogStateStore _stateStore;
    private readonly CatalogOperationLock _operationLock;
    private readonly WidgetCatalogOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IWidgetUninstallAuthorityParticipant? _uninstallAuthority;

    public WidgetCatalog(string currentUserRoot, WidgetCatalogOptions? options = null)
        : this(currentUserRoot, options, TimeProvider.System)
    {
    }

    internal WidgetCatalog(
        string currentUserRoot,
        IWidgetUninstallAuthorityParticipant uninstallAuthority)
        : this(currentUserRoot, null, TimeProvider.System, uninstallAuthority)
    {
    }

    internal WidgetCatalog(
        string currentUserRoot,
        WidgetCatalogOptions? options,
        TimeProvider timeProvider)
        : this(currentUserRoot, options, timeProvider, null)
    {
    }

    internal WidgetCatalog(
        string currentUserRoot,
        WidgetCatalogOptions? options,
        TimeProvider timeProvider,
        IWidgetUninstallAuthorityParticipant? uninstallAuthority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserRoot);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _root = Path.GetFullPath(currentUserRoot);
        _packagesRoot = Path.Combine(_root, "packages");
        _options = options ?? new WidgetCatalogOptions();
        _options.Validate();
        _timeProvider = timeProvider;
        _uninstallAuthority = uninstallAuthority;
        _stateStore = new CatalogStateStore(_root);
        _operationLock = new CatalogOperationLock(_root);
    }

    public string Root => _root;

    public WidgetPackageInstaller CreateInstaller() => new(_root, _options);

    public async Task<InstalledWidgetVersion> InstallAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
        => await InstallAsync(
            packagePath, WidgetPackageTrustApproval.None, cancellationToken);

    public async Task<InstalledWidgetVersion> InstallAsync(
        string packagePath,
        WidgetPackageTrustApproval trustApproval,
        CancellationToken cancellationToken = default)
    {
        ValidateTrustApproval(trustApproval);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        return await CreateInstaller().InstallAsync(
            packagePath,
            (inspection, token) => PreparePackageInstallUnderLockAsync(
                inspection, trustApproval, token),
            cancellationToken);
    }

    public async Task<InstalledWidgetVersion> InstallAsync(
        Stream packageStream,
        CancellationToken cancellationToken = default)
        => await InstallAsync(
            packageStream, WidgetPackageTrustApproval.None, cancellationToken);

    public async Task<InstalledWidgetVersion> InstallAsync(
        Stream packageStream,
        WidgetPackageTrustApproval trustApproval,
        CancellationToken cancellationToken = default)
    {
        ValidateTrustApproval(trustApproval);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        return await CreateInstaller().InstallAsync(
            packageStream,
            (inspection, token) => PreparePackageInstallUnderLockAsync(
                inspection, trustApproval, token),
            cancellationToken);
    }

    internal async Task<InstalledWidgetVersion> InstallAsync(
        Stream packageStream,
        Func<WidgetPackageInspection, CancellationToken, Task> trustedPrePublish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        ArgumentNullException.ThrowIfNull(trustedPrePublish);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        return await CreateInstaller().InstallAsync(
            packageStream,
            async (inspection, token) =>
            {
                await PreparePackageInstallUnderLockAsync(
                        inspection, WidgetPackageTrustApproval.None, token)
                    .ConfigureAwait(false);
                await trustedPrePublish(inspection, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // Called by trusted Settings/CLI update flows after explicit review.
    // Stage/validate first, pin the old version while publishing, then select
    // the new version disabled. Failed validation never stops the old widget.
    internal async Task<InstalledWidgetVersion> UpdateFromFileAsync(
        Stream packageStream, string expectedTargetHash, WidgetPackageTrustApproval trustApproval,
        Func<WidgetPackageInspection, CancellationToken, Task> trustedPrePublish,
        CancellationToken cancellationToken = default)
    {
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken).ConfigureAwait(false);
        var installed = await CreateInstaller().InstallAsync(packageStream, async (incoming, token) =>
        {
            var expectedWidgetId = incoming.Id;
            var targetHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expectedWidgetId)));
            if (!string.Equals(targetHash, expectedTargetHash, StringComparison.OrdinalIgnoreCase))
                throw new WidgetPackageException("update_wrong_widget", "Choose an update for the selected widget.");
            await DemandNoPendingUninstallAsync(expectedWidgetId, token).ConfigureAwait(false);
            RequireFullTrustApproval(incoming.Manifest, true, trustApproval);
            var all = DiscoverInstalledVersions(token);
            var snapshot = await DiscoverAsync(token).ConfigureAwait(false);
            var current = snapshot.Widgets.SingleOrDefault(item => item.Id == expectedWidgetId)
                ?? throw new WidgetPackageException("update_target_missing", "The widget is no longer installed.");
            if (incoming.Manifest.Publisher != current.ActiveVersion.Manifest.Publisher)
                throw new WidgetPackageException("update_publisher_changed", "The update has a different publisher.");
            if (WidgetManifestTrust.Resolve(incoming.Manifest) != WidgetManifestTrust.Resolve(current.ActiveVersion.Manifest))
                throw new WidgetPackageException("update_trust_changed", "The update changes how this widget runs.");
            if (incoming.Version <= current.ActiveVersion.Version || current.Versions.Any(item => item.Version == incoming.Version))
                throw new WidgetPackageException("update_not_newer", "Choose a newer version that is not already installed.");
            if (!WidgetHostCompatibility.Evaluate(incoming.Manifest).IsSupported)
                throw new WidgetPackageException("update_incompatible", "The update is not compatible with this WidgetRail version.");
            EnforceProspectiveInstallLimits(all, current.Versions.ToArray(), incoming);
            await trustedPrePublish(incoming, token).ConfigureAwait(false);
            await _stateStore.MutateAsync(state =>
            {
                var entries = state.Widgets.ToList();
                var index = entries.FindIndex(item => item.Id == expectedWidgetId);
                if (index >= 0) entries[index] = entries[index] with { ActiveVersion = current.ActiveVersion.Version.ToString() };
                else entries.Add(new CatalogStateEntry { Id = expectedWidgetId, Enabled = current.Enabled,
                    ActiveVersion = current.ActiveVersion.Version.ToString(), Order = entries.Count == 0 ? 0 : entries.Max(item => item.Order) + 1 });
                return state with { Version = 2, Widgets = entries.OrderBy(item => item.Order).ToArray() };
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        // Publication is durable. Finish with a non-cancellable state commit;
        // if storage fails, the old explicit pin still prevents activation.
        try
        {
            await _stateStore.MutateAsync(state => state with
            {
                Widgets = state.Widgets.Select(item => item.Id == installed.Id
                    ? item with { Enabled = false, ActiveVersion = installed.Version.ToString() } : item).ToArray(),
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WidgetPackageException)
        {
            throw new WidgetPackageException("update_selection_failed", "The new version was installed. Review installed versions to finish the update.", exception);
        }
        return installed;
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

    /// <summary>
    /// Produces a bounded control-plane projection from canonical package and
    /// version directory names plus validated catalog state. Candidate package
    /// manifests and executable content are never opened or trusted.
    /// </summary>
    public async Task<WidgetCatalogHealthSnapshot> InspectHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var directories = DiscoverRepairDirectories(cancellationToken);
        string? failureCode = null;
        if (directories.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() >
            _options.MaximumInstalledWidgetIds)
            failureCode = "installed_widget_id_limit";
        else if (directories.GroupBy(item => item.Id, StringComparer.Ordinal)
                 .Any(group => group.Count() > _options.MaximumVersionsPerWidget))
            failureCode = "installed_widget_version_limit";
        else if (directories.Count > _options.MaximumInstalledVersions)
            failureCode = "installed_version_limit";

        var stateById = state.Widgets.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var candidates = directories
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var ordered = group.OrderByDescending(item => item.Version).ToArray();
                var hasState = stateById.TryGetValue(group.Key, out var saved);
                var selected = hasState && saved!.ActiveVersion is not null
                    ? Version.Parse(saved.ActiveVersion)
                    : ordered[0].Version;
                var enabled = hasState && saved!.Enabled;
                return ordered.Select(item => new WidgetCatalogRepairCandidate(
                    item.Id, item.Version, enabled, item.Version == selected));
            })
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ThenByDescending(item => item.Version)
            .ToArray();
        return new WidgetCatalogHealthSnapshot(failureCode, candidates);
    }

    /// <summary>
    /// Retires exactly one inactive, non-selected version using only its
    /// canonical directory identity. Cancellation is not observed after the
    /// atomic move removes that version from discovery.
    /// </summary>
    public Task<WidgetVersionRemovalResult> RemoveInactiveVersionAsync(
        string widgetId, Version version, CancellationToken cancellationToken = default) =>
        RemoveInactiveVersionCoreAsync(widgetId, version, null, cancellationToken);

    /// <summary>Removes only the reviewed content while holding the catalog operation lock.
    /// Selection and content changes invalidate the confirmation before retirement.</summary>
    public Task<WidgetVersionRemovalResult> RemoveInactiveVersionConfirmedAsync(
        string widgetId, Version version, string expectedContentDigest, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentDigest);
        return RemoveInactiveVersionCoreAsync(widgetId, version, expectedContentDigest, cancellationToken);
    }

    private async Task<WidgetVersionRemovalResult> RemoveInactiveVersionCoreAsync(
        string widgetId, Version version, string? expectedContentDigest, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        ArgumentNullException.ThrowIfNull(version);
        var canonicalVersion = version.ToString();

        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        var health = await InspectHealthAsync(cancellationToken).ConfigureAwait(false);
        var candidate = health.Candidates.SingleOrDefault(item =>
            item.Id == widgetId && item.Version == version)
            ?? throw new KeyNotFoundException(
                $"Widget '{widgetId}' version {canonicalVersion} is not installed.");
        if (candidate.Selected)
            throw new WidgetPackageException(
                "selected_version",
                $"Widget '{widgetId}' version {canonicalVersion} is selected and cannot be removed.");

        if (expectedContentDigest is not null)
        {
            var snapshot = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
            var installed = snapshot.Widgets.SingleOrDefault(widget => widget.Id == widgetId)?
                .Versions.SingleOrDefault(item => item.Version == version);
            if (installed is null || !string.Equals(installed.ContentDigest, expectedContentDigest, StringComparison.Ordinal))
                throw new WidgetPackageException("version_changed", "The version changed. Review it again before removing it.");
        }

        var packageDirectory = Path.Combine(_packagesRoot, widgetId);
        var versionDirectory = Path.Combine(packageDirectory, canonicalVersion);
        if (!FileSystemSafety.IsWithin(_packagesRoot, packageDirectory) ||
            !FileSystemSafety.IsWithin(packageDirectory, versionDirectory) ||
            !Directory.Exists(versionDirectory) ||
            !string.Equals(Path.GetFileName(packageDirectory), widgetId, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(versionDirectory), canonicalVersion, StringComparison.Ordinal))
            throw new WidgetPackageException(
                "package_not_found",
                $"Widget '{widgetId}' version {canonicalVersion} has no canonical package directory.");
        FileSystemSafety.EnsureTreeContainsNoReparsePoints(
            _root,
            versionDirectory,
            _options.MaximumInstalledEntries,
            cancellationToken.ThrowIfCancellationRequested);

        var stagingRoot = Path.Combine(_root, "staging");
        Directory.CreateDirectory(stagingRoot);
        FileSystemSafety.EnsureNoReparsePoints(_root, stagingRoot);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var retiredDirectory = Path.Combine(
            stagingRoot, $".uninstall-version-{Guid.NewGuid():N}");
        Directory.Move(versionDirectory, retiredDirectory);

        var cleanupPending = !await TryDeleteRetiredTreeAsync(retiredDirectory)
            .ConfigureAwait(false);
        try
        {
            if (Directory.Exists(packageDirectory) &&
                !Directory.EnumerateFileSystemEntries(packageDirectory).Any())
                Directory.Delete(packageDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            cleanupPending = true;
        }
        return new WidgetVersionRemovalResult(widgetId, version, cleanupPending);
    }

    public async Task SetEnabledAsync(string widgetId, bool enabled, CancellationToken cancellationToken = default)
        => await SetEnabledAsync(
            widgetId, enabled, WidgetPackageTrustApproval.None, cancellationToken);

    public async Task SetEnabledAsync(
        string widgetId,
        bool enabled,
        WidgetPackageTrustApproval trustApproval,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        ValidateTrustApproval(trustApproval);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await DiscoverAsync(cancellationToken);
        var selected = snapshot.Widgets.SingleOrDefault(widget => widget.Id == widgetId);
        if (selected is null)
            throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");
        RequireFullTrustApproval(selected.ActiveVersion.Manifest, enabled, trustApproval);

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
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        await SetActiveVersionUnderLockAsync(widgetId, version, cancellationToken);
    }

    public async Task<WidgetVersionChange> RollbackAsync(
        string widgetId,
        Version? requestedVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
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
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await DiscoverAsync(cancellationToken);
        var widget = snapshot.Widgets.SingleOrDefault(candidate => candidate.Id == widgetId)
            ?? throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");
        return await UninstallUnderLockAsync(widget, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<WidgetUninstallInspection> InspectUninstallAsync(
        string widgetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await DiscoverAsync(cancellationToken);
        var widget = snapshot.Widgets.SingleOrDefault(candidate => candidate.Id == widgetId)
            ?? throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");
        return CreateUninstallInspection(widget);
    }

    internal async Task<WidgetUninstallResult> UninstallConfirmedAsync(
        string widgetId,
        string publisherId,
        Version activeVersion,
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);
        ArgumentNullException.ThrowIfNull(activeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationToken);
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = await DiscoverAsync(cancellationToken);
        var widget = snapshot.Widgets.SingleOrDefault(candidate => candidate.Id == widgetId)
            ?? throw new KeyNotFoundException($"Widget '{widgetId}' is not installed.");
        var current = CreateUninstallInspection(widget);
        if (!string.Equals(current.PublisherId, publisherId, StringComparison.Ordinal) ||
            current.ActiveVersion != activeVersion ||
            !TryEqualsConfirmationToken(current.ConfirmationToken, confirmationToken))
            throw new WidgetPackageException(
                "confirmation_stale", "The installed widget identity changed before uninstall.");
        return await UninstallUnderLockAsync(widget, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WidgetUninstallResult> UninstallUnderLockAsync(
        CatalogWidget widget,
        CancellationToken cancellationToken)
    {
        var widgetId = widget.Id;
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
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        var retiredDirectory = Path.Combine(
            stagingRoot, $".uninstall-{Guid.NewGuid():N}");
        var markerPath = PendingWidgetUninstallStore.MarkerPath(retiredDirectory);
        if (_uninstallAuthority is not null)
        {
            await PendingWidgetUninstallStore.WriteAsync(
                markerPath,
                new PendingWidgetUninstall(
                    1,
                    widgetId,
                    widget.Versions.Select(InstalledWidgetAuthority.PublisherId)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray()),
                cancellationToken).ConfigureAwait(false);
        }
        var priorState = await _stateStore.LoadAsync(cancellationToken);
        var stateMutated = false;
        var directoryMoved = false;
        var authorityCommitted = false;
        var cleanupPending = false;
        try
        {
            await _stateStore.MutateAsync(
                state => RemoveStateEntry(state, widgetId), cancellationToken)
                .ConfigureAwait(false);
            stateMutated = true;
            Directory.Move(packageDirectory, retiredDirectory);
            directoryMoved = true;
            if (_uninstallAuthority is not null)
            {
                var retirement = await _uninstallAuthority.RetirePackageAsync(
                    widgetId, cancellationToken).ConfigureAwait(false);
                if (!retirement.Committed)
                    throw new WidgetPackageException(
                        "registration_cleanup_failed",
                        "Portable app registration cleanup did not commit.");
                authorityCommitted = true;
                cleanupPending |= retirement.CleanupPending;
                if (!retirement.CleanupPending)
                    cleanupPending |= !TryDeletePendingMarker(markerPath);
            }
        }
        catch when (!authorityCommitted)
        {
            var rollbackComplete = true;
            if (directoryMoved)
            {
                try
                {
                    if (!Directory.Exists(packageDirectory) &&
                        Directory.Exists(retiredDirectory))
                        Directory.Move(retiredDirectory, packageDirectory);
                }
                catch (Exception exception) when (exception is IOException or
                    UnauthorizedAccessException)
                {
                    rollbackComplete = false;
                }
            }
            if (stateMutated && rollbackComplete)
            {
                try
                {
                    await _stateStore.MutateAsync(
                        _ => priorState, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or
                    UnauthorizedAccessException or WidgetPackageException)
                {
                    rollbackComplete = false;
                }
            }
            if (rollbackComplete) _ = TryDeletePendingMarker(markerPath);
            if (!rollbackComplete)
                throw new WidgetPackageException(
                    "uninstall_recovery_pending",
                    "Package uninstall requires pending recovery.");
            throw;
        }

        // The widget is no longer discoverable after the move. Finish bounded
        // physical cleanup without accepting cancellation at this commit point.
        cleanupPending |= !await TryDeleteRetiredTreeAsync(retiredDirectory)
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

    private static WidgetUninstallInspection CreateUninstallInspection(CatalogWidget widget)
    {
        var publisherId = InstalledWidgetAuthority.PublisherId(widget.ActiveVersion);
        var material = new StringBuilder()
            .Append(widget.Id).Append('\n')
            .Append(publisherId).Append('\n')
            .Append(widget.ActiveVersion.Version).Append('\n')
            .Append(widget.Enabled ? '1' : '0');
        foreach (var version in widget.Versions.OrderBy(item => item.Version))
            material.Append('\n').Append(version.Version).Append('\n').Append(version.ContentDigest);
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
        return new WidgetUninstallInspection(
            widget.Id,
            widget.Name,
            publisherId,
            widget.ActiveVersion.Version,
            widget.Versions.Count,
            widget.Enabled,
            token);
    }

    private static bool TryEqualsConfirmationToken(string expected, string actual)
    {
        if (expected.Length != actual.Length) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
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

    private async Task RecoverPendingAndCleanupRetiredTreesAsync(
        CancellationToken cancellationToken)
    {
        var stagingRoot = Path.Combine(_root, "staging");
        if (!Directory.Exists(stagingRoot)) return;
        try
        {
            FileSystemSafety.EnsureNoReparsePoints(_root, stagingRoot);
            var markedDirectories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var pendingMarkers = PendingMarkers(stagingRoot);
            foreach (var marker in pendingMarkers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var retired = marker[..^".pending.json".Length];
                markedDirectories.Add(retired);
                var pending = await PendingWidgetUninstallStore.ReadAsync(
                    marker, cancellationToken).ConfigureAwait(false);
                var source = Path.Combine(_packagesRoot, pending.PackageId);
                if (Directory.Exists(source) && !Directory.Exists(retired))
                {
                    _ = TryDeletePendingMarker(marker);
                    continue;
                }
                if (_uninstallAuthority is null ||
                    Directory.Exists(source) && Directory.Exists(retired))
                    continue;
                WidgetUninstallAuthorityCommit retirement;
                try
                {
                    retirement = await _uninstallAuthority.RetirePackageAsync(
                        pending.PackageId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or
                    UnauthorizedAccessException or WidgetPackageException)
                {
                    continue;
                }
                if (!retirement.Committed) continue;
                if (!retirement.CleanupPending)
                    _ = TryDeletePendingMarker(marker);
                if (Directory.Exists(retired))
                    _ = await TryDeleteRetiredTreeAsync(retired).ConfigureAwait(false);
            }
            foreach (var retired in Directory.EnumerateDirectories(
                         stagingRoot, ".uninstall-*", SearchOption.TopDirectoryOnly).Take(16))
            {
                if (markedDirectories.Contains(retired) ||
                    File.Exists(PendingWidgetUninstallStore.MarkerPath(retired)))
                    continue;
                if (!FileSystemSafety.IsWithin(stagingRoot, retired)) continue;
                try
                {
                    FileSystemSafety.EnsureTreeContainsNoReparsePoints(
                        stagingRoot, retired, _options.MaximumInstalledEntries);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            // Staging cleanup is maintenance; it must not block a safe package mutation.
        }
    }

    private static bool TryDeletePendingMarker(string markerPath)
    {
        if (!File.Exists(markerPath)) return true;
        try
        {
            if ((File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
                return false;
            File.Delete(markerPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task DemandNoPendingUninstallAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        var stagingRoot = Path.Combine(_root, "staging");
        if (!Directory.Exists(stagingRoot)) return;
        foreach (var marker in PendingMarkers(stagingRoot))
        {
            var pending = await PendingWidgetUninstallStore.ReadAsync(
                marker, cancellationToken).ConfigureAwait(false);
            if (string.Equals(
                    pending.PackageId, packageId, StringComparison.Ordinal))
                throw new WidgetPackageException(
                    "pending_uninstall_cleanup",
                    $"Widget '{packageId}' has pending uninstall cleanup.");
        }
    }

    private static IReadOnlyList<string> PendingMarkers(string stagingRoot)
    {
        var markers = Directory.EnumerateFiles(
                stagingRoot,
                ".uninstall-*.pending.json",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Take(PendingWidgetUninstallStore.MaximumPendingRecords + 1)
            .ToArray();
        if (markers.Length > PendingWidgetUninstallStore.MaximumPendingRecords)
            throw new WidgetPackageException(
                "pending_uninstall_capacity",
                "Pending uninstall recovery exceeds its bound.");
        return markers;
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
        WidgetPackageTrustApproval trustApproval,
        CancellationToken cancellationToken)
    {
        await DemandNoPendingUninstallAsync(
            inspection.Id, cancellationToken).ConfigureAwait(false);
        RequireFullTrustApproval(inspection.Manifest, enabling: true, trustApproval);
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

    private static void RequireFullTrustApproval(
        WidgetManifest manifest,
        bool enabling,
        WidgetPackageTrustApproval approval)
    {
        if (!enabling || WidgetManifestTrust.Resolve(manifest) !=
            WidgetExecutionTrust.FullTrustCurrentUser) return;
        if (approval != WidgetPackageTrustApproval.FullTrustCurrentUser)
            throw new WidgetPackageException(
                "full_trust_approval_required",
                "This package runs as an ordinary current-user process, is not AppContainer sandboxed, and requires explicit full-trust approval.");
    }

    private static void ValidateTrustApproval(WidgetPackageTrustApproval approval)
    {
        if (!Enum.IsDefined(approval))
            throw new ArgumentOutOfRangeException(nameof(approval));
    }

    /// <summary>Moves the listed installed IDs to the front in the supplied order and preserves the remaining relative order.</summary>
    public async Task SetOrderAsync(IReadOnlyList<string> widgetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widgetIds);
        if (widgetIds.Count != widgetIds.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Widget order cannot contain duplicate IDs.", nameof(widgetIds));

        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var widgetId in widgetIds)
            await DemandNoPendingUninstallAsync(widgetId, cancellationToken)
                .ConfigureAwait(false);
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
                    _root, versionDirectory, _options, CheckBudget);
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
                    WidgetEntrypointRuntimes.ResolvePackagePath(manifest.Entrypoint)
                        .Replace('/', Path.DirectorySeparatorChar)));
                if (!FileSystemSafety.IsWithin(versionDirectory, entrypointPath) || !File.Exists(entrypointPath))
                    throw new WidgetPackageException("missing_entrypoint", $"Installed widget entrypoint is missing: {entrypointPath}");
                FileSystemSafety.EnsureNoReparsePoints(_root, entrypointPath);
                result.Add(new InstalledWidgetVersion(
                    manifest.Id, version, versionDirectory, manifest, verification.ContentDigest)
                {
                    VerifiedWrssDigests = verification.WrssDigests,
                    VerifiedFiles = verification.VerifiedFiles,
                    VerificationOptions = _options,
                    VerifiedEntryCount = verification.EntryCount,
                    VerifiedTotalBytes = verification.TotalBytes,
                });
            }
        }
        return result.OrderBy(item => item.Id, StringComparer.Ordinal)
            .ThenByDescending(item => item.Version)
            .ToArray();
    }

    private IReadOnlyList<(string Id, Version Version)> DiscoverRepairDirectories(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_packagesRoot)) return [];
        var maximumIds = Math.Min(10_000, _options.MaximumInstalledWidgetIds + 32);
        var maximumPerWidget = Math.Min(10_000, _options.MaximumVersionsPerWidget + 128);
        var maximumVersions = Math.Min(10_000, _options.MaximumInstalledVersions + 512);
        var started = _timeProvider.GetTimestamp();
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(started) > _options.MaximumDiscoveryDuration)
                throw new WidgetPackageException(
                    "installed_repair_time_limit",
                    "Installed widget repair inspection exceeded its time limit.");
        }

        CheckBudget();
        FileSystemSafety.EnsureNoReparsePoints(_root, _packagesRoot);
        var result = new List<(string Id, Version Version)>();
        foreach (var idDirectory in EnumerateBoundedDirectories(
                     _packagesRoot, maximumIds, "installed_repair_limit",
                     "Installed widget catalog exceeds the repair inspection limit."))
        {
            CheckBudget();
            FileSystemSafety.EnsureNoReparsePoints(_root, idDirectory);
            var id = Path.GetFileName(idDirectory);
            if (!WidgetManifestValidator.IsValidPackageIdentity(id))
                throw new WidgetPackageException(
                    "invalid_catalog_entry",
                    $"Installed widget ID directory is not canonical: {id}");
            foreach (var versionDirectory in EnumerateBoundedDirectories(
                         idDirectory, maximumPerWidget, "installed_repair_limit",
                         "Installed widget version history exceeds the repair inspection limit."))
            {
                CheckBudget();
                FileSystemSafety.EnsureNoReparsePoints(_root, versionDirectory);
                if (result.Count == maximumVersions)
                    throw new WidgetPackageException(
                        "installed_repair_limit",
                        "Installed widget catalog exceeds the repair inspection limit.");
                var versionText = Path.GetFileName(versionDirectory);
                if (!Version.TryParse(versionText, out var version) ||
                    !string.Equals(version.ToString(), versionText, StringComparison.Ordinal))
                    throw new WidgetPackageException(
                        "invalid_catalog_entry",
                        $"Installed widget version directory is not canonical: {versionText}");
                result.Add((id, version));
            }
        }
        return result;
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
