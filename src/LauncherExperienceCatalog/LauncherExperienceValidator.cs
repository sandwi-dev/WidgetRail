using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using GameBarAlternative.WidgetStyling;

namespace GameBarAlternative.LauncherExperienceCatalog;

public sealed class LauncherExperienceValidator
{
    public const int MaximumFiles = 64;
    public const int MaximumDirectories = 64;
    public const long MaximumExpandedBytes = 32L * 1024 * 1024;
    public const long MaximumAssetBytes = 16L * 1024 * 1024;
    public const int MaximumImageDimension = 4096;
    public const int MaximumPathLength = 240;
    public const int MaximumManifestBytes = 128 * 1024;
    public const int MaximumRecipeBytes = 256 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        [".json", ".gbss", ".png", ".jpg", ".jpeg", ".webp"];
    private static readonly HashSet<string> LauncherStyleRoles =
        ["launcher", "launcher-hero-background", "launcher-game-rail", "launcher-details-panel", "launcher-collection-tabs", "launcher-source-status", "launcher-operation-status", "launcher-system-status", "launcher-controller-hints"];
    private static readonly LauncherSlot[] CriticalSlots =
        [LauncherSlot.GameRail, LauncherSlot.DetailsPanel, LauncherSlot.SourceStatus, LauncherSlot.ControllerHints];

