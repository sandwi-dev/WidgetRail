using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

internal static class CodeTextThemeTests
{
    public static Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "gba-code-text-theme-test");
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(root));
        var compiled = WrssThemeCompiler.Compile(catalog.BuiltInDefault.Package);
        True(compiled.IsValid, string.Join(Environment.NewLine,
            compiled.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        var style = compiled.Theme!.Resolve(new WrssElement(
            "text",
            null,
            new HashSet<string>(["wrail-code-text"], StringComparer.Ordinal),
            new HashSet<WrssPseudoState>()));

        Equal("Consolas", style.Get("font-family")?.Text);
        Equal("0", style.Get("min-width")?.Text);
        Equal("13px", style.Get("font-size")?.Text);
        Equal("1.4", style.Get("line-height")?.Text);
        Equal("8", style.Get("max-lines")?.Text);
        Equal("clip", style.Get("text-overflow")?.Text);
        Equal("start", style.Get("text-align")?.Text);
        return Task.CompletedTask;
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
}
