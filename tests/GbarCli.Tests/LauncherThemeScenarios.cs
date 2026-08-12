using System.IO.Compression;
using System.Text.Json;
using GameBarAlternative.GbarCli;
using GameBarAlternative.LauncherExperienceCatalog;

internal static class LauncherThemeScenarios
{
    public static async Task WorkflowIsDeterministicAndImmutable()
    {
        using var temp = new TestDirectory();
        var help = await Run("launcher-theme", "help");
        Equal(0, help.Code);
        foreach (var command in new[] { "new", "validate", "preview", "pack", "inspect", "install", "list", "remove" })
            Contains($"launcher-theme {command}", help.Output);
        var first = Path.Combine(temp.Path, "first");
        var second = Path.Combine(temp.Path, "second");
        Equal(0, (await Run("launcher-theme", "new", "Deep Space", "--output", first,
            "--id", "dev.example.deep-space", "--publisher", "dev.example",
            "--preset", "hero-rail")).Code);
        Equal(0, (await Run("launcher-theme", "new", "Deep Space", "--output", second,
            "--id", "dev.example.deep-space", "--publisher", "dev.example",
            "--preset", "hero-rail")).Code);
        foreach (var relative in new[]
                 {
                     "launcher.json", "layouts/launcher-layout.json",
                     "styles/launcher.gbss", "assets/preview.png",
                 })
            SequenceEqual(File.ReadAllBytes(Path.Combine(first, relative.Replace('/', Path.DirectorySeparatorChar))),
                File.ReadAllBytes(Path.Combine(second, relative.Replace('/', Path.DirectorySeparatorChar))));

        var validation = await Run("launcher-theme", "validate", first);
        Equal(0, validation.Code);
        Contains("Valid Launcher Experience", validation.Output);
        var firstArchive = Path.Combine(temp.Path, "first.gbarlauncher");
        var secondArchive = Path.Combine(temp.Path, "second.gbarlauncher");
        var firstPack = await Run("launcher-theme", "pack", first, "--output", firstArchive);
        var secondPack = await Run("launcher-theme", "pack", second, "--output", secondArchive);
        Equal(0, firstPack.Code);
        Equal(0, secondPack.Code);
        SequenceEqual(File.ReadAllBytes(firstArchive), File.ReadAllBytes(secondArchive));
        Equal(HashLine(firstPack.Output), HashLine(secondPack.Output));
        Equal(1, (await Run("launcher-theme", "pack", first, "--output", firstArchive)).Code);

        var inspect = await Run("launcher-theme", "inspect", firstArchive);
        Equal(0, inspect.Code);
        Contains("ID: dev.example.deep-space", inspect.Output);
        Contains("Trust: strict data-only validation passed", inspect.Output);
        var catalog = Path.Combine(temp.Path, "catalog");
        Equal(0, (await Run("launcher-theme", "install", firstArchive, "--catalog", catalog)).Code);
        var immutable = await Run("launcher-theme", "install", firstArchive, "--catalog", catalog);
        Equal(1, immutable.Code);
        Contains("immutable_version", immutable.Error);
        var listed = await Run("launcher-theme", "list", "--catalog", catalog);
        Equal(0, listed.Code);
        Contains("dev.example.deep-space", listed.Output);
        Contains("org.gbar.builtin.hero-rail", listed.Output);
        Equal(1, (await Run("launcher-theme", "remove", "org.gbar.builtin.hero-rail", "1.0.0",
            "--catalog", catalog)).Code);
        Equal(0, (await Run("launcher-theme", "remove", "dev.example.deep-space", "1.0.0",
            "--catalog", catalog)).Code);
        DoesNotContain("dev.example.deep-space",
            (await Run("launcher-theme", "list", "--catalog", catalog)).Output);
    }

