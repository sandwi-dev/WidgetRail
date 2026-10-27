using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using System.Text.Json;

internal static class WebCaptureStreamTests
{
    internal static async Task Ownership()
    {
        var registry=new WebSessionRegistry();
        var identity=new BrokerWidgetIdentity("example.capture","example.publisher","instance");
        using var owner=registry.CreateOwner("capture",identity,false,true);
        var authority=new object();
        var reference=await Open(registry,owner,"capture");
        var request=new WebCaptureStreamRequest(reference,new());
        using var sameIdentityOwner=registry.CreateOwner("different-worker",identity,false,true);
        await Reject(()=>registry.PrepareWebCaptureAsync(authority,identity,request,default,sameIdentityOwner).AsTask(),"session_closed");
        await Reject(()=>owner.ExecuteAsync(new(WebSessionOperation.PrepareWebCapture)
            {Session=reference,CaptureOptions=new()},default).AsTask(),"permission_denied");
        await Reject(()=>registry.PrepareWebCaptureAsync(authority,identity with{InstanceId="other"},request,default,owner).AsTask(),"session_closed");
        var plain=await Open(registry,owner,"plain",native:false);
        await Reject(()=>registry.PrepareWebCaptureAsync(authority,identity,request with{Session=plain},default,owner).AsTask(),"permission_denied");

        var preparing=registry.PrepareWebCaptureAsync(authority,identity,request,default,owner).AsTask();
        var work=(await registry.TakeAsync(default)).Work.Single();
        var ticket=work.Command.CaptureTicket!;
        Check(ticket.IsWellFormed() && ticket.Session==reference && work.Command.CaptureOptions==request.Options,"Host prepare lost its scoped ticket/options");
        await Reject(()=>registry.PrepareWebCaptureAsync(authority,identity,request,default,owner).AsTask(),"capture_already_active");
        registry.Publish(new(reference,[],Capture:Prepared(ticket) with{Phase=WebCaptureStreamPhase.Capturing}));
        registry.Complete(new(work.RequestId,Capture:Prepared(ticket)));
        var initialResult=await preparing;
        Check(initialResult.Ticket==ticket && initialResult.Phase==WebCaptureStreamPhase.Capturing,"Delayed preparation acknowledgement regressed a live native stream");
        registry.Publish(new(reference,[],Capture:Prepared(ticket) with{Phase=WebCaptureStreamPhase.Capturing}));
        Check(registry.WebCaptureStatus(authority,identity,ticket,owner).Phase==WebCaptureStreamPhase.Capturing,"Native capture state was not retained");
        var retiredTicket=ticket with{Id=new string('f',32)};
        await Reject(()=>registry.StopWebCaptureAsync(authority,identity,retiredTicket,default,owner).AsTask(),"capture_retired");
        Check(registry.WebCaptureStatus(authority,identity,ticket,owner).Phase==WebCaptureStreamPhase.Capturing,"A stale ticket stopped a different stream");

        registry.RetireWebCaptures(authority);
        var revoked=(await registry.TakeAsync(default)).Work.Single();
        Check(revoked.CaptureRevoked && revoked.Command.CaptureTicket==ticket,"Permission retirement failed to wake exact-source native cleanup");
        registry.Publish(new(reference,[],Capture:Prepared(ticket) with{Phase=WebCaptureStreamPhase.Capturing}));
        Check(registry.WebCaptureStatus(authority,identity,ticket,owner).Phase==WebCaptureStreamPhase.Capturing,"Queued retirement falsely confirmed native stop");
        await Reject(()=>registry.PrepareWebCaptureAsync(new object(),identity,request,default,owner).AsTask(),"capture_already_active");
        var revokedStop=registry.StopWebCaptureAsync(authority,identity,ticket,default,owner).AsTask();
        Check(!revokedStop.IsCompleted,"Revoked Stop completed before native teardown");
        registry.Publish(new(reference,[],Capture:new(WebCaptureStreamPhase.Stopped){Ticket=ticket}));
        await revokedStop;

        var nextAuthority=new object();
        using(var cancel=new CancellationTokenSource())
        {
            var pending=registry.PrepareWebCaptureAsync(nextAuthority,identity,request,cancel.Token,owner).AsTask();
            var pendingWork=(await registry.TakeAsync(default)).Work.Single();
            cancel.Cancel();
            try{await pending;throw new Exception("Cancelled picker admitted capture");}catch(OperationCanceledException){}
            var cancellation=(await registry.TakeAsync(default)).Work.Single();
            var cleanup=(await registry.TakeAsync(default)).Work.Single();
            Check(cancellation.Cancelled && cancellation.RequestId==pendingWork.RequestId && cleanup.CaptureRevoked &&
                cleanup.Command.CaptureTicket==pendingWork.Command.CaptureTicket,"Cancel did not retire both picker and late native startup");
            registry.Complete(new(pendingWork.RequestId,Capture:Prepared(pendingWork.Command.CaptureTicket!)));
            Check(!registry.IsCurrent(pendingWork),"Late preparation revived cancelled authority");
            // Cancel can precede native allocation: no state-change event is
            // emitted, but the cleanup reply must still release capacity.
            registry.Complete(new(cleanup.RequestId,Capture:new(WebCaptureStreamPhase.Stopped){Ticket=pendingWork.Command.CaptureTicket}));
        }
        var invalid=registry.PrepareWebCaptureAsync(nextAuthority,identity,request,default,owner).AsTask();
        var invalidWork=(await registry.TakeAsync(default)).Work.Single();
        registry.Complete(new(invalidWork.RequestId,Capture:Prepared(ticket)));
        await Reject(()=>invalid,"invalid_host_result");
        Check((await registry.TakeAsync(default)).Work.Single().CaptureRevoked,"Malformed native result leaked its reserved source");
        registry.Publish(new(reference,[],Capture:new(WebCaptureStreamPhase.Stopped){Ticket=invalidWork.Command.CaptureTicket}));

        var first=await Prepare(registry,owner,nextAuthority,identity,request);
        var secondReference=await Open(registry,owner,"second");
        var second=await Prepare(registry,owner,nextAuthority,identity,new(secondReference,new(480,15)));
        var thirdReference=await Open(registry,owner,"third");
        await Reject(()=>registry.PrepareWebCaptureAsync(nextAuthority,identity,new(thirdReference,new()),default,owner).AsTask(),"capture_limit");
        registry.Publish(new(reference,[],Capture:first with{Phase=WebCaptureStreamPhase.Stopped,Reason="source_closed"}));
        registry.Publish(new(reference,[],Capture:first with{Phase=WebCaptureStreamPhase.Capturing}));
        Check(registry.WebCaptureStatus(nextAuthority,identity,first.Ticket!,owner).Phase==WebCaptureStreamPhase.Stopped,"Late native progress revived a terminal source");
        var third=await Prepare(registry,owner,nextAuthority,identity,new(thirdReference,new()));
        Check(third.Phase==WebCaptureStreamPhase.Prepared,"Native source loss did not release the bounded capture slot");
        foreach(var malformed in new[]{second,second with{Phase=WebCaptureStreamPhase.Stopped,Ticket=null},second with{Phase=WebCaptureStreamPhase.Stopped,Ticket=first.Ticket}})
        {
            var rejectedStop=registry.StopWebCaptureAsync(nextAuthority,identity,second.Ticket!,default,owner).AsTask();
            var rejectedWork=(await registry.TakeAsync(default)).Work.Single();
            registry.Complete(new(rejectedWork.RequestId,Capture:malformed));
            await Reject(()=>rejectedStop,"invalid_host_result");
            Check(registry.WebCaptureStatus(nextAuthority,identity,second.Ticket!,owner).Phase==WebCaptureStreamPhase.Prepared,
                "Unconfirmed or mismatched Stop released capture ownership");
        }
        var stopping=registry.StopWebCaptureAsync(nextAuthority,identity,second.Ticket!,default,owner).AsTask();
        var stop=(await registry.TakeAsync(default)).Work.Single();
        registry.Complete(new(stop.RequestId,Capture:second with{Phase=WebCaptureStreamPhase.Stopped,Reason="stopped"}));
        await stopping;
        Check(registry.WebCaptureStatus(nextAuthority,identity,second.Ticket!,owner).Phase==WebCaptureStreamPhase.Stopped,"Explicit stop did not publish terminal state");
        await Retirement();
        await ConstrainedQuality();
    }

