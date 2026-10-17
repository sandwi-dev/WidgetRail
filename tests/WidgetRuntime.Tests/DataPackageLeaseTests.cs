using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;

internal static class DataPackageLeaseTests
{
    internal static Task Run()
    {
        if(!OperatingSystem.IsWindows())return Task.CompletedTask;
        var root=Path.Combine(Path.GetTempPath(),"widgetrail-data-lease-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            for(var i=0;i<3;i++)using(var file=File.Create(Path.Combine(root,"part"+i)))file.SetLength(24L*1024*1024);
            using(var lease=PinnedDirectoryContentLease.Acquire(root))
                if(lease.Targets.Count!=4)throw new InvalidOperationException("The lease did not pin every data part.");
            using(var file=File.OpenWrite(Path.Combine(root,"part0")))file.SetLength(WidgetPackageLimits.MaximumExpandedBytes);
            var rejected=false;try{using var lease=PinnedDirectoryContentLease.Acquire(root);}catch(WidgetProcessAdmissionException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("The aggregate package limit was not enforced.");
        }
        finally{Directory.Delete(root,true);}
        return Task.CompletedTask;
    }
}
