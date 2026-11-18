using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetBridge;

internal static class LinkedRecycleTests
{
    private static readonly BrokerWidgetIdentity Owner=new("example.provider","example.publisher","provider.instance");
    private static readonly BrokerWidgetIdentity Caller=new("example.library","example.publisher","library.instance");
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static CaptureLibraryMutationReference Reference(CaptureLibraryItem item)=>new(item.Id,item.Revision,item.MetadataRevision);

    internal static async Task RemovalAndRecovery()
    {
        using var f=new Fixture();var first=await f.Add("first.png");var second=await f.Add("second.png");
        var old=await f.Store.RecycleAsync(new([Reference(first)]),default);
        Check(old.Results.Single().ErrorCode=="capture_library_read_only_source"&&f.Calls==0,"Legacy permission recycled linked originals");
        await f.Store.RemoveLinkAsync(Reference(first),default);
        Check(File.Exists(f.Path("first.png"))&&f.Calls==0,"Remove from library touched the original");
        second=await f.Store.UpdateMetadataAsync(new(second.Id,second.MetadataRevision,"Retained title",true),default);
        var result=await f.Store.RecycleLinkedAsync(new([Reference(second)]),default);
        Check(result.Results.Single().ErrorCode is null&&f.Calls==1&&!File.Exists(f.Path("second.png")),"Explicit linked recycle failed");
        Check(!(await f.Store.QueryAsync(new(new()),default)).Items.Any(item=>item.Id==second.Id),"Recycled original stayed indexed");
        File.Move(f.Bin("second.png"),f.Path("second.png"));
        var restored=(await f.Store.LinkAsync(new(f.Source!.Id,"second.png"),Owner,default)).Item;
        Check(restored.Id==second.Id&&restored.Title=="Retained title"&&restored.IsFavorite,"Restored original lost its library metadata");
        Check(File.Exists(f.Path("first.png")),"Recycle changed a separately unlinked original");
    }

    internal static async Task IdentityAndPartialResults()
    {
        using var f=new Fixture();var first=await f.Add("first.png");var second=await f.Add("second.png");
        var stale=await f.Store.RecycleLinkedAsync(new([Reference(first) with{MetadataRevision=first.MetadataRevision+1}]),default);
        Check(stale.Results.Single().ErrorCode=="capture_library_metadata_conflict"&&f.Calls==0,"Stale metadata reached file operation");
        File.AppendAllText(f.Path("first.png"),"changed");
        var changed=await f.Store.RecycleLinkedAsync(new([Reference(first)]),default);
        Check(changed.Results.Single().ErrorCode is not null&&f.Calls==0,"Changed file incarnation reached recycle");
        var third=await f.Add("third.png");
        f.Store.LinkedOriginalRecycler=(path,current,token)=>
        {
            f.Calls++;Check(current(),"Current original failed validation");
            if(path==f.Path("third.png"))return Task.FromResult(false);
            File.Move(path,f.Bin(System.IO.Path.GetFileName(path)));return Task.FromResult(true);
        };
        var batch=await f.Store.RecycleLinkedAsync(new([Reference(second),Reference(third)]),default);
        Check(batch.Results[0].ErrorCode is null&&batch.Results[1].ErrorCode=="capture_library_recycle_unavailable"&&
            !File.Exists(f.Path("second.png"))&&File.Exists(f.Path("third.png")),"Partial recycle rolled back success or deleted an unsupported original");
        await f.Store.RemoveSourceAsync(f.Source!.Id,Owner,default);
        var calls=f.Calls;var disconnected=await f.Store.RecycleLinkedAsync(new([Reference(third)]),default);
        Check(disconnected.Results.Single().ErrorCode is not null&&f.Calls==calls&&File.Exists(f.Path("third.png")),"Disconnected source retained recycle authority");
    }

