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
        foreach(var state in new[]{BrokerLifecycleState.Created,BrokerLifecycleState.Visible,BrokerLifecycleState.Background})
        {
            broker.SetLifecycle(state);
            await Reject(request,"lifecycle_denied");
        }
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await broker.ExecuteAsync(request);
        foreach(var json in new[]{"{}","{\"layoutId\":\"host.full-widget\"}","{\"layoutId\":\"stream\",\"widgetId\":\"another\"}","{\"layoutId\":\"../other\"}"})
            await Reject(request with {Payload=JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(json))},"invalid_payload");
        async Task Reject(BrokerRequestEnvelope value,string code)
        {
            try{await broker.ExecuteAsync(value);throw new Exception("Pin request was not rejected");}
            catch(BrokerException error) when(error.Code==code) { }
        }
    }
}
