using System.Text.Json;

namespace WidgetRail.WidgetSdk;

/// <summary>Assembles a bounded addBatchAction result. Use one reader per web session;
/// only a complete, ordered batch is returned. Discard the reader with its session.</summary>
public sealed class WebSessionPageActionBatchReader
{
    private string? id;
    private int total,bytes;
    private DateTime expires;
    private JsonElement context;
    private readonly List<JsonElement> items=[];
    private int next;
    public bool TryRead(JsonElement payload,out JsonElement batchContext,out JsonElement[] batchItems)
    {
        batchContext=default;batchItems=[];
        if(payload.ValueKind!=JsonValueKind.Object||!payload.TryGetProperty("batchId",out var bid)||bid.ValueKind!=JsonValueKind.String||
            !Guid.TryParse(bid.GetString(),out _)||!payload.TryGetProperty("index",out var index)||!index.TryGetInt32(out var part)||
            !payload.TryGetProperty("total",out var count)||!count.TryGetInt32(out var parts)||parts is <1 or >48||part<0||part>=parts||
            !payload.TryGetProperty("context",out var ctx)||ctx.ValueKind!=JsonValueKind.Object||
            !payload.TryGetProperty("items",out var values)||values.ValueKind!=JsonValueKind.Array||values.GetArrayLength() is <1 or >100)
        {Reset();throw new IOException("The website returned an invalid result batch. Evaluate again.");}
        if(part==0){Reset();id=bid.GetString();total=parts;context=ctx.Clone();expires=DateTime.UtcNow.AddSeconds(30);}
        if(id!=bid.GetString()||part!=next||parts!=total||DateTime.UtcNow>expires||context.GetRawText()!=ctx.GetRawText())
        {Reset();throw new IOException("The website result batch was incomplete or changed. Evaluate again.");}
        bytes+=System.Text.Encoding.UTF8.GetByteCount(payload.GetRawText());
        if(bytes>3*1024*1024||items.Count+values.GetArrayLength()>100)
        {Reset();throw new IOException("The website result batch exceeded its limit.");}
        items.AddRange(values.EnumerateArray().Select(v=>v.Clone()));next++;
        if(next<total)return false;
        batchContext=context;batchItems=items.ToArray();Reset();return true;
    }
    public void Reset(){id=null;total=0;next=0;bytes=0;context=default;items.Clear();}
}
