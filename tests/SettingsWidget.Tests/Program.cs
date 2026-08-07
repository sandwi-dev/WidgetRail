using GameBarAlternative.FirstPartyWidgets.Settings;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Root exposes all first-party settings categories", RootCategories),
    ("Nested pages own scoped B navigation", NestedScopesAndBack),
    ("Settings composites expose controller semantics", CompositeControls),
    ("Scale and opacity actions persist within bounds", BoundedPersistence),
    ("Theme picker paginates valid and invalid packages", ThemePagination),
    ("Theme selection atomically pins ID and version", ThemeSelection),
    ("Reset requires confirmation and restores defaults", ResetConfirmation),
    ("Malformed settings recover through safe defaults", InvalidSettingsRecovery),
    ("Saving exposes busy and completion feedback", BusyFeedback),
    ("Diagnostics report invalid theme packages", InvalidThemeDiagnostics),
    ("Activation reloads once per visible lifetime without polling", ActivationLifecycle),
    ("Focus IDs remain stable at setting bounds", StableBoundFocus),
    ("Installed widgets use controller pages and explicit review", InstalledWidgetReview),
    ("Installed widget enable and disable update catalog state", InstalledWidgetToggle),
    ("Malformed installed widget catalogs fail closed", InstalledWidgetCatalogFailure),
    ("Installed widget review reloads only on activation", InstalledWidgetActivationReload),
    ("Incompatible installed widgets cannot be enabled", IncompatibleInstalledWidget),
    ("Permissions use nested controller scopes and bounded package pages", PermissionScopesAndPagination),
    ("Capability grant confirms and deny revokes atomically", GrantAndRevoke),
    ("Consent decisions isolate package publisher identities", PublisherIsolation),
    ("Undeclared capabilities and decisions are never actionable", UndeclaredCapabilitiesAreHidden),
    ("Malformed catalog and consent fail closed with diagnostics", MalformedPermissionStateFailsClosed),
    ("Permission catalog reloads only on activation", PermissionActivationReload),
    ("Bundled first-party capability manifests join permission review", BundledPermissionsAreDiscovered),
    ("First-party packages are never auto-granted", FirstPartyIsNotAutoGranted),
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
        ["category.appearance", "category.accessibility", "category.overlay", "category.installed-widgets", "category.permissions", "category.diagnostics", "category.reset"],
        Buttons(snapshot.Root).Select(button => button.Id));
    Assert.Valid(snapshot);
    return Task.CompletedTask;
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
    Assert.Equal("motion.reduced", buttons["motion.system"].Focus!.Down);
    Assert.Valid(snapshot);
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

