namespace GameBarAlternative.WidgetStyling;

public interface IGbssSourceProvider
{
    bool TryRead(string packageRelativePath, out string source);
}

public sealed class GbssFileSourceProvider : IGbssSourceProvider
{
    private readonly string _root;
    private readonly string _rootPrefix;

    public GbssFileSourceProvider(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        _root = Path.GetFullPath(packageRoot);
        _rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
    }

    public bool TryRead(string packageRelativePath, out string source)
    {
        source = string.Empty;
        try
        {
            if (!GbssPackageLoader.IsSafePackagePath(packageRelativePath)) return false;
            var candidate = Path.GetFullPath(Path.Combine(_root, packageRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(candidate) || new FileInfo(candidate).Length > GbssLimits.MaximumSourceBytes ||
                ContainsReparsePoint(packageRelativePath)) return false;
            source = File.ReadAllText(candidate);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool ContainsReparsePoint(string packageRelativePath)
    {
        var current = _root;
        foreach (var segment in packageRelativePath.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }
}

public static class GbssPackageLoader
{
    public const int MaximumImportDepth = 16;

    public static GbssPackageResult Load(string entryPath, IGbssSourceProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        ArgumentNullException.ThrowIfNull(provider);
        var documents = new List<GbssDocument>();
        var diagnostics = new List<GbssDiagnostic>();
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        if (!IsSafePackagePath(entryPath))
        {
            diagnostics.Add(new GbssDiagnostic(entryPath, 1, 1, GbssDiagnosticSeverity.Error,
                "unsafe_import", "Entry path must be a normalized package-relative .gbss path."));
            return new GbssPackageResult(documents, diagnostics);
        }
        LoadOne(entryPath, 0, null);
        return new GbssPackageResult(documents, diagnostics);

        void LoadOne(string path, int depth, GbssSourceLocation? importLocation)
        {
            if (depth > MaximumImportDepth)
            {
                Add(importLocation ?? new GbssSourceLocation(path, 1, 1), "import_depth", $"Imports may be at most {MaximumImportDepth} levels deep.");
                return;
            }
            if (!active.Add(path))
            {
                Add(importLocation ?? new GbssSourceLocation(path, 1, 1), "import_cycle", $"Import cycle detected at '{path}'.");
                return;
            }
            if (loaded.Contains(path))
            {
                active.Remove(path);
                return;
            }
            if (!provider.TryRead(path, out var source))
            {
                Add(importLocation ?? new GbssSourceLocation(path, 1, 1), "missing_import", $"GBSS source was not found: {path}");
                active.Remove(path);
                return;
            }

            var parsed = GbssParser.Parse(source, path);
            diagnostics.AddRange(parsed.Diagnostics);
            foreach (var import in parsed.Document.Statements.OfType<GbssImport>())
            {
                var resolved = ResolveImport(path, import.Path);
                if (resolved is null)
                    Add(import.Location, "unsafe_import", $"Import escapes its package directory: {import.Path}");
                else
                    LoadOne(resolved, depth + 1, import.Location);
            }
            active.Remove(path);
            loaded.Add(path);
            documents.Add(parsed.Document with
            {
                Statements = parsed.Document.Statements.Where(statement => statement is not GbssImport).ToArray(),
            });
        }

        void Add(GbssSourceLocation location, string code, string message) =>
            diagnostics.Add(new GbssDiagnostic(location.Source, location.Line, location.Column, GbssDiagnosticSeverity.Error, code, message));
    }

    public static GbssPackageResult LoadFile(string entryFile, string? packageRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryFile);
        var fullEntry = Path.GetFullPath(entryFile);
        var root = Path.GetFullPath(packageRoot ?? Path.GetDirectoryName(fullEntry)!);
        var relative = Path.GetRelativePath(root, fullEntry).Replace(Path.DirectorySeparatorChar, '/');
        return Load(relative, new GbssFileSourceProvider(root));
    }

    public static bool IsSafePackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':')) return false;
        if (!Path.GetExtension(path).Equals(".gbss", StringComparison.OrdinalIgnoreCase)) return false;
        return path.Split('/').All(segment => segment.Length != 0 && segment is not "." and not "..");
    }

    private static string? ResolveImport(string importer, string imported)
    {
        if (!IsSafePackagePath(imported)) return null;
        var prefix = importer.Contains('/') ? importer[..(importer.LastIndexOf('/') + 1)] : string.Empty;
        var combined = prefix + imported;
        return IsSafePackagePath(combined) ? combined : null;
    }
}
