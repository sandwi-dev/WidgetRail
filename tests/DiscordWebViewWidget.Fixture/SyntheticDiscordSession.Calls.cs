using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

internal sealed partial class SyntheticDiscordSession
{
    private string? callsAuditPath;
    private readonly List<object> callActions=[];
    private readonly List<string> callPhases=[];
    private readonly HashSet<string> callCommands=[];
    private readonly CancellationTokenSource callLifetime=new();
    private Task callSchedule=Task.CompletedTask;
    private int microphoneRequests, completedCalls;
    private string? appliedDockPhase;

    // Only the isolated, credential-free private-call fixture reads this file.
    // The native test advances known states; no production adapter hook exists.
    private void ApplyCallDockFixturePhase()
    {
        if(callsAuditPath is null)return;
        var path=Path.Combine(Path.GetDirectoryName(callsAuditPath)!,"call-dock-phase.txt");
        if(!File.Exists(path))return;
        var phase=File.ReadAllText(path).Trim();
        lock(messageGate)
        {
            if(phase==appliedDockPhase||state.ChannelId is null||state.Calls?.Current?.Phase!="active")return;
            var people=Enumerable.Range(1,7).Select(index=>new DiscordParticipant(index.ToString(),
                index==1?"You":"Synthetic participant "+index,index==2,index==3,true)).ToArray();
            state=phase switch
            {
                "overflow"=>state with{Participants=people,VoiceConnection="connected"},
                "reconnecting"=>state with{Participants=people.Reverse().ToArray(),VoiceConnection="reconnecting"},
                "one"=>state with{Participants=people.Take(1).ToArray(),VoiceConnection="connected"},
                "empty"=>state with{Participants=[],VoiceConnection="connected"},
                _=>throw new InvalidDataException("Unknown synthetic dock phase.")
            };
            appliedDockPhase=phase;WriteCallAudit();
        }
    }
    public event Action<WebSessionReference>? Invalidated;
    private static DiscordCalls AvailableCalls(params DiscordCallInvitation[] invitations)=>new(true,true,true,true,invitations,[]);

    private void InitializePrivateCalls()
    {
        var marker=Path.Combine(AppContext.BaseDirectory,"fixture-private-calls.enabled");
        if(!File.Exists(marker))return;
        callsAuditPath=File.ReadAllText(marker).Trim();
        if(!Path.IsPathFullyQualified(callsAuditPath)||Path.GetFileName(callsAuditPath)!="calls-provider.json")
            throw new InvalidDataException("Invalid synthetic call audit path.");
        state=state with{ChannelId=null,ChannelName="",Participants=[],Streams=[],Activities=[],VoiceConnection="disconnected",
            Calls=AvailableCalls(new DiscordCallInvitation(new string('b',32),"11","Morgan — synthetic call"))};
        WriteCallAudit();
    }
    private void ObserveMicrophoneRequest()
    {if(callsAuditPath is not null){lock(messageGate){microphoneRequests++;WriteCallAudit();}}}

    private JsonElement InvokePrivateCall(string method,JsonElement request)
    {
        lock(messageGate)
        {
            if(request.GetProperty("account").GetString()!=state.Account||request.GetProperty("previousChannelId").GetString()!=state.ChannelId)
                throw new InvalidDataException("Synthetic call owner changed.");
            var target=request.GetProperty("channelId").GetString()!;
            if(!SyntheticConversations().Any(item=>item.ChannelId==target))throw new InvalidDataException("Unknown synthetic call target.");
            var command=request.GetProperty("commandId").GetString()!;
            if(!Guid.TryParseExact(command,"N",out _)||!callCommands.Add(command))throw new InvalidDataException("Duplicate synthetic call command.");
            if(method!="callStart" && !state.Calls!.Incoming.Any(invite=>invite.ChannelId==target&&invite.Id==request.GetProperty("invitationId").GetString()))
                throw new InvalidDataException("Synthetic invitation retired.");
            callActions.Add(new{method,target,microphoneRequests});
            if(method!="callDecline" && microphoneRequests==0)throw new InvalidDataException("Synthetic microphone admission was skipped.");
            if(method=="callDecline")state=state with{Calls=AvailableCalls()};
            else
            {
                var name=SyntheticConversations().Single(item=>item.ChannelId==target).Name;
                state=state with{ChannelId=target,ChannelName=name,Participants=[new("1","You",state.Muted,state.Deafened,false)],
                    Streams=[],VoiceConnection=method=="callStart"?"connecting":"connected",
                    Calls=AvailableCalls() with{Current=new(target,name,method=="callStart"?"outgoing":"active")}};
                callPhases.Add(state.Calls.Current!.Phase);
                if(method=="callStart")ScheduleCall(async token=>
                {
                    await Task.Delay(700,token);
                    lock(messageGate)
                    {
                        if(state.ChannelId!=target)return;
                        state=state with{VoiceConnection="connected",Calls=state.Calls! with{Current=new(target,name,"active")}};
                        callPhases.Add("active");WriteCallAudit();
                    }
                    Invalidated?.Invoke(Reference!);
                });
            }
            WriteCallAudit();
            return JsonSerializer.SerializeToElement(new{status="applied"});
        }
    }
    private void PrivateCallLeft()
    {
        if(callsAuditPath is null)return;
        lock(messageGate)
        {
            state=state with{Calls=AvailableCalls()};completedCalls++;
            callActions.Add(new{method="leave",target=(string?)null,microphoneRequests});WriteCallAudit();
            if(completedCalls!=1)return;
            ScheduleCall(async token=>
            {
                await Task.Delay(1800,token);
                lock(messageGate)
                {
                    if(state.ChannelId is not null)return;
                    state=state with{Calls=AvailableCalls(new DiscordCallInvitation(new string('c',32),"11","Morgan — synthetic background call"))};WriteCallAudit();
                }
                Invalidated?.Invoke(Reference!);
            });
        }
    }
    private void ScheduleCall(Func<CancellationToken,Task> action)=>callSchedule=Task.WhenAll(callSchedule,RunScheduledCall(action));
    private async Task RunScheduledCall(Func<CancellationToken,Task> action)
    {try{await action(callLifetime.Token);}catch(OperationCanceledException)when(callLifetime.IsCancellationRequested){}}
    private void WriteCallAudit()
    {
        if(callsAuditPath is null)return;
        state.Validate();
        var staging=callsAuditPath+".tmp";
        File.WriteAllText(staging,JsonSerializer.Serialize(new{microphoneRequests,actions=callActions,phases=callPhases,
            incoming=state.Calls?.Incoming.Length,current=state.ChannelId,dockPhase=appliedDockPhase}));
        File.Move(staging,callsAuditPath,true);
    }
}
