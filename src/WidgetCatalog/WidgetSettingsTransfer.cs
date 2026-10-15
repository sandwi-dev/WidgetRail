using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

// Trusted updater helper, not a public widget capability. Both inputs come from
// verified catalog/staging trees under the catalog update lock, after review.
internal sealed class WidgetSettingsTransfer(string stateRoot)
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
            await new PrivateStateStore(stateRoot).CopyIfAbsentAsync(
                Identity(previous), Identity(replacement), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WidgetPackageException("settings_transfer_busy",
                "Widget settings are busy. The current version is still selected. Try again shortly.", exception);
        }
        catch (Exception exception) when (exception is BrokerException or IOException or UnauthorizedAccessException)
        {
            throw new WidgetPackageException("settings_transfer_failed",
                "Widget settings could not be kept. The current version is still selected. Try the update again after resolving the local-data error.", exception);
        }
    }

    private static BrokerWidgetIdentity Identity(InstalledWidgetVersion version) =>
        new(version.Id, InstalledWidgetAuthority.PublisherId(version), "settings-transfer");
}
