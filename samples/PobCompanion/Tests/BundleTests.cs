using System.IO.Compression;
using WidgetRail.Samples.PobCompanion;

internal static class BundleTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"widgetrail-pob-bundle-"+Guid.NewGuid().ToString("N"));
        var cache=Path.Combine(root,"cache");Directory.CreateDirectory(cache);
        var previous=Environment.GetEnvironmentVariable("WIDGETRAIL_POB_CACHE");
        Environment.SetEnvironmentVariable("WIDGETRAIL_POB_CACHE",cache);
        try
        {
            var good=Path.Combine(root,"good");
            using(var zip=ZipFile.Open(good+".zip",ZipArchiveMode.Create))
            { using var writer=new StreamWriter(zip.CreateEntry("src/HeadlessWrapper.lua").Open());writer.Write("return {}"); }
            var first=await EngineBundle.ResolveAsync(good,default);
            check(Directory.GetDirectories(cache).Length==1&&File.Exists(Path.Combine(first,".complete")),"Engine cache publishes a complete directory without staging leftovers");
            check(first==await EngineBundle.ResolveAsync(good,default),"Identical engine bundle reuses its immutable cache");
            var bad=Path.Combine(root,"bad");
            using(var zip=ZipFile.Open(bad+".zip",ZipArchiveMode.Create))
            { using var writer=new StreamWriter(zip.CreateEntry("../escape.lua").Open());writer.Write("invalid"); }
            try{await EngineBundle.ResolveAsync(bad,default);check(false,"Engine traversal rejected");}
            catch(IOException){check(true,"Engine traversal rejected");}
            check(Directory.GetDirectories(cache).Length==1&&!File.Exists(Path.Combine(cache,"escape.lua")),"Invalid extraction removes its staging folder");
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            try{await EngineBundle.ResolveAsync(good,cancel.Token);check(false,"Cancelled extraction rejected");}
            catch(OperationCanceledException){check(true,"Cancelled extraction rejected");}
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIDGETRAIL_POB_CACHE",previous);
            // This test created this exact fresh temporary directory.
            if(Path.GetDirectoryName(root)==Path.TrimEndingDirectorySeparator(Path.GetTempPath()))Directory.Delete(root,true);
        }
    }
}