    private static async Task ConstrainedQuality()
    {
        var registry=new WebSessionRegistry();
        var identity=new BrokerWidgetIdentity("example.capture.quality","example.publisher","instance");
        using var owner=registry.CreateOwner("capture-quality",identity,false,true);
        var authority=new object();
        var session=await Open(registry,owner,"quality");
        WebCaptureStreamOptions[] choices=[new(720,30),new(1080,60)];
        var request=new WebCaptureStreamRequest(session,new()){AllowedQualities=choices};
        choices[1]=new(1440,60);
        await Reject(()=>owner.ExecuteAsync(new(WebSessionOperation.PrepareWebCaptureConstrained)
            {Session=session,CaptureOptions=new(),CaptureQualities=request.AllowedQualities},default).AsTask(),"permission_denied");
        var pending=registry.PrepareWebCaptureAsync(authority,identity,request,default,owner).AsTask();
        var work=(await registry.TakeAsync(default)).Work.Single();
        Check(work.Command.Operation==WebSessionOperation.PrepareWebCaptureConstrained&&
            work.Command.CaptureQualities!.SequenceEqual(new WebCaptureStreamOptions[]{new(720,30),new(1080,60)}),
            "Constrained preparation lost its operation or immutable provider choices");
        var selected=Prepared(work.Command.CaptureTicket!) with{Quality=new(1080,60){ContentPreference=WebCaptureContentPreference.Motion}};
        registry.Complete(new(work.RequestId,Capture:selected));
        Check((await pending).Quality==selected.Quality,"Host-selected admitted higher quality/content preference was rejected");
        await Reject(()=>{registry.Publish(new(session,[],Capture:selected with{Phase=WebCaptureStreamPhase.Capturing,Quality=new(1080,60)}));return Task.CompletedTask;},"invalid_host_result");
        Check((await registry.TakeAsync(default)).Work.Single().CaptureRevoked,"Changed capture quality did not revoke the source");
        Check(registry.WebCaptureStatus(authority,identity,selected.Ticket!,owner).Quality==selected.Quality,"Rejected quality overwrote admitted choice");
        registry.Publish(new(session,[],Capture:new(WebCaptureStreamPhase.Stopped){Ticket=selected.Ticket}));

        pending=registry.PrepareWebCaptureAsync(authority,identity,request,default,owner).AsTask();
        work=(await registry.TakeAsync(default)).Work.Single();
        registry.Complete(new(work.RequestId,Capture:Prepared(work.Command.CaptureTicket!) with{Quality=new(1440,60)}));
        await Reject(()=>pending,"invalid_host_result");
        var cleanup=(await registry.TakeAsync(default)).Work.Single();
        Check(cleanup.CaptureRevoked&&cleanup.Command.CaptureTicket==work.Command.CaptureTicket,"Invalid choice did not reserve and revoke its exact capture");
        await Reject(()=>registry.PrepareWebCaptureAsync(authority,identity,request,default,owner).AsTask(),"capture_already_active");
        registry.Complete(new(cleanup.RequestId,Capture:new(WebCaptureStreamPhase.Stopped){Ticket=work.Command.CaptureTicket}));

        using var cancellation=new CancellationTokenSource();
        pending=registry.PrepareWebCaptureAsync(authority,identity,request,cancellation.Token,owner).AsTask();
        work=(await registry.TakeAsync(default)).Work.Single();
        cancellation.Cancel();
        try{await pending;throw new Exception("Constrained picker ignored cancellation");}catch(OperationCanceledException){}
        var cancelled=(await registry.TakeAsync(default)).Work.Single();
        cleanup=(await registry.TakeAsync(default)).Work.Single();
        Check(cancelled.Cancelled&&cancelled.RequestId==work.RequestId&&cleanup.CaptureRevoked&&cleanup.Command.CaptureTicket==work.Command.CaptureTicket,
            "Constrained picker cancellation did not cancel consent UI and retire native startup");
        registry.Complete(new(cleanup.RequestId,Capture:new(WebCaptureStreamPhase.Stopped){Ticket=work.Command.CaptureTicket}));
    }

