namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private BridgeClientPublication<T> AdmitInputPublication<T>(ClientRegistration registration, T value, bool pinned = false)
    {
        lock (_gate)
        {
            DemandCurrentInputRegistration(registration, pinned);
            return AdmitPublicationLocked(registration, value);
        }
    }

    private void DemandCurrentInputRegistration(ClientRegistration registration, bool pinned = false)
    {
        if (IsCurrent(registration)) return;
        if (pinned) throw new BridgeStalePinnedInputAuthorityException("Pinned input registration retired.");
        throw new BridgeStaleControllerInputAuthorityException("Input registration retired.");
    }

    // Caller owns OperationGate: compare with both the cached tree's owner and
    // the running process, before any action can revalidate colliding sequences.
    private static void DemandInputWorkerRun(ClientRegistration registration, BridgeWorkerRun? expected, bool pinned = false)
    {
        if (expected is null) return; // Legacy hosts retain their existing contract.
        if (expected.RegistryGeneration <= 0 || expected.StartOrdinal <= 0 ||
            registration.CachedWorkerRun != expected || !registration.HasCurrentSnapshotWorker)
        {
            if (pinned) throw new BridgeStalePinnedInputAuthorityException("Pinned input belongs to a retired worker run.");
            throw new BridgeStaleControllerInputAuthorityException("Input belongs to a retired worker run.");
        }
    }
}
