using System.Globalization;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

namespace GameBarAlternative.FirstPartyWidgets.Settings;

internal sealed record SettingsThemeSelection(string ThemeId, string Version);

internal sealed record SettingsPresentationState(
    SettingsPage Page,
    PlatformSettingsDocument Settings,
    ThemeCatalogSnapshot Themes,
    PlatformDiagnosticsSnapshot Diagnostics,
    string? SelectedAuthorityRecoveryId,
    string Status,
    bool SettingsValid,
    bool Busy,
    bool Error,
    SettingsThemeSelection? SelectedTheme = null,
    string? ThemePickerFocusId = null);

/// <summary>Pure snapshot-only composition for Settings pages owned by DLV-036.</summary>
internal static class SettingsPresentation
{
    public static bool TryRender(SettingsPresentationState state, out WidgetView view)
    {
        ArgumentNullException.ThrowIfNull(state);
        var header = Header(state);
        view = state.Page switch
        {
            SettingsPage.Root => RenderRoot(header, state.Settings, state.Busy),
            SettingsPage.Appearance => RenderAppearance(
                header, state.Settings, state.Themes, state.Busy),
            SettingsPage.ThemePicker => RenderThemes(
                header, state.Settings, state.Themes, state.ThemePickerFocusId, state.Busy),
            SettingsPage.ThemeVersion => RenderThemeVersion(header, state, confirmation: false),
            SettingsPage.ThemeRemoval => RenderThemeVersion(header, state, confirmation: true),
            SettingsPage.Accessibility => RenderAccessibility(
                header, state.Settings, state.Busy),
            SettingsPage.AccessibilityVisual => RenderVisualAccessibility(
                header, state.Settings, state.Busy),
            SettingsPage.Overlay => RenderOverlay(header, state.Settings, state.Busy),
            SettingsPage.Diagnostics => RenderDiagnostics(header, state),
            SettingsPage.AuthorityRecovery => RenderAuthorityRecovery(header, state),
            SettingsPage.Reset => RenderReset(header, state.Busy),
            _ => null!,
        };
        return view is not null;
    }

    public static StackElement Header(SettingsPresentationState state) =>
        UI.Stack("settings.header",
            UI.Text("SETTINGS", "settings.title", "Settings").Classes("settings-title"),
            UI.Text(state.Status, "settings.status", state.Status).Classes(
                "settings-status",
                state.Error ? "is-error" : state.Busy ? "is-busy" : "is-ready"))
        .Classes("settings-header");

    public static ScrollElement PageScope(string id, params WidgetElement[] children) =>
        UI.VerticalScroll(id, children)
            .InputScope(id)
            .Shortcut(ControllerButton.B, "back")
            .Classes("settings-page");

