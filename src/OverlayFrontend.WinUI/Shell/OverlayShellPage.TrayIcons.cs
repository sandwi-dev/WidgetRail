using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private void TrayItemContentLoaded(object sender, RoutedEventArgs args)
    {
        var host = (ContentControl)sender;
        if (host.Content is not WidgetCatalogItemContent)
            host.Content = new WidgetCatalogItemContent(ResolveTrayIconAsync) { ShowLabel = false };
        ((WidgetCatalogItemContent)host.Content).IconSize = RailIconSize;
        ((WidgetCatalogItemContent)host.Content).TileSize = railGeometry.TileSize > 0 ? railGeometry.TileSize : 64;
        ((WidgetCatalogItemContent)host.Content).SetItem(host.DataContext as BridgeWidgetDescriptor);
    }

    private async Task<WidgetPresentationPackageIcon> ResolveTrayIconAsync(BridgeWidgetDescriptor expected,
        string assetId, CancellationToken token)
    {
        if (retired || owner is null) throw new OperationCanceledException(token);
        using var demand = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var session = owner.Session;
        var target = session.GetTarget(expected.Id);
        EnsureIconAuthority(expected, target.Descriptor, assetId);
        try { return await session.ResolvePackageIconAsync(target, assetId, demand.Token); }
        catch (WidgetPresentationSessionException error) when (error.Code == "catalog_stale")
        {
            var latest = session.GetTarget(expected.Id);
            EnsureIconAuthority(expected, latest.Descriptor, assetId);
            return await session.ResolvePackageIconAsync(latest, assetId, demand.Token);
        }
    }

    private static void EnsureIconAuthority(BridgeWidgetDescriptor expected, BridgeWidgetDescriptor current, string assetId)
    {
        if (expected.Id != current.Id || expected.InstanceId != current.InstanceId ||
            expected.RuntimeGeneration != current.RuntimeGeneration || expected.PresentationGeneration != current.PresentationGeneration ||
            expected.PackageContentDigest != current.PackageContentDigest || expected.PackageIcon != current.PackageIcon ||
            expected.IconAssets.SingleOrDefault(asset => asset.AssetId == assetId) != current.IconAssets.SingleOrDefault(asset => asset.AssetId == assetId))
            throw new WidgetPresentationSessionException("package_icon_stale", "The tray icon catalog entry is no longer current.");
    }
}
