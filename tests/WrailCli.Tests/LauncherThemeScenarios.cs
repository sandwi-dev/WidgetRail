using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using WidgetRail.WrailCli;
using WidgetRail.LauncherExperienceCatalog;
using WidgetRail.PlatformSettings;

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
                     "styles/launcher.wrss", "assets/preview.png",
                 })
            SequenceEqual(File.ReadAllBytes(Path.Combine(first, relative.Replace('/', Path.DirectorySeparatorChar))),
                File.ReadAllBytes(Path.Combine(second, relative.Replace('/', Path.DirectorySeparatorChar))));

        var validation = await Run("launcher-theme", "validate", first);
        Equal(0, validation.Code);
        Contains("Valid Launcher Experience", validation.Output);
        var firstArchive = Path.Combine(temp.Path, "first.wrlauncher");
        var secondArchive = Path.Combine(temp.Path, "second.wrlauncher");
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
        Contains("widgetrail.builtin.hero-rail", listed.Output);
        Equal(1, (await Run("launcher-theme", "remove", "widgetrail.builtin.hero-rail", "1.0.0",
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

        var traversal = Path.Combine(temp.Path, "traversal.wrlauncher");
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

    public static async Task AuthorToProductionLifecycle()
    {
        using var temp = new TestDirectory();
        var installation = RequiredEnvironment("WRAIL_LAUNCHER_LIFECYCLE_INSTALLATION");
        var hostTest = RequiredEnvironment("WRAIL_LAUNCHER_LIFECYCLE_HOST_TEST");
        var fixtureBridge = RequiredEnvironment("WRAIL_LAUNCHER_LIFECYCLE_FIXTURE_BRIDGE");
        var bottom = Path.Combine(temp.Path, "bottom-rail");
        var left = Path.Combine(temp.Path, "left-rail");
        var bottomArchive = Path.Combine(temp.Path, "bottom-rail.wrlauncher");
        var leftArchive = Path.Combine(temp.Path, "left-rail.wrlauncher");
        var bottomPreview = Path.Combine(temp.Path, "bottom-preview.json");
        var leftPreview = Path.Combine(temp.Path, "left-preview.json");
        var settingsRoot = Path.Combine(temp.Path, "local-app-data", "WidgetRail");
        var catalog = Path.Combine(settingsRoot, "launcher-experiences");
        var control = Path.Combine(temp.Path, "lifecycle-control");
        Directory.CreateDirectory(control);

        await RequireSuccess("scaffold bottom rail", "launcher-theme", "new", "Bottom Rail",
            "--output", bottom, "--id", "dev.example.production", "--publisher", "dev.example",
            "--version", "1.0.0", "--preset", "hero-rail");
        await RequireSuccess("scaffold left rail", "launcher-theme", "new", "Left Rail Glass",
            "--output", left, "--id", "dev.example.production", "--publisher", "dev.example",
            "--version", "2.0.0", "--preset", "hero-rail");
        await File.WriteAllTextAsync(
            Path.Combine(left, "layouts", "launcher-layout.json"), LeftRailRecipe);

        foreach (var source in new[] { bottom, left })
        {
            await RequireSuccess("validate authored pack", "launcher-theme", "validate", source);
        }
        await RequireSuccess("preview bottom rail", "launcher-theme", "preview", bottom,
            "--output", bottomPreview);
        await RequireSuccess("preview left rail", "launcher-theme", "preview", left,
            "--output", leftPreview);
        await RequireSuccess("pack bottom rail", "launcher-theme", "pack", bottom,
            "--output", bottomArchive);
        await RequireSuccess("pack left rail", "launcher-theme", "pack", left,
            "--output", leftArchive);
        await RequireSuccess("inspect bottom rail", "launcher-theme", "inspect", bottomArchive);
        await RequireSuccess("inspect left rail", "launcher-theme", "inspect", leftArchive);
        await RequireSuccess("install bottom rail", "launcher-theme", "install", bottomArchive,
            "--catalog", catalog);
        await RequireSuccess("install left rail", "launcher-theme", "install", leftArchive,
            "--catalog", catalog);

        var paths = new PlatformSettingsPaths(settingsRoot);
        var store = new PlatformSettingsStore(paths);
        var policy = new LauncherExperienceSelectionPolicy(store);
        await policy.SelectAsync("dev.example.production", "1.0.0");

        using var host = StartHostTest(hostTest, installation, fixtureBridge, settingsRoot, control);
        var hostOutputTask = host.StandardOutput.ReadToEndAsync();
        var hostErrorTask = host.StandardError.ReadToEndAsync();
        await WaitForStage(host, hostErrorTask, control, "v1-active");
        await policy.SelectAsync("dev.example.production", "2.0.0");
        Signal(control, "v2-selected");
        await WaitForStage(host, hostErrorTask, control, "v2-active");
        await WaitForStage(host, hostErrorTask, control, "selected-after-safe-start");

        var selectedRecipe = Path.Combine(
            catalog, "dev.example.production", "2.0.0", "layouts", "launcher-layout.json");
        var lastGoodRecipe = await File.ReadAllBytesAsync(selectedRecipe);
        await File.WriteAllTextAsync(selectedRecipe,
            "{\"schemaVersion\":1,\"branches\":{\"wide\":{\"root\":{\"type\":\"overlay\",\"children\":[]}}}}");
        Signal(control, "v2-corrupted");
        await WaitForStage(host, hostErrorTask, control, "last-good-retained");
        await File.WriteAllBytesAsync(selectedRecipe, lastGoodRecipe);
        await policy.RecoverBuiltInAsync();
        Signal(control, "hero-rail-restored");
        await WaitForStage(host, hostErrorTask, control, "host-complete");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        await host.WaitForExitAsync(timeout.Token);
        var hostOutput = await hostOutputTask.WaitAsync(timeout.Token);
        var hostError = await hostErrorTask.WaitAsync(timeout.Token);
        Equal(0, host.ExitCode);
        Contains("author-to-production replacement", hostOutput);
        DoesNotContain(temp.Path, hostOutput + hostError);

        await RequireSuccess("remove bottom rail", "launcher-theme", "remove",
            "dev.example.production", "1.0.0", "--catalog", catalog);
        await RequireSuccess("remove left rail", "launcher-theme", "remove",
            "dev.example.production", "2.0.0", "--catalog", catalog);
        var listed = await Run("launcher-theme", "list", "--catalog", catalog);
        Equal(0, listed.Code);
        DoesNotContain("dev.example.production", listed.Output);
        var finalSettings = await store.LoadAsync();
        Equal("widgetrail.builtin.hero-rail", finalSettings.LauncherExperience.SelectedId);
        Equal("1.0.0", finalSettings.LauncherExperience.SelectedVersion);

        foreach (var artifact in new[] { bottomPreview, leftPreview })
        {
            var text = await File.ReadAllTextAsync(artifact);
            DoesNotContain(temp.Path, text);
            DoesNotContain("http://", text);
            DoesNotContain("https://", text);
        }
        foreach (var archivePath in new[] { bottomArchive, leftArchive })
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                var extension = Path.GetExtension(entry.FullName);
                True(extension is not (".exe" or ".dll" or ".js" or ".html"),
                    "Authored lifecycle archive contained executable or active content.");
                DoesNotContain(temp.Path, entry.FullName);
                if (extension is ".json" or ".wrss")
                {
                    using var reader = new StreamReader(entry.Open());
                    var text = await reader.ReadToEndAsync();
                    DoesNotContain(temp.Path, text);
                    DoesNotContain("http://", text);
                    DoesNotContain("https://", text);
                }
            }
        }
        var productionLog = await File.ReadAllTextAsync(Path.Combine(settingsRoot, "overlay.log"));
        DoesNotContain(temp.Path, productionLog);
        Console.WriteLine("LauncherThemeScenarios: exact author-to-production lifecycle passed");
    }

    private static Process StartHostTest(
        string hostTest,
        string installation,
        string fixtureBridge,
        string settingsRoot,
        string control)
    {
        var start = new ProcessStartInfo(hostTest)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 {
                     "--installation", installation,
                     "--fixture-bridge", fixtureBridge,
                     "--lifecycle-only",
                     "--lifecycle-settings-root", settingsRoot,
                     "--lifecycle-control-root", control,
                 })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ??
            throw new InvalidOperationException("Could not start the production-host lifecycle fixture.");
    }

    private static async Task WaitForStage(
        Process host,
        Task<string> hostError,
        string control,
        string stage)
    {
        var path = Path.Combine(control, stage);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        try
        {
            while (!File.Exists(path))
            {
                if (host.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Production-host lifecycle exited before {stage}: {await hostError}");
                }
                await Task.Delay(25, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Production-host lifecycle did not publish stage {stage} within 50 seconds.");
        }
    }

    private static void Signal(string control, string stage) =>
        File.WriteAllText(Path.Combine(control, stage), "ready\n");

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? Path.GetFullPath(value)
            : throw new InvalidOperationException($"Required lifecycle input {name} was absent.");

    private static async Task RequireSuccess(string step, params string[] args)
    {
        var result = await Run(args);
        if (result.Code != 0)
            throw new InvalidOperationException(
                $"Could not {step}: {result.Error}{result.Output}");
    }

    private const string LeftRailRecipe = """
        {
          "schemaVersion": 1,
          "branches": {
            "compact": { "root": { "type": "overlay", "children": [
              { "type": "region", "slot": "hero-background", "region": { "x": 0, "y": 0, "width": 1, "height": 1 } },
              { "type": "region", "slot": "source-status", "region": { "x": 0.05, "y": 0.04, "width": 0.42, "height": 0.11 } },
              { "type": "region", "slot": "details-panel", "surface": "glass", "region": { "x": 0.05, "y": 0.17, "width": 0.9, "height": 0.25 } },
              { "type": "region", "slot": "game-rail", "orientation": "vertical", "region": { "x": 0.05, "y": 0.44, "width": 0.9, "height": 0.34 } },
              { "type": "region", "slot": "controller-hints", "region": { "x": 0.5, "y": 0.82, "width": 0.45, "height": 0.15 } }
            ] } },
            "standard": { "root": { "type": "overlay", "children": [
              { "type": "region", "slot": "hero-background", "region": { "x": 0, "y": 0, "width": 1, "height": 1 } },
              { "type": "region", "slot": "game-rail", "orientation": "vertical", "region": { "x": 0.04, "y": 0.08, "width": 0.22, "height": 0.78 } },
              { "type": "region", "slot": "details-panel", "surface": "glass", "region": { "x": 0.32, "y": 0.18, "width": 0.47, "height": 0.5 } },
              { "type": "region", "slot": "source-status", "region": { "x": 0.81, "y": 0.08, "width": 0.15, "height": 0.12 } },
              { "type": "region", "slot": "controller-hints", "region": { "x": 0.58, "y": 0.89, "width": 0.38, "height": 0.08 } }
            ] } },
            "wide": { "root": { "type": "overlay", "children": [
              { "type": "region", "slot": "hero-background", "region": { "x": 0, "y": 0, "width": 1, "height": 1 } },
              { "type": "region", "slot": "game-rail", "orientation": "vertical", "region": { "x": 0.04, "y": 0.08, "width": 0.22, "height": 0.78 } },
              { "type": "region", "slot": "details-panel", "surface": "glass", "region": { "x": 0.32, "y": 0.18, "width": 0.47, "height": 0.5 } },
              { "type": "region", "slot": "source-status", "region": { "x": 0.81, "y": 0.08, "width": 0.15, "height": 0.12 } },
              { "type": "region", "slot": "controller-hints", "region": { "x": 0.58, "y": 0.89, "width": 0.38, "height": 0.08 } }
            ] } }
          }
        }
        """;

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
                System.IO.Path.GetTempPath(), "wrail-launcher-theme-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
