namespace WidgetRail.WidgetCatalog;

/// <summary>Packages replaced by host-owned shell utilities.</summary>
public static class HostUtilityPackages
{
    public static bool IsRetired(string id) => id is "widgetrail.firstparty.task-switcher" or "widgetrail.firstparty.recent-apps";
}

public sealed partial class WidgetCatalog
{
    // Use the ordinary uninstall transaction: launch leases, consent cleanup,
    // per-widget data removal and pending-file recovery remain authoritative.
    internal async Task RetireHostUtilityPackagesAsync(Action<string> warning, CancellationToken token)
    {
        // Clean/current profiles should not pay for a second catalogue scan.
        if (!Directory.Exists(Path.Combine(_packagesRoot, "widgetrail.firstparty.task-switcher")) &&
            !Directory.Exists(Path.Combine(_packagesRoot, "widgetrail.firstparty.recent-apps"))) return;
        var snapshot = await DiscoverAsync(token).ConfigureAwait(false);
        foreach (var widget in snapshot.Widgets.Where(widget => HostUtilityPackages.IsRetired(widget.Id)))
        {
            try
            {
                if (widget.Enabled) await SetEnabledAsync(widget.Id, false, token).ConfigureAwait(false);
                await UninstallAsync(widget.Id, token).ConfigureAwait(false);
            }
            catch (KeyNotFoundException) { /* Another normal uninstall already removed it. */ }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or WidgetPackageException)
            { warning($"Retired utility package '{widget.Id}' could not be removed yet; cleanup will retry on next startup."); }
        }
    }
}
