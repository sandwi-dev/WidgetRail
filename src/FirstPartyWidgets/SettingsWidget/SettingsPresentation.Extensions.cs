using WidgetRail.PlatformSettings.BrowserExtensions;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static partial class SettingsPresentation
{
    private static WidgetView RenderExtensions(StackElement header, SettingsPresentationState state)
    {
        var data = state.Extensions ?? new(ExtensionCatalog.Empty);
        var profiles = data.Catalog.Profiles.Where(p => !p.Private).OrderBy(p => p.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var selected = data.Catalog.Library.FirstOrDefault(e => e.Slot == data.Selected);
        var detail = state.Page == SettingsPage.ExtensionDetails && selected is not null;
        var scope = detail ? "extensions.details" : "extensions.library";
        var rows = new List<WidgetElement> { UI.Text(detail ? selected!.Manifest.Name : "Extensions", "extensions.heading").Classes("page-heading") };
        if (data.Busy) rows.Add(UI.LoadingIndicator("extensions.progress", "Applying extension change"));
        if (data.Error is { } error) rows.Add(UI.Text(error, "extensions.error").Classes("page-help"));
        if (!detail)
        {
            rows.Add(UI.Text("Install once. New extensions start disabled; choose where each one runs.", "extensions.help").Classes("page-help"));
            if (profiles.Length > 0)
                rows.Add(Field("Show settings for", (UI.Select("Show settings for", [new("all", "All widgets", "extensions.profile.all", IsSelected: data.Profile.Length == 0),
                    .. profiles.Select(p => new SelectOption(p.Name, p.Label, "extensions.profile." + p.Name, IsSelected: data.Profile == p.Name))], "extensions.profile", "Widget profile").Busy(data.Busy).Classes("setting-row") with { ShowLabel = false })));
            var target = data.Profile.Length == 0 ? "all" : data.Profile;
            rows.Add(UI.ResponsiveGrid("extensions.install-actions", 180, 2,
                UI.Button("Install from ZIP…", "host.extensions.install.zip." + target, "extensions.install-zip").Busy(data.Busy).Classes("secondary-button"),
                UI.Button("Install from folder…", "host.extensions.install.folder." + target, "extensions.install-folder").Busy(data.Busy).Classes("secondary-button")));
            if (data.Catalog.Library.Length == 0) rows.Add(UI.Text("No extensions installed. Choose an MV3 Edge/Chromium extension package to get started.", "extensions.empty").Classes("page-help"));
            foreach (var entry in data.Catalog.Library.OrderBy(e => e.Manifest.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var summary = data.Profile.Length == 0 ? (entry.EnabledByDefault ? "Enabled by default" : "Disabled by default") :
                    entry.Overrides.GetValueOrDefault(data.Profile) == ExtensionOverride.Default ? "Default · " + (entry.EnabledFor(data.Profile) ? "On" : "Off") : entry.EnabledFor(data.Profile) ? "On" : "Off";
                var id = "extensions.item." + entry.Slot;
                rows.Add(UI.ActionSurface("extensions.open." + entry.Slot, id, entry.Manifest.Name + ". " + summary + ". Open details.",
                    ActionSurfaceOrientation.Horizontal,
                    UI.Stack(id + ".copy",
                        UI.Text(entry.Manifest.Name, id + ".name").Classes("installed-entry-name"),
                        UI.Text(summary, id + ".status").Classes("installed-entry-status")).Classes("installed-entry-copy"),
                    UI.Text("›", id + ".chevron").Classes("installed-entry-chevron")).Busy(data.Busy).Classes("installed-entry"));
            }
        }
        else
        {
            var entry = selected!;
            rows.Add(UI.Text("Version " + entry.Manifest.Version, "extensions.version").Classes("page-help"));
            rows.Add(Field("Enabled by default", UI.Switch("Enabled by default", entry.EnabledByDefault,
                "host.extensions.default." + entry.Slot + "." + (entry.EnabledByDefault ? "off" : "on"), "extensions.default", isDisabled: false, showLabel: false).Busy(data.Busy),
                "Applies to existing and new profiles using Default. Explicit On or Off choices are kept. Each enabled profile runs the extension separately and can add memory and CPU usage. Enable it only where needed."));
            rows.Add(UI.Text("Widget profiles", "extensions.profiles-heading").Classes("section-heading"));
            if (profiles.Length == 0) rows.Add(UI.Text("Open Browser or a webapp once to add its profile here. New profiles inherit this extension's default.", "extensions.no-profiles").Classes("page-help"));
            foreach (var profile in profiles.OrderByDescending(p => p.Name == data.Profile))
            {
                var choice = entry.Overrides.GetValueOrDefault(profile.Name);
                var prefix = "host.extensions.override." + entry.Slot + "." + profile.Name + ".";
                var controls = new List<WidgetElement> { (UI.Select(profile.Label, [
                    new("default", "Use default · " + (entry.EnabledByDefault ? "On" : "Off"), prefix + "default", IsSelected: choice == ExtensionOverride.Default),
                    new("on", "On", prefix + "on", IsSelected: choice == ExtensionOverride.On),
                    new("off", "Off", prefix + "off", IsSelected: choice == ExtensionOverride.Off)],
                    "extensions.override." + profile.Name, profile.Label) with { ShowLabel = false }).Busy(data.Busy).Classes("setting-control") };
                if (entry.Bindings.GetValueOrDefault(profile.Name)?.Error is { } failure)
                    controls.Add(UI.Button(failure, prefix + (choice == ExtensionOverride.Default ? "default" : choice == ExtensionOverride.On ? "on" : "off"), "extensions.retry." + profile.Name).Busy(data.Busy).Classes("secondary-button"));
                rows.Add(UI.SettingsField("extensions.profile-row." + profile.Name, profile.Label,
                    UI.Stack("extensions.profile-controls." + profile.Name, controls.ToArray()).Classes("extension-profile-controls")));
            }
            if (entry.Manifest.OptionsPage is not null)
                rows.Add(UI.Text("To configure this extension, open the widget's tray menu and choose Preferences → Extensions → Extension options.", "extensions.options-help").Classes("page-help"));
            rows.Add(UI.Text("Shared installation", "extensions.package-heading").Classes("section-heading"));
            rows.Add(UI.ResponsiveGrid("extensions.package-actions", 180, 3,
                UI.Button("Update from ZIP…", "host.extensions.update.zip." + entry.Slot, "extensions.update-zip").Busy(data.Busy).Classes("secondary-button"),
                UI.Button("Update from folder…", "host.extensions.update.folder." + entry.Slot, "extensions.update-folder").Busy(data.Busy).Classes("secondary-button"),
                UI.Button("Remove…", "host.extensions.remove." + entry.Slot, "extensions.remove").Busy(data.Busy).Classes("secondary-button", "danger-button")));
        }
        rows.Add(UI.Row("extensions.refresh-actions", UI.Button("Refresh", "extensions.refresh", "extensions.refresh").Classes("secondary-button")));
        return View(header, PageScope(scope, rows.ToArray()), detail ? "extensions.default" : "extensions.install-zip", scope);
    }
}
