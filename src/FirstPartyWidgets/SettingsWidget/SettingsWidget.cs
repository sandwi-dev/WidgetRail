using System.Globalization;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;
using GameBarAlternative.WidgetCatalog;
using CatalogService = GameBarAlternative.WidgetCatalog.WidgetCatalog;

namespace GameBarAlternative.FirstPartyWidgets.Settings;

public enum SettingsPage
{
    Root,
    Appearance,
    ThemePicker,
    Accessibility,
    AccessibilityVisual,
    Overlay,
    InstalledWidgets,
    InstalledWidgetDetails,
    InstalledWidgetVersions,
    Permissions,
    PermissionDiagnostics,
    PackageCapabilities,
    CapabilityDecision,
    Diagnostics,
    Reset,
}

public sealed partial class SettingsWidget : Widget
{
    private const double ScaleStep = 0.05;
    private const double OpacityStep = 0.05;
    private readonly PlatformSettingsStore _store;
    private readonly ThemeCatalog _catalog;
    private readonly CatalogService _widgetCatalog;
    private readonly ConsentStore _consentStore;
    private readonly IPlatformDiagnosticsService _diagnosticsService;
    private readonly string? _bundledWidgetRoot;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private PlatformSettingsDocument _settings = PlatformSettingsDocument.Default;
    private ThemeCatalogSnapshot _themes;
    private PlatformDiagnosticsSnapshot _diagnostics = PlatformDiagnosticsSnapshot.Unavailable();
    private WidgetCatalogSnapshot _installedWidgets = new([]);
    private IReadOnlyList<WidgetManifest> _builtInWidgets = [];
    private bool _installedWidgetCatalogValid = true;
    private string? _installedWidgetDiagnostic;
    private int _installedWidgetPage;
    private int _installedVersionPage;
    private string? _selectedInstalledWidgetId;
    private string? _selectedBuiltInWidgetId;
    private SettingsPage _page;
    private int _activationLoadCount;
    private bool _settingsValid = true;
    private bool _busy;
    private bool _error;
    private string _status = "Settings load when this widget becomes visible";

    public SettingsWidget(
        PlatformSettingsStore? store = null,
        ThemeCatalog? catalog = null,
        CatalogService? widgetCatalog = null,
        ConsentStore? consentStore = null,
        IPlatformDiagnosticsService? diagnostics = null,
        string? bundledWidgetRoot = null)
    {
        var paths = store?.Paths ?? PlatformSettingsPaths.CreateDefault();
        _store = store ?? new PlatformSettingsStore(paths);
        _catalog = catalog ?? new ThemeCatalog(paths);
        _widgetCatalog = widgetCatalog ?? new CatalogService(
            Path.Combine(paths.RootDirectory, "widgets"));
        _consentStore = consentStore ?? new ConsentStore(
            Path.Combine(paths.RootDirectory, "consent"));
        _diagnosticsService = diagnostics ?? UnavailablePlatformDiagnosticsService.Instance;
        _bundledWidgetRoot = string.IsNullOrWhiteSpace(bundledWidgetRoot)
            ? null
            : Path.GetFullPath(bundledWidgetRoot);
        var builtIn = _catalog.BuiltInDefault;
        _themes = new ThemeCatalogSnapshot(
            [new ThemeCatalogEntry(builtIn.Descriptor, builtIn.IsValid, builtIn.Diagnostics)]);
    }

    public SettingsPage CurrentPage
    {
        get { lock (_stateLock) return _page; }
    }

    public int ActivationLoadCount => Volatile.Read(ref _activationLoadCount);

