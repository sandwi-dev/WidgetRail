using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeClientRegistryScenarios
{
    internal static async Task CatalogIconAuthorityCoversEstablishmentAndDeferredRoots()
    {
        var configured = Widget("foreign-icons", worker: '1', catalog: '2');
        var icon = WidgetIcon.CatalogSvg(new("example.other", "1.0.0", false), "mark", WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Connection);
        var snapshot = new WidgetView(UI.Icon(icon, "foreign.icon", "Other package")).CreateSnapshot(configured.InstanceId, 1);
        BridgeClientRegistry.DemandPackageIconAuthority(configured, Array.Empty<ViewNode>());
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => { BridgeClientRegistry.DemandPackageIconAuthority(configured, new[] { snapshot.Root }); return Task.CompletedTask; });
        await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) => client.SnapshotFactory = sequence => snapshot with { Sequence = sequence });
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.Registry.EstablishPresentationAsync(configured.Id,
            WidgetLifecycleState.Visible, WidgetPresentationTransactionKind.OrdinaryCheckpoint,
            PresentationUpdateCapabilities.None, 0, 0, default, default));
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.GetSnapshotAsync(configured.Id));
    }
}
