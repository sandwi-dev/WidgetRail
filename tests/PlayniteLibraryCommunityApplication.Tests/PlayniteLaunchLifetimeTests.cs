using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using WidgetRail.Samples.PlayniteLibrary;

namespace WidgetRail.Tests.PlayniteLibraryCommunityApplication;

[TestClass]
public sealed partial class PlayniteLaunchLifetimeTests
{
    [TestMethod]
    public void LaunchArgumentsRoundTripWithoutAShellInterpreter()
    {
        string[] values = ["", "--userdatadir", @"C:\two words\", "a\"b", @"C:\unicode 日本語 & (profile)\", "--startclosedtotray"];
        var command = "fixture.exe " + string.Join(' ', values.Select(PlayniteDesktopLauncher.QuoteArgument));
        CollectionAssert.AreEqual(values, PlayniteSetupPlatform.SplitArguments(command).Skip(1).ToArray());
    }

    [TestMethod]
    public void CancelledLaunchDoesNotContactTheShell()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => PlayniteDesktopLauncher.StartAsync(new("missing.exe"), cancellation.Token));
    }

    [TestMethod]
    public void MissingApplicationDoesNotFallBackToOtherLaunchRoutes()
    {
        var start = new ProcessStartInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"))
            { WorkingDirectory = Path.GetTempPath() };
        Assert.ThrowsExactly<PlayniteSetupException>(() => PlayniteDesktopLauncher.StartAsync(start, default));
    }

    [TestMethod]
    [DataRow("direct", "job", false)]
    [DataRow("shell", "job", true)]
    [DataRow("shell", "tree", true)]
    [DataRow("shell", "exit", true)]
    public async Task DesktopLaunchSurvivesWorkerOwnershipCleanup(string route, string cleanup, bool independent)
    {
        var root = Path.Combine(Path.GetTempPath(), "WidgetRail.Lifetime." + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "profile with spaces & 日本語");
        Directory.CreateDirectory(directory);
        var receipt = Path.Combine(root, "child.json");
        var release = Path.Combine(root, "release");
        var jobName = "Local\\WidgetRail.Lifetime." + Guid.NewGuid().ToString("N");
        using var job = Native.CreateJobObjectW(0, jobName);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var limits = new Native.ExtendedLimits { Basic = new() { Flags = 0x2000 /* KILL_ON_JOB_CLOSE */ } };
        if (Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimits>()) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        var start = new ProcessStartInfo(FixtureExecutable())
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var value in new[] { "worker", jobName, route, receipt, release, directory }) start.ArgumentList.Add(value);
        start.Environment["WRAIL_LIFETIME_TEST_SENTINEL"] = "worker-only";
        using var worker = Process.Start(start)!;
        var errors = worker.StandardError.ReadToEndAsync();
        Process? child = null;
        try
        {
            var ready = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if (ready != "launched") Assert.Fail("Fixture failed before launch: " + await errors.WaitAsync(TimeSpan.FromSeconds(5)));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(receipt)) await Task.Delay(50, deadline.Token);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(receipt, deadline.Token));
            var data = result.RootElement;
            child = Process.GetProcessById(data.GetProperty("ProcessId").GetInt32());
            Assert.IsTrue(Native.IsProcessInJob(worker.SafeHandle, job, out var workerContained) != 0 && workerContained != 0);
            Assert.IsTrue(Native.IsProcessInJob(child.SafeHandle, job, out var childContained) != 0);
            Assert.AreEqual(!independent, childContained != 0, "The old launch inherits the job; Explorer launch must not.");
            Assert.AreEqual(worker.SessionId, child.SessionId, "Stay in the desktop user's session.");
            Assert.AreEqual(directory, data.GetProperty("Directory").GetString());
            Assert.AreEqual(independent ? null : "worker-only", data.GetProperty("Sentinel").GetString(),
                "An independent launch must not inherit widget runtime environment overrides.");
            CollectionAssert.AreEqual(new[] { "--userdatadir", directory, "", "quotes\"and spaces", @"C:\trailing slash\",
                "--startclosedtotray", "--hidesplashscreen" },
                data.GetProperty("Arguments").EnumerateArray().Select(item => item.GetString()!).ToArray());

            if (cleanup == "tree") worker.Kill(entireProcessTree: true);
            else if (cleanup == "exit") { await worker.StandardInput.WriteLineAsync("exit"); await worker.StandardInput.FlushAsync(); }
            else job.Dispose();
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            job.Dispose(); // Also exercise ordinary job retirement after graceful exit/tree kill.
            if (independent)
            {
                var exited = child.WaitForExitAsync();
                Assert.AreNotEqual(exited, await Task.WhenAny(exited, Task.Delay(750)), "The launched app must survive worker retirement.");
                File.WriteAllText(release, "done");
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual(0, child.ExitCode);
            }
            else await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            job.Dispose();
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            File.WriteAllText(release, "cleanup");
            if (child is not null)
            {
                if (!child.HasExited) await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                child.Dispose();
            }
            Directory.Delete(root, true);
        }
    }

    private static string FixtureExecutable()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json"))) current = current.Parent;
        Assert.IsNotNull(current);
        var configuration = typeof(PlayniteLaunchLifetimeTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var file = Path.Combine(current.FullName, "tests", "PlayniteLaunchLifetimeFixture", "bin", configuration,
            "net8.0-windows10.0.19041.0", "win-x64", "PlayniteLaunchLifetimeFixture.exe");
        Assert.IsTrue(File.Exists(file), file);
        return file;
    }

    private static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public nuint MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct ExtendedLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial SafeFileHandle CreateJobObjectW(nint attributes, string name);
        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial int SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, uint size);
        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial int IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, out int contained);
    }
}
