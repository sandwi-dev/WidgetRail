using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;

internal static class TradeItemTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var baseline=await engine.ExecuteAsync(new{op="load",xml,name="Trade conversion coverage"},default);
        JsonObject Item(string name,string type,string rarity="Rare")=>new(){["name"]=name,["typeLine"]=type,["rarity"]=rarity,["ilvl"]=80};
        JsonArray Mods(params string[] lines)=>new(lines.Select(line=>(JsonNode)new JsonObject{["description"]=line}).ToArray());
        JsonObject Property(string name,string value)=>new(){["name"]=name,["values"]=new JsonArray(new JsonArray(value,0))};
        async Task<string> Convert(JsonObject item)
        {
            var entry=new JsonObject{["id"]=new string('a',64),["item"]=item,["listing"]=new JsonObject()};
            // Compare against the loaded snapshot: upstream may normalize XML
            // during restore, independently of the conversion being tested.
            var before=await engine.ExecuteAsync(new{op="snapshot"},default,baseline.Xml);
            var next=await engine.ExecuteAsync(new{op="trade-item",entry},default,before.Xml);
            check(next.Xml==before.Xml,"Trade conversion leaves build XML and skill groups untouched: "+item["typeLine"]);
            return next.State.GetProperty("raw").GetString()!;
        }
        var ring=Item("Test Signet","Gold Ring");ring["implicitMods"]=Mods("15% increased Rarity of Items found");
        ring["explicitMods"]=new JsonArray(new JsonObject{["description"]="+60 to maximum Life",["flags"]=new JsonObject{["fractured"]=true}},
            new JsonObject{["description"]="+30% to Fire Resistance",["flags"]=new JsonObject{["crafted"]=true}});
        if(game==1)ring["influences"]=new JsonObject{["shaper"]=true};
        var raw=await Convert(ring);
        check(raw.Contains("{fractured}+60 to maximum Life")&&raw.Contains("{crafted}+30% to Fire Resistance"),"Fractured and crafted flags survive upstream import");
        if(game==1)check(raw.Contains("Shaper Item"),"PoE1 influence survives item conversion");
        var actual=await engine.ExecuteAsync(new{op="item",slot="Ring 1",raw},default,baseline.Xml,compare:true);
        var canonical="Rarity: RARE\nTest Signet\nGold Ring\nItem Level: 80\n"+(game==1?"Shaper Item\n":"")+
            "Implicits: 1\n15% increased Rarity of Items found\n{fractured}+60 to maximum Life\n{crafted}+30% to Fire Resistance";
        var expected=await engine.ExecuteAsync(new{op="item",slot="Ring 1",raw=canonical},default,baseline.Xml,compare:true);
        check(actual.State.GetProperty("stats").GetRawText()==expected.State.GetProperty("stats").GetRawText()&&
            actual.State.GetProperty("comparison").GetRawText()==expected.State.GetProperty("comparison").GetRawText(),
            "Trade item calculations and comparison equal independently authored PoB item text");
        var unknown=(JsonObject)ring.DeepClone();unknown["futureMods"]=Mods("+60 to maximum Life");
        var omitted=false;
        try{await Convert(unknown);}catch(PobCommandException failure){omitted=failure.Message.Contains("omitted a modifier",StringComparison.Ordinal);}
        check(omitted,"An omitted modifier cannot hide behind an identical description in another group");
        var bow=Item("Quill Rain",game==1?"Short Bow":"Shortbow","Unique");
        bow["properties"]=new JsonArray(Property("Quality","+20%"));bow["corrupted"]=true;
        bow["explicitMods"]=Mods("100% increased Attack Speed","+14 to Dexterity","40% less Attack Damage");
        if(game==1)bow["sockets"]=new JsonArray(new JsonObject{["group"]=0,["sColour"]="R"},new JsonObject{["group"]=0,["sColour"]="G"},new JsonObject{["group"]=1,["sColour"]="B"},new JsonObject{["group"]=1,["sColour"]="B"});
        else{bow["sockets"]=new JsonArray(new JsonObject(),new JsonObject());bow["runeMods"]=Mods("+120 to Accuracy Rating","Leeches 4% of Physical Damage as Mana","Bonded: 5% increased maximum Mana","Bonded: Attacks have +1% to Critical Hit Chance");}
        raw=await Convert(bow);
        check(raw.Contains("Quality: 20")&&raw.Contains("Corrupted")&&raw.Contains("100% increased Attack Speed"),"Unique weapon quality, corruption and attack modifier survive conversion");
        check(game==1?raw.Contains("Sockets: R-G B-B"):raw.Contains("Bonded: 5% increased maximum Mana")&&raw.Contains("{rune}"),game==1?"Separate PoE1 socket groups remain separate":"PoE2 rune and Bonded modifiers remain present");
        if(game==2)
        {
            var armour=Item("","Runeforged Rusted Cuirass","Normal");armour["properties"]=new JsonArray(Property("Armour","45"),Property("Runic Ward","37"));
            raw=await Convert(armour);check(raw.Contains("Ward: 37"),"PoE2 Runic Ward uses the listed defence value rather than a default base roll");
            var sceptre=Item("","Rattling Sceptre","Normal");sceptre["grantedSkills"]=new JsonArray(Property("Grants Skill","Level 12 Skeletal Warrior Minion"));
            raw=await Convert(sceptre);check(raw.Contains("Grants Skill: Level 12 Skeletal Warrior Minion"),"PoE2 granted skill level survives trade conversion");
        }
        else
        {
            var jewel=Item("Test Eye","Cobalt Jewel");jewel["explicitMods"]=Mods("7% increased maximum Life");
            raw=await Convert(jewel);check(raw.Contains("7% increased maximum Life"),"PoE1 jewel modifier survives conversion");
        }
    }
}
