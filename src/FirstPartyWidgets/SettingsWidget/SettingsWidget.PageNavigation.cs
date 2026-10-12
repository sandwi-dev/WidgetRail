using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.Settings;

public sealed partial class SettingsWidget
{
    private SettingsPage? displayedPage;
    private WidgetNavigationOperation displayedNavigation = WidgetNavigationOperation.Replace;

    private WidgetNavigationOperation ObservePageNavigation(SettingsPage page)
    {
        lock (_stateLock)
        {
            if (displayedPage == page) return displayedNavigation;
            displayedNavigation = WidgetNavigationOperation.Forward;
            if (displayedPage is { } previous)
            {
                var visited = new HashSet<SettingsPage>();
                while (previous != SettingsPage.Root && visited.Add(previous))
                {
                    previous = SettingsNavigationPolicy.Parent(previous, _permissionState.PackageCapabilitiesReturnPage);
                    if (previous == page) { displayedNavigation = WidgetNavigationOperation.Back; break; }
                }
            }
            else displayedNavigation = WidgetNavigationOperation.Replace;
            displayedPage = page;
            return displayedNavigation;
        }
    }
}
