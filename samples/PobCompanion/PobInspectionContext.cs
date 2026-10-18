using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobMainSkillWarning(int Group,string Skill,string Reason)
{
    internal static PobMainSkillWarning? Read(JsonElement state)
    {
        if(state.ValueKind!=JsonValueKind.Object||!PobAdapterData.Flag(state,"mainSkillDisabled",optional:true))return null;
        var reason=PobAdapterData.Text(state,"mainSkillDisabledReason",4096,optional:true);
        if(string.IsNullOrWhiteSpace(reason))return null;
        var main=PobAdapterData.Array(state,"skills",4096).Where(g=>PobAdapterData.Flag(g,"main",optional:true)).ToArray();
        if(main.Length!=1)return null; // No guessed destination if the adapter did not identify one exact main group.
        return new(PobAdapterData.Id(main[0]),PobAdapterData.Text(state,"mainSkill",512),reason);
    }
}

internal sealed record PobInspectionContext(string Loadout,string WeaponSet,string GroupWeaponSets)
{
    internal static PobInspectionContext Read(JsonElement state,string xml,JsonElement group)
    {
        var active=PobAdapterData.Array(state,"loadouts",512,optional:true).Where(l=>PobAdapterData.Flag(l,"active")).ToArray();
        var loadout=active.Length==1?PobAdapterData.Text(active[0],"title",512):"";
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=BuildDocument.MaximumBytes});
        var document=XDocument.Load(reader);var items=document.Root?.Element("Items");
        var selectedId=(string?)items?.Attribute("activeItemSet");
        var selected=items?.Elements("ItemSet").FirstOrDefault(set=>(string?)set.Attribute("id")==selectedId);
        var second=(string?)(selected??items)?.Attribute("useSecondWeaponSet");
        var weapon=second is "true" or "false"?(second=="true"?"Weapon set 2":"Weapon set 1"):"";
        var enabled=PobAdapterData.Array(group,"weaponSets",2,optional:true).Where(s=>PobAdapterData.Flag(s,"enabled"))
            .Select(s=>"Weapon set "+PobAdapterData.Id(s)).ToArray();
        return new(loadout,weapon,enabled.Length==0?"":string.Join(" + ",enabled));
    }
    internal string Summary=>string.Join(" · ",new[]{Loadout.Length>0?"Loadout: "+Loadout:null,
        WeaponSet.Length>0?"Calculation: "+WeaponSet:null,GroupWeaponSets.Length>0?"Group: "+GroupWeaponSets:null}.Where(s=>s is not null));
}
