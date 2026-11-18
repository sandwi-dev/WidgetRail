using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;

namespace WidgetRail.WidgetCatalog;

// Host-only reviewed-update receipt, never widget private state. Preparation is
// not activation: the exact replacement must be selected before owner migration.
internal sealed class ReviewedReplayOwnerTransfer(string settingsRoot, ConsentStore consent)
{
    private static readonly BrokerWidgetIdentity ReceiptOwner = new("widgetrail.host.replay-update", "widgetrail.host", "host");
    private readonly PrivateStateStore receipts = new(Path.Combine(consent.RootDirectory, "replay-owner-update"));
    private readonly PlatformSettingsStore settings = new(new(settingsRoot));
    private sealed record Receipt([property: JsonRequired] int Version,
        [property: JsonRequired] long ReplayRevision,
        [property: JsonRequired] BrokerWidgetIdentity Source,
        [property: JsonRequired] BrokerWidgetIdentity Destination);

    internal async Task PrepareAsync(InstalledWidgetVersion previous, InstalledWidgetVersion replacement,
        IReadOnlyCollection<string> retainedCapabilities, CancellationToken token)
    {
        if (!retainedCapabilities.Contains(PlatformCapabilities.WindowCaptureV1)) return;
        var source = InstalledWidgetAuthority.Identity(previous);
        var destination = InstalledWidgetAuthority.Identity(replacement);
        var current = (await settings.LoadAsync(token).ConfigureAwait(false)).ReplayCapture;
        if (!Owns(current, source) || source == destination) return;
        if (current.Revision >= CaptureConfirmationSettings.MaximumResetVersion)
            throw new BrokerException("invalid_replay_transfer", "Replay preference revision cannot advance.");
        if (await consent.GetDecisionAsync(source, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) != ConsentDecision.Grant ||
            await consent.GetDecisionAsync(destination, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) != ConsentDecision.Grant) return;
        var old = await receipts.ReadAsync(ReceiptOwner, token).ConfigureAwait(false);
        var next = new Receipt(1, current.Revision, source, destination);
        if (old.Exists && Read(old) == next) return;
        var json = PrivateStateJsonCodec.CanonicalBase64(JsonSerializer.SerializeToUtf8Bytes(next, BrokerJson.StrictOptions), "invalid_replay_transfer");
        await receipts.WriteAsync(ReceiptOwner, new(json, old.Revision), token).ConfigureAwait(false);
    }

    internal Task CompleteAsync(InstalledWidgetVersion selected, CancellationToken token) =>
        RecoverAsync(owner => owner == InstalledWidgetAuthority.Identity(selected) &&
            selected.Manifest.Permissions.Concat(selected.Manifest.OptionalPermissions).Contains(PlatformCapabilities.WindowCaptureV1), token);

    // The predicate comes from a verified, completed catalog snapshot (or the
    // trusted catalog's post-selection callback), never a manifest label alone.
    internal async Task RecoverAsync(Func<BrokerWidgetIdentity, bool> selectedWithCapturePermission, CancellationToken token)
    {
        var snapshot = await receipts.ReadAsync(ReceiptOwner, token).ConfigureAwait(false);
        if (!snapshot.Exists) return;
        var receipt = Read(snapshot);
        var current = (await settings.LoadAsync(token).ConfigureAwait(false)).ReplayCapture;
        if (current.Revision == receipt.ReplayRevision && Owns(current, receipt.Source))
        {
            if (!selectedWithCapturePermission(receipt.Destination)) return;
            if (await consent.GetDecisionAsync(receipt.Source, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) != ConsentDecision.Deny &&
                await consent.GetDecisionAsync(receipt.Destination, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) == ConsentDecision.Grant)
                await settings.UpdateAsync(document =>
                {
                    var replay = document.ReplayCapture;
                    if (replay.Revision != receipt.ReplayRevision || !Owns(replay, receipt.Source)) return document;
                    return document with { ReplayCapture = replay with
                    {
                        Revision = checked(replay.Revision + 1), OwnerPackageId = receipt.Destination.PackageId,
                        OwnerPublisherId = receipt.Destination.PublisherId, OwnerInstanceId = receipt.Destination.InstanceId,
                    } };
                }, token).ConfigureAwait(false);
        }
        // A changed revision includes explicit Stop/Forget/uninstall. Never
        // replay an older enabled setting or resurrect approval after it.
        await receipts.ClearAsync(ReceiptOwner, new(snapshot.Revision), token).ConfigureAwait(false);
    }

    private static bool Owns(CaptureReplayPreferences current, BrokerWidgetIdentity owner) =>
        current.OwnerPackageId == owner.PackageId && current.OwnerPublisherId == owner.PublisherId && current.OwnerInstanceId == owner.InstanceId;
    private static Receipt Read(PrivateStateSnapshotSummary snapshot)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(Convert.FromBase64String(snapshot.CanonicalJsonBase64!), BrokerJson.StrictOptions);
            if (receipt is null || receipt.Version != 1 || receipt.ReplayRevision is < 0 or >= CaptureConfirmationSettings.MaximumResetVersion ||
                receipt.Source is null || receipt.Destination is null || receipt.Source.PackageId != receipt.Destination.PackageId)
                throw new JsonException("Invalid replay owner transfer.");
            receipt.Source.Validate(); receipt.Destination.Validate();
            return receipt;
        }
        catch (Exception error) when (error is JsonException or FormatException)
        { throw new BrokerException("invalid_replay_transfer", "Reviewed replay owner transfer is invalid.", error); }
    }
}
