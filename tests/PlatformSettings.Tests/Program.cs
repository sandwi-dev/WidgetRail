using System.Text;
using System.Text.Json;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Missing settings use safe appearance defaults", DefaultsAreSafe),
    ("Settings round trip through strict canonical JSON", SettingsRoundTrip),
    ("Legacy schema-one settings receive additive accessibility defaults", LegacyAccessibilityDefaults),
    ("Malformed duplicate unknown and oversized settings fail closed", StrictSettingsFailClosed),
    ("Settings ranges and enums are enforced", SettingsRangesAreEnforced),
    ("Failed mutations preserve the prior atomic document", FailedMutationPreservesState),
    ("Independent stores serialize concurrent mutations", ConcurrentMutationsPersist),
    ("Theme catalog discovers the embedded default and valid user themes", ThemeDiscovery),
    ("Theme manifests enforce identity paths bounds and strict JSON", ThemeManifestSafety),
    ("Theme sources reject traversal and reparse points", ThemeSourceSafety),
    ("Theme count is bounded", ThemeCountIsBounded),
    ("Theme layers apply platform widget and user precedence", ThemeLayerPrecedence),
    ("Invalid reload retains the last valid theme and revision", InvalidReloadRetainsLastGood),
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

static Task DefaultsAreSafe()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    return VerifyAsync();

    async Task VerifyAsync()
    {
        var settings = await store.LoadAsync();
        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal(ThemeIdentity.BuiltInDefault, settings.Appearance.ThemeId);
        Assert.Equal(ThemeIdentity.BuiltInDefaultVersion, settings.Appearance.ThemeVersion);
        Assert.Equal(1D, settings.Appearance.InterfaceScale);
        Assert.Equal(1D, settings.Appearance.TextScale);
        Assert.Equal(0.64D, settings.Appearance.BackdropOpacity);
        Assert.Equal(MotionPreference.System, settings.Appearance.Motion);
        Assert.Equal(ContrastPreference.System, settings.Appearance.Contrast);
        Assert.Equal(false, settings.Appearance.BoldText);
        Assert.Equal(TransparencyPreference.Full, settings.Appearance.Transparency);
        Assert.True(!File.Exists(store.Paths.SettingsFile), "Reading defaults must not create a settings file.");
    }
}

static async Task SettingsRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var store = Store(temp.Path);
    var updated = await store.UpdateAsync(current => current with
    {
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
        },
    });
    Assert.Equal("dev.example.slate", updated.Appearance.ThemeId);
    Assert.Equal("1.2.3", updated.Appearance.ThemeVersion);
    var reloaded = await new PlatformSettingsStore(new PlatformSettingsPaths(temp.Path)).LoadAsync();
    Assert.Equal(updated, reloaded);
    var source = await File.ReadAllTextAsync(store.Paths.SettingsFile);
    Assert.Contains("\"schemaVersion\": 1", source);
    Assert.Contains("\"motion\": \"reduced\"", source);
    Assert.Contains("\"contrast\": \"high\"", source);
    Assert.Contains("\"boldText\": true", source);
    Assert.Contains("\"transparency\": \"reduced\"", source);
    Assert.True(!Directory.EnumerateFiles(temp.Path, ".platform-settings.*.tmp").Any(),
        "Atomic settings temporary file leaked.");
}

