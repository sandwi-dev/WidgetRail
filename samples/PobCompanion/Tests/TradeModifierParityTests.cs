using System.Diagnostics;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class TradeModifierParityTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var baseline=await engine.ExecuteAsync(new{op="load",xml,name="Modifier parity"},default);
        var catalog=(await engine.ExecuteAsync(new{op="trade-catalog",slot="Ring 1"},default,baseline.Xml)).State;
        var mana=catalog.GetProperty("stats").EnumerateArray().First(s=>s.GetProperty("title").GetString()=="Mana").GetProperty("id").GetInt32();
        var cases=new List<object>();var actual=new List<JsonElement>();
        foreach(var displayOnly in new[]{true,false})
        {
            var line=displayOnly?"+1 Suffix Modifier allowed":"8% chance to gain an additional random Charge when you gain a Charge";
            var rune=game==2&&!displayOnly;var slot=rune?"Weapon 1":"Ring 1";
            var entry=new {id=new string('d',64),listing=new{},item=new{rarity="Rare",name="Parity item",typeLine=rune?"Bolting Quarterstaff":"Gold Ring",ilvl=80,
                explicitMods=(rune?new[]{"+25 to maximum Mana"}:new[]{"+25 to maximum Mana",line}).Select(description=>new{description}).ToArray(),
                sockets=rune?new[]{new{group=0}}:Array.Empty<object>(),
                socketedItems=rune?new[]{new{baseType="Saqawal's Rune of the Sky",socket=0}}:Array.Empty<object>(),
                runeMods=rune?new[]{"Gain 5% of Damage as Extra Damage of all Elements","Bonded: "+line}:Array.Empty<string>()}};
            var converted=(await engine.ExecuteAsync(new{op="trade-item",entry},default,baseline.Xml)).State;
            var raw=converted.GetProperty("raw").GetString()!;
            check(raw.Contains(line)&&converted.GetProperty("warnings").GetArrayLength()==(displayOnly?0:1),displayOnly?"PoB-recognized display-only modifier is preserved without warning":"Unmodeled modifier is preserved and reported rather than rejecting the item");
            await engine.ExecuteAsync(new{op="trade-rank-begin",slot,priority="custom",weights=new[]{new{id=mana,weight=1.0}}},default,baseline.Xml);
            var batch=(await engine.ExecuteAsync(new{op="trade-rank-items",items=new object[]{new{entry},new{raw}}},default,baseline.Xml)).State.GetProperty("results");
            check(batch.EnumerateArray().All(r=>!r.TryGetProperty("error",out _)&&r.GetProperty("warnings").GetArrayLength()==(displayOnly?0:1)),"JSON and saved-raw batch paths share modifier warning semantics");
            check(batch[0].GetProperty("score").GetDouble()==batch[1].GetProperty("score").GetDouble(),"Restored raw candidate scores match fresh trade JSON");
            actual.Add(batch[0].Clone());cases.Add(new{raw,slot});
            await engine.ExecuteAsync(new{op="trade-rank-finish"},default,baseline.Xml);
            var proposed=await engine.ExecuteAsync(new{op="item",raw,slot},default,baseline.Xml,compare:true);
            check(proposed.State.GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("slot").GetString()==slot).GetProperty("warnings").GetArrayLength()==(displayOnly?0:1),"Comparison snapshot retains upstream modifier warning status");
        }
        var dir=Path.Combine(Path.GetTempPath(),"pob-modifier-parity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir,"build.xml"),baseline.Xml);File.WriteAllText(Path.Combine(dir,"cases.json"),JsonSerializer.Serialize(cases));
            var start=new ProcessStartInfo(Path.Combine(runtime,"luajit.exe")){WorkingDirectory=Path.Combine(root,"src"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.Environment["LUA_PATH"]="../runtime/lua/?.lua;../runtime/lua/?/init.lua;;";start.Environment["LUA_CPATH"]=runtime.Replace('\\','/')+"/?.dll;;";start.Environment.Remove("CI");
            start.ArgumentList.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(adapter)!,"../Tests/TradeModifierOracle.lua")));
            start.ArgumentList.Add(Path.Combine(dir,"build.xml"));start.ArgumentList.Add(Path.Combine(dir,"cases.json"));
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(entireProcessTree:true);throw;}
            if(process.ExitCode!=0)throw new IOException(await errors);
            using var expected=JsonDocument.Parse(await output);
            for(var i=0;i<actual.Count;i++)check(Math.Abs(actual[i].GetProperty("score").GetDouble()-expected.RootElement[i].GetProperty("score").GetDouble())<1e-9&&
                Math.Abs(actual[i].GetProperty("changes").EnumerateArray().Single(c=>c.GetProperty("stat").GetString()=="Mana").GetProperty("after").GetDouble()-expected.RootElement[i].GetProperty("mana").GetDouble())<1e-9,
                "Scores and calculated mana match standalone upstream desktop evaluator: "+(i==0?"display-only":"unmodeled"));
        }
        finally{Directory.Delete(dir,true);}
    }
}
