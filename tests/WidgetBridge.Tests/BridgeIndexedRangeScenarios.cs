using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetRuntime;
using System.IO.Pipes;
using System.Text;

internal static class BridgeIndexedRangeScenarios
{
    private static ConfiguredWidget Configured(char version = 'a') => new()
    {
        Id = "indexed", PackageId = "dev.indexed", PublisherId = "dev", Name = "Indexed", InstanceId = "indexed.instance",
        WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new(version,64), CatalogFingerprint = new(version,64),
    };
    private static ViewSnapshot Snapshot(ConfiguredWidget configured, IndexedCollectionDescriptor source, long sequence) => new()
    {
        WidgetInstanceId = configured.InstanceId, Sequence = sequence, ActiveInputScopeId = "root", InitialFocusId = "button",
        Root = new() { Id = "root", Kind = ViewNodeKind.Stack, Children = [
            new() { Id = "button", Kind = ViewNodeKind.Button, Text = "Action", ActionId = "action" },
            new() { Id = "items", Kind = ViewNodeKind.IndexedCollection, IndexedCollection = source,
                ScrollAxis = ScrollAxis.Vertical, AccessibilityLabel = "Items", CollectionLayout = new() { Kind = CollectionLayoutKind.List, EstimatedItemExtent = 64 } },
        ] },
    };
    private static BridgeIndexedRangeRequest Request(ConfiguredWidget configured, IndexedCollectionDescriptor source, string demand = "demand")
    {
        var d = configured.PublicDescriptor();
        return new(d.Id,d.InstanceId,d.RuntimeGeneration,d.PresentationGeneration,new("items",source,0,1,demand));
    }
    private static IndexedCollectionRange Range(BridgeIndexedRangeRequest request) => new(request.InstanceId, request.Range.CollectionId,
        request.Range.Source,"root",request.Range.StartIndex,request.Range.DemandId,
        [new("item.0",new() { Id = "item.0",Kind = ViewNodeKind.Button,Text = "Item",ActionId = "item.action",CollectionItemKey = "item.0" })]);

    internal static async Task SlowReadsPreserveSerialWork()
    {
        var configured = Configured(); var source = new IndexedCollectionDescriptor("source",1,0,100);
        var started = NewSignal(); var release = NewSignal();
        await using var fixture = new RegistryFixture(new([configured]), configure: (_, client) =>
        {
            client.SnapshotFactory = sequence => Snapshot(configured,source,sequence);
            client.IndexedReader = async (request, token) => { started.TrySetResult(); await release.Task.WaitAsync(token); return Range(Request(configured,request.Source,request.DemandId)); };
        });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        _ = await fixture.GetSnapshotAsync(configured.Id);
        var request = Request(configured,source);
        var read = fixture.Registry.ReadIndexedRangeAsync(request,CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        _ = await fixture.GetSnapshotAsync(configured.Id).WaitAsync(TimeSpan.FromSeconds(2));
        using (var action = await fixture.Registry.AdmitActionAsync(configured.Id,new("action","button"),CancellationToken.None,CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)))
            Check(action.Value == WidgetOperationAdmission.Started || action.Value == WidgetOperationAdmission.Enqueued,"Action should remain admitted.");
        Check(!read.IsCompleted,"Read should still be held.");
        Check(await fixture.Registry.CancelIndexedRangeAsync(request),"Exact cancel should find read.");
        await Reject(read);
        Check(fixture.Clients[0].Starts == 1,"Cancel cannot restart worker.");
        release.TrySetResult();
        using var current = await fixture.Registry.ReadIndexedRangeAsync(request with { Range = request.Range with { DemandId = "next" } },CancellationToken.None);
        Check(current.Value.Range.Items.Count == 1,"Recovery read should succeed.");
    }

