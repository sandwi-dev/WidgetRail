using System.Globalization;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record ConnectionDetailsOwner(WidgetNavigationSnapshot<Route> Visit,string Account,string Channel,WebSessionReference Session);
    private sealed record ConnectionDetailsState(ConnectionDetailsOwner? Owner=null);
    private sealed record MeasuredWatch(string? Key,Relay? Relay);
    private sealed record ConnectionReading(ConnectionDetailsOwner Owner,MeasuredWatch Watch,DiscordCallMeasurement Call,
        DiscordIncomingMeasurement? Incoming,DiscordPinMeasurement? Pin,long ObservedAt,string Playback,double?[] Pings);
    private WidgetModel<ConnectionDetailsState> connectionDetails=null!;
    private WidgetResource<ConnectionReading> connectionReading=null!;

    private void InitializeConnectionDetails()
    {
        connectionDetails=CreateModel(new ConnectionDetailsState());
        connectionReading=CreateResource("discord.connection-details",new WidgetResourceOptions<ConnectionReading>
        {
            Load=ReadConnectionDetailsAsync,Lifetime=WidgetOperationLifetime.Active,RetainLastGoodValue=false,
            MapError=_=>new("discord.connection-details.unavailable","Connection measurements are unavailable. Refresh to try again.")
        });
    }

    private static string ConnectionDetailsAction(DiscordState data,WebSessionReference? session)=>DomainAction("connection-info",session?.Id,data.Account,data.ChannelId);
    private static string ConnectionAction(string method,ConnectionDetailsOwner owner)=>DomainAction(method,owner.Session.Id,owner.Account,owner.Channel,
        owner.Visit.Revision.ToString(CultureInfo.InvariantCulture));
    private static string ConnectionCloseAction(WidgetNavigationSnapshot<Route> route)=>DomainAction("connectionClose",route.InputScopeId,
        route.Revision.ToString(CultureInfo.InvariantCulture));
    private static string ConnectionRetryAction(ConnectionDetailsOwner owner,MeasuredWatch watch)=>DomainAction("connectionRetry",owner.Session.Id,
        owner.Account,owner.Channel,owner.Visit.Revision.ToString(CultureInfo.InvariantCulture),watch.Key,watch.Relay?.Lease);
    private bool ConnectionDetailsCurrent(ConnectionDetailsOwner owner,DiscordState? next=null)=>
        ReferenceEquals(navigation.Value,owner.Visit)&&!owner.Visit.RouteCancellationToken.IsCancellationRequested&&client?.Reference==owner.Session&&
        (next??CurrentState()) is {Ready:true} data&&data.Account==owner.Account&&data.ChannelId==owner.Channel;
    private MeasuredWatch CurrentMeasuredWatch()=>new(view.Value.StreamKey,view.Value.Receiver);
    private bool MeasuredWatchCurrent(MeasuredWatch watch)=>watch.Key==view.Value.StreamKey&&ReferenceEquals(watch.Relay,view.Value.Receiver);

    private void ReconcileConnectionDetails(DiscordState next)
    {
        if(connectionDetails.Value.Owner is not {} owner||ConnectionDetailsCurrent(owner,next))return;
        if(ReferenceEquals(navigation.Value,owner.Visit))navigation.Back();
        RetireConnectionDetails();
    }
    private void RetireConnectionDetails()
    {
        Operations.Cancel("discord.connection-details.ticker");
        connectionReading.Reset();connectionDetails.Update(_=>new());
    }
    private void ResumeConnectionDetails()
    {
        if(connectionDetails.Value.Owner is not {} owner||!ConnectionDetailsCurrent(owner))return;
        connectionReading.Reset();
        Operations.RunLatest("discord.connection-details.ticker",async context=>
        {
            using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken,owner.Visit.RouteCancellationToken);
            await WidgetTicker.RunWhileActiveAsync(TimeSpan.FromSeconds(2),async token=>
            {
                if(!context.IsCurrent||!ConnectionDetailsCurrent(owner))return;
                await connectionReading.Refresh().Completion.WaitAsync(token);
            },lifetime.Token,tickImmediately:true,timeProvider:time);
        },WidgetOperationLifetime.Active);
    }
    internal Task WaitForConnectionDetailsAsync(CancellationToken token=default)=>connectionReading.WhenIdleAsync(token);

    private async ValueTask<ConnectionReading> ReadConnectionDetailsAsync(CancellationToken token)
    {
        var owner=connectionDetails.Value.Owner;
        if(owner is null||!ConnectionDetailsCurrent(owner))throw new OperationCanceledException();
        var watch=CurrentMeasuredWatch();
        var before=connectionReading.Snapshot.Value;
        var value=await client!.InvokeAsync("connectionDiagnostics",new{account=owner.Account,channelId=owner.Channel},token);
        token.ThrowIfCancellationRequested();
        if(!ConnectionDetailsCurrent(owner)||!MeasuredWatchCurrent(watch))throw new OperationCanceledException();
        var call=DiscordCallMeasurement.Read(value);
        DiscordIncomingMeasurement? incoming=null;DiscordPinMeasurement? pin=null;
        if(watch is {Key:not null,Relay:{} relay}&&relay.Account==owner.Account&&relay.Channel==owner.Channel)
        {
            try{incoming=DiscordIncomingMeasurement.Read(await client.InvokeAsync("incomingDiagnostics",
                new{account=owner.Account,channelId=owner.Channel,key=watch.Key,lease=relay.Lease},token));}
            catch(Exception) when(!token.IsCancellationRequested){incoming=new(false);}
            if(!ConnectionDetailsCurrent(owner)||!MeasuredWatchCurrent(watch))throw new OperationCanceledException();
            try{pin=DiscordPinMeasurement.Read(await relay.Receiver.ReadDiagnosticsAsync(relay.Lease,token),relay.Lease);}
            catch(Exception) when(!token.IsCancellationRequested){ }
        }
        token.ThrowIfCancellationRequested();
        if(!ConnectionDetailsCurrent(owner)||!MeasuredWatchCurrent(watch))throw new OperationCanceledException();
        var now=time.GetTimestamp();
        var sameOwner=before is not null&&ReferenceEquals(before.Owner,owner);
        var previous=sameOwner&&before!.Watch==watch?before:null;
        var elapsed=previous is null?0:time.GetElapsedTime(previous.ObservedAt,now).TotalSeconds;
        var playback=watch.Relay is null?view.Value.ReceiverFailure is not null?"Pin disconnected · select the screen again to retry":"No active video pin":
            DiscordConnectionMeasurementPolicy.Playback(pin,incoming,previous?.Pin,previous?.Incoming,elapsed);
        return new(owner,watch,call,incoming,pin,now,playback,
            DiscordConnectionMeasurementPolicy.AppendPing(sameOwner?before!.Pings:[],call.PingMilliseconds));
    }

    private bool TryConnectionDetailsAction(WidgetActionEvent action,CancellationToken token,out ValueTask task)
    {
        task=ValueTask.CompletedTask;
        var data=CurrentState();var owner=connectionDetails.Value.Owner;
        if(action.ActionId.StartsWith("connectionClose.",StringComparison.Ordinal))
        {
            var route=navigation.Value;
            if(route.Route==Route.ConnectionDetails&&action.InputScopeId==route.InputScopeId&&action.ActionId==ConnectionCloseAction(route))
            {navigation.Back(action.FocusedElementId);RetireConnectionDetails();}
            return true;
        }
        if(action.ActionId.StartsWith("connection-info.",StringComparison.Ordinal))
        {
            if(navigation.Value.Route!=Route.Home||data is not {Ready:true,ChannelId:not null}||client?.Reference is not {} session||
                action.ActionId!=ConnectionDetailsAction(data,session))return true;
            PushRoute(Route.ConnectionDetails,action);
            connectionDetails.Update(_=>new(new(navigation.Value,data.Account,data.ChannelId,session)));
            ResumeConnectionDetails();return true;
        }
        if(!action.ActionId.StartsWith("connectionRefresh.",StringComparison.Ordinal)&&!action.ActionId.StartsWith("connectionRetry.",StringComparison.Ordinal))return false;
        if(owner is null||!ConnectionDetailsCurrent(owner)||action.InputScopeId!=owner.Visit.InputScopeId)return true;
        if(action.ActionId==ConnectionAction("connectionRefresh",owner))connectionReading.Refresh();
        else if(action.ActionId==ConnectionRetryAction(owner,CurrentMeasuredWatch())&&data is {Capabilities.Watch:true}&&
            data.Streams.FirstOrDefault(stream=>stream.Key==view.Value.StreamKey) is {} stream)
        {
            // Reuse the existing explicit Watch action and its domain guards.
            // This occurs only after the user's Reconnect pin command.
            navigation.Back(action.FocusedElementId);RetireConnectionDetails();
            task=OnActionAsync(new(WatchAction(data,stream,owner.Session),"discord.stream."+stream.UserId,
                InputScopeId:navigation.Value.InputScopeId),token);
        }
        return true;
    }

    private WidgetView ApplyConnectionDetails(WidgetView result,DiscordState? data)
    {
        if(navigation.Value.Route!=Route.ConnectionDetails)return result;
        const string id="discord.connection-dialog";
        var owner=connectionDetails.Value.Owner;
        var current=owner is not null&&ConnectionDetailsCurrent(owner,data);
        var snapshot=connectionReading.Snapshot;
        var observed=current&&snapshot is {HasValue:true,Value:{} sample}&&ReferenceEquals(sample.Owner,owner)&&MeasuredWatchCurrent(sample.Watch)?sample:null;
        var summary=current?VoiceConnectionLabel(data! with{VoiceConnection=observed?.Call.Phase??data!.VoiceConnection}):"The call changed. Return to Discord controls.";
        var content=new List<WidgetElement>{UI.ReadingSurface(id+".summary",summary,ActionSurfaceOrientation.Vertical,
            UI.Text(summary,id+".phase").Classes("discord-row-title")).Classes("discord-connection-summary")};
        if(observed is {} reading)
        {
            content.Add(UI.Text(reading.Call.PingMilliseconds is {} ping?$"Ping: {ping:0} ms":"Ping unavailable",id+".ping").Classes("discord-secondary"));
            if(reading.Call.AveragePingMilliseconds is {} average)
                content.Add(UI.Text($"Provider average: {average:0} ms",id+".average").Classes("discord-secondary"));
            if(reading.Call.Quality is {} quality)content.Add(UI.Text("Connection quality: "+(quality switch{"fine"=>"Good","average"=>"Fair",_=>"Poor"}),
                id+".quality").Classes("discord-secondary"));
            if(reading.Pings.Any(value=>value.HasValue))content.Add(UI.LineGraph("Recent connection ping",
                new(reading.Pings.Select((value,index)=>new GraphPoint(index,value)).ToArray(),0,DiscordConnectionMeasurementPolicy.HistoryLimit-1,Unit:"ms"),id+".history")
                .Classes("discord-connection-history"));
            if(reading.Watch.Key is not null)
            {
                content.Add(UI.Text("Incoming screen",id+".screen.title").Classes("discord-row-title"));
                var incoming=reading.Incoming;
                content.Add(UI.Text(incoming is {Available:true}?$"Discord received: {Dimensions(incoming.Width,incoming.Height)}"+
                    (incoming.FramesPerSecond is {} fps?$" · {fps:0.#} FPS":""):"Incoming measurements unavailable",id+".incoming").Classes("discord-secondary"));
                content.Add(UI.Text(reading.Playback,id+".playback").Classes("discord-secondary"));
                if(reading.Pin is {} pin)content.Add(UI.Text($"Pin video: {Dimensions(pin.Width,pin.Height)}"+
                    (pin.DroppedFrames is {} dropped?$" · {dropped:0} dropped frames":""),id+".pin").Classes("discord-secondary"));
            }
        }
        else content.Add(UI.Text(current?snapshot.Error?.Message??"Reading connection measurements…":"The call changed. Return to Discord controls.",id+".status")
            .Classes("discord-secondary"));
        content.Add(UI.Text("Updates while this panel is open. Ping is a connection measurement, not end-to-end audio latency.",id+".help").Classes("discord-secondary"));
        if(owner is not null)content.Add(UI.Button("Refresh",ConnectionAction("connectionRefresh",owner),id+".refresh").Busy(connectionReading.IsBusy).Disabled(!current).Classes("discord-control"));
        if(current&&owner is not null&&data is {Capabilities.Watch:true}&&data.Streams.Any(stream=>stream.Key==view.Value.StreamKey))
            content.Add(UI.Button("Reconnect pin",ConnectionRetryAction(owner,CurrentMeasuredWatch()),id+".retry").Classes("discord-control","discord-quiet"));
        return result.WithModal(new(id,"Connection details",UI.Stack(id+".content",content.ToArray()).Classes("discord-voice-dialog"),
            id+".summary",ConnectionCloseAction(navigation.Value))
        {HeaderActions=UI.ControllerHint(ControllerButton.B,"Back",id+".back"),AnchoredPresentation=new(new(ElementId:"discord.main.heading"),Width:480,Height:520)});
    }
    private static string Dimensions(double? width,double? height)=>width is >0&&height is >0?$"{width:0} × {height:0}":"not available";
}
