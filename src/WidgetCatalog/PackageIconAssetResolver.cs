using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetCatalog;

internal sealed record ResolvedPackageIconMetadata(
    string AssetId,
    string RelativePath,
    string SourceSha256,
    string NormalizedSha256,
    int SourceBytes,
    int NormalizedBytes);

internal sealed record ResolvedPackageIconPayload(
    ResolvedPackageIconMetadata Metadata,
    byte[] NormalizedSvg);

internal sealed record PackageIconMetadataLoadResult(
    IReadOnlyDictionary<string, ResolvedPackageIconMetadata> Available,
    IReadOnlyDictionary<string, string> Unavailable);

internal sealed record PackageIconMetadataPreflightEntry(
    string AssetId,
    WidgetPackageIconAsset Asset,
    VerifiedPackageFile Verified);

internal sealed record PackageIconMetadataPreflight(
    IReadOnlyList<PackageIconMetadataPreflightEntry> Readable,
    IReadOnlyDictionary<string, WidgetPackageException> Unavailable,
    WidgetPackageException? AggregateFailure);

internal static class PackageIconAssetResolver
{
    internal static IReadOnlyDictionary<string, ResolvedPackageIconMetadata> LoadMetadata(
        string packageRoot,
        WidgetManifest manifest,
        IReadOnlyDictionary<string, VerifiedPackageFile> verifiedFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(verifiedFiles);
        var preflight = Preflight(manifest, verifiedFiles);
        if (preflight.AggregateFailure is not null)
            throw preflight.AggregateFailure;
        if (preflight.Unavailable.Values.FirstOrDefault() is { } unavailable)
            throw unavailable;
        var result = new Dictionary<string, ResolvedPackageIconMetadata>(StringComparer.Ordinal);
        foreach (var entry in preflight.Readable)
        {
            var metadata = LoadOne(packageRoot, entry);
            result.Add(entry.AssetId, metadata);
        }
        return result;
    }

    internal static PackageIconMetadataLoadResult LoadAvailableMetadata(
        string packageRoot,
        WidgetManifest manifest,
        IReadOnlyDictionary<string, VerifiedPackageFile> verifiedFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(verifiedFiles);
        var assets = manifest.IconAssets ??
            throw new WidgetPackageException("invalid_manifest", "Icon assets cannot be null.");
        var preflight = Preflight(manifest, verifiedFiles);
        if (preflight.AggregateFailure is not null)
        {
            var allUnavailable = assets.Keys.ToDictionary(
                assetId => assetId,
                _ => preflight.AggregateFailure.Code,
                StringComparer.Ordinal);
            return new(
                new Dictionary<string, ResolvedPackageIconMetadata>(StringComparer.Ordinal),
                allUnavailable);
        }
        var available = new Dictionary<string, ResolvedPackageIconMetadata>(StringComparer.Ordinal);
        var unavailable = preflight.Unavailable.ToDictionary(
            pair => pair.Key, pair => pair.Value.Code, StringComparer.Ordinal);
        foreach (var entry in preflight.Readable)
        {
            try
            {
                var metadata = LoadOne(packageRoot, entry);
                available.Add(entry.AssetId, metadata);
            }
            catch (Exception exception) when (exception is WidgetPackageException or
                                               IOException or UnauthorizedAccessException)
            {
                unavailable[entry.AssetId] = exception is WidgetPackageException package
                    ? package.Code
                    : "icon_asset_unavailable";
            }
        }
        return new(available, unavailable);
    }

