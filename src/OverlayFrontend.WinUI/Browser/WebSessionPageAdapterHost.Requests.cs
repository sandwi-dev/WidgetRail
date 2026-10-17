using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class WebSessionPageAdapterHost
{
    private readonly Dictionary<string,long> requests=[];
    internal async Task PostMessageAsync(string json)
    {
        using var message=JsonDocument.Parse(json);
        var value=message.RootElement;
        if(!value.TryGetProperty("widgetRail",out var version)||version.GetInt32()!=1||
            !value.TryGetProperty("id",out var idValue)||idValue.GetString() is not {} id||!Guid.TryParseExact(id,"N",out _)||
            !value.TryGetProperty("kind",out var kindValue))throw new InvalidOperationException("Invalid adapter request.");
        var kind=kindValue.GetString();
        if(kind=="cancel")requests.Remove(id);
        else
        {
            if(kind!="request"||!value.TryGetProperty("method",out var method)||!WebSessionContract.IsKey(method.GetString())||
                context is null||!Current(epoch)||!canRequest())throw new InvalidOperationException("Adapter is unavailable.");
            foreach(var expired in requests.Where(p=>p.Value<Environment.TickCount64).Select(p=>p.Key).ToArray())requests.Remove(expired);
            if(requests.Count>=16||requests.ContainsKey(id))throw new InvalidOperationException("Too many adapter requests.");
            requests[id]=Environment.TickCount64+30000;
        }
        try{await Evaluate("globalThis.__widgetRailRequest?.("+json+")",epoch);}
        catch{requests.Remove(id);throw;}
    }
    private void ReceiveReply(JsonElement reply)
    {
        if(!reply.TryGetProperty("id",out var identifier)||identifier.GetString() is not {} id||
            !requests.TryGetValue(id,out var expires)||expires<Environment.TickCount64)return;
        if(!reply.TryGetProperty("widgetRail",out var version)||version.GetInt32()!=1)return;
        var kind=reply.GetProperty("kind").GetString();
        if(kind is not("response"or"response-batch"or"progress"))return;
        if(!canRequest())
        {
            CancelRequests();requests.Remove(id);
            publish(JsonSerializer.Serialize(new{widgetRail=1,kind="response",id,error="page_hidden"}));return;
        }
        // Only replies to outstanding host requests can leave the isolated world.
        publish(reply.GetRawText());
        if(kind=="response"||kind=="response-batch"&&reply.GetProperty("payload").GetProperty("index").GetInt32()==reply.GetProperty("payload").GetProperty("total").GetInt32()-1)requests.Remove(id);
    }
    internal void CancelRequests()
    {
        if(requests.Count==0)return;
        _=CancelRequestsAsync(epoch);
    }
    private async Task CancelRequestsAsync(long current)
    {
        try{await Evaluate("globalThis.__widgetRailCancelRequests?.()",current);}
        catch(Exception error)when(error is not OutOfMemoryException){ }
    }
    private void RetireRequests()
    {
        if(requests.Count==0)return;
        requests.Clear();publish("{\"widgetRail\":1,\"kind\":\"navigation\"}");
    }
    private const string RequestsPrelude="""
        let requestHandler;
        const batchResultKey=Symbol('batchResult');
        const requests=new Map();
        const reply=value=>deliver(JSON.stringify(value));
        globalThis.__widgetRailCancelRequests=()=>{for(const controller of requests.values())controller.abort();};
        globalThis.__widgetRailRequest=message=>{
          const {id,kind}=message;
          if(kind==='cancel'){requests.get(id)?.abort();return;}
          if(kind!=='request'||requests.has(id)||requests.size>=16)return;
          const controller=new AbortController();requests.set(id,controller);
          const timeout=setTimeout(()=>controller.abort(),29000);
          const send=value=>reply({widgetRail:1,id,...value});
          Promise.resolve().then(()=>{
            if(!requestHandler)throw new Error('No request handler');
            return requestHandler(message.method,message.payload,{signal:controller.signal,
              progress:payload=>{if(!controller.signal.aborted)send({kind:'progress',payload});}});
          }).then(value=>{
            if(controller.signal.aborted)throw new Error('Cancelled');
            if(value?.[batchResultKey])for(const payload of batchPayloads(value))send({kind:'response-batch',payload});
            else send({kind:'response',payload:value});
          }).catch(()=>send({kind:'response',error:'request_failed'}))
            .finally(()=>{clearTimeout(timeout);requests.delete(id);});
        };
        """;
}
