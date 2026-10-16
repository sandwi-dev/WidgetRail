using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetRuntime;

internal static class HostWebAppScenarios
{
    internal static async Task RunAsync()
    {
        var app=new UserWebApp{Id=UserWebAppStore.IdPrefix+Guid.NewGuid().ToString("N"),Name="Test website",Url="https://example.com/app"};
        var catalog=new BridgeCatalog([]).WithUserWebApps([app]);
        var descriptor=catalog.Widgets.Single();
        Require(descriptor.IsUserWebApp&&descriptor.CanManage&&descriptor.CanStop&&descriptor.PinningSupported&&descriptor.FullWidgetPinningSupported,"Webapp shell features");
        var configured=catalog.IntentWidgets.Single();
        var renamed=new BridgeCatalog([]).WithUserWebApps([app with{Name="Renamed",Revision=2}]).IntentWidgets.Single();
        Require(configured.WorkerFingerprint==renamed.WorkerFingerprint && configured.CatalogFingerprint!=renamed.CatalogFingerprint,"Rename preserves the active browser lifetime");
        var navigated=new BridgeCatalog([]).WithUserWebApps([app with{Url="https://other.example/",Revision=2}]).IntentWidgets.Single();
        Require(configured.WorkerFingerprint!=navigated.WorkerFingerprint,"Address replacement retires the old view");
        Require(catalog.WithUserWebApps([app with{Enabled=false}]).Widgets.Count==0,"Disabled webapp is removed from active catalog");
        Require(catalog.WithStoppedWidgets([app.Id]).Widgets.Single().IsStopped,"Webapp uses ordinary persisted stop state");
        await using var client=new HostWebAppClient(configured);
        Require(!client.IsRunning&&client.Starts==0,"No browser view starts during catalog loading");
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive,default);
        var view=await client.GetSnapshotAsync(default);
        var browser=view.Root.Children.Single().WebBrowser!;
        Require(browser.Url==app.Url && browser.Options==WebBrowserOptions.WebApp,"Shared view uses standard persistent webapp defaults");
        Require(view.InitialFocusId=="webapp.page" && view.WidgetInstanceId==app.Id+".default","Stable identity and initial browser focus");
        Require(client.Starts==1,"One logical presentation lifetime");
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Background,default);
        Require(client.Starts==1,"Background transition keeps the existing lifetime");
        await client.UnloadAsync(default);
        Require(!client.IsRunning,"Stop retires the host-authored view");
        await client.GetSnapshotAsync(default);
        Require(client.Starts==2,"Reopening advances the authority lifetime");
        foreach(var url in new[]{"http://127.0.0.1:18786/","http://localhost:3000/app","http://example.com/"})
        {
            var local=new BridgeCatalog([]).WithUserWebApps([app with{Url=url}]).IntentWidgets.Single();
            await using var localClient=new HostWebAppClient(local);
            var localView=await localClient.GetSnapshotAsync(default);
            var localBrowser=localView.Root.Children.Single().WebBrowser!;
            Require(localBrowser.IsWellFormed() && localBrowser.Options.Storage==BrowserStorage.Persistent &&
                localBrowser.Options.Popups==BrowserPopupMode.Hosted && localBrowser.Options.ExternalLinks==BrowserExternalLinkMode.InWidget,
                "HTTP webapps use a valid persistent browser policy without terminating the bridge");
        }
        await using var invalidClient=new HostWebAppClient(configured with{UserWebApp=app with{Url="file:///C:/not-a-webapp"}});
        try { await invalidClient.GetSnapshotAsync(default); throw new InvalidOperationException("Invalid webapp did not fail"); }
        catch(WidgetProcessException) { /* Isolated widget failure, not an uncaught bridge exception. */ }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
