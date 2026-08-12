using System.Buffers.Binary;
using GameBarAlternative.LauncherExperienceCatalog;

internal static class LauncherExperienceCatalogTests
{
    public static Task Run()
    {
        BuiltInsAndReferenceRecipesValidate();
        ValidPackagesDiscoverWithStableDigest();
        StrictManifestAndParametersFailClosed();
        RecipeSafetyFailsWithExactPaths();
        ContentAndStyleAuthorityFailClosed();
        InvalidSelectionUsesBuiltInRecovery();
        return Task.CompletedTask;
    }

    private static void BuiltInsAndReferenceRecipesValidate()
    {
        var validator = new LauncherExperienceValidator();
        Equal(4, LauncherExperienceBuiltIns.RecoveryPackages.Count);
        Equal(0, validator.ValidateRecipe(LauncherExperienceBuiltIns.BottomRailReferenceRecipe).Count);
        Equal(0, validator.ValidateRecipe(LauncherExperienceBuiltIns.LeftRailGlassPanelReferenceRecipe).Count);
        var leftDetails = LauncherExperienceBuiltIns.LeftRailGlassPanelReferenceRecipe
            .Branches[LauncherResponsiveBranch.Wide].Children
            .Single(item => item.Slot == LauncherSlot.DetailsPanel);
        Equal<LauncherSurfaceRole?>(LauncherSurfaceRole.Glass, leftDetails.Surface);
        SequenceEqual(
            new[] { LauncherLayoutPreset.HeroRail, LauncherLayoutPreset.CoverWall, LauncherLayoutPreset.Carousel, LauncherLayoutPreset.CompactGrid },
            LauncherExperienceBuiltIns.RecoveryPackages.Select(item => item.Descriptor.LayoutPreset));
        True(LauncherExperienceBuiltIns.RecoveryPackages.All(item => item.Descriptor.IsBuiltIn),
            "Recovery descriptors must remain built-in and package-independent.");
    }

    private static void ValidPackagesDiscoverWithStableDigest()
    {
        using var temp = new TempDirectory();
        var package = WritePackage(temp.Path, "dev.example.bottom", "1.0.0", BottomRecipe());
        var validator = new LauncherExperienceValidator();
        var first = validator.ValidateDirectory(package, "dev.example.bottom", "1.0.0");
        var second = validator.ValidateDirectory(package, "dev.example.bottom", "1.0.0");
        True(first.IsValid, Describe(first));
        True(second.IsValid, Describe(second));
        Equal(first.Package!.Descriptor.ContentDigest, second.Package!.Descriptor.ContentDigest);
        Equal(3, first.Package.Recipe!.Branches.Count);

        var catalog = new LauncherExperienceCatalog(temp.Path);
        var snapshot = catalog.Discover();
        Equal(5, snapshot.Experiences.Count);
        var installed = snapshot.Experiences.Single(item => item.Descriptor.Id == "dev.example.bottom");
        True(installed.IsValid, string.Join("; ", installed.Diagnostics));
        True(!installed.Descriptor.IsBuiltIn, "Installed package was incorrectly marked built-in.");
    }

