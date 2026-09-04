using System.Text;
using System.Text.Json;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Missing settings use safe appearance defaults", DefaultsAreSafe),
    ("Default paths use only the WidgetRail local state root", DefaultPathsUseWidgetRail),
    ("Settings round trip through strict canonical JSON", SettingsRoundTrip),
    ("Schema-one settings retire launcher selection and preserve unrelated state", LegacyAccessibilityDefaults),
    ("Malformed duplicate unknown and oversized settings fail closed", StrictSettingsFailClosed),
    ("Settings ranges and enums are enforced", SettingsRangesAreEnforced),
    ("Failed mutations preserve the prior atomic document", FailedMutationPreservesState),
    ("Independent stores serialize concurrent mutations", ConcurrentMutationsPersist),
    ("Theme catalog discovers every embedded theme and valid user themes", ThemeDiscovery),
    ("Built-in Cool Slate selection publishes distinct controller-safe tokens", BuiltInCoolSlateSelection),
    ("Built-in Neon Circuit selection keeps controller-safe geometry", BuiltInNeonCircuitSelection),
    ("Built-in Arcade Rush selection keeps controller-safe geometry", BuiltInArcadeRushSelection),
    ("Built-in Redline selection marks structure without touching geometry", BuiltInRedlineSelection),
    ("Theme manifests enforce identity paths bounds and strict JSON", ThemeManifestSafety),
    ("Theme sources reject traversal and reparse points", ThemeSourceSafety),
    ("Theme count is bounded", ThemeCountIsBounded),
    ("Theme version mutation is exact protected and bounded", ThemeVersionMutation),
    ("Theme layers apply platform widget and user precedence", ThemeLayerPrecedence),
    ("Invalid reload retains the last valid theme and revision", InvalidReloadRetainsLastGood),
    ("Built-in theme gives CodeText bounded Windows monospace wrapping", CodeTextThemeTests.Run),
};

static Task DefaultPathsUseWidgetRail()
{
    var paths = PlatformSettingsPaths.CreateDefault();
    Assert.Equal("WidgetRail", Path.GetFileName(paths.RootDirectory));
    Assert.True(
        !paths.RootDirectory.Contains("GameBarAlternative", StringComparison.Ordinal),
        "Default settings path retained the retired local state root.");
    return Task.CompletedTask;
}

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

static Task DefaultsAreSafe()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    return VerifyAsync();

    async Task VerifyAsync()
    {
        var settings = await store.LoadAsync();
        Assert.Equal(2, settings.SchemaVersion);
        Assert.Equal(ThemeIdentity.BuiltInDefault, settings.Appearance.ThemeId);
        Assert.Equal(ThemeIdentity.BuiltInDefaultVersion, settings.Appearance.ThemeVersion);
        Assert.Equal(1D, settings.Appearance.InterfaceScale);
        Assert.Equal(1D, settings.Appearance.TextScale);
        Assert.Equal(0.64D, settings.Appearance.BackdropOpacity);
        Assert.Equal(MotionPreference.System, settings.Appearance.Motion);
        Assert.Equal(ContrastPreference.System, settings.Appearance.Contrast);
        Assert.Equal(false, settings.Appearance.BoldText);
        Assert.Equal(TransparencyPreference.Full, settings.Appearance.Transparency);
        Assert.Equal(false, settings.Appearance.AnimateWidgetSwitching);
        Assert.Equal(false, settings.AppLibrary.EpicInstalledGamesEnabled);
        Assert.Equal(false, settings.AppLibrary.GogInstalledGamesEnabled);
        Assert.True(!File.Exists(store.Paths.SettingsFile), "Reading defaults must not create a settings file.");
    }
}

static async Task SettingsRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    var updated = await store.UpdateAsync(current => current with
    {
        AppLibrary = current.AppLibrary with
        {
            EpicInstalledGamesEnabled = true,
            GogInstalledGamesEnabled = true,
        },
        Appearance = current.Appearance with
        {
            ThemeId = "dev.example.slate",
            ThemeVersion = "1.2.3",
            InterfaceScale = 1.15,
            TextScale = 1.2,
            BackdropOpacity = 0.7,
            Motion = MotionPreference.Reduced,
            Contrast = ContrastPreference.High,
            BoldText = true,
            Transparency = TransparencyPreference.Reduced,
            AnimateWidgetSwitching = true,
        },
    });
    Assert.Equal("dev.example.slate", updated.Appearance.ThemeId);
    Assert.Equal("1.2.3", updated.Appearance.ThemeVersion);
    Assert.Equal(true, updated.AppLibrary.EpicInstalledGamesEnabled);
    Assert.Equal(true, updated.AppLibrary.GogInstalledGamesEnabled);
    Assert.Equal(true, updated.Appearance.AnimateWidgetSwitching);
    var reloaded = await new PlatformSettingsStore(new PlatformSettingsPaths(temp.Path)).LoadAsync();
    Assert.DocumentEqual(updated, reloaded);
    Assert.Equal(true, reloaded.Appearance.AnimateWidgetSwitching);
    var source = await File.ReadAllTextAsync(store.Paths.SettingsFile);
    Assert.Contains("\"schemaVersion\": 2", source);
    Assert.Contains("\"motion\": \"reduced\"", source);
    Assert.Contains("\"contrast\": \"high\"", source);
    Assert.Contains("\"boldText\": true", source);
    Assert.Contains("\"transparency\": \"reduced\"", source);
    Assert.Contains("\"animateWidgetSwitching\": true", source);
    Assert.True(!Directory.EnumerateFiles(temp.Path, ".platform-settings.*.tmp").Any(),
        "Atomic settings temporary file leaked.");

    var reset = await store.ReplaceAsync(PlatformSettingsDocument.Default);
    Assert.Equal(false, reset.Appearance.AnimateWidgetSwitching);
    var resetReloaded = await new PlatformSettingsStore(new PlatformSettingsPaths(temp.Path)).LoadAsync();
    Assert.Equal(false, resetReloaded.Appearance.AnimateWidgetSwitching);
}