    public static WidgetView View(
        StackElement header,
        WidgetElement content,
        string? initialFocus,
        string activeScope) => new(
            UI.Stack("settings-root", header, content).Classes("settings-widget"),
            initialFocus,
            ActiveInputScopeId: activeScope,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                PreferredWidth = 880,
                PreferredHeight = 520,
                MinimumWidth = 520,
                MinimumHeight = 360,
            });

    private static WidgetView RenderRoot(
        StackElement header,
        PlatformSettingsDocument settings,
        bool busy)
    {
        var appearance = UI.Button("Appearance", "open.appearance", "category.appearance")
            .Busy(busy).Classes("category-card");
        var accessibility = UI.Button(
                "Accessibility", "open.accessibility", "category.accessibility")
            .Busy(busy).Classes("category-card");
        var overlay = UI.Button("Overlay", "open.overlay", "category.overlay")
            .Busy(busy).Classes("category-card");
        var installedWidgets = UI.Button(
                "Installed widgets", "open.installed-widgets", "category.installed-widgets")
            .Busy(busy).Classes("category-card");
        var diagnostics = UI.Button("Diagnostics", "open.diagnostics", "category.diagnostics")
            .Classes("category-card");
        var refresh = UI.Button("Refresh", "refresh", "settings.refresh")
            .Busy(busy).Classes("category-card");
        var reset = UI.Button("Reset", "open.reset", "category.reset")
            .Classes("category-card", "danger-card");
        return View(
            header,
            UI.VerticalScroll("settings.categories",
                UI.Text($"Theme: {settings.Appearance.ThemeId} {settings.Appearance.ThemeVersion}",
                    "settings.summary", "Selected theme").Classes("settings-summary"),
                UI.ResponsiveGrid("settings.category-grid", 250, 2,
                        appearance, accessibility, overlay, installedWidgets,
                        diagnostics, refresh, reset)
                    .Classes("category-grid")).Classes("category-list"),
            "category.appearance",
            "settings-root");
    }

    private static WidgetView RenderAppearance(
        StackElement header,
        PlatformSettingsDocument settings,
        ThemeCatalogSnapshot themes,
        bool busy)
    {
        var selected = themes.Themes.FirstOrDefault(entry =>
            entry.IsValid && entry.Descriptor.Id == settings.Appearance.ThemeId &&
            entry.Descriptor.Version.ToString() == settings.Appearance.ThemeVersion);
        var label = selected is null
            ? $"Theme: unavailable ({settings.Appearance.ThemeId})"
            : $"Theme: {selected.Descriptor.Name}";
        return View(header,
            PageScope("appearance.page",
                UI.Text("Appearance", "appearance.heading", "Appearance settings")
                    .Classes("page-heading"),
                UI.Button(label, "open.themes", "appearance.theme")
                    .Busy(busy).Classes("setting-row"),
                UI.Text(
                    "Choose a versioned theme package. Invalid packages remain visible but cannot be selected.",
                    "appearance.help", "Theme picker help").Classes("page-help")),
            "appearance.theme",
            "appearance.page");
    }

    private static WidgetView RenderAccessibility(
        StackElement header,
        PlatformSettingsDocument settings,
        bool busy)
    {
        var appearance = settings.Appearance;
        var text = LinkStepper(
            UI.Stepper("Text size", Percent(appearance.TextScale),
                "text.decrease", "text.increase", "text.stepper",
                appearance.TextScale > AppearanceSettings.MinimumTextScale,
                appearance.TextScale < AppearanceSettings.MaximumTextScale),
            up: null,
            down: "motion.system",
            busy);
        var system = UI.ToggleButton(
                "Follow Windows motion", appearance.Motion == MotionPreference.System,
                "motion.system", "motion.system")
            .FocusUp("text.stepper.decrement").FocusDown("motion.reduced").Busy(busy);
        var reduced = UI.ToggleButton(
                "Reduced motion", appearance.Motion == MotionPreference.Reduced,
                "motion.reduced", "motion.reduced")
            .FocusUp("motion.system").FocusDown("accessibility.visual").Busy(busy);
        var visual = UI.Button(
                "Contrast and visibility", "open.visual-accessibility", "accessibility.visual")
            .FocusUp("motion.reduced").Busy(busy).Classes("setting-row");
        return View(header,
            PageScope("accessibility.page",
                UI.Text("Accessibility", "accessibility.heading", "Accessibility settings")
                    .Classes("page-heading"),
                text, system, reduced, visual),
            "text.stepper.decrement",
            "accessibility.page");
    }

    private static WidgetView RenderVisualAccessibility(
        StackElement header,
        PlatformSettingsDocument settings,
        bool busy)
    {
        var appearance = settings.Appearance;
        var systemContrast = UI.ToggleButton(
                "Follow Windows high contrast", appearance.Contrast == ContrastPreference.System,
                "contrast.system", "contrast.system")
            .FocusDown("contrast.high").Busy(busy);
        var highContrast = UI.ToggleButton(
                "High contrast", appearance.Contrast == ContrastPreference.High,
                "contrast.high", "contrast.high")
            .FocusUp("contrast.system").FocusDown("bold-text.toggle").Busy(busy);
        var boldText = UI.ToggleButton(
                "Bold text", appearance.BoldText, "bold-text.toggle", "bold-text.toggle")
            .FocusUp("contrast.high").FocusDown("transparency.reduced").Busy(busy);
        var reducedTransparency = UI.ToggleButton(
                "Reduced transparency",
                appearance.Transparency == TransparencyPreference.Reduced,
                "transparency.reduced", "transparency.reduced")
            .FocusUp("bold-text.toggle").Busy(busy);
        return View(header,
            PageScope("accessibility.visual.page",
                UI.Text("Contrast and visibility", "accessibility.visual.heading",
                    "Contrast and visibility settings").Classes("page-heading"),
                systemContrast, highContrast, boldText, reducedTransparency,
                UI.Text("Accessibility overrides every theme and widget style.",
                    "accessibility.visual.help", "Accessibility policy help")
                    .Classes("page-help")),
            "contrast.system",
            "accessibility.visual.page");
    }

    private static WidgetView RenderOverlay(
        StackElement header,
        PlatformSettingsDocument settings,
        bool busy)
    {
        var appearance = settings.Appearance;
        var interfaceScale = LinkStepper(
            UI.Stepper("Interface size", Percent(appearance.InterfaceScale),
                "interface.decrease", "interface.increase", "interface.stepper",
                appearance.InterfaceScale > AppearanceSettings.MinimumInterfaceScale,
                appearance.InterfaceScale < AppearanceSettings.MaximumInterfaceScale),
            up: null,
            down: "opacity.stepper.decrement",
            busy);
        var opacity = LinkStepper(
            UI.Stepper("Backdrop darkness", Percent(appearance.BackdropOpacity),
                "opacity.decrease", "opacity.increase", "opacity.stepper",
                appearance.BackdropOpacity > AppearanceSettings.MinimumBackdropOpacity,
                appearance.BackdropOpacity < AppearanceSettings.MaximumBackdropOpacity),
            up: "interface.stepper.decrement",
            down: null,
            busy);
        return View(header,
            PageScope("overlay.page",
                UI.Text("Overlay", "overlay.heading", "Overlay settings")
                    .Classes("page-heading"),
                interfaceScale, opacity,
                UI.Text("Changes are stored atomically and applied by the host theme pipeline.",
                    "overlay.help", "Overlay settings help").Classes("page-help")),
            "interface.stepper.decrement",
            "overlay.page");
    }

    private static WidgetView RenderDiagnostics(
        StackElement header,
        SettingsPresentationState state)
    {
        var diagnostics = state.Diagnostics;
        var invalidThemes = state.Themes.Themes.Count(theme => !theme.IsValid);
        var runningWorkers = diagnostics.Workers.Count(worker => worker.IsRunning);
        var failedWorkers = diagnostics.Workers.Count(worker => worker.LastFailureCode is not null);
        var areas = new[]
        {
            diagnostics.Bridge,
            diagnostics.Catalog,
            diagnostics.Appearance,
            diagnostics.Providers,
            diagnostics.Consent,
            diagnostics.Overlay,
            diagnostics.Guide,
        };
        var children = new List<WidgetElement>
        {
            UI.Text("Diagnostics", "diagnostics.heading", "Settings diagnostics")
                .Classes("page-heading"),
            UI.Text(
                state.SettingsValid
                    ? "Settings file: valid"
                    : "Settings file: invalid; defaults shown",
                "diagnostics.settings", "Settings file status")
                .Classes(state.SettingsValid ? "diagnostic-ok" : "diagnostic-error"),
            UI.Text($"Theme packages: {state.Themes.Themes.Count} total, {invalidThemes} invalid",
                "diagnostics.themes", "Theme package status")
                .Classes(invalidThemes == 0 ? "diagnostic-ok" : "diagnostic-error"),
            UI.CodeText(
                    $"Schema: {state.Settings.SchemaVersion}; runtime snapshot {diagnostics.Revision}",
                    "diagnostics.schema", "Settings and runtime diagnostics schema")
                .AddClasses("diagnostic-line"),
        };
        children.AddRange(areas.Select(area => UI.Text(
            $"{DiagnosticPrefix(area.State)} {area.Label}: {area.Summary}",
            $"diagnostics.area.{area.Id}",
            $"{area.Label} diagnostic: {area.State}; {area.Summary}").Classes(
                area.State == PlatformDiagnosticState.Healthy
                    ? "diagnostic-ok"
                    : "diagnostic-error")));
        children.Add(UI.Text(
            $"Workers: {runningWorkers}/{diagnostics.Workers.Count} running; " +
            $"{failedWorkers} with a recorded failure",
            "diagnostics.workers", "Widget worker status").Classes(
                failedWorkers == 0 ? "diagnostic-ok" : "diagnostic-error"));
        foreach (var worker in diagnostics.Workers
                     .Where(worker => worker.LastFailureCode is not null)
                     .Take(3))
        {
            children.Add(UI.CodeText(
                $"{worker.WidgetName}: {worker.LastFailureCode}; " +
                (worker.IsRunning
                    ? "running after the recorded failure"
                    : worker.CanRestart ? "will restart on demand" : "restart limit reached"),
                $"diagnostics.worker.{worker.WidgetId}",
                $"{worker.WidgetName} worker failure").AddClasses("diagnostic-error"));
        }
        var recoveryOrder = SettingsAuthorityRecoveryPolicy.OrderedIndexes(diagnostics);
        children.Add(UI.Text(
            recoveryOrder.Length == 0
                ? "Content authority recovery: no pending records"
                : $"Content authority recovery: {recoveryOrder.Length} " +
                  (recoveryOrder.Length == 1
                      ? "record requires review"
                      : "records require review"),
            "diagnostics.authority.summary",
            recoveryOrder.Length == 0
                ? "No content authority recovery records are pending"
                : $"{recoveryOrder.Length} content authority recovery " +
                  (recoveryOrder.Length == 1
                      ? "record requires review"
                      : "records require review"))
            .Classes(recoveryOrder.Length == 0 ? "diagnostic-ok" : "diagnostic-error"));
        for (var displayIndex = 0; displayIndex < recoveryOrder.Length; displayIndex++)
        {
            var recovery = diagnostics.AuthorityRecoveries[recoveryOrder[displayIndex]];
            children.Add(UI.Button(
                    $"Review recovery · {recovery.DisplayName} · {recovery.State}",
                    $"authority.recovery.select.{displayIndex}",
                    $"diagnostics.authority.item.{displayIndex}")
                .Busy(state.Busy)
                .Classes(recovery.CanRetry ? "danger-button" : "secondary-button") with
                {
                    AccessibilityLabel =
                        $"Review content authority recovery {recovery.DisplayName}, " +
                        $"state {recovery.State}, status {recovery.StatusCode}, " +
                        (recovery.CanRetry ? "retry available" : "retry unavailable"),
                });
        }
        children.Add(UI.Button("Refresh diagnostics", "refresh", "diagnostics.refresh")
            .Icon(WidgetGlyph.Refresh, "Refresh diagnostics")
            .Busy(state.Busy).Classes("primary-button"));
        children.Add(UI.Button("Back", "back", "diagnostics.back")
            .Classes("secondary-button"));
        LinkVertical(children);
        return View(header,
            PageScope("diagnostics.page", children.ToArray()),
            recoveryOrder.Length == 0
                ? "diagnostics.refresh"
                : "diagnostics.authority.item.0",
            "diagnostics.page");
    }

    private static WidgetView RenderAuthorityRecovery(
        StackElement header,
        SettingsPresentationState state)
    {
        var recovery = state.Diagnostics.AuthorityRecoveries.FirstOrDefault(item =>
            string.Equals(item.RecoveryId, state.SelectedAuthorityRecoveryId,
                StringComparison.Ordinal));
        if (recovery is null)
        {
            var stale = new List<WidgetElement>
            {
                UI.Text("Recovery record changed", "authority.recovery.heading",
                    "Content authority recovery record changed").Classes("page-heading"),
                UI.Text(
                    "This recovery record is no longer pending or its confirmation token changed. " +
                    "Return to diagnostics and review the current bounded list before retrying.",
                    "authority.recovery.stale", "Recovery record is stale")
                    .Classes("diagnostic-error"),
                UI.Button("Review current diagnostics", "authority.recovery.cancel",
                        "authority.recovery.review-current")
                    .Busy(state.Busy).Classes("primary-button"),
                UI.Button("Back", "back", "authority.recovery.back")
                    .Classes("secondary-button"),
            };
            LinkVertical(stale);
            return View(header, PageScope("authority.recovery.page", stale.ToArray()),
                "authority.recovery.review-current", "authority.recovery.page");
        }

        var children = new List<WidgetElement>
        {
            UI.Text("Retry content authority recovery?", "authority.recovery.heading",
                "Confirm content authority recovery retry").Classes("page-heading"),
            UI.Text(recovery.DisplayName, "authority.recovery.display-name",
                $"Recovery {recovery.DisplayName}").Classes("diagnostic-line"),
            UI.CodeText($"Recovery ID: {recovery.RecoveryId}", "authority.recovery.id",
                $"Recovery ID {recovery.RecoveryId}").AddClasses("diagnostic-line"),
            UI.Text($"State: {recovery.State}", "authority.recovery.state",
                $"Recovery state {recovery.State}").Classes(
                    recovery.CanRetry ? "diagnostic-error" : "diagnostic-line"),
            UI.CodeText($"Status: {recovery.StatusCode}", "authority.recovery.status-code",
                $"Recovery status {recovery.StatusCode}").AddClasses("diagnostic-line"),
            UI.Text(
                recovery.CanRetry
                    ? "The host will retry this exact pending record. It clears the record only after verified recovery; there is no force-clear action."
                    : "This record cannot currently be retried. Return to diagnostics after resolving the reported condition; there is no force-clear action.",
                "authority.recovery.help", "Content authority recovery safety")
                .Classes("page-help"),
            UI.Button("Retry verified recovery", "authority.recovery.retry",
                    "authority.recovery.retry")
                .Disabled(!recovery.CanRetry || recovery.ConfirmationToken is null)
                .Busy(state.Busy).Classes("danger-button"),
            UI.Button("Cancel", "authority.recovery.cancel", "authority.recovery.cancel")
                .Classes("secondary-button"),
        };
        LinkVertical(children);
        return View(header, PageScope("authority.recovery.page", children.ToArray()),
            "authority.recovery.cancel", "authority.recovery.page");
    }

    private static WidgetView RenderReset(StackElement header, bool busy) => View(
        header,
        PageScope("reset.page",
            UI.Text("Reset settings?", "reset.heading", "Reset settings confirmation")
                .Classes("page-heading"),
            UI.Text("This restores the built-in theme, sizing, backdrop, and motion defaults.",
                "reset.warning", "Reset warning").Classes("page-help"),
            UI.Row("reset.actions",
                UI.Button("Reset", "reset.confirm", "reset.confirm")
                    .FocusRight("reset.cancel").Busy(busy).Classes("danger-button"),
                UI.Button("Cancel", "reset.cancel", "reset.cancel")
                    .FocusLeft("reset.confirm").Classes("secondary-button"))
                .Classes("reset-actions")),
        "reset.cancel",
        "reset.page");

    private static WidgetView RenderThemes(
        StackElement header,
        PlatformSettingsDocument settings,
        ThemeCatalogSnapshot themes,
        string? requestedFocus,
        bool busy)
    {
        var children = new List<WidgetElement>
        {
            UI.Text("Theme versions", "theme.heading", "Installed theme versions")
                .Classes("page-heading"),
            UI.Text("Choose an exact version to select or manage. Versions are grouped by theme ID.",
                "theme.help", "Theme version management help").Classes("page-help"),
        };
        string? lastId = null;
        var initialFocus = themes.Themes.Count > 0 ? "theme.item.0.action" : null;
        for (var index = 0; index < themes.Themes.Count; index++)
        {
            var entry = themes.Themes[index];
            if (!string.Equals(lastId, entry.CatalogId, StringComparison.Ordinal))
            {
                children.Add(UI.Text(
                    entry.CatalogId,
                    $"theme.group.{index}",
                    $"Theme {entry.CatalogId}").Classes("section-heading"));
                lastId = entry.CatalogId;
            }
            var selected = entry.IsValid &&
                entry.Descriptor.Id == settings.Appearance.ThemeId &&
                entry.Descriptor.Version.ToString() == settings.Appearance.ThemeVersion;
            var diagnostic = entry.Diagnostics.FirstOrDefault()?.Code;
            children.Add(UI.SettingsRow(
                entry.Descriptor.Name,
                new ComponentAction("Review", $"theme.open.{index}"),
                $"theme.item.{index}",
                description: entry.IsValid
                    ? entry.Descriptor.Publisher ?? (entry.Descriptor.IsBuiltIn ? "Platform" : "Legacy package")
                    : diagnostic ?? "Validation error",
                value: entry.Descriptor.Version.ToString(),
                status: selected ? "Active" : entry.IsValid ? "Installed" : "Invalid",
                statusTone: entry.IsValid ? StatusTone.Neutral : StatusTone.Danger,
                isBusy: busy));
            if (selected && requestedFocus is null) initialFocus = $"theme.item.{index}.action";
        }
        return View(header, PageScope("theme.picker", children.ToArray()),
            requestedFocus ?? initialFocus, "theme.picker");
    }

    private static WidgetView RenderThemeVersion(
        StackElement header,
        SettingsPresentationState state,
        bool confirmation)
    {
        var entry = state.SelectedTheme is null ? null : state.Themes.Themes.FirstOrDefault(item =>
            string.Equals(item.CatalogId, state.SelectedTheme.ThemeId, StringComparison.Ordinal) &&
            string.Equals(item.CatalogVersion, state.SelectedTheme.Version, StringComparison.Ordinal));
        if (entry is null)
            return View(header, PageScope("theme.version.page",
                    UI.Text("Theme version changed", "theme.version.missing", "Theme version changed"),
                    UI.Button("Back", "back", "theme.version.back")),
                "theme.version.back", "theme.version.page");
        var active = entry.Descriptor.Id == state.Settings.Appearance.ThemeId &&
                     entry.Descriptor.Version.ToString() == state.Settings.Appearance.ThemeVersion;
        var removable = !entry.Descriptor.IsBuiltIn && !active;
        if (confirmation)
        {
            return View(header, PageScope("theme.removal.page",
                    UI.Text("Remove theme version?", "theme.removal.heading", "Remove theme version confirmation")
                        .Classes("page-heading"),
                    UI.Text($"{entry.Descriptor.Name} · {entry.CatalogId} · {entry.CatalogVersion}",
                        "theme.removal.identity", "Exact theme version to remove").Classes("page-help"),
                    UI.Row("theme.removal.actions",
                        UI.Button("Remove", "theme.remove.confirm", "theme.removal.confirm")
                            .FocusRight("theme.removal.cancel").Busy(state.Busy).Disabled(!removable)
                            .Classes("danger-button"),
                        UI.Button("Cancel", "theme.remove.cancel", "theme.removal.cancel")
                            .FocusLeft("theme.removal.confirm").Classes("secondary-button"))),
                "theme.removal.cancel", "theme.removal.page");
        }
        var status = active ? "Active version" : entry.IsValid ? "Installed version" : "Invalid package";
        return View(header, PageScope("theme.version.page",
                UI.Text(entry.Descriptor.Name, "theme.version.heading", entry.Descriptor.Name)
                    .Classes("page-heading"),
                UI.Text($"{entry.CatalogId} · {entry.CatalogVersion}",
                    "theme.version.identity", "Exact theme identity").Classes("page-help"),
                UI.Text(status, "theme.version.status", status),
                UI.Button("Select this version", "theme.select", "theme.version.select")
                    .Busy(state.Busy).Disabled(!entry.IsValid || active)
                    .FocusDown("theme.version.remove"),
                UI.Button("Remove this version", "theme.remove.request", "theme.version.remove")
                    .Busy(state.Busy).Disabled(!removable)
                    .FocusUp("theme.version.select").FocusDown("theme.version.back")
                    .Classes("danger-button"),
                UI.Button("Back", "back", "theme.version.back")
                    .FocusUp("theme.version.remove").Classes("secondary-button")),
            entry.IsValid && !active ? "theme.version.select" : removable
                ? "theme.version.remove" : "theme.version.back",
            "theme.version.page");
    }

    private static RowElement LinkStepper(
        RowElement stepper,
        string? up,
        string? down,
        bool busy)
    {
        var children = stepper.Children.Select(child => child switch
        {
            ButtonElement button => button with
            {
                IsBusy = busy ? true : null,
                FocusNeighbors = (button.FocusNeighbors ?? new()) with
                {
                    Up = up,
                    Down = down,
                },
            },
            _ => child,
        }).ToArray();
        return stepper with { Children = children };
    }

    public static void LinkVertical(List<WidgetElement> elements)
    {
        var focusable = elements
            .Select((element, index) => (element, index))
            .Where(item => item.element is ButtonElement)
            .ToArray();
        for (var index = 0; index < focusable.Length; index++)
        {
            var current = (ButtonElement)focusable[index].element;
            elements[focusable[index].index] = current with
            {
                FocusNeighbors = (current.FocusNeighbors ?? new()) with
                {
                    Up = index > 0 ? ((ButtonElement)focusable[index - 1].element).Id : null,
                    Down = index + 1 < focusable.Length
                        ? ((ButtonElement)focusable[index + 1].element).Id
                        : null,
                },
            };
        }
    }

    private static string DiagnosticPrefix(PlatformDiagnosticState state) => state switch
    {
        PlatformDiagnosticState.Healthy => "OK",
        PlatformDiagnosticState.Degraded => "Check",
        _ => "Unavailable",
    };

    private static string Percent(double value) =>
        $"{Math.Round(value * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}%";

    public static string ShortContentDigest(string digest) =>
        digest.Length >= 12
            ? digest[..12].ToLowerInvariant()
            : "invalid-digest";
}

