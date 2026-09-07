using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsAppLibraryProvider;

internal static class WindowsPortableAppRegistrationPaths
{
    internal const int MaximumExecutablePathCharacters = 32_762;

    internal static string ForCatalogRoot(string catalogRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRoot);
        return Path.Combine(
            Path.GetFullPath(catalogRoot), "broker", "portable-apps");
    }
}

internal sealed record WindowsExecutableFileIdentity(
    ulong VolumeSerialNumber,
    ulong FileIdLow,
    ulong FileIdHigh);

internal sealed record PortableAppRegistration(
    string SavedId,
    string StableIdentity,
    string DisplayName,
    string ExecutablePath,
    WindowsExecutableFileIdentity FileIdentity);

internal sealed record PortableAppRegistrationSnapshot(
    long Revision,
    IReadOnlyList<PortableAppRegistration> Items);

internal sealed record PortableAppRegistrationMutation(
    PortableAppRegistrationSnapshot Snapshot,
    bool Changed);

internal sealed record PortableAppPackageRetirement(
    bool Committed,
    bool CleanupPending);

internal interface IWindowsPortableAppStore
{
    Task<PortableAppRegistrationSnapshot> ReadAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken);
    Task<PortableAppRegistrationMutation> UpsertAsync(
        BrokerWidgetIdentity identity, PortableAppRegistration registration,
        CancellationToken cancellationToken);
    Task<PortableAppRegistrationMutation> RemoveAsync(
        BrokerWidgetIdentity identity, string savedId,
        CancellationToken cancellationToken);
    Task ClearAsync(
        BrokerWidgetIdentity identity, long expectedRevision,
        CancellationToken cancellationToken);
    Task<PortableAppPackageRetirement> RetirePackageAsync(
        string packageId, CancellationToken cancellationToken);
}