static async Task LegacyAccessibilityDefaults()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    Directory.CreateDirectory(temp.Path);
    await File.WriteAllTextAsync(paths.SettingsFile, SettingsJson());
    var loaded = await new PlatformSettingsStore(paths).LoadAsync();
    Assert.Equal(ContrastPreference.System, loaded.Appearance.Contrast);
    Assert.Equal(false, loaded.Appearance.BoldText);
    Assert.Equal(TransparencyPreference.Full, loaded.Appearance.Transparency);
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
        SettingsJson(schemaVersion: 2),
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
    Assert.Equal(saved, await store.LoadAsync());
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
        [ThemeIdentity.BuiltInDefault, "dev.example.slate"],
        snapshot.Themes.Select(theme => theme.Descriptor.Id));
    Assert.True(snapshot.Themes.All(theme => theme.IsValid), Describe(snapshot.Themes.SelectMany(item => item.Diagnostics)));
    var builtIn = catalog.BuiltInDefault;
    var compiled = GbssThemeCompiler.Compile(builtIn.Package);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
    var canvas = compiled.Theme!.Resolve(new GbssElement("canvas"));
    Assert.Equal("#090908", canvas.Get("background")!.Text);
    Assert.Equal("Segoe UI Variable Text, Segoe UI", canvas.Get("font-family")!.Text);
    var panel = compiled.Theme.Resolve(new GbssElement("panel"));
    Assert.Equal("1px", panel.Get("border-width")!.Text);
    Assert.Equal("12px", panel.Get("corner-radius")!.Text);
    var button = compiled.Theme.Resolve(new GbssElement("button"));
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("10px", button.Get("corner-radius")!.Text);
    Assert.Equal("400", button.Get("font-weight")!.Text);
    Assert.True(button.Get("shadow-blur") is null,
        "The minimalist default must not add a heavy component shadow.");
    var primaryIconButton = compiled.Theme.Resolve(new GbssElement(
        "button",
        null,
        new HashSet<string>(["gbar-icon-button", "gbar-icon-button--primary"]),
        new HashSet<GbssPseudoState>()));
    Assert.Equal("44px", primaryIconButton.Get("min-width")!.Text);
    Assert.Equal("44px", primaryIconButton.Get("min-height")!.Text);
    Assert.Equal("10px", primaryIconButton.Get("corner-radius")!.Text);
    Assert.Equal("#b8ae92", primaryIconButton.Get("background")!.Text);
    var slider = compiled.Theme.Resolve(new GbssElement("slider"));
    Assert.Equal("44px", slider.Get("min-height")!.Text);
    Assert.Equal("1px", slider.Get("border-width")!.Text);
    var segmentedTabs = compiled.Theme.Resolve(new GbssElement(
        "row",
        null,
        new HashSet<string>(["gbar-segmented-tabs"]),
        new HashSet<GbssPseudoState>()));
    Assert.Equal("50px", segmentedTabs.Get("min-height")!.Text);
    Assert.Equal("0", segmentedTabs.Get("flex-shrink")!.Text);
    Assert.Equal("1px", segmentedTabs.Get("border-width")!.Text);
    var focusedButton = compiled.Theme.Resolve(new GbssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<GbssPseudoState>([GbssPseudoState.Focused])));
    Assert.Equal("1", focusedButton.Get("scale")!.Text);
    Assert.Equal("#f4f0e8", focusedButton.Get("outline-color")!.Text);
    Assert.Equal("-2px", focusedButton.Get("outline-offset")!.Text);
    Assert.True(focusedButton.Get("transition-duration") is null,
        "The built-in theme must not imply animation before the renderer interpolates transitions.");
    var eyebrow = compiled.Theme.Resolve(new GbssElement(
        "text",
        null,
        new HashSet<string>(["gbar-section-header__eyebrow"]),
        new HashSet<GbssPseudoState>()));
    Assert.Equal("none", eyebrow.Get("text-transform")!.Text);
    Assert.Equal("500", eyebrow.Get("font-weight")!.Text);
    foreach (var role in new[] { "button", "slider" })
    {
        foreach (var state in new[] { GbssPseudoState.Disabled, GbssPseudoState.Busy })
        {
            var stateStyle = compiled.Theme.Resolve(new GbssElement(
                role,
                null,
                new HashSet<string>(),
                new HashSet<GbssPseudoState>([state])));
            Assert.True(stateStyle.Get("opacity") is null,
                $"Built-in {role}:{state} opacity would compound the native accessibility factor.");
        }
    }
    return Task.CompletedTask;
}

