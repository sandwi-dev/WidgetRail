using WidgetRail.WidgetBridge;
using WidgetRail.WidgetSdk;

internal static partial class BridgeClientRegistryScenarios
{
    internal static async Task SettingsUpdateRetiresAndBlocksWorkers()
    {
        var configured = Widget("settings-update", worker: '6', catalog: '6');
        await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) => client.BlockDispose = true);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var first = fixture.Clients.Single();
        var updating = fixture.Registry.AcquirePackageUpdateAsync(configured.Id, default);
        await first.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var navigation = fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        RegistryAssert.True(!updating.IsCompleted && !navigation.IsCompleted, "Update raced the old worker or admitted navigation.");
        await RegistryAssert.ThrowsAsync<BridgeProtocolException>(() => fixture.RestartAsync(configured.Id));
        first.ReleaseDispose();
        using (await updating.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            RegistryAssert.True(!navigation.IsCompleted, "Worker restarted before settings transfer completed.");
            fixture.Registry.ApplyCatalog(Catalog(Widget("settings-update", worker: '7', catalog: '7')), 1);
        }
        await navigation.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(2, fixture.Clients.Count);
        fixture.Clients.Last().ReleaseDispose();
    }

    internal static async Task CancelledSettingsUpdateReleasesAdmission()
    {
        var configured = Widget("settings-cancel", worker: '6', catalog: '6');
        await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) => client.BlockDispose = true);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var first = fixture.Clients.Single();
        using var cancellation = new CancellationTokenSource();
        var updating = fixture.Registry.AcquirePackageUpdateAsync(configured.Id, cancellation.Token);
        await first.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await RegistryAssert.ThrowsAsync<OperationCanceledException>(() => updating);
        first.ReleaseDispose();
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible).WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(2, fixture.Clients.Count);
        fixture.Clients.Last().ReleaseDispose();
    }

    internal static async Task StopDuringRestartNeverPublishesFreshWorker()
    {
        var configured = Widget("stop-restart", worker: '6', catalog: '6');
        await using var fixture = new RegistryFixture(Catalog(configured), configure: (_, client) => client.BlockDispose = true);
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var first = fixture.Clients.Single();
        var restarting = fixture.Registry.RestartAsync(configured.Id, default);
        await first.DisposeEntered.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Registry.ApplyCatalog(Catalog(configured).WithStoppedWidgets([configured.PackageId]), 1);
        var stopping = fixture.Registry.WaitForWidgetRetirementAsync(configured.Id);
        RegistryAssert.True(!stopping.IsCompleted, "Stop acknowledged before its pending restart retired.");
        first.ReleaseDispose();
        await RegistryAssert.ThrowsAsync<BridgeWidgetStoppedException>(() => restarting);
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));
        RegistryAssert.Equal(1, fixture.Clients.Count);
        RegistryAssert.Equal(0, fixture.Registry.RunningWorkerCount);
        await RegistryAssert.ThrowsAsync<BridgeWidgetStoppedException>(() => fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible));
        await RegistryAssert.ThrowsAsync<BridgeWidgetStoppedException>(() => fixture.RestartAsync(configured.Id));
    }
}
