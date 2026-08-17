using WidgetRail.LauncherExperienceCatalog;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsLauncherExperienceSelection(string Id, string Version);

internal sealed record SettingsLauncherExperiencePresentationState(
    SettingsPage Page,
    LauncherExperienceSelectionSettings Selection,
    LauncherExperienceCatalogSnapshot Catalog,
    SettingsLauncherExperienceSelection? Reviewed,
    string? ListFocusId,
    bool Busy);

internal static class SettingsLauncherExperiencePresentation
{
    public static WidgetView Render(
        SettingsLauncherExperiencePresentationState state,
        StackElement header) => state.Page switch
        {
            SettingsPage.LauncherExperiences => RenderList(state, header),
            SettingsPage.LauncherExperienceVersion => RenderVersion(state, header, false),
            SettingsPage.LauncherExperienceRemoval => RenderVersion(state, header, true),
            _ => throw new InvalidOperationException("Unsupported Launcher Experience settings page."),
        };

    private static WidgetView RenderList(
        SettingsLauncherExperiencePresentationState state,
        StackElement header)
    {
        var selection = state.Selection;
        var children = new List<WidgetElement>
        {
            UI.Text("Launcher Experiences", "launcher-experience.heading",
                    "Launcher Experience version management")
                .Classes("page-heading"),
            UI.Text(
                    "Choose one exact installed ID and version, inherit global appearance, or restore the built-in Hero Rail in one action.",
                    "launcher-experience.help", "Launcher Experience management help")
                .Classes("page-help"),
            UI.Button(
                    selection.UseGlobalAppearance ? "Using global appearance" : "Use global appearance",
                    "launcher-experience.global", "launcher-experience.global")
                .Disabled(selection.UseGlobalAppearance).Busy(state.Busy),
            UI.Button("Restore built-in Hero Rail", "launcher-experience.recover",
                    "launcher-experience.recover")
                .Busy(state.Busy).Classes("secondary-button"),
        };
        string? lastId = null;
        for (var index = 0; index < state.Catalog.Experiences.Count; index++)
        {
            var entry = state.Catalog.Experiences[index];
            if (!string.Equals(lastId, entry.Descriptor.Id, StringComparison.Ordinal))
            {
                children.Add(UI.Text(entry.Descriptor.Id, $"launcher-experience.group.{index}",
                    $"Launcher Experience {entry.Descriptor.Id}").Classes("section-heading"));
                lastId = entry.Descriptor.Id;
            }
            var selected = IsSelected(selection, entry);
            var status = selected
                ? selection.UseGlobalAppearance ? "Saved selection" : "Active selection"
                : entry.IsValid ? "Installed" : "Invalid";
            children.Add(UI.SettingsRow(
                entry.Descriptor.Name,
                new ComponentAction("Review", $"launcher-experience.open.{index}"),
                $"launcher-experience.item.{index}",
                description: entry.Descriptor.IsBuiltIn
                    ? "Built-in platform recovery"
                    : "Unsigned local package",
                value: entry.Descriptor.Version.ToString(),
                status: status,
                statusTone: entry.IsValid ? StatusTone.Neutral : StatusTone.Danger,
                isBusy: state.Busy));
        }
        children.Add(UI.Button("Back", "back", "launcher-experience.back")
            .Classes("secondary-button"));
        SettingsPresentation.LinkVertical(children);
        return View(header, children,
            state.ListFocusId ?? "launcher-experience.global");
    }

