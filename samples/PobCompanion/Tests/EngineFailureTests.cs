using System.Diagnostics;
using System.Reflection;
using WidgetRail.Samples.PobCompanion;

internal static class EngineFailureTests
{
    internal static async Task RunAsync(string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var baseline=await engine.ExecuteAsync(new{op="load",xml,name="Crash recovery fixture"},default);
        var changed=await engine.ExecuteAsync(new{op="level",value=83},default,baseline.Xml);
        var expected=changed.Xml;
        async Task Crash()
        {
            using var process=Process.GetProcessById(engine.ProcessId!.Value);
            process.Kill(entireProcessTree:true);await process.WaitForExitAsync();
        }
        var oldProcess=engine.ProcessId;await Crash();
        var restored=await engine.ExecuteAsync(new{op="snapshot"},default,expected);
        check(engine.ProcessId!=oldProcess&&WorkStateTests.SameBuild(expected,restored.Xml),"A killed Lua process restarts and restores the caller's complete working build");

        // Fault injection models cached state that a newly started engine cannot
        // load. The caller's durable recovery snapshot must take precedence.
        var cached=typeof(PobEngine).GetField("lastXml",BindingFlags.Instance|BindingFlags.NonPublic)!;
        cached.SetValue(engine,"not a build");await Crash();
        var explicitRecovery=await engine.ExecuteAsync(new{op="snapshot"},default,baseline.Xml);
        check(WorkStateTests.SameBuild(baseline.Xml,explicitRecovery.Xml),"Explicit saved-build recovery bypasses an unrestorable cached engine session");
        cached.SetValue(engine,"not a build");await Crash();
        var failed=false;try{await engine.ExecuteAsync(new{op="snapshot"},default);}catch(IOException error){failed=error.Message.Contains("could not be restored",StringComparison.Ordinal);}
        check(failed&&engine.ProcessId is null,"A rejected restart restore gives actionable feedback and retires its process");
        var fallback=await engine.ExecuteAsync(new{op="load",xml=baseline.Xml,name="Saved fallback"},default);
        check(WorkStateTests.SameBuild(baseline.Xml,fallback.Xml),"A saved build opens after failed restore without repeating the bad cached state");

        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-engine-fault-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var hanging=Path.Combine(directory,"hang.lua");
            await File.WriteAllTextAsync(hanging,"io.stdout:setvbuf('no')\nio.write('{\"ready\":true}\\n')\nio.read('*l')\nio.read('*l')\n");
            await using var stalled=new PobEngine(root,runtime,hanging){CommandTimeout=TimeSpan.FromSeconds(1)};
            var timedOut=false;
            try{await stalled.ExecuteAsync(new{op="snapshot"},default);}catch(IOException error){timedOut=error.Message.Contains("did not respond in time",StringComparison.Ordinal);}
            check(timedOut&&stalled.ProcessId is null,"Engine timeout is reported as failure rather than user cancellation and releases the child process");
            using var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var cancelled=false;
            try{await stalled.ExecuteAsync(new{op="snapshot"},cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            check(cancelled&&stalled.ProcessId is null,"Explicit cancellation stays distinct from timeout and releases the child process");
        }
        finally {Directory.Delete(directory,true);}
    }
}
