using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetStyling;

var tests = new (string Name, Action Run)[]
{
    ("Parser preserves semantic selectors and source locations", ParserAndLocations),
    ("WinUI layout guidance retains authored locations without changing valid styles", WinUiLayoutGuidance),
    ("WinUI layout guidance ignores tokens and supported track inputs", WinUiSupportedLayout),
    ("Parser rejects browser and script escape syntax", UnsafeValues),
    ("Parser recovers independent declaration diagnostics", ErrorRecovery),
    ("Parser enforces bounded untrusted source sizes", ParserLimits),
    ("Package loader resolves safe imports deterministically", SafeImports),
    ("Package loader rejects traversal, missing files, and cycles", UnsafeImports),
    ("File sources enforce consumed bytes and verified digests", VerifiedFileSources),
    ("Variables resolve forward references and fallbacks", Variables),
    ("Variable cycles prevent theme publication", VariableCycles),
    ("Alpha colors multiply existing transparency without changing channels", AlphaColors),
    ("Alpha colors compose with token fallback and theme layers", AlphaVariables),
    ("Alpha color grammar errors retain source diagnostics and safe functions", AlphaErrors),
    ("Alpha color nesting and expansion remain bounded", AlphaLimits),
    ("Playnite translucent panels preserve descendant opacity", PlaynitePanelColors),
    ("Cascade applies specificity states and source order", Cascade),
    ("Explicit theme layers outrank selector specificity", LayerPrecedence),
    ("Selected disabled busy and focused states compose", InteractionStateComposition),
    ("Typed values clamp bounded renderer inputs", Clamping),
    ("Surface shading is typed bounded and theme-owned", SurfaceShading),
    ("Scrollbar parts have independent colors and bounded widths", ScrollbarStyles),
    ("Every built-in theme supplies scrollbar colors, depth and thin focus edges", BuiltinScrollbarPalettes),
    ("Pressed scale overrides focus enlargement under every built-in theme", PressedScalePalettes),
    ("Invalid typed values prevent theme publication", InvalidTypedValues),
    ("Resolved property enumeration is deterministic", DeterministicResolution),
    ("Media-card properties compile to typed renderer values", MediaCardValues),
    ("Responsive viewport units remain bounded", ResponsiveUnits),
    ("Translation lengths validate clamp and cascade by axis", TranslationValues),
    ("Responsive row wrapping compiles to a closed keyword contract", ResponsiveWrapValues),
    ("Per-edge borders validate and cascade independently", PerEdgeBorders),
    ("Media-card properties preserve the safe allowlist", MediaSafety),
    ("Default visual-system tokens compile exactly", VisualSystemTokens),
    ("Cool Slate built-in source compiles with distinct typed tokens", CoolSlateSource),
    ("Neon Circuit built-in source layers over platform state rules", NeonCircuitSource),
    ("Arcade Rush built-in source keeps pill geometry off controller targets", ArcadeRushSource),
    ("Redline built-in source adds a structural edge without geometry drift", RedlineSource),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
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

static void WinUiLayoutGuidance()
{
    var parsed = WrssParser.Parse("""
        :root { --basis: 80px; }
        .row, .row:focused {
          flex-wrap: wrap;
          flex-basis: var(--basis);
          flex-shrink: 0;
          flex-grow: 1;
        }
        """, "styles/layout.wrss");
    Assert.EmptyErrors(parsed.Diagnostics);
    var compiled = WrssThemeCompiler.Compile(new[] { parsed.Document });
    Assert.True(compiled.IsValid, "Guidance must not invalidate legal shared stylesheet syntax.");
    var diagnostics = WinUiStyleDiagnostics.Analyze(parsed.Document);
    Assert.Equal(3, diagnostics.Count);
    Assert.SequenceEqual(new[] { 3, 4, 5 }, diagnostics.Select(item => item.Line));
    Assert.True(diagnostics.All(item => item.Source == "styles/layout.wrss" && item.Column > 0 &&
        item.Code == "winui_unmapped_layout" && item.Severity == WrssDiagnosticSeverity.Warning), "Warnings retain declaration locations and stable codes.");
    Assert.True(diagnostics[0].Message.Contains("UI.ResponsiveGrid", StringComparison.Ordinal) &&
        diagnostics[1].Message.Contains("UI.Grid", StringComparison.Ordinal), "Warnings provide native authoring alternatives.");
    Assert.True(diagnostics.All(item => !item.Message.Contains("--basis", StringComparison.Ordinal)), "Guidance need not echo authored values.");
    var style = compiled.Theme!.Resolve(new WrssElement("row", StyleClasses: new HashSet<string> { "row" }));
    Assert.Equal("80px", style.Get("flex-basis")!.Text);
}

static void WinUiSupportedLayout()
{
    var parsed = WrssParser.Parse("""
        :root { --flex-wrap: wrap; --flex-basis: 80px; }
        .row { flex-grow: 1; width: 100%; min-width: 0px; max-width: 200px; gap: 8px; direction: row; align: stretch; }
        .row:focused { scale: 1.05; }
        """);
    Assert.EmptyErrors(parsed.Diagnostics);
    Assert.Equal(0, WinUiStyleDiagnostics.Analyze(parsed.Document).Count);
    Assert.Throws<ArgumentNullException>(() => WinUiStyleDiagnostics.Analyze(null!));
}

static void ParserAndLocations()
{
    var source = """
        :root {
          --accent: #8b5cf6;
        }

        button.primary#play:focused,
        .transport:pressed {
          outline-color: var(--accent);
        }
        """;
    var result = WrssParser.Parse(source, "styles/default.wrss");
    Assert.EmptyErrors(result.Diagnostics);
    var rules = result.Document.Statements.OfType<WrssRule>().ToArray();
    Assert.Equal(2, rules.Length);
    var selector = rules[1].Selectors[0];
    Assert.Equal("button", selector.Role);
    Assert.Equal("play", selector.Id);
    Assert.True(selector.Classes.Contains("primary"), "Expected primary class.");
    Assert.True(selector.States.Contains(WrssPseudoState.Focused), "Expected focused state.");
    Assert.Equal(121, selector.Specificity);
    Assert.Equal(5, rules[1].Location.Line);
    Assert.Equal(7, rules[1].Declarations[0].Location.Line);
}

static void UnsafeValues()
{
    var source = """
        button {
          background: url(https://bad.example/a.png);
          width: calc(100% - 4px);
          color: expression(alert(1));
          font-family: file:C:\secret.ttf;
          opacity: script(evil);
        }
        """;
    var result = WrssParser.Parse(source, "unsafe.wrss");
    var unsafeDiagnostics = result.Diagnostics.Where(item => item.Code == "unsafe_value").ToArray();
    Assert.Equal(5, unsafeDiagnostics.Length);
    Assert.Equal(2, unsafeDiagnostics[0].Line);
    Assert.True(unsafeDiagnostics.All(item => item.Column > 1), "Expected declaration-level columns.");
}

static void ErrorRecovery()
{
    var source = """
        button:hover { mystery: 3; }
        text { color #fff; }
        row { opacity: 0.5; }
        """;
    var result = WrssParser.Parse(source, "errors.wrss");
    Assert.HasCode(result.Diagnostics, "invalid_selector");
    Assert.HasCode(result.Diagnostics, "unknown_property");
    Assert.HasCode(result.Diagnostics, "invalid_declaration");
    Assert.True(result.Document.Statements.OfType<WrssRule>().Any(rule => rule.Selectors.Any(selector => selector.Role == "row")),
        "Parser did not recover to the final valid rule.");
}

static void ParserLimits()
{
    var oversized = new string(' ', WrssLimits.MaximumSourceCharacters + 1);
    var result = WrssParser.Parse(oversized, "oversized.wrss");
    Assert.HasCode(result.Diagnostics, "source_too_large");
    Assert.Equal(0, result.Document.Statements.Count);

    var declarations = string.Join(';', Enumerable.Range(0, WrssLimits.MaximumDeclarationsPerRule + 1)
        .Select(index => $"--v{index}: {index}"));
    result = WrssParser.Parse($":root {{ {declarations}; }}", "declarations.wrss");
    Assert.HasCode(result.Diagnostics, "too_many_declarations");
}

static void SafeImports()
{
    var provider = new DictionaryProvider(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["styles/default.wrss"] = "@import \"tokens.wrss\"; button { color: var(--brand); }",
        ["styles/tokens.wrss"] = ":root { --brand: #123456; } button { opacity: 0.8; }",
    });
    var package = WrssPackageLoader.Load("styles/default.wrss", provider);
    Assert.EmptyErrors(package.Diagnostics);
    Assert.SequenceEqual(["styles/tokens.wrss", "styles/default.wrss"], package.Documents.Select(item => item.Source));
    var compile = WrssThemeCompiler.Compile(package);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(Element("button"));
    Assert.Equal("#123456", style.Get("color")!.Text);
    Assert.Equal("0.8", style.Get("opacity")!.Text);
}

