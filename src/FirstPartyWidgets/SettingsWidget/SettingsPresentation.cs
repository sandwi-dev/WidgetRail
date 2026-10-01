using System.Globalization;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.FirstPartyWidgets.Settings;

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
    string? ThemePickerFocusId = null,
    StartupRegistrationStatus? Startup = null,
    OverlayDisplayContext? Display = null,
    bool Loading = false,
    SettingsHostFeatures? HostFeatures = null);

/// <summary>Pure snapshot-only composition for Settings pages owned by DLV-036.</summary>
internal static partial class SettingsPresentation
{
    public static bool TryRender(SettingsPresentationState state, out WidgetView view)
    {
        ArgumentNullException.ThrowIfNull(state);
        var header = Header(state);
        if (state.Loading && state.Page != SettingsPage.Root)
        {
            view = View(header, PageScope("settings.loading.section",
                UI.LoadingIndicator("settings.loading.indicator", "Loading settings"),
                UI.Text("Loading settings…", "settings.loading.message").Classes("home-loading-message"),
                UI.Button("Back", "back", "settings.loading.back")),
                "settings.loading.back", "settings.loading.section");
            return true;
        }
        view = state.Page switch
        {
            SettingsPage.Root => RenderRoot(header, state.Settings, state.Themes, state.Busy, state.Loading,
                state.HostFeatures ?? new()),
            SettingsPage.Appearance => RenderAppearance(
                header, state.Settings, state.Themes, state.Busy, state.Display),
            SettingsPage.ThemePicker => RenderThemes(
                header, state.Settings, state.Themes, state.ThemePickerFocusId, state.Busy),
            SettingsPage.ThemeVersion => RenderThemeVersion(header, state, confirmation: false),
            SettingsPage.ThemeRemoval => RenderThemeVersion(header, state, confirmation: true),
            SettingsPage.Accessibility => RenderAccessibility(
                header, state.Settings, state.Busy, state.Display),
            SettingsPage.AccessibilityVisual => RenderVisualAccessibility(
                header, state.Settings, state.Busy),
            SettingsPage.Overlay => RenderOverlay(header, state.Settings, state.Busy, state.Startup, state.Display,
                state.HostFeatures ?? new()),
            SettingsPage.Controllers => ControllerSettingsPresentation.Render(state),
            SettingsPage.ControllerHelp => ControllerSettingsPresentation.Render(state, details: true),
            SettingsPage.About => RenderAbout(header),
            SettingsPage.Diagnostics => RenderDiagnostics(header, state),
            SettingsPage.AuthorityRecovery => RenderAuthorityRecovery(header, state),
            SettingsPage.Reset => RenderReset(header, state.Busy),
            _ => null!,
        };
        return view is not null;
    }

    internal static WidgetView AnimatePage(WidgetView view, SettingsPage page, bool loading)
    {
        // Loading is a data state within a page. Keep the motion parent and key
        // stable so background completion cannot reparent already-focused cards.
        if (view.Root is not StackElement { Id: "settings-root" } root || root.Children.Count < 2)
            return view;
        var children = root.Children.ToArray();
        children[1] = UI.Stack("settings.page-motion", children[1]).Classes("settings-page-motion")
            .TransitionContent("settings.pages", page.ToString(), (int)page);
        return view with { Root = root with { Children = children } };
    }

