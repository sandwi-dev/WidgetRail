using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WebSessionBatchTests
{
    internal static Task Run()
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
        return Task.CompletedTask;
    }
}
