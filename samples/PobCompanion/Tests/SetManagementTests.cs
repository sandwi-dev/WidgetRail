using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class SetManagementTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        foreach(var(kind,field)in new[]{("tree","trees"),("items","itemSets"),("skills","skillSets"),("config","configSets")})
        {
            var state=await engine.ExecuteAsync(new{op="load",xml,name="Set management"},default);
            JsonElement[] Sets()=>state.State.GetProperty(field).EnumerateArray().ToArray();
            var count=Sets().Length;var source=Sets().First().GetProperty("id").GetInt32();
            state=await engine.ExecuteAsync(new{op="set-manage",kind,id=source,mode="copy",name="Independent copy"},default,state.Xml);
            var copy=state.State.GetProperty("editedSetId").GetInt32();
            check(copy!=source&&Sets().Length==count+1&&Sets().Single(s=>s.GetProperty("id").GetInt32()==copy).GetProperty("title").GetString()=="Independent copy",kind+": copy has its own identity and name");
            state=await engine.ExecuteAsync(new{op="set-manage",kind,id=copy,mode="rename",name="Renamed copy"},default,state.Xml);
            check(Sets().Any(s=>s.GetProperty("title").GetString()=="Renamed copy"),kind+": rename uses the selected set");
            state=await engine.ExecuteAsync(new{op="set",kind,id=copy},default,state.Xml);
            check(Sets().Single(s=>s.GetProperty("id").GetInt32()==copy).GetProperty("active").GetBoolean(),kind+": copied set can be activated");
            var saved=state.Xml;
            state=await engine.ExecuteAsync(new{op="set-manage",kind,id=copy,mode="delete",name=""},default,state.Xml);
            check(Sets().Length==count&&Sets().Any(s=>s.GetProperty("active").GetBoolean()),kind+": deleting active copy leaves a valid active set");
            state=await engine.ExecuteAsync(new{op="load",xml=saved,name="Undo"},default);
            check(Sets().Any(s=>s.GetProperty("title").GetString()=="Renamed copy"),kind+": XML history restores deleted set");
            state=await engine.ExecuteAsync(new{op="set-manage",kind,id=source,mode="new",name="Empty set"},default,state.Xml);
            check(Sets().Length==count+2&&Sets().Any(s=>s.GetProperty("title").GetString()=="Empty set"),kind+": create empty set");
            // Upstream writes nil weapon-set flags for a newly empty item set;
            // its loader canonicalizes those to false. Compare after that normal round trip.
            state=await engine.ExecuteAsync(new{op="load",xml=state.Xml,name="Set round trip"},default);
            var before=state.Xml;var rejected=false;
            try{await engine.ExecuteAsync(new{op="set-manage",kind,id=source,mode="new",name="Empty set"},default,before);}catch(IOException){rejected=true;}
            state=await engine.ExecuteAsync(new{op="snapshot"},default,before);
            check(rejected&&WorkStateTests.SameBuild(before,state.Xml),kind+": duplicate name fails without changing the build");
        }
    }
}
