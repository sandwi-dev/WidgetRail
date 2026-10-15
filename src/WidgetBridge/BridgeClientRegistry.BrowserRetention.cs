using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private const int MaximumBackgroundBrowsers = 4;
    private sealed record BrowserRetention(ClientRegistration Registration, BridgeWorkerRun Run);
    private readonly Dictionary<(string Widget, string Document), BrowserRetention> browserRetentions = [];
    internal int BackgroundBrowserCount { get { lock (_gate) { PruneBrowserRetentionsLocked(); return browserRetentions.Count; } } }

    internal async Task<bool> SetBrowserRetentionAsync(BridgeBrowserRetention request,
        CancellationToken sessionCancellation, CancellationToken token)
    {
        if (!BridgeRequestKey.IsBoundedIdentifier(request.WidgetId) || !BridgeRequestKey.IsBoundedIdentifier(request.DocumentId) ||
            request.WorkerRun is null || request.WorkerRun.RegistryGeneration <= 0 || request.WorkerRun.StartOrdinal <= 0)
            throw new BridgeProtocolException("Invalid browser retention identity.");
        ClientRegistration? registration;
        lock (_gate)
        {
            DemandNotDisposed();
            PruneBrowserRetentionsLocked();
            _clients.TryGetValue(request.WidgetId, out registration);
        }
        // A release never starts or revives a worker.
        if (registration is null) return false;
        await registration.OperationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                PruneBrowserRetentionsLocked();
                if (!IsCurrentLocked(registration) || !registration.HasCurrentSnapshotWorker || registration.CachedWorkerRun != request.WorkerRun)
                    return false;
                var key = (request.WidgetId, request.DocumentId);
                if (request.Retain)
                {
                    if (!DeclaresBackgroundBrowser(registration.CachedSnapshot, request.DocumentId) ||
                        !browserRetentions.ContainsKey(key) && browserRetentions.Count >= MaximumBackgroundBrowsers) return false;
                    browserRetentions[key] = new(registration, request.WorkerRun);
                }
                else if (browserRetentions.TryGetValue(key, out var prior) && prior.Run == request.WorkerRun)
                    browserRetentions.Remove(key);
            }
            if (request.Retain) registration.CancelIdleUnload();
            else ScheduleIdleUnload(registration, sessionCancellation);
            return request.Retain;
        }
        finally { registration.OperationGate.Release(); }
    }

    private bool HasBrowserRetention(ClientRegistration registration)
    {
        lock (_gate)
        {
            PruneBrowserRetentionsLocked();
            if (WidgetRail.PlatformBroker.WebSessionRegistry.Shared.HasBackgroundSession(registration.Configured.InstanceId)) return true;
            return browserRetentions.Values.Any(retention => ReferenceEquals(retention.Registration, registration));
        }
    }

    private void PruneBrowserRetentionsLocked()
    {
        foreach (var pair in browserRetentions.ToArray())
            if (!IsCurrentLocked(pair.Value.Registration) || !pair.Value.Registration.HasCurrentSnapshotWorker ||
                pair.Value.Registration.CachedWorkerRun != pair.Value.Run ||
                !DeclaresBackgroundBrowser(pair.Value.Registration.CachedSnapshot, pair.Key.Document)) browserRetentions.Remove(pair.Key);
    }

    private static bool DeclaresBackgroundBrowser(ViewSnapshot? snapshot, string id) => snapshot is not null &&
        (FindBackgroundBrowser(snapshot.Root, id) || snapshot.PinnedLayouts.Any(layout => layout.Root is { } root && FindBackgroundBrowser(root, id)));

    // All input authority remains Background; residency alone never authorizes actions.
    private static bool FindBackgroundBrowser(ViewNode node, string id) =>
        node.WebBrowser is { } browser && browser.Id == id && browser.Options.Background == BrowserBackgroundMode.KeepAlive &&
        browser.ProviderDocument is null && browser.IsWellFormed() || node.Children.Any(child => FindBackgroundBrowser(child, id));
}
