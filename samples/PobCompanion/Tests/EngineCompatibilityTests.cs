using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;

internal static class EngineCompatibilityTests
{
    internal static async Task RunAsync(string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var loaded=await engine.ExecuteAsync(new{op="load",xml,name="Engine compatibility"},default);
        var lines=loaded.State.GetProperty("items").EnumerateArray().SelectMany(i=>i.GetProperty("details").EnumerateArray())
            .SelectMany(g=>g.GetProperty("lines").EnumerateArray()).Select(l=>l.GetProperty("text").GetString()!).ToArray();
        check(lines.Length>0&&!lines.Any(l=>l.Contains("could not be formatted")),"Engine constructs item tooltips and formats equipped item details");
        if(!loaded.State.GetProperty("skillWeaponSets").GetBoolean())return;
        var doc=XDocument.Parse(loaded.Xml);
        foreach(var set in doc.Descendants("ConfigSet"))
        {
            set.Elements("CustomModifierBlock").Remove();
            set.Add(new XElement("CustomModifierBlock",new XAttribute("title","Enabled fixture"),new XAttribute("enabled",true),"+123 to maximum Life"),
                new XElement("CustomModifierBlock",new XAttribute("title","Disabled fixture"),new XAttribute("enabled",false),"+999 to maximum Life"));
        }
        var blocks=await engine.ExecuteAsync(new{op="load",xml=doc.ToString(),name="Modifier blocks"},default);
        var descriptions=blocks.State.GetProperty("customModGroups").EnumerateArray().ToArray();
        check(descriptions.Length==2&&descriptions[0].GetProperty("title").GetString()=="Enabled fixture"&&descriptions[1].GetProperty("title").GetString()=="Disabled fixture"&&
            descriptions[0].GetProperty("text").GetString()=="+123 to maximum Life"&&descriptions[1].GetProperty("text").GetString()=="+999 to maximum Life","Beta custom modifier blocks retain title, text and enabled status in presentation");
        var reloaded=await engine.ExecuteAsync(new{op="load",xml=blocks.Xml,name="Reload blocks"},default);
        check(XNode.DeepEquals(XDocument.Parse(blocks.Xml).Root!.Element("Config"),XDocument.Parse(reloaded.Xml).Root!.Element("Config")),"Custom modifier blocks survive save and engine reload without flattening");
        static double Life(JsonElement state)=>state.GetProperty("stats").EnumerateArray().Single(s=>s.GetProperty("name").GetString()=="Life").GetProperty("value").GetDouble();
        var withoutDisabled=XDocument.Parse(blocks.Xml);
        withoutDisabled.Descendants("CustomModifierBlock").Where(b=>(string?)b.Attribute("enabled")=="false").Remove();
        var enabledOnly=await engine.ExecuteAsync(new{op="load",xml=withoutDisabled.ToString(),name="Enabled only"},default);
        check(Life(enabledOnly.State)==Life(blocks.State),"Disabled custom modifiers do not affect calculations");
        var legacy=XDocument.Parse(loaded.Xml);
        foreach(var set in legacy.Descendants("ConfigSet"))
        {
            set.Elements("CustomModifierBlock").Remove();
            set.Add(new XElement("Input",new XAttribute("name","customMods"),new XAttribute("string","+123 to maximum Life")));
        }
        var migrated=await engine.ExecuteAsync(new{op="load",xml=legacy.ToString(),name="Legacy modifiers"},default);
        check(migrated.State.GetProperty("customModGroups").EnumerateArray().Any(c=>c.GetProperty("text").GetString()=="+123 to maximum Life")&&Life(migrated.State)==Life(enabledOnly.State),"Legacy custom modifiers migrate with identical calculation effect");
    }
}
