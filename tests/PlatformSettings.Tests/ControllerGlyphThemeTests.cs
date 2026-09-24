using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

internal static class ControllerGlyphThemeTests
{
    public static Task Run()
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(
            Path.Combine(Path.GetTempPath(), "wrail-controller-glyph-theme-test")));
        foreach (var (id, version) in new[]
                 {
                     (ThemeIdentity.BuiltInDefault, ThemeIdentity.BuiltInDefaultVersion),
                     (ThemeIdentity.BuiltInCoolSlate, ThemeIdentity.BuiltInCoolSlateVersion),
                     (ThemeIdentity.BuiltInNeonCircuit, ThemeIdentity.BuiltInNeonCircuitVersion),
                     (ThemeIdentity.BuiltInArcadeRush, ThemeIdentity.BuiltInArcadeRushVersion),
                     (ThemeIdentity.BuiltInRedline, ThemeIdentity.BuiltInRedlineVersion),
                 })
        {
            var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                new([], []), catalog.Load(id, version).Package);
            if (!compiled.IsValid) throw new InvalidOperationException("Invalid theme " + id);
            var glyph = compiled.Theme!.Resolve(new WrssElement("controllerGlyph", null,
                new HashSet<string>(["wrail-controller-glyph", "wrail-controller-hint__key"])));
            var canvas = compiled.Theme.Resolve(new WrssElement("canvas"));
            if (glyph.Get("font-size")?.Text != "24px" || glyph.Get("padding")?.Text != "0px" ||
                glyph.Get("border-width")?.Text != "0px" || glyph.Get("background")?.Text != "transparent" ||
                glyph.Get("color")?.Text != canvas.Get("color")?.Text)
                throw new InvalidOperationException("Controller symbol styling changed unexpectedly in " + id);
        }
        return Task.CompletedTask;
    }
}