    public override WidgetView Render()
    {
        PlatformSettingsDocument settings;
        ThemeCatalogSnapshot themes;
        SettingsPage page;
        bool busy;
        bool error;
        bool settingsValid;
        PlatformDiagnosticsSnapshot diagnostics;
        string status;
        lock (_stateLock)
        {
            settings = _settings;
            themes = _themes;
            page = _page;
            busy = _busy;
            error = _error;
            settingsValid = _settingsValid;
            diagnostics = _diagnostics;
            status = _status;
        }

        var header = UI.Stack("settings.header",
            UI.Text("SETTINGS", "settings.title", "Settings").Classes("settings-title"),
            UI.Text(status, "settings.status", status).Classes(
                "settings-status", error ? "is-error" : busy ? "is-busy" : "is-ready"))
            .Classes("settings-header");
        return page switch
        {
            SettingsPage.Root => RenderRoot(header, settings, busy),
            SettingsPage.Appearance => RenderAppearance(header, settings, themes, busy),
            SettingsPage.ThemePicker => RenderThemes(header, settings, themes, busy),
            SettingsPage.Accessibility => RenderAccessibility(header, settings, busy),
            SettingsPage.AccessibilityVisual => RenderVisualAccessibility(header, settings, busy),
            SettingsPage.Overlay => RenderOverlay(header, settings, busy),
            SettingsPage.InstalledWidgets => RenderInstalledWidgets(header, busy),
            SettingsPage.InstalledWidgetDetails => RenderInstalledWidgetDetails(header, busy),
            SettingsPage.InstalledWidgetVersions => RenderInstalledWidgetVersions(header, busy),
            SettingsPage.Permissions => RenderPermissionPackages(header, busy),
            SettingsPage.PermissionDiagnostics => RenderPermissionDiagnostics(header),
            SettingsPage.PackageCapabilities => RenderPackageCapabilities(header, busy),
            SettingsPage.CapabilityDecision => RenderCapabilityDecision(header, busy),
            SettingsPage.Diagnostics => RenderDiagnostics(
                header, settings, themes, settingsValid, diagnostics, busy),
            SettingsPage.Reset => RenderReset(header, busy),
            _ => RenderRoot(header, settings, busy),
        };
    }

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationLoadCount);
        await ReloadAsync(activeLifetime).ConfigureAwait(false);
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        // ReloadAsync owns the same operation gate used by normal actions. Keep
        // refresh outside that critical section so controller refresh cannot
        // deadlock while still serializing against every other mutation.
        if (action.ActionId == "refresh")
        {
            await ReloadAsync(cancellationToken, "Settings refreshed").ConfigureAwait(false);
            return;
        }
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            switch (action.ActionId)
            {
                case "open.appearance": Navigate(SettingsPage.Appearance); break;
                case "open.accessibility": Navigate(SettingsPage.Accessibility); break;
                case "open.visual-accessibility": Navigate(SettingsPage.AccessibilityVisual); break;
                case "open.overlay": Navigate(SettingsPage.Overlay); break;
                case "open.installed-widgets": Navigate(SettingsPage.InstalledWidgets); break;
                case "open.permissions": Navigate(SettingsPage.Permissions); break;
                case "installed.permissions.open": OpenSelectedInstalledPermissions(); break;
                case "open.permission-diagnostics": OpenPermissionDiagnostics(); break;
                case "open.diagnostics": Navigate(SettingsPage.Diagnostics); break;
                case "open.reset": Navigate(SettingsPage.Reset); break;
                case "open.themes": Navigate(SettingsPage.ThemePicker); break;
                case "back": Navigate(ParentPage(CurrentPage)); break;
                case "installed.previous-page": ChangeInstalledWidgetPage(-1); break;
                case "installed.next-page": ChangeInstalledWidgetPage(1); break;
                case "installed.versions.open": OpenInstalledWidgetVersions(); break;
                case "installed.versions.previous-page": ChangeInstalledVersionPage(-1); break;
                case "installed.versions.next-page": ChangeInstalledVersionPage(1); break;
                case "installed.toggle": await ToggleSelectedInstalledWidgetAsync(cancellationToken)
                    .ConfigureAwait(false); break;
                case "capability.grant": await ChangeConsentAsync(
                    ConsentDecision.Grant, cancellationToken).ConfigureAwait(false); break;
                case "capability.deny": await ChangeConsentAsync(
                    ConsentDecision.Deny, cancellationToken).ConfigureAwait(false); break;
                case "text.decrease": await ChangeAppearanceAsync(
                    appearance => appearance with { TextScale = Step(
                        appearance.TextScale, -ScaleStep,
                        AppearanceSettings.MinimumTextScale,
                        AppearanceSettings.MaximumTextScale) }, "Text size saved", cancellationToken).ConfigureAwait(false); break;
                case "text.increase": await ChangeAppearanceAsync(
                    appearance => appearance with { TextScale = Step(
                        appearance.TextScale, ScaleStep,
                        AppearanceSettings.MinimumTextScale,
                        AppearanceSettings.MaximumTextScale) }, "Text size saved", cancellationToken).ConfigureAwait(false); break;
                case "interface.decrease": await ChangeAppearanceAsync(
                    appearance => appearance with { InterfaceScale = Step(
                        appearance.InterfaceScale, -ScaleStep,
                        AppearanceSettings.MinimumInterfaceScale,
                        AppearanceSettings.MaximumInterfaceScale) }, "Interface size saved", cancellationToken).ConfigureAwait(false); break;
                case "interface.increase": await ChangeAppearanceAsync(
                    appearance => appearance with { InterfaceScale = Step(
                        appearance.InterfaceScale, ScaleStep,
                        AppearanceSettings.MinimumInterfaceScale,
                        AppearanceSettings.MaximumInterfaceScale) }, "Interface size saved", cancellationToken).ConfigureAwait(false); break;
                case "opacity.decrease": await ChangeAppearanceAsync(
                    appearance => appearance with { BackdropOpacity = Step(
                        appearance.BackdropOpacity, -OpacityStep,
                        AppearanceSettings.MinimumBackdropOpacity,
                        AppearanceSettings.MaximumBackdropOpacity) }, "Backdrop saved", cancellationToken).ConfigureAwait(false); break;
                case "opacity.increase": await ChangeAppearanceAsync(
                    appearance => appearance with { BackdropOpacity = Step(
                        appearance.BackdropOpacity, OpacityStep,
                        AppearanceSettings.MinimumBackdropOpacity,
                        AppearanceSettings.MaximumBackdropOpacity) }, "Backdrop saved", cancellationToken).ConfigureAwait(false); break;
                case "motion.system": await ChangeAppearanceAsync(
                    appearance => appearance with
                    {
                        Motion = appearance.Motion == MotionPreference.System
                            ? MotionPreference.Full
                            : MotionPreference.System,
                    }, "Motion preference saved", cancellationToken).ConfigureAwait(false); break;
                case "motion.reduced": await ChangeAppearanceAsync(
                    appearance => appearance with
                    {
                        Motion = appearance.Motion == MotionPreference.Reduced
                            ? MotionPreference.Full
                            : MotionPreference.Reduced,
                    }, "Motion preference saved", cancellationToken).ConfigureAwait(false); break;
                case "contrast.system": await ChangeAppearanceAsync(
                    appearance => appearance with
                    {
                        Contrast = appearance.Contrast == ContrastPreference.System
                            ? ContrastPreference.Standard
                            : ContrastPreference.System,
                    }, "Contrast preference saved", cancellationToken).ConfigureAwait(false); break;
                case "contrast.high": await ChangeAppearanceAsync(
                    appearance => appearance with
                    {
                        Contrast = appearance.Contrast == ContrastPreference.High
                            ? ContrastPreference.Standard
                            : ContrastPreference.High,
                    }, "Contrast preference saved", cancellationToken).ConfigureAwait(false); break;
                case "bold-text.toggle": await ChangeAppearanceAsync(
                    appearance => appearance with { BoldText = !appearance.BoldText },
                    "Bold text preference saved", cancellationToken).ConfigureAwait(false); break;
                case "transparency.reduced": await ChangeAppearanceAsync(
                    appearance => appearance with
                    {
                        Transparency = appearance.Transparency == TransparencyPreference.Reduced
                            ? TransparencyPreference.Full
                            : TransparencyPreference.Reduced,
                    }, "Transparency preference saved", cancellationToken).ConfigureAwait(false); break;
                case "reset.confirm": await ResetAsync(cancellationToken).ConfigureAwait(false); break;
                case "reset.cancel": Navigate(SettingsPage.Root); break;
                default:
                    if (TryThemeIndex(action.ActionId, out var index))
                        await SelectThemeAsync(index, cancellationToken).ConfigureAwait(false);
                    else if (TryIndexedAction(action.ActionId, "installed.select.", out index))
                        SelectInstalledWidget(index);
                    else if (TryIndexedAction(action.ActionId, "installed.builtin.select.", out index))
                        SelectBuiltInWidget(index);
                    else if (TryIndexedAction(action.ActionId, "installed.version.select.", out index))
                        await SelectInstalledVersionAsync(index, cancellationToken).ConfigureAwait(false);
                    else if (TryIndexedAction(action.ActionId, "permission.select.", out index))
                        SelectPermissionPackage(index);
                    else if (TryIndexedAction(action.ActionId, "capability.select.", out index))
                        SelectCapability(index);
                    break;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ReloadAsync(
        CancellationToken cancellationToken,
        string successStatus = "Ready")
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetOperation("Loading settings…", busy: true, error: false);
            PlatformSettingsDocument settings;
            var settingsValid = true;
            string? warning = null;
            try
            {
                settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PlatformSettingsException exception)
            {
                settings = PlatformSettingsDocument.Default;
                settingsValid = false;
                warning = $"Settings were invalid ({exception.Code}); safe defaults are shown";
            }

            ThemeCatalogSnapshot themes;
            try
            {
                themes = _catalog.Discover();
            }
            catch (PlatformSettingsException exception)
            {
                var builtIn = _catalog.BuiltInDefault;
                themes = new ThemeCatalogSnapshot(
                    [new ThemeCatalogEntry(builtIn.Descriptor, builtIn.IsValid, builtIn.Diagnostics)]);
                warning ??= $"Theme catalog unavailable ({exception.Code}); default theme only";
            }

            var selectedInstalled = themes.Themes.Any(entry =>
                entry.IsValid &&
                string.Equals(entry.Descriptor.Id, settings.Appearance.ThemeId, StringComparison.Ordinal) &&
                string.Equals(entry.Descriptor.Version.ToString(), settings.Appearance.ThemeVersion, StringComparison.Ordinal));
            if (!selectedInstalled)
                warning ??= "Selected theme is unavailable; choose an installed theme";
            var installedWarning = await ReloadInstalledWidgetsAsync(cancellationToken)
                .ConfigureAwait(false);
            warning ??= installedWarning;
            var permissionWarning = await ReloadPermissionsAsync(cancellationToken)
                .ConfigureAwait(false);
            warning ??= permissionWarning;
            PlatformDiagnosticsSnapshot diagnostics;
            try
            {
                diagnostics = await _diagnosticsService.GetSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (PlatformDiagnosticsException exception)
            {
                diagnostics = PlatformDiagnosticsSnapshot.Unavailable(
                    $"Runtime diagnostics unavailable ({exception.Code})");
                warning ??= $"Runtime diagnostics unavailable ({exception.Code})";
            }
            lock (_stateLock)
            {
                _settings = settings;
                _settingsValid = settingsValid;
                _themes = themes;
                _diagnostics = diagnostics;
                _busy = false;
                _error = warning is not null;
                _status = warning ?? successStatus;
            }
            Invalidate();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ChangeAppearanceAsync(
        Func<AppearanceSettings, AppearanceSettings> mutation,
        string success,
        CancellationToken cancellationToken)
    {
        await PersistAsync(
            current => current with { Appearance = mutation(current.Appearance) },
            success,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SelectThemeAsync(int index, CancellationToken cancellationToken)
    {
        ThemeCatalogEntry? entry;
        lock (_stateLock)
            entry = index >= 0 && index < _themes.Themes.Count ? _themes.Themes[index] : null;
        if (entry is null || !entry.IsValid) return;
        await ChangeAppearanceAsync(
            appearance => appearance with
            {
                ThemeId = entry.Descriptor.Id,
                ThemeVersion = entry.Descriptor.Version.ToString(),
            },
            $"Theme set to {entry.Descriptor.Name}",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        SetOperation("Resetting settings…", busy: true, error: false);
        try
        {
            var saved = await _store.ReplaceAsync(
                PlatformSettingsDocument.Default,
                cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _settingsValid = true;
                _page = SettingsPage.Root;
                _busy = false;
                _error = false;
                _status = "Settings reset to defaults";
            }
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Reset failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private async Task PersistAsync(
        Func<PlatformSettingsDocument, PlatformSettingsDocument> mutation,
        string success,
        CancellationToken cancellationToken)
    {
        SetOperation("Saving…", busy: true, error: false);
        try
        {
            PlatformSettingsDocument saved;
            bool valid;
            PlatformSettingsDocument fallback;
            lock (_stateLock)
            {
                valid = _settingsValid;
                fallback = _settings;
            }
            if (valid)
                saved = await _store.UpdateAsync(mutation, cancellationToken).ConfigureAwait(false);
            else
                saved = await _store.ReplaceAsync(mutation(fallback), cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _settings = saved;
                _settingsValid = true;
                _busy = false;
                _error = false;
                _status = success;
            }
        }
        catch (PlatformSettingsException exception)
        {
            SetOperation($"Save failed ({exception.Code})", busy: false, error: true);
            return;
        }
        Invalidate();
    }

    private static WidgetView RenderRoot(
        StackElement header,
        PlatformSettingsDocument settings,
        bool busy)
    {
        var appearance = UI.Button("Appearance", "open.appearance", "category.appearance")
            .Busy(busy).Classes("category-card");
        var accessibility = UI.Button("Accessibility", "open.accessibility", "category.accessibility")
            .Busy(busy).Classes("category-card");
        var overlay = UI.Button("Overlay", "open.overlay", "category.overlay")
            .Busy(busy).Classes("category-card");
        var installedWidgets = UI.Button("Installed widgets", "open.installed-widgets", "category.installed-widgets")
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
                        appearance, accessibility, overlay, installedWidgets, diagnostics, refresh, reset)
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
        var content = PageScope("appearance.page",
            UI.Text("Appearance", "appearance.heading", "Appearance settings").Classes("page-heading"),
            UI.Button(label, "open.themes", "appearance.theme")
                .Busy(busy).Classes("setting-row"),
            UI.Text("Choose a versioned theme package. Invalid packages remain visible but cannot be selected.",
                "appearance.help", "Theme picker help").Classes("page-help"));
        return View(header, content, "appearance.theme", "appearance.page");
    }

    private static WidgetView RenderAccessibility(
        StackElement header,
        PlatformSettingsDocument settings,
        bool busy)
    {
        var appearance = settings.Appearance;
        var text = LinkStepper(
            UI.Stepper("Text size", Percent(appearance.TextScale), "text.decrease", "text.increase", "text.stepper",
                appearance.TextScale > AppearanceSettings.MinimumTextScale,
                appearance.TextScale < AppearanceSettings.MaximumTextScale),
            up: null, down: "motion.system", busy: busy);
        var system = UI.ToggleButton("Follow Windows motion", appearance.Motion == MotionPreference.System,
                "motion.system", "motion.system")
            .FocusUp("text.stepper.decrement").FocusDown("motion.reduced").Busy(busy);
        var reduced = UI.ToggleButton("Reduced motion", appearance.Motion == MotionPreference.Reduced,
                "motion.reduced", "motion.reduced")
            .FocusUp("motion.system").FocusDown("accessibility.visual").Busy(busy);
        var visual = UI.Button("Contrast and visibility", "open.visual-accessibility",
                "accessibility.visual")
            .FocusUp("motion.reduced").Busy(busy).Classes("setting-row");
        return View(header,
            PageScope("accessibility.page",
                UI.Text("Accessibility", "accessibility.heading", "Accessibility settings").Classes("page-heading"),
                text, system, reduced, visual),
            "text.stepper.decrement", "accessibility.page");
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
                "Bold text", appearance.BoldText,
                "bold-text.toggle", "bold-text.toggle")
            .FocusUp("contrast.high").FocusDown("transparency.reduced").Busy(busy);
        var reducedTransparency = UI.ToggleButton(
                "Reduced transparency", appearance.Transparency == TransparencyPreference.Reduced,
                "transparency.reduced", "transparency.reduced")
            .FocusUp("bold-text.toggle").Busy(busy);
        return View(header,
            PageScope("accessibility.visual.page",
                UI.Text("Contrast and visibility", "accessibility.visual.heading",
                    "Contrast and visibility settings").Classes("page-heading"),
                systemContrast, highContrast, boldText, reducedTransparency,
                UI.Text("Accessibility overrides every theme and widget style.",
                    "accessibility.visual.help", "Accessibility policy help").Classes("page-help")),
            "contrast.system", "accessibility.visual.page");
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
            up: null, down: "opacity.stepper.decrement", busy: busy);
        var opacity = LinkStepper(
            UI.Stepper("Backdrop darkness", Percent(appearance.BackdropOpacity),
                "opacity.decrease", "opacity.increase", "opacity.stepper",
                appearance.BackdropOpacity > AppearanceSettings.MinimumBackdropOpacity,
                appearance.BackdropOpacity < AppearanceSettings.MaximumBackdropOpacity),
            up: "interface.stepper.decrement", down: null, busy: busy);
        return View(header,
            PageScope("overlay.page",
                UI.Text("Overlay", "overlay.heading", "Overlay settings").Classes("page-heading"),
                interfaceScale, opacity,
                UI.Text("Changes are stored atomically and applied by the host theme pipeline.",
                    "overlay.help", "Overlay settings help").Classes("page-help")),
            "interface.stepper.decrement", "overlay.page");
    }

    private static WidgetView RenderDiagnostics(
        StackElement header,
        PlatformSettingsDocument settings,
        ThemeCatalogSnapshot themes,
        bool settingsValid,
        PlatformDiagnosticsSnapshot diagnostics,
        bool busy)
    {
        var invalidThemes = themes.Themes.Count(theme => !theme.IsValid);
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
            UI.Text("Diagnostics", "diagnostics.heading", "Settings diagnostics").Classes("page-heading"),
            UI.Text(settingsValid ? "Settings file: valid" : "Settings file: invalid; defaults shown",
                "diagnostics.settings", "Settings file status").Classes(settingsValid ? "diagnostic-ok" : "diagnostic-error"),
            UI.Text($"Theme packages: {themes.Themes.Count} total, {invalidThemes} invalid",
                "diagnostics.themes", "Theme package status").Classes(
                    invalidThemes == 0 ? "diagnostic-ok" : "diagnostic-error"),
            UI.CodeText($"Schema: {settings.SchemaVersion}; runtime snapshot {diagnostics.Revision}",
                "diagnostics.schema", "Settings and runtime diagnostics schema")
                .AddClasses("diagnostic-line"),
        };
        children.AddRange(areas.Select(area => UI.Text(
            $"{DiagnosticPrefix(area.State)} {area.Label}: {area.Summary}",
            $"diagnostics.area.{area.Id}",
            $"{area.Label} diagnostic: {area.State}; {area.Summary}").Classes(
                area.State == PlatformDiagnosticState.Healthy ? "diagnostic-ok" : "diagnostic-error")));
        children.Add(UI.Text(
            $"Workers: {runningWorkers}/{diagnostics.Workers.Count} running; {failedWorkers} with a recorded failure",
            "diagnostics.workers", "Widget worker status").Classes(
                failedWorkers == 0 ? "diagnostic-ok" : "diagnostic-error"));
        foreach (var worker in diagnostics.Workers.Where(worker => worker.LastFailureCode is not null).Take(3))
        {
            children.Add(UI.CodeText(
                $"{worker.WidgetName}: {worker.LastFailureCode}; " +
                (worker.IsRunning
                    ? "running after the recorded failure"
                    : worker.CanRestart ? "will restart on demand" : "restart limit reached"),
                $"diagnostics.worker.{worker.WidgetId}",
                $"{worker.WidgetName} worker failure").AddClasses("diagnostic-error"));
        }
        children.Add(UI.Button("Refresh diagnostics", "refresh", "diagnostics.refresh")
            .Icon(WidgetGlyph.Refresh, "Refresh diagnostics")
            .FocusDown("diagnostics.back").Busy(busy).Classes("primary-button"));
        children.Add(UI.Button("Back", "back", "diagnostics.back")
            .FocusUp("diagnostics.refresh").Classes("secondary-button"));
        return View(header,
            PageScope("diagnostics.page", children.ToArray()),
            "diagnostics.refresh", "diagnostics.page");
    }

    private static string DiagnosticPrefix(PlatformDiagnosticState state) => state switch
    {
        PlatformDiagnosticState.Healthy => "OK",
        PlatformDiagnosticState.Degraded => "Check",
        _ => "Unavailable",
    };

    private static WidgetView RenderReset(StackElement header, bool busy) => View(
        header,
        PageScope("reset.page",
            UI.Text("Reset settings?", "reset.heading", "Reset settings confirmation").Classes("page-heading"),
            UI.Text("This restores the built-in theme, sizing, backdrop, and motion defaults.",
                "reset.warning", "Reset warning").Classes("page-help"),
            UI.Row("reset.actions",
                UI.Button("Reset", "reset.confirm", "reset.confirm")
                    .FocusRight("reset.cancel").Busy(busy).Classes("danger-button"),
                UI.Button("Cancel", "reset.cancel", "reset.cancel")
                    .FocusLeft("reset.confirm").Classes("secondary-button")).Classes("reset-actions")),
        "reset.cancel", "reset.page");

    private static WidgetView RenderThemes(
        StackElement header,
        PlatformSettingsDocument settings,
        ThemeCatalogSnapshot themes,
        bool busy)
    {
        var options = new PickerOption[themes.Themes.Count];
        var initialFocus = themes.Themes.Count > 0 ? "theme.item.0" : null;
        for (var index = 0; index < themes.Themes.Count; index++)
        {
            var entry = themes.Themes[index];
            var selected = entry.IsValid &&
                entry.Descriptor.Id == settings.Appearance.ThemeId &&
                entry.Descriptor.Version.ToString() == settings.Appearance.ThemeVersion;
            var diagnostic = entry.Diagnostics.FirstOrDefault()?.Code;
            var label = entry.IsValid
                ? $"{entry.Descriptor.Name}  {entry.Descriptor.Version}"
                : $"Invalid · {entry.Descriptor.Name} · {diagnostic ?? "validation error"}";
            var accessibilityLabel = entry.IsValid
                ? $"{entry.Descriptor.Name}, version {entry.Descriptor.Version}"
                : $"{entry.Descriptor.Name}, invalid theme, {diagnostic ?? "validation error"}";
            options[index] = new PickerOption(
                $"theme.item.{index}",
                label,
                $"theme.select.{index}",
                IsSelected: selected,
                AccessibilityLabel: accessibilityLabel,
                IsDisabled: !entry.IsValid,
                IsBusy: busy);
            if (selected) initialFocus = $"theme.item.{index}";
        }
        var picker = UI.Picker(
                "Choose theme",
                "theme.picker",
                "theme.picker",
                "back",
                options,
                "Themes change the overlay and every widget that uses shared semantic styles.")
            .AddClasses("theme-picker");
        return View(header, picker, initialFocus, "theme.picker");
    }

    private static ScrollElement PageScope(string id, params WidgetElement[] children) =>
        UI.VerticalScroll(id, children).InputScope(id).Shortcut(ControllerButton.B, "back").Classes("settings-page");

    private static WidgetView View(
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

    private static RowElement LinkStepper(RowElement stepper, string? up, string? down, bool busy)
    {
        var children = stepper.Children.Select(child => child switch
        {
            ButtonElement button => button with
            {
                IsBusy = busy ? true : null,
                FocusNeighbors = (button.FocusNeighbors ?? new FocusNeighbors()) with
                {
                    Up = up,
                    Down = down,
                },
            },
            _ => child,
        }).ToArray();
        return stepper with { Children = children };
    }

    private void Navigate(SettingsPage page)
    {
        lock (_stateLock)
        {
            var previousPage = _page;
            _page = page;
            if (page == SettingsPage.Permissions &&
                previousPage != SettingsPage.PermissionDiagnostics)
                _permissionDiagnosticsReturnFocus = false;
        }
        Invalidate();
    }

    private void SetOperation(string status, bool busy, bool error)
    {
        lock (_stateLock)
        {
            _status = status;
            _busy = busy;
            _error = error;
        }
        Invalidate();
    }

    private SettingsPage ParentPage(SettingsPage page) => page switch
    {
        SettingsPage.ThemePicker => SettingsPage.Appearance,
        SettingsPage.AccessibilityVisual => SettingsPage.Accessibility,
        SettingsPage.InstalledWidgetDetails => SettingsPage.InstalledWidgets,
        SettingsPage.InstalledWidgetVersions => SettingsPage.InstalledWidgetDetails,
        SettingsPage.PermissionDiagnostics => SettingsPage.Permissions,
        SettingsPage.PackageCapabilities => PackageCapabilitiesReturnPage(),
        SettingsPage.CapabilityDecision => SettingsPage.PackageCapabilities,
        SettingsPage.Root => SettingsPage.Root,
        _ => SettingsPage.Root,
    };

    private SettingsPage PackageCapabilitiesReturnPage()
    {
        lock (_stateLock) return _packageCapabilitiesReturnPage;
    }

    private static bool TryThemeIndex(string action, out int index)
    {
        index = -1;
        return action.StartsWith("theme.select.", StringComparison.Ordinal) &&
               int.TryParse(action["theme.select.".Length..], NumberStyles.None,
                   CultureInfo.InvariantCulture, out index);
    }

    private static bool TryIndexedAction(string action, string prefix, out int index)
    {
        index = -1;
        return action.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(action[prefix.Length..], NumberStyles.None,
                   CultureInfo.InvariantCulture, out index);
    }

    private static double Step(double current, double delta, double minimum, double maximum) =>
        Math.Clamp(Math.Round(current + delta, 2, MidpointRounding.AwayFromZero), minimum, maximum);

    private static string Percent(double value) =>
        $"{Math.Round(value * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}%";
}
