using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;

/// <summary>Read-only selected-file custody. Offers are not read grants; only trusted intent delivery can authorize another owner.</summary>
internal sealed class FileReferenceRegistry : IDisposable
{
    internal static FileReferenceRegistry Shared {get;}=new();
    private const int MaximumReferences=128,MaximumOwnerReferences=32,MaximumOffers=16,MaximumDeliveries=32;
    private static readonly TimeSpan ReferenceLifetime=TimeSpan.FromMinutes(15),OfferLifetime=TimeSpan.FromSeconds(60);
    private readonly object gate=new();
    private readonly TimeProvider clock;
    private readonly Dictionary<BrokerWidgetIdentity,Owner> owners=[];
    private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Offer> offers=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Delivery> deliveries=new(StringComparer.Ordinal);
    private ITimer? sweeper;
    private bool disposed;
    internal FileReferenceRegistry(TimeProvider? clock=null)=>this.clock=clock??TimeProvider.System;
    internal sealed class Owner(FileReferenceRegistry registry,BrokerWidgetIdentity identity):IDisposable
    {
        private readonly byte[] contentIdentityKey = RandomNumberGenerator.GetBytes(32);
        internal readonly BrokerWidgetIdentity Identity=identity;
        internal bool Retired;
        internal bool IsCurrent=>registry.Current(this);
        public void Dispose()=>registry.Retire(this);
        internal string? ContentIdentity(Resource resource) => resource.Version is { } version
            ? Convert.ToHexString(HMACSHA256.HashData(contentIdentityKey, version).AsSpan(0, 16)).ToLowerInvariant() : null;
    }
    internal sealed class Resource(FileStream stream,string name,string type,Func<bool>? sourceCurrent)
    {
        internal readonly FileStream Stream=stream;
        internal readonly WidgetFileMetadata Metadata=new(name,type,stream.Length);
        internal readonly byte[]? Version = ReadFileVersion(stream);
        internal int References;
        internal readonly Func<bool>? SourceCurrent=sourceCurrent;
    }
    internal sealed class Entry(Owner owner,string id,Resource resource,DateTimeOffset expires,bool transferred)
    {
        internal readonly Owner Owner=owner;
        internal readonly string Id=id;
        internal readonly Resource Resource=resource;
        internal DateTimeOffset Expires=expires;
        internal readonly bool Transferred=transferred;
        internal WidgetFileReference Reference=>new(Id,Resource.Metadata.Name,Resource.Metadata.ContentType,Resource.Metadata.ByteLength,Expires.ToUnixTimeMilliseconds())
            { ContentIdentity = Owner.ContentIdentity(Resource) };
    }
    private static byte[]? ReadFileVersion(FileStream stream)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            // Registration denies writes. Physical identity distinguishes replacement
            // at the same path; timestamps/length distinguish versions without hashing
            // media. Unsupported filesystem identity means no cross-grant dedup hint.
            var identity = CapturePinnedFile.ReadIdentity(stream);
            var bytes = new byte[48];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes, identity.VolumeSerialNumber);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), identity.FileIdLow);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), identity.FileIdHigh);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), identity.ChangeTime);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), identity.LastWriteTime);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(40), identity.Length);
            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
    internal sealed record Offer(Owner Owner,string Id,Resource[] Resources,Func<bool>[] SourceChecks,DateTimeOffset Expires);
    internal sealed class Delivery:IDisposable
    {
        private readonly FileReferenceRegistry registry;
        internal readonly Offer offer;
        internal readonly Owner target;
        internal readonly Entry[] received;
        internal bool closed,accepted;
        internal Delivery(FileReferenceRegistry registry,Offer offer,Owner target,Entry[] received)
        {this.registry=registry;this.offer=offer;this.target=target;this.received=received;}
        /// <summary>Commit only after the selected worker accepts this exact delivery. False/rejection revokes provisional references.</summary>
        internal bool Complete(bool value)=>registry.Complete(this,value);
        public void Dispose()=>registry.Complete(this,false);
    }
    internal Owner CreateOwner(BrokerWidgetIdentity identity)
    {
        identity.Validate();
        lock(gate)
        {
            ThrowIfDisposed();
            if(owners.TryGetValue(identity,out var prior))RetireLocked(prior);
            var owner=new Owner(this,identity);owners.Add(identity,owner);return owner;
        }
    }
    private bool Current(Owner owner){lock(gate)return CurrentLocked(owner);}
    private bool CurrentLocked(Owner owner)=>!disposed&&!owner.Retired&&owners.GetValueOrDefault(owner.Identity)==owner;
    private void Demand(Owner owner){if(!CurrentLocked(owner))throw Error("file_owner_retired");}

    internal WidgetFileSelection RegisterSelected(Owner owner,IReadOnlyList<string> selected,CancellationToken token,string? name=null,string? contentType=null,long? expectedLength=null,Func<bool>? sourceCurrent=null)
    {
        if(selected.Count>WidgetFileContract.MaximumFiles||selected.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=selected.Count)throw Error("invalid_payload");
        var opened=new List<Resource>();
        try
        {
            foreach(var path in selected)
            {
                token.ThrowIfCancellationRequested();
                if(!Path.IsPathFullyQualified(path)||path.Length>32760||path.Any(char.IsControl))throw Error("invalid_payload");
                var display=name??Path.GetFileName(path);
                var type=contentType??ContentType(path);
                if(!WidgetFileContract.Name(display)||!WidgetFileContract.Type(type))throw Error("invalid_payload");
                // Deny writes, but allow the source owner to unlink its temporary
                // capture. Our open handle still addresses those exact bytes and
                // cannot be redirected by a replacement at the original path.
                var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,WidgetFileContract.ChunkBytes,FileOptions.Asynchronous|FileOptions.SequentialScan);
                Resource resource;
                try{resource=new Resource(stream,display,type,sourceCurrent);}
                catch{stream.Dispose();throw;}
                opened.Add(resource);
                if(!resource.Metadata.IsWellFormed()||expectedLength is {} length&&resource.Metadata.ByteLength!=length)throw Error("file_changed");
            }
            lock(gate)
            {
                SweepLocked();Demand(owner);token.ThrowIfCancellationRequested();Capacity(owner,opened.Count);
                if(opened.Any(resource=>!SourceCurrent(resource)))throw Error("file_unavailable");
                var result=opened.Select(resource=>Add(owner,resource).Reference).ToArray();
                opened.Clear();EnsureSweep();return new(result);
            }
        }
        catch(IOException error){throw new BrokerException("file_unavailable","The selected file cannot be read.",error);}
        catch(UnauthorizedAccessException error){throw new BrokerException("file_unavailable","The selected file cannot be read.",error);}
        finally{foreach(var resource in opened)resource.Stream.Dispose();}
    }
    internal WidgetFileReference Get(Owner owner,string id)
    {lock(gate){SweepLocked();var entry=Find(owner,id);entry.Expires=clock.GetUtcNow()+ReferenceLifetime;return entry.Reference;}}
    internal async Task<WidgetFileReadChunk> ReadAsync(Owner owner,WidgetFileReadRequest request,CancellationToken token)
    {
        Entry entry;
        lock(gate)
        {
            SweepLocked();entry=Find(owner,request.Id);
            if(request.Offset<0||request.Offset>entry.Resource.Metadata.ByteLength||request.Count is <1 or >WidgetFileContract.ChunkBytes)throw Error("invalid_payload");
            entry.Expires=clock.GetUtcNow()+ReferenceLifetime;++entry.Resource.References;
        }
        try
        {
            token.ThrowIfCancellationRequested();
            var length=entry.Resource.Metadata.ByteLength;
            var bytes=new byte[(int)Math.Min(request.Count,length-request.Offset)];
            var read=0;
            while(read<bytes.Length)
            {
                var count=await RandomAccess.ReadAsync(entry.Resource.Stream.SafeFileHandle,bytes.AsMemory(read),request.Offset+read,token).ConfigureAwait(false);
                if(count==0)throw Error("file_changed");
                read+=count;
            }
            lock(gate)
            {
                token.ThrowIfCancellationRequested();SweepLocked();
                if(!CurrentLocked(owner)||entries.GetValueOrDefault(request.Id)!=entry||!entry.Transferred&&!SourceCurrent(entry.Resource))throw Error("file_unavailable");
                if(entry.Resource.Stream.Length!=length)throw Error("file_changed");
            }
            return new(request.Offset,bytes,request.Offset+bytes.Length==length);
        }
        finally{lock(gate)Drop(entry.Resource);}
    }
    internal void Release(Owner owner,string id)
    {
        if(!WidgetFileContract.Token(id))throw Error("invalid_payload");
        lock(gate){SweepLocked();Demand(owner);if(entries.TryGetValue(id,out var entry)){if(entry.Owner!=owner)throw Error("file_unavailable");Remove(entry);}}
    }
    internal WidgetFileShareOffer OfferFiles(Owner owner,WidgetFileOfferRequest request)
    {
        if(request.FileIds is not {Count:>0 and <=WidgetFileContract.MaximumFiles}||request.FileIds.Distinct(StringComparer.Ordinal).Count()!=request.FileIds.Count)throw Error("invalid_payload");
        lock(gate)
        {
            SweepLocked();Demand(owner);if(offers.Count>=MaximumOffers)throw Error("file_limit");
            var sourceEntries=request.FileIds.Select(id=>Find(owner,id)).ToArray();
            var resources=sourceEntries.Select(entry=>entry.Resource).ToArray();
            var sourceChecks=sourceEntries.Select(entry=>(Func<bool>)(()=>entry.Transferred||SourceCurrent(entry.Resource))).ToArray();
            foreach(var resource in resources)++resource.References;
            var offer=new Offer(owner,Token(),resources,sourceChecks,clock.GetUtcNow()+OfferLifetime);offers.Add(offer.Id,offer);EnsureSweep();
            return new(offer.Id,resources.Select(resource=>resource.Metadata).ToArray(),offer.Expires.ToUnixTimeMilliseconds());
        }
    }
    internal void CancelOffer(Owner owner,string id)
    {
        if(!WidgetFileContract.Token(id))throw Error("invalid_payload");
        lock(gate)
        {
            SweepLocked();Demand(owner);
            if(offers.TryGetValue(id,out var offer)){if(offer.Owner!=owner)throw Error("file_offer_unavailable");RemoveOffer(offer);}
        }
    }
    /// <summary>Trusted bridge only: invoke after its existing intent source/selected-target/worker-run checks.</summary>
    internal Delivery AuthorizeShare(BrokerWidgetIdentity source,BrokerWidgetIdentity target,string offerId)
    {
        lock(gate)
        {
            SweepLocked();
            if(!WidgetFileContract.Token(offerId)||!owners.TryGetValue(source,out var sender)||!owners.TryGetValue(target,out var receiver)||
                !CurrentLocked(sender)||!CurrentLocked(receiver)||!offers.TryGetValue(offerId,out var offer)||offer.Owner!=sender||deliveries.ContainsKey(offerId))throw Error("file_offer_unavailable");
            if(deliveries.Count>=MaximumDeliveries)throw Error("file_limit");Capacity(receiver,offer.Resources.Length);
            var received=offer.Resources.Select(resource=>Add(receiver,resource,true)).ToArray();
            var delivery=new Delivery(this,offer,receiver,received);deliveries.Add(offerId,delivery);return delivery;
        }
    }
    internal WidgetFileSelection AcceptShare(Owner owner,string offerId)
    {
        lock(gate)
        {
            SweepLocked();Demand(owner);
            if(!WidgetFileContract.Token(offerId)||!deliveries.TryGetValue(offerId,out var delivery)||delivery.target!=owner||
                delivery.closed&&!delivery.accepted||delivery.received.Any(entry=>entries.GetValueOrDefault(entry.Id)!=entry))throw Error("file_offer_unavailable");
            foreach(var entry in delivery.received)entry.Expires=clock.GetUtcNow()+ReferenceLifetime;
            return new(delivery.received.Select(entry=>entry.Reference).ToArray());
        }
    }
    private bool Complete(Delivery delivery,bool accepted)
    {
        lock(gate)
        {
            if(delivery.closed)return delivery.accepted;
            accepted=accepted&&!disposed&&CurrentLocked(delivery.offer.Owner)&&CurrentLocked(delivery.target)&&
                offers.GetValueOrDefault(delivery.offer.Id)==delivery.offer&&delivery.offer.Expires>clock.GetUtcNow()&&delivery.offer.SourceChecks.All(check=>check());
            delivery.closed=true;delivery.accepted=accepted;
            if(offers.Remove(delivery.offer.Id))foreach(var resource in delivery.offer.Resources)Drop(resource);
            if(!accepted){foreach(var entry in delivery.received)Remove(entry);deliveries.Remove(delivery.offer.Id);}
            return accepted;
        }
    }
    private Entry Add(Owner owner,Resource resource,bool transferred=false)
    {var entry=new Entry(owner,Token(),resource,clock.GetUtcNow()+ReferenceLifetime,transferred);++resource.References;entries.Add(entry.Id,entry);return entry;}
    private Entry Find(Owner owner,string id)
    {Demand(owner);return WidgetFileContract.Token(id)&&entries.TryGetValue(id,out var entry)&&entry.Owner==owner&&(entry.Transferred||SourceCurrent(entry.Resource))?entry:throw Error("file_unavailable");}
    private void Capacity(Owner owner,int count)
    {if(entries.Count+count>MaximumReferences||entries.Values.Count(entry=>entry.Owner==owner)+count>MaximumOwnerReferences)throw Error("file_limit");}
    private void Remove(Entry entry){if(entries.Remove(entry.Id))Drop(entry.Resource);}
    private static void Drop(Resource resource){if(--resource.References==0)resource.Stream.Dispose();}
    private void RemoveOffer(Offer offer)
    {
        if(deliveries.TryGetValue(offer.Id,out var delivery)&&!delivery.closed){Complete(delivery,false);return;}
        if(offers.Remove(offer.Id))foreach(var resource in offer.Resources)Drop(resource);
    }
    private void Retire(Owner owner){lock(gate)RetireLocked(owner);}
    private void RetireLocked(Owner owner)
    {
        if(owner.Retired)return;owner.Retired=true;
        if(owners.GetValueOrDefault(owner.Identity)==owner)owners.Remove(owner.Identity);
        foreach(var offer in offers.Values.Where(offer=>offer.Owner==owner).ToArray())RemoveOffer(offer);
        foreach(var delivery in deliveries.Values.Where(delivery=>delivery.target==owner).ToArray())
        {if(!delivery.closed)Complete(delivery,false);deliveries.Remove(delivery.offer.Id);}
        foreach(var entry in entries.Values.Where(entry=>entry.Owner==owner).ToArray())Remove(entry);
    }
    private void SweepLocked()
    {
        ThrowIfDisposed();var now=clock.GetUtcNow();
        foreach(var offer in offers.Values.Where(offer=>offer.Expires<=now||!CurrentLocked(offer.Owner)||!offer.SourceChecks.All(check=>check())).ToArray())RemoveOffer(offer);
        foreach(var entry in entries.Values.Where(entry=>entry.Expires<=now||!entry.Transferred&&!SourceCurrent(entry.Resource)).ToArray())Remove(entry);
        foreach(var delivery in deliveries.Values.Where(delivery=>delivery.accepted&&delivery.received.All(entry=>!entries.ContainsKey(entry.Id))).ToArray())deliveries.Remove(delivery.offer.Id);
    }
    private void EnsureSweep()=>sweeper??=clock.CreateTimer(_=>{lock(gate){if(!disposed)SweepLocked();}},null,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30));
    private void ThrowIfDisposed(){if(disposed)throw new ObjectDisposedException(nameof(FileReferenceRegistry));}
    private static bool SourceCurrent(Resource resource)=>resource.SourceCurrent?.Invoke()??true;
    private static string Token()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private static string ContentType(string path)=>Path.GetExtension(path).ToLowerInvariant() switch
    {".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",".webp"=>"image/webp",".gif"=>"image/gif",".mp4"=>"video/mp4",".webm"=>"video/webm",".txt"=>"text/plain",".pdf"=>"application/pdf",_=>"application/octet-stream"};
    private static BrokerException Error(string code)=>new(code,"The selected file reference is unavailable or invalid.");
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed)return;
            foreach(var owner in owners.Values.ToArray())RetireLocked(owner);
            disposed=true;sweeper?.Dispose();sweeper=null;
        }
    }
}
