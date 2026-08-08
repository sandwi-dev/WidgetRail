using GameBarAlternative.FirstPartyWidgets.Settings;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Root keeps widget permissions inside Installed Widgets", RootCategories),
    ("Settings uses a bounded controller-scroll surface", ControllerScrollSurface),
    ("Nested pages own scoped B navigation", NestedScopesAndBack),
    ("Settings composites expose controller semantics", CompositeControls),
    ("Visual accessibility preferences persist through a nested controller scope", VisualAccessibilityPersistence),
    ("Scale and opacity actions persist within bounds", BoundedPersistence),
    ("Theme picker scrolls every valid and invalid package", ThemePickerScroll),
    ("Theme selection atomically pins ID and version", ThemeSelection),
    ("Reset requires confirmation and restores defaults", ResetConfirmation),
    ("Malformed settings recover through safe defaults", InvalidSettingsRecovery),
    ("Saving exposes busy and completion feedback", BusyFeedback),
    ("Diagnostics report invalid theme packages", InvalidThemeDiagnostics),
    ("Runtime diagnostics expose bounded failures and refresh recovery", RuntimeDiagnosticsRecovery),
    ("Activation reloads once per visible lifetime without polling", ActivationLifecycle),
    ("Focus IDs remain stable at setting bounds", StableBoundFocus),
    ("Installed widgets use controller pages and explicit review", InstalledWidgetReview),
    ("Built-in widgets remain visible and read-only without community packages", BuiltInWidgetInventory),
    ("Installed widget enable and disable update catalog state", InstalledWidgetToggle),
    ("Installed widget versions support controller rollback while disabled", InstalledWidgetVersionRollback),
    ("Installed version changes immediately refresh permission authority", InstalledVersionRefreshesPermissions),
    ("Malformed installed widget catalogs fail closed", InstalledWidgetCatalogFailure),
    ("Installed widget review reloads only on activation", InstalledWidgetActivationReload),
    ("Incompatible installed widgets cannot be enabled", IncompatibleInstalledWidget),
    ("Permissions use nested controller-scroll scopes and B-only Back", PermissionScopesAreScrollable),
    ("Permission screens explain declarations consent enforcement and Windows access", PermissionModelIsClear),
    ("App-library copy separates opaque catalog read from exact launch", AppLibraryPermissionCopy),
    ("Community service permissions explain exact loopback and write-only secrets", CommunityServicePermissionCopy),
    ("Spotify permissions use supported human-readable capability metadata", SpotifyPermissionCopy),
    ("Bluetooth permission copy states privacy and radio-control boundaries", BluetoothPermissionCopy),
    ("Capability grant confirms and deny revokes atomically", GrantAndRevoke),
    ("Consent decisions isolate package publisher identities", PublisherIsolation),
    ("Undeclared capabilities and decisions are never actionable", UndeclaredCapabilitiesAreHidden),
    ("Permission diagnostics are bounded sanitized and controller reachable", PermissionDiagnosticsAreBounded),
    ("Truncated permission catalogs suppress inactive-decision classification", TruncatedPermissionCatalogSuppressesClassification),
    ("Malformed catalog and consent fail closed with diagnostics", MalformedPermissionStateFailsClosed),
    ("Retired consent migrates without blocking current permission review", RetiredConsentMigration),
    ("Permission catalog reloads only on activation", PermissionActivationReload),
    ("Bundled first-party capability manifests join permission review", BundledPermissionsAreDiscovered),
    ("First-party packages are never auto-granted", FirstPartyIsNotAutoGranted),
    ("Explicit refresh reloads themes catalog and permissions while visible", ExplicitRefresh),
    ("Manifest and default GBSS validate", ShippedAssetsValidate),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static Task RootCategories()
{
    using var temp = new TemporaryDirectory();
    var widget = Create(temp.Path);
    var snapshot = Snapshot(widget);
    Assert.Equal("settings-root", snapshot.ActiveInputScopeId);
    Assert.Equal("category.appearance", snapshot.InitialFocusId);
    Assert.SequenceEqual(
        ["category.appearance", "category.accessibility", "category.overlay", "category.installed-widgets", "category.diagnostics", "settings.refresh", "category.reset"],
        Buttons(snapshot.Root).Select(button => button.Id));
    Assert.Valid(snapshot);
    return Task.CompletedTask;
}

static async Task ControllerScrollSurface()
{
    using var temp = new TemporaryDirectory();
    var widget = Create(temp.Path);
    var root = Snapshot(widget);
    Assert.Equal(ProtocolConstants.ResponsiveGridVersion, root.ProtocolVersion);
    Assert.Equal(WidgetSurfaceMode.Standard, root.Surface?.Mode);
    Assert.Equal(880d, root.Surface?.PreferredWidth);
    Assert.Equal(520d, root.Surface?.PreferredHeight);
    Assert.Equal(ViewNodeKind.Scroll,
        Nodes(root.Root).Single(node => node.Id == "settings.categories").Kind);
    Assert.Equal(ScrollAxis.Vertical,
        Nodes(root.Root).Single(node => node.Id == "settings.categories").ScrollAxis);
    var categoryGrid = Nodes(root.Root).Single(node => node.Id == "settings.category-grid");
    Assert.Equal(ViewNodeKind.Grid, categoryGrid.Kind);
    Assert.Equal(250d, categoryGrid.GridMinimumColumnWidth);
    Assert.Equal(2, categoryGrid.GridMaximumColumns);
    Assert.True(Buttons(categoryGrid).All(button => button.Focus is null),
        "Responsive category navigation must use final host geometry rather than static edges.");

    await Action(widget, "open.diagnostics");
    var diagnostics = Snapshot(widget);
    var page = Nodes(diagnostics.Root).Single(node => node.Id == "diagnostics.page");
    Assert.Equal(ViewNodeKind.Scroll, page.Kind);
    Assert.Equal(ScrollAxis.Vertical, page.ScrollAxis);
    Assert.Equal("diagnostics.page", page.InputScopeId);
    Assert.True(Nodes(diagnostics.Root).Single(node => node.Id == "diagnostics.schema")
            .StyleClasses.Contains("gbar-code-text", StringComparer.Ordinal),
        "Diagnostics schema should use semantic code text styling.");
    Assert.Valid(diagnostics);
}

static async Task NestedScopesAndBack()
{
    using var temp = new TemporaryDirectory();
    var widget = Create(temp.Path);
    await Action(widget, "open.appearance");
    var appearance = Snapshot(widget);
    Assert.Equal("appearance.page", appearance.ActiveInputScopeId);
    Assert.HasShortcut(appearance.Root, "appearance.page", ControllerButton.B, "back");

    await Action(widget, "open.themes");
    var themes = Snapshot(widget);
    Assert.Equal("theme.picker", themes.ActiveInputScopeId);
    Assert.HasShortcut(themes.Root, "theme.picker", ControllerButton.B, "back");
    await Action(widget, "back");
    Assert.Equal(SettingsPage.Appearance, widget.CurrentPage);
    await Action(widget, "back");
    Assert.Equal(SettingsPage.Root, widget.CurrentPage);
}

static async Task CompositeControls()
{
    using var temp = new TemporaryDirectory();
    var widget = Create(temp.Path);
    await Action(widget, "open.accessibility");
    var snapshot = Snapshot(widget);
    var buttons = Buttons(snapshot.Root).ToDictionary(button => button.Id, StringComparer.Ordinal);
    Assert.Equal("Decrease Text size", buttons["text.stepper.decrement"].AccessibilityLabel);
    Assert.Equal("Increase Text size", buttons["text.stepper.increment"].AccessibilityLabel);
    Assert.Equal("Follow Windows motion: On", buttons["motion.system"].Text);
    Assert.Equal(true, buttons["motion.system"].IsSelected);
    Assert.Equal(null, buttons["motion.system"].Glyph);
    Assert.Equal(null, buttons["motion.reduced"].Glyph);
    Assert.Equal("motion.reduced", buttons["motion.system"].Focus!.Down);
    Assert.Equal("accessibility.visual", buttons["motion.reduced"].Focus!.Down);
    Assert.Valid(snapshot);
}

static async Task VisualAccessibilityPersistence()
{
    using var temp = new TemporaryDirectory();
    var widget = Create(temp.Path);
    await Action(widget, "open.accessibility");
    await Action(widget, "open.visual-accessibility");

    var initial = Snapshot(widget);
    Assert.Equal("accessibility.visual.page", initial.ActiveInputScopeId);
    Assert.Equal("contrast.system", initial.InitialFocusId);
    Assert.HasShortcut(initial.Root, "accessibility.visual.page", ControllerButton.B, "back");
    Assert.Equal(true, Button(initial.Root, "contrast.system").IsSelected);
    Assert.Equal(null, Button(initial.Root, "contrast.system").Glyph);
    Assert.Equal(null, Button(initial.Root, "contrast.high").Glyph);
    Assert.Equal(null, Button(initial.Root, "bold-text.toggle").Glyph);
    Assert.Equal(null, Button(initial.Root, "transparency.reduced").Glyph);
    Assert.Equal("contrast.high", Button(initial.Root, "contrast.system").Focus!.Down);
    Assert.Equal("bold-text.toggle", Button(initial.Root, "contrast.high").Focus!.Down);
    Assert.Equal("transparency.reduced", Button(initial.Root, "bold-text.toggle").Focus!.Down);
    Assert.Valid(initial);

    await Action(widget, "contrast.high");
    await Action(widget, "bold-text.toggle");
    await Action(widget, "transparency.reduced");
    var saved = await Store(temp.Path).LoadAsync();
    Assert.Equal(ContrastPreference.High, saved.Appearance.Contrast);
    Assert.Equal(true, saved.Appearance.BoldText);
    Assert.Equal(TransparencyPreference.Reduced, saved.Appearance.Transparency);

    var updated = Snapshot(widget);
    Assert.True(Button(updated.Root, "contrast.system").IsSelected is not true,
        "System contrast remained selected after choosing high contrast.");
    Assert.Equal(true, Button(updated.Root, "contrast.high").IsSelected);
    Assert.Equal(true, Button(updated.Root, "bold-text.toggle").IsSelected);
    Assert.Equal(true, Button(updated.Root, "transparency.reduced").IsSelected);
    await Action(widget, "back");
    Assert.Equal(SettingsPage.Accessibility, widget.CurrentPage);
}