    public static StackElement Header(SettingsPresentationState state)
    {
        var children = new List<WidgetElement>
        {
            state.Page == SettingsPage.Root
                ? UI.Row("settings.header.row",
                    UI.Icon(WidgetGlyph.Settings, "settings.home.mark", "Settings").Classes("home-mark"),
                    UI.Text("Settings", "settings.title", "Settings").Classes("home-title"))
                    .Classes("settings-header-row", "home-header")
                : UI.Row("settings.header.row",
                    UI.Button("‹ Back", "back", "settings.back").Classes("header-action") with { AccessibilityLabel = "Back" },
                    UI.Text("Settings", "settings.title", "Settings").Classes("settings-title", "header-title"))
                    .Classes("settings-header-row"),
        };
        if (state.Status != "Ready" && state.Status != "Settings load when this widget becomes visible")
            children.Add(UI.Toast("Settings",
                state.Status.Length <= UI.MaximumToastMessageCharacters ? state.Status : state.Status[..(UI.MaximumToastMessageCharacters - 1)] + "…",
                state.Error ? ToastTone.Danger : state.Busy ? ToastTone.Info : ToastTone.Neutral,
                "settings.toast").Classes("settings-toast"));
        return UI.Stack("settings.header", children.ToArray()).Classes("settings-header");
    }

    private static StackElement ComposeRoot(StackElement header, WidgetElement content, string scope, string? initialFocus)
    {
        IReadOnlyList<ControllerShortcut> shortcuts = [];
        if (content is ContainerElement container && container.InputScopeId == scope)
        {
            shortcuts = container.Shortcuts;
            content = container with { InputScopeId = null, Shortcuts = [] };
        }
        // Promote the authored page title into the stable header. Body content
        // remains the same scroll owner and retains its control identities.
        if (scope != "settings-root" && content is ContainerElement page)
        {
            var heading = page.Children.OfType<TextElement>().FirstOrDefault(child => child.StyleClasses.Contains("page-heading"));
            if (heading is not null)
            {
                header = header with { Children = header.Children.Select(child => child is RowElement row
                    ? row with { Children = row.Children.Select(item => item.Id == "settings.title" ? heading : item).ToArray() }
                    : child).ToArray() };
                content = page with { Children = page.Children.Where(child => child != heading).ToArray() };
            }
            content = ArrangeSettingsRows((ContainerElement)content);
        }
        WidgetElement Link(WidgetElement element)
        {
            if (element is ButtonElement button && button.Id == "settings.back" && initialFocus is not null && initialFocus != "settings.back")
                return button.FocusDown(initialFocus);
            if (element is ContainerElement group) return group with { Children = group.Children.Select(Link).ToArray() };
            return element;
        }
        var notifications = header.Children.OfType<ToastElement>().Cast<WidgetElement>().ToArray();
        header = header with { Children = header.Children.Where(child => child is not ToastElement).ToArray() };
        return UI.Stack("settings-root", [Link(header), Link(content), .. notifications]).InputScope(scope)
            .Classes("settings-widget") with { Shortcuts = shortcuts };
    }

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
            ComposeRoot(header, content, activeScope, initialFocus),
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