static async Task LegacyAccessibilityDefaults()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    Directory.CreateDirectory(temp.Path);
    await File.WriteAllTextAsync(paths.SettingsFile, SettingsJson(extra:
        ",\"appLibrary\":{\"epicInstalledGamesEnabled\":true," +
        "\"gogInstalledGamesEnabled\":false}," +
        "\"launcherExperience\":{\"useGlobalAppearance\":false," +
        "\"selectedId\":\"dev.example.launcher\",\"selectedVersion\":\"1.2.3\"," +
        "\"lastGoodId\":\"dev.example.launcher\",\"lastGoodVersion\":\"1.2.3\"}"));
    var store = new PlatformSettingsStore(paths);
    var loaded = await store.LoadAsync();
    Assert.Equal(2, loaded.SchemaVersion);
    Assert.Equal(ContrastPreference.System, loaded.Appearance.Contrast);
    Assert.Equal(false, loaded.Appearance.BoldText);
    Assert.Equal(TransparencyPreference.Full, loaded.Appearance.Transparency);
    Assert.Equal(false, loaded.Appearance.AnimateWidgetSwitching);
    Assert.Equal(true, loaded.AppLibrary.EpicInstalledGamesEnabled);
    Assert.Equal(false, loaded.AppLibrary.GogInstalledGamesEnabled);

    var updated = await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with { TextScale = 1.1 },
    });
    Assert.Equal(1.1, updated.Appearance.TextScale);
    Assert.Equal(true, updated.AppLibrary.EpicInstalledGamesEnabled);
    var persisted = await File.ReadAllTextAsync(paths.SettingsFile);
    Assert.Contains("\"schemaVersion\": 2", persisted);
    Assert.True(!persisted.Contains("launcherExperience", StringComparison.Ordinal),
        "The retired launcher selection remained in the current settings schema.");
}

static async Task StrictSettingsFailClosed()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    Directory.CreateDirectory(temp.Path);
    var invalidDocuments = new[]
    {
        "{",
        SettingsJson(extra: ",\"unknown\":true"),
        SettingsJson().Replace("\"themeId\":", "\"ThemeId\":", StringComparison.Ordinal),
        "{\"schemaVersion\":1,\"schemaVersion\":1,\"appearance\":{}}",
        "{\"schemaVersion\":1}",
    };
    foreach (var source in invalidDocuments)
    {
        await File.WriteAllTextAsync(paths.SettingsFile, source);
        var exception = await Assert.ThrowsAsync<PlatformSettingsException>(
            () => new PlatformSettingsStore(paths).LoadAsync());
        Assert.True(exception.Code is "invalid_settings" or "required",
            $"Unexpected strict settings error: {exception.Code}");
    }

    await File.WriteAllBytesAsync(
        paths.SettingsFile,
        Enumerable.Repeat((byte)' ', PlatformSettingsStore.MaximumSettingsBytes + 1).ToArray());
    var oversized = await Assert.ThrowsAsync<PlatformSettingsException>(
        () => new PlatformSettingsStore(paths).LoadAsync());
    Assert.Equal("settings_too_large", oversized.Code);
}

static async Task SettingsRangesAreEnforced()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    Directory.CreateDirectory(temp.Path);
    foreach (var source in new[]
    {
        SettingsJson(interfaceScale: "1.251"),
        SettingsJson(textScale: "0.849"),
        SettingsJson(backdropOpacity: "0.81"),
        SettingsJson(themeId: "Upper.Case"),
        SettingsJson(themeVersion: "01.0"),
        SettingsJson(motion: "unknown"),
        SettingsJson(appearanceExtra: ",\"contrast\":\"future\""),
        SettingsJson(appearanceExtra: ",\"transparency\":\"future\""),
        SettingsJson(appearanceExtra: ",\"boldText\":1"),
        SettingsJson(schemaVersion: 3),
    })
    {
        await File.WriteAllTextAsync(paths.SettingsFile, source);
        await Assert.ThrowsAsync<PlatformSettingsException>(
            () => new PlatformSettingsStore(paths).LoadAsync());
    }
}

static async Task FailedMutationPreservesState()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    var saved = await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with { TextScale = 1.1 },
    });
    var exception = await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        store.UpdateAsync(current => current with
        {
            Appearance = current.Appearance with { TextScale = 99 },
        }));
    Assert.Equal("out_of_range", exception.Code);
    Assert.DocumentEqual(saved, await store.LoadAsync());
    Assert.True(!Directory.EnumerateFiles(temp.Path, ".platform-settings.*.tmp").Any(),
        "Rejected mutation leaked a temporary file.");
}

static async Task ConcurrentMutationsPersist()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    await Store(temp.Path).UpdateAsync(current => current with
    {
        Appearance = current.Appearance with { InterfaceScale = 0.8 },
    });
    var stores = Enumerable.Range(0, 20)
        .Select(_ => new PlatformSettingsStore(paths))
        .ToArray();
    await Task.WhenAll(stores.Select(store => store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with
        {
            InterfaceScale = Math.Round(current.Appearance.InterfaceScale + 0.01, 2),
        },
    })));
    var result = await Store(temp.Path).LoadAsync();
    Assert.Near(1.0, result.Appearance.InterfaceScale);
}