internal static class SettingsNavigationPolicy
{
    public static bool TryResolve(
        string actionId,
        SettingsPage currentPage,
        SettingsPage packageCapabilitiesReturnPage,
        out SettingsPage target)
    {
        target = actionId switch
        {
            "open.appearance" => SettingsPage.Appearance,
            "open.accessibility" => SettingsPage.Accessibility,
            "open.visual-accessibility" => SettingsPage.AccessibilityVisual,
            "open.overlay" => SettingsPage.Overlay,
            "open.installed-widgets" => SettingsPage.InstalledWidgets,
            "open.permissions" => SettingsPage.Permissions,
            "open.diagnostics" => SettingsPage.Diagnostics,
            "authority.recovery.cancel" => SettingsPage.Diagnostics,
            "open.reset" => SettingsPage.Reset,
            "open.themes" => SettingsPage.ThemePicker,
            "reset.cancel" => SettingsPage.Root,
            "back" => Parent(currentPage, packageCapabilitiesReturnPage),
            _ => currentPage,
        };
        return actionId is
            "open.appearance" or
            "open.accessibility" or
            "open.visual-accessibility" or
            "open.overlay" or
            "open.installed-widgets" or
            "open.permissions" or
            "open.diagnostics" or
            "authority.recovery.cancel" or
            "open.reset" or
            "open.themes" or
            "reset.cancel" or
            "back";
    }

    public static SettingsPage Parent(
        SettingsPage page,
        SettingsPage packageCapabilitiesReturnPage) => page switch
        {
            SettingsPage.ThemePicker => SettingsPage.Appearance,
            SettingsPage.ThemeVersion => SettingsPage.ThemePicker,
            SettingsPage.ThemeRemoval => SettingsPage.ThemeVersion,
            SettingsPage.AccessibilityVisual => SettingsPage.Accessibility,
            SettingsPage.InstalledWidgetDetails => SettingsPage.InstalledWidgets,
            SettingsPage.InstalledWidgetVersions => SettingsPage.InstalledWidgetDetails,
            SettingsPage.InstalledWidgetRecovery => SettingsPage.InstalledWidgets,
            SettingsPage.InstalledWidgetLocalData => SettingsPage.InstalledWidgetDetails,
            SettingsPage.PermissionDiagnostics => SettingsPage.Permissions,
            SettingsPage.PackageCapabilities => packageCapabilitiesReturnPage,
            SettingsPage.CapabilityDecision => SettingsPage.PackageCapabilities,
            SettingsPage.AuthorityRecovery => SettingsPage.Diagnostics,
            _ => SettingsPage.Root,
        };
}