static async Task BoundedPersistence()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    await store.ReplaceAsync(PlatformSettingsDocument.Default with
    {
        Appearance = PlatformSettingsDocument.Default.Appearance with
        {
            TextScale = AppearanceSettings.MaximumTextScale,
            InterfaceScale = AppearanceSettings.MinimumInterfaceScale,
            BackdropOpacity = AppearanceSettings.MaximumBackdropOpacity,
        },
    });
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "text.increase");
    await Action(widget, "interface.decrease");
    await Action(widget, "opacity.increase");
    var saved = await store.LoadAsync();
    Assert.Equal(AppearanceSettings.MaximumTextScale, saved.Appearance.TextScale);
    Assert.Equal(AppearanceSettings.MinimumInterfaceScale, saved.Appearance.InterfaceScale);
    Assert.Equal(AppearanceSettings.MaximumBackdropOpacity, saved.Appearance.BackdropOpacity);

    await Action(widget, "text.decrease");
    await Action(widget, "interface.increase");
    await Action(widget, "opacity.decrease");
    saved = await store.LoadAsync();
    Assert.Equal(1.45D, saved.Appearance.TextScale);
    Assert.Equal(0.85D, saved.Appearance.InterfaceScale);
    Assert.Equal(0.75D, saved.Appearance.BackdropOpacity);
}

static async Task ThemePickerScroll()
{
    using var temp = new TemporaryDirectory();
    for (var index = 0; index < 6; index++)
        WriteTheme(temp.Path, $"dev.test.theme{index}", "Theme " + index, "1.0.0", valid: true);
    WriteTheme(temp.Path, "dev.test.invalid", "Broken", "1.0.0", valid: false);
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "open.appearance");
    await Action(widget, "open.themes");
    var snapshot = Snapshot(widget);
    var options = Buttons(snapshot.Root)
        .Where(button => button.Id.StartsWith("theme.item.", StringComparison.Ordinal))
        .ToArray();
    Assert.True(options.Length >= 7,
        "Theme picker did not expose every installed test theme in one Scroll.");
    for (var index = 0; index < 6; index++)
        Assert.True(options.Any(button =>
                button.Text!.StartsWith($"Theme {index}", StringComparison.Ordinal)),
            $"Theme {index} was omitted from the Picker.");
    Assert.Equal(ViewNodeKind.Scroll,
        Nodes(snapshot.Root).Single(node => node.Id == "theme.picker.options").Kind);
    Assert.True(options.Any(button => button.IsDisabled is true),
        "Invalid theme was not visible and disabled.");
    Assert.True(!Scope(snapshot.Root, "theme.picker").Shortcuts.Any(item =>
            item.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper),
        "Theme selection must not consume bumpers for pagination.");
    Assert.HasShortcut(snapshot.Root, "theme.picker", ControllerButton.B, "back");
    Assert.Valid(snapshot);
}

static async Task ThemeSelection()
{
    using var temp = new TemporaryDirectory();
    WriteTheme(temp.Path, "dev.test.slate", "Slate", "2.3.4", valid: true);
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "open.appearance");
    await Action(widget, "open.themes");
    var snapshot = Snapshot(widget);
    var target = Buttons(snapshot.Root).Single(button => button.Text!.StartsWith("Slate", StringComparison.Ordinal));
    await Action(widget, target.ActionId!);
    var saved = await Store(temp.Path).LoadAsync();
    Assert.Equal("dev.test.slate", saved.Appearance.ThemeId);
    Assert.Equal("2.3.4", saved.Appearance.ThemeVersion);
    var selected = Buttons(Snapshot(widget).Root).Single(button => button.Id == target.Id);
    Assert.Equal(true, selected.IsSelected);
}

static async Task ResetConfirmation()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    await store.ReplaceAsync(PlatformSettingsDocument.Default with
    {
        Appearance = PlatformSettingsDocument.Default.Appearance with { TextScale = 1.3 },
    });
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "open.reset");
    Assert.Equal(SettingsPage.Reset, widget.CurrentPage);
    Assert.Equal(1.3D, (await store.LoadAsync()).Appearance.TextScale);
    await Action(widget, "reset.confirm");
    Assert.Equal(PlatformSettingsDocument.Default, await store.LoadAsync());
    Assert.Equal(SettingsPage.Root, widget.CurrentPage);
}

static async Task InvalidSettingsRecovery()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    await File.WriteAllTextAsync(paths.SettingsFile, "{ invalid");
    var widget = Create(temp.Path);
    await Activate(widget);
    var snapshot = Snapshot(widget);
    Assert.Contains("invalid", Text(snapshot.Root, "settings.status").Text!);
    Assert.True(Text(snapshot.Root, "settings.status").StyleClasses.Contains("is-error"),
        "Invalid settings did not expose error styling.");
    await Action(widget, "open.reset");
    await Action(widget, "reset.confirm");
    Assert.Equal(PlatformSettingsDocument.Default, await Store(temp.Path).LoadAsync());
}

static async Task BusyFeedback()
{
    using var temp = new TemporaryDirectory();
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "open.accessibility");
    var lockPath = Path.Combine(temp.Path, ".platform-settings.lock");
    await using var heldLock = new FileStream(
        lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var save = widget.OnActionAsync(new WidgetActionEvent("motion.reduced", "motion.reduced")).AsTask();
    await Task.Delay(20);
    var busy = Snapshot(widget);
    Assert.Contains("Saving", Text(busy.Root, "settings.status").Text!);
    Assert.Equal(true, Button(busy.Root, "motion.reduced").IsBusy);
    await heldLock.DisposeAsync();
    await save;
    var complete = Snapshot(widget);
    Assert.Contains("saved", Text(complete.Root, "settings.status").Text!);
    Assert.True(Button(complete.Root, "motion.reduced").IsBusy is not true,
        "Busy state remained after persistence completed.");
}

static async Task InvalidThemeDiagnostics()
{
    using var temp = new TemporaryDirectory();
    WriteTheme(temp.Path, "dev.test.invalid", "Broken", "1.0.0", valid: false);
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "open.diagnostics");
    var snapshot = Snapshot(widget);
    Assert.Contains("1 invalid", Text(snapshot.Root, "diagnostics.themes").Text!);
    Assert.Valid(snapshot);
}

static async Task RuntimeDiagnosticsRecovery()
{
    using var temp = new TemporaryDirectory();
    var degraded = new PlatformDiagnosticsSnapshot(
        PlatformDiagnosticsSnapshot.CurrentSchemaVersion,
        7,
        PlatformDiagnosticsSnapshot.Area("bridge", "Bridge", PlatformDiagnosticState.Healthy,
            "Native host session is connected"),
        PlatformDiagnosticsSnapshot.Area("catalog", "Widget catalog", PlatformDiagnosticState.Degraded,
            "Revision 4; retained last good after a rejected reload"),
        PlatformDiagnosticsSnapshot.Area("appearance", "Appearance", PlatformDiagnosticState.Degraded,
            "Revision 3; retained last good after 1 errors"),
        PlatformDiagnosticsSnapshot.Area("providers", "Platform providers", PlatformDiagnosticState.Healthy,
            "Audio and network providers are available on demand"),
        PlatformDiagnosticsSnapshot.Area("consent", "Permissions", PlatformDiagnosticState.Healthy,
            "Revision 2; 1 decisions; 1 denied"),
        PlatformDiagnosticsSnapshot.Area("overlay", "Overlay host", PlatformDiagnosticState.Unavailable,
            "Host telemetry is not reported by this build"),
        PlatformDiagnosticsSnapshot.Area("guide", "Guide input", PlatformDiagnosticState.Unavailable,
            "Host telemetry is not reported by this build"),
        [new PlatformWorkerDiagnostic(
            "audio-mixer", "Audio Mixer", false, 3, "ProcessExited", true)]);
    var recovered = degraded with
    {
        Revision = 8,
        Catalog = PlatformDiagnosticsSnapshot.Area(
            "catalog", "Widget catalog", PlatformDiagnosticState.Healthy,
            "Revision 5; 4 widgets validated"),
        Appearance = PlatformDiagnosticsSnapshot.Area(
            "appearance", "Appearance", PlatformDiagnosticState.Healthy,
            "Revision 4; active theme validated"),
        Workers = [new PlatformWorkerDiagnostic(
            "audio-mixer", "Audio Mixer", true, 4, null, false)],
    };
    var service = new SequenceDiagnosticsService(degraded, recovered);
    var paths = new PlatformSettingsPaths(temp.Path);
    var widget = new SettingsWidget(
        new PlatformSettingsStore(paths), new ThemeCatalog(paths), diagnostics: service);
    await Activate(widget);
    await Action(widget, "open.diagnostics");

    var initial = Snapshot(widget);
    Assert.Equal("diagnostics.page", initial.ActiveInputScopeId);
    Assert.Equal("diagnostics.refresh", initial.InitialFocusId);
    Assert.HasShortcut(initial.Root, "diagnostics.page", ControllerButton.B, "back");
    Assert.Contains("retained last good", Text(initial.Root, "diagnostics.area.catalog").Text!);
    Assert.Contains("1 denied", Text(initial.Root, "diagnostics.area.consent").Text!);
    Assert.Contains("not reported", Text(initial.Root, "diagnostics.area.guide").Text!);
    Assert.Contains("ProcessExited", Text(initial.Root, "diagnostics.worker.audio-mixer").Text!);
    Assert.Equal("diagnostics.back", Button(initial.Root, "diagnostics.refresh").Focus!.Down);
    Assert.Equal("diagnostics.refresh", Button(initial.Root, "diagnostics.back").Focus!.Up);
    Assert.Valid(initial);

    await Action(widget, "refresh");
    var refreshed = Snapshot(widget);
    Assert.Contains("4 widgets validated", Text(refreshed.Root, "diagnostics.area.catalog").Text!);
    Assert.Contains("1/1 running; 0", Text(refreshed.Root, "diagnostics.workers").Text!);
    Assert.True(!Nodes(refreshed.Root).Any(node => node.Id == "diagnostics.worker.audio-mixer"),
        "Recovered worker retained a stale failure row.");
    Assert.Equal(2, service.RequestCount);
    Assert.Valid(refreshed);
}

