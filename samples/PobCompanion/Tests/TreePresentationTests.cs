using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;

internal static class TreePresentationTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        // Current tree includes constraints/proxies/masteries absent in old imports.
        var result=await engine.ExecuteAsync(new{op="new"},default);
        var nodes=result.State.GetProperty("nodes").EnumerateArray().ToArray();
        var decorative=nodes.Where(n=>n.GetProperty("decoration").GetBoolean()).ToArray();
        check((game==1||decorative.Length>0) && decorative.All(n=>!n.GetProperty("interactive").GetBoolean()&&!n.GetProperty("canAllocate").GetBoolean()&&!n.GetProperty("canRefund").GetBoolean()),"Decorative cluster artwork is never a passive action");
        var messages=TreeMapMessages.Create(result.State,null,false,0);
        var projected=messages.Where(m=>m.GetProperty("type").GetString()=="nodes").SelectMany(m=>m.GetProperty("items").EnumerateArray()).ToArray();
        var ids=projected.Select(n=>n.GetProperty("id").GetInt32()).ToHashSet();
        check(ids.SetEquals(nodes.Where(n=>n.GetProperty("visible").GetBoolean()).Select(n=>n.GetProperty("id").GetInt32())),"Only upstream-visible nodes reach the rendered map");
        check(messages.Where(m=>m.GetProperty("type").GetString()=="links").SelectMany(m=>m.GetProperty("items").EnumerateArray()).All(l=>ids.Contains(l.GetProperty("a").GetInt32())&&ids.Contains(l.GetProperty("b").GetInt32())),"Hidden endpoints never leave orphan connectors");
        var backgrounds=messages.Where(m=>m.GetProperty("type").GetString()=="artwork").SelectMany(m=>m.GetProperty("items").EnumerateArray()).ToArray();
        check(backgrounds.Length>0 && backgrounds.Length==result.State.GetProperty("treeArtwork").GetArrayLength(),"Upstream artwork placement is batched with the map transaction");
        check(projected.All(n=>n.GetProperty("art").ValueKind==JsonValueKind.Object),"Node artwork state reaches the renderer");
        check(messages.All(m=>JsonSerializer.SerializeToUtf8Bytes(m).Length<=WebSessionContract.MaximumMessageBytes),"Full tree respects shared message limits");
        foreach(var node in decorative.Take(1).Concat(nodes.Where(n=>!n.GetProperty("interactive").GetBoolean()).Take(1)))
        {
            try{await engine.ExecuteAsync(new{op="node",id=node.GetProperty("id").GetInt32(),mode="allocate"},default);check(false,"Noninteractive mutation rejected");}
            catch(PobCommandException){check(true,"Noninteractive mutation rejected");}
        }
        var modified=JsonNode.Parse(result.State.GetRawText())!;
        modified["nodes"]![0]!["visible"]=!modified["nodes"]![0]!["visible"]!.GetValue<bool>();
        check(TreeMapMessages.GeometryKey(result.State)!=TreeMapMessages.GeometryKey(JsonSerializer.SerializeToElement(modified)),"Unlock visibility changes invalidate cached geometry");
        var artRoot=Environment.GetEnvironmentVariable("POB_TREE_ART");
        if(artRoot is not null)
        {
            using var art=JsonDocument.Parse(File.ReadAllText(Path.Combine(artRoot,result.State.GetProperty("treeVersion").GetString()!,"index.json")));
            var missing=projected.Where(n=>n.GetProperty("decoration").GetBoolean()||n.GetProperty("interactive").GetBoolean()&&n.GetProperty("kind").GetString()!="Socket")
                .Where(n=>{var atlas=n.GetProperty("decoration").GetBoolean()?art.RootElement.GetProperty("decorations"):art.RootElement;return !atlas.GetProperty("icons").TryGetProperty(n.GetProperty("icon").GetString()!,out _);}).ToArray();
            check(missing.Length==0,"Every rendered passive and decoration has atlas artwork (missing: "+string.Join(", ",missing.Take(8).Select(n=>n.GetProperty("name").GetString()))+")");
        }
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-tree-audit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var file=Path.Combine(directory,"build.xml");File.WriteAllText(file,result.Xml);
            var start=new ProcessStartInfo(Path.Combine(Path.GetFullPath(runtime),"luajit.exe")){WorkingDirectory=Path.Combine(await EngineBundle.ResolveAsync(root,default),"src"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.Environment["LUA_PATH"]="../runtime/lua/?.lua;../runtime/lua/?/init.lua;;";
            start.Environment["LUA_CPATH"]=Path.GetFullPath(runtime).Replace('\\','/')+"/?.dll;;";start.Environment.Remove("CI");
            start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(adapter)!,"../Tests/TreePresentationOracle.lua"));
            start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(adapter)!,"tree-presentation.lua"));start.ArgumentList.Add(file);
            var imported=Path.Combine(directory,"imported.xml");File.WriteAllText(imported,xml);start.ArgumentList.Add(imported);
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
            check(process.ExitCode==0,"Pinned desktop metadata validates every node and state transition: "+(process.ExitCode==0?await output:await errors));
        }
        finally{Directory.Delete(directory,true);}
    }
}