    public static async Task PreviewCoversTheBoundedFixtureMatrix()
    {
        using var temp = new TestDirectory();
        var source = Path.Combine(temp.Path, "source");
        Equal(0, (await Run("launcher-theme", "new", "Preview Matrix", "--output", source,
            "--id", "dev.example.preview", "--publisher", "dev.example")).Code);
        var first = Path.Combine(temp.Path, "preview-one.json");
        var second = Path.Combine(temp.Path, "preview-two.json");
        Equal(0, (await Run("launcher-theme", "preview", source, "--output", first)).Code);
        Equal(0, (await Run("launcher-theme", "preview", source, "--output", second)).Code);
        SequenceEqual(File.ReadAllBytes(first), File.ReadAllBytes(second));
        using var document = JsonDocument.Parse(File.ReadAllBytes(first));
        var root = document.RootElement;
        Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Equal("dev.example.preview", root.GetProperty("packageId").GetString());
        var frames = root.GetProperty("frames").EnumerateArray().ToArray();
        Equal(132, frames.Length);
        SetEqual(new[] { "compact", "standard", "wide" }, frames.Select(Frame("branch")));
        SetEqual(new[] { "hero-rail", "cover-wall", "carousel", "compact-grid" },
            frames.Select(Frame("preset")));
        SetEqual(new[]
        {
            "empty", "twenty-games", "two-thousand-games", "offline", "long-title",
            "missing-art", "active-operation", "interface-scale-150", "reduced-motion",
            "reduced-transparency", "high-contrast",
        }, frames.Select(Frame("scenario")));
        True(frames.Where(frame => Frame("scenario")(frame) == "two-thousand-games")
            .All(frame => frame.GetProperty("totalItems").GetInt32() == 2000 &&
                          frame.GetProperty("retainedItems").GetInt32() == 64),
            "The 2,000-item fixture was not represented by a bounded retained window.");
        True(frames.Where(frame => Frame("scenario")(frame) == "interface-scale-150")
            .All(frame => frame.GetProperty("interfaceScale").GetDouble() == 1.5),
            "The 150% fixture was missing its scale policy.");
        True(frames.Where(frame => Frame("scenario")(frame) == "long-title")
            .All(frame => frame.GetProperty("selectedTitle").GetString()!.Length > 80),
            "The long-title fixture did not carry deterministic long content.");
        True(frames.Where(frame => Frame("scenario")(frame) == "missing-art")
            .All(frame => frame.GetProperty("artworkState").GetString() == "missing"),
            "The missing-art fixture did not carry its fallback state.");
        True(frames.Where(frame => Frame("scenario")(frame) == "active-operation")
            .All(frame => frame.GetProperty("operationState").GetString() == "download-active"),
            "The active-operation fixture did not carry operation state.");
        True(frames.Where(frame => Frame("scenario")(frame) == "reduced-motion")
            .All(frame => frame.GetProperty("reducedMotion").GetBoolean()),
            "The reduced-motion fixture did not carry accessibility policy.");
        True(frames.Where(frame => Frame("scenario")(frame) == "reduced-transparency")
            .All(frame => frame.GetProperty("reducedTransparency").GetBoolean()),
            "The reduced-transparency fixture did not carry accessibility policy.");
        True(frames.Where(frame => Frame("scenario")(frame) == "high-contrast")
            .All(frame => frame.GetProperty("highContrast").GetBoolean()),
            "The high-contrast fixture did not carry accessibility policy.");
        var text = File.ReadAllText(first);
        DoesNotContain("actionId", text);
        DoesNotContain("savedId", text);
        DoesNotContain("provider", text);

        static Func<JsonElement, string> Frame(string name) =>
            frame => frame.GetProperty(name).GetString()!;
    }

