using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

public sealed partial class WidgetCatalog
{
    internal async Task<IReadOnlyList<string>> InitialPackageReceiptsAsync(CancellationToken cancellationToken) =>
        (await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false)).SeededPackageIds;

    // Only the trusted application bundle caller can use this API. It confers
    // no special publisher authority, full-access approval or capability grants.
    // Install/update/uninstall and receipt publication share one operation lock.
    internal async Task<bool> SeedInitialPackageAsync(Stream packageStream, string expectedPackageId,
        CancellationToken cancellationToken = default)
    {
        if (!WidgetManifestValidator.IsValidPackageIdentity(expectedPackageId) ||
            expectedPackageId is "widgetrail.firstparty.settings" or "widgetrail.firstparty.marketplace")
            throw new WidgetPackageException("invalid_initial_widget", "Initial widget identity is invalid.");
        await using var operation = await _operationLock.AcquireAsync(cancellationToken);
        await RecoverPendingAndCleanupRetiredTreesAsync(cancellationToken).ConfigureAwait(false);
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state.SeededPackageIds.Contains(expectedPackageId, StringComparer.Ordinal)) return false;
        if (state.SeededPackageIds.Count >= 64)
            throw new WidgetPackageException("initial_widget_limit", "Initial widget receipt limit exceeded.");
        // Never supersede an existing selection, including disabled widgets,
        // unhealthy packages and bytes staged intentionally through the CLI.
        if (state.Widgets.Any(item => item.Id == expectedPackageId) ||
            Directory.Exists(Path.Combine(_packagesRoot, expectedPackageId)))
        {
            await _stateStore.MutateAsync(current => current with
            {
                SeededPackageIds = current.SeededPackageIds.Append(expectedPackageId).ToArray(),
            }, cancellationToken).ConfigureAwait(false);
            return false;
        }
        var installed = await CreateInstaller().InstallAsync(packageStream, async (inspection, token) =>
        {
            if (inspection.Id != expectedPackageId || WidgetManifestTrust.Resolve(inspection.Manifest) != WidgetExecutionTrust.Sandboxed)
                throw new WidgetPackageException("invalid_initial_widget", "Initial widgets must match their declared identity and use the sandbox.");
            if (!WidgetHostCompatibility.Evaluate(inspection.Manifest).IsSupported)
                throw new WidgetPackageException("package_incompatible", "Initial widget is incompatible with this host.");
            await PreparePackageInstallUnderLockAsync(inspection, WidgetPackageTrustApproval.None, token).ConfigureAwait(false);
        }, cancellationToken, PrepareRetentionAsync).ConfigureAwait(false);
        // The package is committed. Record enablement and receipt together even
        // if shutdown was requested after publication, just like reviewed install.
        await _stateStore.MutateAsync(current => current with
        {
            Version = 2,
            SeededPackageIds = current.SeededPackageIds.Append(expectedPackageId).ToArray(),
            Widgets = current.Widgets.Append(new CatalogStateEntry
            {
                Id = installed.Id, Enabled = true, ActiveVersion = installed.Version.ToString(),
                Order = current.Widgets.Count == 0 ? 0 : current.Widgets.Max(entry => entry.Order) + 1,
            }).ToArray(),
        }, CancellationToken.None).ConfigureAwait(false);
        await FinishRetentionAsync(installed).ConfigureAwait(false);
        return true;
    }
}