static async Task ActivationLifecycle()
{
    using var temp = new TemporaryDirectory();
    var widget = Create(temp.Path);
    await widget.InitializeAsync(CancellationToken.None);
    Assert.Equal(0, widget.ActivationLoadCount);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    Assert.Equal(1, widget.ActivationLoadCount);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, CancellationToken.None);
    Assert.Equal(1, widget.ActivationLoadCount);
    await Task.Delay(80);
    Assert.Equal(1, widget.ActivationLoadCount);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    Assert.Equal(2, widget.ActivationLoadCount);
}

static async Task StableBoundFocus()
{
    using var temp = new TemporaryDirectory();
    await Store(temp.Path).ReplaceAsync(PlatformSettingsDocument.Default with
    {
        Appearance = PlatformSettingsDocument.Default.Appearance with
        {
            TextScale = AppearanceSettings.MinimumTextScale,
            InterfaceScale = AppearanceSettings.MaximumInterfaceScale,
            BackdropOpacity = AppearanceSettings.MinimumBackdropOpacity,
        },
    });
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "open.accessibility");
    var accessibility = Snapshot(widget);
    Assert.Equal("text.stepper.decrement", accessibility.InitialFocusId);
    Assert.Equal(true, Button(accessibility.Root, "text.stepper.decrement").IsDisabled);
    Assert.True(Button(accessibility.Root, "text.stepper.increment") is not null, "Increment ID disappeared at bound.");
    Assert.Valid(accessibility);
    await Action(widget, "back");
    await Action(widget, "open.overlay");
    var overlay = Snapshot(widget);
    Assert.Equal(true, Button(overlay.Root, "interface.stepper.increment").IsDisabled);
    Assert.Equal(true, Button(overlay.Root, "opacity.stepper.decrement").IsDisabled);
    Assert.Valid(overlay);
}

static async Task InstalledWidgetReview()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    for (var index = 0; index < 6; index++)
        WriteInstalledWidget(catalogRoot, $"dev.test.installed{index}", $"dev.publisher{index}",
            $"Installed {index}", [PlatformCapabilities.AudioSessionsReadV1],
            [PlatformCapabilities.AudioSessionsControlV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.installed-widgets");

    var first = Snapshot(widget);
    Assert.Equal("installed.widgets", first.ActiveInputScopeId);
    Assert.HasShortcut(first.Root, "installed.widgets", ControllerButton.B, "back");
    Assert.HasShortcut(first.Root, "installed.widgets", ControllerButton.RightBumper,
        "installed.next-page");
    Assert.Equal(5, Buttons(first.Root).Count(button =>
        button.Id.StartsWith("installed.item.", StringComparison.Ordinal)));
    Assert.Contains("review required", Button(first.Root, "installed.item.0").Text!);

    await Action(widget, "installed.next-page");
    var second = Snapshot(widget);
    Assert.Equal("installed.item.5", second.InitialFocusId);
    Assert.HasShortcut(second.Root, "installed.widgets", ControllerButton.LeftBumper,
        "installed.previous-page");
    await Action(widget, "installed.select.5");
    var details = Snapshot(widget);
    Assert.Equal(SettingsPage.InstalledWidgetDetails, widget.CurrentPage);
    Assert.Equal("installed.details", details.ActiveInputScopeId);
    Assert.HasShortcut(details.Root, "installed.details", ControllerButton.B, "back");
    Assert.Contains("dev.test.installed5", Text(details.Root, "installed.details.id").Text!);
    Assert.Contains("dev.publisher5", Text(details.Root, "installed.details.publisher").Text!);
    Assert.Contains(PlatformCapabilities.AudioSessionsReadV1,
        Text(details.Root, "installed.details.required-permissions").Text!);
    Assert.Contains(PlatformCapabilities.AudioSessionsControlV1,
        Text(details.Root, "installed.details.optional-permissions").Text!);
    Assert.Contains("Suspend when hidden",
        Text(details.Root, "installed.details.residency").Text!);
    Assert.Contains("no process/thread suspension",
        Text(details.Root, "installed.details.residency").Text!);
    Assert.Contains("Enable reviewed widget", Button(details.Root, "installed.details.toggle").Text!);
    Assert.Contains("Permissions & configuration",
        Button(details.Root, "installed.details.permissions").Text!);

    await Action(widget, "installed.permissions.open");
    var permissions = Snapshot(widget);
    Assert.Equal(SettingsPage.PackageCapabilities, widget.CurrentPage);
    Assert.Equal("capabilities.package", permissions.ActiveInputScopeId);
    Assert.HasShortcut(permissions.Root, "capabilities.package", ControllerButton.B, "back");
    Assert.Contains("Installed 5", Text(permissions.Root, "capabilities.heading").Text!);
    await Action(widget, "back");
    Assert.Equal(SettingsPage.InstalledWidgetDetails, widget.CurrentPage);
    Assert.Valid(first);
    Assert.Valid(second);
    Assert.Valid(details);
    Assert.Valid(permissions);
}

static async Task BuiltInWidgetInventory()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var bundledRoot = Path.Combine(temp.Path, "runtime");
    WriteBundledWidget(
        bundledRoot,
        "AudioMixer",
        "org.gbar.firstparty.audio-mixer",
        "org.gbar.firstparty",
        "Audio Mixer",
        [PlatformCapabilities.AudioSessionsReadV1],
        [PlatformCapabilities.AudioSessionsControlV1]);
    WriteBundledWidget(
        bundledRoot,
        "MediaSessions",
        "org.gbar.firstparty.media-sessions",
        "org.gbar.firstparty",
        "Now Playing",
        [PlatformCapabilities.MediaSessionsReadV1],
        [PlatformCapabilities.MediaSessionsControlV1]);
    var audioManifest = Path.Combine(bundledRoot, "AudioMixer", "manifest.json");
    var mediaManifest = Path.Combine(bundledRoot, "MediaSessions", "manifest.json");
    var audioBefore = await File.ReadAllBytesAsync(audioManifest);
    var mediaBefore = await File.ReadAllBytesAsync(mediaManifest);

    var widget = CreateWithPermissions(
        temp.Path,
        catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")),
        bundledRoot);
    await Activate(widget);
    await Action(widget, "open.installed-widgets");

    var list = Snapshot(widget);
    Assert.Equal(ViewNodeKind.Scroll,
        Nodes(list.Root).Single(node => node.Id == "installed.widgets").Kind);
    Assert.Equal(2, Buttons(list.Root).Count(button =>
        button.Id.StartsWith("installed.builtin.item.", StringComparison.Ordinal)));
    Assert.Contains("Built-in", Button(list.Root, "installed.builtin.item.0").Text!);
    Assert.Contains("Audio Mixer", Button(list.Root, "installed.builtin.item.0").Text!);
    Assert.Contains("Now Playing", Button(list.Root, "installed.builtin.item.1").Text!);
    Assert.True(!Buttons(list.Root).Any(button =>
        button.Id.StartsWith("installed.item.", StringComparison.Ordinal)),
        "Empty community catalog exposed a community package row.");

    await Action(widget, "installed.builtin.select.0");
    var details = Snapshot(widget);
    Assert.Equal(SettingsPage.InstalledWidgetDetails, widget.CurrentPage);
    Assert.Contains("Built-in", Text(details.Root, "installed.details.source").Text!);
    Assert.Contains("cannot be disabled or version-managed",
        Text(details.Root, "installed.details.status").Text!);
    Assert.True(!Buttons(details.Root).Any(button =>
        button.ActionId is "installed.toggle" or "installed.versions.open"),
        "Built-in details exposed a package-management action.");

    await Action(widget, "installed.toggle");
    await Action(widget, "installed.versions.open");
    await Action(widget, "installed.version.select.0");
    Assert.Equal(SettingsPage.InstalledWidgetDetails, widget.CurrentPage);
    Assert.SequenceEqual(audioBefore, await File.ReadAllBytesAsync(audioManifest));
    Assert.SequenceEqual(mediaBefore, await File.ReadAllBytesAsync(mediaManifest));
    Assert.True(!File.Exists(Path.Combine(catalogRoot, "catalog-state.json")),
        "A forged built-in management action mutated community catalog state.");
    Assert.Valid(list);
    Assert.Valid(details);
}

