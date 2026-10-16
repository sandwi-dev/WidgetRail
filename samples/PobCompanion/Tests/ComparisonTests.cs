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
