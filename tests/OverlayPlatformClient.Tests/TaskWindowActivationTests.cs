using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WindowsWindowActivation;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class TaskWindowActivationTests
{
    private static readonly TaskWindowTarget Target = new(42, 51, 1234, "ExampleWindow");

    [TestMethod]
    public async Task PreparedHandoffWaitsThenRevalidatesBeforeHideAndBroker()
    {
        var native = new Fake();
        var prepared = new TaskCompletionSource<bool>();
        var pending = new TaskWindowActivation(native).ExecuteAsync(Target, 1000, 900, () => true,
            () => native.Calls.Add("hide"), () => { native.Calls.Add("broker"); return Task.FromResult(TaskWindowActivationResult.Requested); },
            prepareHandoff: () => prepared.Task);
        CollectionAssert.AreEqual(new[] { "validate" }, native.Calls);
        Assert.IsFalse(pending.IsCompleted);
        prepared.SetResult(true);
        Assert.AreEqual(TaskWindowActivationResult.Requested, await pending);
        CollectionAssert.AreEqual(new[] { "validate", "validate", "hide", "validate", "broker" }, native.Calls);
    }

    [TestMethod]
    public async Task PreparedHandoffRejectsCancelExpiryForegroundAuthorityAndTargetChanges()
    {
        for (var scenario = 0; scenario < 5; ++scenario)
        {
            var native = new Fake(); var authority = true;
            var result = await new TaskWindowActivation(native).ExecuteAsync(Target, 1000, 900, () => authority,
                () => Assert.Fail("Rejected handoff must not hide"),
                () => throw new AssertFailedException("Rejected handoff reached broker"),
                prepareHandoff: () =>
                {
                    if (scenario == 1) native.UptimeMilliseconds = 3001;
                    if (scenario == 2) native.IsOverlayForeground = false;
                    if (scenario == 3) authority = false;
                    if (scenario == 4) native.Current = false;
                    return Task.FromResult(scenario != 0);
                });
            Assert.AreEqual(TaskWindowActivationResult.Rejected, result);
        }
    }

    [TestMethod]
    public async Task AsyncCompletionHidesAndRevalidatesBeforeCallingBrokerOnly()
    {
        var native = new Fake();
        var result = await new TaskWindowActivation(native).ExecuteAsync(Target, 1000, 900, () => true,
            () => { native.Calls.Add("hide"); native.IsOverlayForeground = false; },
            () => { native.Calls.Add("broker"); return Task.FromResult(TaskWindowActivationResult.Denied); });
        Assert.AreEqual(TaskWindowActivationResult.Denied, result);
        CollectionAssert.AreEqual(new[] { "validate", "hide", "validate", "broker" }, native.Calls);
    }

    [TestMethod]
    public async Task AsyncCompletionRejectsChangedAuthorityIdentityAndExpiryAfterHide()
    {
        for (var scenario = 0; scenario < 3; scenario++)
        {
            var native = new Fake();
            var authority = true;
            var result = await new TaskWindowActivation(native).ExecuteAsync(Target, 1000, 900, () => authority,
                () =>
                {
                    if (scenario == 0) authority = false;
                    if (scenario == 1) native.Current = false;
                    if (scenario == 2) native.UptimeMilliseconds = 3001;
                }, () => throw new AssertFailedException("Stale completion reached broker."));
            Assert.AreEqual(TaskWindowActivationResult.Rejected, result);
            Assert.IsFalse(native.Calls.Contains("activate"));
        }
    }

    [TestMethod]
    public void DiagnosticsReportSpecificRejectionWithoutChangingAdmission()
    {
        var native = new Fake { IsOverlayForeground = false };
        var messages = new List<string>();
        var hides = 0;
        var result = new TaskWindowActivation(native).Execute(Target, 1000, 900, () => true, () => ++hides, messages.Add);
        Assert.AreEqual(TaskWindowActivationResult.Rejected, result);
        Assert.AreEqual(0, hides);
        CollectionAssert.AreEqual(new[] { "rejected reason=not-foreground" }, messages);
        Assert.IsFalse(native.Calls.Contains("activate"));
    }

    [TestMethod]
    public void DiagnosticsDistinguishAuthorityLossAfterHide()
    {
        var native = new Fake();
        var current = true;
        var messages = new List<string>();
        var result = new TaskWindowActivation(native).Execute(Target, 1000, 900, () => current, () => current = false, messages.Add);
        Assert.AreEqual(TaskWindowActivationResult.Rejected, result);
        CollectionAssert.AreEqual(new[] { "phase=before-hide", "rejected reason=authority-after-hide" }, messages);
        Assert.IsFalse(native.Calls.Contains("activate"));
    }

    [TestMethod]
    public void DiagnosticsReportDenialWithoutRetry()
    {
        var native = new Fake { Accepted = false };
        var messages = new List<string>();
        var result = new TaskWindowActivation(native).Execute(Target, 1000, 900, () => true, () => { }, messages.Add);
        Assert.AreEqual(TaskWindowActivationResult.Denied, result);
        Assert.IsTrue(messages.Contains("phase=activation-returned accepted=False"));
        Assert.AreEqual(1, native.Calls.Count(value => value == "activate"));
    }

    [TestMethod]
    public void DiagnosticFailureCannotAlterActivation()
    {
        var native = new Fake();
        var hides = 0;
        var result = new TaskWindowActivation(native).Execute(Target, 1000, 900, () => true, () => ++hides,
            _ => throw new IOException("logging unavailable"));
        Assert.AreEqual(TaskWindowActivationResult.Requested, result);
        Assert.AreEqual(1, hides);
        Assert.AreEqual(1, native.Calls.Count(value => value == "activate"));
    }

    [TestMethod]
    public void HandoffReleasesOverlayBeforeRevalidatingAndActivating()
    {
        var native = new Fake();
        var result = new TaskWindowActivation(native).Execute(Target, 1000, 900, () => true,
            () => { native.Calls.Add("hide"); native.IsOverlayForeground = false; });
        Assert.AreEqual(TaskWindowActivationResult.Requested, result);
        CollectionAssert.AreEqual(new[] { "validate", "hide", "validate", "activate" }, native.Calls);
    }

    [TestMethod]
    public void StaleFutureOrPriorVisibleSessionNeverHides()
    {
        foreach (var initiated in new long[] { 899, 999, 3001 })
        {
            var native = new Fake { UptimeMilliseconds = 3000 };
            var result = new TaskWindowActivation(native).Execute(Target, initiated, 900, () => true,
                () => Assert.Fail("Must not hide"));
            Assert.AreEqual(TaskWindowActivationResult.Rejected, result);
            Assert.IsEmpty(native.Calls);
        }
    }

    [TestMethod]
    public void LostForegroundRetiredWorkerOrReusedWindowNeverHides()
    {
        for (int scenario = 0; scenario < 3; ++scenario)
        {
            var native = new Fake { IsOverlayForeground = scenario != 0, Current = scenario != 2 };
            var result = new TaskWindowActivation(native).Execute(Target, 1000, 900, () => scenario != 1,
                () => Assert.Fail("Must not hide"));
            Assert.AreEqual(TaskWindowActivationResult.Rejected, result);
            Assert.IsFalse(native.Calls.Contains("activate"));
        }
    }

    [TestMethod]
    public void RetirementExpirationOrReusedWindowDuringHideNeverActivates()
    {
        for (int scenario = 0; scenario < 3; ++scenario)
        {
            var native = new Fake();
            bool authority = true;
            var result = new TaskWindowActivation(native).Execute(Target, 1000, 900, () => authority, () =>
            {
                if (scenario == 0) authority = false;
                if (scenario == 1) native.UptimeMilliseconds = 3001;
                if (scenario == 2) native.Current = false;
            });
            Assert.AreEqual(TaskWindowActivationResult.Rejected, result);
            Assert.IsFalse(native.Calls.Contains("activate"));
        }
    }

    [TestMethod]
    public void DeniedActivationIsSingleAttemptAndDoesNotReopenOverlay()
    {
        var native = new Fake { Accepted = false };
        int hides = 0;
        Assert.AreEqual(TaskWindowActivationResult.Denied,
            new TaskWindowActivation(native).Execute(Target, 1000, 900, () => true, () => ++hides));
        Assert.AreEqual(1, hides);
        Assert.AreEqual(1, native.Calls.Count(value => value == "activate"));
    }

    [TestMethod]
    public void NativeValidationRejectsInvalidAndOwnProcessTargets()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only platform adapter.");
        var native = new WindowsTaskWindowActivation();
        Assert.IsFalse(native.IsCurrent(Target with { Handle = 0 }));
        Assert.IsFalse(native.IsCurrent(Target with { ProcessId = (uint)Environment.ProcessId }));
        Assert.IsFalse(native.IsCurrent(Target with { ClassName = new string('x', 256) }));
        Assert.IsFalse(native.RequestActivation(Target with { Handle = 0 }));
    }

    [TestMethod]
    public async Task NativeValidationChecksRealExternalWindowIdentityAndRetirement()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only platform adapter.");
        // Create an invisible owned helper window. Never activate another user application.
        const string script = """
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type 'using System; using System.Runtime.InteropServices; using System.Text; public static class WindowClass { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count); }'
            $form = New-Object System.Windows.Forms.Form
            $hwnd = $form.Handle
            $name = New-Object System.Text.StringBuilder 256
            [void][WindowClass]::GetClassName($hwnd, $name, 256)
            $process = [System.Diagnostics.Process]::GetCurrentProcess()
            [Console]::WriteLine((@{ Handle=[uint64]$hwnd.ToInt64(); ProcessId=[uint32]$PID; ProcessCreated=[uint64]$process.StartTime.ToUniversalTime().ToFileTimeUtc(); ClassName=$name.ToString() } | ConvertTo-Json -Compress))
            [void][Console]::ReadLine()
            $form.Dispose()
            """;
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-STA", "-EncodedCommand",
            Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using var helper = System.Diagnostics.Process.Start(start)!;
        try
        {
            var json = await helper.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsNotNull(json);
            var target = System.Text.Json.JsonSerializer.Deserialize<TaskWindowTarget>(json)!;
            var native = new WindowsTaskWindowActivation();
            Assert.IsTrue(native.IsCurrent(target));
            Assert.IsFalse(native.IsCurrent(target with { ProcessId = target.ProcessId + 1 }));
            Assert.IsFalse(native.IsCurrent(target with { ProcessCreated = target.ProcessCreated + 1 }));
            Assert.IsFalse(native.IsCurrent(target with { ClassName = "DifferentWindowClass" }));
            await helper.StandardInput.WriteLineAsync("close");
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(native.IsCurrent(target));
        }
        finally
        {
            if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(); }
        }
    }

    private sealed class Fake : ITaskWindowActivationPlatform
    {
        public List<string> Calls { get; } = [];
        public long UptimeMilliseconds { get; set; } = 2000;
        public bool IsOverlayForeground { get; set; } = true;
        internal bool Current { get; set; } = true;
        internal bool Accepted { get; set; } = true;
        public bool IsCurrent(TaskWindowTarget target) { Assert.AreEqual(Target, target); Calls.Add("validate"); return Current; }
        public bool RequestActivation(TaskWindowTarget target) { Assert.AreEqual(Target, target); Calls.Add("activate"); return Accepted; }
    }
}