static async Task InstalledWidgetToggle()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    const string widgetId = "dev.test.toggle";
    WriteInstalledWidget(catalogRoot, widgetId, "dev.publisher.toggle", "Toggle",
        [PlatformCapabilities.NetworkReadV1], []);
    var catalog = new WidgetCatalog(catalogRoot);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.installed-widgets");
    await Action(widget, "installed.select.0");
    await Action(widget, "installed.toggle");
    Assert.Equal(true, (await catalog.DiscoverAsync()).Widgets.Single().Enabled);
    var enabled = Snapshot(widget);
    Assert.Contains("Disable widget", Button(enabled.Root, "installed.details.toggle").Text!);
    Assert.Contains("Toggle enabled", Text(enabled.Root, "settings.status").Text!);

    await Action(widget, "installed.toggle");
    Assert.Equal(false, (await catalog.DiscoverAsync()).Widgets.Single().Enabled);
    var disabled = Snapshot(widget);
    Assert.Contains("Enable reviewed widget", Button(disabled.Root, "installed.details.toggle").Text!);
    Assert.Contains("Toggle disabled", Text(disabled.Root, "settings.status").Text!);
    Assert.Valid(enabled);
    Assert.Valid(disabled);
}

static async Task InstalledWidgetVersionRollback()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    const string widgetId = "dev.test.versions";
    foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0" })
        WriteInstalledWidget(catalogRoot, widgetId, "dev.publisher.versions", "Versions",
            [], [], version: version);

    var catalog = new WidgetCatalog(catalogRoot);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.installed-widgets");
    await Action(widget, "installed.select.0");
    var details = Snapshot(widget);
    Assert.Contains("Manage versions (3)", Button(details.Root, "installed.details.versions").Text!);

    await Action(widget, "installed.versions.open");
    var versions = Snapshot(widget);
    Assert.Equal(SettingsPage.InstalledWidgetVersions, widget.CurrentPage);
    Assert.Equal("installed.versions", versions.ActiveInputScopeId);
    Assert.HasShortcut(versions.Root, "installed.versions", ControllerButton.B, "back");
    Assert.Equal("installed.version.item.1", versions.InitialFocusId);
    Assert.Equal(true, Button(versions.Root, "installed.version.item.0").IsSelected);
    Assert.Equal(true, Button(versions.Root, "installed.version.item.0").IsDisabled);
    Assert.Contains("Rollback · 2.0.0", Button(versions.Root, "installed.version.item.1").Text!);

    await Action(widget, "installed.version.select.1");
    Assert.Equal("2.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());
    var rolledBack = Snapshot(widget);
    Assert.Equal(true, Button(rolledBack.Root, "installed.version.item.1").IsSelected);
    Assert.Contains("Select newer · 3.0.0", Button(rolledBack.Root, "installed.version.item.0").Text!);
    Assert.Contains("2.0.0 selected; review before enabling", Text(rolledBack.Root, "settings.status").Text!);

    await Action(widget, "back");
    await Action(widget, "installed.toggle");
    await Action(widget, "installed.versions.open");
    var enabled = Snapshot(widget);
    Assert.Equal("installed.versions.back", enabled.InitialFocusId);
    Assert.True(Buttons(enabled.Root)
        .Where(button => button.Id.StartsWith("installed.version.item.", StringComparison.Ordinal))
        .All(button => button.IsDisabled is true), "Enabled widget exposed a version-selection action.");
    await Action(widget, "installed.version.select.2");
    Assert.Equal("2.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());
    Assert.Contains("Disable the widget", Text(Snapshot(widget).Root, "settings.status").Text!);
    Assert.Valid(details);
    Assert.Valid(versions);
    Assert.Valid(rolledBack);
    Assert.Valid(enabled);
}

static async Task InstalledVersionRefreshesPermissions()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    const string widgetId = "dev.test.permission-version";
    WriteInstalledWidget(catalogRoot, widgetId, "dev.publisher.versioned", "Versioned permissions",
        [PlatformCapabilities.AudioSessionsReadV1], [], version: "1.0.0");
    WriteInstalledWidget(catalogRoot, widgetId, "dev.publisher.versioned", "Versioned permissions",
        [PlatformCapabilities.NetworkReadV1], [], version: "2.0.0");

    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    await Activate(widget);
    await Action(widget, "open.installed-widgets");
    await Action(widget, "installed.select.0");
    await Action(widget, "installed.versions.open");
    await Action(widget, "installed.version.select.1");

    await Action(widget, "back");
    await Action(widget, "back");
    await Action(widget, "back");
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    Assert.Equal(1, Buttons(capabilities.Root).Count(button =>
        button.Id.StartsWith("capability.item.", StringComparison.Ordinal)));
    Assert.Contains("Read audio sessions", Button(capabilities.Root, "capability.item.0").Text!);
    Assert.True(!Nodes(capabilities.Root).Any(node =>
            node.Text?.Contains("Read network status", StringComparison.Ordinal) == true),
        "Permissions retained declarations from the previously active version.");

    await Action(widget, "capability.select.0");
    await Action(widget, "capability.grant");
    Assert.Equal(ConsentDecision.Grant, await consent.GetDecisionAsync(
        new(widgetId, InstalledAuthority(catalogRoot, widgetId, "1.0.0"), "test"),
        PlatformCapabilities.AudioSessionsReadV1));
    Assert.Equal((ConsentDecision?)null, await consent.GetDecisionAsync(
        new(widgetId, InstalledAuthority(catalogRoot, widgetId, "2.0.0"), "test"),
        PlatformCapabilities.NetworkReadV1));
    Assert.Valid(capabilities);
}

static async Task ExplicitRefresh()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    await Activate(widget);

    WriteTheme(temp.Path, "dev.test.refreshed", "Refreshed", "1.0.0", valid: true);
    WriteInstalledWidget(catalogRoot, "dev.test.refreshed-widget", "dev.publisher.refreshed",
        "Refreshed widget", [PlatformCapabilities.NetworkReadV1], []);

    await Action(widget, "refresh");
    Assert.Contains("Settings refreshed", Text(Snapshot(widget).Root, "settings.status").Text!);
    await Action(widget, "open.appearance");
    await Action(widget, "open.themes");
    Assert.True(Buttons(Snapshot(widget).Root).Any(button =>
            button.Text?.Contains("Refreshed", StringComparison.Ordinal) == true),
        "Explicit refresh did not reload theme discovery.");
    await Action(widget, "back");
    await Action(widget, "back");
    await Action(widget, "open.installed-widgets");
    Assert.Contains("Refreshed widget", Button(Snapshot(widget).Root, "installed.item.0").Text!);
    await Action(widget, "back");
    await Action(widget, "open.permissions");
    Assert.Contains("Refreshed widget", Button(Snapshot(widget).Root, "permission.item.0").Text!);
}

static async Task InstalledWidgetCatalogFailure()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var package = Path.Combine(catalogRoot, "packages", "dev.test.bad", "1.0.0");
    Directory.CreateDirectory(package);
    await File.WriteAllTextAsync(Path.Combine(package, "manifest.json"), "{ invalid");
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.installed-widgets");
    var snapshot = Snapshot(widget);
    Assert.Contains("invalid_manifest", Text(snapshot.Root, "installed.help").Text!);
    Assert.True(!Buttons(snapshot.Root).Any(button =>
        button.Id.StartsWith("installed.item.", StringComparison.Ordinal)),
        "Malformed catalog exposed an installed-widget action.");
    Assert.Equal("installed.back", snapshot.InitialFocusId);
    Assert.Valid(snapshot);
}

static async Task InstalledWidgetActivationReload()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.installed-widgets");
    Assert.True(!Buttons(Snapshot(widget).Root).Any(button =>
        button.Id.StartsWith("installed.item.", StringComparison.Ordinal)),
        "Empty catalog unexpectedly contained an installed package.");

    WriteInstalledWidget(catalogRoot, "dev.test.later-review", "dev.publisher.later", "Later",
        [PlatformCapabilities.NetworkReadV1], []);
    await Task.Delay(80);
    Assert.True(!Buttons(Snapshot(widget).Root).Any(button =>
        button.Id.StartsWith("installed.item.", StringComparison.Ordinal)),
        "Settings polled the package catalog while already visible.");

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    Assert.True(Buttons(Snapshot(widget).Root).Any(button => button.Id == "installed.item.0"),
        "Installed package review did not reload on the next activation.");
}

static async Task IncompatibleInstalledWidget()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    WriteInstalledWidget(catalogRoot, "dev.test.incompatible", "dev.publisher.future", "Future",
        [], [], hostApi: new HostApiRange("2.0", 2));
    var catalog = new WidgetCatalog(catalogRoot);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.installed-widgets");
    Assert.Contains("Incompatible", Button(Snapshot(widget).Root, "installed.item.0").Text!);
    await Action(widget, "installed.select.0");
    var details = Snapshot(widget);
    Assert.Equal(true, Button(details.Root, "installed.details.toggle").IsDisabled);
    Assert.Equal("installed.details.back", details.InitialFocusId);
    Assert.Contains("Requires host API 2", Text(details.Root, "installed.details.compatibility").Text!);
    await Action(widget, "installed.toggle");
    Assert.Equal(false, (await catalog.DiscoverAsync()).Widgets.Single().Enabled);
    Assert.Valid(details);
}