    internal static async Task PermissionAndRevocation()
    {
        using var f=new Fixture();var first=await f.Add("first.png");var second=await f.Add("second.png");
        var consent=new ConsentStore(System.IO.Path.Combine(f.Root,"consent"));
        var simulation=new SimulatedPlatformBrokerBackend();
        await using var backend=new CompositePlatformBrokerBackend(simulation,simulation,captureLibrary:f.Store);
        await using var broker=new PlatformCapabilityBroker(Caller,[PlatformCapabilities.CaptureLibraryRecycleLinkedV1],consent,backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);long sequence=0;
        Task<System.Text.Json.JsonElement> Call(CaptureLibraryItem item)=>broker.ExecuteAsync(new(BrokerJson.ProtocolVersion,++sequence,Caller,
            PlatformCapabilities.CaptureLibraryRecycleLinkedV1,"capture-library.recycle-linked",BrokerJson.ToElement(new CaptureLibraryRecycleRequest([Reference(item)]))));
        await consent.SetDecisionAsync(Caller,PlatformCapabilities.CaptureLibraryManageV1,ConsentDecision.Grant);
        await Reject(()=>Call(first));Check(f.Calls==0,"Unapproved linked recycle invoked OS primitive");
        await consent.SetDecisionAsync(Caller,PlatformCapabilities.CaptureLibraryRecycleLinkedV1,ConsentDecision.Grant);
        Check((await f.Store.SourcesAsync(Caller,default)).Items.Count==0,"Read-only source grant widened to another widget");
        var result=BrokerJson.ParsePayload<CaptureLibraryBatchResult>(await Call(first));
        Check(result.Results.Single().ErrorCode is null&&!File.Exists(f.Path("first.png")),"Explicit permission could not recycle another source owner's indexed original");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var committed=false;
        f.Store.LinkedOriginalRecycler=async(path,current,token)=>{entered.TrySetResult();await release.Task;if(!current())return false;committed=true;File.Move(path,f.Bin(System.IO.Path.GetFileName(path)));return true;};
        var pending=Call(second);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await consent.SetDecisionAsync(Caller,PlatformCapabilities.CaptureLibraryRecycleLinkedV1,ConsentDecision.Deny);
        await broker.RefreshConsentAsync();release.TrySetResult();
        var cancelled=BrokerJson.ParsePayload<CaptureLibraryBatchResult>(await pending);
        Check(cancelled.Results.Single().ErrorCode=="cancelled","Revoked batch did not report its cancelled item");
        Check(!committed&&File.Exists(f.Path("second.png")),"Late recycle ignored permission revocation");
    }

    internal static async Task LateIdentityAndCancellation()
    {
        using var f=new Fixture();var first=await f.Add("first.png");var second=await f.Add("second.png");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var committed=false;
        f.Store.LinkedOriginalRecycler=async(path,current,token)=>{entered.TrySetResult();await release.Task;if(!current())return false;committed=true;File.Move(path,f.Bin(System.IO.Path.GetFileName(path)));return true;};
        var pending=f.Store.RecycleLinkedAsync(new([Reference(first)]),default);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        File.Move(f.Path("first.png"),f.Path("original.png"));f.Write("first.png");release.TrySetResult();
        Check((await pending).Results.Single().ErrorCode is not null&&!committed&&File.Exists(f.Path("original.png"))&&File.Exists(f.Path("first.png")),
            "Queued recycle deleted a replacement at the same path");
        using var cancel=new CancellationTokenSource();
        f.Store.LinkedOriginalRecycler=(path,current,token)=>{Check(current(),"Current file rejected");File.Move(path,f.Bin(System.IO.Path.GetFileName(path)));cancel.Cancel();throw new OperationCanceledException(token);};
        var third=await f.Add("third.png");
        var batch=await f.Store.RecycleLinkedAsync(new([Reference(second),Reference(third)]),cancel.Token);
        Check(batch.Results[0].ErrorCode is null&&batch.Results[1].ErrorCode=="cancelled"&&!File.Exists(f.Path("second.png"))&&File.Exists(f.Path("third.png")),
            "Cancellation forgot a committed OS recycle or processed later items");
    }

