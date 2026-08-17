using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.WidgetStyling;

public interface IWrssSourceProvider
{
    WrssSourceReadResult Read(string packageRelativePath);
}

public sealed class WrssFileSourceProvider : IWrssSourceProvider
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly IReadOnlyDictionary<string, string>? _expectedContentDigests;

    public WrssFileSourceProvider(
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

    public WrssSourceReadResult Read(string packageRelativePath)
    {
        try
        {
            if (!WrssPackageLoader.IsSafePackagePath(packageRelativePath))
                return WrssSourceReadResult.Failure(WrssSourceReadStatus.UnsafePath);
            var candidate = Path.GetFullPath(Path.Combine(_root, packageRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                ContainsReparsePoint(packageRelativePath))
                return WrssSourceReadResult.Failure(WrssSourceReadStatus.UnsafePath);
            if (!File.Exists(candidate))
                return WrssSourceReadResult.Failure(WrssSourceReadStatus.Missing);
            using var input = new FileStream(
                candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            var bytes = ReadBounded(input);
            if (!MatchesExpectedDigest(packageRelativePath, bytes))
                return WrssSourceReadResult.Failure(WrssSourceReadStatus.DigestMismatch);
            var offset = bytes.Length >= 3 &&
                         bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf
                ? 3
                : 0;
            return WrssSourceReadResult.FromSource(
                StrictUtf8.GetString(bytes, offset, bytes.Length - offset));
        }
        catch (WrssSourceReadException exception)
        {
            return WrssSourceReadResult.Failure(exception.Status);
        }
        catch (DecoderFallbackException)
        {
            return WrssSourceReadResult.Failure(WrssSourceReadStatus.InvalidEncoding);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return WrssSourceReadResult.Failure(WrssSourceReadStatus.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WrssSourceReadResult.Failure(WrssSourceReadStatus.IoUnavailable);
        }
    }

    internal static byte[] ReadBounded(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("Input stream is not readable.", nameof(input));
        var expectedLength = input.CanSeek ? input.Length : (long?)null;
        if (expectedLength is < 0 || expectedLength > WrssLimits.MaximumSourceBytes)
            throw new WrssSourceReadException(WrssSourceReadStatus.TooLarge);

        using var output = new MemoryStream(expectedLength is > 0 ? (int)expectedLength.Value : 0);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var remainingWithSentinel = WrssLimits.MaximumSourceBytes - total + 1;
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remainingWithSentinel));
            if (read == 0) break;
            total += read;
            if (total > WrssLimits.MaximumSourceBytes)
                throw new WrssSourceReadException(WrssSourceReadStatus.TooLarge);
            output.Write(buffer, 0, read);
        }
        if (expectedLength is { } expected &&
            (total != expected || (input.CanSeek && input.Length != expected)))
            throw new WrssSourceReadException(WrssSourceReadStatus.ChangedDuringRead);
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

public static class WrssPackageLoader
{
    public const int MaximumImportDepth = 16;

    public static WrssPackageResult Load(string entryPath, IWrssSourceProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        ArgumentNullException.ThrowIfNull(provider);
        var documents = new List<WrssDocument>();
        var diagnostics = new List<WrssDiagnostic>();
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        if (!IsSafePackagePath(entryPath))
        {
            diagnostics.Add(new WrssDiagnostic(entryPath, 1, 1, WrssDiagnosticSeverity.Error,
                "unsafe_import", "Entry path must be a normalized package-relative .wrss path."));
            return new WrssPackageResult(documents, diagnostics);
        }
        LoadOne(entryPath, 0, null);
        return new WrssPackageResult(documents, diagnostics);

        void LoadOne(string path, int depth, WrssSourceLocation? importLocation)
        {
            if (depth > MaximumImportDepth)
            {
                Add(importLocation ?? new WrssSourceLocation(path, 1, 1), "import_depth", $"Imports may be at most {MaximumImportDepth} levels deep.");
                return;
            }
            if (!active.Add(path))
            {
                Add(importLocation ?? new WrssSourceLocation(path, 1, 1), "import_cycle", $"Import cycle detected at '{path}'.");
                return;
            }
            if (loaded.Contains(path))
            {
                active.Remove(path);
                return;
            }
            WrssSourceReadResult read;
            try
            {
                read = provider.Read(path);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException and
                not OutOfMemoryException and
                not StackOverflowException and
                not AccessViolationException)
            {
                AddSourceFailure(
                    importLocation ?? new WrssSourceLocation(path, 1, 1),
                    WrssSourceReadStatus.IoUnavailable);
                active.Remove(path);
                return;
            }
            if (read.Status != WrssSourceReadStatus.Success || read.Source is null)
            {
                AddSourceFailure(
                    importLocation ?? new WrssSourceLocation(path, 1, 1),
                    read.Status);
                active.Remove(path);
                return;
            }

            var parsed = WrssParser.Parse(read.Source, path);
            diagnostics.AddRange(parsed.Diagnostics);
            foreach (var import in parsed.Document.Statements.OfType<WrssImport>())
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
                Statements = parsed.Document.Statements.Where(statement => statement is not WrssImport).ToArray(),
            });
        }

        void Add(WrssSourceLocation location, string code, string message) =>
            diagnostics.Add(new WrssDiagnostic(location.Source, location.Line, location.Column, WrssDiagnosticSeverity.Error, code, message));

        void AddSourceFailure(WrssSourceLocation location, WrssSourceReadStatus status)
        {
            var (code, message) = status switch
            {
                WrssSourceReadStatus.Missing =>
                    ("missing_import", "WRSS source was not found."),
                WrssSourceReadStatus.UnsafePath =>
                    ("unsafe_import", "WRSS source path is unsafe."),
                WrssSourceReadStatus.TooLarge =>
                    ("source_too_large", "WRSS source exceeds its byte limit."),
                WrssSourceReadStatus.ChangedDuringRead =>
                    ("source_changed", "WRSS source changed while it was read."),
                WrssSourceReadStatus.InvalidEncoding =>
                    ("invalid_encoding", "WRSS source is not valid UTF-8."),
                WrssSourceReadStatus.DigestMismatch =>
                    ("digest_mismatch", "WRSS source does not match the verified package digest."),
                _ => ("source_unavailable", "WRSS source could not be read."),
            };
            Add(location, code, message);
        }
    }

    public static WrssPackageResult LoadFile(string entryFile, string? packageRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryFile);
        var fullEntry = Path.GetFullPath(entryFile);
        var root = Path.GetFullPath(packageRoot ?? Path.GetDirectoryName(fullEntry)!);
        var relative = Path.GetRelativePath(root, fullEntry).Replace(Path.DirectorySeparatorChar, '/');
        return Load(relative, new WrssFileSourceProvider(root));
    }

    public static bool IsSafePackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':')) return false;
        if (!Path.GetExtension(path).Equals(".wrss", StringComparison.OrdinalIgnoreCase)) return false;
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

internal sealed class WrssSourceReadException(WrssSourceReadStatus status) : Exception
{
    internal WrssSourceReadStatus Status { get; } = status;
}