static async Task PermissionScopesAreScrollable()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    for (var index = 0; index < 5; index++)
        WriteInstalledWidget(catalogRoot, $"dev.test.widget{index}", $"dev.publisher{index}",
            $"Widget {index}", [PlatformCapabilities.AudioSessionsReadV1],
            [PlatformCapabilities.AudioSessionsControlV1]);
    WriteInstalledWidget(catalogRoot, "dev.test.widget5", "dev.publisher5", "Widget 5",
        [
            PlatformCapabilities.AudioOutputReadV1,
            PlatformCapabilities.AudioSessionsReadV1,
            PlatformCapabilities.AudioDevicesReadV1,
            PlatformCapabilities.AudioInputReadV1,
        ],
        [
            PlatformCapabilities.AudioOutputControlV1,
            PlatformCapabilities.AudioSessionsControlV1,
            PlatformCapabilities.AudioInputControlV1,
        ]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot, new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    var packages = Snapshot(widget);
    Assert.Equal("permissions.packages", packages.ActiveInputScopeId);
    Assert.Equal(ViewNodeKind.Scroll, Scope(packages.Root, "permissions.packages").Kind);
    Assert.HasShortcut(packages.Root, "permissions.packages", ControllerButton.B, "back");
    Assert.True(!Scope(packages.Root, "permissions.packages").Shortcuts.Any(shortcut =>
        shortcut.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper),
        "Permission package scrolling retained hidden page shortcuts.");
    Assert.Equal(6, Buttons(packages.Root).Count(button =>
        button.Id.StartsWith("permission.item.", StringComparison.Ordinal)));
    Assert.Equal("permission.item.0", packages.InitialFocusId);
    Assert.True(!Nodes(packages.Root).Any(node => node.Id == "permissions.page-label"),
        "Permission package paging label remained visible.");
    Assert.True(!Buttons(packages.Root).Any(button => button.ActionId == "back"),
        "Permission package page rendered a redundant Back button.");

    await Action(widget, "permission.select.5");
    var capabilities = Snapshot(widget);
    Assert.Equal(SettingsPage.PackageCapabilities, widget.CurrentPage);
    Assert.Equal("capabilities.package", capabilities.ActiveInputScopeId);
    Assert.Equal(ViewNodeKind.Scroll, Scope(capabilities.Root, "capabilities.package").Kind);
    Assert.HasShortcut(capabilities.Root, "capabilities.package", ControllerButton.B, "back");
    Assert.Equal(7, Buttons(capabilities.Root).Count(button =>
        button.Id.StartsWith("capability.item.", StringComparison.Ordinal)));
    Assert.True(!Nodes(capabilities.Root).Any(node => node.Id == "capabilities.page-label"),
        "Capability paging label remained visible.");
    Assert.True(!Buttons(capabilities.Root).Any(button => button.ActionId == "back"),
        "Capability list rendered a redundant Back button.");
    Assert.Contains("Required", Button(capabilities.Root, "capability.item.0").Text!);
    Assert.Contains("Optional", Button(capabilities.Root, "capability.item.4").Text!);
    Assert.Contains("Not decided", Button(capabilities.Root, "capability.item.0").Text!);

    await Action(widget, "capability.select.6");
    var decision = Snapshot(widget);
    Assert.Equal("capability.decision", decision.ActiveInputScopeId);
    Assert.Equal(ViewNodeKind.Scroll, Scope(decision.Root, "capability.decision").Kind);
    Assert.HasShortcut(decision.Root, "capability.decision", ControllerButton.B, "back");
    Assert.True(Buttons(decision.Root).Any(button => button.Id == "capability.grant"),
        "Grant confirmation action is missing.");
    Assert.True(!Buttons(decision.Root).Any(button => button.ActionId == "back"),
        "Capability decision rendered a redundant Back button.");
    await Action(widget, "back");
    Assert.Equal(SettingsPage.PackageCapabilities, widget.CurrentPage);
    var restoredCapabilities = Snapshot(widget);
    Assert.True(restoredCapabilities.InitialFocusId == "capability.item.6",
        "Capability Back did not restore the previously selected capability.");
    await Action(widget, "back");
    Assert.Equal(SettingsPage.Permissions, widget.CurrentPage);
    var restoredPackages = Snapshot(widget);
    Assert.True(restoredPackages.InitialFocusId == "permission.item.5",
        "Permission Back did not restore the previously selected package.");
    Assert.Valid(packages);
    Assert.Valid(capabilities);
    Assert.Valid(decision);
}

static async Task PermissionModelIsClear()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    WriteInstalledWidget(catalogRoot, "dev.test.network", "dev.publisher.network", "Network helper",
        [PlatformCapabilities.NetworkWifiReadV1],
        [PlatformCapabilities.NetworkWifiConnectV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);

    await Action(widget, "open.permissions");
    var packages = Snapshot(widget);
    Assert.Contains("request", Text(packages.Root, "permissions.help").Text!);
    Assert.Contains("not permission", Text(packages.Root, "permissions.help").Text!);
    Assert.Contains("host enforces", Text(packages.Root, "permissions.help").Text!);
    Assert.Contains("Windows may separately require", Text(packages.Root, "permissions.system-help").Text!);
    Assert.Contains("1 required · 1 optional", Button(packages.Root, "permission.item.0").Text!);

    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    Assert.Contains("Neither type is allowed automatically",
        Text(capabilities.Root, "capabilities.help").Text!);
    Assert.Contains("Required", Button(capabilities.Root, "capability.item.0").Text!);
    Assert.Contains("access is blocked", Button(capabilities.Root, "capability.item.0").Text!);
    Assert.Contains("Optional", Button(capabilities.Root, "capability.item.1").Text!);

    await Action(widget, "capability.select.0");
    var decision = Snapshot(widget);
    Assert.Contains("Windows precise-location permission",
        Text(decision.Root, "capability.description").Text!);
    Assert.Contains("widget identity", Text(decision.Root, "capability.enforcement").Text!);
    Assert.Contains(PlatformCapabilities.NetworkWifiReadV1,
        Text(decision.Root, "capability.technical-id").Text!);
    Assert.Equal("Allow access", Button(decision.Root, "capability.grant").Text);
    Assert.Equal("Block access", Button(decision.Root, "capability.deny").Text);

    await Action(widget, "back");
    await Action(widget, "capability.select.1");
    var optional = Snapshot(widget);
    Assert.Contains("cannot read saved passwords",
        Text(optional.Root, "capability.description").Text!);
    Assert.Valid(packages);
    Assert.Valid(capabilities);
    Assert.Valid(decision);
    Assert.Valid(optional);
}