static async Task ThemeManifestSafety()
{
    using var temp = new TemporaryDirectory();
    var theme = WriteTheme(temp.Path, "dev.example.bad", "Bad", "1.0.0", "button { color: #ffffff; }");
    var manifest = Path.Combine(theme, "theme.json");
    var catalog = Catalog(temp.Path);

    await File.WriteAllTextAsync(manifest, ThemeManifest("dev.example.other", "Bad", "1.0.0", "theme.gbss"));
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "theme_identity_mismatch");

    await File.WriteAllTextAsync(manifest, ThemeManifest("dev.example.bad", "Bad", "1.0.0", "../outside.gbss"));
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "invalid_theme_entry");

    await File.WriteAllTextAsync(manifest,
        ThemeManifest("dev.example.bad", "Bad", "1.0.0", "theme.gbss", ",\"unknown\":true"));
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "invalid_theme_manifest");

    await File.WriteAllBytesAsync(
        manifest,
        Enumerable.Repeat((byte)' ', ThemeCatalog.MaximumManifestBytes + 1).ToArray());
    Assert.HasCode(catalog.Load("dev.example.bad", "1.0.0").Diagnostics, "theme_manifest_too_large");
}

static Task ThemeSourceSafety()
{
    using var temp = new TemporaryDirectory();
    var theme = WriteTheme(temp.Path, "dev.example.link", "Link", "1.0.0", "button { color: #ffffff; }");
    var outside = Path.Combine(temp.Path, "outside.gbss");
    File.WriteAllText(outside, "button { color: #000000; }");
    var entry = Path.Combine(theme, "theme.gbss");
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
    Assert.HasCode(loaded.Diagnostics, "missing_import");
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

static Task ThemeLayerPrecedence()
{
    var platform = Package("platform.gbss", """
        :root { --choice: #111111; }
        button { color: var(--choice); background: #010101; }
        """);
    var widget = Package("widget.gbss", """
        :root { --choice: #222222; }
        #special { color: var(--choice); background: #020202; }
        """);
    var user = Package("user.gbss", """
        :root { --choice: #333333; }
        button { color: var(--choice); }
        """);
    var compiled = ThemeLayerCompiler.Compile(platform, widget, user);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
    var style = compiled.Theme!.Resolve(new GbssElement("button", "special"));
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
    Assert.Equal("#123456", valid.Current.Theme.Resolve(new GbssElement("button")).Get("color")!.Text);

    await File.WriteAllTextAsync(Path.Combine(themeDirectory, "theme.gbss"), "button { color: definitely-not-a-color; }");
    var invalid = await manager.ReloadAsync();
    Assert.True(!invalid.Published, "Invalid theme reload unexpectedly published.");
    Assert.Equal(revision, invalid.Current.Revision);
    Assert.Equal("#123456", manager.Current.Theme.Resolve(new GbssElement("button")).Get("color")!.Text);
    Assert.HasCode(manager.LastReloadDiagnostics, "invalid_value");

    await File.WriteAllTextAsync(Path.Combine(themeDirectory, "theme.gbss"), "button { color: #abcdef; }");
    var recovered = await manager.ReloadAsync();
    Assert.True(recovered.Published, Describe(recovered.Diagnostics));
    Assert.True(recovered.Current.Revision > revision, "Successful reload did not advance revision.");
    Assert.Equal("#abcdef", recovered.Current.Theme.Resolve(new GbssElement("button")).Get("color")!.Text);

    var widget = Package("widget.gbss", "button { background: #0a0b0c; color: #000000; }");
    var layered = recovered.Current.CompileForWidget(widget);
    Assert.True(layered.IsValid, Describe(layered.Diagnostics));
    var style = layered.Theme!.Resolve(new GbssElement("button"));
    Assert.Equal("#abcdef", style.Get("color")!.Text);
    Assert.Equal("#0a0b0c", style.Get("background")!.Text);
}

static PlatformSettingsStore Store(string root) =>
    new(new PlatformSettingsPaths(root));

static ThemeCatalog Catalog(string root) =>
    new(new PlatformSettingsPaths(root));

static string WriteTheme(string root, string id, string name, string version, string gbss)
{
    var directory = Path.Combine(new PlatformSettingsPaths(root).ThemesDirectory, id, version);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "theme.json"), ThemeManifest(id, name, version, "theme.gbss"));
    File.WriteAllText(Path.Combine(directory, "theme.gbss"), gbss);
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

static GbssPackageResult Package(string sourceName, string source)
{
    var parsed = GbssParser.Parse(source, sourceName);
    return new GbssPackageResult([parsed.Document], parsed.Diagnostics);
}

static string Describe(IEnumerable<GbssDiagnostic> diagnostics) =>
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

    public static void HasCode(IEnumerable<GbssDiagnostic> diagnostics, string code)
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
