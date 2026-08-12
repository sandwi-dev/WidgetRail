using System.Buffers.Binary;
using GameBarAlternative.LauncherExperienceCatalog;

internal static class LauncherExperienceCatalogTests
{
    public static async Task Run()
    {
        BuiltInsAndReferenceRecipesValidate();
        ValidPackagesDiscoverWithStableDigest();
        StrictManifestAndParametersFailClosed();
        RecipeSafetyFailsWithExactPaths();
        ContentAndStyleAuthorityFailClosed();
        WebPVariantsReportExactDimensions();
        PackageAndCatalogBudgetsFailFast();
        InvalidSelectionUsesBuiltInRecovery();
        await ArchiveAndCatalogMutationsAreDeterministic();
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
        HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$.name", "duplicate_field");

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

        File.WriteAllText(recipePath, baseline.Replace(
            "{\"type\":\"region\",\"slot\":\"source-status\",\"region\":{\"x\":0.68,\"y\":0.08,\"width\":0.24,\"height\":0.1}}",
            "{\"type\":\"region\",\"slot\":\"collection-tabs\",\"region\":{\"x\":0.68,\"y\":0.08,\"width\":0.24,\"height\":0.1}}",
            StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "missing_critical_slot");

        File.WriteAllText(recipePath, baseline.Replace(
            "\"x\":0.68,\"y\":0.08,\"width\":0.24,\"height\":0.1",
            "\"x\":0.1,\"y\":0.1,\"width\":0.24,\"height\":0.1", StringComparison.Ordinal));
        HasCode(validator.ValidateDirectory(package).Diagnostics, "invalid_overlap");

        File.WriteAllText(recipePath, baseline.Replace(
            "{\"type\":\"overlay\",\"children\"",
            "{\"type\":\"grid\",\"rows\":13,\"columns\":1,\"children\"", StringComparison.Ordinal));
        HasPathCode(validator.ValidateDirectory(package).Diagnostics,
            "$.branches.compact.root.rows", "grid_out_of_range");

        File.WriteAllText(recipePath, NodeLimitRecipe());
        HasCode(validator.ValidateDirectory(package).Diagnostics, "too_many_nodes");

        File.WriteAllText(recipePath, DepthLimitRecipe());
        HasCode(validator.ValidateDirectory(package).Diagnostics, "layout_depth");
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

        File.WriteAllText(Path.Combine(package, "launcher.json"),
            Manifest("dev.example.content", "1.0.0").Replace(
                "assets/preview.png", "../outside.png", StringComparison.Ordinal));
        HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$.previewFile", "unsafe_asset_path");

        File.WriteAllText(Path.Combine(package, "launcher.json"), Manifest("dev.example.content", "1.0.0"));
        foreach (var forbidden in new[] { "payload.exe", "payload.zip", "payload.html" })
        {
            File.WriteAllText(Path.Combine(package, forbidden), "not trusted package data");
            HasPathCode(validator.ValidateDirectory(package).Diagnostics, forbidden, "forbidden_content");
            File.Delete(Path.Combine(package, forbidden));
        }
    }

    private static void WebPVariantsReportExactDimensions()
    {
        var variants = new[]
        {
            (Name: "VP8", Bytes: Vp8(640, 360), Width: 640, Height: 360),
            (Name: "VP8L", Bytes: Vp8L(321, 177), Width: 321, Height: 177),
            (Name: "VP8X", Bytes: Vp8X(1920, 1080), Width: 1920, Height: 1080),
        };
        foreach (var variant in variants)
        {
            True(LauncherExperienceFileGuard.TryReadImageDimensions(
                variant.Bytes, ".webp", out var width, out var height), $"{variant.Name} WebP was rejected.");
            Equal(variant.Width, width);
            Equal(variant.Height, height);

            using var temp = new TempDirectory();
            var package = WritePackage(temp.Path, $"dev.example.{variant.Name.ToLowerInvariant()}", "1.0.0", BottomRecipe());
            File.Delete(Path.Combine(package, "assets", "preview.png"));
            File.WriteAllBytes(Path.Combine(package, "assets", "preview.webp"), variant.Bytes);
            var manifest = File.ReadAllText(Path.Combine(package, "launcher.json"))
                .Replace("assets/preview.png", "assets/preview.webp", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(package, "launcher.json"), manifest);
            True(new LauncherExperienceValidator().ValidateDirectory(package).IsValid,
                $"{variant.Name} package validation failed.");
        }

        var truncated = Vp8X(64, 36)[..^1];
        True(!LauncherExperienceFileGuard.TryReadImageDimensions(truncated, ".webp", out _, out _),
            "Truncated WebP was accepted.");
        var malformed = Vp8X(64, 36);
        malformed[8] = (byte)'X';
        True(!LauncherExperienceFileGuard.TryReadImageDimensions(malformed, ".webp", out _, out _),
            "Malformed WebP was accepted.");
        True(!LauncherExperienceFileGuard.TryReadImageDimensions(Vp8(0, 36), ".webp", out _, out _),
            "Zero-dimension VP8 WebP was accepted.");

        ValidateWebPFailure("truncated", truncated);
        ValidateWebPFailure("malformed", malformed);
        ValidateWebPFailure("zero", Vp8(0, 36));

        using var oversizedTemp = new TempDirectory();
        var oversizedPackage = WritePackage(
            oversizedTemp.Path, "dev.example.oversized-webp", "1.0.0", BottomRecipe());
        File.Delete(Path.Combine(oversizedPackage, "assets", "preview.png"));
        File.WriteAllBytes(Path.Combine(oversizedPackage, "assets", "preview.webp"), Vp8X(4097, 32));
        File.WriteAllText(Path.Combine(oversizedPackage, "launcher.json"),
            File.ReadAllText(Path.Combine(oversizedPackage, "launcher.json"))
                .Replace("assets/preview.png", "assets/preview.webp", StringComparison.Ordinal));
        HasPathCode(new LauncherExperienceValidator().ValidateDirectory(oversizedPackage).Diagnostics,
            "assets/preview.webp", "image_dimensions");

        static void ValidateWebPFailure(string suffix, byte[] bytes)
        {
            using var temp = new TempDirectory();
            var package = WritePackage(temp.Path, $"dev.example.{suffix}-webp", "1.0.0", BottomRecipe());
            File.Delete(Path.Combine(package, "assets", "preview.png"));
            File.WriteAllBytes(Path.Combine(package, "assets", "preview.webp"), bytes);
            File.WriteAllText(Path.Combine(package, "launcher.json"),
                File.ReadAllText(Path.Combine(package, "launcher.json"))
                    .Replace("assets/preview.png", "assets/preview.webp", StringComparison.Ordinal));
            HasPathCode(new LauncherExperienceValidator().ValidateDirectory(package).Diagnostics,
                "assets/preview.webp", "invalid_image");
        }
    }

    private static void PackageAndCatalogBudgetsFailFast()
    {
        var validator = new LauncherExperienceValidator();
        EntrySet("all-valid", index => $"asset-{index:D2}.png", maximumDiagnostics: 1);
        EntrySet("all-forbidden", index => $"payload-{index:D2}.exe",
            maximumDiagnostics: LauncherExperienceValidator.MaximumFiles + 1);
        EntrySet("mixed", index => index % 2 == 0
                ? $"asset-{index:D2}.png"
                : $"payload-{index:D2}.zip",
            maximumDiagnostics: LauncherExperienceValidator.MaximumFiles + 1);
        EntrySet("repeated-invalid", index =>
                new string('x', 110) + $"-{index:D2}.png",
            maximumDiagnostics: LauncherExperienceValidator.MaximumFiles + 1);

        using (var temp = new TempDirectory())
        {
            var package = WritePackage(temp.Path, "dev.example.files", "1.0.0", BottomRecipe());
            for (var index = 0; index < 61; index++)
                File.WriteAllText(Path.Combine(package, $"extra-{index:D2}.json"), "{}");
            HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$", "too_many_files");
        }
        using (var temp = new TempDirectory())
        {
            var package = WritePackage(temp.Path, "dev.example.directories", "1.0.0", BottomRecipe());
            for (var index = 0; index < 62; index++)
                Directory.CreateDirectory(Path.Combine(package, $"extra-{index:D2}"));
            HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$", "too_many_directories");
        }
        using (var temp = new TempDirectory())
        {
            var package = WritePackage(temp.Path, "dev.example.file-size", "1.0.0", BottomRecipe());
            SetLength(Path.Combine(package, "assets", "oversized.webp"), LauncherExperienceValidator.MaximumAssetBytes + 1);
            HasPathCode(validator.ValidateDirectory(package).Diagnostics, "assets/oversized.webp", "file_too_large");
        }
        using (var temp = new TempDirectory())
        {
            var package = WritePackage(temp.Path, "dev.example.expanded", "1.0.0", BottomRecipe());
            SetLength(Path.Combine(package, "assets", "large-a.webp"), LauncherExperienceValidator.MaximumAssetBytes);
            SetLength(Path.Combine(package, "assets", "large-b.webp"), LauncherExperienceValidator.MaximumAssetBytes);
            HasPathCode(validator.ValidateDirectory(package).Diagnostics, "$", "package_too_large");
        }
        using (var temp = new TempDirectory())
        {
            var package = WritePackage(temp.Path, "dev.example.reparse", "1.0.0", BottomRecipe());
            var outside = Path.Combine(temp.Path, "outside");
            Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(Path.Combine(package, "linked"), outside);
            HasPathCode(validator.ValidateDirectory(package).Diagnostics, "linked", "reparse_point");
        }
        using (var temp = new TempDirectory())
        {
            for (var index = 0; index <= LauncherExperienceCatalog.MaximumInstalledVersions; index++)
                Directory.CreateDirectory(Path.Combine(temp.Path, $"dev.example.id-{index:D3}"));
            ThrowsCode(() => new LauncherExperienceCatalog(temp.Path).Discover(), "too_many_experience_ids");
        }
        using (var temp = new TempDirectory())
        {
            var id = Path.Combine(temp.Path, "dev.example.versions");
            for (var index = 0; index <= LauncherExperienceCatalog.MaximumInstalledVersions; index++)
                Directory.CreateDirectory(Path.Combine(id, $"1.0.{index}"));
            ThrowsCode(() => new LauncherExperienceCatalog(temp.Path).Discover(), "too_many_experiences");
        }

        void EntrySet(string name, Func<int, string> fileName, int maximumDiagnostics)
        {
            using var temp = new TempDirectory();
            var package = Path.Combine(temp.Path, name);
            Directory.CreateDirectory(package);
            var unvisited = Path.Combine(package, "unvisited-tail");
            Directory.CreateDirectory(unvisited);
            File.WriteAllText(Path.Combine(unvisited, "must-not-be-diagnosed.exe"), "tail");
            for (var index = 0; index <= LauncherExperienceValidator.MaximumFiles; index++)
                File.WriteAllText(Path.Combine(package, fileName(index)), "entry");
            var result = validator.ValidateDirectory(package);
            HasPathCode(result.Diagnostics, "$", "too_many_files");
            True(result.Diagnostics.Count <= maximumDiagnostics,
                $"{name} accumulated {result.Diagnostics.Count} diagnostics past its bounded set.");
            True(!result.Diagnostics.Any(item => item.Path.Contains("must-not-be-diagnosed", StringComparison.Ordinal)),
                $"{name} continued into the tail tree after file 65.");
        }
    }

    private static void InvalidSelectionUsesBuiltInRecovery()
    {
        using var temp = new TempDirectory();
        var catalog = new LauncherExperienceCatalog(temp.Path);
        var recovery = catalog.ResolveOrRecovery("dev.missing.pack", "1.0.0", LauncherLayoutPreset.CompactGrid);
        Equal("org.gbar.builtin.compact-grid", recovery.Descriptor.Id);
        True(recovery.Descriptor.IsBuiltIn, "Missing selection did not resolve to a built-in recovery descriptor.");
    }

    private static async Task ArchiveAndCatalogMutationsAreDeterministic()
    {
        using var temp = new TempDirectory();
        var source = WritePackage(
            Path.Combine(temp.Path, "source"), "dev.example.archive", "1.0.0", BottomRecipe());
        var firstPath = Path.Combine(temp.Path, "first.gbarlauncher");
        var secondPath = Path.Combine(temp.Path, "second.gbarlauncher");
        var first = await LauncherExperienceArchive.PackAsync(source, firstPath, CancellationToken.None);
        var second = await LauncherExperienceArchive.PackAsync(source, secondPath, CancellationToken.None);
        Equal(first.Sha256, second.Sha256);
        SequenceEqual(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        var inspected = await LauncherExperienceArchive.InspectArchiveAsync(firstPath, CancellationToken.None);
        Equal("dev.example.archive", inspected.Package.Manifest.Id);
        Equal(first.Inspection.Package.Descriptor.ContentDigest, inspected.Package.Descriptor.ContentDigest);

        var catalogRoot = Path.Combine(temp.Path, "catalog");
        var installed = await LauncherExperienceArchive.InstallAsync(
            inspected, catalogRoot, CancellationToken.None);
        True(Directory.Exists(installed), "Installed archive was not published.");
        ThrowsCode(() => LauncherExperienceArchive.InstallAsync(
            inspected, catalogRoot, CancellationToken.None).GetAwaiter().GetResult(), "immutable_version");
        await LauncherExperienceArchive.RemoveAsync(
            catalogRoot, "dev.example.archive", "1.0.0", CancellationToken.None);
        True(!Directory.Exists(installed), "Exact installed version was not removed.");

        var imagePath = Path.Combine(source, "assets", "preview.png");
        File.WriteAllBytes(imagePath, [.. File.ReadAllBytes(imagePath), .. "acTL"u8.ToArray()]);
        HasPathCode(new LauncherExperienceValidator().ValidateDirectory(source).Diagnostics,
            "assets/preview.png", "multi_frame_image");
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

    private static byte[] Vp8(int width, int height)
    {
        var payload = new byte[10];
        payload[3] = 0x9d;
        payload[4] = 0x01;
        payload[5] = 0x2a;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), checked((ushort)width));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), checked((ushort)height));
        return WebP("VP8 ", payload);
    }

    private static byte[] Vp8L(int width, int height)
    {
        var payload = new byte[5];
        payload[0] = 0x2f;
        var bits = checked((uint)(width - 1)) | (checked((uint)(height - 1)) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1, 4), bits);
        return WebP("VP8L", payload);
    }

    private static byte[] Vp8X(int width, int height)
    {
        var payload = new byte[10];
        WriteUInt24(payload.AsSpan(4, 3), width - 1);
        WriteUInt24(payload.AsSpan(7, 3), height - 1);
        return WebP("VP8X", payload);
    }

    private static byte[] WebP(string fourCc, byte[] payload)
    {
        var paddedLength = payload.Length + (payload.Length & 1);
        var bytes = new byte[20 + paddedLength];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), checked((uint)(bytes.Length - 8)));
        "WEBP"u8.CopyTo(bytes.AsSpan(8, 4));
        System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), checked((uint)payload.Length));
        payload.CopyTo(bytes, 20);
        return bytes;
    }

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private static string NodeLimitRecipe()
    {
        var leaf = "{\"type\":\"region\",\"slot\":\"hero-background\"}";
        var group = "{\"type\":\"overlay\",\"children\":[" + string.Join(',', Enumerable.Repeat(leaf, 4)) + "]}";
        var root = "{\"type\":\"overlay\",\"children\":[" + string.Join(',', Enumerable.Repeat(group, 32)) + "]}";
        return "{\"schemaVersion\":1,\"branches\":{\"compact\":{\"root\":" + root + "}}}";
    }

    private static string DepthLimitRecipe()
    {
        var node = "{\"type\":\"region\",\"slot\":\"game-rail\"}";
        for (var index = 0; index < 18; index++)
            node = "{\"type\":\"inset\",\"children\":[" + node + "]}";
        return "{\"schemaVersion\":1,\"branches\":{\"compact\":{\"root\":" + node + "}}}";
    }

    private static void SetLength(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
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

    private static void ThrowsCode(Action action, string code)
    {
        try
        {
            action();
        }
        catch (LauncherExperiencePackageException exception) when (exception.Code == code)
        {
            return;
        }
        throw new InvalidOperationException($"Expected LauncherExperiencePackageException '{code}'.");
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
