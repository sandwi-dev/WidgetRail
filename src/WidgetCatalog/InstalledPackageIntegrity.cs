using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetCatalog;

/// <summary>
/// Host-owned integrity metadata for an extracted unsigned widget package.
/// The metadata file is never accepted from a package and is excluded from
/// the content-tree digest it records.
/// </summary>
internal static class InstalledPackageIntegrity
{
    internal const string MetadataFileName = ".gbar-integrity.json";
    internal const string Algorithm = "sha256-content-tree-v1";
    private const int MaximumMetadataBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    internal static string Seal(
        string catalogRoot,
        string packageRoot,
        WidgetCatalogOptions options)
    {
        var digest = Compute(catalogRoot, packageRoot, options);
        var path = Path.Combine(packageRoot, MetadataFileName);
        if (File.Exists(path) || Directory.Exists(path))
            throw new WidgetPackageException(
                "reserved_path", "Package contains a host-reserved integrity path.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new IntegrityDocument(1, Algorithm, digest), JsonOptions);
        using var output = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4 * 1024, FileOptions.WriteThrough);
        output.Write(payload);
        output.Flush(flushToDisk: true);
        return digest;
    }

    internal static string Verify(
        string catalogRoot,
        string packageRoot,
        WidgetCatalogOptions options)
    {
        var path = Path.Combine(packageRoot, MetadataFileName);
        if (!File.Exists(path))
            throw new WidgetPackageException(
                "integrity_metadata_missing",
                $"Installed widget is missing host integrity metadata: {packageRoot}");
        FileSystemSafety.EnsureNoReparsePoints(catalogRoot, path);

        IntegrityDocument document;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is < 1 or > MaximumMetadataBytes)
                throw new JsonException("Integrity metadata size is invalid.");
            document = JsonSerializer.Deserialize<IntegrityDocument>(
                File.ReadAllBytes(path), JsonOptions)
                ?? throw new JsonException("Integrity metadata was null.");
        }
        catch (JsonException exception)
        {
            throw new WidgetPackageException(
                "invalid_integrity_metadata",
                $"Installed widget integrity metadata is invalid: {packageRoot}", exception);
        }

        if (document.SchemaVersion != 1 ||
            !string.Equals(document.Algorithm, Algorithm, StringComparison.Ordinal) ||
            !TryDecodeDigest(document.ContentDigest, out var expected))
            throw new WidgetPackageException(
                "invalid_integrity_metadata",
                $"Installed widget integrity metadata is invalid: {packageRoot}");

        var actualText = Compute(catalogRoot, packageRoot, options);
        _ = TryDecodeDigest(actualText, out var actual);
        var matches = CryptographicOperations.FixedTimeEquals(expected, actual);
        CryptographicOperations.ZeroMemory(expected);
        CryptographicOperations.ZeroMemory(actual);
        if (!matches)
            throw new WidgetPackageException(
                "package_tampered",
                $"Installed widget content no longer matches its immutable package digest: {packageRoot}");
        return actualText;
    }

    private static string Compute(
        string catalogRoot,
        string packageRoot,
        WidgetCatalogOptions options)
    {
        FileSystemSafety.EnsureTreeContainsNoReparsePoints(catalogRoot, packageRoot);
        var files = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetRelativePath(packageRoot, path),
                MetadataFileName,
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                FullPath = path,
                RelativePath = Path.GetRelativePath(packageRoot, path)
                    .Replace(Path.DirectorySeparatorChar, '/'),
            })
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (files.Length < 2 || files.Length > options.MaximumArchiveEntries)
            throw new WidgetPackageException(
                "integrity_limit", "Installed widget file count is outside package limits.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(StrictUtf8.GetBytes(Algorithm));
        Span<byte> integer = stackalloc byte[sizeof(long)];
        var buffer = new byte[64 * 1024];
        long totalBytes = 0;
        try
        {
            foreach (var file in files)
            {
                if (file.RelativePath.Length is < 1 ||
                    file.RelativePath.Length > options.MaximumPathLength ||
                    file.RelativePath != file.RelativePath.Normalize(NormalizationForm.FormC))
                    throw new WidgetPackageException(
                        "invalid_path", "Installed widget contains an invalid content path.");
                var pathBytes = StrictUtf8.GetBytes(file.RelativePath);
                BinaryPrimitives.WriteInt64BigEndian(integer, pathBytes.Length);
                hash.AppendData(integer);
                hash.AppendData(pathBytes);

                using var input = new FileStream(
                    file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    buffer.Length, FileOptions.SequentialScan);
                if (input.Length < 0 || input.Length > options.MaximumEntryBytes)
                    throw new WidgetPackageException(
                        "integrity_limit", "Installed widget file exceeds package limits.");
                totalBytes = checked(totalBytes + input.Length);
                if (totalBytes > options.MaximumTotalBytes)
                    throw new WidgetPackageException(
                        "integrity_limit", "Installed widget exceeds package limits.");
                BinaryPrimitives.WriteInt64BigEndian(integer, input.Length);
                hash.AppendData(integer);
                while (true)
                {
                    var read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    hash.AppendData(buffer, 0, read);
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch (OverflowException exception)
        {
            throw new WidgetPackageException(
                "integrity_limit", "Installed widget size overflowed package limits.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static bool TryDecodeDigest(string? value, out byte[] digest)
    {
        digest = [];
        if (value is null || value.Length != 64) return false;
        try
        {
            digest = Convert.FromHexString(value);
            return digest.Length == 32;
        }
        catch (FormatException)
        {
            digest = [];
            return false;
        }
    }

    private sealed record IntegrityDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string Algorithm,
        [property: JsonRequired] string ContentDigest);
}