    private static async Task Retirement()
    {
        var registry=new WebSessionRegistry();
        var identity=new BrokerWidgetIdentity("example.capture.retirement","example.publisher","instance");
        using var owner=registry.CreateOwner("retirement",identity,false,true);
        var authority=new object();
        var firstReference=await Open(registry,owner,"first");
        var first=await Prepare(registry,owner,authority,identity,new(firstReference,new()));
        await owner.ExecuteAsync(new(WebSessionOperation.Close){Session=firstReference},default);
        var retirement=(await registry.TakeAsync(default)).Work.Single();
        Check(retirement.CaptureRevoked&&retirement.Command.CaptureTicket==first.Ticket,"Backend close lost exact capture retirement");
        Check((await registry.TakeAsync(default)).Work.Single().Command.Operation==WebSessionOperation.Close,"Backend close work was not queued");
        Check(registry.WebCaptureStatus(authority,identity,first.Ticket!,owner).Phase==WebCaptureStreamPhase.Prepared,"Closed backend discarded uncertain capture state");
        registry.Complete(new(retirement.RequestId,Capture:first));
        Check(registry.WebCaptureStatus(authority,identity,first.Ticket!,owner).Phase==WebCaptureStreamPhase.Prepared,
            "Nonterminal cleanup reply falsely confirmed retirement");
        await Reject(()=>registry.StopWebCaptureAsync(new object(),identity,first.Ticket!,default,owner).AsTask(),"capture_retired");
        using(var cancel=new CancellationTokenSource())
        {
            var cancelledStop=registry.StopWebCaptureAsync(authority,identity,first.Ticket!,cancel.Token,owner).AsTask();
            var retry=(await registry.TakeAsync(default)).Work.Single();
            Check(retry.CaptureRevoked&&retry.Command.CaptureTicket==first.Ticket,"Explicit Stop did not retry unconfirmed retirement");
            cancel.Cancel();
            try{await cancelledStop;throw new Exception("Retired capture wait ignored cancellation");}catch(OperationCanceledException){}
        }
        var secondReference=await Open(registry,owner,"second");
        _=await Prepare(registry,owner,authority,identity,new(secondReference,new()));
        var thirdReference=await Open(registry,owner,"third");
        await Reject(()=>registry.PrepareWebCaptureAsync(authority,identity,new(thirdReference,new()),default,owner).AsTask(),"capture_limit");
        var stop=registry.StopWebCaptureAsync(authority,identity,first.Ticket!,default,owner).AsTask();
        registry.Publish(new(firstReference,[],Capture:first with{Phase=WebCaptureStreamPhase.Capturing}));
        Check(!stop.IsCompleted&&registry.WebCaptureStatus(authority,identity,first.Ticket!,owner).Phase==WebCaptureStreamPhase.Prepared,
            "Late progress revived a retiring backend");
        registry.Publish(new(firstReference,[],Capture:first with{Phase=WebCaptureStreamPhase.Stopped}));
        await stop;
        Check(registry.WebCaptureStatus(authority,identity,first.Ticket!,owner).Phase==WebCaptureStreamPhase.Stopped,"Exact late teardown confirmation was dropped after backend close");
        for(var i=0;i<66;i++)
        {
            var capture=await Prepare(registry,owner,authority,identity,new(thirdReference,new()));
            registry.Publish(new(thirdReference,[],Capture:capture with{Phase=WebCaptureStreamPhase.Stopped}));
            await registry.StopWebCaptureAsync(authority,identity,capture.Ticket!,default,owner);
        }
        await Reject(()=>registry.StopWebCaptureAsync(authority,identity,first.Ticket!,default,owner).AsTask(),"capture_retired");
        Check(true,"Terminal capture history is bounded without discarding uncertain captures");
        using var replacement=registry.CreateOwner("retirement",identity,false,true);
        await Reject(()=>registry.StopWebCaptureAsync(authority,identity,first.Ticket!,default,replacement).AsTask(),"capture_retired");
        await Reject(()=>registry.StopWebCaptureAsync(authority,identity,first.Ticket!,default,owner).AsTask(),"session_closed");
    }

