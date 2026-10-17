using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WebSessionBatchTests
{
    internal static async Task Run()
    {
        var values=Enumerable.Range(0,600).Select(i=>new {id=i,text=new string('界',64)+"\"\\🎮"}).ToArray();
        var batches=WebSessionMessageBatcher.Create(values,items=>JsonSerializer.SerializeToElement(new{kind="batch",revision=12,items}));
        if(batches.Count<=1 || batches.Any(b=>JsonSerializer.SerializeToUtf8Bytes(b).Length>WebSessionContract.MaximumMessageBytes))throw new Exception("Message byte bound");
        if(!batches.SelectMany(b=>b.GetProperty("items").EnumerateArray()).Select(v=>v.GetProperty("id").GetInt32()).SequenceEqual(Enumerable.Range(0,600)))throw new Exception("Item order/completeness");
        if(batches.Any(b=>b.GetProperty("revision").GetInt32()!=12))throw new Exception("Envelope loss");
        if(WebSessionMessageBatcher.Create(Array.Empty<int>(),items=>JsonSerializer.SerializeToElement(new{items})).Count!=0)throw new Exception("Empty batches");
        try{WebSessionMessageBatcher.Create(new[]{new string('x',WebSessionContract.MaximumMessageBytes)},items=>JsonSerializer.SerializeToElement(new{items}));throw new Exception("Oversized item accepted");}
        catch(ArgumentException){}
        var exact=System.Text.Encoding.UTF8.GetBytes("\""+new string('x',WebSessionContract.MaximumMessageBytes-2)+"\"");
        if(WebSessionContract.DecodeMessage(Convert.ToBase64String(exact)).Length!=WebSessionContract.MaximumMessageBytes)throw new Exception("Exact size rejected");
        try{WebSessionContract.DecodeMessage(Convert.ToBase64String([..exact,(byte)' ']));throw new Exception("Over limit accepted");}catch(ArgumentException){}
        string Nested(int depth)=>new string('[',depth)+"0"+new string(']',depth);
        WebSessionContract.DecodeMessage(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Nested(WebSessionContract.MaximumMessageDepth))));
        try{WebSessionContract.DecodeMessage(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Nested(WebSessionContract.MaximumMessageDepth+1))));throw new Exception("Over-depth result accepted");}catch(ArgumentException){}
        await Requests();
    }
    private static async Task Requests()
    {
        var messages=new System.Collections.Concurrent.ConcurrentQueue<string>();var cancels=0;
        var widget=WidgetTestHost.Attach(new Probe(),new WidgetTestHostServicesBuilder().WithHandler(WidgetWebSessionCapabilities.Command,async(command,token)=>
        {
            if(command.Operation is WebSessionOperation.Open or WebSessionOperation.OpenAdapted)return new(){Session=new(new string('a',32))};
            if(command.Operation==WebSessionOperation.ReadMessages){await Task.Delay(5,token);return new(){MessagesBase64=messages.TryDequeue(out var message)?[message]:[]};}
            if(command.Operation==WebSessionOperation.PostMessage)
            {
                using var parsed=JsonDocument.Parse(Convert.FromBase64String(command.MessageBase64!));var request=parsed.RootElement;
                if(request.GetProperty("kind").GetString()=="cancel")cancels++;
                else
                {
                    var id=request.GetProperty("id").GetString();var method=request.GetProperty("method").GetString();var batchId=Guid.NewGuid().ToString();
                    if(method!="wait")for(var index=method=="broken"?1:0;index<4;index++)messages.Enqueue(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{widgetRail=1,kind="response-batch",id,payload=new{batchId,index,total=4,context=new{source="fixture"},items=Enumerable.Range(index*25,25).Select(i=>new{id=i,text=new string('x',1000)}).ToArray()}})));
                }
            }
            return new();
        }).Build());
        await using var session=await widget.Open();
        var result=await session.RequestAsync<object,JsonElement>("bulk",new{});
        if(result.GetProperty("items").GetArrayLength()!=100||result.GetProperty("items")[99].GetProperty("id").GetInt32()!=99)throw new Exception("RPC batch lost order or items");
        try{await session.RequestAsync<object,JsonElement>("broken",new{});throw new Exception("Incomplete RPC batch accepted");}
        catch(WidgetCapabilityException error)when(error.ErrorCode=="malformed_response"){}
        using var cancel=new CancellationTokenSource(50);
        try{await session.RequestAsync<object,JsonElement>("wait",new{},cancel.Token);throw new Exception("Cancelled RPC returned success");}catch(OperationCanceledException){}
        if(cancels!=1)throw new Exception("RPC cancellation not delivered");
        result=await session.RequestAsync<object,JsonElement>("bulk",new{});
        if(result.GetProperty("items").GetArrayLength()!=100)throw new Exception("Failed request poisoned next batch");
        await WidgetTestHost.DestroyAsync(widget);
    }
    private sealed class Probe:Widget
    {
        public override WidgetView Render()=>new(UI.Text("Session test","test"));
        internal ValueTask<WidgetWebSession> Open()=>HostServices.WebSessions.OpenAsync(new("test","Test",WebSessionSource.Remote("https://example.com")){PageAdapter=new(){ScriptAsset="web/actions.js",Origins=["https://example.com"]}});
    }
}
