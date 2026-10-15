using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private BridgeCatalogIcons? catalogIcons;

    private async Task ResolveCatalogIconAsync(BridgePackageIconRequest request, long requestId, CancellationToken token)
    {
        var (catalog, revision) = _registry.CatalogSnapshot();
        var owner = catalog.GetConfigured(request.WidgetId);
        if ((!IsTrustedSettings(owner) && !IsTrustedMarketplace(owner)) ||
            !string.Equals(owner.WorkerFingerprint[..32], request.RuntimeGeneration, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(owner.CatalogFingerprint[..32], request.PresentationGeneration, StringComparison.OrdinalIgnoreCase))
            throw new BridgeProtocolException("Catalog icon management authority is unavailable.");
        var resolver = LazyInitializer.EnsureInitialized(ref catalogIcons, () => new BridgeCatalogIcons(_catalogMonitor));
        var icon = await resolver.ResolveAsync(catalog, revision, request.Catalog!, request.AssetId, token).ConfigureAwait(false);
        var (latest, latestRevision) = _registry.CatalogSnapshot();
        if (revision != latestRevision || !ReferenceEquals(catalog, latest))
            throw new BridgeStalePackageIconAuthorityException("Catalog changed during icon resolution.");
        await ReplyAsync(BridgeMessageTypes.PackageIcon, requestId, new
        {
            widgetId = request.WidgetId,
            runtimeGeneration = request.RuntimeGeneration,
            presentationGeneration = request.PresentationGeneration,
            catalog = request.Catalog,
            assetId = icon.Metadata.AssetId,
            sourceSha256 = icon.Metadata.SourceSha256,
            normalizedSha256 = icon.Metadata.NormalizedSha256,
            normalizedBytes = icon.Metadata.NormalizedBytes,
            normalizedSvgBase64 = Convert.ToBase64String(icon.NormalizedSvg),
        }, token).ConfigureAwait(false);
    }
}