static void UnsafeImports()
{
    var traversal = WrssParser.Parse("@import \"../secret.wrss\";", "default.wrss");
    Assert.HasCode(traversal.Diagnostics, "unsafe_import");

    var missing = WrssPackageLoader.Load("default.wrss", new DictionaryProvider(new Dictionary<string, string>
    {
        ["default.wrss"] = "@import \"missing.wrss\";",
    }));
    Assert.HasCode(missing.Diagnostics, "missing_import");
    var missingCompile = WrssThemeCompiler.Compile(missing);
    Assert.True(!missingCompile.IsValid && missingCompile.Theme is null, "A package with a missing import published a theme.");

    var cycle = WrssPackageLoader.Load("a.wrss", new DictionaryProvider(new Dictionary<string, string>
    {
        ["a.wrss"] = "@import \"b.wrss\";",
        ["b.wrss"] = "@import \"a.wrss\";",
    }));
    Assert.HasCode(cycle.Diagnostics, "import_cycle");
}

static void VerifiedFileSources()
{
    Assert.Equal(0, typeof(WrssSourceReadResult).GetConstructors().Length);
    _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
        WrssSourceReadResult.Failure(WrssSourceReadStatus.Success));

    using var temporary = new TemporaryDirectory();
    var styles = Path.Combine(temporary.Path, "styles");
    Directory.CreateDirectory(styles);
    var path = Path.Combine(styles, "default.wrss");
    var bytes = Encoding.UTF8.GetBytes("button { color: #123456; }");
    File.WriteAllBytes(path, bytes);
    var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    var verified = new WrssFileSourceProvider(
        temporary.Path,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["styles/default.wrss"] = digest,
        });
    var verifiedRead = verified.Read("styles/default.wrss");
    Assert.Equal(WrssSourceReadStatus.Success, verifiedRead.Status);
    Assert.True(verifiedRead.Source is not null,
        "Exact verified WRSS was rejected.");
    Assert.Equal("button { color: #123456; }", verifiedRead.Source!);

    var mismatched = new WrssFileSourceProvider(
        temporary.Path,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["styles/default.wrss"] = new string('0', 64),
        });
    Assert.Equal(
        WrssSourceReadStatus.DigestMismatch,
        mismatched.Read("styles/default.wrss").Status);

    var importedBytes = Encoding.UTF8.GetBytes("button { opacity: 0.5; }");
    var entryWithImportBytes = Encoding.UTF8.GetBytes("@import \"tokens.wrss\";");
    File.WriteAllBytes(path, entryWithImportBytes);
    File.WriteAllBytes(Path.Combine(styles, "tokens.wrss"), importedBytes);
    var verifiedImports = WrssPackageLoader.Load(
        "styles/default.wrss",
        new WrssFileSourceProvider(
            temporary.Path,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["styles/default.wrss"] = Convert.ToHexString(
                    SHA256.HashData(entryWithImportBytes)).ToLowerInvariant(),
                ["styles/tokens.wrss"] = Convert.ToHexString(
                    SHA256.HashData(importedBytes)).ToLowerInvariant(),
            }));
    Assert.EmptyErrors(verifiedImports.Diagnostics);

    var entryBytes = Encoding.UTF8.GetBytes("@import \"late.wrss\";");
    File.WriteAllBytes(path, entryBytes);
    File.WriteAllText(Path.Combine(styles, "late.wrss"), "button { opacity: 0.5; }");
    var inventory = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["styles/default.wrss"] = Convert.ToHexString(SHA256.HashData(entryBytes)).ToLowerInvariant(),
    };
    var lateImport = WrssPackageLoader.Load(
        "styles/default.wrss",
        new WrssFileSourceProvider(temporary.Path, inventory));
    Assert.HasCode(lateImport.Diagnostics, "digest_mismatch");

    var bomBytes = new byte[] { 0xef, 0xbb, 0xbf }
        .Concat(Encoding.UTF8.GetBytes("button { color: #abcdef; }")).ToArray();
    File.WriteAllBytes(path, bomBytes);
    Assert.Equal(
        "button { color: #abcdef; }",
        new WrssFileSourceProvider(temporary.Path).Read("styles/default.wrss").Source!);

    File.WriteAllBytes(path, [0xff]);
    Assert.Equal(
        WrssSourceReadStatus.InvalidEncoding,
        new WrssFileSourceProvider(temporary.Path).Read("styles/default.wrss").Status);

    File.WriteAllBytes(path, new byte[(int)WrssLimits.MaximumSourceBytes + 1]);
    Assert.Equal(
        WrssSourceReadStatus.TooLarge,
        new WrssFileSourceProvider(temporary.Path).Read("styles/default.wrss").Status);

    using var misleading = new MisreportedLengthStream(
        new byte[(int)WrssLimits.MaximumSourceBytes + 1], reportedLength: 1);
    Assert.Equal(
        WrssSourceReadStatus.TooLarge,
        Assert.Throws<WrssSourceReadException>(() =>
            WrssFileSourceProvider.ReadBounded(misleading)).Status);

    using var changing = new MisreportedLengthStream(
        [0x20], reportedLength: 1, lengthAfterRead: 2);
    Assert.Equal(
        WrssSourceReadStatus.ChangedDuringRead,
        Assert.Throws<WrssSourceReadException>(() =>
            WrssFileSourceProvider.ReadBounded(changing)).Status);

    foreach (var (status, code) in new[]
    {
        (WrssSourceReadStatus.Missing, "missing_import"),
        (WrssSourceReadStatus.UnsafePath, "unsafe_import"),
        (WrssSourceReadStatus.TooLarge, "source_too_large"),
        (WrssSourceReadStatus.ChangedDuringRead, "source_changed"),
        (WrssSourceReadStatus.InvalidEncoding, "invalid_encoding"),
        (WrssSourceReadStatus.DigestMismatch, "digest_mismatch"),
        (WrssSourceReadStatus.IoUnavailable, "source_unavailable"),
    })
        Assert.HasCode(
            WrssPackageLoader.Load(
                "styles/default.wrss", new ResultProvider(WrssSourceReadResult.Failure(status)))
                .Diagnostics,
            code);

    var throwing = WrssPackageLoader.Load(
        "styles/default.wrss", new ThrowingProvider());
    Assert.HasCode(throwing.Diagnostics, "source_unavailable");
    Assert.True(
        throwing.Diagnostics.All(item => !item.Message.Contains("provider-secret", StringComparison.Ordinal)),
        "A provider exception escaped into a WRSS diagnostic.");
}

