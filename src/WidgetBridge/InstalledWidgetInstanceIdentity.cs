
namespace WidgetRail.WidgetBridge;

internal static class InstalledWidgetInstanceIdentity
{
    internal static string Derive(string packageId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return WidgetRail.WidgetCatalog.InstalledWidgetAuthority.InstanceId(packageId, version);
    }
}
