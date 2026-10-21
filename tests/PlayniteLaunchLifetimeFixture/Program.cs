using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using WidgetRail.Samples.PlayniteLibrary;

// Disposable native processes only. Never opens Playnite or reads its library/profile.
if (args is ["child", var receipt, var release, ..])
{
    File.WriteAllText(receipt + ".tmp", JsonSerializer.Serialize(new
    {
        ProcessId = Environment.ProcessId,
        Arguments = args.Skip(3).ToArray(),
        Directory = Environment.CurrentDirectory,
        Sentinel = Environment.GetEnvironmentVariable("WRAIL_LIFETIME_TEST_SENTINEL"),
    }));
    File.Move(receipt + ".tmp", receipt);
    var deadline = Stopwatch.StartNew();
    while (!File.Exists(release) && deadline.Elapsed < TimeSpan.FromSeconds(90)) await Task.Delay(50);
    return 0;
}

if (args is not ["worker", var jobName, var mode, var childReceipt, var releasePath, var workingDirectory]) return 2;
using (var job = Native.OpenJobObjectW(0x0001 /* assign */, 0, jobName))
{
    if (job.IsInvalid || Native.AssignProcessToJobObject(job, new nint(-1)) == 0)
        throw new Win32Exception(Marshal.GetLastPInvokeError());
} // Only the test harness keeps the job alive.

var start = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = workingDirectory,
    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
foreach (var argument in new[] { "child", childReceipt, releasePath, "--userdatadir", workingDirectory,
    "", "quotes\"and spaces", @"C:\trailing slash\", "--startclosedtotray", "--hidesplashscreen" })
    start.ArgumentList.Add(argument);
if (mode == "direct") { using var child = Process.Start(start); }
else await PlayniteDesktopLauncher.StartAsync(start, default);
Console.WriteLine("launched");
Console.Out.Flush();
await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90));
return 0;

internal static partial class Native
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial SafeFileHandle OpenJobObjectW(uint access, int inherit, string name);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial int AssignProcessToJobObject(SafeFileHandle job, nint process);
}
