using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class TreeEditingTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var loaded=await engine.ExecuteAsync(new{op="load",xml,name="Tree tools"},default);
        if(game==2)
        {
            check(loaded.State.GetProperty("weaponTreeModes").GetBoolean(),"PoE2 exposes weapon-set allocation modes");
            check(!loaded.State.GetProperty("timelessFinder").GetBoolean(),"PoE2 does not expose its disabled legacy Timeless finder");
            check(loaded.State.GetProperty("passiveBudgets").GetArrayLength()==4&&loaded.State.GetProperty("passivePoints").GetInt32()==loaded.State.GetProperty("passiveBudgets")[0].GetProperty("used").GetInt32(),"Compact passive count uses PoB's displayed budget");
            foreach(var mode in new[]{1,2,0})
            {
                var selected=await engine.ExecuteAsync(new{op="tree-mode",value=mode},default,loaded.Xml);
                check(WorkStateTests.SameBuild(loaded.Xml,selected.Xml),"Choosing allocation mode does not spend or refund points");
                var node=selected.State.GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("canAllocate").GetBoolean()&&!n.GetProperty("ascendancy").GetBoolean()&&n.GetProperty("kind").GetString()=="Normal").OrderBy(n=>n.GetProperty("pathCount").GetInt32()).First();
                var allocated=await engine.ExecuteAsync(new{op="node",id=node.GetProperty("id").GetInt32(),mode="allocate",allocationMode=mode},default,loaded.Xml,compare:true);
                var changed=allocated.State.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("id").GetInt32()==node.GetProperty("id").GetInt32());
                check(changed.GetProperty("allocated").GetBoolean()&&changed.GetProperty("allocationMode").GetInt32()==mode,"New path uses requested allocation mode "+mode);
                if(mode>0)check(allocated.State.GetProperty("passiveBudgets")[mode].GetProperty("used").GetInt32()>0,"Weapon-set budget includes the allocated path");
                check(TreeMapMessages.Create(allocated.State,loaded.State,false,0).Last().GetProperty("weaponTreeModes").GetBoolean(),"Map advertises weapon-set labels only for a supporting engine");
                var restored=await engine.ExecuteAsync(new{op="load",xml=allocated.Xml,name="Restored"},default);
                check(restored.State.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("id").GetInt32()==node.GetProperty("id").GetInt32()).GetProperty("allocationMode").GetInt32()==mode,"Weapon-set node membership survives XML reload");
                await engine.ExecuteAsync(new{op="load",xml=loaded.Xml,name="Baseline"},default);
            }
        }
        else
        {
            var node=loaded.State.GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("canTattoo").GetBoolean()&&n.GetProperty("kind").GetString()=="Normal").OrderByDescending(n=>n.GetProperty("allocated").GetBoolean()).First();
            var id=node.GetProperty("id").GetInt32();
            var catalog=await engine.ExecuteAsync(new{op="tattoo-choices",id},default,loaded.Xml);
            var choices=catalog.State.GetProperty("choices");check(choices.GetArrayLength()>0,"Upstream tattoo dialog supplies eligible modifier descriptions");
            check(WorkStateTests.SameBuild(loaded.Xml,catalog.Xml),"Reading tattoo options preserves the tree");
            var choice=choices[0].GetProperty("id").GetInt32();
            var applied=await engine.ExecuteAsync(new{op="tattoo-edit",id,choice},default,loaded.Xml,compare:true);
            check(applied.State.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("id").GetInt32()==id).GetProperty("isTattoo").GetBoolean(),"Tattoo preview uses the upstream node replacement");
            var reloaded=await engine.ExecuteAsync(new{op="load",xml=applied.Xml,name="Tattoo reload"},default);
            check(reloaded.State.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("id").GetInt32()==id).GetProperty("isTattoo").GetBoolean(),"Tattoo survives XML reload");
            var removed=await engine.ExecuteAsync(new{op="tattoo-edit",id,remove=true},default,applied.Xml,compare:true);
            check(!removed.State.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("id").GetInt32()==id).GetProperty("isTattoo").GetBoolean(),"Upstream reset restores the original passive");
            var finder=await engine.ExecuteAsync(new{op="timeless-catalog"},default,loaded.Xml);

            check(finder.State.GetProperty("settings").GetArrayLength()>=3,"Timeless finder exposes upstream jewel, conqueror and socket choices");
            check(WorkStateTests.SameBuild(loaded.Xml,finder.Xml),"Opening the jewel finder preserves the build");
            var setup=await engine.ExecuteAsync(new{op="timeless-edit",control="jewelSelect",value=2},default,loaded.Xml);
            setup=await engine.ExecuteAsync(new{op="timeless-edit",control="socketSelect",value=2},default,setup.Xml);
            var modifiers=setup.State.GetProperty("timeless").GetProperty("settings").EnumerateArray().Single(s=>s.GetProperty("key").GetString()=="nodeSelect").GetProperty("options");

            var desired=modifiers.EnumerateArray().First(o=>o.GetProperty("description").GetString()!.Length>0);
            setup=await engine.ExecuteAsync(new{op="timeless-edit",control="nodeSelect",value=desired.GetProperty("id").GetInt32()},default,setup.Xml);
            check(setup.State.GetProperty("timeless").GetProperty("searchList").GetString()!.Length>0,"Selecting a desired modifier updates PoB's weighted search list");
            var searched=await engine.ExecuteAsync(new{op="timeless-edit",search=true},default,setup.Xml);
            var results=searched.State.GetProperty("timeless").GetProperty("results");
            check(results.GetArrayLength()>0,"Upstream finder evaluates real jewel seeds for the selected socket");
            var countResults=searched.State.GetProperty("timeless").GetProperty("resultCount").GetInt32();
            var tail=await engine.ExecuteAsync(new{op="timeless-range",version=searched.State.GetProperty("timeless").GetProperty("resultVersion").GetInt32(),start=countResults-1,count=1},default,searched.Xml);
            check(tail.State.GetProperty("results")[0].GetProperty("id").GetInt32()==countResults,"Last seed remains accessible beyond the initial result page");
            if(results.GetArrayLength()>0)
            {
                var seed=results[0];var count=System.Xml.Linq.XDocument.Parse(searched.Xml).Descendants("Item").Count();
                var added=await engine.ExecuteAsync(new{op="timeless-edit",addSeed=seed.GetProperty("seed").GetInt32(),socket=seed.GetProperty("socket").GetInt32()},default,searched.Xml);
                check(System.Xml.Linq.XDocument.Parse(added.Xml).Descendants("Item").Count()==count+1,"Selected seed adds an upstream-generated jewel to inventory");
            }
        }
        await TreeToolsWorkflowTests.RunAsync(game,root,runtime,adapter,loaded.Xml,check);
    }
}