    public static async Task ValidationFailsClosedForAuthorityAndUnsafeContent()
    {
        using var temp = new TestDirectory();
        var source = Path.Combine(temp.Path, "source");
        Equal(0, (await Run("launcher-theme", "new", "Safety", "--output", source,
            "--id", "dev.example.safety", "--publisher", "dev.example")).Code);
        var manifestPath = Path.Combine(source, "launcher.json");
        var recipePath = Path.Combine(source, "layouts", "launcher-layout.json");
        var imagePath = Path.Combine(source, "assets", "preview.png");
        var manifest = File.ReadAllText(manifestPath);
        var recipe = File.ReadAllText(recipePath);
        var image = File.ReadAllBytes(imagePath);

        await Reject("unknown_field", () => File.WriteAllText(manifestPath,
            manifest.Replace("\"parameters\":", "\"remoteUrl\":\"https://example.invalid/a\",\"parameters\":",
                StringComparison.Ordinal)));
        await Reject("unsafe_asset_path", () => File.WriteAllText(manifestPath,
            manifest.Replace("assets/preview.png", "https://example.invalid/preview.png", StringComparison.Ordinal)));
        foreach (var file in new[] { "payload.js", "payload.html", "payload.exe" })
            await Reject("forbidden_content", () => File.WriteAllText(Path.Combine(source, file), "not data"),
                () => File.Delete(Path.Combine(source, file)));
        await Reject("multi_frame_image", () =>
            File.WriteAllBytes(imagePath, [.. image, .. "acTL"u8.ToArray()]));
        await Reject("file_too_large", () =>
        {
            using var stream = new FileStream(Path.Combine(source, "assets", "oversized.png"), FileMode.CreateNew);
            stream.SetLength(LauncherExperienceValidator.MaximumAssetBytes + 1);
        }, () => File.Delete(Path.Combine(source, "assets", "oversized.png")));
        await Reject("missing_critical_slot", () => File.WriteAllText(recipePath,
            recipe.Replace("\"controller-hints\"", "\"system-status\"", StringComparison.Ordinal)));
        await Reject("slot_reuse", () => File.WriteAllText(recipePath,
            recipe.Replace("\"hero-background\"", "\"game-rail\"", StringComparison.Ordinal)));
        await Reject("unknown_field", () => File.WriteAllText(recipePath,
            recipe.Replace("\"slot\": \"game-rail\"", "\"slot\": \"game-rail\", \"actionId\": \"launch\"",
                StringComparison.Ordinal)));
        await Reject("unknown_field", () => File.WriteAllText(recipePath,
            recipe.Replace("\"slot\": \"game-rail\"", "\"slot\": \"game-rail\", \"providerBinding\": \"games\"",
                StringComparison.Ordinal)));
        await Reject("unreachable_back", () => File.WriteAllText(recipePath,
            recipe.Replace("\"width\": 0.45", "\"width\": 0.05", StringComparison.Ordinal)));

        var traversal = Path.Combine(temp.Path, "traversal.gbarlauncher");
        using (var archive = ZipFile.Open(traversal, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../launcher.json");
            await using var destination = entry.Open();
            await destination.WriteAsync("{}"u8.ToArray());
        }
        var traversalResult = await Run("launcher-theme", "inspect", traversal);
        Equal(1, traversalResult.Code);
        Contains("invalid_path", traversalResult.Error);

        async Task Reject(string code, Action mutate, Action? cleanup = null)
        {
            File.WriteAllText(manifestPath, manifest);
            File.WriteAllText(recipePath, recipe);
            File.WriteAllBytes(imagePath, image);
            cleanup?.Invoke();
            mutate();
            var result = await Run("launcher-theme", "validate", source);
            Equal(1, result.Code);
            Contains(code, result.Error);
            cleanup?.Invoke();
        }
    }

    private static async Task<TestCliResult> Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync(args, output, error);
        return new(code, output.ToString(), error.ToString());
    }

    private static string HashLine(string output) =>
        output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("SHA-256: ", StringComparison.Ordinal));

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{expected}' in: {actual}");
    }

    private static void DoesNotContain(string expected, string actual)
    {
        if (actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Did not expect '{expected}' in: {actual}");
    }

    private static void SequenceEqual(byte[] expected, byte[] actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Expected byte-identical content.");
    }

    private static void SetEqual(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        if (!expected.Order(StringComparer.Ordinal).SequenceEqual(actual.Distinct().Order(StringComparer.Ordinal)))
            throw new InvalidOperationException(
                $"Expected [{string.Join(',', expected)}], got [{string.Join(',', actual.Distinct())}].");
    }

    private sealed record TestCliResult(int Code, string Output, string Error);

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "gbar-launcher-theme-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
