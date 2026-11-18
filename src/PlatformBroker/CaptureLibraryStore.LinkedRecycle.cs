using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    // Separate from the legacy owned-library recycler. The broker admits this
    // operation only through its explicit linked-original capability.
    internal Func<string,Func<bool>,CancellationToken,Task<bool>>? LinkedOriginalRecycler {get;set;}

    internal Task<CaptureLibraryBatchResult> RecycleLinkedAsync(CaptureLibraryRecycleRequest request,CancellationToken token)
        =>RecycleBatchAsync(request,RecycleLinkedOneAsync,token);

    private async Task RecycleLinkedOneAsync(CaptureLibraryMutationReference reference,CancellationToken token)
    {
        if(LinkedOriginalRecycler is not {} recycleOriginal)throw Error("capture_library_recycle_unavailable");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership=Own();
            RefreshEntry(reference.Id,forceMetadata:true);
            var saved=Read(reference.Id);
            if(saved.Item.SourceId is not {} sourceId)throw Error("capture_library_not_linked");
            if(saved.Item.Revision!=reference.ContentRevision||saved.Item.MetadataRevision!=reference.MetadataRevision)
                throw Error("capture_library_metadata_conflict");
            var source=ReadSource(sourceId);
            var path=ResolveSourcePath(source,saved.RelativePath??string.Empty);
            if(saved.ExternalIdentity is not {} expected||!File.Exists(path))throw Error("capture_library_not_found");
            // Pin the existing directory chain for the whole OS operation. The
            // file's write/delete-denying read handle is released only because
            // Windows must rename the original into its Recycle Bin.
            using var pinned=CapturePinnedFile.Open(path);
            if(pinned.Identity!=expected)throw Error("capture_library_metadata_conflict");
            pinned.Stream.Dispose();
            var marker=Path.Combine(root,reference.Id+".recycled");PlainPath(marker);
            token.ThrowIfCancellationRequested();
            File.WriteAllText(marker,JsonSerializer.Serialize(saved));
            bool removed;
            try {removed=await recycleOriginal(path,Current,token).ConfigureAwait(false);}
            catch when(!File.Exists(path)){removed=true;}
            catch{DeleteOwned(marker);throw;}
            if(!removed||File.Exists(path))
            {DeleteOwned(marker);throw Error("capture_library_recycle_unavailable");}
            // Reconcile the completed OS effect even if the caller cancelled
            // during the synchronous shell operation. Later batch items stop.
            IndexRecycled(saved);RemoveLinkedMetadata(saved);NotifyChanged();

            bool Current()
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if(!SourceEnabled(source)||ResolveSourcePath(source,saved.RelativePath??string.Empty)!=path)return false;
                    using var current=CapturePinnedFile.Open(path);
                    return current.Identity==expected;
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or BrokerException){return false;}
            }
        }
        finally{gate.Release();}
    }
}