    internal static async Task SdkRouting()
    {
        var reference=new CaptureLibraryMutationReference(new string('a',32),7,9);var calls=0;
        var services=new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.RecycleLinked,(request,_)=>
        {calls++;Check(request.Items.SequenceEqual(new[]{reference}),"SDK changed exact mutation revisions");return ValueTask.FromResult(new CaptureLibraryBatchResult([new(reference.Id)]));}).Build();
        Check(WidgetCaptureLibraryCapabilities.RecycleLinked.CapabilityId==WidgetCaptureLibraryCapabilities.RecycleLinkedPermission,"SDK reused old Manage permission");
        await services.CaptureLibrary.RecycleLinkedAsync([reference]);
        try{await services.CaptureLibrary.RecycleLinkedAsync([reference,reference]);throw new Exception("Duplicate batch accepted");}catch(ArgumentException){}
        Check(calls==1,"Invalid SDK payload reached the broker");
    }

    internal static async Task PathFences()
    {
        using var f=new Fixture();var item=await f.Add("first.png");
        var outside=System.IO.Path.Combine(f.Root,"outside.png");File.WriteAllText(outside,"outside source");
        File.Move(f.Path("first.png"),f.Path("original.png"));
        try
        {
            File.CreateSymbolicLink(f.Path("first.png"),outside);
            var linked=await f.Store.RecycleLinkedAsync(new([Reference(item)]),default);
            Check(linked.Results.Single().ErrorCode is not null&&f.Calls==0&&File.ReadAllText(outside)=="outside source",
                "Reparse-point replacement authorized external deletion");
        }
        finally{if(File.Exists(f.Path("first.png")))File.Delete(f.Path("first.png"));}
        File.Move(f.Path("original.png"),f.Path("first.png"));
        var metadata=System.IO.Path.Combine(f.Root,"index",item.Id+".json");
        var json=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(metadata))!;
        json["RelativePath"]="../outside.png";await File.WriteAllTextAsync(metadata,json.ToJsonString());
        var escaped=await f.Store.RecycleLinkedAsync(new([Reference(item)]),default);
        Check(escaped.Results.Single().ErrorCode is not null&&f.Calls==0&&File.Exists(outside),"Corrupt relative path escaped source containment");
    }
    internal static async Task NativeGuardRejectsBeforeMutation()
    {
        using var f=new Fixture();f.Write("guard.png");var checkedCurrent=false;
        var removed=await WindowsFileOperations.RecycleLinkedAsync(f.Path("guard.png"),()=>{checkedCurrent=true;return false;},default);
        Check(checkedCurrent&&!removed&&File.Exists(f.Path("guard.png")),"Native shell recycle ignored the final authority guard");
    }
    internal static async Task NativeRecycle(string output)
    {
        output=System.IO.Path.GetFullPath(output);
        if(File.Exists(output))throw new ArgumentException("Use a fresh native result path.");
        using var f=new Fixture();var name="WidgetRail-owned-recycle-check-"+Guid.NewGuid().ToString("N")+".png";
        var item=await f.Add(name);var guards=0;
        f.Store.LinkedOriginalRecycler=(path,current,token)=>WindowsFileOperations.RecycleLinkedAsync(path,()=>{guards++;return current();},token);
        var result=await f.Store.RecycleLinkedAsync(new([Reference(item)]),default);
        var missing=!File.Exists(f.Path(name));
        var indexed=(await f.Store.QueryAsync(new(new()),default)).Items.Any(value=>value.Id==item.Id);
        var passed=result.Results.Single().ErrorCode is null&&guards==1&&missing&&!indexed;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output,System.Text.Json.JsonSerializer.Serialize(new{passed,guards,originalMissing=missing,indexed,
            error=result.Results.Single().ErrorCode,fixtureFile=name,scope="One uniquely-owned 12-byte fixture, production store pins and Windows ITransferSource, no permanent-delete fallback."}));
        Check(passed,"Actual linked-file OS recycle failed; inspect native evidence.");
        Console.WriteLine("PASS actual Windows recycle with exact identity guard and retained directory pins");
    }

    private static async Task Reject(Func<Task> action)
    {try{await action();}catch(Exception error) when(error is BrokerException or OperationCanceledException){return;}throw new Exception("Expected permission rejection");}
    private sealed class Fixture:IDisposable
    {
        internal string Root{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wrail-linked-recycle-"+Guid.NewGuid().ToString("N"));
        internal CaptureLibraryStore Store{get;}
        internal CaptureLibrarySource? Source;
        internal int Calls;
        internal Fixture()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Root,"source"));Directory.CreateDirectory(System.IO.Path.Combine(Root,"bin"));
            Store=new(System.IO.Path.Combine(Root,"index"),System.IO.Path.Combine(Root,"owned"),null,null,
                (path,seed,_)=>Task.FromResult(seed with{Width=16,Height=16,ByteLength=new FileInfo(path).Length}),
                (_,_)=>throw new Exception("Legacy recycle delegate must not handle linked originals"));
            Store.LinkedOriginalRecycler=(path,current,token)=>{Calls++;Check(current(),"OS guard rejected current original");File.Move(path,Bin(System.IO.Path.GetFileName(path)));return Task.FromResult(true);};
        }
        internal string Path(string name)=>System.IO.Path.Combine(Root,"source",name);
        internal string Bin(string name)=>System.IO.Path.Combine(Root,"bin",name);
        internal void Write(string name)=>File.WriteAllBytes(Path(name),[137,80,78,71,13,10,26,10,1,2,3,4]);
        internal async Task<CaptureLibraryItem> Add(string name)
        {Write(name);Source??=await Store.AddSourceAsync(System.IO.Path.Combine(Root,"source"),"Linked captures",Owner,default);return (await Store.LinkAsync(new(Source.Id,name),Owner,default)).Item;}
        public void Dispose(){Store.Dispose();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
}
