using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

internal static class WinUiShippingStylesTests
{
    public static Task Run()
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(
            Path.Combine(Path.GetTempPath(), "wrail-winui-shipping-styles-test")));
        Check(catalog.BuiltInDefault.Package, "built-in default", catalog.BuiltInDefault.Package);
        foreach (var theme in catalog.BuiltInThemes)
            Check(theme.Package, theme.Descriptor.Id, catalog.BuiltInDefault.Package);

        // The project copies the shipping widget styles as theme fixtures.
        // Check all of them so new sample/reference packages cannot bypass this gate.
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ThemeFixtures"), "*.wrss"))
        {
            var parsed = WrssParser.Parse(File.ReadAllText(path), path);
            Check(new WrssPackageResult([parsed.Document], parsed.Diagnostics), Path.GetFileName(path), catalog.BuiltInDefault.Package);
        }
        return Task.CompletedTask;
    }

    private static void Check(WrssPackageResult package, string name, WrssPackageResult baseline)
    {
        var compiled = ThemeLayerCompiler.Compile(baseline, package, new([], []));
        if (!compiled.IsValid)
            throw new InvalidOperationException($"{name}: {string.Join("; ", compiled.Diagnostics)}");
        var diagnostics = package.Documents.SelectMany(WinUiStyleDiagnostics.Analyze).ToArray();
        if (diagnostics.Length != 0)
            throw new InvalidOperationException($"{name}: {string.Join("; ", diagnostics.Select(value => value.ToString()))}");
    }
}