static void Variables()
{
    var compile = Compile("""
        :root {
          --foreground: var(--brand);
          --brand: #abcdef;
        }
        text { color: var(--foreground); }
        button { outline-color: var(--missing, #010203); }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    Assert.Equal("#abcdef", compile.Theme!.Resolve(Element("text")).Get("color")!.Text);
    Assert.Equal("#010203", compile.Theme.Resolve(Element("button")).Get("outline-color")!.Text);
}

static void VariableCycles()
{
    var compile = Compile("""
        :root { --one: var(--two); --two: var(--one); }
        text { color: var(--one); }
        """);
    Assert.True(!compile.IsValid, "Variable cycle unexpectedly compiled.");
    Assert.True(compile.Diagnostics.Any(item => item.Code is "variable_cycle" or "invalid_variable"), Describe(compile.Diagnostics));
}

static void AlphaColors()
{
    foreach (var (input, expected) in new[]
    {
        ("alpha(#123, .84)", "rgba(17, 34, 51, 0.84)"),
        ("alpha(#aBcD, .5)", "rgba(170, 187, 204, 0.4333333333333333)"),
        ("alpha(#10203080, 1)", "rgba(16, 32, 48, 0.5019607843137255)"),
        ("alpha(rgb(10%, 12.5, 255), 25%)", "rgba(10%, 12.5, 255, 0.25)"),
        ("alpha(rgba(23, 26, 34, .98), .84)", "rgba(23, 26, 34, 0.8232)"),
        ("alpha(rgba(1, 2, 3, 50%), .5)", "rgba(1, 2, 3, 0.25)"),
        ("alpha(alpha(rgba(1, 2, 3, .8), .5), .25)", "rgba(1, 2, 3, 0.1)"),
        ("alpha(transparent, .5)", "rgba(0, 0, 0, 0)"),
        ("alpha(#abcdef, 0)", "rgba(171, 205, 239, 0)"),
    })
    {
        var compile = Compile($"button {{ background: {input}; }}");
        Assert.True(compile.IsValid, Describe(compile.Diagnostics));
        var computed = compile.Theme!.Resolve(Element("button")).Get("background")!;
        Assert.Equal(WrssValueKind.Color, computed.Kind);
        var separator = expected.LastIndexOf(',');
        var actualSeparator = computed.Text.LastIndexOf(',');
        Assert.Equal(expected[..separator], computed.Text[..actualSeparator]);
        var expectedAlpha = double.Parse(expected[(separator + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        var actualAlpha = double.Parse(computed.Text[(actualSeparator + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Math.Abs(expectedAlpha - actualAlpha) < 1e-14, $"Wrong multiplied alpha for {input}: {computed.Text}");
        // Frontends receive only an existing RGBA color; round-trip it through
        // the old typed-value path, without understanding alpha() themselves.
        Assert.True(Compile($"button {{ background: {computed.Text}; }}").IsValid, "Computed RGBA did not round-trip.");
    }
}

static void AlphaVariables()
{
    var package = WrssParser.Parse("""
        :root { --factor: .84; --panel: alpha(var(--tone), var(--factor)); --tone: var(--surface); }
        button { background: var(--panel); border-color: alpha(var(--missing, var(--other, rgba(1, 2, 3, .5))), .2); }
        button:focused { outline-color: alpha(var(--accent, #123456), 50%); }
        """, "package.wrss");
    var theme = WrssParser.Parse(":root { --surface: rgba(10, 20, 30, .75); --accent: #123456; }", "theme.wrss");
    Assert.EmptyErrors(package.Diagnostics);
    var compile = WrssThemeCompiler.Compile([new WrssThemeLayer(0, [theme.Document]), new WrssThemeLayer(1, [package.Document])]);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(new WrssElement("button", PseudoStates: new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("rgba(10, 20, 30, 0.63)", style.Get("background")!.Text);
    Assert.Equal("rgba(1, 2, 3, 0.1)", style.Get("border-color")!.Text);
    Assert.Equal("rgba(18, 52, 86, 0.5)", style.Get("outline-color")!.Text);
    var cycle = Compile(":root { --one: alpha(var(--two), .5); --two: var(--one); } button { background: var(--one); }");
    Assert.True(!cycle.IsValid, "Color expression bypassed variable cycle validation.");
    var missing = Compile("button { background: alpha(var(--missing), .5); }");
    Assert.HasCode(missing.Diagnostics, "invalid_variable");
}

static void AlphaErrors()
{
    foreach (var expression in new[] { "alpha(#fff)", "alpha(#fff, .5, .6)", "alpha(, .5)", "alpha(#fff,)",
        "alpha(#fff, 1.01)", "alpha(#fff, -0.1)", "alpha(#fff, 101%)", "alpha(#fff, NaN)",
        "alpha(#fff, Infinity)", "alpha(#fff, 1e999)", "alpha(#fff, 4px)", "alpha(12px, .5)",
        "alpha(rgb(256, 0, 0), .5)", "alpha(rgba(0,0,0,2), .5)", "alpha(#12345, .5)",
        "alpha(#fff, alpha(#000, .5))", "alpha(#fff, .5) trailing", "alpha((#fff), .5)" })
    {
        var parsed = WrssParser.Parse($"button {{\n  background: {expression};\n}}", "alpha-errors.wrss");
        Assert.EmptyErrors(parsed.Diagnostics);
        var compile = WrssThemeCompiler.Compile([parsed.Document]);
        Assert.True(!compile.IsValid && compile.Theme is null, $"Invalid color compiled: {expression}");
        Assert.HasCode(compile.Diagnostics, "invalid_value");
        var error = compile.Diagnostics.First(item => item.Code == "invalid_value");
        Assert.Equal("alpha-errors.wrss", error.Source);
        Assert.Equal(2, error.Line);
        Assert.Equal(3, error.Column);
    }
    Assert.True(!Compile("button { opacity: alpha(#fff, .5); }").IsValid, "A color function escaped the color property family.");
    Assert.HasCode(WrssParser.Parse("button { background: alpha(url(https://bad.example/a), .5); }", "unsafe.wrss").Diagnostics, "unsafe_value");
    Assert.HasCode(WrssParser.Parse("button { background: alpha(calc(1 + 2), .5); }", "unsafe.wrss").Diagnostics, "unsafe_value");
    Assert.HasCode(WrssParser.Parse("button { background: alpha(#fff, .5; }", "malformed.wrss").Diagnostics, "unbalanced_parenthesis");
}

static void AlphaLimits()
{
    static string Nest(int count) => string.Concat(Enumerable.Repeat("alpha(", count)) + "#fff" +
        string.Concat(Enumerable.Repeat(", 1)", count));
    Assert.True(Compile($"button {{ color: {Nest(WrssLimits.MaximumColorFunctionNesting)}; }}").IsValid, "Maximum color nesting rejected.");
    var excessive = Compile($"button {{ color: {Nest(WrssLimits.MaximumColorFunctionNesting + 1)}; }}");
    Assert.HasCode(excessive.Diagnostics, "invalid_value");
    Assert.True(excessive.Diagnostics.Any(item => item.Message.Contains("nested alpha()", StringComparison.Ordinal)), "Missing nesting-limit diagnostic.");
    // Limits apply to expressions introduced through token expansion too.
    var expanded = Compile($":root {{ --a: {Nest(32)}; }} button {{ color: alpha(var(--a), .5); }}");
    Assert.True(!expanded.IsValid, "Token substitution bypassed color nesting limit.");
    var large = new string(' ', 4000) + "#fff";
    var oversized = Compile($":root {{ --a: '{large}'; --b: var(--a) var(--a) var(--a); --c: var(--b) var(--b); }} button {{ background: alpha(var(--c), .5); }}");
    Assert.HasCode(oversized.Diagnostics, "invalid_variable");
    Assert.True(oversized.Diagnostics.Any(item => item.Message.Contains("Expanded value exceeds", StringComparison.Ordinal)), "Expanded color bypassed byte-work bound.");
}

static void PlaynitePanelColors()
{
    var parsed = WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Themes", "playnite-library.wrss")), "playnite.wrss");
    Assert.EmptyErrors(parsed.Diagnostics);
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
    foreach (var panel in new[] { "browse-foreground", "home-actions", "home-summary" })
    {
        var style = compiled.Theme!.Resolve(new WrssElement("stack", StyleClasses: new HashSet<string>(["playnite-library-" + panel])));
        Assert.Equal("rgba(23, 26, 34, 0.8232)", style.Get("background")!.Text);
        Assert.Equal("rgba(44, 49, 62, 0.7728)", style.Get("border-color")!.Text);
        Assert.True(style.Get("opacity") is null, "Panel applies opacity to descendants.");
    }
    var hint = compiled.Theme!.Resolve(new WrssElement("text", StyleClasses: new HashSet<string>(["playnite-library-hint-unavailable"])));
    Assert.Equal("0.64", hint.Get("opacity")!.Text);
}

static void Cascade()
{
    var compile = Compile("""
        button { color: #111111; scale: 1; }
        .primary { color: #222222; }
        button.primary:focused { color: #333333; scale: 1.1; }
        #play { color: #444444; }
        #play { color: #555555; }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var focused = new WrssElement(
        "button",
        "play",
        new HashSet<string>(["primary"], StringComparer.Ordinal),
        new HashSet<WrssPseudoState> { WrssPseudoState.Focused });
    var style = compile.Theme!.Resolve(focused);
    Assert.Equal("#555555", style.Get("color")!.Text);
    Assert.Equal("1.1", style.Get("scale")!.Text);

    var unfocused = compile.Theme.Resolve(new WrssElement("button", null, new HashSet<string>(["primary"]), null));
    Assert.Equal("#222222", unfocused.Get("color")!.Text);
    Assert.Equal("1", unfocused.Get("scale")!.Text);
}

static void LayerPrecedence()
{
    var platform = WrssParser.Parse("#play { color: #111111; }", "platform.wrss");
    var widget = WrssParser.Parse("#play { color: #222222; }", "widget.wrss");
    var user = WrssParser.Parse("button { color: #333333; }", "user.wrss");
    Assert.EmptyErrors(platform.Diagnostics.Concat(widget.Diagnostics).Concat(user.Diagnostics));
    var compile = WrssThemeCompiler.Compile(
    [
        new WrssThemeLayer(0, [platform.Document]),
        new WrssThemeLayer(100, [widget.Document]),
        new WrssThemeLayer(200, [user.Document]),
    ]);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(new WrssElement("button", "play"));
    Assert.Equal("#333333", style.Get("color")!.Text);
}

static void Clamping()
{
    var compile = Compile("""
        button {
          opacity: 4;
          scale: 9;
          background-blur: 500px;
          transition-duration: 99s;
          padding: 999px -5px;
        }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    Assert.Equal(5, compile.Diagnostics.Count(item => item.Code == "value_clamped"));
    var style = compile.Theme!.Resolve(Element("button"));
    Assert.Equal("1", style.Get("opacity")!.Text);
    Assert.Equal("2", style.Get("scale")!.Text);
    Assert.Equal("64px", style.Get("background-blur")!.Text);
    Assert.Equal("2000ms", style.Get("transition-duration")!.Text);
    Assert.Equal("256px 0px", style.Get("padding")!.Text);
}

static void ScrollbarStyles()
{
    var compile = Compile("""
        :root { --track: #123456; --thumb: #abcdef; }
        scroll { scrollbar-track-color: var(--track); scrollbar-thumb-color: var(--thumb); scrollbar-width: 4px; }
        #thin { scrollbar-width: 0px; }
        #wide { scrollbar-width: 100px; }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(new WrssElement("scroll", "list"));
    Assert.Equal("#123456", style.Get("scrollbar-track-color")!.Text);
    Assert.Equal("#abcdef", style.Get("scrollbar-thumb-color")!.Text);
    Assert.Equal("4px", style.Get("scrollbar-width")!.Text);
    Assert.Equal("2px", compile.Theme.Resolve(new WrssElement("scroll", "thin")).Get("scrollbar-width")!.Text);
    Assert.Equal("8px", compile.Theme.Resolve(new WrssElement("scroll", "wide")).Get("scrollbar-width")!.Text);
    Assert.True(!WrssParser.Parse("scroll { scrollbar-thumb-color: url(file:///secret); }", "unsafe.wrss").IsValid,
        "Scrollbar colors must not admit external resources.");
}

static void BuiltinScrollbarPalettes()
{
    var baseline = WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Themes", "builtin-default.wrss")), "builtin-default.wrss");
    Assert.EmptyErrors(baseline.Diagnostics);
    foreach (var name in new[] { "default", "cool-slate", "arcade-rush", "neon-circuit", "redline" })
    {
        var palette = WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Themes", $"builtin-{name}.wrss")), $"builtin-{name}.wrss");
        Assert.EmptyErrors(palette.Diagnostics);
        var compiled = WrssThemeCompiler.Compile([
            new WrssThemeLayer(0, [baseline.Document]), new WrssThemeLayer(200, [palette.Document])]);
        Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
        var scroll = compiled.Theme!.Resolve(new WrssElement("scroll", "items"));
        Assert.Equal("4px", scroll.Get("scrollbar-width")!.Text);
        Assert.True(scroll.Get("scrollbar-thumb-color")!.Text != scroll.Get("scrollbar-track-color")!.Text,
            $"Theme {name} must distinguish the thumb from its track.");
        foreach (var styleClass in new[] { "wrail-switch", "wrail-stepper__button", "wrail-tile", "wrail-choice-row" })
        {
            var focus = compiled.Theme.Resolve(new WrssElement("button", null,
                new HashSet<string> { styleClass }, new HashSet<WrssPseudoState> { WrssPseudoState.Focused }));
            Assert.Equal("1.5px", focus.Get("outline-width")!.Text);
            Assert.True(focus.Get("surface-shading")!.Number > 0, $"{name}/{styleClass} has focused surface emphasis.");
        }
        var raised = compiled.Theme.Resolve(new WrssElement("stack", null,
            new HashSet<string> { "wrail-surface-raised" }, null));
        var inset = compiled.Theme.Resolve(new WrssElement("row", null,
            new HashSet<string> { "wrail-surface-inset" }, null));
        Assert.True(raised.Get("surface-shading")!.Number > 0 && inset.Get("surface-shading")!.Number < 0,
            $"{name} distinguishes raised panels from inset navigation tracks.");
        var flat = compiled.Theme.Resolve(new WrssElement("stack", null,
            new HashSet<string> { "wrail-card", "wrail-surface-flat" }, null));
        Assert.Equal(0d, flat.Get("surface-shading")!.Number!.Value);
        Assert.Equal("transparent", flat.Get("shadow-color")!.Text);
    }
}

static void PressedScalePalettes()
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "WidgetStyling")))
        root = root.Parent;
    Assert.True(root is not null, "Repository sources are required for package-style validation.");
    var baseline = WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Themes", "builtin-default.wrss")), "builtin-default.wrss");
    var targets = new[]
    {
        ("", "button", "ordinary-button"),
        ("", "action-surface", "ordinary-tile"),
        ("src/FirstPartyWidgets/GamesAppsWidget", "action-surface", "games-app-tile"),
        ("src/FirstPartyWidgets/SettingsWidget", "action-surface", "category-card"),
        ("samples/PlayniteLibraryWidget", "action-surface", "playnite-library-tile"),
        ("samples/YtMusicWidget", "action-surface", "music-home-poster"),
        ("samples/YtMusicWidget", "button", "music-play"),
        ("samples/SpotifyWidget", "button", "spotify-play"),
        ("samples/ClockWidget", "button", "primary-action"),
    };
    foreach (var name in new[] { "default", "cool-slate", "arcade-rush", "neon-circuit", "redline" })
    foreach (var (directory, role, styleClass) in targets)
    {
        var palette = WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Themes", $"builtin-{name}.wrss")), name);
        var layers = new List<WrssThemeLayer> { new(0, [baseline.Document]), new(100, [palette.Document]) };
        if (directory.Length > 0)
        {
            var package = WrssPackageLoader.Load("default.wrss",
                new WrssFileSourceProvider(Path.Combine(root!.FullName, directory, "styles")));
            Assert.True(package.IsValid, Describe(package.Diagnostics));
            layers.Add(new(200, package.Documents));
        }
        var compiled = WrssThemeCompiler.Compile(layers);
        Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
        var classes = new HashSet<string> { styleClass };
        if (role == "action-surface") classes.Add("wrail-action-surface");
        var focused = compiled.Theme!.Resolve(new WrssElement(role, null, classes,
            new HashSet<WrssPseudoState> { WrssPseudoState.Focused }));
        var pressed = compiled.Theme.Resolve(new WrssElement(role, null, classes,
            new HashSet<WrssPseudoState> { WrssPseudoState.Focused, WrssPseudoState.Pressed }));
        Assert.Equal("0.96", pressed.Get("scale")?.Text);
        Assert.Equal(focused.Get("width")?.Text, pressed.Get("width")?.Text);
        Assert.Equal(focused.Get("height")?.Text, pressed.Get("height")?.Text);
        Assert.True(pressed.Get("transition-duration") is not null, $"Missing press timing for {styleClass}.");
    }
}

static void InteractionStateComposition()
{
    var compile = Compile("""
        .primary:selected { border-width: 3px; }
        .primary:disabled { opacity: 0.4; }
        .primary:busy { color: #59d5ff; }
        .primary:selected:focused { scale: 1.1; }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var element = new WrssElement(
        "button",
        "play",
        new HashSet<string>(["primary"], StringComparer.Ordinal),
        new HashSet<WrssPseudoState>
        {
            WrssPseudoState.Selected,
            WrssPseudoState.Disabled,
            WrssPseudoState.Busy,
            WrssPseudoState.Focused,
        });
    var style = compile.Theme!.Resolve(element);
    Assert.Equal("3px", style.Get("border-width")!.Text);
    Assert.Equal("0.4", style.Get("opacity")!.Text);
    Assert.Equal("#59d5ff", style.Get("color")!.Text);
    Assert.Equal("1.1", style.Get("scale")!.Text);
}

static void InvalidTypedValues()
{
    var compile = Compile("""
        button {
          color: red;
          opacity: very;
          transition-duration: fast;
          font-weight: 555;
          background: rgb(999, 0, 0);
          overflow-wrap: sometimes;
        }
        """);
    Assert.True(!compile.IsValid, "Invalid typed values unexpectedly compiled.");
    Assert.Equal(6, compile.Diagnostics.Count(item => item.Code == "invalid_value"));
}

static void DeterministicResolution()
{
    var compile = Compile("button { width: 24px; color: #ffffff; opacity: 0.75; }");
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var first = compile.Theme!.Resolve(Element("button"));
    var second = compile.Theme.Resolve(Element("button"));
    Assert.SequenceEqual(["color", "opacity", "width"], first.Properties.Keys);
    Assert.SequenceEqual(
        first.Properties.Select(item => $"{item.Key}={item.Value.Text}"),
        second.Properties.Select(item => $"{item.Key}={item.Value.Text}"));
}

static void MediaCardValues()
{
    var compile = Compile("""
        image.album-art {
          width: 24vw;
          min-width: 120px;
          max-width: 360px;
          aspect-ratio: 1/1;
          object-fit: cover;
          object-position: center;
          shape: rounded;
          corner-radius: 16px;
          image-tint: rgba(255, 255, 255, 0.92);
          flex-grow: 0;
          flex-shrink: 0;
          flex-basis: 184px;
        }
        text.track-title {
          font-size: 24px;
          font-weight: 700;
          line-height: 1.2;
          max-lines: 2;
          text-overflow: ellipsis;
          overflow-wrap: anywhere;
          text-transform: none;
          flex-grow: 1;
          flex-shrink: 1;
          flex-basis: auto;
        }
        button:focused {
          outline-offset: 3px;
          transition-duration: 120ms;
          transition-easing: ease-out;
        }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var art = compile.Theme!.Resolve(new WrssElement("image", null, new HashSet<string>(["album-art"]), null));
    Assert.Equal(WrssValueKind.Length, art.Get("width")!.Kind);
    Assert.Equal("24vw", art.Get("width")!.Text);
    Assert.Equal(WrssValueKind.Ratio, art.Get("aspect-ratio")!.Kind);
    Assert.Equal("1", art.Get("aspect-ratio")!.Text);
    Assert.Equal("cover", art.Get("object-fit")!.Text);
    Assert.Equal("rounded", art.Get("shape")!.Text);
    Assert.Equal("184px", art.Get("flex-basis")!.Text);
    Assert.Equal("0", art.Get("flex-shrink")!.Text);

    var title = compile.Theme.Resolve(new WrssElement("text", null, new HashSet<string>(["track-title"]), null));
    Assert.Equal(WrssValueKind.Integer, title.Get("max-lines")!.Kind);
    Assert.Equal("2", title.Get("max-lines")!.Text);
    Assert.Equal("1.2", title.Get("line-height")!.Text);
    Assert.Equal("ellipsis", title.Get("text-overflow")!.Text);
    Assert.Equal("anywhere", title.Get("overflow-wrap")!.Text);
}

static void ResponsiveUnits()
{
    var compile = Compile("card { width: 130vw; height: 36vh; aspect-ratio: 16/1; max-lines: 999; line-height: 5; flex-grow: 99; }");
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    Assert.Equal(5, compile.Diagnostics.Count(item => item.Code == "value_clamped"));
    var style = compile.Theme!.Resolve(Element("card"));
    Assert.Equal("100vw", style.Get("width")!.Text);
    Assert.Equal("36vh", style.Get("height")!.Text);
    Assert.Equal("5", style.Get("aspect-ratio")!.Text);
    Assert.Equal("128", style.Get("max-lines")!.Text);
    Assert.Equal("3", style.Get("line-height")!.Text);
    Assert.Equal("8", style.Get("flex-grow")!.Text);
}

static void TranslationValues()
{
    var compile = Compile("""
        card {
          translate-x: -12.5vw;
          translate-y: 25%;
        }
        card:focused {
          translate-x: 1.5em;
        }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var baseStyle = compile.Theme!.Resolve(Element("card"));
    Assert.Equal(WrssValueKind.Length, baseStyle.Get("translate-x")!.Kind);
    Assert.Equal("-12.5vw", baseStyle.Get("translate-x")!.Text);
    Assert.Equal("25%", baseStyle.Get("translate-y")!.Text);

    var focused = compile.Theme.Resolve(new WrssElement(
        "card", null, null, new HashSet<WrssPseudoState> { WrssPseudoState.Focused }));
    Assert.Equal("1.5em", focused.Get("translate-x")!.Text);
    Assert.Equal("25%", focused.Get("translate-y")!.Text);

    var bounded = Compile("card { translate-x: 5000px; translate-y: -120vh; }");
    Assert.Equal(2, bounded.Diagnostics.Count(item => item.Code == "value_clamped"));
    var boundedStyle = bounded.Theme!.Resolve(Element("card"));
    Assert.Equal("4096px", boundedStyle.Get("translate-x")!.Text);
    Assert.Equal("-100vh", boundedStyle.Get("translate-y")!.Text);

    var invalid = CompileExpectingErrors("card { translate-x: auto; translate-y: 10deg; }");
    Assert.Equal(2, invalid.Diagnostics.Count(item => item.Code == "invalid_value"));
    Assert.True(WrssPropertyCatalog.AllowedProperties.Contains("translate-x"),
        "translate-x is missing from the public allowlist.");
    Assert.True(WrssPropertyCatalog.AllowedProperties.Contains("translate-y"),
        "translate-y is missing from the public allowlist.");
}

static void SurfaceShading()
{
    var compile = Compile(":root { --light: 0.06; } button { surface-shading: var(--light); } button:pressed { surface-shading: -0.04; }");
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(Element("button"));
    Assert.Equal(WrssValueKind.Number, style.Get("surface-shading")!.Kind);
    Assert.Equal(0.06d, style.Get("surface-shading")!.Number!.Value);
    var bounded = Compile("button { surface-shading: 2; }");
    Assert.Equal(0.25d, bounded.Theme!.Resolve(Element("button")).Get("surface-shading")!.Number!.Value);
    Assert.Equal(1, bounded.Diagnostics.Count(item => item.Code == "value_clamped"));
    var invalid = CompileExpectingErrors("button { surface-shading: 10px; }");
    Assert.Equal(1, invalid.Diagnostics.Count(item => item.Code == "invalid_value"));
}

static void ResponsiveWrapValues()
{
    var compile = Compile("row { flex-wrap: wrap; gap: 8px 12px; }");
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(Element("row"));
    Assert.Equal(WrssValueKind.Keyword, style.Get("flex-wrap")!.Kind);
    Assert.Equal("wrap", style.Get("flex-wrap")!.Text);
    Assert.Equal("8px 12px", style.Get("gap")!.Text);

    var noWrap = Compile("row { flex-wrap: nowrap; }");
    Assert.Equal("nowrap", noWrap.Theme!.Resolve(Element("row")).Get("flex-wrap")!.Text);

    var invalid = CompileExpectingErrors("row { flex-wrap: wrap-reverse; }");
    Assert.Equal(1, invalid.Diagnostics.Count(item => item.Code == "invalid_value"));
}

static void PerEdgeBorders()
{
    var compile = Compile("""
        button {
          border-width: 2px;
          border-color: #112233;
          border-top-width: 3px;
          border-left-color: transparent;
        }
        .primary { border-right-width: 4px; border-top-color: rgba(1, 2, 3, 0.5); }
        #play { border-bottom-width: 5px; border-bottom-color: #abcdef80; }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(new WrssElement(
        "button", "play", new HashSet<string>(["primary"]), null));
    Assert.Equal("2px", style.Get("border-width")!.Text);
    Assert.Equal("#112233", style.Get("border-color")!.Text);
    Assert.Equal("3px", style.Get("border-top-width")!.Text);
    Assert.Equal("4px", style.Get("border-right-width")!.Text);
    Assert.Equal("5px", style.Get("border-bottom-width")!.Text);
    Assert.True(style.Get("border-left-width") is null, "An absent edge must retain uniform fallback semantics.");
    Assert.Equal("rgba(1, 2, 3, 0.5)", style.Get("border-top-color")!.Text);
    Assert.True(style.Get("border-right-color") is null, "An absent color edge must retain uniform fallback semantics.");
    Assert.Equal("#abcdef80", style.Get("border-bottom-color")!.Text);
    Assert.Equal("transparent", style.Get("border-left-color")!.Text);

    var layeredBase = WrssParser.Parse("#play { border-top-width: 7px; }", "base.wrss");
    var layeredUser = WrssParser.Parse("button { border-top-width: 1px; }", "user.wrss");
    var layered = WrssThemeCompiler.Compile([
        new WrssThemeLayer(0, [layeredBase.Document]),
        new WrssThemeLayer(100, [layeredUser.Document]),
    ]);
    Assert.Equal("1px", layered.Theme!.Resolve(new WrssElement("button", "play")).Get("border-top-width")!.Text);

    var bounded = Compile("button { border-left-width: 999px; border-right-width: -2px; }");
    Assert.Equal(2, bounded.Diagnostics.Count(item => item.Code == "value_clamped"));
    var boundedStyle = bounded.Theme!.Resolve(Element("button"));
    Assert.Equal("16px", boundedStyle.Get("border-left-width")!.Text);
    Assert.Equal("0px", boundedStyle.Get("border-right-width")!.Text);

    var invalid = CompileExpectingErrors("button { border-top-width: thick; border-right-color: red; }");
    Assert.Equal(2, invalid.Diagnostics.Count(item => item.Code == "invalid_value"));
    var unsafeColor = WrssParser.Parse("button { border-bottom-color: url(evil); }", "unsafe-edge.wrss");
    Assert.Equal(1, unsafeColor.Diagnostics.Count(item => item.Code == "unsafe_value"));

    foreach (var edge in new[] { "top", "right", "bottom", "left" })
    {
        Assert.True(WrssPropertyCatalog.AllowedProperties.Contains($"border-{edge}-width"), $"Missing {edge} width.");
        Assert.True(WrssPropertyCatalog.AllowedProperties.Contains($"border-{edge}-color"), $"Missing {edge} color.");
    }
}

static void MediaSafety()
{
    foreach (var property in new[]
    {
        "aspect-ratio", "object-fit", "object-position", "shape", "line-height", "max-lines",
        "text-overflow", "overflow-wrap", "text-transform", "image-tint", "scrim-color", "outline-offset", "transition-easing",
        "flex-grow", "flex-shrink", "flex-basis", "flex-wrap",
    })
        Assert.True(WrssPropertyCatalog.AllowedProperties.Contains(property), $"Missing property {property}.");

    var parsed = WrssParser.Parse("image { object-fit: url(file:///cover); image-tint: shader(evil); }", "media.wrss");
    Assert.Equal(2, parsed.Diagnostics.Count(item => item.Code == "unsafe_value"));

    var invalid = CompileExpectingErrors("image { object-fit: stretch-crop; shape: star; aspect-ratio: 0/1; max-lines: 2.5; }");
    Assert.Equal(4, invalid.Diagnostics.Count(item => item.Code == "invalid_value"));
}

static void VisualSystemTokens()
{
    var compile = Compile("card { background: var(--surface); color: var(--text-subdued); border-color: var(--accent); scrim-color: var(--scrim); }");
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(Element("card"));
    Assert.Equal("rgba(23, 26, 34, 0.98)", style.Get("background")!.Text);
    Assert.Equal("#7f8796", style.Get("color")!.Text);
    Assert.Equal("#8f80ff", style.Get("border-color")!.Text);
    Assert.Equal("rgba(0, 0, 0, 0.64)", style.Get("scrim-color")!.Text);
}

static void CoolSlateSource()
{
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Themes",
        "builtin-cool-slate.wrss"));
    var palette = WrssParser.Parse(source, "builtin-cool-slate/theme.wrss");
    Assert.EmptyErrors(palette.Diagnostics);
    var probe = WrssParser.Parse("""
        canvas { background: var(--canvas); color: var(--text); }
        button {
          min-height: 44px;
          background: var(--surface-raised);
          color: var(--text);
          outline-color: var(--focus);
          border-color: var(--border);
        }
        """, "cool-slate-probe.wrss");
    Assert.EmptyErrors(probe.Diagnostics);
    var compiled = WrssThemeCompiler.Compile([palette.Document, probe.Document]);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));
    var canvas = compiled.Theme!.Resolve(Element("canvas"));
    Assert.Equal("#080d14", canvas.Get("background")!.Text);
    Assert.Equal("#edf2f7", canvas.Get("color")!.Text);
    var button = compiled.Theme.Resolve(Element("button"));
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("rgba(22, 32, 45, 0.98)", button.Get("background")!.Text);
    Assert.Equal("#8ca9c4", button.Get("outline-color")!.Text);
    Assert.Equal("rgba(219, 230, 240, 0.12)", button.Get("border-color")!.Text);
    Assert.True(button.Get("shadow-blur") is null, "Cool Slate must not introduce a component shadow.");
}

