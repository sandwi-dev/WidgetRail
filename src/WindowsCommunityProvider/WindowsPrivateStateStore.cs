using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsCommunityProvider;

internal interface IPrivateStateStore
{
    Task<PrivateStateSnapshotSummary> ReadAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken);
    Task<PrivateStateMutationSummary> WriteAsync(
        BrokerWidgetIdentity identity, WritePrivateStateRequest request,
        CancellationToken cancellationToken);
    Task<PrivateStateMutationSummary> ClearAsync(
        BrokerWidgetIdentity identity, ClearPrivateStateRequest request,
        CancellationToken cancellationToken);
}

// Windows backend and package maintenance share the same validated, atomic store.
internal sealed class WindowsPrivateStateStore(string root, TimeProvider? timeProvider = null)
    : PrivateStateStore(root, timeProvider), IPrivateStateStore;