static async Task ThemePagination()
{
    using var temp = new TemporaryDirectory();
    for (var index = 0; index < 6; index++)
        WriteTheme(temp.Path, $"dev.test.theme{index}", "Theme " + index, "1.0.0", valid: true);
    WriteTheme(temp.Path, "dev.test.invalid", "Broken", "1.0.0", valid: false);
    var widget = Create(temp.Path);
    await Activate(widget);
    await Action(widget, "open.appearance");
    await Action(widget, "open.themes");
    var first = Snapshot(widget);
    Assert.True(Buttons(first.Root).Count(button => button.Id.StartsWith("theme.item.", StringComparison.Ordinal)) <= 5,
        "Theme page exceeded five items.");
    Assert.HasShortcut(first.Root, "theme.picker", ControllerButton.RightBumper, "theme.next-page");
    Assert.True(Buttons(first.Root).Any(button => button.IsDisabled is true), "Invalid theme was not visible and disabled.");
    await Action(widget, "theme.next-page");
    var second = Snapshot(widget);
    Assert.Equal("theme.item.5", second.InitialFocusId);
    Assert.HasShortcut(second.Root, "theme.picker", ControllerButton.LeftBumper, "theme.previous-page");
    Assert.True(!Scope(second.Root, "theme.picker").Shortcuts.Any(item => item.Button == ControllerButton.RightBumper),
        "Last theme page exposed a next-page shortcut.");
    Assert.Valid(second);
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
    Assert.Contains("Enable reviewed widget", Button(details.Root, "installed.details.toggle").Text!);
    Assert.Valid(first);
    Assert.Valid(second);
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

static async Task PermissionScopesAndPagination()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    for (var index = 0; index < 6; index++)
        WriteInstalledWidget(catalogRoot, $"dev.test.widget{index}", $"dev.publisher{index}",
            $"Widget {index}", [PlatformCapabilities.AudioSessionsReadV1],
            [PlatformCapabilities.AudioSessionsControlV1]);
    var widget = CreateWithPermissions(temp.Path, catalogRoot, new ConsentStore(Path.Combine(temp.Path, "consent")));
    await Activate(widget);
    await Action(widget, "open.permissions");
    var first = Snapshot(widget);
    Assert.Equal("permissions.packages", first.ActiveInputScopeId);
    Assert.HasShortcut(first.Root, "permissions.packages", ControllerButton.B, "back");
    Assert.HasShortcut(first.Root, "permissions.packages", ControllerButton.RightBumper,
        "permission.next-page");
    Assert.Equal(5, Buttons(first.Root).Count(button =>
        button.Id.StartsWith("permission.item.", StringComparison.Ordinal)));
    await Action(widget, "permission.next-page");
    var second = Snapshot(widget);
    Assert.Equal("permission.item.5", second.InitialFocusId);
    Assert.HasShortcut(second.Root, "permissions.packages", ControllerButton.LeftBumper,
        "permission.previous-page");

    await Action(widget, "permission.select.5");
    var capabilities = Snapshot(widget);
    Assert.Equal(SettingsPage.PackageCapabilities, widget.CurrentPage);
    Assert.Equal("capabilities.package", capabilities.ActiveInputScopeId);
    Assert.HasShortcut(capabilities.Root, "capabilities.package", ControllerButton.B, "back");
    Assert.Contains("Required", Button(capabilities.Root, "capability.item.0").Text!);
    Assert.Contains("Optional", Button(capabilities.Root, "capability.item.1").Text!);
    Assert.Contains("Not decided", Button(capabilities.Root, "capability.item.0").Text!);

    await Action(widget, "capability.select.0");
    var decision = Snapshot(widget);
    Assert.Equal("capability.decision", decision.ActiveInputScopeId);
    Assert.HasShortcut(decision.Root, "capability.decision", ControllerButton.B, "back");
    Assert.True(Buttons(decision.Root).Any(button => button.Id == "capability.grant"),
        "Grant confirmation action is missing.");
    await Action(widget, "back");
    Assert.Equal(SettingsPage.PackageCapabilities, widget.CurrentPage);
    await Action(widget, "back");
    Assert.Equal(SettingsPage.Permissions, widget.CurrentPage);
    Assert.Valid(second);
    Assert.Valid(capabilities);
    Assert.Valid(decision);
}

static async Task GrantAndRevoke()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "catalog");
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    WriteInstalledWidget(catalogRoot, "dev.test.audio", "dev.publisher.audio", "Audio",
        [PlatformCapabilities.AudioSessionsReadV1], []);
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
        new("dev.test.audio", "dev.publisher.audio", "test"),
        PlatformCapabilities.AudioSessionsReadV1));
    Assert.Contains("Granted", Text(Snapshot(widget).Root, "capability.state").Text!);
    Assert.True(invalidations > beforeGrant, "Grant did not invalidate Settings UI.");

    await Action(widget, "capability.deny");
    Assert.Equal(ConsentDecision.Deny, await consent.GetDecisionAsync(
        new("dev.test.audio", "dev.publisher.audio", "test"),
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
        new(packageId, "dev.publisher.real", "test"), PlatformCapabilities.NetworkReadV1));
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
    Assert.Contains("Hidden: 1 unknown declarations, 1 undeclared",
        Text(packages.Root, "permissions.hidden").Text!);
    await Action(widget, "permission.select.0");
    var capabilities = Snapshot(widget);
    Assert.Equal(1, Buttons(capabilities.Root).Count(button =>
        button.Id.StartsWith("capability.item.", StringComparison.Ordinal)));
    Assert.True(!Nodes(capabilities.Root).Any(node =>
        node.Text?.Contains("Control audio", StringComparison.OrdinalIgnoreCase) == true),
        "Undeclared stored grant became actionable.");
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
    await Action(consentFailureWidget, "permission.select.0");
    var consentFailure = Snapshot(consentFailureWidget);
    Assert.Contains("invalid_consent", Text(consentFailure.Root, "capabilities.page-label").Text!);
    Assert.Equal(true, Button(consentFailure.Root, "capability.item.0").IsDisabled);
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
        bundledWidgetRoot);
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
    IReadOnlyList<string>? architectures = null)
{
    const string version = "1.0.0";
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
