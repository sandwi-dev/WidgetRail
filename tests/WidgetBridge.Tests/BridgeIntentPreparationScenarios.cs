using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

internal static partial class BridgeIntentScenarios
{
    internal static async Task PreparedPayloads()
    {
        var legacy=WidgetIntentRequest.Create(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url="https://example.com"}));
        Check(!WidgetRail.WidgetRuntime.RuntimeJson.ToElement(new WidgetRail.WidgetRuntime.IntentDeliveryPayload(1,legacy)).TryGetProperty("prepare",out _));
        Check(!BridgeJson.ToElement(new WidgetIntentPreparation("source",null,WidgetIntentLaunchKind.Rejected,[])).TryGetProperty("preparationFailed",out _));
        var url="https://example.com/placeholder";
        await using var fixture=Fixture([Source()],()=>url,routing:WidgetIntentRouting.Windows,preparationKey:"search");
        var request=(await PrepareSource(fixture)) with{PreparationId=Guid.NewGuid().ToString("N")};
        var client=fixture.Clients.Single();
        client.PreparationHandler=(intent,_)=>Task.FromResult<WidgetIntentRequest?>(intent with{PreparationKey=null,Payload=JsonSerializer.SerializeToElement(new{url="https://example.com/calculated"})});
        var prepared=await fixture.Registry.PrepareIntentAsync(request,default);
        Check(prepared.Kind==WidgetIntentLaunchKind.ExternalBrowser);
        var committed=await fixture.Registry.CommitIntentAsync(new("source",prepared.TicketId!),default);
        Check(committed.Accepted&&committed.ExternalUrl=="https://example.com/calculated");
        Check(!(await fixture.Registry.CommitIntentAsync(new("source",prepared.TicketId!),default)).Accepted);
        Check((await fixture.Registry.PrepareIntentAsync(request,default)).PreparationFailed);
        Check((await fixture.Registry.PrepareIntentAsync(request,default)).PreparationFailed);
        foreach(var mode in new[]{"route","contract","schema","recursive"})
        {
            client.PreparationHandler=(intent,_)=>Task.FromResult<WidgetIntentRequest?>(mode switch
            {
                "route"=>intent with{PreparationKey=null,Routing=WidgetIntentRouting.WidgetPreferred},
                "contract"=>intent with{PreparationKey=null,Version=2},
                "schema"=>intent with{PreparationKey=null,Payload=JsonSerializer.SerializeToElement(new{url="https://example.com",extra=true})},
                _=>intent
            });
            Check((await fixture.Registry.PrepareIntentAsync(request with{PreparationId=Guid.NewGuid().ToString("N")},default)).Kind==WidgetIntentLaunchKind.Rejected);
        }
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PreparationHandler=async(intent,token)=>{entered.SetResult();await release.Task.WaitAsync(token);return intent with{PreparationKey=null};};
        var pending=fixture.Registry.PrepareIntentAsync(request with{PreparationId=Guid.NewGuid().ToString("N")},default);
        await entered.Task;url="https://example.com/changed";await fixture.GetSnapshotAsync("source");release.SetResult();
        Check((await pending).Kind==WidgetIntentLaunchKind.Rejected);
    }

    internal static async Task PreparationCancellation()
    {
        await using var fixture=Fixture([Source()],preparationKey:"search");
        var request=(await PrepareSource(fixture)) with{PreparationId=Guid.NewGuid().ToString("N")};
        var client=fixture.Clients.Single();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled=false;
        client.PreparationHandler=async(_,token)=>
        {
            entered.TrySetResult();try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){cancelled=true;throw;}
            return null;
        };
        var pending=fixture.Registry.PrepareIntentAsync(request,default);await entered.Task;
        await fixture.Registry.CancelIntentAsync(new("wrong-source",request.PreparationId!));Check(!pending.IsCompleted);
        await fixture.Registry.CancelIntentAsync(new("source",request.PreparationId!));
        var result=await pending.WaitAsync(TimeSpan.FromSeconds(3));
        Check(cancelled&&result.PreparationFailed&&result.TicketId is null);
        var beforeStart=request with{PreparationId=Guid.NewGuid().ToString("N")};
        var calls=0;client.PreparationHandler=(intent,_)=>{calls++;return Task.FromResult<WidgetIntentRequest?>(intent with{PreparationKey=null});};
        await fixture.Registry.CancelIntentAsync(new("source",beforeStart.PreparationId!));
        Check((await fixture.Registry.PrepareIntentAsync(beforeStart,default)).PreparationFailed&&calls==0);
        Check((await fixture.Registry.PrepareIntentAsync(request with{PreparationId=Guid.NewGuid().ToString("N")},default)).TicketId is not null&&calls==1);
    }
}