static async Task SpotifyPermissionCopy()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    WriteInstalledWidget(
        catalogRoot,
        "org.gbar.samples.spotify",
        "org.gbar.samples",
        "Spotify",
        [
            PlatformCapabilities.SpotifyConfigurationV1,
            PlatformCapabilities.SpotifyAuthorizationV1,
            PlatformCapabilities.SpotifyPlaybackReadV1,
        ],
        [PlatformCapabilities.SpotifyPlaybackControlV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");

    var capabilities = Snapshot(widget);
    var labels = Buttons(capabilities.Root)
        .Where(button => button.Id.StartsWith("capability.item.", StringComparison.Ordinal))
        .Select(button => button.Text ?? string.Empty).ToArray();
    Assert.Equal(4, labels.Length);
    Assert.True(labels.Any(label => label.Contains(
        "Use Spotify developer configuration", StringComparison.Ordinal)),
        "Spotify configuration capability name was not rendered.");
    Assert.True(labels.Any(label => label.Contains(
        "Connect a Spotify account", StringComparison.Ordinal)),
        "Spotify authorization capability name was not rendered.");
    Assert.True(labels.Any(label => label.Contains(
        "Read Spotify playback", StringComparison.Ordinal)),
        "Spotify playback-read capability name was not rendered.");
    Assert.True(labels.Any(label => label.Contains(
        "Control Spotify playback", StringComparison.Ordinal)),
        "Spotify playback-control capability name was not rendered.");
    Assert.True(labels.All(label => !label.Contains(
        "Unsupported capability", StringComparison.Ordinal)),
        "A declared Spotify capability fell back to unsupported copy.");
    Assert.Valid(capabilities);
}

static async Task AppLibraryPermissionCopy()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    WriteInstalledWidget(catalogRoot, "dev.test.launcher", "dev.publisher.launcher", "Launcher",
        [PlatformCapabilities.AppLibraryReadV1], [PlatformCapabilities.AppLibraryLaunchV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    Assert.Contains("See installed apps", Button(capabilities.Root, "capability.item.0").Text!);
    await Action(widget, "capability.select.0");
    var decision = Snapshot(widget);
    var description = Text(decision.Root, "capability.description").Text!;
    Assert.Contains("opaque IDs", description);
    Assert.Contains("never receive paths", description);
    Assert.Contains("launch authority", description);
    await Action(widget, "back");
    var launchCapabilities = Snapshot(widget);
    Assert.Contains("Launch installed apps",
        Button(launchCapabilities.Root, "capability.item.1").Text!);
    await Action(widget, "capability.select.1");
    var launchDecision = Snapshot(widget);
    var launchDescription = Text(launchDecision.Root, "capability.description").Text!;
    Assert.Contains("trusted host rechecks", launchDescription);
    Assert.Contains("cannot supply paths", launchDescription);
    Assert.Contains("foreground-window commands", launchDescription);
    Assert.Valid(capabilities);
    Assert.Valid(decision);
    Assert.Valid(launchCapabilities);
    Assert.Valid(launchDecision);
}

static async Task CommunityServicePermissionCopy()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    WriteInstalledWidget(catalogRoot, "dev.test.companion", "dev.publisher.companion", "Companion",
        ["network.loopback:13091"], [PlatformCapabilities.PrivateSecretsV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    Assert.Contains("Access local app on port 13091",
        Button(capabilities.Root, "capability.item.0").Text!);
    await Action(widget, "capability.select.0");
    var loopback = Snapshot(widget);
    var loopbackDescription = Text(loopback.Root, "capability.description").Text!;
    Assert.Contains("only 127.0.0.1:13091", loopbackDescription);
    Assert.Contains("cannot choose another host or port", loopbackDescription);
    Assert.Contains("host-issued dashboard gesture", loopbackDescription);
    await Action(widget, "back");
    await Action(widget, "capability.select.1");
    var secrets = Snapshot(widget);
    var secretsDescription = Text(secrets.Root, "capability.description").Text!;
    Assert.Contains("never returned to widget code", secretsDescription);
    Assert.Contains("Windows Credential Manager", secretsDescription);
    Assert.Contains("same package publisher", secretsDescription);
    Assert.Valid(capabilities);
    Assert.Valid(loopback);
    Assert.Valid(secrets);
}

static async Task BluetoothPermissionCopy()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    WriteInstalledWidget(catalogRoot, "dev.test.bluetooth", "dev.publisher.bluetooth", "Bluetooth helper",
        [PlatformCapabilities.NetworkBluetoothReadV1],
        [PlatformCapabilities.NetworkBluetoothRadioControlV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");
    await Action(widget, "capability.select.0");
    var read = Snapshot(widget);
    Assert.Contains("addresses", Text(read.Root, "capability.description").Text!);
    Assert.Contains("device IDs", Text(read.Root, "capability.description").Text!);
    await Action(widget, "back");
    await Action(widget, "capability.select.1");
    var control = Snapshot(widget);
    Assert.Contains("hardware switches", Text(control.Root, "capability.description").Text!);
    Assert.Contains("device policy", Text(control.Root, "capability.description").Text!);
    Assert.Valid(read);
    Assert.Valid(control);
}

static async Task GrantAndRevoke()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    WriteInstalledWidget(catalogRoot, "dev.test.audio", "dev.publisher.audio", "Audio",
        [PlatformCapabilities.AudioSessionsReadV1], []);
    var authority = InstalledAuthority(catalogRoot, "dev.test.audio");
    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    var invalidations = 0;
    widget.Invalidated += (_, _) => invalidations++;
    await Activate(widget);
    await Action(widget, "capability.grant");
    Assert.Equal((ConsentDecision?)null, await consent.GetDecisionAsync(
        new("dev.test.audio", "dev.publisher.audio", "test"),
        PlatformCapabilities.AudioSessionsReadV1));
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");
    await Action(widget, "capability.select.0");
    Assert.Equal((ConsentDecision?)null, await consent.GetDecisionAsync(
        new("dev.test.audio", "dev.publisher.audio", "test"),
        PlatformCapabilities.AudioSessionsReadV1));
    Assert.Contains("Confirm granting", Text(Snapshot(widget).Root, "capability.confirmation").Text!);

    var beforeGrant = invalidations;
    await Action(widget, "capability.grant");
    Assert.Equal(ConsentDecision.Grant, await consent.GetDecisionAsync(
        new("dev.test.audio", authority, "test"),
        PlatformCapabilities.AudioSessionsReadV1));
    Assert.Equal((ConsentDecision?)null, await consent.GetDecisionAsync(
        new("dev.test.audio", "dev.publisher.audio", "test"),
        PlatformCapabilities.AudioSessionsReadV1));
    Assert.Contains("Granted", Text(Snapshot(widget).Root, "capability.state").Text!);
    Assert.True(invalidations > beforeGrant, "Grant did not invalidate Settings UI.");

    await Action(widget, "capability.deny");
    Assert.Equal(ConsentDecision.Deny, await consent.GetDecisionAsync(
        new("dev.test.audio", authority, "test"),
        PlatformCapabilities.AudioSessionsReadV1));
    Assert.Contains("Denied", Text(Snapshot(widget).Root, "capability.state").Text!);
}

static async Task PublisherIsolation()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    const string packageId = "dev.test.network";
    WriteInstalledWidget(catalogRoot, packageId, "dev.publisher.real", "Network",
        [PlatformCapabilities.NetworkReadV1], []);
    var authority = InstalledAuthority(catalogRoot, packageId);
    await consent.SetDecisionAsync(new(packageId, "dev.publisher.impostor", "test"),
        PlatformCapabilities.NetworkReadV1, ConsentDecision.Grant);
    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    await Activate(widget);
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");
    Assert.Contains("Not decided", Button(Snapshot(widget).Root, "capability.item.0").Text!);
    await Action(widget, "capability.select.0");
    await Action(widget, "capability.grant");
    Assert.Equal(ConsentDecision.Grant, await consent.GetDecisionAsync(
        new(packageId, authority, "test"), PlatformCapabilities.NetworkReadV1));
    Assert.Equal(ConsentDecision.Grant, await consent.GetDecisionAsync(
        new(packageId, "dev.publisher.impostor", "test"), PlatformCapabilities.NetworkReadV1));
    Assert.Equal(2, (await consent.LoadAsync()).Entries.Count);
}

static async Task UndeclaredCapabilitiesAreHidden()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    WriteInstalledWidget(catalogRoot, "dev.test.minimal", "dev.publisher.minimal", "Minimal",
        [PlatformCapabilities.AudioSessionsReadV1], ["network.client:example.test"]);
    await consent.SetDecisionAsync(new("dev.test.minimal", "dev.publisher.minimal", "test"),
        PlatformCapabilities.AudioSessionsControlV1, ConsentDecision.Grant);
    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    await Activate(widget);
    await Action(widget, "open.permissions");
    var packages = Snapshot(widget);
    var review = Button(packages.Root, "permissions.diagnostics.open");
    Assert.Contains("1 requests · 1 saved decisions", review.Text!);
    Assert.True(!Nodes(packages.Root).Any(node =>
        node.Id.StartsWith("permission-diagnostics.", StringComparison.Ordinal)),
        "Permission details leaked inline ahead of the package controls.");

    await Action(widget, "open.permission-diagnostics");
    var diagnostics = Snapshot(widget);
    Assert.Equal(SettingsPage.PermissionDiagnostics, widget.CurrentPage);
    Assert.HasShortcut(diagnostics.Root, "permission-diagnostics.page", ControllerButton.B, "back");
    Assert.True(!Buttons(diagnostics.Root).Any(button => button.ActionId == "back"),
        "Diagnostic review rendered a redundant Back action.");
    var diagnosticRows = Buttons(diagnostics.Root)
        .Where(button => button.Id.StartsWith("permission-diagnostics.unknown.", StringComparison.Ordinal) ||
                         button.Id.StartsWith("permission-diagnostics.inactive.", StringComparison.Ordinal))
        .ToArray();
    Assert.Equal(2, diagnosticRows.Length);
    Assert.True(diagnosticRows.All(row => row.IsDisabled is true),
        "Read-only permission diagnostics became actionable.");
    var unknown = diagnosticRows.Single(row =>
        row.Id.StartsWith("permission-diagnostics.unknown.", StringComparison.Ordinal)).Text!;
    Assert.Contains("Minimal", unknown);
    Assert.Contains("network.client:example.test", unknown);
    Assert.Contains("optional", unknown);
    var inactive = diagnosticRows.Single(row =>
        row.Id.StartsWith("permission-diagnostics.inactive.", StringComparison.Ordinal)).Text!;
    Assert.Contains("Minimal", inactive);
    Assert.Contains("Control audio sessions", inactive);
    Assert.Contains(PlatformCapabilities.AudioSessionsControlV1, inactive);
    Assert.Contains("Grant", inactive);
    Assert.Contains("dev.publisher.minimal", inactive);
    Assert.Equal(diagnosticRows[0].Id, diagnostics.InitialFocusId);
    Assert.Equal(diagnosticRows[1].Id, diagnosticRows[0].Focus!.Down);
    Assert.Equal(diagnosticRows[0].Id, diagnosticRows[1].Focus!.Up);
    Assert.Valid(diagnostics);

    await Action(widget, "back");
    var restoredPackages = Snapshot(widget);
    Assert.Equal(SettingsPage.Permissions, widget.CurrentPage);
    Assert.Equal("permissions.diagnostics.open", restoredPackages.InitialFocusId);
    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    Assert.Equal(1, Buttons(capabilities.Root).Count(button =>
        button.Id.StartsWith("capability.item.", StringComparison.Ordinal)));
    Assert.True(!Nodes(capabilities.Root).Any(node =>
        node.Text?.Contains("Control audio", StringComparison.OrdinalIgnoreCase) == true),
        "Undeclared stored grant became actionable.");
}

static async Task PermissionDiagnosticsAreBounded()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    var unknown = Enumerable.Range(0, 12)
        .Select(index => index == 0
            ? "network.client:" + new string('a', 113)
            : $"network.client:diagnostic-{index:D2}.example")
        .ToArray();
    const string packageId = "dev.test.diagnostics";
    WriteInstalledWidget(catalogRoot, packageId, "dev.publisher.current",
        "Helper " + new string('N', 73),
        [PlatformCapabilities.AudioSessionsReadV1], unknown);
    for (var index = 0; index < 12; index++)
    {
        await consent.SetDecisionAsync(
            new(packageId, $"dev.publisher.retired{index:D2}", "test"),
            PlatformCapabilities.AudioSessionsControlV1,
            index % 2 == 0 ? ConsentDecision.Grant : ConsentDecision.Deny);
    }

    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    await Activate(widget);
    await Action(widget, "open.permissions");
    var packages = Snapshot(widget);
    Assert.Contains("12 requests · 12 saved decisions",
        Button(packages.Root, "permissions.diagnostics.open").Text!);
    await Action(widget, "open.permission-diagnostics");
    var diagnostics = Snapshot(widget);
    var rows = Buttons(diagnostics.Root).ToArray();
    var exactDetails = rows.Where(row =>
        row.Id.StartsWith("permission-diagnostics.unknown.", StringComparison.Ordinal) ||
        row.Id.StartsWith("permission-diagnostics.inactive.", StringComparison.Ordinal)).ToArray();
    Assert.Equal(16, exactDetails.Length);
    Assert.Equal(12, exactDetails.Count(row =>
        row.Id.StartsWith("permission-diagnostics.unknown.", StringComparison.Ordinal)));
    Assert.Equal(4, exactDetails.Count(row =>
        row.Id.StartsWith("permission-diagnostics.inactive.", StringComparison.Ordinal)));
    Assert.Contains("8 more", Button(diagnostics.Root, "permission-diagnostics.more").Text!);
    Assert.True(rows.All(row => row.IsDisabled is true),
        "Permission diagnostic review exposed an actionable row.");
    Assert.Equal(rows[0].Id, diagnostics.InitialFocusId);
    for (var index = 0; index < rows.Length; index++)
    {
        Assert.Equal(index == 0 ? null : rows[index - 1].Id, rows[index].Focus!.Up);
        Assert.Equal(index == rows.Length - 1 ? null : rows[index + 1].Id,
            rows[index].Focus!.Down);
    }
    Assert.Equal(rows.Length, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
    Assert.True(Nodes(diagnostics.Root).All(node =>
            (node.Text?.Length ?? 0) < 4096 &&
            (node.AccessibilityLabel?.Length ?? 0) < 4096 &&
            !(node.Text?.Contains('\n') ?? false) &&
            !(node.Text?.Contains('\u202E') ?? false)),
        "Diagnostic display or accessibility text escaped its one-line protocol budget.");
    var retiredAuthorities = exactDetails
        .Where(row => row.Id.StartsWith("permission-diagnostics.inactive.", StringComparison.Ordinal))
        .Select(row => row.Text!)
        .ToArray();
    Assert.True(retiredAuthorities.All(text => text.Contains("authority dev.publisher.retired", StringComparison.Ordinal)),
        "Exact retired publisher authority was not visible.");
    Assert.Equal(retiredAuthorities.Length,
        retiredAuthorities.Distinct(StringComparer.Ordinal).Count());
    var rerendered = Snapshot(widget);
    Assert.SequenceEqual(rows.Select(row => row.Id), Buttons(rerendered.Root).Select(row => row.Id));
    Assert.Valid(diagnostics);
    Assert.Valid(rerendered);
}

static async Task TruncatedPermissionCatalogSuppressesClassification()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    for (var index = 0; index < 257; index++)
    {
        WriteInstalledWidget(catalogRoot, $"dev.test.widget{index:D3}",
            $"dev.publisher.widget{index:D3}", $"Widget {index:D3}",
            [PlatformCapabilities.AudioSessionsReadV1], []);
    }
    var omittedId = "dev.test.widget256";
    await consent.SetDecisionAsync(new(omittedId,
            InstalledAuthority(catalogRoot, omittedId), "test"),
        PlatformCapabilities.AudioSessionsReadV1, ConsentDecision.Grant);

    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    await Activate(widget);
    await Action(widget, "open.permissions");
    var packages = Snapshot(widget);
    var review = Button(packages.Root, "permissions.diagnostics.open");
    Assert.Contains("inactive classification unavailable", review.Text!);
    Assert.True(!review.Text!.Contains("saved decisions", StringComparison.OrdinalIgnoreCase),
        "An incomplete catalog reported a false inactive-decision count.");
    await Action(widget, "open.permission-diagnostics");
    var diagnostics = Snapshot(widget);
    Assert.True(Buttons(diagnostics.Root).Any(button =>
        button.Id == "permission-diagnostics.classification-unavailable"),
        "The review did not explain why inactive classification was unavailable.");
    Assert.True(!Buttons(diagnostics.Root).Any(button =>
        button.Id.StartsWith("permission-diagnostics.inactive.", StringComparison.Ordinal)),
        "A truncated catalog classified a potentially active decision as inactive.");
    Assert.Valid(packages);
    Assert.Valid(diagnostics);
}

