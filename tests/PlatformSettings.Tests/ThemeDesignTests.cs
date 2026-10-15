using System.Globalization;
using System.Text.Json;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

// Palette measurements use the production cascade and the WinUI adapter's
// sRGB shading formula. They are not screenshots or WCAG certification.
internal static class ThemeDesignTests
{
    private static readonly string[] NewIds =
    [
        ThemeIdentity.BuiltInMineralLight, ThemeIdentity.BuiltInCanopy,
        ThemeIdentity.BuiltInAmberSignal, ThemeIdentity.BuiltInVelvetDusk,
        ThemeIdentity.BuiltInMonoInk,
    ];

    public static async Task Run()
    {
        // Known contrast/compositing pairs keep the measurement itself honest.
        Require(Math.Abs(Contrast(new(0, 0, 0), new(255, 255, 255)) - 21) < .0001, "Black/white contrast");
        Require(Math.Abs(Contrast(new(128, 128, 128), new(128, 128, 128)) - 1) < .0001, "Equal-color contrast");
        Require(Parse("rgba(255,255,255,0.5)").Over(new(0, 0, 0)).R == 127.5, "Alpha compositing");

        using var temp = new AuditDirectory();
        var paths = new PlatformSettingsPaths(temp.Path);
        var catalog = new ThemeCatalog(paths);
        var store = new PlatformSettingsStore(paths);
        var policy = new ThemeCatalogMutationPolicy(store, catalog);
        using var manager = new ThemeManager(store, catalog);
        var audit = Measure();
        Require(audit.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == audit.Count, "Theme IDs collide");
        Require(audit.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == audit.Count, "Theme names collide");

        foreach (var id in audit.Select(item => item.Id))
        {
            Require(ThemeIdentity.IsBuiltIn(id), id + " is not reserved");
            var entry = catalog.Load(id, "1.0.0");
            Require(entry.IsValid && entry.Diagnostics.Count == 0, id + " has diagnostics");
            Require(!catalog.Load(id, "9.9.9").IsValid, id + " accepted an unavailable version");
            await policy.SelectAsync(id, "1.0.0");
            var reloaded = await manager.ReloadAsync();
            Require(reloaded.Published && reloaded.Current.ActiveTheme.Id == id, id + " did not publish");
            Require((await store.LoadAsync()).Appearance.ThemeId == id, id + " did not persist");
            try
            {
                await policy.RetireAsync(id, "1.0.0");
                throw new InvalidOperationException(id + " is removable");
            }
            catch (PlatformSettingsException exception)
            {
                Require(exception.Code == "builtin_theme_protected", id + " has the wrong removal guard");
            }

            var sample = audit.Single(item => item.Id == id);
            foreach (var metric in sample.Metrics)
                Require(metric.Ratio >= metric.Minimum, $"{id}: {metric.Name} is {metric.Ratio:F2}:1; needs {metric.Minimum}:1");

            var focused = Resolve(reloaded.Current.Theme, "button", [], WrssPseudoState.Focused);
            Require(focused.Get("outline-width")?.Number >= 2, id + " has a thin focus edge");
            Require(Resolve(reloaded.Current.Theme, "button").Get("min-height")?.Number >= 44, id + " shrinks button targets");
            var busy = Resolve(reloaded.Current.Theme, "button", [], WrssPseudoState.Busy, WrssPseudoState.Focused);
            Require(busy.Get("outline-color")?.Text == focused.Get("outline-color")?.Text, id + " loses busy focus");

            // A selected theme must not override a widget's intentional branding.
            var parsed = WrssParser.Parse(":root { --accent: #123456; } button { background: #abcdef; }");
            var widget = reloaded.Current.CompileForWidget(new([parsed.Document], parsed.Diagnostics));
            Require(widget.IsValid, id + " rejects widget styles");
            Require(Resolve(widget.Theme!, "button").Get("background")?.Text == "#abcdef", id + " replaces widget fill");
            Require(Resolve(widget.Theme!, "progress").Get("color")?.Text == "#123456", id + " replaces widget token");
        }

        // Switching back rebuilds the cascade; it must not retain a new palette
        // or radius in an existing theme.
        var expected = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []),
            catalog.Load(ThemeIdentity.BuiltInNeonCircuit, "1.0.0").Package).Theme!;
        await policy.SelectAsync(ThemeIdentity.BuiltInNeonCircuit, "1.0.0");
        var restored = await manager.ReloadAsync();
        Require(restored.Published, "Switch-back did not publish");
        foreach (var role in new[] { "canvas", "panel", "button", "title" })
            Require(Resolve(expected, role).Properties.SequenceEqual(Resolve(restored.Current.Theme, role).Properties),
                role + " retained new theme styles after switching back");

        foreach (var item in audit)
            Console.WriteLine($"AUDIT {item.Name}: " + string.Join("; ", item.Metrics.Select(metric =>
                $"{metric.Name}={metric.Ratio:F2}:1{(metric.Ratio < metric.Minimum ? " [review]" : "")}")));
    }

    public static void WriteAudit(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(Measure(), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<ThemeAudit> Measure()
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(Path.GetTempPath(), "wrail-palette-audit")));
        var result = new List<ThemeAudit>();
        foreach (var entry in catalog.BuiltInThemes)
        {
            var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), entry.Package);
            Require(compiled.IsValid, entry.Descriptor.Id + " failed to compile");
            var theme = compiled.Theme!;
            var styles = new Dictionary<string, WrssResolvedStyle>
            {
                ["canvas"] = Resolve(theme, "canvas"),
                ["backdrop"] = Resolve(theme, "backdrop"),
                ["panel"] = Resolve(theme, "panel"),
                ["tray"] = Resolve(theme, "tray"),
                ["title"] = Resolve(theme, "title"),
                ["body"] = Resolve(theme, "body"),
                ["hint"] = Resolve(theme, "hint"),
                ["button"] = Resolve(theme, "button"),
                ["focused"] = Resolve(theme, "button", [], WrssPseudoState.Focused),
                ["selected"] = Resolve(theme, "button", [], WrssPseudoState.Selected, WrssPseudoState.Focused),
                ["disabled"] = Resolve(theme, "button", [], WrssPseudoState.Disabled),
                ["primary"] = Resolve(theme, "button", ["wrail-icon-button", "wrail-icon-button--primary"], WrssPseudoState.Focused),
                ["subtle"] = Resolve(theme, "panel", ["wrail-card", "wrail-card--subtle"]),
                ["modal"] = Resolve(theme, "panel", ["wrail-modal"]),
                ["glyph"] = Resolve(theme, "controllerGlyph", ["wrail-controller-glyph"]),
                ["scroll"] = Resolve(theme, "scroll"),
                ["posterScrim"] = Resolve(theme, "panel", ["wrail-poster-tile__scrim"]),
                ["posterTitle"] = Resolve(theme, "text", ["wrail-poster-tile__title"]),
                ["posterMetadata"] = Resolve(theme, "text", ["wrail-poster-tile__metadata"]),
                ["accent"] = Resolve(theme, "text", ["wrail-inline-link"]),
            };
            foreach (var status in new[] { "info", "success", "warning", "danger" })
                styles[status] = Resolve(theme, "text", ["wrail-badge__label", "wrail-badge__label--" + status]);

            // Conservative bounds: each ordinary surface over black AND white,
            // at both ends of its shading ramp. Modals include the theme veil.
            // Nested panels can improve the result; this is not native capture.
            var surfaces = new[] { "panel", "button", "focused", "selected", "subtle" }
                .SelectMany(name => Backgrounds(styles[name]))
                .Concat(Backgrounds(styles["modal"], Color(styles["backdrop"], "background"))).ToArray();
            double Ink(string name) => surfaces.Min(background => Contrast(Color(styles[name], "color").Over(background), background));
            double Own(string name, string property) => Backgrounds(styles[name]).Min(background =>
                Contrast(Color(styles[name], property).Over(background), background));
            var metrics = new List<Metric>
            {
                new("Primary text", Ink("body"), 4.5),
                new("Secondary text", Ink("hint"), 4.5),
                new("Accent text", Ink("accent"), 4.5),
                new("Status text", new[] { "info", "success", "warning", "danger" }.Min(Ink), 4.5),
                new("Controller glyph", Ink("glyph"), 3),
                new("Focus edge", new[] { "focused", "selected" }.Min(name => Own(name, "outline-color")), 3),
                new("Primary button ink", Own("primary", "color"), 4.5),
                new("Primary button focus", Own("primary", "outline-color"), 3),
                new("Poster copy", new[] { "posterTitle", "posterMetadata" }.Min(name =>
                    Backgrounds(styles["posterScrim"]).Min(background => Contrast(Color(styles[name], "color").Over(background), background))), 4.5),
                new("Scrollbar", Contrast(Color(styles["scroll"], "scrollbar-thumb-color"), Color(styles["scroll"], "scrollbar-track-color")), 3),
            };
            result.Add(new(entry.Descriptor.Id, entry.Descriptor.Name, NewIds.Contains(entry.Descriptor.Id), metrics,
                styles.ToDictionary(pair => pair.Key, pair => pair.Value.Properties.ToDictionary(property => property.Key, property => property.Value.Text))));
        }
        return result;
    }

    private static WrssResolvedStyle Resolve(WrssTheme theme, string role, string[]? classes = null, params WrssPseudoState[] states) =>
        theme.Resolve(new WrssElement(role, null, new HashSet<string>(classes ?? []), new HashSet<WrssPseudoState>(states)));

    private static IEnumerable<Rgba> Backgrounds(WrssResolvedStyle style, Rgba? veil = null)
    {
        var color = Color(style, "background");
        var shading = style.Get("surface-shading")?.Number ?? 0;
        foreach (var backdrop in new[] { new Rgba(0, 0, 0), new Rgba(255, 255, 255) })
        foreach (var amount in new[] { -Math.Abs(shading), 0, Math.Abs(shading) })
            yield return color.Shade(amount).Over(veil?.Over(backdrop) ?? backdrop);
    }

    private static Rgba Color(WrssResolvedStyle style, string property) =>
        Parse(style.Get(property)?.Text ?? throw new InvalidOperationException("Missing audit color " + property));

    private static Rgba Parse(string text)
    {
        if (text == "transparent") return new(0, 0, 0, 0);
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
            double Byte(int start) => int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new(Byte(0), Byte(2), Byte(4), hex.Length == 8 ? Byte(6) / 255 : 1);
        }
        var values = text[(text.IndexOf('(') + 1)..^1].Split(',')
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        return new(values[0], values[1], values[2], values.Length == 4 ? values[3] : 1);
    }

    private static double Contrast(Rgba first, Rgba second)
    {
        static double Luminance(Rgba color)
        {
            static double Linear(double channel) => channel / 255 <= .04045 ? channel / 255 / 12.92 : Math.Pow((channel / 255 + .055) / 1.055, 2.4);
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record ThemeAudit(string Id, string Name, bool IsNew, List<Metric> Metrics,
        Dictionary<string, Dictionary<string, string>> Styles);
    private sealed record Metric(string Name, double Ratio, double Minimum);
    private sealed class AuditDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("wrail-theme-design-");
        public string Path => directory.FullName;
        public void Dispose() => directory.Delete(recursive: true);
    }
    private readonly record struct Rgba(double R, double G, double B, double A = 1)
    {
        public Rgba Over(Rgba background) => new(R * A + background.R * (1 - A), G * A + background.G * (1 - A), B * A + background.B * (1 - A));
        public Rgba Shade(double amount)
        {
            double Channel(double value) => Math.Clamp(Math.Round(amount >= 0 ? value + (255 - value) * amount : value * (1 + amount)), 0, 255);
            return new(Channel(R), Channel(G), Channel(B), A);
        }
    }
}
