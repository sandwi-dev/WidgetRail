using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;

internal static class EngineImportTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        const string duplicated="<PathOfBuilding2><Skills><Skill><Gem gemId='active'/><Gem gemId='support'/></Skill><Skill source='Item:1'><Gem gemId='active'/></Skill></Skills></PathOfBuilding2>";
        const string merged="<PathOfBuilding2><Skills><Skill source='Item:1'><Gem gemId='active'/><Gem gemId='support'/></Skill></Skills></PathOfBuilding2>";
        BuildDocument.VerifySkillPreservation(duplicated,merged);check(true,"Import guard permits merging a duplicate generated base skill");
        var rejected=false;try{BuildDocument.VerifySkillPreservation(duplicated,merged.Replace("<Gem gemId='support'/>",""));}catch(IOException){rejected=true;}
        check(rejected,"Import guard still rejects losing authored supports during a generated-group merge");
        rejected=false;try{BuildDocument.VerifySkillPreservation(duplicated.Replace("source='Item:1'",""),merged);}catch(IOException){rejected=true;}
        check(rejected,"Import guard does not excuse lost duplicate authored gems");
        await using var engine=new PobEngine(root,runtime,adapter);
        check(BuildDocument.Decode(BuildDocument.Encode(xml),game)==xml,"Standard share code decodes without a site-specific adapter");
        var loaded=await engine.ExecuteAsync(new{op="load",xml,name="External share code"},default);
        if(Environment.GetEnvironmentVariable("POB_IMPORT_OUTPUT") is {} path)File.WriteAllText(path,loaded.Xml);
        BuildDocument.VerifySkillPreservation(xml,loaded.Xml);check(true,"Imported authored gem identities survive engine normalization");
        var before=XDocument.Parse(xml);var after=XDocument.Parse(loaded.Xml);
        check(before.Descendants("Item").Select(i=>(string?)i.Attribute("id")).SequenceEqual(after.Descendants("Item").Select(i=>(string?)i.Attribute("id"))),"Imported item identities are retained");
        var reloaded=await engine.ExecuteAsync(new{op="load",xml=loaded.Xml,name="Import round trip"},default);
        static Dictionary<string,double> Stats(JsonElement state)=>state.GetProperty("stats").EnumerateArray().ToDictionary(s=>s.GetProperty("name").GetString()!,s=>s.GetProperty("value").GetDouble());
        var current=Stats(reloaded.State);
        check(Stats(loaded.State).All(s=>current.TryGetValue(s.Key,out var v)&&Math.Abs(v-s.Value)<Math.Max(1,Math.Abs(v))*1e-8),"Export and reimport retain recalculated totals");
        Console.WriteLine(JsonSerializer.Serialize(new{level=loaded.State.GetProperty("level"),characterClass=loaded.State.GetProperty("class"),ascendancy=loaded.State.GetProperty("ascendancy"),stats=Stats(loaded.State)}));
    }
}
