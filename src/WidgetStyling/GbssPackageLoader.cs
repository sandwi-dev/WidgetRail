using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.WidgetStyling;

public interface IGbssSourceProvider
{
    bool TryRead(string packageRelativePath, out string source);
}

public sealed class GbssFileSourceProvider : IGbssSourceProvider
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly IReadOnlyDictionary<string, string>? _expectedContentDigests;

    public GbssFileSourceProvider(
        string packageRoot,
        IReadOnlyDictionary<string, string>? expectedContentDigests = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        _root = Path.GetFullPath(packageRoot);
        _rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        _expectedContentDigests = expectedContentDigests is null
            ? null
            : new Dictionary<string, string>(expectedContentDigests, StringComparer.Ordinal);
    }

    public bool TryRead(string packageRelativePath, out string source)
    {
        source = string.Empty;
        try
        {
            if (!GbssPackageLoader.IsSafePackagePath(packageRelativePath)) return false;
            var candidate = Path.GetFullPath(Path.Combine(_root, packageRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(candidate) ||
                ContainsReparsePoint(packageRelativePath)) return false;
            using var input = new FileStream(
                candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            var bytes = ReadBounded(input);
            if (!MatchesExpectedDigest(packageRelativePath, bytes)) return false;
            var offset = bytes.Length >= 3 &&
                         bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf
                ? 3
                : 0;
            source = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           DecoderFallbackException or InvalidDataException)
        {
            return false;
        }
    }

    internal static byte[] ReadBounded(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input stream is not readable.", nameof(input));
        var expectedLength = input.CanSeek ? input.Length : (long?)null;
        if (expectedLength is < 0 || expectedLength > GbssLimits.MaximumSourceBytes)
            throw new InvalidDataException("GBSS source exceeds its byte limit.");

        using var output = new MemoryStream(expectedLength is > 0 ? (int)expectedLength.Value : 0);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var remainingWithSentinel = GbssLimits.MaximumSourceBytes - total + 1;
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remainingWithSentinel));
            if (read == 0) break;
            total += read;
            if (total > GbssLimits.MaximumSourceBytes)
                throw new InvalidDataException("GBSS source exceeds its byte limit.");
            output.Write(buffer, 0, read);
        }
        if (expectedLength is { } expected &&
            (total != expected || (input.CanSeek && input.Length != expected)))
            throw new InvalidDataException("GBSS source length changed while it was read.");
        return output.ToArray();
    }

    private bool MatchesExpectedDigest(string packageRelativePath, byte[] bytes)
    {
        if (_expectedContentDigests is null) return true;
        if (!_expectedContentDigests.TryGetValue(packageRelativePath, out var expectedText) ||
            expectedText.Length != 64)
            return false;
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedText);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = SHA256.HashData(bytes);
        try
        {
            return expected.Length == actual.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
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
