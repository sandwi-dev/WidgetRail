using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsVersionRemoval(string WidgetId, string Name, Version Version, string Digest);

internal static class SettingsVersionRemovalPresentation
{
    public static WidgetView Render(StackElement header, bool busy, SettingsInstalledWidgetState state)
    {
        var selected = state.VersionRemoval;
        var content = SettingsPresentation.PageScope("installed.version-removal",
            UI.Text("Remove this version?", "installed.version-removal.heading", "Remove widget version").Classes("page-heading"),
            UI.Text(selected is null ? "This version is no longer available. Return to the version list." : $"{selected.Name} · {selected.Version}",
                "installed.version-removal.name", "Selected version").Classes("diagnostic-line"),
            UI.Text("Your current version and saved data will stay. You will need its package file to install this version again.",
                "installed.version-removal.help", "Removal details").Classes("page-help"),
            UI.Button("Remove version", "installed.version-removal.confirm", "installed.version-removal.confirm")
                .Disabled(selected is null).Busy(busy).Classes("danger-button"),
            UI.Button("Cancel", "installed.version-removal.cancel", "installed.version-removal.cancel").Classes("secondary-button"));
        return SettingsPresentation.View(header, content, "installed.version-removal.cancel", "installed.version-removal");
    }
}
