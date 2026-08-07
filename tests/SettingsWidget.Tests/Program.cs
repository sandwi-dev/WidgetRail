using GameBarAlternative.FirstPartyWidgets.Settings;
using GameBarAlternative.PlatformSettings;
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
        ["category.appearance", "category.accessibility", "category.overlay", "category.diagnostics", "category.reset"],
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