    private static WidgetView RenderVersion(
        SettingsLauncherExperiencePresentationState state,
        StackElement header,
        bool confirmation)
    {
        var entry = FindReviewed(state);
        if (entry is null)
            return View(header,
                [
                    UI.Text("Launcher Experience changed", "launcher-experience.version.missing",
                        "Launcher Experience version changed"),
                    UI.Button("Back", "back", "launcher-experience.version.back"),
                ], "launcher-experience.version.back");
        var selected = IsSelected(state.Selection, entry);
        var removable = entry.IsValid && !entry.Descriptor.IsBuiltIn && !selected;
        if (confirmation)
        {
            var confirmationChildren = new List<WidgetElement>
            {
                UI.Text("Remove Launcher Experience version?", "launcher-experience.removal.heading",
                        "Remove Launcher Experience version confirmation")
                    .Classes("page-heading"),
                UI.Text($"{entry.Descriptor.Name} · {entry.Descriptor.Id} · {entry.Descriptor.Version}",
                        "launcher-experience.removal.identity", "Exact Launcher Experience version to remove")
                    .Classes("page-help"),
                UI.Button("Remove", "launcher-experience.remove.confirm",
                        "launcher-experience.removal.confirm")
                    .Disabled(!removable).Busy(state.Busy).Classes("danger-button"),
                UI.Button("Cancel", "launcher-experience.remove.cancel",
                        "launcher-experience.removal.cancel")
                    .Classes("secondary-button"),
            };
            SettingsPresentation.LinkVertical(confirmationChildren);
            return View(header, confirmationChildren, "launcher-experience.removal.cancel");
        }

        var package = entry.Package;
        var assetCount = package?.Files.Count(path =>
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) ?? 0;
        var children = new List<WidgetElement>
        {
            UI.Text(entry.Descriptor.Name, "launcher-experience.version.heading",
                    entry.Descriptor.Name).Classes("page-heading"),
            UI.CodeText($"ID: {entry.Descriptor.Id}", "launcher-experience.version.id",
                $"Launcher Experience ID {entry.Descriptor.Id}"),
            UI.CodeText($"Version: {entry.Descriptor.Version}", "launcher-experience.version.version",
                $"Launcher Experience version {entry.Descriptor.Version}"),
            UI.Text($"Publisher claim: {entry.Descriptor.Publisher}",
                "launcher-experience.version.publisher", "Launcher Experience publisher claim"),
            UI.CodeText($"Content digest: {entry.Descriptor.ContentDigest}",
                "launcher-experience.version.digest", "Launcher Experience content digest"),
            UI.Text($"Layout preset: {entry.Descriptor.LayoutPreset}",
                "launcher-experience.version.layout", "Launcher Experience layout preset"),
            UI.Text(package is null
                    ? "Package contents unavailable"
                    : $"Package files: {package.Files.Count}; image assets: {assetCount}",
                "launcher-experience.version.contents", "Launcher Experience package contents"),
            UI.Text(entry.IsValid ? "Compatibility: valid for this host" :
                    $"Compatibility: invalid ({entry.Diagnostics.FirstOrDefault()?.Code ?? "validation_error"})",
                "launcher-experience.version.compatibility", "Launcher Experience compatibility"),
            UI.Text(entry.Descriptor.IsBuiltIn
                    ? "Trust: built-in platform recovery"
                    : "Trust: unsigned local package; publisher is a package claim, not a verified signature",
                "launcher-experience.version.trust", "Launcher Experience trust disclosure")
                .Classes(entry.Descriptor.IsBuiltIn ? "diagnostic-ok" : "page-help"),
            UI.Button("Select this exact version", "launcher-experience.select",
                    "launcher-experience.version.select")
                .Disabled(!entry.IsValid || selected).Busy(state.Busy),
            UI.Button("Remove this exact version", "launcher-experience.remove.request",
                    "launcher-experience.version.remove")
                .Disabled(!removable).Busy(state.Busy).Classes("danger-button"),
            UI.Button("Back", "back", "launcher-experience.version.back")
                .Classes("secondary-button"),
        };
        SettingsPresentation.LinkVertical(children);
        return View(header, children,
            entry.IsValid && !selected ? "launcher-experience.version.select"
                : removable ? "launcher-experience.version.remove"
                : "launcher-experience.version.back");
    }

    private static LauncherExperienceCatalogEntry? FindReviewed(
        SettingsLauncherExperiencePresentationState state) => state.Reviewed is null
        ? null
        : state.Catalog.Experiences.FirstOrDefault(entry =>
            string.Equals(entry.Descriptor.Id, state.Reviewed.Id, StringComparison.Ordinal) &&
            string.Equals(entry.Descriptor.Version.ToString(), state.Reviewed.Version,
                StringComparison.Ordinal));

    private static bool IsSelected(
        LauncherExperienceSelectionSettings selection,
        LauncherExperienceCatalogEntry entry) =>
        string.Equals(selection.SelectedId, entry.Descriptor.Id, StringComparison.Ordinal) &&
        string.Equals(selection.SelectedVersion, entry.Descriptor.Version.ToString(),
            StringComparison.Ordinal);

    private static WidgetView View(
        StackElement header,
        IEnumerable<WidgetElement> children,
        string? initialFocus)
    {
        var page = UI.VerticalScroll("launcher-experience.page", children.ToArray())
            .InputScope("launcher-experience.page")
            .Shortcut(ControllerButton.B, "back");
        return new WidgetView(
            UI.Stack("settings-root", header, page),
            initialFocus,
            ActiveInputScopeId: "launcher-experience.page",
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                PreferredWidth = 880,
                PreferredHeight = 520,
                MinimumWidth = 520,
                MinimumHeight = 360,
            });
    }
}
