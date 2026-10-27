using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class PinningEffectTests
{
    internal static async Task Contract()
    {
        var identity=new BrokerWidgetIdentity("example.widget","example.publisher","instance");
        var consent=new ConsentStore(Path.Combine(Path.GetTempPath(),"pin-effects-"+Guid.NewGuid().ToString("N")));
        var effects=new List<BrokerHostEffect>();
        await using var companion=new BrokerWidgetProcessCompanion(identity.PackageId,identity.PublisherId,identity.InstanceId,[],consent,
            new SimulatedPlatformBrokerBackend(),new(WidgetWorkerIsolationPolicy.HostTrustedJobOnly,null,null),hostEffectSink:effects.Add);
        await companion.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server=companion.RunAsync(lifetime.Token);
        var arguments=companion.WorkerArguments.ToArray();
        string Argument(string name)=>arguments[Array.IndexOf(arguments,name)+1];
        await using var client=new BrokerPipeClient(Argument("--broker-pipe"),identity,Argument("--broker-nonce"));
        await client.ConnectAsync(lifetime.Token);
        var noAction=await client.RequestAsync(PlatformCapabilities.WidgetPinningV1,"pin.open",new WidgetPinRequest("stream"),lifetime.Token);
        Require(noAction.ErrorCode=="action_context_required","An out-of-action request can create a pin");
        async Task<BrokerResponseEnvelope> Pin(long action)=>await client.RequestWithActionAsync(PlatformCapabilities.WidgetPinningV1,"pin.open",new WidgetPinRequest("stream"),null,null,action,lifetime.Token);
        void Complete(long id,WidgetActionExecutionOutcome outcome)=>((IWidgetActionEffectCoordinator)companion).CompleteAction(new(id,outcome));
        Require((await Pin(1)).Succeeded && effects.Count==0,"Effect escaped before action completion");
        Complete(1,WidgetActionExecutionOutcome.Succeeded);
        Require(effects.Count==1 && effects[0].Kind==BrokerHostEffectKind.OpenPinnedLayout && effects[0].PinnedLayoutId=="stream","Completed pin action lost its identity");
        Complete(1,WidgetActionExecutionOutcome.Succeeded);
        Require(effects.Count==1,"Terminal replay opened another pin");
        Require(!(await Pin(1)).Succeeded,"Retired action was admitted again");
        Require((await Pin(2)).Succeeded,"Failure test did not admit its request");
        Complete(2,WidgetActionExecutionOutcome.Failed);
        Require(effects.Count==1,"Failed action opened a pin");
        Require((await Pin(3)).Succeeded,"Lifecycle test did not admit its request");
        await companion.SetLifecycleStateAsync(WidgetLifecycleState.Background);
        Complete(3,WidgetActionExecutionOutcome.Succeeded);
        Require(effects.Count==1,"Background transition preserved a pending pin request");
        await companion.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        var adapter=new BrokerWidgetCapabilityClient(client);
        var invocation=new WidgetActionInvocationState(4);
        using(WidgetActionInvocationContext.Enter(invocation))
            Require((await adapter.InvokeAsync(WidgetPinningCapabilities.Open,new WidgetPinRequest("stream"))).Accepted,"SDK runtime adapter omitted the pin action context");
        Require(invocation.HadHostEffectRequest && effects.Count==1,"Runtime did not retain the pin's terminal-success barrier");
        Complete(4,WidgetActionExecutionOutcome.Succeeded);
        Require(effects.Count==2,"SDK runtime pin did not reach the deferred effect");
        try{await adapter.InvokeAsync(WidgetPinningCapabilities.Open,new WidgetPinRequest("stream"));throw new Exception("Out-of-action runtime call accepted");}
        catch(WidgetCapabilityException error) when(error.ErrorCode=="action_context_required") { }
        lifetime.Cancel();
        try{await server;}catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { }
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
