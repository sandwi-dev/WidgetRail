using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

internal enum PobSearchTargetKind { Passive, Skill, Gem, EquippedItem, InventoryItem, Configuration, Calculation, SavedBuild, Navigation, Warning }
internal sealed record PobSearchTarget(PobSearchTargetKind Kind,string Key,int Id=0,int Child=0,string Section="");
internal sealed record PobSearchResult(string Key,string Title,string Category,string Context,string SearchText,PobSearchTarget Target);

/// <summary>Read-only identities and search terms from one exact engine projection. No commands or effects.</summary>
internal sealed class PobUnifiedSearchCatalog
{
    internal IReadOnlyList<PobSearchResult> Entries { get; }
    internal IReadOnlyList<string> Categories { get; }
    internal PobUnifiedSearchCatalog(JsonElement state,PobConfigurationCatalog configuration,JsonElement inventory,JsonElement calculations,
        IEnumerable<(string Id,string Name)> saved)
    {
        var entries=new List<PobSearchResult>();
        void Add(string title,string category,string context,string description,PobSearchTarget target)
        {
            if(string.IsNullOrWhiteSpace(title))return;
            var key=WidgetIds.Scope("unified").KeyedId("result",target.Kind+"|"+target.Key+"|"+target.Id+"|"+target.Child+"|"+target.Section);
            entries.Add(new(key,title,category,context,title+" "+context+" "+description,target));
        }
        foreach(var node in Array(state,"nodes").Where(n=>Flag(n,"interactive")))
            Add(Text(node,"name"),"Passives",(Flag(node,"allocated")?"Allocated":Flag(node,"granted")?"Granted":"Unallocated")+" · Node "+Number(node,"id"),
                Text(node,"description"),new(PobSearchTargetKind.Passive,"",Number(node,"id")));
        foreach(var group in Array(state,"skills"))
        {
            var id=Number(group,"id");var title=Text(group,"title");
            Add(title,"Skills",Flag(group,"main")?"Main skill group":"Skill group",Text(group,"slot"),new(PobSearchTargetKind.Skill,"",id));
            foreach(var gem in Array(group,"gems"))
                Add(Text(gem,"name"),"Gems",title+" · Gem "+Number(gem,"id"),"",new(PobSearchTargetKind.Gem,"",id,Number(gem,"id")));
        }
        foreach(var item in Array(state,"items"))
        {
            var slot=Text(item,"slot");var target=new PobSearchTarget(PobSearchTargetKind.EquippedItem,Number(item,"itemId").ToString(),Number(item,"id"));
            Add(Text(item,"name"),"Equipment",slot,Text(item,"base")+" "+Text(item,"raw"),target);
            foreach(var (warning,index) in Array(item,"warnings").Select((w,i)=>(w,i)))
                Add(warning.GetString()??"","Warnings",Text(item,"name")+" · "+slot+" · Unmodelled modifier","",
                    target with{Section="warning."+index});
        }
        foreach(var item in Array(inventory,"inventory"))
            Add(Text(item,"title"),"Inventory",Text(item,"base"),Text(item,"rarity"),new(PobSearchTargetKind.InventoryItem,"",Number(item,"id")));
        foreach(var option in configuration.Options)
            Add(option.Title,"Configuration",option.Section,option.Description,new(PobSearchTargetKind.Configuration,option.Id,option.Index,Section:option.Section));
        foreach(var section in Array(calculations,"sections"))
            foreach(var cell in Array(section,"items"))
                Add(Text(cell,"title"),"Calculations",Text(section,"title")+" · "+Text(cell,"value"),string.Join(" ",Array(cell,"outputs").Select(v=>v.GetString())),
                    new(PobSearchTargetKind.Calculation,Text(cell,"id"),Section:Text(section,"id")));
        foreach(var build in saved)Add(build.Name,"Saved builds","Saved build","",new(PobSearchTargetKind.SavedBuild,build.Id));
        foreach(var (key,title) in new[]{("inventory","Item inventory"),("library","Saved builds"),("calcs","Calculation overview"),("skills","Skill groups"),
            ("items","Equipped items"),("configuration","Configuration"),("tree","Passive tree"),("notes","Build notes"),("sets","Manage sets")})
            Add(title,"Navigation","Open page","",new(PobSearchTargetKind.Navigation,key));
        foreach(var (warning,index) in Array(state,"warnings").Select((w,i)=>(w,i)))
            Add(warning.GetString()??"","Warnings","Build warning · no supplied target","",new(PobSearchTargetKind.Warning,"warning."+index));
        Entries=entries.AsReadOnly();Categories=entries.Select(e=>e.Category).Distinct().ToArray();
    }
    internal PobSearchResult[] Match(string text,string category)
    {
        var terms=text.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        return Entries.Where(e=>(category=="All"||e.Category==category)&&terms.All(term=>e.SearchText.Contains(term,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e=>text.Length==0?0:e.Title.StartsWith(text,StringComparison.OrdinalIgnoreCase)?0:e.Title.Contains(text,StringComparison.OrdinalIgnoreCase)?1:2)
            .ToArray();
    }
    private static JsonElement[] Array(JsonElement value,string field)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(field,out var array)&&array.ValueKind==JsonValueKind.Array?array.EnumerateArray().ToArray():[];
    private static string Text(JsonElement value,string field)=>value.TryGetProperty(field,out var text)&&text.ValueKind==JsonValueKind.String?text.GetString()!:"";
    private static int Number(JsonElement value,string field)=>value.TryGetProperty(field,out var number)&&number.TryGetInt32(out var result)?result:0;
    private static bool Flag(JsonElement value,string field)=>value.TryGetProperty(field,out var flag)&&flag.ValueKind==JsonValueKind.True;
}