static Task ThemeDiscovery()
{
    using var temp = new TemporaryDirectory();
    WriteTheme(temp.Path, "dev.example.slate", "Slate", "1.2.3", "button { color: #112233; }");
    var catalog = Catalog(temp.Path);
    var snapshot = catalog.Discover();
    Assert.SequenceEqual(
        [
            ThemeIdentity.BuiltInDefault,
            ThemeIdentity.BuiltInCoolSlate,
            ThemeIdentity.BuiltInNeonCircuit,
            ThemeIdentity.BuiltInArcadeRush,
            ThemeIdentity.BuiltInRedline,
            "dev.example.slate",
        ],
        snapshot.Themes.Select(theme => theme.Descriptor.Id));
    Assert.True(snapshot.Themes.All(theme => theme.IsValid), Describe(snapshot.Themes.SelectMany(item => item.Diagnostics)));
    Assert.SequenceEqual(
        [
            ThemeIdentity.BuiltInDefault,
            ThemeIdentity.BuiltInCoolSlate,
            ThemeIdentity.BuiltInNeonCircuit,
            ThemeIdentity.BuiltInArcadeRush,
            ThemeIdentity.BuiltInRedline,
        ],
        catalog.BuiltInThemes.Select(theme => theme.Descriptor.Id));
    Assert.True(catalog.BuiltInThemes.All(theme => theme.Descriptor.IsBuiltIn && theme.IsValid),
        Describe(catalog.BuiltInThemes.SelectMany(item => item.Diagnostics)));
    var builtIn = catalog.BuiltInDefault;
    var compiled = WrssThemeCompiler.Compile(builtIn.Package);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
    var canvas = compiled.Theme!.Resolve(new WrssElement("canvas"));
    Assert.Equal("#090908", canvas.Get("background")!.Text);
    Assert.Equal("Segoe UI Variable Text, Segoe UI", canvas.Get("font-family")!.Text);
    var panel = compiled.Theme.Resolve(new WrssElement("panel"));
    Assert.Equal("1px", panel.Get("border-width")!.Text);
    Assert.Equal("12px", panel.Get("corner-radius")!.Text);
    var button = compiled.Theme.Resolve(new WrssElement("button"));
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("10px", button.Get("corner-radius")!.Text);
    Assert.Equal("400", button.Get("font-weight")!.Text);
    Assert.Equal("1", button.Get("scale")!.Text);
    Assert.Equal("90ms", button.Get("transition-duration")!.Text);
    Assert.Equal("ease-out", button.Get("transition-easing")!.Text);
    Assert.True(button.Get("shadow-blur") is null,
        "The minimalist default must not add a heavy component shadow.");
    var primaryIconButton = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-icon-button", "wrail-icon-button--primary"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("44px", primaryIconButton.Get("min-width")!.Text);
    Assert.Equal("44px", primaryIconButton.Get("min-height")!.Text);
    Assert.Equal("10px", primaryIconButton.Get("corner-radius")!.Text);
    Assert.Equal("#b8ae92", primaryIconButton.Get("background")!.Text);
    var toggleOff = compiled.Theme.Resolve(new WrssElement(
        "button", null,
        new HashSet<string>(["wrail-switch", "wrail-switch--off"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("44px", toggleOff.Get("min-height")!.Text);
    Assert.Equal("10px", toggleOff.Get("corner-radius")!.Text);
    var toggleOn = compiled.Theme.Resolve(new WrssElement(
        "button", null,
        new HashSet<string>(["wrail-switch", "wrail-switch--on"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("rgba(48, 47, 43, 0.98)", toggleOn.Get("background")!.Text);
    Assert.Equal("rgba(246, 240, 226, 0.22)", toggleOn.Get("border-color")!.Text);
    var focusedToggle = compiled.Theme.Resolve(new WrssElement(
        "button", null,
        new HashSet<string>(["wrail-switch", "wrail-switch--off"]),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("#f4f0e8", focusedToggle.Get("outline-color")!.Text);
    var disabledToggle = compiled.Theme.Resolve(new WrssElement(
        "button", null,
        new HashSet<string>(["wrail-switch", "wrail-switch--off"]),
        new HashSet<WrssPseudoState>([WrssPseudoState.Disabled])));
    Assert.Equal("0.64", disabledToggle.Get("opacity")!.Text);
    var stepper = compiled.Theme.Resolve(new WrssElement(
        "row", null, new HashSet<string>(["wrail-stepper"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("10px", stepper.Get("gap")!.Text);
    Assert.Equal("center", stepper.Get("align")!.Text);
    var stepperLabel = compiled.Theme.Resolve(new WrssElement(
        "text", null, new HashSet<string>(["wrail-stepper__label"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("1", stepperLabel.Get("flex-grow")!.Text);
    var stepperValue = compiled.Theme.Resolve(new WrssElement(
        "text", null, new HashSet<string>(["wrail-stepper__value"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("64px", stepperValue.Get("min-width")!.Text);
    Assert.Equal("center", stepperValue.Get("text-align")!.Text);
    foreach (var direction in new[] { "decrement", "increment" })
    {
        var stepperButton = compiled.Theme.Resolve(new WrssElement(
            "button", null,
            new HashSet<string>(["wrail-stepper__button", $"wrail-stepper__button--{direction}"]),
            new HashSet<WrssPseudoState>()));
        Assert.Equal("44px", stepperButton.Get("width")!.Text);
        Assert.Equal("44px", stepperButton.Get("min-width")!.Text);
        Assert.Equal("0", stepperButton.Get("flex-shrink")!.Text);
    }
    var focusedStepperButton = compiled.Theme.Resolve(new WrssElement(
        "button", null, new HashSet<string>(["wrail-stepper__button"]),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("#f4f0e8", focusedStepperButton.Get("outline-color")!.Text);
    var disabledStepperButton = compiled.Theme.Resolve(new WrssElement(
        "button", null, new HashSet<string>(["wrail-stepper__button"]),
        new HashSet<WrssPseudoState>([WrssPseudoState.Disabled])));
    Assert.Equal("0.64", disabledStepperButton.Get("opacity")!.Text);
    var slider = compiled.Theme.Resolve(new WrssElement("slider"));
    Assert.Equal("44px", slider.Get("min-height")!.Text);
    Assert.Equal("1px", slider.Get("border-width")!.Text);
    Assert.Equal("0.99", slider.Get("scale")!.Text);
    Assert.Equal("90ms", slider.Get("transition-duration")!.Text);
    var segmentedTabs = compiled.Theme.Resolve(new WrssElement(
        "row",
        null,
        new HashSet<string>(["wrail-segmented-tabs"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("50px", segmentedTabs.Get("min-height")!.Text);
    Assert.Equal("0", segmentedTabs.Get("flex-shrink")!.Text);
    Assert.Equal("1px", segmentedTabs.Get("border-width")!.Text);
    var navigationRail = compiled.Theme.Resolve(new WrssElement(
        "stack",
        null,
        new HashSet<string>(["wrail-navigation-shell__rail"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("156px", navigationRail.Get("width")!.Text);
    Assert.Equal("0", navigationRail.Get("flex-shrink")!.Text);
    Assert.Equal("clip", navigationRail.Get("overflow")!.Text);
    var navigationItem = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-navigation-shell__compact-item"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("44px", navigationItem.Get("min-height")!.Text);
    Assert.Equal("0", navigationItem.Get("min-width")!.Text);
    var settingsRowAction = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-settings-row__action"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("44px", settingsRowAction.Get("min-height")!.Text);
    Assert.Equal("start", settingsRowAction.Get("text-align")!.Text);
    var actionSheetItem = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-action-sheet__item"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("44px", actionSheetItem.Get("min-height")!.Text);
    Assert.Equal("0", actionSheetItem.Get("flex-shrink")!.Text);
    var settingsDescription = compiled.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-settings-row__description"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("4", settingsDescription.Get("max-lines")!.Text);
    var focusedButton = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("1", focusedButton.Get("scale")!.Text);
    Assert.Equal("#f4f0e8", focusedButton.Get("outline-color")!.Text);
    Assert.Equal("-2px", focusedButton.Get("outline-offset")!.Text);
    Assert.Equal("90ms", focusedButton.Get("transition-duration")!.Text);
    Assert.Equal("ease-out", focusedButton.Get("transition-easing")!.Text);
    var eyebrow = compiled.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-section-header__eyebrow"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("none", eyebrow.Get("text-transform")!.Text);
    Assert.Equal("500", eyebrow.Get("font-weight")!.Text);
    foreach (var role in new[] { "button", "slider" })
    {
        foreach (var state in new[] { WrssPseudoState.Disabled, WrssPseudoState.Busy })
        {
            var stateStyle = compiled.Theme.Resolve(new WrssElement(
                role,
                null,
                new HashSet<string>(),
                new HashSet<WrssPseudoState>([state])));
            Assert.True(stateStyle.Get("opacity") is null,
                $"Built-in {role}:{state} opacity would compound the native accessibility factor.");
        }
    }
    return Task.CompletedTask;
}

static async Task BuiltInCoolSlateSelection()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with
        {
            ThemeId = ThemeIdentity.BuiltInCoolSlate,
            ThemeVersion = ThemeIdentity.BuiltInCoolSlateVersion,
        },
    });

    var catalog = Catalog(temp.Path);
    var slatePackage = catalog.Load(
        ThemeIdentity.BuiltInCoolSlate,
        ThemeIdentity.BuiltInCoolSlateVersion);
    Assert.True(slatePackage.IsValid, Describe(slatePackage.Diagnostics));
    Assert.Equal("Cool Slate", slatePackage.Descriptor.Name);
    Assert.Equal(true, slatePackage.Descriptor.IsBuiltIn);
    Assert.Equal("widgetrail.builtin", slatePackage.Descriptor.Publisher);
    Assert.Equal(new Version(1, 0, 0), slatePackage.Descriptor.Version);

    using var manager = new ThemeManager(store, catalog);
    var reload = await manager.ReloadAsync();
    Assert.True(reload.Published, Describe(reload.Diagnostics));
    Assert.Equal(ThemeIdentity.BuiltInCoolSlate, reload.Current.ActiveTheme.Id);
    Assert.Equal(ThemeIdentity.BuiltInCoolSlateVersion, reload.Current.ActiveTheme.Version.ToString());

    var canvas = reload.Current.Theme.Resolve(new WrssElement("canvas"));
    Assert.Equal("#080d14", canvas.Get("background")!.Text);
    Assert.Equal("#edf2f7", canvas.Get("color")!.Text);
    var button = reload.Current.Theme.Resolve(new WrssElement("button"));
    Assert.Equal("rgba(22, 32, 45, 0.98)", button.Get("background")!.Text);
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("10px", button.Get("corner-radius")!.Text);
    Assert.Equal("1", button.Get("scale")!.Text);
    Assert.Equal("90ms", button.Get("transition-duration")!.Text);
    Assert.Equal("ease-out", button.Get("transition-easing")!.Text);
    var focused = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("#f1f4f7", focused.Get("outline-color")!.Text);
    Assert.Equal("-2px", focused.Get("outline-offset")!.Text);
    Assert.Equal("1", focused.Get("scale")!.Text);

    var defaultCanvas = WrssThemeCompiler.Compile(catalog.BuiltInDefault.Package)
        .Theme!.Resolve(new WrssElement("canvas"));
    Assert.True(defaultCanvas.Get("background")!.Text != canvas.Get("background")!.Text,
        "Cool Slate must be visibly distinct from the warm graphite default.");

    var layeredWidget = reload.Current.CompileForWidget(Package(
        "widget.wrss",
        "button { min-height: 44px; color: var(--text); }"));
    Assert.True(layeredWidget.IsValid, Describe(layeredWidget.Diagnostics));
    Assert.Equal("#edf2f7", layeredWidget.Theme!.Resolve(new WrssElement("button")).Get("color")!.Text);
}

static async Task BuiltInNeonCircuitSelection()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with
        {
            ThemeId = ThemeIdentity.BuiltInNeonCircuit,
            ThemeVersion = ThemeIdentity.BuiltInNeonCircuitVersion,
        },
    });

    var catalog = Catalog(temp.Path);
    var neonPackage = catalog.Load(
        ThemeIdentity.BuiltInNeonCircuit,
        ThemeIdentity.BuiltInNeonCircuitVersion);
    Assert.True(neonPackage.IsValid, Describe(neonPackage.Diagnostics));
    Assert.Equal("Neon Circuit", neonPackage.Descriptor.Name);
    Assert.Equal(true, neonPackage.Descriptor.IsBuiltIn);
    Assert.Equal("widgetrail.builtin", neonPackage.Descriptor.Publisher);
    Assert.Equal(new Version(1, 0, 0), neonPackage.Descriptor.Version);

    using var manager = new ThemeManager(store, catalog);
    var reload = await manager.ReloadAsync();
    Assert.True(reload.Published, Describe(reload.Diagnostics));
    Assert.Equal(ThemeIdentity.BuiltInNeonCircuit, reload.Current.ActiveTheme.Id);

    var canvas = reload.Current.Theme.Resolve(new WrssElement("canvas"));
    Assert.Equal("#05070e", canvas.Get("background")!.Text);
    Assert.Equal("#e8f1fb", canvas.Get("color")!.Text);
    var panel = reload.Current.Theme.Resolve(new WrssElement("panel"));
    Assert.Equal("8px", panel.Get("corner-radius")!.Text);

    // The theme layer outranks selector specificity, so a themed base rule can
    // silently defeat a platform pseudo-state rule. Neon Circuit only retunes
    // properties the platform never varies per state; these assertions prove
    // the focused and controller-safe contract survived the override.
    var button = reload.Current.Theme.Resolve(new WrssElement("button"));
    Assert.Equal("6px", button.Get("corner-radius")!.Text);
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("rgba(19, 24, 41, 0.98)", button.Get("background")!.Text);
    Assert.True(button.Get("shadow-blur") is null, "Neon Circuit must not introduce a component shadow.");
    var focused = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("rgba(35, 45, 74, 0.98)", focused.Get("background")!.Text);
    Assert.Equal("#eaf7ff", focused.Get("outline-color")!.Text);
    Assert.Equal("2px", focused.Get("outline-width")!.Text);
    Assert.Equal("-2px", focused.Get("outline-offset")!.Text);
    Assert.Equal("6px", focused.Get("corner-radius")!.Text);

    var eyebrow = reload.Current.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-section-header__eyebrow"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("uppercase", eyebrow.Get("text-transform")!.Text);
    Assert.Equal("#ff4fd8", eyebrow.Get("color")!.Text);

    var primaryIconButton = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-icon-button", "wrail-icon-button--primary"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("44px", primaryIconButton.Get("min-height")!.Text);
    Assert.Equal("#3fe0ff", primaryIconButton.Get("background")!.Text);

    var defaultCanvas = WrssThemeCompiler.Compile(catalog.BuiltInDefault.Package)
        .Theme!.Resolve(new WrssElement("canvas"));
    Assert.True(defaultCanvas.Get("background")!.Text != canvas.Get("background")!.Text,
        "Neon Circuit must be visibly distinct from the warm graphite default.");

    var layeredWidget = reload.Current.CompileForWidget(Package(
        "widget.wrss",
        "button { min-height: 44px; color: var(--text); }"));
    Assert.True(layeredWidget.IsValid, Describe(layeredWidget.Diagnostics));
    Assert.Equal("#e8f1fb", layeredWidget.Theme!.Resolve(new WrssElement("button")).Get("color")!.Text);
}

static async Task BuiltInArcadeRushSelection()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with
        {
            ThemeId = ThemeIdentity.BuiltInArcadeRush,
            ThemeVersion = ThemeIdentity.BuiltInArcadeRushVersion,
        },
    });

    var catalog = Catalog(temp.Path);
    var arcadePackage = catalog.Load(
        ThemeIdentity.BuiltInArcadeRush,
        ThemeIdentity.BuiltInArcadeRushVersion);
    Assert.True(arcadePackage.IsValid, Describe(arcadePackage.Diagnostics));
    Assert.Equal("Arcade Rush", arcadePackage.Descriptor.Name);
    Assert.Equal(true, arcadePackage.Descriptor.IsBuiltIn);
    Assert.Equal("widgetrail.builtin", arcadePackage.Descriptor.Publisher);
    Assert.Equal(new Version(1, 0, 0), arcadePackage.Descriptor.Version);

    using var manager = new ThemeManager(store, catalog);
    var reload = await manager.ReloadAsync();
    Assert.True(reload.Published, Describe(reload.Diagnostics));
    Assert.Equal(ThemeIdentity.BuiltInArcadeRush, reload.Current.ActiveTheme.Id);

    var canvas = reload.Current.Theme.Resolve(new WrssElement("canvas"));
    Assert.Equal("#1a1020", canvas.Get("background")!.Text);
    Assert.Equal("#f6eef8", canvas.Get("color")!.Text);
    var panel = reload.Current.Theme.Resolve(new WrssElement("panel"));
    Assert.Equal("14px", panel.Get("corner-radius")!.Text);

    // Pill geometry must not reach the dimensions a controller depends on, and
    // the themed base rules must not outrank the platform's state rules.
    var button = reload.Current.Theme.Resolve(new WrssElement("button"));
    Assert.Equal("22px", button.Get("corner-radius")!.Text);
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("rgba(46, 30, 60, 0.98)", button.Get("background")!.Text);
    Assert.True(button.Get("shadow-blur") is null, "Arcade Rush must not introduce a component shadow.");
    var focused = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("rgba(72, 48, 92, 0.98)", focused.Get("background")!.Text);
    Assert.Equal("#fdf4fa", focused.Get("outline-color")!.Text);
    Assert.Equal("2px", focused.Get("outline-width")!.Text);
    Assert.Equal("-2px", focused.Get("outline-offset")!.Text);
    Assert.Equal("22px", focused.Get("corner-radius")!.Text);

    var trayItem = reload.Current.Theme.Resolve(new WrssElement("tray-item"));
    Assert.Equal("22px", trayItem.Get("corner-radius")!.Text);
    Assert.Equal("44px", trayItem.Get("min-width")!.Text);
    Assert.Equal("44px", trayItem.Get("min-height")!.Text);

    // Uppercase is reserved for the eyebrow role; badges keep sentence case,
    // which is what separates this theme's label voice from Neon Circuit's.
    var eyebrow = reload.Current.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-section-header__eyebrow"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("uppercase", eyebrow.Get("text-transform")!.Text);
    Assert.Equal("0.12em", eyebrow.Get("letter-spacing")!.Text);
    Assert.Equal("#c0aec8", eyebrow.Get("color")!.Text);
    var badgeLabel = reload.Current.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-badge__label"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("600", badgeLabel.Get("font-weight")!.Text);
    Assert.True(badgeLabel.Get("text-transform") is null,
        "Arcade Rush must leave badge labels in sentence case.");

    var primaryIconButton = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-icon-button", "wrail-icon-button--primary"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("44px", primaryIconButton.Get("min-height")!.Text);
    Assert.Equal("23px", primaryIconButton.Get("corner-radius")!.Text);
    Assert.Equal("#ff3ea5", primaryIconButton.Get("background")!.Text);

    var neonCanvas = catalog.Load(
        ThemeIdentity.BuiltInNeonCircuit,
        ThemeIdentity.BuiltInNeonCircuitVersion);
    Assert.True(neonCanvas.IsValid, Describe(neonCanvas.Diagnostics));
    var defaultCanvas = WrssThemeCompiler.Compile(catalog.BuiltInDefault.Package)
        .Theme!.Resolve(new WrssElement("canvas"));
    Assert.True(defaultCanvas.Get("background")!.Text != canvas.Get("background")!.Text,
        "Arcade Rush must be visibly distinct from the warm graphite default.");

    var layeredWidget = reload.Current.CompileForWidget(Package(
        "widget.wrss",
        "button { min-height: 44px; color: var(--text); }"));
    Assert.True(layeredWidget.IsValid, Describe(layeredWidget.Diagnostics));
    Assert.Equal("#f6eef8", layeredWidget.Theme!.Resolve(new WrssElement("button")).Get("color")!.Text);
}

static async Task BuiltInRedlineSelection()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with
        {
            ThemeId = ThemeIdentity.BuiltInRedline,
            ThemeVersion = ThemeIdentity.BuiltInRedlineVersion,
        },
    });

    var catalog = Catalog(temp.Path);
    var redlinePackage = catalog.Load(
        ThemeIdentity.BuiltInRedline,
        ThemeIdentity.BuiltInRedlineVersion);
    Assert.True(redlinePackage.IsValid, Describe(redlinePackage.Diagnostics));
    Assert.Equal("Redline", redlinePackage.Descriptor.Name);
    Assert.Equal(true, redlinePackage.Descriptor.IsBuiltIn);
    Assert.Equal("widgetrail.builtin", redlinePackage.Descriptor.Publisher);
    Assert.Equal(new Version(1, 0, 0), redlinePackage.Descriptor.Version);

    using var manager = new ThemeManager(store, catalog);
    var reload = await manager.ReloadAsync();
    Assert.True(reload.Published, Describe(reload.Diagnostics));
    Assert.Equal(ThemeIdentity.BuiltInRedline, reload.Current.ActiveTheme.Id);

    var canvas = reload.Current.Theme.Resolve(new WrssElement("canvas"));
    Assert.Equal("#160b0d", canvas.Get("background")!.Text);
    Assert.Equal("#fbeeec", canvas.Get("color")!.Text);

    // The structural edge is additive: the uniform hairline and the platform's
    // corner radii survive underneath it.
    var panel = reload.Current.Theme.Resolve(new WrssElement("panel"));
    Assert.Equal("#ff3d2e", panel.Get("border-bottom-color")!.Text);
    Assert.Equal("2px", panel.Get("border-bottom-width")!.Text);
    Assert.Equal("rgba(255, 214, 208, 0.14)", panel.Get("border-color")!.Text);
    Assert.Equal("1px", panel.Get("border-width")!.Text);
    Assert.Equal("12px", panel.Get("corner-radius")!.Text);

    // Redline leaves geometry to the platform entirely, and the edge marks
    // containers only — never a hit target.
    var button = reload.Current.Theme.Resolve(new WrssElement("button"));
    Assert.Equal("10px", button.Get("corner-radius")!.Text);
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("rgba(46, 23, 25, 0.98)", button.Get("background")!.Text);
    Assert.True(button.Get("border-bottom-width") is null,
        "The structural edge must not reach controls.");
    Assert.True(button.Get("shadow-blur") is null, "Redline must not introduce a component shadow.");
    var focused = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("rgba(77, 40, 43, 0.98)", focused.Get("background")!.Text);
    Assert.Equal("#fff4f2", focused.Get("outline-color")!.Text);
    Assert.Equal("2px", focused.Get("outline-width")!.Text);
    Assert.Equal("-2px", focused.Get("outline-offset")!.Text);

    // A transparent card has no box to underline, so the edge is zeroed again.
    var transparentCard = reload.Current.Theme.Resolve(new WrssElement(
        "container",
        null,
        new HashSet<string>(["wrail-card", "wrail-card--transparent"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("0px", transparentCard.Get("border-bottom-width")!.Text);

    // A red accent forces danger off red; the two must stay separable.
    var dangerItem = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-action-sheet__item", "wrail-action-sheet__item--danger"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("#ff5fa8", dangerItem.Get("color")!.Text);
    var primaryIconButton = reload.Current.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-icon-button", "wrail-icon-button--primary"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("#ff3d2e", primaryIconButton.Get("background")!.Text);
    Assert.True(dangerItem.Get("color")!.Text != primaryIconButton.Get("background")!.Text,
        "Destructive intent must not resolve to the same ink as the accent.");

    var eyebrow = reload.Current.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-section-header__eyebrow"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("#ff3d2e", eyebrow.Get("color")!.Text);
    Assert.Equal("uppercase", eyebrow.Get("text-transform")!.Text);
    Assert.Equal("0.14em", eyebrow.Get("letter-spacing")!.Text);

    var defaultCanvas = WrssThemeCompiler.Compile(catalog.BuiltInDefault.Package)
        .Theme!.Resolve(new WrssElement("canvas"));
    Assert.True(defaultCanvas.Get("background")!.Text != canvas.Get("background")!.Text,
        "Redline must be visibly distinct from the warm graphite default.");

    var layeredWidget = reload.Current.CompileForWidget(Package(
        "widget.wrss",
        "button { min-height: 44px; color: var(--text); }"));
    Assert.True(layeredWidget.IsValid, Describe(layeredWidget.Diagnostics));
    Assert.Equal("#fbeeec", layeredWidget.Theme!.Resolve(new WrssElement("button")).Get("color")!.Text);
}

static async Task ThemeManifestSafety()
{
    using var temp = new TemporaryDirectory();
    var theme = WriteTheme(temp.Path, "dev.example.bad", "Bad", "1.0.0", "button { color: #ffffff; }");
    var manifest = Path.Combine(theme, "theme.json");
    var catalog = Catalog(temp.Path);

    await File.WriteAllTextAsync(manifest, ThemeManifest("dev.example.other", "Bad", "1.0.0", "theme.wrss"));
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "theme_identity_mismatch");

    await File.WriteAllTextAsync(manifest, ThemeManifest("dev.example.bad", "Bad", "1.0.0", "../outside.wrss"));
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "invalid_theme_entry");

    await File.WriteAllTextAsync(manifest,
        ThemeManifest("dev.example.bad", "Bad", "1.0.0", "theme.wrss", ",\"unknown\":true"));
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "invalid_theme_manifest");

    await File.WriteAllBytesAsync(
        manifest,
        Enumerable.Repeat((byte)' ', ThemeCatalog.MaximumManifestBytes + 1).ToArray());
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "theme_manifest_too_large");

    WriteTheme(
        temp.Path,
        ThemeIdentity.BuiltInCoolSlate,
        "Shadowed Slate",
        ThemeIdentity.BuiltInCoolSlateVersion,
        "button { color: #ffffff; }");
    var reservedCollision = catalog.Discover().Themes.Single(item =>
        item.Descriptor.Id == ThemeIdentity.BuiltInCoolSlate && !item.IsValid);
    Assert.HasCode(reservedCollision.Diagnostics, "invalid_theme_id");
}

static Task ThemeSourceSafety()
{
    using var temp = new TemporaryDirectory();
    var theme = WriteTheme(temp.Path, "dev.example.link", "Link", "1.0.0", "button { color: #ffffff; }");
    var outside = Path.Combine(temp.Path, "outside.wrss");
    File.WriteAllText(outside, "button { color: #000000; }");
    var entry = Path.Combine(theme, "theme.wrss");
    File.Delete(entry);
    try
    {
        File.CreateSymbolicLink(entry, outside);
    }
    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
    {
        return Task.CompletedTask;
    }
    var loaded = Catalog(temp.Path).Load("dev.example.link", "1.0.0");
    Assert.True(!loaded.IsValid, "Reparse-backed theme source unexpectedly loaded.");
    Assert.HasCode(loaded.Diagnostics, "unsafe_import");
    return Task.CompletedTask;
}

static Task ThemeCountIsBounded()
{
    using var temp = new TemporaryDirectory();
    var themes = new PlatformSettingsPaths(temp.Path).ThemesDirectory;
    Directory.CreateDirectory(themes);
    for (var index = 0; index <= ThemeCatalog.MaximumThemes; index++)
        Directory.CreateDirectory(Path.Combine(themes, $"dev.test.theme-{index:D3}", "1.0.0"));
    var exception = Assert.Throws<PlatformSettingsException>(() => Catalog(temp.Path).Discover());
    Assert.Equal("too_many_themes", exception.Code);
    return Task.CompletedTask;
}

static async Task ThemeVersionMutation()
{
    using var temp = new TemporaryDirectory();
    var first = WriteTheme(temp.Path, "dev.example.family", "Family", "1.0.0", "button { color: #111111; }");
    var second = WriteTheme(temp.Path, "dev.example.family", "Family", "2.0.0", "button { color: #222222; }");
    var unrelated = WriteTheme(temp.Path, "dev.example.other", "Other", "1.0.0", "button { color: #333333; }");
    var store = Store(temp.Path);
    var catalog = Catalog(temp.Path);
    var policy = new ThemeCatalogMutationPolicy(store, catalog);

    var selected = await policy.SelectAsync("dev.example.family", "2.0.0");
    Assert.Equal("dev.example.family", selected.Appearance.ThemeId);
    Assert.Equal("2.0.0", selected.Appearance.ThemeVersion);
    Assert.SequenceEqual("button { color: #222222; }"u8.ToArray(),
        await File.ReadAllBytesAsync(Path.Combine(second, "theme.wrss")));

    var protectedSelection = await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        policy.RetireAsync("dev.example.family", "2.0.0"));
    Assert.Equal("selected_theme_protected", protectedSelection.Code);
    var protectedBuiltIn = await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        policy.RetireAsync(ThemeIdentity.BuiltInDefault, ThemeIdentity.BuiltInDefaultVersion));
    Assert.Equal("builtin_theme_protected", protectedBuiltIn.Code);

    var retired = await policy.RetireAsync("dev.example.family", "1.0.0");
    Assert.Equal("dev.example.family", retired.ThemeId);
    Assert.True(!Directory.Exists(first), "Retired exact version remained in the catalog.");
    Assert.True(Directory.Exists(second), "Selected sibling version was removed.");
    Assert.True(Directory.Exists(unrelated), "Unrelated theme was removed.");
    Assert.DocumentEqual(selected, await store.LoadAsync());
    Assert.True(!catalog.Discover().Themes.Any(item =>
        item.Descriptor.Id == "dev.example.family" && item.Descriptor.Version == new Version(1, 0, 0)),
        "Retired version remained discoverable.");

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() =>
        policy.RetireAsync("dev.example.other", "1.0.0", cancelled.Token));
    Assert.True(Directory.Exists(unrelated), "Cancelled retirement changed the catalog.");
    var malformed = await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        policy.RetireAsync("../escape", "1.0.0"));
    Assert.Equal("invalid_theme_id", malformed.Code);

    var concurrent = WriteTheme(
        temp.Path, "dev.example.concurrent", "Concurrent", "1.0.0",
        "button { color: #444444; }");
    var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
    {
        try
        {
            await new ThemeCatalogMutationPolicy(Store(temp.Path), Catalog(temp.Path))
                .RetireAsync("dev.example.concurrent", "1.0.0");
            return "removed";
        }
        catch (PlatformSettingsException exception)
        {
            return exception.Code;
        }
    }));
    Assert.Equal(1, attempts.Count(result => result == "removed"));
    Assert.Equal(1, attempts.Count(result => result is "theme_not_found" or "theme_changed"));
    Assert.True(!Directory.Exists(concurrent), "Concurrent retirement did not settle exactly once.");

    var linked = WriteTheme(
        temp.Path, "dev.example.linked-removal", "Linked", "1.0.0",
        "button { color: #555555; }");
    var outside = Path.Combine(temp.Path, "outside-theme-data.txt");
    await File.WriteAllTextAsync(outside, "must survive");
    var link = Path.Combine(linked, "linked.wrss");
    try
    {
        File.CreateSymbolicLink(link, outside);
    }
    catch (Exception exception) when (
        exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
    {
        return;
    }
    var linkedResult = await policy.RetireAsync("dev.example.linked-removal", "1.0.0");
    Assert.True(linkedResult.CleanupPending, "Reparse-backed cleanup was not quarantined.");
    Assert.Equal("must survive", await File.ReadAllTextAsync(outside));
    Assert.True(!catalog.Discover().Themes.Any(item =>
        item.CatalogId == "dev.example.linked-removal" && item.CatalogVersion == "1.0.0"),
        "Reparse-backed retired package remained in the active catalog.");
}

static Task ThemeLayerPrecedence()
{
    var platform = Package("platform.wrss", """
        :root { --choice: #111111; }
        button { color: var(--choice); background: #010101; }
        """);
    var widget = Package("widget.wrss", """
        :root { --choice: #222222; }
        #special { color: var(--choice); background: #020202; }
        """);
    var user = Package("user.wrss", """
        :root { --choice: #333333; }
        button { color: var(--choice); }
        """);
    var compiled = ThemeLayerCompiler.Compile(platform, widget, user);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
    var style = compiled.Theme!.Resolve(new WrssElement("button", "special"));
    Assert.Equal("#333333", style.Get("color")!.Text);
    Assert.Equal("#020202", style.Get("background")!.Text);
    return Task.CompletedTask;
}

static async Task InvalidReloadRetainsLastGood()
{
    using var temp = new TemporaryDirectory();
    var themeDirectory = WriteTheme(
        temp.Path, "dev.example.live", "Live", "1.0.0",
        "button { color: #123456; }");
    var store = Store(temp.Path);
    await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with
        {
            ThemeId = "dev.example.live",
            ThemeVersion = "1.0.0",
        },
    });
    using var manager = new ThemeManager(store, Catalog(temp.Path));
    var valid = await manager.ReloadAsync();
    Assert.True(valid.Published, Describe(valid.Diagnostics));
    var revision = valid.Current.Revision;
    Assert.Equal("#123456", valid.Current.Theme.Resolve(new WrssElement("button")).Get("color")!.Text);

    await File.WriteAllTextAsync(Path.Combine(themeDirectory, "theme.wrss"), "button { color: definitely-not-a-color; }");
    var invalid = await manager.ReloadAsync();
    Assert.True(!invalid.Published, "Invalid theme reload unexpectedly published.");
    Assert.Equal(revision, invalid.Current.Revision);
    Assert.Equal("#123456", manager.Current.Theme.Resolve(new WrssElement("button")).Get("color")!.Text);
    Assert.HasCode(manager.LastReloadDiagnostics, "invalid_value");

    await File.WriteAllTextAsync(Path.Combine(themeDirectory, "theme.wrss"), "button { color: #abcdef; }");
    var recovered = await manager.ReloadAsync();
    Assert.True(recovered.Published, Describe(recovered.Diagnostics));
    Assert.True(recovered.Current.Revision > revision, "Successful reload did not advance revision.");
    Assert.Equal("#abcdef", recovered.Current.Theme.Resolve(new WrssElement("button")).Get("color")!.Text);

    var widget = Package("widget.wrss", "button { background: #0a0b0c; color: #000000; }");
    var layered = recovered.Current.CompileForWidget(widget);
    Assert.True(layered.IsValid, Describe(layered.Diagnostics));
    var style = layered.Theme!.Resolve(new WrssElement("button"));
    Assert.Equal("#abcdef", style.Get("color")!.Text);
    Assert.Equal("#0a0b0c", style.Get("background")!.Text);
}

static PlatformSettingsStore Store(string root) =>
    new(new PlatformSettingsPaths(root));

static ThemeCatalog Catalog(string root) =>
    new(new PlatformSettingsPaths(root));

static string WriteTheme(string root, string id, string name, string version, string wrss)
{
    var directory = Path.Combine(new PlatformSettingsPaths(root).ThemesDirectory, id, version);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "theme.json"), ThemeManifest(id, name, version, "theme.wrss"));
    File.WriteAllText(Path.Combine(directory, "theme.wrss"), wrss);
    return directory;
}

static string ThemeManifest(
    string id,
    string name,
    string version,
    string entry,
    string extra = "") =>
    $$"""
      {"schemaVersion":1,"id":"{{id}}","name":"{{name}}","version":"{{version}}","entryFile":"{{entry}}"{{extra}}}
      """;

static string SettingsJson(
    int schemaVersion = 1,
    string themeId = "builtin.default",
    string themeVersion = "1.0.0",
    string interfaceScale = "1",
    string textScale = "1",
    string backdropOpacity = "0.64",
    string motion = "system",
    string extra = "",
    string appearanceExtra = "") =>
    $$"""
      {"schemaVersion":{{schemaVersion}},"appearance":{"themeId":"{{themeId}}","themeVersion":"{{themeVersion}}","interfaceScale":{{interfaceScale}},"textScale":{{textScale}},"backdropOpacity":{{backdropOpacity}},"motion":"{{motion}}"{{appearanceExtra}}}{{extra}}}
      """;

static WrssPackageResult Package(string sourceName, string source)
{
    var parsed = WrssParser.Parse(source, sourceName);
    return new WrssPackageResult([parsed.Document], parsed.Diagnostics);
}

static string Describe(IEnumerable<WrssDiagnostic> diagnostics) =>
    string.Join(Environment.NewLine, diagnostics);

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "platform-settings-tests", Guid.NewGuid().ToString("N"));
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
    public static void DocumentEqual(
        PlatformSettingsDocument expected,
        PlatformSettingsDocument actual)
    {
        Equal(expected.SchemaVersion, actual.SchemaVersion);
        Equal(expected.Appearance.ThemeId, actual.Appearance.ThemeId);
        Equal(expected.Appearance.ThemeVersion, actual.Appearance.ThemeVersion);
        Equal(expected.Appearance.InterfaceScale, actual.Appearance.InterfaceScale);
        Equal(expected.Appearance.TextScale, actual.Appearance.TextScale);
        Equal(expected.Appearance.BackdropOpacity, actual.Appearance.BackdropOpacity);
        Equal(expected.Appearance.Motion, actual.Appearance.Motion);
        Equal(expected.Appearance.Contrast, actual.Appearance.Contrast);
        Equal(expected.Appearance.BoldText, actual.Appearance.BoldText);
        Equal(expected.Appearance.Transparency, actual.Appearance.Transparency);
        Equal(expected.Appearance.AnimateWidgetSwitching, actual.Appearance.AnimateWidgetSwitching);
        Equal(expected.Appearance.WidgetSurfaceAppearance, actual.Appearance.WidgetSurfaceAppearance);
        SequenceEqual(
            expected.Appearance.WidgetSurfaceAppearanceOverrides.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal),
            actual.Appearance.WidgetSurfaceAppearanceOverrides.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal));
        Equal(
            expected.AppLibrary.EpicInstalledGamesEnabled,
            actual.AppLibrary.EpicInstalledGamesEnabled);
        Equal(
            expected.AppLibrary.GogInstalledGamesEnabled,
            actual.AppLibrary.GogInstalledGamesEnabled);
    }

    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"Expected approximately '{expected}', got '{actual}'.");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static void HasCode(IEnumerable<WrssDiagnostic> diagnostics, string code)
    {
        if (!diagnostics.Any(item => item.Code == code))
            throw new InvalidOperationException(
                $"Expected diagnostic '{code}'. Actual: {string.Join(Environment.NewLine, diagnostics)}");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
