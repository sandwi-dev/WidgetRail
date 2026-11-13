using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class WebSessionMicrophoneTests
{
    internal static async Task Run()
    {
        var registry=new WebSessionRegistry();
        var identity=new BrokerWidgetIdentity("example.widget","example.publisher","instance");
        using var owner=registry.CreateOwner("widget",identity,false,true);
        var definition=new WebSessionDefinition("backend","Synthetic backend",WebSessionSource.Remote("https://example.com/app"))
            {PageAdapter=new(){Mode=WebSessionPageAdapterMode.SessionBackend,ScriptAsset="backend.js",Origins=["https://example.com"]}};
        var opening=owner.ExecuteAsync(new(WebSessionOperation.OpenBackend){Definition=definition},default).AsTask();
        registry.Complete(new((await registry.TakeAsync(default)).Work.Single().RequestId));
        var reference=(await opening).Session!;
        var command=new WebSessionCommand(WebSessionOperation.RequestMicrophone){Session=reference};
        using var cancel=new CancellationTokenSource();
        var first=owner.ExecuteAsync(command,cancel.Token).AsTask();
        var pending=(await registry.TakeAsync(default)).Work.Single();
        await Reject(()=>owner.ExecuteAsync(command,default).AsTask(),"request_limit");
        cancel.Cancel();
        try{await first;throw new Exception("Cancelled consent returned success");}catch(OperationCanceledException){}
        var cancellation=await registry.TakeAsync(default);
        Check(cancellation.Work.Single().Cancelled && cancellation.Work.Single().RequestId==pending.RequestId,"Cancellation did not wake the native owner");
        Check(cancellation.LiveRequests?.Contains(pending.RequestId)==false,"Cancelled consent remained in native request authority");
        registry.Complete(new(pending.RequestId,Microphone:new(WebSessionMicrophoneState.Granted,true)));
        Check(!registry.IsCurrent(pending),"Late consent revived retired authority");
        using(var earlyCancel=new CancellationTokenSource())
        {
            var early=owner.ExecuteAsync(command,earlyCancel.Token).AsTask();earlyCancel.Cancel();
            try{await early;throw new Exception("Early cancellation succeeded");}catch(OperationCanceledException){}
            var earlyBatch=await registry.TakeAsync(default);
            Check(earlyBatch.Work.Count==1&&earlyBatch.Work[0].Cancelled,"Cancellation before delivery still exposed an actionable prompt");
        }
        var second=owner.ExecuteAsync(command,default).AsTask();
        var next=(await registry.TakeAsync(default)).Work.Single();
        registry.Complete(new(next.RequestId,Microphone:new(WebSessionMicrophoneState.Denied,true)));
        Check((await second).Microphone==new WebSessionMicrophoneResult(WebSessionMicrophoneState.Denied,true),"Typed remembered denial was lost");
        var malformed=owner.ExecuteAsync(command,default).AsTask();
        next=(await registry.TakeAsync(default)).Work.Single();registry.Complete(new(next.RequestId));
        await Reject(()=>malformed,"invalid_host_result");

        using var other=registry.CreateOwner("other",new("example.other","example.publisher","other"),false,true);
        await Reject(()=>other.ExecuteAsync(command,default).AsTask(),"session_closed");
        var plain=owner.ExecuteAsync(new(WebSessionOperation.Open){Definition=new("plain","Plain",WebSessionSource.Remote("https://example.com"))},default).AsTask();
        registry.Complete(new((await registry.TakeAsync(default)).Work.Single().RequestId));
        var plainReference=(await plain).Session;
        await Reject(()=>owner.ExecuteAsync(command with{Session=plainReference},default).AsTask(),"permission_denied");

        var store=new ConsentStore(Path.Combine(Path.GetTempPath(),"web-microphone-tests-"+Guid.NewGuid().ToString("N")));
        await using var broker=new PlatformCapabilityBroker(identity,[],store,new SimulatedPlatformBrokerBackend(),
            [PlatformCapabilities.WebSessionsV1],AppLibrarySavedIdIssuer.Shared,webSessions:owner.ExecuteAsync);
        var request=new BrokerRequestEnvelope(1,1,identity,PlatformCapabilities.WebSessionsV1,"web-session.microphone",BrokerJson.ToElement(command));
        foreach(var state in new[]{BrokerLifecycleState.Visible,BrokerLifecycleState.Background})
        {
            broker.SetLifecycle(state);
            await Reject(()=>broker.ExecuteAsync(request),"lifecycle_denied");
            await Reject(()=>broker.ExecuteAsync(request with{Operation="web-session.command"}),"invalid_payload");
        }
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        var active=broker.ExecuteAsync(request);
        pending=(await registry.TakeAsync(default)).Work.Single();
        broker.SetLifecycle(BrokerLifecycleState.Background);
        try{await active;throw new Exception("Input loss did not cancel pending consent");}catch(BrokerException error) when(error.Code=="lifecycle_denied"){}
        cancellation=await registry.TakeAsync(default);
        Check(cancellation.Work.Single().Cancelled,"Broker lifecycle loss did not retire the native dialog");
        var domain=new WebSessionCapabilityDomain(owner.ExecuteAsync);
        await Reject(()=>domain.ExecuteAsync("web-session.microphone",BrokerJson.ToElement(new WebSessionCommand(WebSessionOperation.ReadMessages){Session=reference}),default).AsTask(),"invalid_payload");
        var permission = new WebSessionCommand(WebSessionOperation.RequestPermission) { Session=reference, PermissionKind=WebSessionPermissionKind.Notifications };
        await Reject(()=>owner.ExecuteAsync(permission with {PermissionKind=null}, default).AsTask(),"invalid_payload");
        await Reject(()=>owner.ExecuteAsync(permission with {PermissionKind=(WebSessionPermissionKind)999}, default).AsTask(),"invalid_payload");
        await Reject(()=>owner.ExecuteAsync(command with {PermissionKind=WebSessionPermissionKind.Notifications}, default).AsTask(),"invalid_payload");
        await Reject(()=>other.ExecuteAsync(permission, default).AsTask(),"session_closed");
        await Reject(()=>owner.ExecuteAsync(permission with {Session=plainReference},default).AsTask(),"permission_denied");
        var nativePermission = request with {Operation="web-session.permission",Payload=BrokerJson.ToElement(permission)};
        await Reject(()=>broker.ExecuteAsync(nativePermission),"lifecycle_denied");
        await Reject(()=>domain.ExecuteAsync("web-session.command",nativePermission.Payload,default).AsTask(),"invalid_payload");
        await Reject(()=>domain.ExecuteAsync("web-session.microphone",nativePermission.Payload,default).AsTask(),"invalid_payload");
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        var setup=broker.ExecuteAsync(nativePermission);
        var work=(await registry.TakeAsync(default)).Work.Single();
        await Reject(()=>owner.ExecuteAsync(command,default).AsTask(),"request_limit");
        registry.Complete(new(work.RequestId,Permission:new(WebSessionPermissionState.Granted,true)));
        Check(BrokerJson.ParsePayload<WebSessionReply>(await setup).Permission==new WebSessionPermissionResult(WebSessionPermissionState.Granted,true),
            "Notification decision did not reach the requesting widget");
        var mismatched=owner.ExecuteAsync(permission,default).AsTask();
        work=(await registry.TakeAsync(default)).Work.Single();
        registry.Complete(new(work.RequestId,Microphone:new(WebSessionMicrophoneState.Granted,true)));
        await Reject(()=>mismatched,"invalid_host_result");
        var retiring=owner.ExecuteAsync(command,default).AsTask();
        pending=(await registry.TakeAsync(default)).Work.Single();owner.Dispose();
        await Reject(()=>retiring,"session_closed");
        var retired=await registry.TakeAsync(default);
        Check(retired.LiveRequests?.Contains(pending.RequestId)==false&&!registry.IsOwned("widget",reference),"Owner retirement retained consent authority");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Reject(Func<Task> action,string code)
    {try{await action();throw new Exception("Expected rejection: "+code);}catch(BrokerException error)when(error.Code==code){}}
}