    private static PackageIconMetadataPreflight Preflight(
        WidgetManifest manifest,
        IReadOnlyDictionary<string, VerifiedPackageFile> verifiedFiles)
    {
        var assets = manifest.IconAssets ??
            throw new WidgetPackageException("invalid_manifest", "Icon assets cannot be null.");
        var readable = new List<PackageIconMetadataPreflightEntry>();
        var unavailable = new Dictionary<string, WidgetPackageException>(StringComparer.Ordinal);
        long total = 0;
        var aggregateOverflow = false;
        foreach (var pair in assets.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!verifiedFiles.TryGetValue(pair.Value.Path, out var verified) ||
                !string.Equals(verified.RelativePath, pair.Value.Path, StringComparison.Ordinal))
            {
                unavailable[pair.Key] = new WidgetPackageException(
                    "missing_icon_asset",
                    $"Icon asset '{pair.Key}' is missing or has different casing: {pair.Value.Path}");
                continue;
            }
            if (verified.Length is < 1 or > ProtocolConstants.MaximumPackageIconBytes)
            {
                unavailable[pair.Key] = new WidgetPackageException(
                    "icon_asset_too_large",
                    $"Icon asset '{pair.Key}' exceeds {ProtocolConstants.MaximumPackageIconBytes} bytes.");
                continue;
            }
            try { total = checked(total + verified.Length); }
            catch (OverflowException) { aggregateOverflow = true; }
            readable.Add(new(pair.Key, pair.Value, verified));
        }
        WidgetPackageException? aggregateFailure = null;
        if (aggregateOverflow || total > ProtocolConstants.MaximumPackageIconAggregateBytes)
            aggregateFailure = new WidgetPackageException(
                "icon_assets_too_large",
                $"Declared icon assets exceed {ProtocolConstants.MaximumPackageIconAggregateBytes} bytes.");
        return new(readable, unavailable, aggregateFailure);
    }

    private static ResolvedPackageIconMetadata LoadOne(
        string packageRoot,
        PackageIconMetadataPreflightEntry entry)
    {
        var source = ReadExact(packageRoot, entry.Verified);
        var normalized = SvgIconNormalizer.Normalize(source);
        if (!string.Equals(
                normalized.SourceSha256, entry.Verified.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new WidgetPackageException(
                "package_tampered",
                $"Icon asset '{entry.AssetId}' changed after package verification.");
        return new(
            entry.AssetId,
            entry.Asset.Path,
            normalized.SourceSha256,
            normalized.NormalizedSha256,
            source.Length,
            normalized.Bytes.Length);
    }

    internal static ResolvedPackageIconPayload Resolve(
        string packageRoot,
        ResolvedPackageIconMetadata metadata,
        IReadOnlyDictionary<string, VerifiedPackageFile> verifiedFiles)
    {
        if (!verifiedFiles.TryGetValue(metadata.RelativePath, out var verified) ||
            !string.Equals(verified.Sha256, metadata.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new WidgetPackageException("package_icon_retired", "Package icon authority is no longer current.");
        var source = ReadExact(packageRoot, verified);
        var normalized = SvgIconNormalizer.Normalize(source);
        if (!string.Equals(normalized.SourceSha256, metadata.SourceSha256, StringComparison.Ordinal) ||
            !string.Equals(normalized.NormalizedSha256, metadata.NormalizedSha256, StringComparison.Ordinal) ||
            normalized.Bytes.Length != metadata.NormalizedBytes)
            throw new WidgetPackageException("package_icon_retired", "Package icon content changed after admission.");
        return new(metadata, normalized.Bytes);
    }

    private static byte[] ReadExact(string packageRoot, VerifiedPackageFile verified)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var path = Path.GetFullPath(Path.Combine(
            root, verified.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!FileSystemSafety.IsWithin(root, path) || !File.Exists(path))
            throw new WidgetPackageException("missing_icon_asset", "Declared icon asset is unavailable.");
        FileSystemSafety.EnsureNoReparsePoints(root, path);
        if (verified.Length is < 1 or > ProtocolConstants.MaximumPackageIconBytes)
            throw new WidgetPackageException(
                "package_tampered", "Declared icon asset failed integrity verification.");
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4_096, FileOptions.SequentialScan);
        if (stream.Length != verified.Length)
            throw new WidgetPackageException(
                "package_tampered", "Declared icon asset failed integrity verification.");
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)verified.Length));
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes, read, bytes.Length - read);
            if (count == 0)
                throw new WidgetPackageException(
                    "package_tampered", "Declared icon asset failed integrity verification.");
            read += count;
        }
        if (stream.ReadByte() != -1 ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(bytes), Convert.FromHexString(verified.Sha256)))
            throw new WidgetPackageException("package_tampered", "Declared icon asset failed integrity verification.");
        return bytes;
    }
}
