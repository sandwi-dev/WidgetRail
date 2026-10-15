using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

/// <summary>Read-only management inventory. Never creates a worker registration.</summary>
internal sealed class BridgeCatalogIcons(BridgeCatalogMonitor? monitor)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long revision = -1;
    private BridgeCatalog? trusted;
    private WidgetCatalogSnapshot? installed;

    internal async Task<ResolvedPackageIconPayload> ResolveAsync(BridgeCatalog current, long currentRevision,
        WidgetCatalogIconSource source, string assetId, CancellationToken token)
    {
        if (!WidgetCatalogIconValidation.IsValid(source) || !WidgetManifestValidator.IsPackageIconAssetId(assetId))
            throw new BridgeProtocolException("Invalid catalog icon identity.");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (revision != currentRevision)
            {
                trusted = null;
                installed = null;
                revision = currentRevision;
            }
            string root;
            string digest;
            string version;
            WidgetPackageIcon? declared;
            IReadOnlyDictionary<string, VerifiedPackageFile> files;
            ResolvedPackageIconMetadata metadata;
            if (source.BuiltIn)
            {
                trusted ??= monitor?.LoadTrustedForManagement() ?? current;
                var target = trusted.GetConfiguredPackage(source.PackageId);
                root = target.PackageRoot ?? throw Unavailable();
                digest = target.PackageContentDigest;
                version = target.PackageVersion;
                declared = target.PackageIcon;
                files = target.VerifiedPackageFiles;
                if (!target.PackageIconAssets.TryGetValue(assetId, out metadata!)) throw Unavailable();
            }
            else
            {
                if (monitor is null) throw Unavailable();
                installed ??= await new WidgetRail.WidgetCatalog.WidgetCatalog(monitor.InstalledCatalogRoot).DiscoverAsync(token).ConfigureAwait(false);
                var target = installed.Widgets.SingleOrDefault(widget => widget.Id == source.PackageId)?.ActiveVersion ?? throw Unavailable();
                root = target.InstallPath;
                digest = target.ContentDigest;
                version = target.Manifest.Version;
                declared = target.Manifest.Presentation.PackageIcon;
                files = target.VerifiedFiles;
                var icons = PackageIconAssetResolver.LoadAvailableMetadata(root, target.Manifest, files);
                if (!icons.Available.TryGetValue(assetId, out metadata!)) throw Unavailable();
            }
            // Catalog views may display the package's declared presentation icon,
            // not use management authority to read arbitrary manifest assets.
            if (version != source.Version || declared?.AssetId != assetId ||
                (source.ContentDigest is not null && source.ContentDigest != digest)) throw Unavailable();
            token.ThrowIfCancellationRequested();
            return PackageIconAssetResolver.Resolve(root, metadata, files);
        }
        catch (Exception error) when (error is WidgetPackageException or BridgeCatalogException or BridgeProtocolException or IOException or UnauthorizedAccessException)
        {
            // A missing/corrupt optional icon never becomes a transport failure
            // or starts a worker to repair it. The view retains its glyph.
            throw Unavailable();
        }
        finally { gate.Release(); }
    }

    private static BridgeStalePackageIconAuthorityException Unavailable() => new("Catalog icon is stale or unavailable.");
}