    private static void StrictManifestAndParametersFailClosed()
    {
        using var temp = new TempDirectory();
        var validator = new LauncherExperienceValidator();
        var package = WritePackage(temp.Path, "dev.example.strict", "1.0.0", BottomRecipe());
        var manifestPath = Path.Combine(package, "launcher.json");
        var baseline = File.ReadAllText(manifestPath);

        File.WriteAllText(manifestPath, baseline.Replace("\"name\":\"Strict\"", "\"name\":\"Strict\",\"name\":\"Duplicate\"", StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "invalid_json");

        File.WriteAllText(manifestPath, baseline.Replace("\"parameters\":{}", "\"parameters\":{\"action\":\"launch\"}", StringComparison.Ordinal));
        HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$.parameters.action", "unknown_field");

        File.WriteAllText(manifestPath, baseline.Replace("\"parameters\":{}", "\"parameters\":{\"tileSize\":\"enormous\"}", StringComparison.Ordinal));
        HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$.parameters.tileSize", "invalid_parameter");

        File.WriteAllText(manifestPath, baseline.Replace("\"styleFile\":\"styles/launcher.gbss\"", "\"styleFile\":\"https://evil.example/theme.gbss\"", StringComparison.Ordinal));
        HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$.styleFile", "unsafe_path");

        File.WriteAllText(manifestPath, baseline);
        var mismatch = validator.ValidateDirectory(package, "dev.example.other", "2.0.0");
        HasPathCode(mismatch.Diagnostics, "$.id", "identity_mismatch");
        HasPathCode(mismatch.Diagnostics, "$.version", "identity_mismatch");
    }

    private static void RecipeSafetyFailsWithExactPaths()
    {
        using var temp = new TempDirectory();
        var validator = new LauncherExperienceValidator();
        var package = WritePackage(temp.Path, "dev.example.recipe", "1.0.0", BottomRecipe());
        var recipePath = Path.Combine(package, "layouts", "launcher-layout.json");
        var baseline = File.ReadAllText(recipePath);

        File.WriteAllText(recipePath, baseline.Replace("\"slot\":\"controller-hints\"", "\"slot\":\"game-rail\"", StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "slot_reuse");

        File.WriteAllText(recipePath, baseline.Replace("\"width\":0.4", "\"width\":1.4", StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "out_of_bounds");

        File.WriteAllText(recipePath, baseline.Replace("\"width\":0.84,\"height\":0.24", "\"width\":0.1,\"height\":0.05", StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "clipped_focus_extent");

        File.WriteAllText(recipePath, baseline.Replace("\"width\":0.4,\"height\":0.06", "\"width\":0.1,\"height\":0.02", StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "unreachable_back");

        File.WriteAllText(recipePath, baseline.Replace("{\"type\":\"overlay\",\"children\"", "{\"type\":\"grid\",\"children\"", StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "grid_dimensions_required");

        File.WriteAllText(recipePath, baseline.Replace("\"slot\":\"source-status\"", "\"slot\":\"provider-binding\"", StringComparison.Ordinal));
        HasPathCode(validator.ValidateDirectory(package).Diagnostics,
            "$.branches.compact.root.children[2].slot", "invalid_slot");
    }

    private static void ContentAndStyleAuthorityFailClosed()
    {
        using var temp = new TempDirectory();
        var validator = new LauncherExperienceValidator();
        var package = WritePackage(temp.Path, "dev.example.content", "1.0.0", BottomRecipe());
        File.WriteAllText(Path.Combine(package, "payload.js"), "fetch('https://evil.example')");
        HasPathCode(validator.ValidateDirectory(package).Diagnostics, "payload.js", "forbidden_content");
        File.Delete(Path.Combine(package, "payload.js"));

        File.WriteAllText(Path.Combine(package, "styles", "launcher.gbss"), "button { color: #ffffff; }");
        HasCode(validator.ValidateDirectory(package).Diagnostics, "cross_widget_selector");

        File.WriteAllText(Path.Combine(package, "styles", "launcher.gbss"), "#launch { color: #ffffff; }");
        HasCode(validator.ValidateDirectory(package).Diagnostics, "cross_widget_selector");

        File.WriteAllBytes(Path.Combine(package, "assets", "preview.png"), Png(5000, 32));
        HasPathCode(validator.ValidateDirectory(package).Diagnostics, "assets/preview.png", "image_dimensions");
    }

    private static void InvalidSelectionUsesBuiltInRecovery()
    {
        using var temp = new TempDirectory();
        var catalog = new LauncherExperienceCatalog(temp.Path);
        var recovery = catalog.ResolveOrRecovery("dev.missing.pack", "1.0.0", LauncherLayoutPreset.CompactGrid);
        Equal("org.gbar.builtin.compact-grid", recovery.Descriptor.Id);
        True(recovery.Descriptor.IsBuiltIn, "Missing selection did not resolve to a built-in recovery descriptor.");
    }

    private static string WritePackage(string root, string id, string version, string recipe)
    {
        var directory = Path.Combine(root, id, version);
        Directory.CreateDirectory(Path.Combine(directory, "layouts"));
        Directory.CreateDirectory(Path.Combine(directory, "styles"));
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.WriteAllText(Path.Combine(directory, "launcher.json"), Manifest(id, version));
        File.WriteAllText(Path.Combine(directory, "layouts", "launcher-layout.json"), recipe);
        File.WriteAllText(Path.Combine(directory, "styles", "launcher.gbss"),
            "launcher-game-rail { color: #ffffff; } launcher-details-panel { background: rgba(0, 0, 0, 0.5); }");
        File.WriteAllBytes(Path.Combine(directory, "assets", "preview.png"), Png(64, 36));
        return directory;
    }

    private static string Manifest(string id, string version) =>
        "{\"schemaVersion\":1,\"id\":\"" + id +
        "\",\"publisher\":\"dev.example\",\"name\":\"Strict\",\"version\":\"" + version +
        "\",\"layoutPreset\":\"hero-rail\",\"compositionFile\":\"layouts/launcher-layout.json\"," +
        "\"styleFile\":\"styles/launcher.gbss\",\"previewFile\":\"assets/preview.png\",\"parameters\":{}}";

    private static string BottomRecipe() =>
        "{\"schemaVersion\":1,\"branches\":{" +
        "\"compact\":{\"root\":" + RootChildren() + "}," +
        "\"standard\":{\"root\":" + RootChildren() + "}," +
        "\"wide\":{\"root\":" + RootChildren() + "}}}";

    private static string RootChildren() =>
        "{\"type\":\"overlay\",\"children\":[" +
        "{\"type\":\"region\",\"slot\":\"hero-background\",\"region\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}}," +
        "{\"type\":\"region\",\"slot\":\"game-rail\",\"orientation\":\"horizontal\",\"region\":{\"x\":0.08,\"y\":0.62,\"width\":0.84,\"height\":0.24}}," +
        "{\"type\":\"region\",\"slot\":\"source-status\",\"region\":{\"x\":0.68,\"y\":0.08,\"width\":0.24,\"height\":0.1}}," +
        "{\"type\":\"region\",\"slot\":\"details-panel\",\"region\":{\"x\":0.08,\"y\":0.08,\"width\":0.5,\"height\":0.4}}," +
        "{\"type\":\"region\",\"slot\":\"controller-hints\",\"region\":{\"x\":0.52,\"y\":0.91,\"width\":0.4,\"height\":0.06}}]}";

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private static string Describe(LauncherExperienceValidationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Path}: {item.Code}: {item.Message}"));

    private static void HasCode(IEnumerable<LauncherExperienceDiagnostic> diagnostics, string code)
    {
        if (!diagnostics.Any(item => item.Code == code))
            throw new InvalidOperationException($"Expected {code}; actual: {string.Join("; ", diagnostics)}");
    }

    private static void HasPathCode(IEnumerable<LauncherExperienceDiagnostic> diagnostics, string path, string code)
    {
        if (!diagnostics.Any(item => item.Path == path && item.Code == code))
            throw new InvalidOperationException($"Expected {path} {code}; actual: {string.Join("; ", diagnostics)}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "launcher-experience-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