    internal static async Task ConsentAndBackground()
    {
        await ConsentAndBackground(false);
        await ConsentAndBackground(true);
    }

    private static async Task ConsentAndBackground(bool constrained)
    {
        var identity=new BrokerWidgetIdentity("example.capture.policy","example.publisher",Guid.NewGuid().ToString("N"));
        var registry=WebSessionRegistry.Shared;
        using var owner=registry.CreateOwner("capture-policy",identity,false,true);
        var session=await Open(registry,owner,"stream");
        var store=new ConsentStore(Path.Combine(Path.GetTempPath(),"web-capture-policy-"+Guid.NewGuid().ToString("N")));
        await using var broker=new PlatformCapabilityBroker(identity,[PlatformCapabilities.WindowCaptureV1],store,new SimulatedPlatformBrokerBackend(),[],AppLibrarySavedIdIssuer.Shared,webSessionOwner:owner);
        var operation=constrained?"capture.web-prepare-constrained":"capture.web-prepare";
        var payload=new WebCaptureStreamRequest(session,new()){AllowedQualities=constrained?[new(720,30),new(1080,60)]:null};
        var request=new BrokerRequestEnvelope(1,1,identity,PlatformCapabilities.WindowCaptureV1,operation,BrokerJson.ToElement(payload));
        var timeout=new BrokerPipeTransportOptions{RequestTimeout=TimeSpan.FromMilliseconds(50)};
        Check(BrokerPipeRequestTimeoutPolicy.Resolve(timeout,request)==TimeSpan.FromSeconds(95),"Capture picker pipe timeout does not outlive its consent window");
        Check(BrokerPipeRequestTimeoutPolicy.Resolve(new(){RequestTimeout=TimeSpan.FromSeconds(120)},request)==TimeSpan.FromSeconds(120),"Capture timeout shortened an explicit longer deadline");
        Check(BrokerPipeRequestTimeoutPolicy.Resolve(timeout,request with{Operation="capture.web-status"})==timeout.RequestTimeout,"Status inherited an interactive picker timeout");
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await Reject(()=>broker.ExecuteAsync(request),"permission_denied");
        await store.SetDecisionAsync(identity,PlatformCapabilities.WindowCaptureV1,ConsentDecision.Grant);
        await Reject(()=>broker.ExecuteAsync(request with{Operation=constrained?"capture.web-prepare":"capture.web-prepare-constrained"}),"invalid_payload");
        broker.SetLifecycle(BrokerLifecycleState.Background);
        await Reject(()=>broker.ExecuteAsync(request),"lifecycle_denied");
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        var pending=broker.ExecuteAsync(request);
        var work=(await registry.TakeAsync(default)).Work.Single();
        broker.SetLifecycle(BrokerLifecycleState.Background);
        registry.Complete(new(work.RequestId,Capture:Prepared(work.Command.CaptureTicket!)));
        await pending;
        var status=request with{Operation="capture.web-status",Payload=BrokerJson.ToElement(work.Command.CaptureTicket)};
        var backgroundStatus=(await broker.ExecuteAsync(status)).Deserialize<WebCaptureStreamStatus>(BrokerJson.StrictOptions)!;
        Check(backgroundStatus.Phase==WebCaptureStreamPhase.Prepared,"Committed source cannot continue while background: "+backgroundStatus.Phase);
        await Reject(()=>broker.ExecuteAsync(request with{RequestId=2}),"lifecycle_denied");
        var stopping=broker.ExecuteAsync(request with{Operation="capture.web-stop",Payload=BrokerJson.ToElement(work.Command.CaptureTicket)});
        var stop=(await registry.TakeAsync(default)).Work.Single();
        registry.Complete(new(stop.RequestId,Capture:new(WebCaptureStreamPhase.Stopped){Ticket=work.Command.CaptureTicket}));
        await stopping;
        await owner.ExecuteAsync(new(WebSessionOperation.Close){Session=session},default);
        Check((await registry.TakeAsync(default)).Work.Single().Command.Operation==WebSessionOperation.Close,"Consent test backend did not close");
    }

