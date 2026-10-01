using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SettingsUsabilityScenarios
{
    public static Task PagesHaveConsistentOwnershipAndFocus()
    {
        var pages = new Dictionary<SettingsPage, ViewSnapshot>();
        foreach (var page in new[] { SettingsPage.Appearance, SettingsPage.Overlay, SettingsPage.Accessibility,
            SettingsPage.Controllers, SettingsPage.ControllerHelp, SettingsPage.About, SettingsPage.Diagnostics })
        {
            var state = new SettingsPresentationState(page, PlatformSettingsDocument.Default,
                new([]), PlatformDiagnosticsSnapshot.Unavailable(), null, "Ready", true, false, false,
                Display: new("test-display", "Test display"));
            if (!SettingsPresentation.TryRender(state, out var view)) throw new Exception("Page missing: " + page);
            var snapshot = view.CreateSnapshot("settings", 1);
            var errors = ViewSnapshotValidator.Validate(snapshot);
            if (errors.Count != 0) throw new Exception(string.Join("; ", errors));
            var nodes = Walk(snapshot.Root).ToDictionary(node => node.Id);
            if (nodes.ContainsKey("settings.quit") || nodes.ContainsKey("settings.restart")) throw new Exception("App actions leaked into subpage focus.");
            if (nodes["settings.back"].ActionId != "back") throw new Exception("Subpage lacks header Back.");
            foreach (var node in nodes.Values)
                foreach (var target in new[] { node.Focus?.Up, node.Focus?.Down, node.Focus?.Left, node.Focus?.Right }.OfType<string>())
                    if (!nodes.TryGetValue(target, out var destination) || destination.Kind is not (ViewNodeKind.Button or ViewNodeKind.Select or ViewNodeKind.ActionSurface))
                        throw new Exception($"{page}: {node.Id} points to missing/noninteractive {target}");
            pages.Add(page, snapshot);
        }
        bool Has(SettingsPage page, string id) => Walk(pages[page].Root).Any(node => node.Id == id);
        if (!Has(SettingsPage.Appearance, "interface.stepper") || !Has(SettingsPage.Appearance, "overlay.focus-animation") ||
            Has(SettingsPage.Overlay, "interface.stepper") || Has(SettingsPage.Appearance, "overlay.position"))
            throw new Exception("Category ownership overlaps.");
        if (Has(SettingsPage.Accessibility, "motion.system") || Has(SettingsPage.Accessibility, "contrast.high"))
            throw new Exception("Competing switches survived.");
        if (Has(SettingsPage.Controllers, "controllers.exclusive-control") || Has(SettingsPage.Controllers, "controllers.hidhide") ||
            !Has(SettingsPage.ControllerHelp, "controllers.hidhide") || !Has(SettingsPage.ControllerHelp, "controllers.exclusive-control"))
            throw new Exception("Driver details must be available on the help page.");
        return Task.CompletedTask;
    }

    public static Task ExplicitChoicesAreIdempotent()
    {
        foreach (var action in new[] { "motion.choose.system", "motion.choose.full", "motion.choose.reduced",
            "contrast.choose.system", "contrast.choose.standard", "contrast.choose.high", "position.choose.center",
            "position.choose.left", "position.choose.right", "switcher.choose.radial", "switcher.choose.rail" })
        {
            if (!SettingsPreferencePolicy.TryCreate(action, out var mutation)) throw new Exception("Missing choice " + action);
            var once = mutation.Apply(PlatformSettingsDocument.Default);
            if (mutation.Apply(once) != once) throw new Exception("Repeated selection toggled " + action);
            if (once.Controllers != PlatformSettingsDocument.Default.Controllers) throw new Exception("Appearance changed input settings.");
        }
        return Task.CompletedTask;
    }

    public static Task SettingRowKeepsControlSemantics()
    {
        var select = UI.Select("Mode", [new("a", "Alpha", "set.alpha", IsSelected: true)], "mode") with { ShowLabel = false };
        var view = new WidgetView(UI.SettingsField("mode.row", "Mode", select, "Choose a mode."), "mode");
        var snapshot = view.CreateSnapshot("row", 1);
        var nodes = Walk(snapshot.Root).ToArray();
        if (ViewSnapshotValidator.Validate(snapshot).Count != 0 || nodes.Count(node => node.Kind == ViewNodeKind.Select) != 1)
            throw new Exception("Setting row duplicated control authority.");
        var control = nodes.Single(node => node.Id == "mode");
        if (control.AccessibilityLabel != "Mode" || control.AccessibilityValue != "Alpha" || control.Text!.StartsWith("Mode:"))
            throw new Exception("External label lost accessibility or remained duplicated.");
        if (nodes[0].Kind != ViewNodeKind.Grid || nodes[0].GridMaximumColumns != 2)
            throw new Exception("Setting row must use native responsive layout.");
        return Task.CompletedTask;
    }

    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
}
