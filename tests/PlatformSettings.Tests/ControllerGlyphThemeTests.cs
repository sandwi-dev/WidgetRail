using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

internal static class ControllerGlyphThemeTests
{
    public static Task Run()
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(
            Path.Combine(Path.GetTempPath(), "wrail-controller-glyph-theme-test")));
        foreach (var (id, version, color) in new[]
                 {
                     (ThemeIdentity.BuiltInDefault, ThemeIdentity.BuiltInDefaultVersion, "#b8ae92"),
                     (ThemeIdentity.BuiltInCoolSlate, ThemeIdentity.BuiltInCoolSlateVersion, "#9fb7d0"),
                     (ThemeIdentity.BuiltInNeonCircuit, ThemeIdentity.BuiltInNeonCircuitVersion, "#83c6d8"),
                     (ThemeIdentity.BuiltInArcadeRush, ThemeIdentity.BuiltInArcadeRushVersion, "#dba6cf"),
                     (ThemeIdentity.BuiltInRedline, ThemeIdentity.BuiltInRedlineVersion, "#e8a299"),
                     (ThemeIdentity.BuiltInMineralLight, ThemeIdentity.BuiltInMineralLightVersion, "#365c66"),
                     (ThemeIdentity.BuiltInCanopy, ThemeIdentity.BuiltInCanopyVersion, "#c6d9b5"),
                     (ThemeIdentity.BuiltInAmberSignal, ThemeIdentity.BuiltInAmberSignalVersion, "#e8ce9b"),
                     (ThemeIdentity.BuiltInVelvetDusk, ThemeIdentity.BuiltInVelvetDuskVersion, "#cbbbe2"),
                     (ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMonoInkVersion, "#e0e0e0"),
                 })
        {
            var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                new([], []), catalog.Load(id, version).Package);
            if (!compiled.IsValid) throw new InvalidOperationException("Invalid theme " + id);
            var glyph = compiled.Theme!.Resolve(new WrssElement("controllerGlyph", null,
                new HashSet<string>(["wrail-controller-glyph", "wrail-controller-hint__key"])));
            if (glyph.Get("font-size")?.Text != "24px" || glyph.Get("padding")?.Text != "0px" ||
                glyph.Get("border-width")?.Text != "0px" || glyph.Get("background")?.Text != "transparent" ||
                glyph.Get("color")?.Text != color)
                throw new InvalidOperationException("Controller symbol styling changed unexpectedly in " + id);
        }
        return Task.CompletedTask;
    }
}
