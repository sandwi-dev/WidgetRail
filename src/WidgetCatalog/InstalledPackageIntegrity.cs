using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.WidgetProtocol;

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
    internal const int MaximumMetadataBytes = 4 * 1024;
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
        var digest = Compute(
            catalogRoot, packageRoot, options,
            capturedManifestBytes: null, gbssDigests: null,
            entryCount: out _, totalBytes: out _);
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

    internal static InstalledPackageVerification Verify(
        string catalogRoot,
        string packageRoot,
        WidgetCatalogOptions options)
    {
        var manifestBytes = ReadManifest(catalogRoot, packageRoot, options);
        WidgetManifest manifest;
        try
        {
            manifest = ManifestJson.Deserialize(manifestBytes);
        }
        catch (JsonException exception)
        {
            throw new WidgetPackageException(
                "invalid_manifest",
                $"Installed manifest is invalid: {Path.Combine(packageRoot, "manifest.json")}",
                exception);
        }

        var path = Path.Combine(packageRoot, MetadataFileName);
        if (!File.Exists(path))
            throw new WidgetPackageException(
                "integrity_metadata_missing",
                $"Installed widget is missing host integrity metadata: {packageRoot}");
        FileSystemSafety.EnsureNoReparsePoints(catalogRoot, path);

        IntegrityDocument document;
        try
        {
            using var input = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                MaximumMetadataBytes, FileOptions.SequentialScan);
            var bytes = BoundedFileReader.ReadAll(input, MaximumMetadataBytes);
            if (bytes.Length == 0)
                throw new JsonException("Integrity metadata size is invalid.");
            document = JsonSerializer.Deserialize<IntegrityDocument>(
                bytes, JsonOptions)
                ?? throw new JsonException("Integrity metadata was null.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
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

        var gbssDigests = new Dictionary<string, string>(StringComparer.Ordinal);
        var actualText = Compute(
            catalogRoot, packageRoot, options, manifestBytes, gbssDigests,
            out var entryCount, out var totalBytes);
        _ = TryDecodeDigest(actualText, out var actual);
        var matches = CryptographicOperations.FixedTimeEquals(expected, actual);
        CryptographicOperations.ZeroMemory(expected);
        CryptographicOperations.ZeroMemory(actual);
        if (!matches)
            throw new WidgetPackageException(
                "package_tampered",
                $"Installed widget content no longer matches its sealed package digest: {packageRoot}");
        return new InstalledPackageVerification(
            actualText,
            manifest,
            gbssDigests.ToFrozenDictionary(StringComparer.Ordinal),
            checked(entryCount + 1),
            checked(totalBytes + MaximumMetadataBytes));
    }

    private static string Compute(
        string catalogRoot,
        string packageRoot,
        WidgetCatalogOptions options,
        byte[]? capturedManifestBytes,
        IDictionary<string, string>? gbssDigests,
        out int entryCount,
        out long totalBytes)
    {
        FileSystemSafety.EnsureTreeContainsNoReparsePoints(
            catalogRoot, packageRoot, options.MaximumInstalledEntries);
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
            .Take(checked(options.MaximumArchiveEntries + 1))
            .ToArray();
        if (files.Length < 2 || files.Length > options.MaximumArchiveEntries)
            throw new WidgetPackageException(
                "integrity_limit", "Installed widget file count is outside package limits.");
        files = files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        entryCount = files.Length;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(StrictUtf8.GetBytes(Algorithm));
        Span<byte> integer = stackalloc byte[sizeof(long)];
        var buffer = new byte[64 * 1024];
        totalBytes = 0;
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

                var useCapturedManifest = capturedManifestBytes is not null && string.Equals(
                    file.RelativePath, "manifest.json", StringComparison.Ordinal);
                using var input = useCapturedManifest
                    ? null
                    : new FileStream(
                        file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        buffer.Length, FileOptions.SequentialScan);
                var expectedLength = useCapturedManifest
                    ? capturedManifestBytes!.LongLength
                    : input!.Length;
                if (expectedLength < 0 || expectedLength > options.MaximumEntryBytes)
                    throw new WidgetPackageException(
                        "integrity_limit", "Installed widget file exceeds package limits.");
                totalBytes = checked(totalBytes + expectedLength);
                if (totalBytes > options.MaximumTotalBytes)
                    throw new WidgetPackageException(
                        "integrity_limit", "Installed widget exceeds package limits.");
                BinaryPrimitives.WriteInt64BigEndian(integer, expectedLength);
                hash.AppendData(integer);
                if (useCapturedManifest)
                {
                    hash.AppendData(capturedManifestBytes!);
                    continue;
                }

                var trackGbssDigest = gbssDigests is not null &&
                    Path.GetExtension(file.RelativePath).Equals(
                        ".gbss", StringComparison.OrdinalIgnoreCase);
                using var fileHash = !trackGbssDigest
                    ? null
                    : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                try
                {
                    BoundedFileReader.AppendExact(
                        hash, input!, expectedLength, buffer, fileHash);
                }
                catch (InvalidDataException exception)
                {
                    throw new WidgetPackageException(
                        "package_tampered",
                        "Installed widget content changed while its digest was computed.",
                        exception);
                }
                if (fileHash is not null)
                    gbssDigests!.Add(
                        file.RelativePath,
                        Convert.ToHexString(fileHash.GetHashAndReset()).ToLowerInvariant());
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

    private static byte[] ReadManifest(
        string catalogRoot,
        string packageRoot,
        WidgetCatalogOptions options)
    {
        var path = Path.Combine(packageRoot, "manifest.json");
        if (!File.Exists(path))
            throw new WidgetPackageException(
                "missing_manifest", $"Installed widget is missing manifest.json: {packageRoot}");
        FileSystemSafety.EnsureNoReparsePoints(catalogRoot, path);
        try
        {
            using var input = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            return BoundedFileReader.ReadExact(
                input,
                input.Length,
                (int)Math.Min(options.MaximumEntryBytes, 1024L * 1024));
        }
        catch (InvalidDataException exception)
        {
            throw new WidgetPackageException(
                "invalid_manifest", $"Installed manifest is invalid: {path}", exception);
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

internal sealed record InstalledPackageVerification(
    string ContentDigest,
    WidgetManifest Manifest,
    IReadOnlyDictionary<string, string> GbssDigests,
    int EntryCount,
    long TotalBytes);
