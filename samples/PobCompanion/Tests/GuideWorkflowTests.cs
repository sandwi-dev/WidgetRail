using System.Diagnostics;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class GuideWorkflowTests
{
    internal static async Task RunAsync(string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var loaded=await engine.ExecuteAsync(new{op="load",xml,name="UI-created guide build"},default);
        foreach(var name in new[]{"Midgame","Late game"})
        {
            var result=await engine.ExecuteAsync(new{op="loadout",name},default,loaded.Xml);
            check(result.State.GetProperty("mainSkill").GetString()=="Whirling Assault",name+" retains main skill across different group positions");
            check(result.State.GetProperty("mainSkillShowsAverage").GetBoolean(),name+" correctly identifies average damage");
            var oracle=await Oracle(result.Xml);
            foreach(var stat in oracle.GetProperty("stats").EnumerateObject())
            {
                var actual=result.State.GetProperty("stats").EnumerateArray().Single(s=>s.GetProperty("name").GetString()==stat.Name).GetProperty("value").GetDouble();
                check(Math.Abs(actual-stat.Value.GetDouble())<=Math.Max(0.00001,Math.Abs(actual)*1e-9),name+" "+stat.Name+" matches independent upstream calculation");
            }
            check(result.State.GetProperty("level").GetInt32()==oracle.GetProperty("level").GetInt32(),name+" level matches upstream");
            check(!result.State.GetProperty("conditions").EnumerateArray().Any(c=>c.GetProperty("title").GetString()=="Are you in Demon Form?"),name+" excludes inapplicable default configuration");
            var weapon=result.State.GetProperty("items").EnumerateArray().First(i=>i.GetProperty("slot").GetString()=="Weapon 1");
            var slots=await engine.ExecuteAsync(new{op="item-slots",raw=weapon.GetProperty("raw").GetString()},default,result.Xml);
            check(slots.State.GetProperty("slots").GetArrayLength() is >0 and <5 && slots.State.GetProperty("slots").EnumerateArray().All(s=>s.GetProperty("name").GetString()!.StartsWith("Weapon",StringComparison.Ordinal)),name+" weapon picker only offers compatible equipment slots");
            var node=result.State.GetProperty("nodes").EnumerateArray().First(n=>n.GetProperty("name").GetString()=="Tenfold Attacks"&&n.GetProperty("canRefund").GetBoolean());
            var refund=await engine.ExecuteAsync(new{op="node",id=node.GetProperty("id").GetInt32(),mode="refund"},default,result.Xml);
            var refundOracle=await Oracle(refund.Xml);
            check(refund.State.GetProperty("level").GetInt32()==refundOracle.GetProperty("level").GetInt32(),name+" refunded level follows upstream progress estimation");
            loaded=result;
        }

        async Task<JsonElement> Oracle(string input)
        {
            var file=Path.Combine(Path.GetTempPath(),"pob-guide-oracle-"+Guid.NewGuid().ToString("N")+".xml");
            await File.WriteAllTextAsync(file,input);
            try
            {
                var start=new ProcessStartInfo(Path.Combine(runtime,"luajit.exe")){WorkingDirectory=Path.Combine(await EngineBundle.ResolveAsync(root,default),"src"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                start.Environment["LUA_PATH"]="../runtime/lua/?.lua;../runtime/lua/?/init.lua;;";
                start.Environment["LUA_CPATH"]=runtime.Replace('\\','/')+"/?.dll;;";start.Environment.Remove("CI");
                start.ArgumentList.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(adapter)!,"../Tests/CalculationOracle.lua")));start.ArgumentList.Add(file);
                using var process=Process.Start(start)!;
                var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
                if(process.ExitCode!=0)throw new IOException(await stderr);
                return JsonSerializer.Deserialize<JsonElement>(await stdout);
            }
            finally{File.Delete(file);}
        }
    }
}
