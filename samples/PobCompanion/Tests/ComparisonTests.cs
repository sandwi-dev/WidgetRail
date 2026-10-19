using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;

internal static class ComparisonTests
{
    internal static async Task RunAsync(string engineRoot,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(engineRoot,runtime,adapter);
        var baseline=await engine.ExecuteAsync(new{op="load",xml,name="Comparison baseline"},default);
        var nodes=baseline.State.GetProperty("nodes").EnumerateArray().ToArray();
        var allocate=nodes.First(n=>n.GetProperty("canAllocate").GetBoolean()&&n.GetProperty("pathCount").GetInt32()>=2);
        var refund=nodes.Where(n=>n.GetProperty("canRefund").GetBoolean()).MaxBy(n=>n.GetProperty("refundCount").GetInt32());
        foreach(var (node,mode) in new[]{(allocate,"allocate"),(refund,"refund")})
        {
            var next=await engine.ExecuteAsync(new{op="node",id=node.GetProperty("id").GetInt32(),mode},default,baseline.Xml,compare:true);
            var actual=Lines(next);
            var expected=await Oracle(baseline.Xml,next.Xml);
            check(actual.SequenceEqual(expected),mode+" summary matches desktop PoB's complete comparison output");
            check(actual.Length>0,mode+" fixture exercises changed comparison stats");
            var restored=await engine.ExecuteAsync(new{op="load",xml=next.Xml,name="Restored preview"},default,baseline.Xml,compare:true);
            check(Lines(restored).SequenceEqual(actual),mode+" summary is regenerated identically when a preview is restored");
        }
        var unchanged=await engine.ExecuteAsync(new{op="snapshot"},default,baseline.Xml,compare:true);
        check(Lines(unchanged).Length==0,"An unchanged build produces no invented comparison stats");
        var equipped=baseline.State.GetProperty("items").EnumerateArray().First(i=>i.GetProperty("slot").GetString()=="Helmet");
        var originalRaw=equipped.GetProperty("raw").GetString()!;
        var strengthened=await engine.ExecuteAsync(new{op="item-draft",mode="custom-add",raw=originalRaw,text="+37 to maximum Life"},default,baseline.Xml);
        var raw=strengthened.State.GetProperty("authoring").GetProperty("raw").GetString()!;
        var upgrade=await engine.ExecuteAsync(new{op="item",slot="Helmet",raw},default,baseline.Xml,compare:true);
        double Life(PobResult result)=>result.State.GetProperty("stats").EnumerateArray().Single(s=>s.GetProperty("name").GetString()=="Life").GetProperty("value").GetDouble();
        check(Life(upgrade)>Life(baseline)&&Lines(upgrade).Length>0,"Item comparison exercises an actual increase in calculated life");
        check(Lines(upgrade).SequenceEqual(await Oracle(baseline.Xml,upgrade.Xml)),"Item upgrade summary matches every desktop PoB comparison line");
        var changes=PobItemModifierComparison.Read(upgrade.State,"Helmet");
        check(changes.Available&&changes.Rows.Any(row=>row.Status is "added" or "changed"&&row.After.Any(line=>line.Formatted.GetProperty("text").GetString()!.Contains("37"))),"Modifier comparison retains added life and exposes upstream formatted text");
        var restoredItem=await engine.ExecuteAsync(new{op="load",xml=upgrade.Xml,name="Restored item comparison"},default,baseline.Xml,compare:true);
        check(JsonSerializer.Serialize(PobItemModifierComparison.Read(restoredItem.State,"Helmet"))==JsonSerializer.Serialize(changes),"Restored item preview has identical modifier pairings and formatting");
        var sameItem=await engine.ExecuteAsync(new{op="item",slot="Helmet",raw=originalRaw},default,baseline.Xml,compare:true);
        var same=PobItemModifierComparison.Read(sameItem.State,"Helmet");
        check(same.Available&&same.Rows.Length>0&&same.Rows.All(row=>row.Status=="same"),"Comparing the equipped item retains unchanged modifiers without invented differences");
        var duplicateRaw="Rarity: RARE\nComparison fixture\nGold Ring\nImplicits: 1\n15% increased Rarity of Items found\n+10 to maximum Life\n+20 to maximum Life\nAdds 1 to 4 Physical Damage to Attacks";
        var firstRing=await engine.ExecuteAsync(new{op="item",slot="Ring 1",raw=duplicateRaw},default,baseline.Xml);
        var changedRing=await engine.ExecuteAsync(new{op="item",slot="Ring 1",raw=duplicateRaw.Replace("+20","+25").Replace("1 to 4","2 to 8")+"\n+10 to maximum Mana"},default,firstRing.Xml,compare:true);
        var ringChanges=PobItemModifierComparison.Read(changedRing.State,"Ring 1");
        check(ringChanges.Available&&ringChanges.Rows.Any(row=>row.Status=="changed"&&row.Before.Length==2&&row.After.Length==2),"PoB template grouping preserves duplicate modifier occurrences instead of overwriting them");
        check(ringChanges.Rows.Any(row=>row.Status=="changed"&&row.Before.Any(line=>line.Formatted.GetProperty("text").GetString()!.Contains("1 to 4")))&&ringChanges.Rows.Any(row=>row.Status=="added"&&row.Before.Length==0),"Range changes pair through PoB and added modifiers retain an empty equipped side");
        var reverseRing=await engine.ExecuteAsync(new{op="item",slot="Ring 1",raw=duplicateRaw},default,changedRing.Xml,compare:true);
        check(PobItemModifierComparison.Read(reverseRing.State,"Ring 1").Rows.Any(row=>row.Status=="removed"&&row.After.Length==0),"Removed modifiers retain an empty proposed side");
        check(!PobItemModifierComparison.Read(default,"Helmet").Available&&!PobItemModifierComparison.Read(JsonSerializer.SerializeToElement(new{itemComparisons="bad"}),"Helmet").Available,"Missing or malformed comparison data falls back without a rendering failure");
        var downgrade=await engine.ExecuteAsync(new{op="item",slot="Helmet",raw=originalRaw},default,upgrade.Xml,compare:true);
        check(Life(downgrade)<Life(upgrade)&&Lines(downgrade).SequenceEqual(await Oracle(upgrade.Xml,downgrade.Xml)),
            "Weaker item replacement preserves desktop comparison losses and percentage formatting");
        var poe2=xml.Contains("<PathOfBuilding2",StringComparison.Ordinal);
        var gem="RaiseZombie";
        var skill=poe2?"RaiseZombiePlayer":"RaiseZombie";
        string MinionXml(int level)
        {
            var document=XDocument.Parse(baseline.Xml);document.Root!.Element("Build")!.SetAttributeValue("mainSocketGroup",1);
            document.Root.Element("Skills")!.ReplaceWith(XElement.Parse($"<Skills><Skill enabled=\"true\" mainActiveSkill=\"1\"><Gem gemId=\"Metadata/Items/Gems/SkillGem{gem}\" skillId=\"{skill}\" level=\"{level}\" quality=\"0\" enabled=\"true\"/></Skill></Skills>"));
            return document.ToString();
        }
        var minionBefore=await engine.ExecuteAsync(new{op="load",xml=MinionXml(20),name="Minion comparison"},default);
        var minionAfter=await engine.ExecuteAsync(new{op="load",xml=MinionXml(19),name="Minion comparison"},default,compare:true);
        check(minionAfter.State.GetProperty("comparison").EnumerateArray().Any(group=>group.GetProperty("title").GetString()=="Minion"),
            "Minion calculation changes are reported in their own group");
        check(Lines(minionAfter).SequenceEqual(await Oracle(minionBefore.Xml,minionAfter.Xml)),"Minion comparison matches the desktop tooltip output");

        static string[] Lines(PobResult result)=>result.State.GetProperty("comparison").EnumerateArray()
            .SelectMany(group=>group.GetProperty("lines").EnumerateArray()).Select(line=>line.GetProperty("text").GetString()!).ToArray();
        async Task<string[]> Oracle(string before,string after)
        {
            var directory=Path.Combine(Path.GetTempPath(),"widgetrail-comparison-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory,"before.xml"),before);File.WriteAllText(Path.Combine(directory,"after.xml"),after);
                var start=new ProcessStartInfo(Path.Combine(runtime,"luajit.exe")) { WorkingDirectory=Path.Combine(await EngineBundle.ResolveAsync(engineRoot,default),"src"),
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
                start.Environment["LUA_PATH"]="../runtime/lua/?.lua;../runtime/lua/?/init.lua;;";
                start.Environment["LUA_CPATH"]=runtime.Replace('\\','/')+"/?.dll;;";start.Environment.Remove("CI");
                start.ArgumentList.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(adapter)!,"../Tests/ComparisonOracle.lua")));
                start.ArgumentList.Add(Path.Combine(directory,"before.xml"));start.ArgumentList.Add(Path.Combine(directory,"after.xml"));
                using var process=Process.Start(start)!;
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(entireProcessTree:true);throw;}
                if(process.ExitCode!=0)throw new Exception("Desktop comparison oracle failed: "+await errors);
                return JsonSerializer.Deserialize<string[]>(await output)!;
            }
            finally{Directory.Delete(directory,true);}
        }
    }
}
