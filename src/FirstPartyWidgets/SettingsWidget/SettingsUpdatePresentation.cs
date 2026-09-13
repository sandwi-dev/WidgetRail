using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static class SettingsUpdatePresentation
{
    internal static string UpdateSourceId(string id) => "installed.update.file." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();

    public static WidgetView Render(StackElement header, bool busy, SettingsInstalledWidgetState state)
    {
        var package = state.CatalogValid ? state.SelectedInstalled : null;
        var fullTrust = package is not null && WidgetManifestTrust.Resolve(package.ActiveVersion.Manifest) == WidgetExecutionTrust.FullTrustCurrentUser;
        return SettingsPresentation.View(header,
            SettingsPresentation.PageScope("installed.update",
                UI.Text("Update from file", "installed.update.heading", "Update widget").Classes("page-heading"),
                UI.Text(package is null ? "This widget is no longer available." : $"{package.Name} · current version {package.ActiveVersion.Version}",
                    "installed.update.name", "Selected widget").Classes("diagnostic-line"),
                UI.Text("Choose a newer .wrwidget file for this widget. The update will be selected and turned off for review. Your data and previous version stay.",
                    "installed.update.help", "Update instructions").Classes("page-help"),
                UI.Text(fullTrust
                    ? "This widget runs with full access to your Windows account. Only choose an update you trust. Choosing a file approves installing that update; it will not run until you enable it."
                    : "Only choose an update from a source you trust. Review its permissions before turning it back on.",
                    "installed.update.trust", "Update trust guidance").Classes(fullTrust ? "diagnostic-error" : "page-help"),
                UI.Button("Choose update file", "host.install-local-widget", UpdateSourceId(package?.Id ?? "unavailable"))
                    .Disabled(package is null).Busy(busy).Classes("primary-button"),
                UI.Button("Review update", "installed.update.review", "installed.update.review")
                    .Disabled(package is null).Busy(busy).Classes("setting-row"),
                UI.Button("Back", "back", "installed.update.back").Classes("secondary-button")),
            package is null ? "installed.update.back" : UpdateSourceId(package.Id), "installed.update");
    }
}
