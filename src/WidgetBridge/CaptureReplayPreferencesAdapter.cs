using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetCatalog;

namespace WidgetRail.WidgetBridge;

internal static class CaptureReplayPreferencesAdapter
{
    internal static async Task RecoverReviewedUpdateAsync(string settingsRoot, ConsentStore consent,
        BridgeCatalog catalog, Action<string> warning, CancellationToken token)
    {
        if (!catalog.IsComplete) return;
        try
        {
            await new ReviewedReplayOwnerTransfer(settingsRoot, consent).RecoverAsync(owner =>
            {
                var entry = catalog.Widgets.FirstOrDefault(item => item.InstanceId == owner.InstanceId);
                if (entry is null) return false;
                var configured = catalog.GetConfigured(entry.Id);
                return configured.PackageId == owner.PackageId && configured.PublisherId == owner.PublisherId &&
                    configured.DeclaredCapabilities.Contains(PlatformCapabilities.WindowCaptureV1);
            }, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is BrokerException or PlatformSettingsException or IOException or UnauthorizedAccessException)
        {
            // One damaged replay receipt must not suppress unrelated verified
            // widgets. Keep it untouched; normal replay authorization fails
            // closed for the stale owner after the catalog is published.
            var code = error is BrokerException broker ? broker.Code : "storage_unavailable";
            warning($"Replay update continuity could not be restored ({code}). Recording remains subject to current package permissions.");
        }
    }

    internal static CaptureReplayConfiguration Read(CaptureReplayPreferences value)
    {
        if (!value.IsWellFormed()) throw new BrokerException("invalid_replay_settings", "Replay preferences are invalid.");
        BrokerWidgetIdentity? owner = value.OwnerPackageId is null ? null :
            new(value.OwnerPackageId, value.OwnerPublisherId!, value.OwnerInstanceId!);
        owner?.Validate();
        return new(value.Revision, value.Settings.Normalize(), owner, value.ApprovedApplication, value.ApplicationName);
    }
    internal static async Task<CaptureReplayConfiguration> SaveAsync(PlatformSettingsStore store,
        CaptureReplayConfiguration next, long expectedRevision, CancellationToken token)
    {
        if (!next.IsWellFormed() || next.Revision != expectedRevision + 1)
            throw new BrokerException("invalid_replay_settings", "Replay preferences are invalid.");
        var saved = await store.UpdateAsync(current =>
        {
            if (current.ReplayCapture.Revision != expectedRevision)
                throw new BrokerException("replay_settings_conflict", "Replay settings changed.");
            return current with { ReplayCapture = new()
            {
                Revision = next.Revision, Settings = next.Settings, OwnerPackageId = next.Owner?.PackageId,
                OwnerPublisherId = next.Owner?.PublisherId, OwnerInstanceId = next.Owner?.InstanceId,
                ApprovedApplication = next.ApprovedApplication, ApplicationName = next.ApplicationName,
            } };
        }, token).ConfigureAwait(false);
        return Read(saved.ReplayCapture);
    }
}
