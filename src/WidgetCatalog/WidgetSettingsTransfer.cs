using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

// Trusted updater helper, not a public widget capability. Both inputs come from
// verified catalog/staging trees under the catalog update lock, after review.
internal sealed class WidgetSettingsTransfer(string stateRoot, ConsentStore consent)
{
    internal async Task CopyAsync(InstalledWidgetVersion previous, InstalledWidgetVersion replacement,
        CancellationToken cancellationToken)
    {
        if (previous.Id != replacement.Id || previous.Manifest.Publisher != replacement.Manifest.Publisher ||
            WidgetManifestTrust.Resolve(previous.Manifest) != WidgetManifestTrust.Resolve(replacement.Manifest))
            throw new WidgetPackageException("settings_identity_changed", "The update no longer matches this widget.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var state = new PrivateStateStore(stateRoot);
            var destination = await state.ReadAsync(Identity(replacement), deadline.Token).ConfigureAwait(false);
            await new AppLibraryContinuityStore(consent.RootDirectory).KeepAsync(
                Identity(previous), Identity(replacement), deadline.Token, destinationHasState: destination.Revision != 0).ConfigureAwait(false);
            await state.CopyIfAbsentAsync(Identity(previous), Identity(replacement), deadline.Token).ConfigureAwait(false);
            var retained = previous.Manifest.Permissions.Concat(previous.Manifest.OptionalPermissions)
                .Intersect(replacement.Manifest.Permissions.Concat(replacement.Manifest.OptionalPermissions), StringComparer.Ordinal)
                .Where(PlatformCapabilities.IsManifestDeclarable).ToArray();
            await consent.CopyUpdateDecisionsAsync(Identity(previous), Identity(replacement),
                retained, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WidgetPackageException("settings_transfer_busy",
                "Widget update data is busy. The current version is still selected. Try again shortly.", exception);
        }
        catch (Exception exception) when (exception is BrokerException or IOException or UnauthorizedAccessException)
        {
            throw new WidgetPackageException("settings_transfer_failed",
                "Widget settings or permissions could not be kept. The current version is still selected. Resolve the local-data error and retry.", exception);
        }
    }

    private static BrokerWidgetIdentity Identity(InstalledWidgetVersion version) =>
        new(version.Id, InstalledWidgetAuthority.PublisherId(version), "settings-transfer");
}
