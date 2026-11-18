using System.Text.Json;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;

internal sealed class FileCapabilityDomain:IDisposable
{
    private readonly BrokerWidgetIdentity identity;
    private readonly FileReferenceRegistry registry;
    private readonly FileReferenceRegistry.Owner owner;
    private readonly Func<CaptureAttachment,HostCaptureAttachment> capture;
    private readonly CancellationTokenSource lifetime=new();
    private readonly CancellationToken lifetimeToken;
    private int disposed;
    internal FileCapabilityDomain(BrokerWidgetIdentity identity,Func<CaptureAttachment,HostCaptureAttachment> capture,FileReferenceRegistry? registry=null)
    {this.identity=identity;this.capture=capture;this.registry=registry??FileReferenceRegistry.Shared;owner=this.registry.CreateOwner(identity);lifetimeToken=lifetime.Token;}
    internal async ValueTask<JsonElement> ExecuteAsync(string operation,JsonElement payload,CancellationToken token)
    {
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,lifetimeToken);token=cancellation.Token;
        token.ThrowIfCancellationRequested();
        switch(operation)
        {
            case "files.choose":
                var choice=BrokerJson.ParsePayload<WidgetFileChoiceRequest>(payload);
                if(!choice.IsWellFormed())throw Invalid();
                var paths=await FilePickerRequests.ChooseAsync(identity,choice,()=>owner.IsCurrent&&!token.IsCancellationRequested,token).ConfigureAwait(false);
                return BrokerJson.ToElement(registry.RegisterSelected(owner,paths,token));
            case "files.from-capture":
                var source=BrokerJson.ParsePayload<WidgetFileFromCaptureRequest>(payload);
                if(source.Attachment?.IsWellFormed()!=true||source.Name is {} name&&!WidgetFileContract.Name(name))throw Invalid();
                // Resolve using the source broker's exact existing capture domain,
                // then retain an independent read-only handle while that authority
                // is still current. The source capture is never released here.
                var admitted=capture(source.Attachment);
                bool CurrentCapture(){try{_ = capture(source.Attachment);return true;}catch(BrokerException){return false;}catch(ObjectDisposedException){return false;}}
                var selected=registry.RegisterSelected(owner,[admitted.Path],token,source.Name,admitted.Attachment.ContentType,admitted.Attachment.ByteLength,CurrentCapture);
                try{_ = capture(source.Attachment);token.ThrowIfCancellationRequested();}
                catch{foreach(var file in selected.Files)registry.Release(owner,file.Id);throw;}
                return BrokerJson.ToElement(selected.Files.Single());
            case "files.get":return BrokerJson.ToElement(registry.Get(owner,BrokerJson.ParsePayload<WidgetFileIdReference>(payload).Id));
            case "files.read":return BrokerJson.ToElement(await registry.ReadAsync(owner,BrokerJson.ParsePayload<WidgetFileReadRequest>(payload),token).ConfigureAwait(false));
            case "files.release":registry.Release(owner,BrokerJson.ParsePayload<WidgetFileIdReference>(payload).Id);return BrokerCapabilityDomains.Acknowledged();
            case "files.offer":return BrokerJson.ToElement(registry.OfferFiles(owner,BrokerJson.ParsePayload<WidgetFileOfferRequest>(payload)));
            case "files.accept":return BrokerJson.ToElement(registry.AcceptShare(owner,BrokerJson.ParsePayload<WidgetFileIdReference>(payload).Id));
            case "files.cancel-offer":registry.CancelOffer(owner,BrokerJson.ParsePayload<WidgetFileIdReference>(payload).Id);return BrokerCapabilityDomains.Acknowledged();
            default:throw new BrokerException("unsupported_operation","File operation is unsupported.");
        }
    }
    private static BrokerException Invalid()=>new("invalid_payload","The file request is invalid.");
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;lifetime.Cancel();owner.Dispose();lifetime.Dispose();}
}
