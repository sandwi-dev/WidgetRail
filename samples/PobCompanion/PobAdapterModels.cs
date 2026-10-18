using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

/// <summary>Strict readers for the adapter shapes consumed by the changed workflows.</summary>
internal static class PobAdapterData
{
    internal static JsonElement[] Array(JsonElement value,string name,int maximum,bool optional=false)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(name);
        if(!value.TryGetProperty(name,out var data)){if(optional)return [];throw Invalid(name);}
        if(data.ValueKind!=JsonValueKind.Array||data.GetArrayLength()>maximum)throw Invalid(name);
        return data.EnumerateArray().ToArray();
    }
    internal static string Text(JsonElement value,string name,int maximum=4096,bool optional=false,bool blank=true)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(name);
        if(!value.TryGetProperty(name,out var data)){if(optional)return "";throw Invalid(name);}
        if(data.ValueKind!=JsonValueKind.String)throw Invalid(name);
        var text=data.GetString()!;
        if(text.Length>maximum||!blank&&string.IsNullOrWhiteSpace(text))throw Invalid(name);
        return text;
    }
    internal static int Id(JsonElement value,string name="id",bool allowZero=false)
    {
        if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty(name,out var data)||data.ValueKind!=JsonValueKind.Number||
            !data.TryGetInt32(out var id)||id<(allowZero?0:1))throw Invalid(name);
        return id;
    }
    internal static bool Flag(JsonElement value,string name,bool optional=false)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(name);
        if(!value.TryGetProperty(name,out var data)){if(optional)return false;throw Invalid(name);}
        if(data.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(name);
        return data.GetBoolean();
    }
    internal static string[] Strings(JsonElement value,string name,int maximum,int maximumText,bool optional=false)=>Array(value,name,maximum,optional).Select(item=>
        item.ValueKind==JsonValueKind.String&&item.GetString()!.Length<=maximumText?item.GetString()!:throw Invalid(name)).ToArray();
    internal static void Unique<T>(IEnumerable<T> values,string name) where T:notnull
    {var seen=new HashSet<T>();foreach(var value in values)if(!seen.Add(value))throw Invalid(name);}
    internal static IOException Invalid(string field)=>new("PoB returned invalid or incomplete "+field+" data. Retry or update the widget and engine together.");
}

internal sealed record PobSkillChoice(int Id,string Title,bool Active);
internal sealed record PobSkillOption(string Id,string Title,bool Numeric,string Value,IReadOnlyList<PobSkillChoice> Choices);
internal sealed record PobSkillOptions(int Group,IReadOnlyList<PobSkillOption> Options)
{
    internal static PobSkillOptions Read(JsonElement state,int group)
    {
        if(PobAdapterData.Id(state,"skillOptionsGroup")!=group)throw PobAdapterData.Invalid("skill group");
        var values=PobAdapterData.Array(state,"skillOptions",64).Select(option=>
        {
            var id=PobAdapterData.Text(option,"id",128,blank:false);
            if(id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'&&c!='-'))throw PobAdapterData.Invalid("skill option identifier");
            var kind=PobAdapterData.Text(option,"kind",32,optional:true);
            if(kind=="number")
            {
                var value=PobAdapterData.Text(option,"value",9);
                if(value.Any(c=>!char.IsAsciiDigit(c)))throw PobAdapterData.Invalid("numeric skill option");
                return new PobSkillOption(id,PobAdapterData.Text(option,"title",512,blank:false),true,value,[]);
            }
            if(kind.Length!=0)throw PobAdapterData.Invalid("skill option kind");
            var choices=PobAdapterData.Array(option,"options",512).Select(choice=>new PobSkillChoice(PobAdapterData.Id(choice),
                PobAdapterData.Text(choice,"title",2048,blank:false),PobAdapterData.Flag(choice,"active"))).ToArray();
            if(choices.Length==0||choices.Count(c=>c.Active)>1)throw PobAdapterData.Invalid("skill option choices");
            PobAdapterData.Unique(choices.Select(c=>c.Id),"skill option choice identity");
            return new PobSkillOption(id,PobAdapterData.Text(option,"title",512,optional:true),false,"",choices);
        }).ToArray();
        PobAdapterData.Unique(values.Select(v=>v.Id),"skill option identity");
        return new(group,values);
    }
}

internal sealed record PobSearchInventoryItem(int Id,string Title,string Base,string Rarity);
internal sealed record PobSearchInventory(IReadOnlyList<PobSearchInventoryItem> Items)
{
    internal static PobSearchInventory Read(JsonElement state)
    {
        var items=PobAdapterData.Array(state,"inventory",20000).Select(item=>new PobSearchInventoryItem(PobAdapterData.Id(item),
            PobAdapterData.Text(item,"title",4096,blank:false),PobAdapterData.Text(item,"base",4096),PobAdapterData.Text(item,"rarity",64))).ToArray();
        PobAdapterData.Unique(items.Select(i=>i.Id),"inventory identity");return new(items);
    }
}
internal sealed record PobSearchCalculation(string Id,string Title,string Value,string Section,string SectionTitle,IReadOnlyList<string> Outputs);
internal sealed record PobSearchCalculations(IReadOnlyList<PobSearchCalculation> Cells)
{
    internal static PobSearchCalculations Read(JsonElement state)
    {
        var sections=PobAdapterData.Array(state,"sections",512);var cells=new List<PobSearchCalculation>();var ids=new List<string>();
        foreach(var section in sections)
        {
            var id=PobAdapterData.Text(section,"id",256,blank:false);ids.Add(id);
            var title=PobAdapterData.Text(section,"title",4096);
            foreach(var cell in PobAdapterData.Array(section,"items",4096))
            {
                cells.Add(new(PobAdapterData.Text(cell,"id",256,blank:false),PobAdapterData.Text(cell,"title",4096,blank:false),
                    PobAdapterData.Text(cell,"value",4096),id,title,PobAdapterData.Strings(cell,"outputs",128,256)));
                if(cells.Count>20000)throw PobAdapterData.Invalid("calculation count");
            }
        }
        PobAdapterData.Unique(ids,"calculation section identity");PobAdapterData.Unique(cells.Select(c=>c.Id),"calculation cell identity");
        return new(cells);
    }
}
