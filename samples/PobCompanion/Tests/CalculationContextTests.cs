using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;

internal static class CalculationContextTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var doc=XDocument.Parse(xml);
        var skills=doc.Descendants("SkillSet").First();
        var first=skills.Elements("Skill").First();
        var second=new XElement(first);second.SetAttributeValue("label","Low level inspection fixture");
        foreach(var gem in second.Elements("Gem"))gem.SetAttributeValue("level",1);
        skills.Add(second);
        var mainIndex=skills.Elements("Skill").Count();
        doc.Root!.Element("Build")!.SetAttributeValue("mainSocketGroup",mainIndex);
        var input=doc.Descendants("Calcs").First().Elements("Input").FirstOrDefault(e=>(string?)e.Attribute("name")=="skill_number");
        if(input is null)doc.Descendants("Calcs").First().Add(new XElement("Input",new XAttribute("name","skill_number"),new XAttribute("number",1)));
        else input.SetAttributeValue("number",1);
        await using var engine=new PobEngine(root,runtime,adapter);
        var baseline=await engine.ExecuteAsync(new{op="load",xml=doc.ToString(),name="Independent Calcs context"},default);
        object Context(int group=0,string mode="EFFECTIVE",bool minion=false)=>new{group,skill=0,mode,minion};
        async Task<PobResult> Catalog(object context)=>await engine.ExecuteAsync(new{op="calculation-catalog",context},default,baseline.Xml);
        string? Dps(JsonElement state)=>state.GetProperty("sections").EnumerateArray().SelectMany(s=>s.GetProperty("items").EnumerateArray())
            .FirstOrDefault(c=>c.GetProperty("outputs").EnumerateArray().Any(o=>o.GetString()=="TotalDPS")).GetProperty("value").GetString();
        var following=await Catalog(Context());
        check(following.State.GetProperty("context").GetProperty("group").GetInt32()==mainIndex,"Default detailed context follows the main group rather than saved Calcs group");
        var overridden=await Catalog(Context(1));
        check(overridden.State.GetProperty("context").GetProperty("group").GetInt32()==1&&Dps(following.State)!=Dps(overridden.State),"Explicit skill-group inspection recalculates the selected group's damage");
        check(WorkStateTests.SameBuild(baseline.Xml,following.Xml)&&WorkStateTests.SameBuild(baseline.Xml,overridden.Xml),"Inspection leaves the main skill and saved desktop Calcs selection unchanged");
        var unbuffed=await Catalog(Context(mode:"UNBUFFED"));
        check(unbuffed.State.GetProperty("context").GetProperty("mode").GetString()=="UNBUFFED","Buff mode uses the requested upstream calculation context");
        var restored=await engine.ExecuteAsync(new{op="calculation-catalog"},default,baseline.Xml);
        check(restored.State.GetProperty("context").GetProperty("group").GetInt32()==1,"Temporary inspection restores the original desktop Calcs context");
        foreach(var invalid in new[]{Context(99999),Context(mode:"INVALID"),Context(minion:true)})
        {
            var rejected=false;try{await Catalog(invalid);}catch(PobCommandException){rejected=true;}
            check(rejected,"Invalid or unavailable calculation context is rejected");
            var snapshot=await engine.ExecuteAsync(new{op="snapshot"},default);
            check(WorkStateTests.SameBuild(baseline.Xml,snapshot.Xml),"Failed context change restores the original build");
        }
    }
}
