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
        var problem=new ReceiverProblem("Select the shared screen again to retry.");
        var removed=slot.State.Update(current=>context.IsCurrent&&ReferenceEquals(current.Receiver,relay)
            ?(current with{Receiver=null,ReceiverFailure=problem},true):(current,false)).Result;
        if(!removed)return;
        view.Update(current=>current with{Notice="The shared screen disconnected. Select it again to retry."});
        try{await relay.Receiver.DisposeAsync();}
        finally
        {
            var reason=await ReleaseLeaseAsync(relay.Account,relay.Lease);
            var message=reason switch
            {
                "video_no_frames"=>"No video frames arrived in the player. Select the screen to retry.",
                "track_unavailable"=>"Discord stopped providing the video track. Select the screen to retry.",
                "video_view_unavailable"=>"The stream player closed. Select the screen to retry.",
                "watch_ended"=>"The Discord watch ended. Select the screen to retry.",
                "watch_demand_failed"=>"Discord could not keep the video connection active. Select the screen to retry.",
                _=>null
            };
            if(message is not null && context.IsCurrent &&
                slot.State.Update(s=>ReferenceEquals(s.ReceiverFailure,problem) ? (s with{ReceiverFailure=new(message)},true):(s,false)).Result)
                view.Update(s=>s.Notice=="The shared screen disconnected. Select it again to retry." ? s with{Notice=message} : s);
        }
        // Retain selection for an explicit user retry. Never start a new watch here.
    },WidgetOperationLifetime.Widget);
}