static void NeonCircuitSource()
{
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Themes",
        "builtin-neon-circuit.wrss"));
    var theme = WrssParser.Parse(source, "builtin-neon-circuit/theme.wrss");
    Assert.EmptyErrors(theme.Diagnostics);

    // A stand-in for the platform layer. The theme must retune it without
    // defeating the pseudo-state rules it publishes, because layer priority
    // outranks selector specificity.
    var platform = WrssParser.Parse("""
        canvas { background: var(--canvas); color: var(--text); }
        panel { corner-radius: 12px; border-color: var(--border); border-width: 1px; }
        button {
          min-height: 44px;
          padding: 10px 14px;
          background: var(--surface-raised);
          color: var(--text);
          corner-radius: 10px;
        }
        button:focused {
          background: var(--surface-active);
          outline-color: var(--focus);
          outline-width: 2px;
        }
        .wrail-section-header__eyebrow {
          color: var(--text-muted);
          font-size: 11px;
          font-weight: 500;
          letter-spacing: 0.03em;
          text-transform: none;
        }
        """, "neon-circuit-probe.wrss");
    Assert.EmptyErrors(platform.Diagnostics);

    var compiled = WrssThemeCompiler.Compile(
    [
        new WrssThemeLayer(0, [platform.Document]),
        new WrssThemeLayer(200, [theme.Document]),
    ]);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));

    var canvas = compiled.Theme!.Resolve(Element("canvas"));
    Assert.Equal("#05070e", canvas.Get("background")!.Text);
    Assert.Equal("#e8f1fb", canvas.Get("color")!.Text);
    Assert.Equal("8px", compiled.Theme.Resolve(Element("panel")).Get("corner-radius")!.Text);

    var button = compiled.Theme.Resolve(Element("button"));
    Assert.Equal("6px", button.Get("corner-radius")!.Text);
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("rgba(19, 24, 41, 0.98)", button.Get("background")!.Text);
    var focused = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("rgba(35, 45, 74, 0.98)", focused.Get("background")!.Text);
    Assert.Equal("#3fe0ff", focused.Get("outline-color")!.Text);
    // This probe authors a fixed width. Palette tokens must not override it.
    Assert.Equal("2px", focused.Get("outline-width")!.Text);
    Assert.Equal("6px", focused.Get("corner-radius")!.Text);

    var eyebrow = compiled.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-section-header__eyebrow"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("#ff4fd8", eyebrow.Get("color")!.Text);
    Assert.Equal("uppercase", eyebrow.Get("text-transform")!.Text);
    Assert.Equal("0.09em", eyebrow.Get("letter-spacing")!.Text);
    Assert.Equal("11px", eyebrow.Get("font-size")!.Text);
    Assert.True(button.Get("shadow-blur") is null, "Neon Circuit must not introduce a component shadow.");
}