    internal static async Task QueryAndWorkerRetirementAreExact()
    {
        var configured = Configured(); var source = new IndexedCollectionDescriptor("source",1,0,100);
        var started = NewSignal(); var drained = NewSignal();
        await using var fixture = new RegistryFixture(new([configured]), configure: (_, client) =>
        {
            client.SnapshotFactory = sequence => Snapshot(configured,source,sequence);
            client.IndexedReader = async (_, token) =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite,token); throw new InvalidOperationException(); }
                finally { drained.TrySetResult(); Check(client.DisposeCount == 0,"Client disposed during read."); }
            };
        });
        await fixture.SetLifecycleAsync(configured.Id,WidgetLifecycleState.Interactive); _ = await fixture.GetSnapshotAsync(configured.Id);
        var request = Request(configured,source);
        var read = fixture.Registry.ReadIndexedRangeAsync(request,CancellationToken.None); await started.Task;
        source = source with { QueryGeneration = 2 };
        _ = await fixture.GetSnapshotAsync(configured.Id);
        await Reject(read); await drained.Task;
        var read2 = fixture.Registry.ReadIndexedRangeAsync(Request(configured,source,"second"),CancellationToken.None);
        var replacement = Configured('b');
        fixture.Registry.ApplyCatalog(new([replacement]),1);
        await Reject(read2);
        await fixture.Clients[0].Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        Check(!await fixture.Registry.CancelIndexedRangeAsync(request),"Old cancel must not create replacement.");
        Check(fixture.Clients.Count == 1,"Cancellation unexpectedly created worker.");
        await Reject(fixture.Registry.ReadIndexedRangeAsync(Request(replacement,source),CancellationToken.None));
        Check(fixture.Clients.Count == 1,"Read cannot create unpublished replacement.");
    }

    internal static async Task AdmissionAndDispatcherAreBounded()
    {
        var configured=Configured(); var source=new IndexedCollectionDescriptor("source",1,0,100);
        await using var fixture=new RegistryFixture(new([configured]),configure: (_,client)=>
        {
            client.SnapshotFactory=sequence=>Snapshot(configured,source,sequence);
            client.IndexedReader=async (_,token)=>{await Task.Delay(Timeout.Infinite,token); throw new InvalidOperationException();};
        });
        await fixture.SetLifecycleAsync(configured.Id,WidgetLifecycleState.Interactive); _=await fixture.GetSnapshotAsync(configured.Id);
        var reads=Enumerable.Range(0,4).Select(i=>fixture.Registry.ReadIndexedRangeAsync(Request(configured,source,"d"+i),CancellationToken.None)).ToArray();
        await Reject(fixture.Registry.ReadIndexedRangeAsync(Request(configured,source,"excess"),CancellationToken.None));
        for(var i=0;i<4;i++) Check(await fixture.Registry.CancelIndexedRangeAsync(Request(configured,source,"d"+i)),"Cancel missing.");
        foreach(var read in reads) await Reject(read);

        var held=NewSignal(); var entered=NewSignal();
        await using var dispatcher=new BridgeRequestDispatcher(CancellationToken.None,_=>{});
        var rangeKey=BridgeRequestClassifier.Classify(new(){Type=BridgeMessageTypes.ReadIndexedRange,RequestId=1,Payload=BridgeJson.ToElement(Request(configured,source))});
        Check(rangeKey.IsKnown && rangeKey.IsIndependent,"Range classifier must preserve independent scheduling.");
        var pending=dispatcher.TryDispatch(1,rangeKey,async token=>{entered.TrySetResult();await held.Task.WaitAsync(token);});
        await entered.Task;
        var action=dispatcher.TryDispatch(2,BridgeRequestKey.Widget(BridgeRequestKind.Action,configured.Id),_=>Task.CompletedTask);
        await action.Completion!.WaitAsync(TimeSpan.FromSeconds(2));
        var cancel=dispatcher.TryDispatch(3,BridgeRequestKey.Widget(BridgeRequestKind.CancelIndexedRange,configured.Id),_=>Task.CompletedTask);
        await cancel.Completion!.WaitAsync(TimeSpan.FromSeconds(2));
        held.TrySetResult(); await pending.Completion!;

        // Immediate cancel frames must await admission, even if worker tasks are
        // scheduled in the opposite order. They never wait for read completion.
        for (var index=0; index<20; index++)
        {
            var admitted=false;
            var release=NewSignal();
            var readId=10+index*2;
            var demand=Request(configured,source,"ordered"+index);
            var readDispatch=dispatcher.TryDispatch(readId,BridgeRequestKey.Indexed(BridgeRequestKind.ReadIndexedRange,demand),async token=>
            { admitted=true; await release.Task.WaitAsync(token); });
            var cancelDispatch=dispatcher.TryDispatch(readId+1,BridgeRequestKey.Indexed(BridgeRequestKind.CancelIndexedRange,demand),_=>
            { Check(admitted,"Cancel ran before read admission."); release.TrySetResult(); return Task.CompletedTask; });
            await Task.WhenAll(readDispatch.Completion!,cancelDispatch.Completion!).WaitAsync(TimeSpan.FromSeconds(2));
        }
        var blocked=NewSignal();
        for(var index=0;index<8;index++)
            Check(dispatcher.TryDispatch(100+index,rangeKey,token=>blocked.Task.WaitAsync(token)).Status==BridgeRequestDispatchStatus.Accepted,"Read admission missing.");
        Check(dispatcher.TryDispatch(108,rangeKey,_=>Task.CompletedTask).Status==BridgeRequestDispatchStatus.CapacityExceeded,"Global read capacity exceeded.");
        var ordinary=dispatcher.TryDispatch(109,BridgeRequestKey.Widget(BridgeRequestKind.Action,configured.Id),_=>Task.CompletedTask);
        await ordinary.Completion!.WaitAsync(TimeSpan.FromSeconds(2));
        blocked.TrySetResult();
    }

    internal static async Task RealWorkerReadDoesNotBlockAndRetires()
    {
        var configured = Configured() with { InstanceId = "indexed-real.instance" };
        WidgetProcessClient? process = null;
        await using var registry = new BridgeClientRegistry(new([configured]), new(),
            (value, reserve) => new WidgetProcessBridgeClient(process = new(new()
            {
                ExecutablePath = Environment.ProcessPath!, WidgetInstanceId = value.InstanceId,
                IsolationPolicy = WidgetWorkerIsolationPolicy.HostTrustedJobOnly, ProcessLeaseFactory = reserve,
                ConnectTimeout = TimeSpan.FromSeconds(3), RequestTimeout = TimeSpan.FromSeconds(2),
            })), (_,_)=>Task.CompletedTask, (_,_)=>Task.CompletedTask, (_,_)=>Task.CompletedTask);
        using (var visible = await registry.SetLifecycleAsync(configured.Id,WidgetLifecycleState.Interactive,CancellationToken.None,CancellationToken.None)) { }
        IndexedCollectionDescriptor source;
        using (var publication = await registry.GetSnapshotAsync(configured.Id,CancellationToken.None,CancellationToken.None))
            source = publication.Value.Snapshot.Root.Children.Single(node=>node.Id=="items").IndexedCollection!;
        var request=Request(configured,source);
        var read=registry.ReadIndexedRangeAsync(request,CancellationToken.None);
        using(var action=await registry.AdmitActionAsync(configured.Id,new("action","button"),CancellationToken.None,CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2))) { }
        using(var snapshot=await registry.GetSnapshotAsync(configured.Id,CancellationToken.None,CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2))) { }
        Check(!read.IsCompleted,"Slow real worker range should remain held.");
        Check(await registry.CancelIndexedRangeAsync(request),"Real demand cancellation missing."); await Reject(read);
        Check(process!.Starts==1 && process.IsRunning,"Cancellation restarted or stopped worker.");
        try
        {
            _=await process.ReadIndexedRangeAsync(request.Range,process.Starts+1,CancellationToken.None);
            throw new InvalidOperationException("Wrong worker ordinal was accepted.");
        }
        catch(InvalidOperationException exception) when(exception.Message.Contains("expected indexed worker",StringComparison.Ordinal)) { }
        var active=registry.ReadIndexedRangeAsync(request with {Range=request.Range with {DemandId="retire"}},CancellationToken.None);
        await registry.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
        await Reject(active);
        Check(!process.IsRunning,"Retired real worker is still running.");
    }

    internal static async Task ServerWireOrdersCancellationAndPublishesRanges()
    {
        var configured=Configured() with {InstanceId="indexed-real.instance"};
        var name="indexed-bridge-"+Guid.NewGuid().ToString("N");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var server=new WidgetBridgeServer(name,new([configured]));
        var serving=server.RunAsync(TimeSpan.FromSeconds(3),deadline.Token);
        await using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(deadline.Token);
        var channel=new BridgeFrameChannel(pipe,BridgeProtocol.DefaultMaximumMessageBytes);
        var received=new Dictionary<long,BridgeEnvelope>();
        async Task Send<T>(long id,string type,T payload)=>await channel.WriteAsync(new(){RequestId=id,Type=type,Payload=BridgeJson.ToElement(payload)},deadline.Token);
        async Task<BridgeEnvelope> Read(long id)
        {
            if(received.Remove(id,out var prior))return prior;
            while(true)
            {
                var response=await channel.ReadAsync(deadline.Token);
                if(response.RequestId==id)return response;
                if(response.RequestId!=0)received.Add(response.RequestId,response);
            }
        }
        await Send(1,BridgeMessageTypes.Hello,new BridgeHello("indexed-test")); _=await Read(1);
        await Send(2,BridgeMessageTypes.SetWidgetLifecycle,new BridgeWidgetLifecycleRequest(configured.Id,WidgetLifecycleState.Interactive)); _=await Read(2);
        await Send(3,BridgeMessageTypes.GetSnapshot,new BridgePresentationRequest(configured.Id));
        var snapshotReply=await Read(3);
        var snapshot=SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(snapshotReply.Payload.GetProperty("snapshot").GetRawText()));
        var source=snapshot.Root.Children.Single(node=>node.Id=="items").IndexedCollection!;
        var request=Request(configured,source);
        await Send(4,BridgeMessageTypes.ReadIndexedRange,request);
        await Send(5,BridgeMessageTypes.CancelIndexedRange,request);
        Check((await Read(5)).Payload.GetProperty("cancelled").GetBoolean(),"Early cancellation missed pending admission.");
        Check((await Read(4)).Type==BridgeMessageTypes.Error,"Cancelled range published content.");
        var next=request with {Range=request.Range with {DemandId="ready"}};
        await Send(6,BridgeMessageTypes.ReadIndexedRange,next);
        await Send(7,BridgeMessageTypes.Action,new BridgeActionRequest(configured.Id,new("release","release")));
        Check((await Read(7)).Type==BridgeMessageTypes.Acknowledged,"Action did not pass held read.");
        var ready=await Read(6);
        Check(ready.Type==BridgeMessageTypes.IndexedRange,"Missing range reply.");
        var range=BridgeJson.FromElement<BridgeIndexedRangeResponse>(ready.Payload);
        Check(range.WidgetId==configured.Id && range.InstanceId==configured.InstanceId && range.Range.DemandId=="ready","Range reply authority lost.");
        IndexedCollectionContract.ValidateRange(snapshot,next.Range,range.Range);
        await Send(8,BridgeMessageTypes.Stop,new{}); _=await Read(8);
        await serving.WaitAsync(TimeSpan.FromSeconds(4));
    }

    private static TaskCompletionSource NewSignal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Reject(Task<BridgeClientPublication<BridgeIndexedRangeResponse>> task)
    {
        try { using var result=await task.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("Expected rejected range."); }
        catch(BridgeProtocolException){}
    }
}

internal sealed class IndexedBridgeProbeWidget : Widget
{
    private readonly WidgetIndexedCollection<int,int> source;
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal IndexedBridgeProbeWidget()
    {
        source=CreateIndexedCollection<int,int>("source",0,100,new()
        {
            ReadRange=async (_,start,count,token)=>{await release.Task.WaitAsync(token);return Enumerable.Range(start,count).ToArray();},
            ItemKey=item=>new("item."+item), RenderItem=(_,item,context)=>UI.Button("Item "+item,"item.action",context.Id("root")),
        });
    }
    public override WidgetView Render()=>new(UI.Stack("root",UI.Button("Action","action","button"),UI.Button("Release","release","release"),UI.CollectionList("items",source,64,"Items")),"button");
    public override ValueTask OnActionAsync(WidgetActionEvent action,CancellationToken cancellationToken=default)
    { if(action.ActionId=="release")release.TrySetResult();return ValueTask.CompletedTask; }
}
