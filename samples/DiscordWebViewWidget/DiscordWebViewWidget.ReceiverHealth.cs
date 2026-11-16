using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private void MonitorReceiver(WatchSlot slot,Relay relay)=>Operations.RunLatest(slot.Pin.Id+".health",async context=>
    {
        DiscordReceiverFailure failure;
        try{failure=await relay.Receiver.Failure.WaitAsync(context.CancellationToken);}
        catch(OperationCanceledException){return;}
        if(!context.IsCurrent||failure.Lease!=relay.Lease)return;
        var removed=slot.State.Update(current=>context.IsCurrent&&ReferenceEquals(current.Receiver,relay)
            ?(current with{Receiver=null,ReceiverFailure=new("Select the shared screen again to retry.")},true):(current,false)).Result;
        if(!removed)return;
        view.Update(current=>current with{Notice="The shared screen disconnected. Select it again to retry."});
        try{await relay.Receiver.DisposeAsync();}
        finally{await ReleaseLeaseAsync(relay.Account,relay.Lease);}
        // Retain selection for an explicit user retry. Never start a new watch here.
    },WidgetOperationLifetime.Widget);
}