    private static WidgetView RootView(
        StackElement header,
        WidgetElement content,
        string? initialFocus) => new(
            ComposeRoot(header, content, "settings-root", initialFocus),
            initialFocus,
            ActiveInputScopeId: "settings-root",
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                WidthMode = WidgetSurfaceAxisMode.Preferred,
                // Loading and ready content share one stable home surface.
                HeightMode = WidgetSurfaceAxisMode.Preferred,
                PreferredWidth = 880,
                PreferredHeight = 520,
                MinimumWidth = 520,
                MinimumHeight = 360,
            });

    private static WidgetView RenderRoot(
        StackElement header,
        PlatformSettingsDocument settings,
        ThemeCatalogSnapshot themes,
        bool busy,
        bool loading,
        SettingsHostFeatures hostFeatures)
    {
        ActionSurfaceElement Card(string title, string description, string action, string id, string icon, WidgetGlyph fallback) =>
            UI.ActionSurface(action, id, $"{title}. {description}", ActionSurfaceOrientation.Horizontal,
                UI.Icon(WidgetIcon.PackageSvg("settings." + icon, WidgetPackageIconColorMode.ThemeTint, fallback),
                    id + ".icon", title).Classes("home-card-icon"),
                UI.Stack(id + ".copy",
                    UI.Text(title, id + ".title", title).Classes("home-card-title"),
                    UI.Text(description, id + ".description", description).Classes("home-card-description"))
                    .Classes("home-card-copy"))
                .Busy(busy && !loading).Classes("category-card");

        WidgetElement categories = UI.VerticalScroll("settings.categories",
            UI.ResponsiveGrid("settings.category-grid", 250, 2,
                Card("General", hostFeatures.StartupRegistration ? "Startup, position, and navigation" : "Position and navigation",
                    "open.overlay", "category.overlay", "overlay", WidgetGlyph.Fullscreen),
                Card("Appearance", "Theme, size, backdrop, and animations", "open.appearance", "category.appearance", "appearance", WidgetGlyph.Settings),
                Card("Accessibility", "Text, contrast, and motion", "open.accessibility", "category.accessibility", "accessibility", WidgetGlyph.Check),
                Card("Controllers", hostFeatures.ExclusiveControllerControl ? "Input, shortcuts, and exclusive control" : "Controller shortcuts",
                    "open.controllers", "category.controllers", "controllers", WidgetGlyph.Connection),
                Card("Widgets", "Install, update, and permissions", "open.installed-widgets", "category.installed-widgets", "widgets", WidgetGlyph.Settings),
                Card("About & troubleshooting", "App information, diagnostics, and recovery", "open.about", "category.diagnostics", "diagnostics", WidgetGlyph.Connection))
                .Classes("category-grid")).Classes("root-category-list");
        var utilities = UI.ResponsiveGrid("settings.home.utilities", 120, 4,
            UI.Button("Restart", "application.restart", "settings.restart").Busy(busy).Classes("home-utility") with { AccessibilityLabel = "Restart WidgetRail" },
            UI.Button("Quit", "application.quit", "settings.quit").Busy(busy).Classes("home-utility") with { AccessibilityLabel = "Quit WidgetRail" })
            .Classes("home-utilities");
        return RootView(header,
            UI.Stack("settings.home.body", categories, utilities).Classes("home-body"),
            "category.overlay");
    }

    private static WidgetView RenderAppearance(StackElement header, PlatformSettingsDocument settings,
        ThemeCatalogSnapshot themes, bool busy, OverlayDisplayContext? display)
    {
        var scale = DisplayScalePolicy.Resolve(settings.Appearance, display?.Id);
        var appearance = settings.Appearance with { InterfaceScale = scale.InterfaceScale };
        var selected = themes.Themes.FirstOrDefault(entry => entry.IsValid &&
            entry.Descriptor.Id == appearance.ThemeId && entry.CatalogVersion == appearance.ThemeVersion);
        return View(header, PageScope("appearance.page",
            UI.Text("Appearance", "appearance.heading").Classes("page-heading"),
            UI.Text("Theme and sizing", "appearance.visual.heading").Classes("section-heading"),
            UI.Button("Theme: " + (selected?.Descriptor.Name ?? "Unavailable"), "open.themes", "appearance.theme").Busy(busy).Classes("setting-row"),
            DisplayHint(display),
            LinkStepper(UI.Stepper("Interface size", Percent(scale.InterfaceScale),
                ScaleAction("interface.decrease", display), ScaleAction("interface.increase", display), "interface.stepper",
                scale.InterfaceScale > AppearanceSettings.MinimumInterfaceScale, scale.InterfaceScale < AppearanceSettings.MaximumInterfaceScale),
                null, null, busy || string.IsNullOrEmpty(display?.Id)),
            LinkStepper(UI.Stepper("Backdrop darkness", Percent(appearance.BackdropOpacity), "opacity.decrease", "opacity.increase", "opacity.stepper",
                appearance.BackdropOpacity > AppearanceSettings.MinimumBackdropOpacity, appearance.BackdropOpacity < AppearanceSettings.MaximumBackdropOpacity), null, null, busy),
            UI.Text("Animations", "appearance.animations.heading").Classes("section-heading"),
            UI.Text("Reduced motion in Accessibility overrides these effects.", "appearance.motion.help").Classes("page-help"),
            UI.Switch("Animate widget switching", appearance.AnimateWidgetSwitching, "widget-switch-animation.toggle", "appearance.animate-widget-switching").Busy(busy),
                UI.Select("Focus animation", new SelectOption[]
                {
                    new("fade", "Fade", "focus-animation.fade", IsSelected: appearance.FocusAnimation == WidgetFocusAnimation.Fade),
                    new("settle", "Settle", "focus-animation.settle", IsSelected: appearance.FocusAnimation == WidgetFocusAnimation.Settle),
                    new("none", "None", "focus-animation.none", IsSelected: appearance.FocusAnimation == WidgetFocusAnimation.None),
                }, "overlay.focus-animation", "Focus animation")
                    .FocusUp("overlay.widget-switcher").FocusDown("overlay.section-animation")
                    .Busy(busy).AddClasses("setting-row"),
                UI.Select("Section animation", new SelectOption[]
                {
                    new("slide", "Slide", "section-animation.slide", IsSelected: appearance.SectionAnimation == WidgetSectionAnimation.Slide),
                    new("paging", "Paging", "section-animation.paging", IsSelected: appearance.SectionAnimation == WidgetSectionAnimation.Paging),
                    new("verticalslide", "Vertical slide", "section-animation.verticalslide", IsSelected: appearance.SectionAnimation == WidgetSectionAnimation.VerticalSlide),
                    new("reveal", "Reveal", "section-animation.reveal", IsSelected: appearance.SectionAnimation == WidgetSectionAnimation.Reveal),
                    new("coverslide", "Cover slide", "section-animation.coverslide", IsSelected: appearance.SectionAnimation == WidgetSectionAnimation.CoverSlide),
                    new("none", "None", "section-animation.none", IsSelected: appearance.SectionAnimation == WidgetSectionAnimation.None),
                }, "overlay.section-animation", "Section animation")
                    .FocusUp("overlay.focus-animation").FocusDown("overlay.animate-dialogs")
                    .Busy(busy).AddClasses("setting-row"),
                UI.Switch("Animate widget dialogs", appearance.AnimateWidgetModals,
                    "widget-modal-animation.toggle", "overlay.animate-dialogs")
                    .FocusUp("overlay.section-animation").FocusDown("overlay.modal-animation")
                    .Busy(busy).AddClasses("setting-row"),
                UI.Select("Dialog animation", new SelectOption[]
                {
                    new("zoom", "Zoom", "modal-animation.zoom", IsSelected: appearance.ModalAnimation == WidgetModalAnimation.Zoom),
                    new("lift", "Lift", "modal-animation.lift", IsSelected: appearance.ModalAnimation == WidgetModalAnimation.Lift),
                }, "overlay.modal-animation", "Dialog animation")
                    .FocusUp("overlay.animate-dialogs").FocusDown("overlay.animation-speed.decrement")
                    .Busy(busy).Disabled(!appearance.AnimateWidgetModals).AddClasses("setting-row"),
                UI.Text(appearance.AnimateWidgetModals ? "Used when widget dialogs open and close." : "Turn on Animate widget dialogs to choose a style.", "appearance.dialog.help").Classes("page-help"),
                LinkStepper(UI.Stepper("Widget animation speed",
                    appearance.WidgetAnimationSpeed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×",
                    "widget-animation-speed.decrease", "widget-animation-speed.increase", "overlay.animation-speed",
                    appearance.WidgetAnimationSpeed > AppearanceSettings.MinimumWidgetAnimationSpeed,
                    appearance.WidgetAnimationSpeed < AppearanceSettings.MaximumWidgetAnimationSpeed),
                    "overlay.modal-animation", null, busy),
                UI.Text("0.5× is slower; 2× is faster. Applies to supported widget animations.",
                    "overlay.animations.help").Classes("page-help")), "appearance.theme", "appearance.page");
    }

    private static WidgetView RenderAccessibility(StackElement header, PlatformSettingsDocument settings,
        bool busy, OverlayDisplayContext? display)
    {
        var appearance = settings.Appearance;
        var scale = DisplayScalePolicy.Resolve(appearance, display?.Id);
        return View(header, PageScope("accessibility.page",
            UI.Text("Accessibility", "accessibility.heading").Classes("page-heading"),
            UI.Text("Text", "accessibility.text.heading").Classes("section-heading"),
            DisplayHint(display),
            LinkStepper(UI.Stepper("Text size", Percent(scale.TextScale), ScaleAction("text.decrease", display),
                ScaleAction("text.increase", display), "text.stepper", scale.TextScale > AppearanceSettings.MinimumTextScale,
                scale.TextScale < AppearanceSettings.MaximumTextScale), null, null, busy || string.IsNullOrEmpty(display?.Id)),
            UI.Text("For proportional sizing, use Interface size in Appearance.", "text.layout-warning").Classes("page-help"),
            UI.Switch("Bold text", appearance.BoldText, "bold-text.toggle", "bold-text.toggle").Busy(busy),
            UI.Text("Visibility and motion", "accessibility.visibility.heading").Classes("section-heading"),
            UI.Select("Contrast", [
                new("system", "Follow Windows", "contrast.choose.system", IsSelected: appearance.Contrast == ContrastPreference.System),
                new("standard", "Standard", "contrast.choose.standard", IsSelected: appearance.Contrast == ContrastPreference.Standard),
                new("high", "High", "contrast.choose.high", IsSelected: appearance.Contrast == ContrastPreference.High)
            ], "contrast.preference").Busy(busy),
            UI.Switch("Reduced transparency", appearance.Transparency == TransparencyPreference.Reduced,
                "transparency.reduced", "transparency.reduced").Busy(busy),
            UI.Select("Motion", [
                new("system", "Follow Windows", "motion.choose.system", IsSelected: appearance.Motion == MotionPreference.System),
                new("full", "Full", "motion.choose.full", IsSelected: appearance.Motion == MotionPreference.Full),
                new("reduced", "Reduced", "motion.choose.reduced", IsSelected: appearance.Motion == MotionPreference.Reduced)
            ], "motion.preference").Busy(busy),
            UI.Text("Reduced motion overrides animation styles in Appearance.", "accessibility.motion.help").Classes("page-help")
        ), "text.stepper.decrement", "accessibility.page");
    }

    private static WidgetView RenderVisualAccessibility(StackElement header, PlatformSettingsDocument settings, bool busy) =>
        RenderAccessibility(header, settings, busy, null);

    private static string ScaleAction(string action, OverlayDisplayContext? display) =>
        action + "@" + (display?.Id ?? string.Empty);

    private static WidgetElement DisplayHint(OverlayDisplayContext? display) =>
        UI.Text(string.IsNullOrEmpty(display?.Id) ? "Display unavailable - sizing is temporarily unavailable." :
            $"Sizes are saved for {display.Name}.", "settings.display", "Display for saved sizing")
            .Classes("page-help");

    private static WidgetView RenderOverlay(StackElement header, PlatformSettingsDocument settings, bool busy,
        StartupRegistrationStatus? startup, OverlayDisplayContext? display, SettingsHostFeatures hostFeatures)
    {
        var appearance = settings.Appearance;
        return View(header, PageScope("overlay.page", [
            UI.Text("General", "overlay.heading").Classes("page-heading"),
            UI.Text("Overlay", "overlay.layout.heading").Classes("section-heading"),
            UI.Select("Position", [
                new("center", "Center", "position.choose.center", IsSelected: appearance.OverlayPosition == OverlayPosition.Center),
                new("left", "Bottom left", "position.choose.left", IsSelected: appearance.OverlayPosition == OverlayPosition.BottomLeft),
                new("right", "Bottom right", "position.choose.right", IsSelected: appearance.OverlayPosition == OverlayPosition.BottomRight)
            ], "overlay.position").Busy(busy),
            UI.Text("Corner layouts keep the outside edge fixed as widgets resize.", "overlay.position.help").Classes("page-help"),
            UI.Select("Widget switcher", [
                new("radial", "Radial + rail", "switcher.choose.radial", IsSelected: appearance.WidgetSwitcher == WidgetSwitcherLayout.Radial),
                new("rail", "Rail", "switcher.choose.rail", IsSelected: appearance.WidgetSwitcher == WidgetSwitcherLayout.Rail)
            ], "overlay.widget-switcher").Busy(busy),
            UI.Text("B opens the switcher from a widget. A enters the selected widget.", "overlay.widget-switcher.help").Classes("page-help"),
            .. hostFeatures.StartupRegistration ? new WidgetElement[] {
                UI.Text("Startup", "overlay.startup.heading").Classes("section-heading"),
                UI.Switch("Start WidgetRail when I sign in", startup?.Registered == true,
                    "startup.toggle", "overlay.startup").Busy(busy).Disabled(startup?.CanChange != true),
                UI.Text("Status — " + (startup?.Message ?? "Startup settings unavailable."), "overlay.startup.status").Classes("page-help")
            } : []
        ]), "overlay.position", "overlay.page");
    }

    private static readonly string ProductVersion = System.Reflection.CustomAttributeExtensions
        .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(typeof(SettingsWidget).Assembly)
        .First(attribute => attribute.Key == "WidgetRailVersion").Value!;

    private static WidgetView RenderAbout(StackElement header) => View(header, PageScope("about.page",
        UI.Text("About & troubleshooting", "about.heading").Classes("page-heading"),
        UI.Text("WidgetRail", "about.name").Classes("section-heading"),
        UI.Text("Version " + ProductVersion, "about.version").Classes("page-help"),
        UI.Text("Controller-first widgets for your desktop and games.", "about.description").Classes("page-help"),
        UI.Button("Diagnostics", "open.diagnostics", "about.diagnostics").Classes("setting-row"),
        UI.Text("Inspect service status, widget failures, and recovery information.", "about.diagnostics.help").Classes("page-help"),
        UI.Button("Reset settings…", "open.reset", "category.reset").Classes("danger-button"),
        UI.Text("Review what will be reset before confirming.", "about.reset.help").Classes("page-help")
    ), "about.diagnostics", "about.page");

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
                    : "diagnostic-error").AddClasses("diagnostic-area")));
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
            UI.Text("Themes", "theme.heading", "Installed themes")
                .Classes("page-heading"),
            UI.Text("Choose a theme to use or manage.",
                "theme.help", "Theme version management help").Classes("page-help"),
        };
        var initialFocus = themes.Themes.Count > 0 ? "theme.item.0.action" : null;
        for (var index = 0; index < themes.Themes.Count; index++)
        {
            var entry = themes.Themes[index];
            var selected = entry.IsValid &&
                entry.Descriptor.Id == settings.Appearance.ThemeId &&
                entry.Descriptor.Version.ToString() == settings.Appearance.ThemeVersion;
            var id = $"theme.item.{index}";
            var status = selected ? "Active" : entry.IsValid ? "Installed" : "Invalid";
            var row = UI.ActionSurface($"theme.open.{index}", id + ".action",
                $"{entry.Descriptor.Name}, {entry.CatalogVersion}, {status}. Review theme.",
                ActionSurfaceOrientation.Horizontal,
                UI.Text(entry.Descriptor.Name, id + ".label").Classes("theme-name"),
                UI.Text(entry.CatalogVersion, id + ".version").Classes("theme-version"),
                UI.StatusBadge(status, selected ? StatusTone.Success : entry.IsValid ? StatusTone.Neutral : StatusTone.Danger,
                    id + ".status").AddClasses("theme-status"))
                .Selected(selected).Busy(busy).Classes("theme-row");
            children.Add(row);
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
                UI.Text(entry.Descriptor.Publisher ?? (entry.Descriptor.IsBuiltIn ? "Platform" : "Legacy package"),
                    "theme.version.publisher", "Theme publisher").Classes("page-help"),
                UI.Text(entry.IsValid ? status : entry.Diagnostics.FirstOrDefault()?.Code ?? "Validation error",
                    "theme.version.status", status).Classes(entry.IsValid ? "page-help" : "diagnostic-error"),
                UI.Button("Use theme", "theme.select", "theme.version.select")
                    .Busy(state.Busy).Disabled(!entry.IsValid || active)
                    .FocusDown("theme.version.remove").Classes("primary-button"),
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
            "open.controller-help" => SettingsPage.ControllerHelp,
            "open.widget-technical" => SettingsPage.InstalledWidgetTechnical,
            "open.about" => SettingsPage.About,
            "open.appearance" => SettingsPage.Appearance,
            "open.accessibility" => SettingsPage.Accessibility,
            "open.visual-accessibility" => SettingsPage.AccessibilityVisual,
            "open.overlay" => SettingsPage.Overlay,
            "open.controllers" => SettingsPage.Controllers,
            "open.installed-widgets" => SettingsPage.InstalledWidgets,
            "open.permissions" => SettingsPage.Permissions,
            "open.diagnostics" => SettingsPage.Diagnostics,
            "authority.recovery.cancel" => SettingsPage.Diagnostics,
            "open.reset" => SettingsPage.Reset,
            "open.themes" => SettingsPage.ThemePicker,
            "reset.cancel" => SettingsPage.About,
            "back" => Parent(currentPage, packageCapabilitiesReturnPage),
            _ => currentPage,
        };
        return actionId is
            "open.controller-help" or
            "open.widget-technical" or
            "open.about" or
            "open.appearance" or
            "open.accessibility" or
            "open.visual-accessibility" or
            "open.overlay" or
            "open.controllers" or
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
            SettingsPage.ControllerHelp => SettingsPage.Controllers,
            SettingsPage.InstalledWidgetTechnical => SettingsPage.InstalledWidgetDetails,
            SettingsPage.Diagnostics or SettingsPage.Reset => SettingsPage.About,
            SettingsPage.InstalledWidgetUpdate => SettingsPage.InstalledWidgetDetails,
            SettingsPage.InstalledWidgetVersionRemoval => SettingsPage.InstalledWidgetVersions,
            SettingsPage.ThemePicker => SettingsPage.Appearance,
            SettingsPage.ThemeVersion => SettingsPage.ThemePicker,
            SettingsPage.ThemeRemoval => SettingsPage.ThemeVersion,
            SettingsPage.AccessibilityVisual => SettingsPage.Accessibility,
            SettingsPage.InstalledWidgetDetails => SettingsPage.InstalledWidgets,
            SettingsPage.InstalledWidgetVersions => SettingsPage.InstalledWidgetDetails,
            SettingsPage.InstalledWidgetRecovery => SettingsPage.InstalledWidgets,
            SettingsPage.InstalledWidgetLocalData => SettingsPage.InstalledWidgetDetails,
            SettingsPage.InstalledWidgetUninstall => SettingsPage.InstalledWidgetDetails,
            SettingsPage.PermissionDiagnostics => SettingsPage.Permissions,
            SettingsPage.PackageCapabilities => packageCapabilitiesReturnPage,
            SettingsPage.CapabilityDecision => SettingsPage.PackageCapabilities,
            SettingsPage.AuthorityRecovery => SettingsPage.Diagnostics,
            _ => SettingsPage.Root,
        };
}
