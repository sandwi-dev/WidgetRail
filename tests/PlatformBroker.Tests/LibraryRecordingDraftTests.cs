using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class LibraryRecordingDraftTests
{
    private static readonly BrokerWidgetIdentity Identity=new("example.recording","example.publisher","recording.instance");
    private static CaptureCapabilities Capabilities=>new(true,[CaptureImageFormat.Png],
        [new("software",1280,720,15,100_000,100_000_000,CaptureEncoderMode.Software)],[CaptureAudioMode.None],true,120);
    private static CaptureSessionRequest Options=>new(){Recording=new(Bitrate:100_000_000,MaximumDurationSeconds:120),
        Preparation=new(0),MaximumBytes=CaptureLimits.MaximumLibraryRecordingBytes,OutputPurpose=CaptureOutputPurpose.LibraryRecording};
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Reject(Func<Task> action)
    {try{await action();}catch(Exception e) when(e is BrokerException or IOException or OperationCanceledException){return;}throw new Exception("Expected recording draft rejection");}
    private sealed class Clock:TimeProvider{internal DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    private static void Write(string path,long bytes=65536)
    {using var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);output.Write(new byte[]{0,0,0,20,102,116,121,112,0,0,0,0});output.SetLength(bytes);}
    private static CaptureAttachment Complete(WindowCaptureRegistry registry,object owner,WindowCaptureTicket ticket,long bytes=65536)
    {
        var work=registry.Take()!;Write(work.OutputPath,bytes);
        registry.Complete(new(ticket.RequestId,1280,720,2){Properties=new(1280,720,15,30,0,CaptureEncoderMode.Software)});
        return registry.Status(owner,ticket.RequestId).Attachment??throw new Exception("Draft did not become ready");
    }
    internal static async Task StorageAndRetirement()
    {
        var folder=Path.Combine(Path.GetTempPath(),"library-draft-test-"+Guid.NewGuid().ToString("N"));
        var clock=new Clock();var owner=new object();var other=new object();
        TaskCompletionSource? inspectStarted=null,inspectRelease=null;
        using var store=new CaptureLibraryStore(Path.Combine(folder,"library"),Path.Combine(folder,"media"),null,null,
            async(_,media,_)=>{if(inspectStarted is {} started){started.TrySetResult();await inspectRelease!.Task;}return media;});
        using var registry=new WindowCaptureRegistry(clock,maximumBytes:CaptureLimits.MaximumContextBytes);registry.SetCapabilities(Capabilities);
        try
        {
            Check(WindowCaptureRegistry.LibraryRecordingBudget(Options)==1_883_388_608,"Known bitrate/duration budget lost its bounded headroom");
            await Reject(()=>store.ReserveVideoOutputAsync(Identity,256L*1024*1024+1));
            Check(WindowCaptureRegistry.LibraryRecordingBudget(Options with{Recording=Options.Recording! with{Audio=CaptureAudioMode.Application}})==1_885_788_608,"Application AAC was omitted from reservation budget");
            var ticket=await registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);
            var stage=registry.Take()!.OutputPath;
            var media=Complete(registry,owner,ticket,CaptureLimits.MaximumAttachmentBytes+1024);
            Check(media.Origin==CaptureAttachmentOrigin.LibraryRecordingDraft && media.ByteLength>CaptureLimits.MaximumAttachmentBytes,"Large draft used temporary attachment limits");
            Check(registry.Read(owner,new(media.Id,0)).Bytes.Length==CaptureLimits.ChunkBytes,"Draft read is not chunk bounded");
            await Reject(()=>registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default));
            using(var output=await store.ReserveVideoOutputAsync(Identity,1024,5))
                Check(File.Exists(stage),"Replay reservation recovery removed the active draft");
            using(var edit=registry.AcquireEditLease(owner,media))
            {
                using var editing=await store.OpenEditSessionAsync(edit,Identity,()=>true,default);
                Check(editing.IsCurrent,"Library recording draft was not editor readable");
                await Reject(()=>registry.SaveLibraryDraftAsync(owner,media,default));
                Check(registry.ResolveOwned(owner,media).Attachment==media&&File.Exists(stage),"Failed move discarded the reviewable original");
            }
            var saved=await registry.SaveLibraryDraftAsync(owner,media,default);
            Check(saved.Media.Origin==CaptureAttachmentOrigin.SavedLibrary&&!File.Exists(stage)&&saved.Media.ByteLength==media.ByteLength,"Save did not move the original stage");
            await Reject(()=>Task.Run(()=>registry.ResolveOwned(owner,media)));
            ticket=await registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);media=Complete(registry,owner,ticket);
            stage=registry.ResolveOwned(owner,media).Path;registry.Discard(owner,media.Id);Check(!File.Exists(stage),"Discard retained staged bytes");
            ticket=await registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);media=Complete(registry,owner,ticket);
            stage=registry.ResolveOwned(owner,media).Path;clock.Now+=TimeSpan.FromMinutes(31);await Reject(()=>Task.Run(()=>registry.ResolveOwned(owner,media)));
            Check(!File.Exists(stage),"Expired draft retained staged bytes");
            ticket=await registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);media=Complete(registry,owner,ticket);
            var second=await registry.EnqueueLibraryAsync(other,Identity with{InstanceId="other"},Options,Capabilities,store,default);Complete(registry,other,second);
            await Reject(()=>registry.EnqueueLibraryAsync(new object(),Identity,Options,Capabilities,store,default));
            registry.Retire(other);registry.Retire(owner,PlatformCapabilities.CaptureLibrarySaveV1);
            await Reject(()=>Task.Run(()=>registry.ResolveOwned(owner,media)));
            ticket=await registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);media=Complete(registry,owner,ticket);
            stage=registry.ResolveOwned(owner,media).Path;
            inspectStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);inspectRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending=registry.SaveLibraryDraftAsync(owner,media,default);await inspectStarted.Task;
            registry.Retire(owner,PlatformCapabilities.WindowCaptureV1);inspectRelease.SetResult();await Reject(()=>pending);
            Check(!File.Exists(stage),"Revocation allowed a cancellation-ignoring late inspection to retain/commit the draft");
            inspectStarted=null;
            using(var stop=new CancellationTokenSource())
            {stop.Cancel();await Reject(()=>registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,stop.Token));}
            var reservationGate=(SemaphoreSlim)typeof(CaptureLibraryStore).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
            await reservationGate.WaitAsync();
            var queued=registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);
            registry.Retire(owner,PlatformCapabilities.CaptureLibrarySaveV1);reservationGate.Release();await Reject(()=>queued);
            ticket=await registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);stage=registry.Take()!.OutputPath;
            registry.Retire(owner);Write(stage);await Reject(()=>Task.Run(()=>registry.Complete(new(ticket.RequestId,1280,720,2))));
            await store.RecoverEditsAsync(default);Check(!File.Exists(stage),"Known late encoder orphan escaped existing startup recovery");
        }
        finally
        {
            inspectRelease?.TrySetResult();registry.Dispose();store.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    internal static async Task ResolvedConfiguration()
    {
        var folder=Path.Combine(Path.GetTempPath(),"library-draft-resolved-"+Guid.NewGuid().ToString("N"));
        using var store=new CaptureLibraryStore(Path.Combine(folder,"library"),Path.Combine(folder,"media"),null,null,(_,media,_)=>Task.FromResult(media));
        using var registry=new WindowCaptureRegistry();var owner=new object();
        var adapter=new CaptureEncodingAdapter(new string('a',32),"Fixture",new string('b',32));
        var encoding=new CaptureEncodingOptions(CaptureVideoCodec.H264,CaptureVideoPixelFormat.Yuv4208,CaptureVideoRateControl.ConstantBitrate);
        var resolved=new CaptureRecordingConfiguration(new(1280,720,30,12_000_000,Encoder:CaptureEncoderMode.Hardware){Encoding=encoding},adapter,1279,719);
        var request=Options with{Recording=new(Encoder:CaptureEncoderMode.Hardware){Selection=new(){Resolution=CaptureRecordingResolution.Source,FramesPerSecond=30,Codec=CaptureVideoCodec.H264}}};
        var caps=new CaptureCapabilities(true,[CaptureImageFormat.Png],[new("hardware",1280,720,30,100_000,100_000_000,CaptureEncoderMode.Hardware){Encoding=encoding,Adapter=adapter}],[CaptureAudioMode.None],true,120);
        try
        {
            Check(WindowCaptureRegistry.LibraryRecordingBudget(request)==request.MaximumBytes,"Unresolved selection pretended to have fixed bitrate");
            Check(WindowCaptureRegistry.LibraryRecordingBudget(request with{Recording=resolved.Recording with{Encoding=encoding with{RateControl=CaptureVideoRateControl.Quality,Quality=70}}})==request.MaximumBytes,"Quality mode pretended to have fixed bitrate");
            foreach(var valid in new[]{true,false})
            {
                var ticket=await registry.EnqueueLibraryAsync(owner,Identity,request,caps,store,default);Write(registry.Take()!.OutputPath);
                registry.Complete(new(ticket.RequestId,1280,720,2){Properties=new(1279,719,30,60,0,CaptureEncoderMode.Hardware),
                    ResolvedRecording=valid?resolved:resolved with{Recording=resolved.Recording with{FramesPerSecond=60}}});
                var status=registry.Status(owner,ticket.RequestId);
                Check(valid?status.Phase==WindowCapturePhase.Ready:status.Phase==WindowCapturePhase.Failed,"Resolved settings ignored exact source canvas/explicit FPS authority");
                registry.Retire(owner);
            }
            var durationRequest=Options with{Recording=Options.Recording! with{MaximumDurationSeconds=10}};
            var durationTicket=await registry.EnqueueLibraryAsync(owner,Identity,durationRequest,Capabilities,store,default);Write(registry.Take()!.OutputPath);
            registry.Complete(new(durationTicket.RequestId,1280,720,10.25){Properties=new(1280,720,15,154,0,CaptureEncoderMode.Software)});
            var durationMedia=registry.Status(owner,durationTicket.RequestId).Attachment??throw new Exception("Permitted duration tolerance was not reviewable");
            Check((await registry.SaveLibraryDraftAsync(owner,durationMedia,default)).Media.DurationSeconds==10.25,"Ready duration tolerance was rejected at commit");
        }
        finally{registry.Dispose();store.Dispose();if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    internal static async Task PartialPublication()
    {
        var folder=Path.Combine(Path.GetTempPath(),"library-draft-partial-"+Guid.NewGuid().ToString("N"));
        using var store=new CaptureLibraryStore(Path.Combine(folder,"library"),Path.Combine(folder,"media"),null,null,(_,media,_)=>Task.FromResult(media));
        try
        {
            using(var output=await store.ReserveDraftVideoOutputAsync(Identity,65536,5,default))
            {
                Write(output.Path);var metadata=Path.Combine(folder,"library",output.Id+".json");Directory.CreateDirectory(metadata);
                var media=new CaptureAttachment(output.Id,"video/mp4",1280,720,65536,2,long.MaxValue){Origin=CaptureAttachmentOrigin.SavedLibrary};
                await Reject(()=>output.CommitDraftAsync(media,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),()=>true,default));
                Check(File.Exists(output.Path)&&output.IsCurrent&&!output.RecoveryRequired,"Failed metadata publication did not restore a retryable stage");
                Directory.Delete(metadata);
                Check((await output.CommitDraftAsync(media,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),()=>true,default)).Id==output.Id,"Restored stage could not commit once");
            }
            using(var output=await store.ReserveDraftVideoOutputAsync(Identity,65536,5,default))
            {
                Write(output.Path);var journal=Path.Combine(folder,"library","edits",output.Id+".json");
                using var document=JsonDocument.Parse(File.ReadAllText(journal));var stamp=document.RootElement.GetProperty("SavedAt").GetInt64();
                var final=Path.Combine(folder,"media",$"WidgetRail-{DateTimeOffset.FromUnixTimeMilliseconds(stamp):yyyyMMdd-HHmmss}-{output.Id}.mp4");
                var pending=Path.Combine(folder,"library",output.Id+".json.pending");
                File.Move(output.Path,final);File.WriteAllText(pending,"simulated interrupted metadata publication");output.RecoveryRequired=true;
                // Recreate the exact interrupted boundary with a pinned final;
                // no timing-dependent file watcher or production fault hook.
                using(var pin=File.Open(final,FileMode.Open,FileAccess.Read,FileShare.Read))
                {output.Dispose();Check(File.Exists(journal)&&File.Exists(pending)&&File.Exists(final),"Retirement erased failed-rollback recovery evidence");}
                await store.RecoverEditsAsync(default);
                Check(!File.Exists(journal)&&!File.Exists(pending)&&!File.Exists(final),"Abandoned partial publication escaped known-path recovery");
            }
        }
        finally{store.Dispose();if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    internal static async Task EntryReservations()
    {
        var folder=Path.Combine(Path.GetTempPath(),"library-draft-entry-"+Guid.NewGuid().ToString("N"));
        using var store=new CaptureLibraryStore(Path.Combine(folder,"library"),Path.Combine(folder,"media"));
        using var registry=new WindowCaptureRegistry();var owner=new object();
        var reservationGate=(SemaphoreSlim)typeof(CaptureLibraryStore).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        object[] owners=Enumerable.Range(0,CaptureLimits.MaximumTotal-1).Select(_=>new object()).ToArray();
        try
        {
            WindowCaptureTicket? previous=null;
            for(var index=0;index<40;index++)
            {
                var next=await registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);
                if(previous is {} retired)await Reject(()=>Task.Run(()=>registry.Status(owner,retired.RequestId)));
                registry.Take();registry.Complete(new(next.RequestId,0,0,0,"capture_failed"));
                Check(registry.Status(owner,next.RequestId).Phase==WindowCapturePhase.Failed,"Terminal failure receipt vanished before polling");previous=next;
            }
            registry.Retire(owner);
            void ReadyImage(object imageOwner)
            {
                var ticket=registry.Enqueue(imageOwner,Identity,WindowCaptureKind.Screenshot);var path=registry.Take()!.OutputPath;
                File.WriteAllBytes(path,[137,80,78,71,13,10,26,10,0,0,0,0]);registry.Complete(new(ticket.RequestId,1,1,0));
                Check(registry.Status(imageOwner,ticket.RequestId).Phase==WindowCapturePhase.Ready,"Small ordinary capture failed");
            }
            foreach(var item in owners)ReadyImage(item);
            await reservationGate.WaitAsync();
            var pending=registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);
            try{await Reject(()=>Task.Run(()=>registry.Enqueue(new object(),Identity,WindowCaptureKind.Screenshot)));}
            finally{reservationGate.Release();}
            var admitted=await pending;registry.Take();registry.Cancel(owner,admitted.RequestId);registry.Retire(owner);
            foreach(var item in owners)registry.Retire(item);
            for(var index=0;index<CaptureLimits.MaximumPerWidget-1;index++)ReadyImage(owner);
            await reservationGate.WaitAsync();
            pending=registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);
            try{await Reject(()=>Task.Run(()=>registry.Enqueue(owner,Identity,WindowCaptureKind.Screenshot)));}
            finally{reservationGate.Release();}
            admitted=await pending;registry.Take();registry.Cancel(owner,admitted.RequestId);registry.Retire(owner);
            // A revoked await cannot claim the same slot after another request.
            await reservationGate.WaitAsync();pending=registry.EnqueueLibraryAsync(owner,Identity,Options,Capabilities,store,default);
            registry.Retire(owner);ReadyImage(owner);reservationGate.Release();await Reject(()=>pending);
            Check(!Directory.EnumerateFiles(Path.Combine(folder,"library","edits"),"*.json").Any(),"Late rejected reservation leaked its journal");
        }
        finally{registry.Dispose();store.Dispose();if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    internal static async Task Authorization()
    {
        var folder=Path.Combine(Path.GetTempPath(),"library-draft-auth-"+Guid.NewGuid().ToString("N"));
        var consent=new ConsentStore(folder);var simulated=new SimulatedPlatformBrokerBackend();
        await using var backend=new CompositePlatformBrokerBackend(simulated,simulated);
        await consent.SetDecisionAsync(Identity,PlatformCapabilities.WindowCaptureV1,ConsentDecision.Grant);
        await using var broker=new PlatformCapabilityBroker(Identity,[PlatformCapabilities.WindowCaptureV1],consent,backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        try
        {
            try{await broker.ExecuteAsync(new(BrokerJson.ProtocolVersion,1,Identity,PlatformCapabilities.WindowCaptureV1,"capture.start",BrokerJson.ToElement(Options)));throw new Exception("Capture alone admitted library recording");}
            catch(BrokerException error) when(error.Code=="capability_not_declared"){}
            Check(WindowCaptureRegistry.Shared.Take() is null,"Capture-only authority queued a library recording");
            await consent.SetDecisionAsync(Identity,PlatformCapabilities.CaptureLibrarySaveV1,ConsentDecision.Grant);
            await using var saveOnly=new PlatformCapabilityBroker(Identity,[PlatformCapabilities.CaptureLibrarySaveV1],consent,backend);
            saveOnly.SetLifecycle(BrokerLifecycleState.Interactive);
            var draft=new CaptureAttachment(Guid.NewGuid().ToString("N"),"video/mp4",1280,720,65536,2,long.MaxValue)
                {Origin=CaptureAttachmentOrigin.LibraryRecordingDraft,Properties=new(1280,720,15,30,0,CaptureEncoderMode.Software)};
            try{await saveOnly.ExecuteAsync(new(BrokerJson.ProtocolVersion,1,Identity,PlatformCapabilities.CaptureLibrarySaveV1,"capture-library.save-current",BrokerJson.ToElement(new CaptureLibrarySaveRequest(draft))));throw new Exception("Save alone admitted recording source read");}
            catch(BrokerException error) when(error.Code=="capability_not_declared"){}
        }
        finally{await broker.DisposeAsync();if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
