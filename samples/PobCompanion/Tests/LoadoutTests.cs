using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class LoadoutTests
{
    internal static async Task RunAsync(string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var original=await engine.ExecuteAsync(new{op="load",xml,name="Loadout audit"},default);
        var originalName=original.State.GetProperty("loadouts").EnumerateArray().First(l=>l.GetProperty("active").GetBoolean()).GetProperty("name").GetString();
        var copy=await engine.ExecuteAsync(new{op="loadout-create",name="Audit Midgame"},default,original.Xml);
        foreach(var key in new[]{"trees","itemSets","skillSets","configSets"})
            check(copy.State.GetProperty(key).GetArrayLength()>original.State.GetProperty(key).GetArrayLength(),"Loadout independently copies "+key);
        foreach(var loadout in original.State.GetProperty("loadouts").EnumerateArray())
            check(copy.State.GetProperty("loadouts").EnumerateArray().Any(l=>l.GetProperty("name").GetString()==loadout.GetProperty("name").GetString()),"Existing loadout remains available: "+loadout.GetProperty("title").GetString());
        check(copy.State.GetProperty("loadouts").EnumerateArray().Any(l=>l.GetProperty("title").GetString()=="Audit Midgame"&&l.GetProperty("active").GetBoolean()),"Created loadout is selected");
        var condition=copy.State.GetProperty("conditions").EnumerateArray().First(c=>c.GetProperty("value").ValueKind is JsonValueKind.True or JsonValueKind.False);
        var keyName=condition.GetProperty("id").GetString();var old=condition.GetProperty("value").GetBoolean();
        var edited=await engine.ExecuteAsync(new{op="config",key=keyName,value=!old},default,copy.Xml);
        var source=await engine.ExecuteAsync(new{op="loadout",name=originalName},default,edited.Xml);
        check(source.State.GetProperty("conditions").EnumerateArray().Single(c=>c.GetProperty("id").GetString()==keyName).GetProperty("value").GetBoolean()==old,"Editing copied configuration does not change source loadout");
        var reload=await engine.ExecuteAsync(new{op="load",xml=source.Xml,name="Reload"},default);
        var restored=await engine.ExecuteAsync(new{op="loadout",name="Audit Midgame"},default,reload.Xml);
        check(restored.State.GetProperty("conditions").EnumerateArray().Single(c=>c.GetProperty("id").GetString()==keyName).GetProperty("value").GetBoolean()!=old,"Copied configuration survives save and reload");
        var rejected=false;
        try{await engine.ExecuteAsync(new{op="loadout-create",name="Audit Midgame"},default,restored.Xml);}catch(IOException){rejected=true;}
        check(rejected,"Duplicate loadout names are rejected");
        var unchanged=await engine.ExecuteAsync(new{op="snapshot"},default);
        check(unchanged.State.GetProperty("skillSets").GetArrayLength()==restored.State.GetProperty("skillSets").GetArrayLength(),"Rejected creation does not add partial sets");
    }
}
