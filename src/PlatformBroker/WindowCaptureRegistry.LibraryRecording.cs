using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed partial class WindowCaptureRegistry
{
    private const int MaximumLibraryDrafts = 2;
    private readonly Dictionary<string,(object Owner,long Bytes)> pendingLibraryOutputs = new(StringComparer.Ordinal);

    internal static long LibraryRecordingBudget(CaptureSessionRequest options)
    {
        var video=options.Recording ?? throw Error("invalid_capture_options");
        if(video.Selection is not null || video.Encoding?.RateControl==CaptureVideoRateControl.Quality)return options.MaximumBytes;
        var bitsPerSecond=(long)video.Bitrate+(video.Audio==CaptureAudioMode.Application?128_000:0);
        var estimate=(long)Math.Ceiling(bitsPerSecond*(double)video.MaximumDurationSeconds/8*1.25)+8L*1024*1024;
        return Math.Min(options.MaximumBytes,estimate);
    }

    internal async Task<WindowCaptureTicket> EnqueueLibraryAsync(object owner,BrokerWidgetIdentity widget,CaptureSessionRequest options,
        CaptureCapabilities verified,CaptureLibraryStore library,CancellationToken token)
    {
        if(options.OutputPurpose!=CaptureOutputPurpose.LibraryRecording || options.Recording is null || !options.IsWellFormed())throw Error("invalid_capture_options");
        var validation=verified.Validate(options);
        if(!validation.Supported)throw Error(validation.ErrorCode!);
        var budget=LibraryRecordingBudget(options);
        var reservation=Guid.NewGuid().ToString("N");
        lock(gate)
        {
            ThrowIfDisposed();Sweep();token.ThrowIfCancellationRequested();
            foreach(var old in entries.Where(pair=>ReferenceEquals(pair.Value.Owner,owner)&&pair.Value.Phase is WindowCapturePhase.Cancelled or WindowCapturePhase.Failed).ToArray())
            {entries.Remove(old.Key);ReleaseEntry(old.Value);}
            var drafts=entries.Values.Where(entry=>entry.OutputSession is not null&&entry.Phase is not (WindowCapturePhase.Cancelled or WindowCapturePhase.Failed)).ToArray();
            DemandEntryCapacity(owner);
            if(drafts.Length+pendingLibraryOutputs.Count>=MaximumLibraryDrafts ||
                drafts.Any(entry=>ReferenceEquals(entry.Owner,owner)) || pendingLibraryOutputs.Values.Any(entry=>ReferenceEquals(entry.Owner,owner)) ||
                drafts.Sum(entry=>entry.OutputSession!.MaximumBytes)+pendingLibraryOutputs.Values.Sum(entry=>entry.Bytes)+budget>
                    MaximumLibraryDrafts*CaptureLimits.MaximumLibraryRecordingBytes)throw Error("capture_limit");
            if(entries.Values.Any(entry=>entry.Phase is WindowCapturePhase.Queued or WindowCapturePhase.Preparing or WindowCapturePhase.Recording))throw Error("capture_busy");
            pendingLibraryOutputs.Add(reservation,(owner,budget));
        }
        CaptureLibraryOutputSession? output=null;
        try
        {
            output=await library.ReserveDraftVideoOutputAsync(widget,budget,options.Recording.MaximumDurationSeconds+.25,token).ConfigureAwait(false);
            lock(gate)
            {
                ThrowIfDisposed();token.ThrowIfCancellationRequested();
                if(!pendingLibraryOutputs.ContainsKey(reservation))throw Error("capture_stale");
                DemandEntryCapacity(owner,reservation);
                if(entries.Values.Any(entry=>entry.Phase is WindowCapturePhase.Queued or WindowCapturePhase.Preparing or WindowCapturePhase.Recording))throw Error("capture_busy");
                var id=Guid.NewGuid().ToString("N");
                var request=new HostWindowCapture(id,widget,WindowCaptureKind.Video,output.Path){Options=options with{MaximumBytes=budget}};
                entries.Add(id,new(owner,request,clock.GetUtcNow().AddSeconds(120+options.Preparation.DelaySeconds+options.Recording.MaximumDurationSeconds)){OutputSession=output});
                output=null;
                sweeper??=clock.CreateTimer(_=>{lock(gate){if(!disposed)Sweep();}},null,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30));
                return new(id);
            }
        }
        finally{lock(gate)pendingLibraryOutputs.Remove(reservation);output?.Dispose();}
    }

    internal async Task<CaptureLibraryItem> SaveLibraryDraftAsync(object owner,CaptureAttachment expected,CancellationToken token)
    {
        Entry entry;CaptureLibraryOutputSession output;long captured;
        lock(gate)
        {
            token.ThrowIfCancellationRequested();entry=Attachment(owner,expected.Id);
            if(entry.Attachment!=expected || entry.OutputSession is not {} stage || !stage.IsCurrent || entry.Saving)throw Error("capture_unavailable");
            output=stage;entry.Saving=true;captured=Resolved(entry).CapturedAtUnixMilliseconds??clock.GetUtcNow().ToUnixTimeMilliseconds();
        }
        bool Current(){lock(gate)return !disposed&&!token.IsCancellationRequested&&entries.TryGetValue(entry.Request.RequestId,out var current)&&
            ReferenceEquals(current,entry)&&entry.Attachment==expected&&entry.Expires>clock.GetUtcNow();}
        try
        {
            return await output.CommitDraftAsync(expected with{Id=output.Id,Origin=CaptureAttachmentOrigin.SavedLibrary,ExpiresAtUnixMilliseconds=long.MaxValue},
                captured,Current,token).ConfigureAwait(false);
        }
        finally
        {
            lock(gate)
            {
                entry.Saving=false;
                if(output.Published || !output.IsCurrent || !Current())
                {
                    if(entries.TryGetValue(entry.Request.RequestId,out var current)&&ReferenceEquals(current,entry))entries.Remove(entry.Request.RequestId);
                    ReleaseEntry(entry);
                }
            }
        }
    }
}
