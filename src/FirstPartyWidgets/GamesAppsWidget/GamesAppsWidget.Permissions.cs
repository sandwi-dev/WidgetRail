using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

public sealed partial class GamesAppsWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        if (action.ActionId is "games.section.previous" or "games.section.next")
        {
            var offset = action.ActionId == "games.section.previous" ? -1 : 1;
            var destination = RootPages[(Array.IndexOf(RootPages, Page) + offset + RootPages.Length) % RootPages.Length];
            return destination == GamesAppsPage.Running ? ["system.apps.running.read.v1"] : ["system.apps.library.read.v1"];
        }
        if (action.ActionId == "games.remove")
        {
            lock (_gate)
            {
                var item = _items.FirstOrDefault(item => GamesAppsPresentation.LibraryElementId(item.SavedId) == action.SourceElementId);
                return item is not null && _persistedLibraryState.RunningRegistrationSavedIds.Contains(item.SavedId, StringComparer.Ordinal)
                    ? ["system.apps.running.register.v1"] : [];
            }
        }
        return action.ActionId switch
        {
            "games.launch" => ["system.apps.library.launch.v1"],
            "games.open-running" => ["system.apps.running.read.v1"],
            "games.toggle-curation" when Page == GamesAppsPage.Running => ["system.apps.running.read.v1", "system.apps.running.register.v1"],
            "games.open-library" or "games.open-catalog" => ["system.apps.library.read.v1"],
            "games.retry" or "games.refresh-catalog" => Page == GamesAppsPage.Running ? ["system.apps.running.read.v1"] : ["system.apps.library.read.v1"],
            _ => [],
        };
    }
    private void PermissionFeedback(string text) { ShowToast("Permission", text, ToastTone.Warning); }
}