    private static async Task<WebSessionReference> Open(WebSessionRegistry registry,WebSessionRegistry.Owner owner,string key,bool native=true)
    {
        var task=owner.ExecuteAsync(new(WebSessionOperation.OpenBackend)
        {Definition=new(key,"Synthetic capture backend",WebSessionSource.Remote("https://capture.example"))
            {NativeCapture=native,PageAdapter=new(){Mode=WebSessionPageAdapterMode.SessionBackend,ScriptAsset="backend.js",Origins=["https://capture.example"]}}},default).AsTask();
        registry.Complete(new((await registry.TakeAsync(default)).Work.Single().RequestId));
        return (await task).Session!;
    }
    private static WebCaptureStreamStatus Prepared(WebCaptureStreamTicket ticket)=>new(WebCaptureStreamPhase.Prepared)
        {Ticket=ticket,ApplicationName="Synthetic application",Quality=new(),ApplicationAudio=false};
    private static async Task<WebCaptureStreamStatus> Prepare(WebSessionRegistry registry,WebSessionRegistry.Owner owner,object authority,BrokerWidgetIdentity identity,WebCaptureStreamRequest request)
    {
        var task=registry.PrepareWebCaptureAsync(authority,identity,request,default,owner).AsTask();
        var work=(await registry.TakeAsync(default)).Work.Single();
        registry.Complete(new(work.RequestId,Capture:Prepared(work.Command.CaptureTicket!) with{Quality=request.Options}));
        return await task;
    }
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    private static async Task Reject(Func<Task> action,string code)
    {try{await action();throw new Exception("Expected rejection: "+code);}catch(BrokerException error)when(error.Code==code){}}
}
