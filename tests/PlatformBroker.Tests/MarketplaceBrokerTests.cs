using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class MarketplaceBrokerTests
{
    internal static async Task Authority()
    {
        var root=Path.Combine(Path.GetTempPath(),"wrail-marketplace-authority-"+Guid.NewGuid().ToString("N"));
        var identity=new BrokerWidgetIdentity("example.marketplace","example.publisher","instance");
        try
        {
            var consent=new ConsentStore(root); var backend=new SimulatedPlatformBrokerBackend(); var calls=0;
            if(PlatformCapabilities.IsManifestDeclarable(PlatformCapabilities.WidgetMarketplaceV1)) throw new Exception("Marketplace is manifest-declarable");
            await using var ordinary=new PlatformCapabilityBroker(identity,[],consent,backend,[PlatformCapabilities.PrivateStateV1]);
            ordinary.SetLifecycle(BrokerLifecycleState.Interactive);
            async Task Reject(PlatformCapabilityBroker broker,string op,MarketplaceCommand command,string code)
            {
                try { await broker.ExecuteAsync(new(1,1,identity,PlatformCapabilities.WidgetMarketplaceV1,op,BrokerJson.ToElement(command))); throw new Exception("Forbidden command succeeded"); }
                catch(BrokerException error) { if(error.Code!=code) throw; }
            }
            await Reject(ordinary,"marketplace.status",new("status"),"capability_not_declared");
            await using var manager=new PlatformCapabilityBroker(identity,[],consent,backend,[PlatformCapabilities.WidgetMarketplaceV1],AppLibrarySavedIdIssuer.Shared,
                marketplace:(command,_)=>{calls++;return ValueTask.FromResult(MarketplaceSnapshot.Unavailable);});
            manager.SetLifecycle(BrokerLifecycleState.Visible);
            await manager.ExecuteAsync(new(1,2,identity,PlatformCapabilities.WidgetMarketplaceV1,"marketplace.status",BrokerJson.ToElement(new MarketplaceCommand("status"))));
            await Reject(manager,"marketplace.download",new("download",PackageId:"example.widget"),"lifecycle_denied");
            await Reject(manager,"marketplace.download-updates",new("download-updates",PackageIds:["example.widget"]),"lifecycle_denied");
            await Reject(manager,"marketplace.install-updates",new("install-updates",OperationId:new string('a',32),ReviewToken:new string('b',64)),"lifecycle_denied");
            // A read operation cannot smuggle an install command past control admission.
            await Reject(manager,"marketplace.status",new("install",OperationId:new string('a',32),ReviewToken:new string('b',64)),"invalid_payload");
            await Reject(manager,"marketplace.status",new("install-updates",OperationId:new string('a',32),ReviewToken:new string('b',64)),"invalid_payload");
            manager.SetLifecycle(BrokerLifecycleState.Interactive);
            await manager.ExecuteAsync(new(1,3,identity,PlatformCapabilities.WidgetMarketplaceV1,"marketplace.download",BrokerJson.ToElement(new MarketplaceCommand("download",PackageId:"example.widget"))));
            await manager.ExecuteAsync(new(1,4,identity,PlatformCapabilities.WidgetMarketplaceV1,"marketplace.download-updates",BrokerJson.ToElement(new MarketplaceCommand("download-updates",PackageIds:["example.widget"]))));
            if(calls!=3) throw new Exception("Denied command reached management service or an authorized batch was rejected");
            manager.SetLifecycle(BrokerLifecycleState.Background);
            await Reject(manager,"marketplace.status",new("status"),"lifecycle_denied");
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