static async Task MalformedPermissionStateFailsClosed()
{
    using var temp = new TemporaryDirectory();
    var badCatalog = Path.Combine(temp.Path, "bad-catalog");
    var badPackage = Path.Combine(badCatalog, "packages", "dev.test.bad", "1.0.0");
    Directory.CreateDirectory(badPackage);
    await File.WriteAllTextAsync(Path.Combine(badPackage, "manifest.json"), "{ invalid");
    var widget = CreateWithPermissions(temp.Path, badCatalog,
        new ConsentStore(Path.Combine(temp.Path, "consent-a")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    var catalogFailure = Snapshot(widget);
    Assert.Contains("invalid_manifest", Text(catalogFailure.Root, "permissions.help").Text!);
    Assert.True(!Buttons(catalogFailure.Root).Any(button =>
        button.Id.StartsWith("permission.item.", StringComparison.Ordinal)),
        "Malformed catalog exposed actionable packages.");
    Assert.Contains("inactive classification unavailable",
        Button(catalogFailure.Root, "permissions.diagnostics.open").Text!);
    await Action(widget, "open.permission-diagnostics");
    var catalogDiagnostics = Snapshot(widget);
    Assert.True(Buttons(catalogDiagnostics.Root).Any(button =>
        button.Id == "permission-diagnostics.classification-unavailable"),
        "Malformed catalog did not expose the bounded classification warning.");
    Assert.True(!Buttons(catalogDiagnostics.Root).Any(button =>
        button.Id.StartsWith("permission-diagnostics.inactive.", StringComparison.Ordinal)),
        "Malformed catalog produced an unsafe inactive-decision classification.");
    Assert.Valid(catalogFailure);
    Assert.Valid(catalogDiagnostics);

    var validCatalog = Path.Combine(temp.Path, "valid-catalog");
    WriteInstalledWidget(validCatalog, "dev.test.valid", "dev.publisher.valid", "Valid",
        [PlatformCapabilities.AudioSessionsReadV1], []);
    var consentRoot = Path.Combine(temp.Path, "consent-b");
    Directory.CreateDirectory(consentRoot);
    await File.WriteAllTextAsync(Path.Combine(consentRoot, "consent-v1.json"), "{ invalid");
    var consentFailureWidget = CreateWithPermissions(Path.Combine(temp.Path, "settings-b"),
        validCatalog, new ConsentStore(consentRoot));
    await Activate(consentFailureWidget);
    await Action(consentFailureWidget, "open.permissions");
    var invalidConsentPackages = Snapshot(consentFailureWidget);
    Assert.Contains("inactive classification unavailable",
        Button(invalidConsentPackages.Root, "permissions.diagnostics.open").Text!);
    await Action(consentFailureWidget, "permission.select.0");
    var consentFailure = Snapshot(consentFailureWidget);
    Assert.Contains("invalid_consent", Text(consentFailure.Root, "capabilities.diagnostic").Text!);
    Assert.Equal(true, Button(consentFailure.Root, "capability.item.0").IsDisabled);
}

static async Task RetiredConsentMigration()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    const string packageId = "dev.test.current";
    const string publisherId = "dev.publisher.current";
    WriteInstalledWidget(catalogRoot, packageId, publisherId, "Current",
        [PlatformCapabilities.AudioSessionsReadV1], []);
    var authorityPublisher = InstalledAuthority(catalogRoot, packageId, "1.0.0");
    var consentRoot = Path.Combine(temp.Path, "consent");
    Directory.CreateDirectory(consentRoot);
    await File.WriteAllTextAsync(Path.Combine(consentRoot, "consent-v1.json"),
        $$"""
        {"schemaVersion":1,"revision":2,"entries":[
          {"packageId":"{{packageId}}","publisherId":"{{authorityPublisher}}","capabilityId":"system.audio.sessions.read.v1","decision":"grant"},
          {"packageId":"org.gbar.firstparty.recent-apps","publisherId":"org.gbar.firstparty","capabilityId":"system.activity.recent.activate.v1","decision":"grant"}
        ]}
        """);

    var widget = CreateWithPermissions(temp.Path, catalogRoot, new ConsentStore(consentRoot));
    await Activate(widget);
    await Action(widget, "open.permissions");
    var packages = Snapshot(widget);
    Assert.True(!Text(packages.Root, "permissions.help").Text!
        .Contains("invalid_consent", StringComparison.Ordinal),
        "Retired capability left the permission catalog in invalid_consent.");
    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    var current = Button(capabilities.Root, "capability.item.0");
    Assert.Equal(false, current.IsDisabled is true);
    Assert.Contains("Granted by you", current.Text!);
}

static async Task PermissionActivationReload()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var widget = CreateWithPermissions(temp.Path, catalogRoot,
        new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    Assert.True(!Buttons(Snapshot(widget).Root).Any(button =>
        button.Id.StartsWith("permission.item.", StringComparison.Ordinal)),
        "Empty catalog unexpectedly contained a package.");
    WriteInstalledWidget(catalogRoot, "dev.test.later", "dev.publisher.later", "Later",
        [PlatformCapabilities.NetworkReadV1], []);
    await Task.Delay(80);
    Assert.True(!Buttons(Snapshot(widget).Root).Any(button =>
        button.Id.StartsWith("permission.item.", StringComparison.Ordinal)),
        "Catalog changed without a new activation.");
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    Assert.True(Buttons(Snapshot(widget).Root).Any(button => button.Id == "permission.item.0"),
        "Catalog did not reload on the next activation.");
}

static async Task FirstPartyIsNotAutoGranted()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    WriteInstalledWidget(catalogRoot, "org.gbar.firstparty.example", "org.gbar.firstparty", "First party",
        [PlatformCapabilities.AudioSessionsReadV1], []);
    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent);
    await Activate(widget);
    Assert.Equal(0, (await consent.LoadAsync()).Entries.Count);
    await Action(widget, "open.permissions");
    await Action(widget, "permission.select.0");
    Assert.Contains("Not decided", Button(Snapshot(widget).Root, "capability.item.0").Text!);
}

