using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

internal static class ReplayUpdateRecoveryScenarios
{
    internal static async Task CorruptReceipt()
    {
        var parent = Path.Combine(Path.GetTempPath(), "wrail-replay-update-recovery");
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        try
        {
            var consent = new ConsentStore(Path.Combine(root, "consent"));
            var metadata = new PrivateStateStore(Path.Combine(root, "consent", "replay-owner-update"));
            var receiptOwner = new BrokerWidgetIdentity("widgetrail.host.replay-update", "widgetrail.host", "host");
            var malformed = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"version\":99}"));
            await metadata.WriteAsync(receiptOwner, new(malformed, 0), default);
            var old = new BrokerWidgetIdentity("example.replay", "old.publisher", "old.instance");
            var replacement = old with { PublisherId = "new.publisher", InstanceId = "new.instance" };
            var preferences = new CaptureReplayPreferences { Revision = 5, OwnerPackageId = old.PackageId,
                OwnerPublisherId = old.PublisherId, OwnerInstanceId = old.InstanceId,
                Settings = new() { Enabled = true, AutoStart = true }, Approvals = new([new(new string('a', 64), "Fixture")]) };
            var settings = new PlatformSettingsStore(new(root));
            await settings.UpdateAsync(document => document with { ReplayCapture = preferences });
            await consent.SetDecisionAsync(replacement, PlatformCapabilities.WindowCaptureV1, ConsentDecision.Grant);
            var configured = new ConfiguredWidget { Id = "replay", PackageId = replacement.PackageId, PublisherId = replacement.PublisherId,
                InstanceId = replacement.InstanceId, Name = "Verified replacement", WorkerExecutable = "unused.exe",
                WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64),
                DeclaredCapabilities = [PlatformCapabilities.WindowCaptureV1] };
            var catalog = new BridgeCatalog([configured]);
            var warnings = new List<string>();
            await CaptureReplayPreferencesAdapter.RecoverReviewedUpdateAsync(root, consent, catalog, warnings.Add, default);
            Check(warnings.Count == 1 && catalog.IsComplete && catalog.Widgets.Count == 1,
                "Corrupt replay receipt blocked the verified catalog");
            Check((await metadata.ReadAsync(receiptOwner, default)).CanonicalJsonBase64 == malformed &&
                (await settings.LoadAsync()).ReplayCapture == preferences, "Recovery discarded metadata or invented approval");
            await using var server = new WidgetBridgeServer("replay-recovery-" + Guid.NewGuid().ToString("N"), catalog, consentStore: consent);
            using var library = new CaptureLibraryStore(Path.Combine(root, "library"), Path.Combine(root, "media"), null, null, null);
            await using var replay = new CaptureReplayService(library, CaptureReplayPreferencesAdapter.Read(preferences),
                (next, expected, token) => CaptureReplayPreferencesAdapter.SaveAsync(settings, next, expected, token),
                server.AuthorizeReplayFeatureAsync);
            var work = await replay.PollHostAsync();
            Check(work.Command is null && !work.Status.Settings.Enabled && work.Status.Issue == CaptureReplayIssue.FeatureRemoved &&
                !(await settings.LoadAsync()).ReplayCapture.Settings.Enabled, "Stale replay owner bypassed strict authority after warning");
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe fixture cleanup");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
