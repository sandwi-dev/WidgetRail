using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using WidgetRail.WidgetPresentationSession;

namespace WidgetPresentationSession.Tests;

[TestClass]
public sealed class JobBoundProcessTests
{
    [TestMethod]
    public void EnvironmentPreservesPrivateRuntimeOverridesWithoutChangingParent()
    {
        var before = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var start = new ProcessStartInfo();
        start.Environment.Clear();
        start.Environment["DOTNET_ROOT"] = @"C:\private runtime";
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        Assert.AreEqual("DOTNET_MULTILEVEL_LOOKUP=0\0DOTNET_ROOT=C:\\private runtime\0\0", JobBoundProcess.EnvironmentBlock(start));
        Assert.AreEqual(before, Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        Assert.IsNull(new BridgeProcessOptions(@"C:\root", @"C:\settings", @"C:\catalog").ProcessOwnerJobName);
    }

    [TestMethod]
    public async Task CreationOwnsChildBeforeResumeAndPreservesBothDiagnosticStreams()
    {
        var name = "Local\\WidgetRail.JobBoundTest." + Guid.NewGuid().ToString("N");
        using var job = CreateJobObjectW(IntPtr.Zero, name);
        Assert.IsFalse(job.IsInvalid);
        // Exact native size/offset for the x64 test target's extended limits.
        var limits = Marshal.AllocHGlobal(144);
        try
        {
            for (var index = 0; index < 144; index++) Marshal.WriteByte(limits, index, 0);
            Marshal.WriteInt32(limits, 16, 0x2000 /* KILL_ON_JOB_CLOSE */);
            if (!SetInformationJobObject(job, 9, limits, 144)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(limits); }
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetTempPath() };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[Console]::Out.WriteLine($env:DOTNET_ROOT); [Console]::Error.WriteLine('owned-stderr'); Start-Sleep -Seconds 30");
        start.Environment["DOTNET_ROOT"] = @"C:\synthetic private runtime";
        var child = JobBoundProcess.Start(start, name);
        try
        {
            Assert.IsTrue(IsProcessInJob(child.Process.SafeHandle, job, out var contained) && contained);
            Assert.AreEqual(@"C:\synthetic private runtime", await child.Output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.AreEqual("owned-stderr", await child.Error.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            job.Dispose(); // No retained launcher job handle may defeat abrupt owner loss.
            await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(child.Process.HasExited);
        }
        finally
        {
            job.Dispose();
            if (!child.Process.HasExited) child.Process.Kill();
            child.Output.Dispose(); child.Error.Dispose(); child.Process.Dispose();
        }
    }

    [TestMethod]
    public void MissingOwnerFailsBeforeCreatingChild()
    {
        var start = new ProcessStartInfo(@"C:\does-not-exist.exe")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        Assert.ThrowsExactly<Win32Exception>(() => JobBoundProcess.Start(start, "Local\\Missing.Owner." + Guid.NewGuid().ToString("N")));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, IntPtr data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool contained);
}
