using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class PinningBrokerTests
{
    internal static async Task Contract()
    {
        var identity=new BrokerWidgetIdentity("example.widget","example.publisher","instance");
        var store=new ConsentStore(Path.Combine(Path.GetTempPath(),"pinning-tests-"+Guid.NewGuid().ToString("N")));
        if(PlatformCapabilities.IsManifestDeclarable(PlatformCapabilities.WidgetPinningV1))throw new Exception("Pinning became a manifest permission");
        await using var broker=new PlatformCapabilityBroker(identity,[],store,new SimulatedPlatformBrokerBackend(),[PlatformCapabilities.WidgetPinningV1]);
        var request=new BrokerRequestEnvelope(1,1,identity,PlatformCapabilities.WidgetPinningV1,"pin.open",BrokerJson.ToElement(new WidgetPinRequest("stream")));
        var close=request with {Operation="pin.close",Payload=BrokerJson.ToElement(new WidgetPinCloseRequest("stream",Guid.NewGuid().ToString("N")))};
        var manage=request with{Operation="pin.manage",Payload=BrokerJson.ToElement(new WidgetPinManageRequest())};
        foreach(var state in new[]{BrokerLifecycleState.Created,BrokerLifecycleState.Visible,BrokerLifecycleState.Background})
        {
            broker.SetLifecycle(state);
            await Reject(request,"lifecycle_denied");
            await Reject(close,"lifecycle_denied");
            await Reject(manage,"lifecycle_denied");
        }
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await broker.ExecuteAsync(request);
        await broker.ExecuteAsync(close);
        await broker.ExecuteAsync(manage);
        await broker.ExecuteAsync(manage with{Payload=close.Payload});
        foreach(var payload in new[]{new WidgetPinManageRequest("stream"),new WidgetPinManageRequest(null,Guid.NewGuid().ToString("N")),
            new WidgetPinManageRequest("host.embedded-media.compact",Guid.NewGuid().ToString("N"))})
            await Reject(manage with{Payload=BrokerJson.ToElement(payload)},"invalid_payload");
        await Reject(manage with{Payload=JsonSerializer.SerializeToElement(new{widgetId="foreign"})},"invalid_payload");
        await Reject(close with{Payload=BrokerJson.ToElement(new WidgetPinCloseRequest("stream","not-a-selection"))},"invalid_payload");
        await Reject(close with{Payload=BrokerJson.ToElement(new WidgetPinCloseRequest("host.full-widget",Guid.NewGuid().ToString("N")))},"invalid_payload");
        foreach(var json in new[]{"{}","{\"layoutId\":\"host.full-widget\"}","{\"layoutId\":\"stream\",\"widgetId\":\"another\"}","{\"layoutId\":\"../other\"}"})
            await Reject(request with {Payload=JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(json))},"invalid_payload");
        async Task Reject(BrokerRequestEnvelope value,string code)
        {
            try{await broker.ExecuteAsync(value);throw new Exception("Pin request was not rejected");}
            catch(BrokerException error) when(error.Code==code) { }
        }
    }
}
