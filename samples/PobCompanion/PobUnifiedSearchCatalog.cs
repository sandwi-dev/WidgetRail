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
        var nodes=PobAdapterData.Array(state,"nodes",30000);var skills=PobAdapterData.Array(state,"skills",4096);
        var equipment=PobAdapterData.Array(state,"items",1024);var warnings=PobAdapterData.Strings(state,"warnings",1024,4096);
        PobAdapterData.Unique(nodes.Select(n=>PobAdapterData.Id(n)),"passive identity");
        PobAdapterData.Unique(skills.Select(n=>PobAdapterData.Id(n)),"skill group identity");
        PobAdapterData.Unique(equipment.Select(n=>PobAdapterData.Id(n)),"equipment identity");
        var stored=inventory.ValueKind==JsonValueKind.Undefined?null:PobSearchInventory.Read(inventory);
        var detailed=calculations.ValueKind==JsonValueKind.Undefined?null:PobSearchCalculations.Read(calculations);
        var entries=new List<PobSearchResult>();
        void Add(string title,string category,string context,string description,PobSearchTarget target)
        {
            if(string.IsNullOrWhiteSpace(title))return;
            var key=WidgetIds.Scope("unified").KeyedId("result",target.Kind+"|"+target.Key+"|"+target.Id+"|"+target.Child+"|"+target.Section);
            entries.Add(new(key,title,category,context,title+" "+context+" "+description,target));
        }
        foreach(var node in nodes.Where(n=>Flag(n,"interactive")))
            Add(Text(node,"name"),"Passives",(Flag(node,"allocated")?"Allocated":Flag(node,"granted")?"Granted":"Unallocated")+" · Node "+Number(node,"id"),
                Text(node,"description"),new(PobSearchTargetKind.Passive,"",Number(node,"id")));
        foreach(var group in skills)
        {
            var id=Number(group,"id");var title=Text(group,"title");
            Add(title,"Skills",Flag(group,"main")?"Main skill group":"Skill group",Text(group,"slot"),new(PobSearchTargetKind.Skill,"",id));
            var gems=PobAdapterData.Array(group,"gems",256);PobAdapterData.Unique(gems.Select(g=>PobAdapterData.Id(g)),"gem identity");
            foreach(var gem in gems)
                Add(Text(gem,"name"),"Gems",title+" · Gem "+Number(gem,"id"),"",new(PobSearchTargetKind.Gem,"",id,Number(gem,"id")));
        }
        foreach(var item in equipment)
        {
            var slot=Text(item,"slot");var target=new PobSearchTarget(PobSearchTargetKind.EquippedItem,Number(item,"itemId").ToString(),Number(item,"id"));
            Add(Text(item,"name"),"Equipment",slot,Text(item,"base")+" "+Text(item,"raw"),target);
            foreach(var (warning,index) in PobAdapterData.Strings(item,"warnings",4096,4096,optional:true).Select((w,i)=>(w,i)))
                Add(warning,"Warnings",Text(item,"name")+" · "+slot+" · Unmodelled modifier","",
                    target with{Section="warning."+index});
        }
        foreach(var item in stored?.Items??[])
            Add(item.Title,"Inventory",item.Base,item.Rarity,new(PobSearchTargetKind.InventoryItem,"",item.Id));
        foreach(var option in configuration.Options)
            Add(option.Title,"Configuration",option.Section,option.Description,new(PobSearchTargetKind.Configuration,option.Id,option.Index,Section:option.Section));
        foreach(var cell in detailed?.Cells??[])
            Add(cell.Title,"Calculations",cell.SectionTitle+" · "+cell.Value,string.Join(" ",cell.Outputs),
                new(PobSearchTargetKind.Calculation,cell.Id,Section:cell.Section));
        foreach(var build in saved)Add(build.Name,"Saved builds","Saved build","",new(PobSearchTargetKind.SavedBuild,build.Id));
        foreach(var (key,title) in new[]{("inventory","Item inventory"),("library","Saved builds"),("calcs","Calculation overview"),("skills","Skill groups"),
            ("items","Equipped items"),("configuration","Configuration"),("tree","Passive tree"),("notes","Build notes"),("sets","Manage sets")})
            Add(title,"Navigation","Open page","",new(PobSearchTargetKind.Navigation,key));
        foreach(var (warning,index) in warnings.Select((w,i)=>(w,i)))
            Add(warning,"Warnings","Build warning · no supplied target","",new(PobSearchTargetKind.Warning,"warning."+index));
        if(PobMainSkillWarning.Read(state) is {} disabled)
            Add(disabled.Reason,"Warnings",disabled.Skill+" · Main skill disabled","",new(PobSearchTargetKind.Skill,"main-disabled",disabled.Group));
        Entries=entries.AsReadOnly();Categories=entries.Select(e=>e.Category).Distinct().ToArray();
    }
    internal PobSearchResult[] Match(string text,string category)
    {
        var terms=text.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        return Entries.Where(e=>(category=="All"||e.Category==category)&&terms.All(term=>e.SearchText.Contains(term,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e=>text.Length==0?0:e.Title.StartsWith(text,StringComparison.OrdinalIgnoreCase)?0:e.Title.Contains(text,StringComparison.OrdinalIgnoreCase)?1:2)
            .ToArray();
    }
    private static string Text(JsonElement value,string field)=>PobAdapterData.Text(value,field,field=="raw"?32768:4096,optional:field is "slot" or "description" or "raw" or "base");
    private static int Number(JsonElement value,string field)=>PobAdapterData.Id(value,field);
    private static bool Flag(JsonElement value,string field)=>PobAdapterData.Flag(value,field,optional:true);
}