static string InstalledAuthority(
    string catalogRoot,
    string packageId,
    string version = "1.0.0")
{
    var installed = new WidgetCatalog(catalogRoot).DiscoverAsync()
        .GetAwaiter().GetResult().Widgets
        .Single(widget => widget.Id == packageId).Versions
        .Single(item => string.Equals(
            item.Version.ToString(), version, StringComparison.Ordinal));
    return InstalledWidgetAuthority.PublisherId(installed);
}

static async Task BundledPermissionsAreDiscovered()
{
    using var temp = new TemporaryDirectory();
    var bundledRoot = Path.Combine(temp.Path, "runtime");
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    WriteBundledWidget(
        bundledRoot,
        "AudioMixer",
        "org.gbar.firstparty.audiomixer",
        "org.gbar.firstparty",
        "Audio Mixer",
        [PlatformCapabilities.AudioSessionsReadV1],
        [PlatformCapabilities.AudioSessionsControlV1]);
    WriteBundledWidget(
        bundledRoot,
        "NetworkControls",
        "org.gbar.firstparty.network-controls",
        "org.gbar.firstparty",
        "Network Controls",
        [PlatformCapabilities.NetworkReadV1],
        [PlatformCapabilities.NetworkSavedProfileSwitchV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot, consent, bundledRoot);

    await Activate(widget);
    await Action(widget, "open.permissions");
    var packages = Snapshot(widget);
    Assert.Contains("Audio Mixer", Button(packages.Root, "permission.item.0").Text!);
    Assert.Contains("Network Controls", Button(packages.Root, "permission.item.1").Text!);
    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    Assert.Contains("Required", Button(capabilities.Root, "capability.item.0").Text!);
    Assert.Contains("Optional", Button(capabilities.Root, "capability.item.1").Text!);
    Assert.Equal(0, (await consent.LoadAsync()).Entries.Count);

    await Action(widget, "back");
    await Action(widget, "permission.select.1");
    var networkCapabilities = Snapshot(widget);
    Assert.Contains("Required", Button(networkCapabilities.Root, "capability.item.0").Text!);
    Assert.Contains("Optional", Button(networkCapabilities.Root, "capability.item.1").Text!);
}

static async Task ShippedAssetsValidate()
{
    var project = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(project, "manifest.json")));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    var package = GbssPackageLoader.LoadFile(
        Path.Combine(project, "styles", "default.gbss"),
        Path.Combine(project, "styles"));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
}

static SettingsWidget Create(string root)
{
    var paths = new PlatformSettingsPaths(root);
    return new SettingsWidget(new PlatformSettingsStore(paths), new ThemeCatalog(paths));
}

static SettingsWidget CreateWithPermissions(
    string settingsRoot,
    string catalogRoot,
    ConsentStore consentStore,
    string? bundledWidgetRoot = null)
{
    var paths = new PlatformSettingsPaths(settingsRoot);
    return new SettingsWidget(
        new PlatformSettingsStore(paths),
        new ThemeCatalog(paths),
        new WidgetCatalog(catalogRoot),
        consentStore,
        bundledWidgetRoot: bundledWidgetRoot);
}

static PlatformSettingsStore Store(string root) => new(new PlatformSettingsPaths(root));

static async Task Activate(SettingsWidget widget)
{
    await widget.InitializeAsync(CancellationToken.None);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
}

static ValueTask Action(SettingsWidget widget, string action) =>
    widget.OnActionAsync(new WidgetActionEvent(action, "test"));

static ViewSnapshot Snapshot(SettingsWidget widget) => widget.Render().CreateSnapshot("settings-test", 1);

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child))
        yield return descendant;
}

static IEnumerable<ViewNode> Buttons(ViewNode root) =>
    Nodes(root).Where(node => node.Kind == ViewNodeKind.Button);

static ViewNode Button(ViewNode root, string id) =>
    Buttons(root).Single(node => node.Id == id);

static ViewNode Text(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id && node.Kind == ViewNodeKind.Text);

static ViewNode Scope(ViewNode root, string id) =>
    Nodes(root).Single(node => node.InputScopeId == id);

static void WriteTheme(string root, string id, string name, string version, bool valid)
{
    var directory = Path.Combine(new PlatformSettingsPaths(root).ThemesDirectory, id, version);
    Directory.CreateDirectory(directory);
    var manifestId = valid ? id : "dev.test.wrong";
    File.WriteAllText(Path.Combine(directory, "theme.json"),
        $$"""{"schemaVersion":1,"id":"{{manifestId}}","name":"{{name}}","version":"{{version}}","entryFile":"theme.gbss"}""");
    File.WriteAllText(Path.Combine(directory, "theme.gbss"), "button { color: #ffffff; }");
}

static void WriteInstalledWidget(
    string catalogRoot,
    string id,
    string publisher,
    string name,
    IReadOnlyList<string> required,
    IReadOnlyList<string> optional,
    HostApiRange? hostApi = null,
    IReadOnlyList<string>? architectures = null,
    string version = "1.0.0")
{
    var directory = Path.Combine(catalogRoot, "packages", id, version);
    var payload = Path.Combine(directory, "payload");
    Directory.CreateDirectory(payload);
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = publisher,
        Name = name,
        Version = version,
        HostApi = hostApi ?? new("1.0", 1),
        Entrypoint = new("dotnet-worker", "payload/Widget.dll", "Dev.Test.Widget"),
        Permissions = required,
        OptionalPermissions = optional,
        BackgroundPolicy = "suspend",
        ResourceRequest = new(32, 1),
        Architectures = architectures ?? ["x64"],
    };
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    File.WriteAllBytes(Path.Combine(directory, "manifest.json"), ManifestJson.Serialize(manifest));
    File.WriteAllBytes(Path.Combine(payload, "Widget.dll"), [0x47, 0x42, 0x41]);
    InstalledPackageIntegrity.Seal(
        catalogRoot, directory, new WidgetCatalogOptions());
}

static void WriteBundledWidget(
    string bundledRoot,
    string directoryName,
    string id,
    string publisher,
    string name,
    IReadOnlyList<string> required,
    IReadOnlyList<string> optional)
{
    var directory = Path.Combine(bundledRoot, directoryName);
    Directory.CreateDirectory(directory);
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = publisher,
        Name = name,
        Version = "1.0.0",
        HostApi = new("1.0", 1),
        Entrypoint = new("dotnet-worker", "payload/Widget.dll", "Dev.Test.Widget"),
        Permissions = required,
        OptionalPermissions = optional,
        BackgroundPolicy = "suspend",
        ResourceRequest = new(32, 1),
        Architectures = ["x64"],
    };
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    File.WriteAllBytes(Path.Combine(directory, "manifest.json"), ManifestJson.Serialize(manifest));
}

static string ProjectDirectory()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, "src", "FirstPartyWidgets", "SettingsWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate SettingsWidget project directory.");
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "settings-widget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

file sealed class SequenceDiagnosticsService(params PlatformDiagnosticsSnapshot[] snapshots)
    : IPlatformDiagnosticsService
{
    private readonly PlatformDiagnosticsSnapshot[] _snapshots = snapshots;
    private int _requests;

    public int RequestCount => Volatile.Read(ref _requests);

    public ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = Interlocked.Increment(ref _requests);
        return ValueTask.FromResult(_snapshots[Math.Min(request - 1, _snapshots.Length - 1)]);
    }
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    public static void HasShortcut(ViewNode root, string scopeId, ControllerButton button, string action)
    {
        var scope = Nodes(root).Single(node => node.InputScopeId == scopeId);
        if (!scope.Shortcuts.Any(item => item.Button == button && item.ActionId == action))
            throw new InvalidOperationException($"Scope '{scopeId}' lacks {button} -> {action}.");
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }
}
