using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

internal static class FocusSelectionThemeTests
{
    public static Task Run()
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(
            Path.Combine(Path.GetTempPath(), "wrail-focus-selection-theme-test")));
        foreach (var theme in catalog.BuiltInThemes)
        {
            var shared = Compile(new([], []));
            Check(shared, "button", [], []);
            foreach (var role in new[] { "button", "select" })
            {
                var enabled = Resolve(shared, role, []);
                var disabled = Resolve(shared, role, [], WrssPseudoState.Disabled);
                Equal(null, disabled.Get("opacity")?.Text, theme.Descriptor.Id + " " + role + " avoids compound disabled opacity");
                if (enabled.Get("color")?.Text == disabled.Get("color")?.Text)
                    throw new InvalidOperationException(theme.Descriptor.Id + " " + role + " disabled ink must be distinct");
            }
            Equal(null, Resolve(shared, "button", ["wrail-action-surface"], WrssPseudoState.Disabled).Get("opacity")?.Text,
                theme.Descriptor.Id + " compound disabled content avoids dimming");
            Check(shared, "tray-item", [], []);
            Check(shared, "button", ["wrail-segmented-tabs__tab"], ["wrail-segmented-tabs__tab--selected"], false);
            Check(shared, "button", ["wrail-navigation-shell__compact-item"], ["wrail-navigation-shell__item--selected"], false);
            Check(shared, "button", ["wrail-navigation-shell__rail-item"], ["wrail-navigation-shell__item--selected"], false);
            Check(shared, "button", ["wrail-switch"], ["wrail-switch--on"], false);
            Check(shared, "button", ["wrail-choice-row"], ["wrail-choice-row--selected"], false);
            Check(shared, "button", ["wrail-choice-row"], ["wrail-choice-row--selected"]);
            Check(shared, "button", ["wrail-picker__option"], ["wrail-picker__option--selected"], false);
            CheckFillPreserved(shared, ["wrail-icon-button", "wrail-icon-button--primary"]);
            CheckDeclaredFocusFills(shared, catalog.BuiltInDefault.Package, "shared");
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ThemeFixtures"), "*.wrss"))
            {
                var parsed = WrssParser.Parse(File.ReadAllText(path), path);
                var package = new WrssPackageResult([parsed.Document], parsed.Diagnostics);
                CheckDeclaredFocusFills(Compile(package), package, Path.GetFileName(path));
            }

            var nowPlaying = Fixture("NowPlaying");
            Check(nowPlaying, "button", ["media-session-pill"], []);
            CheckFillPreserved(nowPlaying, ["media-play"]);
            var spotify = Fixture("Spotify");
            Check(spotify, "button", ["wrail-choice-row", "spotify-device-row"], ["wrail-choice-row--selected"], false);
            Check(spotify, "button", ["wrail-choice-row", "spotify-device-row"], ["wrail-choice-row--selected"]);
            CheckFillPreserved(spotify, ["spotify-play"]);
            var focusedStop = Resolve(spotify, "button", ["wrail-settings-row__action"], WrssPseudoState.Focused);
            var restingStop = Resolve(spotify, "button", ["wrail-settings-row__action"]);
            var selectedDevice = Resolve(spotify, "button", ["wrail-choice-row", "wrail-choice-row--selected", "spotify-device-row"]);
            Equal(restingStop.Get("background")?.Text, focusedStop.Get("background")?.Text,
                theme.Descriptor.Id + " Spotify Stop preserves its authored fill on focus");
            if (focusedStop.Get("background")?.Text == selectedDevice.Get("background")?.Text)
                throw new InvalidOperationException(theme.Descriptor.Id + " Spotify Stop resembles the selected device");
            var music = Fixture("YouTubeMusic");
            Check(music, "button", ["music-track", "wrail-action-surface"], []);
            Check(music, "button", ["music-filter"], []);
            CheckFillPreserved(music, ["music-play"]);
            Check(Fixture("Playnite"), "button", ["playnite-library-control"], []);
            var settings = Fixture("Settings");
            Check(settings, "button", ["setting-row", "wrail-switch"], ["wrail-switch--on"], false);
            Check(settings, "button", ["setting-row"], []);

            WrssTheme Compile(WrssPackageResult widget)
            {
                var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, widget, theme.Package);
                if (!compiled.IsValid) throw new InvalidOperationException(string.Join("; ", compiled.Diagnostics));
                return compiled.Theme!;
            }

            void CheckDeclaredFocusFills(WrssTheme compiled, WrssPackageResult package, string name)
            {
                foreach (var selector in package.Documents.SelectMany(document => document.Statements)
                    .OfType<WrssRule>().SelectMany(rule => rule.Selectors)
                    .Where(selector => !selector.IsRoot && selector.States.Contains(WrssPseudoState.Focused)))
                {
                    foreach (var persistent in new[] { Array.Empty<WrssPseudoState>(), [WrssPseudoState.Selected], [WrssPseudoState.Disabled] })
                    {
                        var states = selector.States.Where(state => state != WrssPseudoState.Focused).Concat(persistent).ToHashSet();
                        var element = new WrssElement(selector.Role is null or "*" ? "button" : selector.Role,
                            selector.Id, selector.Classes.ToHashSet(), states);
                        var before = compiled.Resolve(element);
                        var after = compiled.Resolve(new WrssElement(element.Role, element.Id, element.Classes,
                            states.Append(WrssPseudoState.Focused).ToHashSet()));
                        Equal(before.Get("background")?.Text, after.Get("background")?.Text,
                            $"{theme.Descriptor.Id}: {name} {selector.Text} focus preserves the existing fill ({string.Join(',', persistent)})");
                    }
                }
            }

            WrssTheme Fixture(string name)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "ThemeFixtures", name + ".wrss");
                var parsed = WrssParser.Parse(File.ReadAllText(path), path);
                return Compile(new([parsed.Document], parsed.Diagnostics));
            }

            void Check(WrssTheme compiled, string role, string[] classes, string[] selectedClasses, bool pseudo = true)
            {
                var selected = classes.Concat(selectedClasses).ToArray();
                var normal = Resolve(compiled, role, classes);
                var focused = Resolve(compiled, role, classes, WrssPseudoState.Focused);
                var selectedOnly = Resolve(compiled, role, selected, pseudo ? [WrssPseudoState.Selected] : []);
                var selectedFocused = Resolve(compiled, role, selected, pseudo
                    ? [WrssPseudoState.Selected, WrssPseudoState.Focused] : [WrssPseudoState.Focused]);
                var name = theme.Descriptor.Id + ": " + role + "." + string.Join(".", classes);
                Equal(normal.Get("background")?.Text, focused.Get("background")?.Text, name + " focus must retain the neutral fill");
                if (focused.Get("background")?.Text == selectedOnly.Get("background")?.Text)
                    throw new InvalidOperationException(name + " focus and selection have identical fills");
                Equal(selectedOnly.Get("background")?.Text, selectedFocused.Get("background")?.Text,
                    name + " focused selection must retain its persistent fill");
                Equal(focused.Get("outline-color")?.Text, selectedFocused.Get("outline-color")?.Text,
                    name + " focused selection must retain the focus edge");
                if (string.IsNullOrWhiteSpace(selectedFocused.Get("outline-color")?.Text))
                    throw new InvalidOperationException(name + " has no focus edge");
                var disabled = Resolve(compiled, role, selected, WrssPseudoState.Disabled);
                var disabledFocused = Resolve(compiled, role, selected, WrssPseudoState.Disabled, WrssPseudoState.Focused);
                Equal(disabled.Get("background")?.Text, disabledFocused.Get("background")?.Text, name + " disabled fill");
                Equal(disabled.Get("opacity")?.Text, disabledFocused.Get("opacity")?.Text, name + " disabled opacity");
            }

            void CheckFillPreserved(WrssTheme compiled, string[] classes)
            {
                Equal(Resolve(compiled, "button", classes).Get("background")?.Text,
                    Resolve(compiled, "button", classes, WrssPseudoState.Focused).Get("background")?.Text,
                    theme.Descriptor.Id + ": primary/brand fill " + string.Join(".", classes));
            }
        }
        return Task.CompletedTask;
    }

    private static WrssResolvedStyle Resolve(WrssTheme theme, string role, string[] classes, params WrssPseudoState[] states) =>
        theme.Resolve(new WrssElement(role, null, new HashSet<string>(classes), new HashSet<WrssPseudoState>(states)));

    private static void Equal(string? expected, string? actual, string context)
    {
        if (expected != actual) throw new InvalidOperationException($"{context}: expected '{expected}', actual '{actual}'.");
    }
}