static void ArcadeRushSource()
{
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Themes",
        "builtin-arcade-rush.wrss"));
    var theme = WrssParser.Parse(source, "builtin-arcade-rush/theme.wrss");
    Assert.EmptyErrors(theme.Diagnostics);

    // A stand-in for the platform layer, including the nested pill container
    // and the 44 DIP tray target the theme must not disturb.
    var platform = WrssParser.Parse("""
        canvas { background: var(--canvas); color: var(--text); }
        panel { corner-radius: 12px; border-color: var(--border); border-width: 1px; }
        tray-item {
          min-width: 44px;
          min-height: 44px;
          background: transparent;
          color: var(--text-muted);
          corner-radius: 10px;
        }
        tray-item:selected { background: var(--surface-active); color: var(--text); }
        button {
          min-height: 44px;
          padding: 10px 14px;
          background: var(--surface-raised);
          color: var(--text);
          corner-radius: 10px;
        }
        button:focused {
          background: var(--surface-active);
          outline-color: var(--focus);
          outline-width: 2px;
        }
        .wrail-segmented-tabs { min-height: 50px; padding: 3px; corner-radius: 10px; }
        .wrail-segmented-tabs__tab { min-height: 44px; corner-radius: 8px; }
        .wrail-badge__label { color: var(--text-muted); font-size: 11px; font-weight: 500; }
        """, "arcade-rush-probe.wrss");
    Assert.EmptyErrors(platform.Diagnostics);

    var compiled = WrssThemeCompiler.Compile(
    [
        new WrssThemeLayer(0, [platform.Document]),
        new WrssThemeLayer(200, [theme.Document]),
    ]);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));

    var canvas = compiled.Theme!.Resolve(Element("canvas"));
    Assert.Equal("#1a1020", canvas.Get("background")!.Text);
    Assert.Equal("#f6eef8", canvas.Get("color")!.Text);
    Assert.Equal("14px", compiled.Theme.Resolve(Element("panel")).Get("corner-radius")!.Text);

    var button = compiled.Theme.Resolve(Element("button"));
    Assert.Equal("22px", button.Get("corner-radius")!.Text);
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("rgba(46, 30, 60, 0.98)", button.Get("background")!.Text);
    var focused = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("rgba(72, 48, 92, 0.98)", focused.Get("background")!.Text);
    Assert.Equal("#ff3ea5", focused.Get("outline-color")!.Text);
    Assert.Equal("22px", focused.Get("corner-radius")!.Text);

    // Circular tray targets keep their 44 DIP extent, and the selected state
    // still comes from the platform rule.
    var trayItem = compiled.Theme.Resolve(Element("tray-item"));
    Assert.Equal("22px", trayItem.Get("corner-radius")!.Text);
    Assert.Equal("44px", trayItem.Get("min-width")!.Text);
    Assert.Equal("44px", trayItem.Get("min-height")!.Text);
    var traySelected = compiled.Theme.Resolve(new WrssElement(
        "tray-item",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Selected])));
    Assert.Equal("rgba(72, 48, 92, 0.98)", traySelected.Get("background")!.Text);

    // The container radius must exceed the inner pill by its own padding.
    var tabs = compiled.Theme.Resolve(new WrssElement(
        "container",
        null,
        new HashSet<string>(["wrail-segmented-tabs"]),
        new HashSet<WrssPseudoState>()));
    var tab = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-segmented-tabs__tab"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("25px", tabs.Get("corner-radius")!.Text);
    Assert.Equal("22px", tab.Get("corner-radius")!.Text);

    var badgeLabel = compiled.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-badge__label"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("600", badgeLabel.Get("font-weight")!.Text);
    Assert.True(badgeLabel.Get("text-transform") is null,
        "Arcade Rush must leave badge labels in sentence case.");
    Assert.True(button.Get("shadow-blur") is null, "Arcade Rush must not introduce a component shadow.");
}