    public LauncherExperienceValidationResult ValidateDirectory(
        string packageRoot,
        string? expectedId = null,
        string? expectedVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var errors = new List<LauncherExperienceDiagnostic>();
        var fullRoot = Path.GetFullPath(packageRoot);
        try
        {
            if (!Directory.Exists(fullRoot))
                return Error("$", "package_not_found", "Launcher experience directory does not exist.");
            LauncherExperienceFileGuard.RejectReparsePoint(fullRoot);
            var files = EnumerateFiles(fullRoot, errors);
            if (errors.Count != 0) return LauncherExperienceValidationResult.Invalid(errors);
            var manifestPath = ResolveExact(files, "launcher.json");
            if (manifestPath is null)
                return Error("launcher.json", "missing_manifest", "Package must contain exact-case launcher.json at its root.");
            var manifestBytes = LauncherExperienceFileGuard.ReadBounded(manifestPath, MaximumManifestBytes);
            var manifest = LauncherExperienceDocumentParser.ParseManifest(manifestBytes, errors);
            if (manifest is null) return LauncherExperienceValidationResult.Invalid(errors);
            if (expectedId is not null && !string.Equals(expectedId, manifest.Id, StringComparison.Ordinal))
                errors.Add(new("$.id", "identity_mismatch", "Manifest ID must exactly match its catalog directory."));
            if (expectedVersion is not null && !string.Equals(expectedVersion, manifest.Version.ToString(), StringComparison.Ordinal))
                errors.Add(new("$.version", "identity_mismatch", "Manifest version must exactly match its catalog directory."));

            LauncherLayoutRecipe? recipe = null;
            if (manifest.CompositionFile is not null)
            {
                var recipePath = ResolveExact(files, manifest.CompositionFile);
                if (recipePath is null)
                    errors.Add(new("$.compositionFile", "missing_file", "Referenced composition file is missing or case-mismatched."));
                else
                {
                    var recipeBytes = LauncherExperienceFileGuard.ReadBounded(recipePath, MaximumRecipeBytes);
                    recipe = LauncherExperienceDocumentParser.ParseRecipe(recipeBytes, manifest.CompositionFile, errors);
                    if (recipe is not null) ValidateRecipe(recipe, errors);
                }
            }
            ValidateReferencedFile(files, manifest.StyleFile, "$.styleFile", errors);
            ValidateReferencedFile(files, manifest.PreviewFile, "$.previewFile", errors);
            ValidateFileRoles(files.Keys, manifest, errors);
            ValidateGbss(fullRoot, files, manifest.StyleFile, errors);
            ValidateImages(files, errors);

            if (errors.Count != 0) return LauncherExperienceValidationResult.Invalid(errors);
            var relativeFiles = files.Keys.Order(StringComparer.Ordinal).ToArray();
            var digest = ComputeDigest(fullRoot, relativeFiles);
            var descriptor = new LauncherExperienceDescriptor(
                manifest.Id, manifest.Publisher, manifest.Name, manifest.Version,
                manifest.LayoutPreset, false, digest);
            var package = new LauncherExperiencePackage(
                descriptor, manifest, recipe, fullRoot, Array.AsReadOnly(relativeFiles));
            return new LauncherExperienceValidationResult(package, []);
        }
        catch (LauncherExperiencePackageException exception)
        {
            errors.Add(new(exception.DiagnosticPath, exception.Code, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add(new("$", "package_io_error", "Launcher experience package could not be read."));
        }
        return LauncherExperienceValidationResult.Invalid(errors);

        LauncherExperienceValidationResult Error(string path, string code, string message)
        {
            errors.Add(new(path, code, message));
            return LauncherExperienceValidationResult.Invalid(errors);
        }
    }

    public IReadOnlyList<LauncherExperienceDiagnostic> ValidateRecipe(LauncherLayoutRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var errors = new List<LauncherExperienceDiagnostic>();
        ValidateRecipe(recipe, errors);
        return errors;
    }

    private static Dictionary<string, string> EnumerateFiles(
        string root,
        List<LauncherExperienceDiagnostic> errors)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;
        var directoryCount = 0;
        var encounteredFileCount = 0;
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count != 0)
        {
            var directory = directories.Pop();
            LauncherExperienceFileGuard.RejectReparsePoint(directory);
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var relative = Path.GetRelativePath(root, child).Replace(Path.DirectorySeparatorChar, '/');
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new LauncherExperiencePackageException(
                        "reparse_point", "Package paths cannot contain reparse points.", diagnosticPath: relative);
                if (!LauncherExperienceFileGuard.IsWithin(root, child))
                    throw new LauncherExperiencePackageException(
                        "path_escape", "Package directory escapes its root.", diagnosticPath: relative);
                if (++directoryCount > MaximumDirectories)
                    throw new LauncherExperiencePackageException(
                        "too_many_directories", $"Package may contain at most {MaximumDirectories} directories.");
                directories.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (++encounteredFileCount > MaximumFiles)
                    throw new LauncherExperiencePackageException(
                        "too_many_files", $"Package may contain at most {MaximumFiles} files.");
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new LauncherExperiencePackageException(
                        "reparse_point", "Package paths cannot contain reparse points.", diagnosticPath: relative);
                if (!LauncherExperienceFileGuard.IsSafePackagePath(relative))
                {
                    errors.Add(new(relative, "unsafe_path", "Package path is not normalized or bounded."));
                    continue;
                }
                if (!AllowedExtensions.Contains(Path.GetExtension(relative)))
                {
                    errors.Add(new(relative, "forbidden_content", "Package contains a non-data file type."));
                    continue;
                }
                if (!files.TryAdd(relative, file))
                {
                    errors.Add(new(relative, "duplicate_path", "Package contains a duplicate path."));
                    continue;
                }
                var length = new FileInfo(file).Length;
                if (length > MaximumAssetBytes)
                    throw new LauncherExperiencePackageException(
                        "file_too_large", $"Package file exceeds {MaximumAssetBytes} bytes.", diagnosticPath: relative);
                total = checked(total + length);
                if (total > MaximumExpandedBytes)
                    throw new LauncherExperiencePackageException(
                        "package_too_large", $"Expanded package exceeds {MaximumExpandedBytes} bytes.");
            }
        }
        return files;
    }

    private static string? ResolveExact(IReadOnlyDictionary<string, string> files, string relativePath) =>
        files.TryGetValue(relativePath, out var value) ? value : null;

    private static void ValidateReferencedFile(
        IReadOnlyDictionary<string, string> files,
        string relativePath,
        string diagnosticPath,
        List<LauncherExperienceDiagnostic> errors)
    {
        if (ResolveExact(files, relativePath) is null)
            errors.Add(new(diagnosticPath, "missing_file", "Referenced package file is missing or case-mismatched."));
    }

    private static void ValidateGbss(
        string root,
        IReadOnlyDictionary<string, string> files,
        string entryFile,
        List<LauncherExperienceDiagnostic> errors)
    {
        if (ResolveExact(files, entryFile) is null) return;
        var package = GbssPackageLoader.Load(entryFile, new GbssFileSourceProvider(root));
        foreach (var diagnostic in package.Diagnostics.Where(item => item.Severity == GbssDiagnosticSeverity.Error))
            errors.Add(new(diagnostic.Source, $"gbss_{diagnostic.Code}", diagnostic.Message));
        if (!package.IsValid) return;
        var compiled = GbssThemeCompiler.Compile(package);
        foreach (var diagnostic in compiled.Diagnostics.Where(item => item.Severity == GbssDiagnosticSeverity.Error))
            errors.Add(new(diagnostic.Source, $"gbss_{diagnostic.Code}", diagnostic.Message));
        foreach (var selector in package.Documents.SelectMany(document => document.Statements)
                     .OfType<GbssRule>().SelectMany(rule => rule.Selectors))
        {
            if (selector.Id is not null ||
                (!selector.IsRoot && (selector.Role is null || !LauncherStyleRoles.Contains(selector.Role))))
                errors.Add(new(selector.Text, "cross_widget_selector",
                    "Launcher experience GBSS may target only launcher semantic roles and cannot use ID selectors."));
        }
    }

    private static void ValidateFileRoles(
        IEnumerable<string> files,
        LauncherExperienceManifest manifest,
        List<LauncherExperienceDiagnostic> errors)
    {
        foreach (var relative in files)
        {
            var extension = Path.GetExtension(relative);
            if (extension == ".json" &&
                !string.Equals(relative, "launcher.json", StringComparison.Ordinal) &&
                !string.Equals(relative, manifest.CompositionFile, StringComparison.Ordinal))
                errors.Add(new(relative, "unexpected_data_file", "Package contains an unreferenced JSON document."));
            else if (extension == ".gbss" && !relative.StartsWith("styles/", StringComparison.Ordinal))
                errors.Add(new(relative, "unexpected_style_file", "GBSS files must remain under styles/."));
            else if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" &&
                     !relative.StartsWith("assets/", StringComparison.Ordinal))
                errors.Add(new(relative, "unexpected_asset_file", "Images must remain under assets/."));
        }
    }

    private static void ValidateImages(
        IReadOnlyDictionary<string, string> files,
        List<LauncherExperienceDiagnostic> errors)
    {
        foreach (var (relative, file) in files)
        {
            var extension = Path.GetExtension(relative);
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) continue;
            var bytes = LauncherExperienceFileGuard.ReadBounded(file, MaximumAssetBytes);
            if (!LauncherExperienceFileGuard.TryReadImageDimensions(bytes, extension, out var width, out var height))
            {
                errors.Add(new(relative, "invalid_image", "Image metadata is invalid or unsupported."));
                continue;
            }
            if (!LauncherExperienceFileGuard.IsSingleFrameImage(bytes, extension))
            {
                errors.Add(new(relative, "multi_frame_image", "Animated or multi-frame images are not supported."));
                continue;
            }
            if (width > MaximumImageDimension || height > MaximumImageDimension)
                errors.Add(new(relative, "image_dimensions", $"Image dimensions may not exceed {MaximumImageDimension} by {MaximumImageDimension}."));
        }
    }

    private static void ValidateRecipe(LauncherLayoutRecipe recipe, List<LauncherExperienceDiagnostic> errors)
    {
        if (recipe.SchemaVersion != 1)
            errors.Add(new("$.schemaVersion", "unsupported_version", "Expected layout recipe schema version 1."));
        foreach (var (branch, root) in recipe.Branches)
        {
            var branchName = branch.ToString().ToLowerInvariant();
            var slots = new Dictionary<LauncherSlot, (LauncherRegion Region, string Path)>();
            Visit(root, LauncherRegion.Full, $"$.branches.{branchName}.root");
            foreach (var critical in CriticalSlots)
                if (!slots.ContainsKey(critical))
                    errors.Add(new($"$.branches.{branchName}", "missing_critical_slot",
                        $"Responsive branch must contain {Name(critical)}."));
            if (slots.TryGetValue(LauncherSlot.GameRail, out var rail) &&
                (rail.Region.Width < 0.15 || rail.Region.Height < 0.12))
                errors.Add(new(rail.Path, "clipped_focus_extent", "Game rail is too small for a visible focus target."));
            if (slots.TryGetValue(LauncherSlot.ControllerHints, out var hints) &&
                (hints.Region.Width < 0.12 || hints.Region.Height < 0.04))
                errors.Add(new(hints.Path, "unreachable_back", "Controller hints cannot expose a reachable Back route."));
            var interactive = slots.Where(item => item.Key != LauncherSlot.HeroBackground).ToArray();
            for (var first = 0; first < interactive.Length; first++)
                for (var second = first + 1; second < interactive.Length; second++)
                    if (Overlaps(interactive[first].Value.Region, interactive[second].Value.Region))
                        errors.Add(new(interactive[second].Value.Path, "invalid_overlap",
                            $"{Name(interactive[first].Key)} overlaps {Name(interactive[second].Key)}."));

            void Visit(LauncherLayoutNode node, LauncherRegion parent, string path)
            {
                var current = Compose(parent, node.Region, node.Insets);
                if (!IsValidRegion(current))
                    errors.Add(new($"{path}.region", "out_of_bounds", "Layout geometry must remain inside its responsive branch."));
                if (node.Slot is { } slot)
                {
                    if (!slots.TryAdd(slot, (current, path)))
                        errors.Add(new($"{path}.slot", "slot_reuse", $"{Name(slot)} may appear only once in a responsive branch."));
                }
                for (var index = 0; index < node.Children.Count; index++)
                    Visit(node.Children[index], current, $"{path}.children[{index}]");
            }
        }
    }

    private static LauncherRegion Compose(LauncherRegion parent, LauncherRegion child, LauncherInsets insets)
    {
        var x = parent.X + parent.Width * child.X;
        var y = parent.Y + parent.Height * child.Y;
        var width = parent.Width * child.Width;
        var height = parent.Height * child.Height;
        x += width * insets.Left;
        y += height * insets.Top;
        width *= 1 - insets.Left - insets.Right;
        height *= 1 - insets.Top - insets.Bottom;
        return new(x, y, width, height);
    }

    private static bool IsValidRegion(LauncherRegion value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Width) && double.IsFinite(value.Height) &&
        value.X >= 0 && value.Y >= 0 && value.Width > 0 && value.Height > 0 &&
        value.X + value.Width <= 1.000001 && value.Y + value.Height <= 1.000001;

    private static bool Overlaps(LauncherRegion first, LauncherRegion second) =>
        first.X < second.X + second.Width && second.X < first.X + first.Width &&
        first.Y < second.Y + second.Height && second.Y < first.Y + first.Height;

    private static string Name(LauncherSlot slot) => slot switch
    {
        LauncherSlot.HeroBackground => "hero-background",
        LauncherSlot.GameRail => "game-rail",
        LauncherSlot.DetailsPanel => "details-panel",
        LauncherSlot.CollectionTabs => "collection-tabs",
        LauncherSlot.SourceStatus => "source-status",
        LauncherSlot.OperationStatus => "operation-status",
        LauncherSlot.SystemStatus => "system-status",
        LauncherSlot.ControllerHints => "controller-hints",
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static string ComputeDigest(string root, IReadOnlyList<string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var relative in files)
        {
            var name = Encoding.UTF8.GetBytes(relative);
            BinaryPrimitives.WriteInt32LittleEndian(length, name.Length);
            hash.AppendData(length);
            hash.AppendData(name);
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            var bytes = LauncherExperienceFileGuard.ReadBounded(path, MaximumAssetBytes);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
