using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Action Run)[]
{
    ("Parser preserves semantic selectors and source locations", ParserAndLocations),
    ("Parser rejects browser and script escape syntax", UnsafeValues),
    ("Parser recovers independent declaration diagnostics", ErrorRecovery),
    ("Parser enforces bounded untrusted source sizes", ParserLimits),
    ("Package loader resolves safe imports deterministically", SafeImports),
    ("Package loader rejects traversal, missing files, and cycles", UnsafeImports),
    ("Variables resolve forward references and fallbacks", Variables),
    ("Variable cycles prevent theme publication", VariableCycles),
    ("Cascade applies specificity states and source order", Cascade),
    ("Explicit theme layers outrank selector specificity", LayerPrecedence),
    ("Selected disabled busy and focused states compose", InteractionStateComposition),
    ("Typed values clamp bounded renderer inputs", Clamping),
    ("Invalid typed values prevent theme publication", InvalidTypedValues),
    ("Resolved property enumeration is deterministic", DeterministicResolution),
    ("Media-card properties compile to typed renderer values", MediaCardValues),
    ("Responsive viewport units remain bounded", ResponsiveUnits),
    ("Media-card properties preserve the safe allowlist", MediaSafety),
    ("Default visual-system tokens compile exactly", VisualSystemTokens),
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
    var result = GbssParser.Parse(source, "styles/default.gbss");
    Assert.EmptyErrors(result.Diagnostics);
    var rules = result.Document.Statements.OfType<GbssRule>().ToArray();
    Assert.Equal(2, rules.Length);
    var selector = rules[1].Selectors[0];
    Assert.Equal("button", selector.Role);
    Assert.Equal("play", selector.Id);
    Assert.True(selector.Classes.Contains("primary"), "Expected primary class.");
    Assert.True(selector.States.Contains(GbssPseudoState.Focused), "Expected focused state.");
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
    var result = GbssParser.Parse(source, "unsafe.gbss");
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
    var result = GbssParser.Parse(source, "errors.gbss");
    Assert.HasCode(result.Diagnostics, "invalid_selector");
    Assert.HasCode(result.Diagnostics, "unknown_property");
    Assert.HasCode(result.Diagnostics, "invalid_declaration");
    Assert.True(result.Document.Statements.OfType<GbssRule>().Any(rule => rule.Selectors.Any(selector => selector.Role == "row")),
        "Parser did not recover to the final valid rule.");
}

static void ParserLimits()
{
    var oversized = new string(' ', GbssLimits.MaximumSourceCharacters + 1);
    var result = GbssParser.Parse(oversized, "oversized.gbss");
    Assert.HasCode(result.Diagnostics, "source_too_large");
    Assert.Equal(0, result.Document.Statements.Count);

    var declarations = string.Join(';', Enumerable.Range(0, GbssLimits.MaximumDeclarationsPerRule + 1)
        .Select(index => $"--v{index}: {index}"));
    result = GbssParser.Parse($":root {{ {declarations}; }}", "declarations.gbss");
    Assert.HasCode(result.Diagnostics, "too_many_declarations");
}

static void SafeImports()
{
    var provider = new DictionaryProvider(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["styles/default.gbss"] = "@import \"tokens.gbss\"; button { color: var(--brand); }",
        ["styles/tokens.gbss"] = ":root { --brand: #123456; } button { opacity: 0.8; }",
    });
    var package = GbssPackageLoader.Load("styles/default.gbss", provider);
    Assert.EmptyErrors(package.Diagnostics);
    Assert.SequenceEqual(["styles/tokens.gbss", "styles/default.gbss"], package.Documents.Select(item => item.Source));
    var compile = GbssThemeCompiler.Compile(package);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(Element("button"));
    Assert.Equal("#123456", style.Get("color")!.Text);
    Assert.Equal("0.8", style.Get("opacity")!.Text);
}

static void UnsafeImports()
{
    var traversal = GbssParser.Parse("@import \"../secret.gbss\";", "default.gbss");
    Assert.HasCode(traversal.Diagnostics, "unsafe_import");

    var missing = GbssPackageLoader.Load("default.gbss", new DictionaryProvider(new Dictionary<string, string>
    {
        ["default.gbss"] = "@import \"missing.gbss\";",
    }));
    Assert.HasCode(missing.Diagnostics, "missing_import");
    var missingCompile = GbssThemeCompiler.Compile(missing);
    Assert.True(!missingCompile.IsValid && missingCompile.Theme is null, "A package with a missing import published a theme.");

    var cycle = GbssPackageLoader.Load("a.gbss", new DictionaryProvider(new Dictionary<string, string>
    {
        ["a.gbss"] = "@import \"b.gbss\";",
        ["b.gbss"] = "@import \"a.gbss\";",
    }));
    Assert.HasCode(cycle.Diagnostics, "import_cycle");
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
    var focused = new GbssElement(
        "button",
        "play",
        new HashSet<string>(["primary"], StringComparer.Ordinal),
        new HashSet<GbssPseudoState> { GbssPseudoState.Focused });
    var style = compile.Theme!.Resolve(focused);
    Assert.Equal("#555555", style.Get("color")!.Text);
    Assert.Equal("1.1", style.Get("scale")!.Text);

    var unfocused = compile.Theme.Resolve(new GbssElement("button", null, new HashSet<string>(["primary"]), null));
    Assert.Equal("#222222", unfocused.Get("color")!.Text);
    Assert.Equal("1", unfocused.Get("scale")!.Text);
}