static void RedlineSource()
{
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Themes",
        "builtin-redline.wrss"));
    var theme = WrssParser.Parse(source, "builtin-redline/theme.wrss");
    Assert.EmptyErrors(theme.Diagnostics);

    // A stand-in for the platform layer, including the transparent card
    // variant whose zeroed border the theme has to respect.
    var platform = WrssParser.Parse("""
        canvas { background: var(--canvas); color: var(--text); }
        panel {
          background: var(--surface);
          border-color: var(--border);
          border-width: 1px;
          corner-radius: 12px;
        }
        button {
          min-height: 44px;
          background: var(--surface-raised);
          color: var(--text);
          border-color: var(--border);
          border-width: 1px;
          corner-radius: 10px;
        }
        button:focused {
          background: var(--surface-active);
          outline-color: var(--focus);
          outline-width: 2px;
        }
        .wrail-card {
          padding: 12px;
          background: var(--surface-raised);
          border-color: var(--border);
          border-width: 1px;
          corner-radius: 10px;
        }
        .wrail-card--transparent { background: transparent; border-width: 0px; padding: 0px; }
        .wrail-section-header__eyebrow { color: var(--text-muted); font-size: 11px; }
        .wrail-action-sheet__item--danger { color: var(--danger); }
        .wrail-icon-button--primary { background: var(--accent); color: var(--canvas); }
        """, "redline-probe.wrss");
    Assert.EmptyErrors(platform.Diagnostics);

    var compiled = WrssThemeCompiler.Compile(
    [
        new WrssThemeLayer(0, [platform.Document]),
        new WrssThemeLayer(200, [theme.Document]),
    ]);
    Assert.True(compiled.IsValid, Describe(compiled.Diagnostics));

    var canvas = compiled.Theme!.Resolve(Element("canvas"));
    Assert.Equal("#160b0d", canvas.Get("background")!.Text);
    Assert.Equal("#fbeeec", canvas.Get("color")!.Text);

    // The edge is additive: uniform border and radius survive beneath it.
    var panel = compiled.Theme.Resolve(Element("panel"));
    Assert.Equal("#ff3d2e", panel.Get("border-bottom-color")!.Text);
    Assert.Equal("2px", panel.Get("border-bottom-width")!.Text);
    Assert.Equal("rgba(255, 214, 208, 0.14)", panel.Get("border-color")!.Text);
    Assert.Equal("1px", panel.Get("border-width")!.Text);
    Assert.Equal("12px", panel.Get("corner-radius")!.Text);

    // Geometry is left entirely to the platform, and the edge stops at
    // containers rather than reaching a focus target.
    var button = compiled.Theme.Resolve(Element("button"));
    Assert.Equal("10px", button.Get("corner-radius")!.Text);
    Assert.Equal("44px", button.Get("min-height")!.Text);
    Assert.Equal("rgba(46, 23, 25, 0.98)", button.Get("background")!.Text);
    Assert.True(button.Get("border-bottom-width") is null,
        "The structural edge must not reach controls.");
    var focused = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(),
        new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
    Assert.Equal("rgba(77, 40, 43, 0.98)", focused.Get("background")!.Text);
    Assert.Equal("#ff3d2e", focused.Get("outline-color")!.Text);

    var transparentCard = compiled.Theme.Resolve(new WrssElement(
        "container",
        null,
        new HashSet<string>(["wrail-card", "wrail-card--transparent"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("0px", transparentCard.Get("border-bottom-width")!.Text);

    // Danger was moved off red on purpose; it must not collapse onto the accent.
    var danger = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-action-sheet__item--danger"]),
        new HashSet<WrssPseudoState>()));
    var primary = compiled.Theme.Resolve(new WrssElement(
        "button",
        null,
        new HashSet<string>(["wrail-icon-button--primary"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("#ff5fa8", danger.Get("color")!.Text);
    Assert.Equal("#ff3d2e", primary.Get("background")!.Text);

    var eyebrow = compiled.Theme.Resolve(new WrssElement(
        "text",
        null,
        new HashSet<string>(["wrail-section-header__eyebrow"]),
        new HashSet<WrssPseudoState>()));
    Assert.Equal("#ff3d2e", eyebrow.Get("color")!.Text);
    Assert.Equal("uppercase", eyebrow.Get("text-transform")!.Text);
    Assert.Equal("0.14em", eyebrow.Get("letter-spacing")!.Text);
}

static WrssCompileResult Compile(string source)
{
    var parsed = WrssParser.Parse(source, "test.wrss");
    Assert.EmptyErrors(parsed.Diagnostics);
    return WrssThemeCompiler.Compile([parsed.Document]);
}

static WrssCompileResult CompileExpectingErrors(string source)
{
    var parsed = WrssParser.Parse(source, "test.wrss");
    Assert.EmptyErrors(parsed.Diagnostics);
    return WrssThemeCompiler.Compile([parsed.Document]);
}

static WrssElement Element(string role) => new(role);
static string Describe(IEnumerable<WrssDiagnostic> diagnostics) => string.Join(Environment.NewLine, diagnostics);

file sealed class DictionaryProvider(IReadOnlyDictionary<string, string> files) : IWrssSourceProvider
{
    public WrssSourceReadResult Read(string packageRelativePath) =>
        files.TryGetValue(packageRelativePath, out var source)
            ? WrssSourceReadResult.FromSource(source)
            : WrssSourceReadResult.Failure(WrssSourceReadStatus.Missing);
}

file sealed class ResultProvider(WrssSourceReadResult result) : IWrssSourceProvider
{
    public WrssSourceReadResult Read(string packageRelativePath) => result;
}

file sealed class ThrowingProvider : IWrssSourceProvider
{
    public WrssSourceReadResult Read(string packageRelativePath) =>
        throw new InvalidOperationException("provider-secret");
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "widget-styling-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

file sealed class MisreportedLengthStream(
    byte[] content,
    long reportedLength,
    long? lengthAfterRead = null) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length =>
        _position > 0 && lengthAfterRead is { } changed ? changed : reportedLength;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var available = content.Length - _position;
        if (available <= 0) return 0;
        var read = Math.Min(available, count);
        Array.Copy(content, _position, buffer, offset, read);
        _position += read;
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static void EmptyErrors(IEnumerable<WrssDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(item => item.Severity == WrssDiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) throw new InvalidOperationException(Describe(errors));
    }

    public static void HasCode(IEnumerable<WrssDiagnostic> diagnostics, string code)
    {
        if (!diagnostics.Any(item => item.Code == code))
            throw new InvalidOperationException($"Expected diagnostic '{code}'. Actual: {Describe(diagnostics)}");
    }

    private static string Describe(IEnumerable<WrssDiagnostic> diagnostics) => string.Join(Environment.NewLine, diagnostics);
}
