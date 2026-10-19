using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class ImportExportTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        Uri Download(string url,int selectedGame)=>BuildLinks.DownloadUri(new(url),selectedGame);
        foreach(var url in new[]{"https://poe.ninja/poe2/profile/account/standard/character/character", "https://poe.ninja/poe1/profile/account/standard/character/character", "https://poe.ninja/poe1/pob/abc123", "https://poe.ninja/poe2/pob/abc123", "https://poe.ninja/pob/abc123"})
        {
            var rejected=false;
            try{await BuildDocument.ReadAsync(url,game,default);}
            catch(IOException error){rejected=error.Message.Contains("copy the build's PoB code")&&error.Message.Contains("Paste build from clipboard");}
            check(rejected,"Internal poe.ninja URL is rejected with clipboard instructions: "+url);
        }
        check(BuildDocument.Decode(BuildDocument.Encode(xml),game)==xml,"Copied PoB codes remain importable without a website API");
        check(BuildDocument.Decode("\uFEFF"+xml,game)==xml,"A UTF-8 byte-order mark does not turn XML into a share code");
        check(BuildDocument.Decode(BuildDocument.Encode("\uFEFF"+xml),game)==xml,"A shared XML payload can contain a UTF-8 byte-order mark");
        var bomFile=Path.Combine(Path.GetTempPath(),"pob-bom-"+Guid.NewGuid().ToString("N")+".xml");
        try
        {
            await File.WriteAllTextAsync(bomFile,xml,new System.Text.UTF8Encoding(true));
            check(await BuildDocument.ReadAsync(bomFile,game,default)==xml,"Local UTF-8 XML files with a BOM import through the bounded reader");
            var rejected=false;
            try{BuildDocument.Decode("\uFEFF<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///private'>]><x>&x;</x>",game);}catch(IOException){rejected=true;}
            check(rejected,"BOM handling preserves external-entity rejection");
        }
        finally {File.Delete(bomFile);}
        foreach(var url in new[]{"http://poe.ninja/poe2/pob/abc","https://poe.ninja.evil.test/poe2/pob/abc","https://user@poe.ninja/poe2/pob/abc","https://poe.ninja:444/poe2/pob/abc","https://poe.ninja/poe1/pob/abc","https://poe.ninja/poe2/profile/a%2Fb/standard/character/test","https://poe.ninja/poe2/builds","https://poe.ninja/poe2/pob/abc?url=https://example.com"})
        {var rejected=false;try{Download(url,2);}catch(IOException){rejected=true;}check(rejected,"Reject unsupported import URL: "+url);}
        check(Download("https://pobb.in/abc_DEF-123",game).AbsoluteUri=="https://pobb.in/pob/abc_DEF-123","Existing pobb.in downloads are preserved");
        if(game!=2)return;
        await using var engine=new PobEngine(root,runtime,adapter);
        var before=await engine.ExecuteAsync(new{op="load",xml,name="Export fixture"},default);
        var result=await engine.ExecuteAsync(new{op="game-export",all=false,name="A build",author="Tester",description="Planned with PoB"},default,before.Xml);
        check(WorkStateTests.SameBuild(before.Xml,result.Xml),"Export does not change the build");
        using var document=JsonDocument.Parse(result.State.GetProperty("files")[0].GetProperty("text").GetString()!);
        check(document.RootElement.GetProperty("name").GetString()=="A build"&&document.RootElement.TryGetProperty("passives",out _)&&document.RootElement.TryGetProperty("skills",out _)&&document.RootElement.TryGetProperty("inventory_slots",out _),"Upstream game export contains the selected tree, skills and equipment");
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-game-export-"+Guid.NewGuid().ToString("N"));
        try
        {
            var first=PobGameExport.Save(directory,result.State.GetProperty("files"),default);
            var original=File.ReadAllText(first[0]);
            var second=PobGameExport.Save(directory,result.State.GetProperty("files"),default);
            check(first[0]!=second[0]&&File.ReadAllText(first[0])==original,"Repeated exports preserve existing guides");
            var malformed=JsonSerializer.SerializeToElement(new[]{new{name="New",text=original},new{name="Broken",text="not json"}});
            var failed=false;try{PobGameExport.Save(directory,malformed,default);}catch(JsonException){failed=true;}
            check(failed&&Directory.GetFiles(directory).Length==2,"Failed batch removes only newly created files");
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}

        var staged=await engine.ExecuteAsync(new{op="loadout-create",name="Export Midgame"},default,before.Xml);
        var group=staged.State.GetProperty("skills").EnumerateArray().First(g=>!g.GetProperty("generated").GetBoolean()&&g.GetProperty("gems").GetArrayLength()>0);
        var groupId=group.GetProperty("id").GetInt32();
        var gemId=group.GetProperty("gems")[0].GetProperty("id").GetInt32();
        staged=await engine.ExecuteAsync(new{op="gem",group=groupId,id=gemId,field="level",value=1},default,staged.Xml);
        var midgame=await engine.ExecuteAsync(new{op="game-export",all=false,name="Progression - Export Midgame",author="Tester",description="Two stages"},default,staged.Xml);
        staged=await engine.ExecuteAsync(new{op="loadout-create",name="Export Endgame"},default,staged.Xml);
        staged=await engine.ExecuteAsync(new{op="gem",group=groupId,id=gemId,field="level",value=3},default,staged.Xml);
        var endgame=await engine.ExecuteAsync(new{op="game-export",all=false,name="Progression - Export Endgame",author="Tester",description="Two stages"},default,staged.Xml);
        var batch=await engine.ExecuteAsync(new{op="game-export",all=true,name="Progression",author="Tester",description="Two stages"},default,staged.Xml);
        check(WorkStateTests.SameBuild(staged.Xml,batch.Xml),"Exporting all loadouts preserves active sets and authored state");
        var files=batch.State.GetProperty("files").EnumerateArray().ToArray();
        check(files.Length==staged.State.GetProperty("loadouts").GetArrayLength(),"Every linked loadout produces one game file");
        string Payload(JsonElement file)
        {
            using var parsed=JsonDocument.Parse(file.GetProperty("text").GetString()!);
            return Canonical(parsed.RootElement);
        }
        foreach(var expected in new[]{midgame,endgame})
        {
            var selected=expected.State.GetProperty("files")[0];
            var name=selected.GetProperty("name").GetString();
            check(Payload(files.Single(f=>f.GetProperty("name").GetString()==name))==Payload(selected),"Batch export matches selected-set export for "+name);
        }
        using var mid=JsonDocument.Parse(midgame.State.GetProperty("files")[0].GetProperty("text").GetString()!);
        using var end=JsonDocument.Parse(endgame.State.GetProperty("files")[0].GetProperty("text").GetString()!);
        check(Canonical(mid.RootElement.GetProperty("skills"))!=Canonical(end.RootElement.GetProperty("skills")),"Distinct gem levels remain distinct in exported loadouts");

        static string Canonical(JsonElement value,string key="")=>value.ValueKind switch
        {
            JsonValueKind.Object=>"{"+string.Join(",",value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>JsonSerializer.Serialize(p.Name)+":"+Canonical(p.Value,p.Name)))+"}",
            JsonValueKind.Array=>"["+string.Join(",",key=="passives"?value.EnumerateArray().Select(v=>Canonical(v)).OrderBy(v=>v,StringComparer.Ordinal):value.EnumerateArray().Select(v=>Canonical(v)))+"]",
            _=>value.GetRawText()
        };
    }
}