static void LayerPrecedence()
{
    var platform = GbssParser.Parse("#play { color: #111111; }", "platform.gbss");
    var widget = GbssParser.Parse("#play { color: #222222; }", "widget.gbss");
    var user = GbssParser.Parse("button { color: #333333; }", "user.gbss");
    Assert.EmptyErrors(platform.Diagnostics.Concat(widget.Diagnostics).Concat(user.Diagnostics));
    var compile = GbssThemeCompiler.Compile(
    [
        new GbssThemeLayer(0, [platform.Document]),
        new GbssThemeLayer(100, [widget.Document]),
        new GbssThemeLayer(200, [user.Document]),
    ]);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var style = compile.Theme!.Resolve(new GbssElement("button", "play"));
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

static void InteractionStateComposition()
{
    var compile = Compile("""
        .primary:selected { border-width: 3px; }
        .primary:disabled { opacity: 0.4; }
        .primary:busy { color: #59d5ff; }
        .primary:selected:focused { scale: 1.1; }
        """);
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    var element = new GbssElement(
        "button",
        "play",
        new HashSet<string>(["primary"], StringComparer.Ordinal),
        new HashSet<GbssPseudoState>
        {
            GbssPseudoState.Selected,
            GbssPseudoState.Disabled,
            GbssPseudoState.Busy,
            GbssPseudoState.Focused,
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
        }
        """);
    Assert.True(!compile.IsValid, "Invalid typed values unexpectedly compiled.");
    Assert.Equal(5, compile.Diagnostics.Count(item => item.Code == "invalid_value"));
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
    var art = compile.Theme!.Resolve(new GbssElement("image", null, new HashSet<string>(["album-art"]), null));
    Assert.Equal(GbssValueKind.Length, art.Get("width")!.Kind);
    Assert.Equal("24vw", art.Get("width")!.Text);
    Assert.Equal(GbssValueKind.Ratio, art.Get("aspect-ratio")!.Kind);
    Assert.Equal("1", art.Get("aspect-ratio")!.Text);
    Assert.Equal("cover", art.Get("object-fit")!.Text);
    Assert.Equal("rounded", art.Get("shape")!.Text);
    Assert.Equal("184px", art.Get("flex-basis")!.Text);
    Assert.Equal("0", art.Get("flex-shrink")!.Text);

    var title = compile.Theme.Resolve(new GbssElement("text", null, new HashSet<string>(["track-title"]), null));
    Assert.Equal(GbssValueKind.Integer, title.Get("max-lines")!.Kind);
    Assert.Equal("2", title.Get("max-lines")!.Text);
    Assert.Equal("1.2", title.Get("line-height")!.Text);
    Assert.Equal("ellipsis", title.Get("text-overflow")!.Text);
}

static void ResponsiveUnits()
{
    var compile = Compile("card { width: 130vw; height: 36vh; aspect-ratio: 16/1; max-lines: 99; line-height: 5; flex-grow: 99; }");
    Assert.True(compile.IsValid, Describe(compile.Diagnostics));
    Assert.Equal(5, compile.Diagnostics.Count(item => item.Code == "value_clamped"));
    var style = compile.Theme!.Resolve(Element("card"));
    Assert.Equal("100vw", style.Get("width")!.Text);
    Assert.Equal("36vh", style.Get("height")!.Text);
    Assert.Equal("5", style.Get("aspect-ratio")!.Text);
    Assert.Equal("8", style.Get("max-lines")!.Text);
    Assert.Equal("3", style.Get("line-height")!.Text);
    Assert.Equal("8", style.Get("flex-grow")!.Text);
}

static void MediaSafety()
{
    foreach (var property in new[]
    {
        "aspect-ratio", "object-fit", "object-position", "shape", "line-height", "max-lines",
        "text-overflow", "text-transform", "image-tint", "scrim-color", "outline-offset", "transition-easing",
        "flex-grow", "flex-shrink", "flex-basis",
    })
        Assert.True(GbssPropertyCatalog.AllowedProperties.Contains(property), $"Missing property {property}.");

    var parsed = GbssParser.Parse("image { object-fit: url(file:///cover); image-tint: shader(evil); }", "media.gbss");
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

static GbssCompileResult Compile(string source)
{
    var parsed = GbssParser.Parse(source, "test.gbss");
    Assert.EmptyErrors(parsed.Diagnostics);
    return GbssThemeCompiler.Compile([parsed.Document]);
}

static GbssCompileResult CompileExpectingErrors(string source)
{
    var parsed = GbssParser.Parse(source, "test.gbss");
    Assert.EmptyErrors(parsed.Diagnostics);
    return GbssThemeCompiler.Compile([parsed.Document]);
}

static GbssElement Element(string role) => new(role);
static string Describe(IEnumerable<GbssDiagnostic> diagnostics) => string.Join(Environment.NewLine, diagnostics);

file sealed class DictionaryProvider(IReadOnlyDictionary<string, string> files) : IGbssSourceProvider
{
    public bool TryRead(string packageRelativePath, out string source) => files.TryGetValue(packageRelativePath, out source!);
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

    public static void EmptyErrors(IEnumerable<GbssDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(item => item.Severity == GbssDiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) throw new InvalidOperationException(Describe(errors));
    }

    public static void HasCode(IEnumerable<GbssDiagnostic> diagnostics, string code)
    {
        if (!diagnostics.Any(item => item.Code == code))
            throw new InvalidOperationException($"Expected diagnostic '{code}'. Actual: {Describe(diagnostics)}");
    }

    private static string Describe(IEnumerable<GbssDiagnostic> diagnostics) => string.Join(Environment.NewLine, diagnostics);
}
