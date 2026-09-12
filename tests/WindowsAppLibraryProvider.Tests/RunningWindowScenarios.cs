using System.ComponentModel;
using WidgetRail.WindowsAppLibraryProvider;

internal static class RunningWindowScenarios
{
    internal static Task EligibilityAndOwnership()
    {
        var native = new Native();
        var normal = new RunningWindowInfo(1, 1, 0, 11, "DesktopApp");
        native.Windows[1] = normal;
        // Minimized windows remain WS_VISIBLE; no size or foreground requirement.
        Assert.True(Inspect(1, native) is not null);
        foreach (var excluded in new[]
        {
            normal with { Visible = false }, normal with { Cloaked = true },
            normal with { ExtendedStyle = WindowsRunningWindowPolicy.ToolWindow },
            normal with { ExtendedStyle = WindowsRunningWindowPolicy.NoActivate },
            normal with { ProcessId = 0 }, normal with { ProcessId = 999 },
            normal with { Root = 2 }, normal with { ClassName = "" },
            normal with { ClassName = "Progman" }, normal with { ClassName = "WorkerW" },
            normal with { ClassName = "Shell_TrayWnd" },
            normal with { ClassName = "Shell_SecondaryTrayWnd" },
        })
        {
            native.Windows[1] = excluded;
            Assert.True(Inspect(1, native) is null);
        }
        native.Windows[1] = normal with
        {
            ExtendedStyle = WindowsRunningWindowPolicy.AppWindow | WindowsRunningWindowPolicy.NoActivate,
        };
        Assert.True(Inspect(1, native) is not null);
        native.Windows[1] = normal;
        native.ShellWindow = 1;
        Assert.True(Inspect(1, native) is null);
        native.ShellWindow = 0;

        var dialog = new RunningWindowInfo(2, 2, 1, 11, "Dialog");
        native.Windows[2] = dialog;
        native.Popups[1] = 2;
        Assert.True(Inspect(1, native) is null);
        Assert.True(Inspect(2, native) is not null);
        native.Windows[2] = dialog with { Visible = false };
        Assert.True(Inspect(1, native) is not null);
        native.Windows[2] = dialog with { ExtendedStyle = WindowsRunningWindowPolicy.ToolWindow };
        Assert.True(Inspect(1, native) is not null);
        Assert.True(Inspect(2, native) is null);
        native.Windows[2] = dialog with { ExtendedStyle = WindowsRunningWindowPolicy.AppWindow };
        native.Popups.Clear();
        Assert.True(Inspect(2, native) is not null);

        native.Windows[1] = normal with { Owner = 2 };
        native.Windows[2] = dialog;
        Assert.True(Inspect(1, native) is null);
        native.Windows[1] = normal;
        native.Windows[2] = dialog with { Visible = false };
        native.Windows[3] = dialog with { Handle = 3, Root = 3, Visible = false };
        native.Popups[1] = 2;
        native.Popups[2] = 3;
        native.Popups[3] = 3;
        Assert.True(Inspect(1, native) is not null);
        native.Popups[3] = 2;
        Assert.True(Inspect(1, native) is null);
        return Task.CompletedTask;
    }

    internal static Task HostedAppIdentity()
    {
        var native = new Native();
        native.Windows[1] = new(1, 1, 0, 11, "ApplicationFrameWindow");
        native.Windows[2] = new(2, 1, 0, 22, "Windows.UI.Core.CoreWindow");
        native.Children = [2];
        native.Packages[22] = new("apps-dts", "dts-instance");
        Assert.Equal("apps-dts", Inspect(1, native)!.RegistrationIdentity);
        Assert.True(native.ProcessReads.All(read => read.PackagedOnly && read.Pid == 22));
        native.Children = [2, 2];
        Assert.Equal("apps-dts", Inspect(1, native)!.RegistrationIdentity);
        native.Windows[3] = new(3, 1, 0, 33, "Windows.UI.Core.CoreWindow");
        native.Packages[33] = new("apps-other", "other-instance");
        native.Children = [2, 3];
        Assert.True(Inspect(1, native) is null);
        native.Packages[33] = null;
        Assert.True(Inspect(1, native) is null);
        native.Children = null; // Native traversal bound was reached.
        Assert.True(Inspect(1, native) is null);
        native.Children = Enumerable.Repeat((nint)2, 129).ToArray();
        Assert.True(Inspect(1, native) is null);
        native.Children = [2];
        native.TrustedFrame = false;
        Assert.True(Inspect(1, native) is null);
        native.TrustedFrame = true;
        var child = native.Windows[2];
        foreach (var excluded in new[]
        {
            child with { Visible = false }, child with { Root = 9 },
            child with { ProcessId = 11 }, child with { ProcessId = 999 },
            child with { ClassName = "BrowserTab" },
        })
        {
            native.Windows[2] = excluded;
            Assert.True(Inspect(1, native) is null);
        }
        native.Windows[2] = child;
        native.OnProcess = () => native.Windows[2] = child with { ProcessId = 44 };
        Assert.True(Inspect(1, native) is null);
        native.OnProcess = null;
        native.Windows[2] = child;
        native.OnProcess = () => native.Windows.Remove(1);
        Assert.True(Inspect(1, native) is null);
        return Task.CompletedTask;
    }

    internal static Task FailureIsolation()
    {
        foreach (var error in new Exception[]
        {
            new Win32Exception(5), new UnauthorizedAccessException(),
            new IOException(), new ArgumentException(), new NotSupportedException(),
            new InvalidOperationException(), new System.Runtime.InteropServices.COMException(),
        })
        {
            var reader = new FaultingReader(error);
            var found = new WindowsRunningAppObserver(reader).Observe(CancellationToken.None);
            Assert.Equal(1, found.Count);
            Assert.Equal("healthy", found[0].RegistrationIdentity);
        }
        Assert.Throws<OperationCanceledException>(() =>
            new WindowsRunningAppObserver(new FaultingReader(new OperationCanceledException()))
                .Observe(CancellationToken.None));
        return Task.CompletedTask;
    }

    private static WindowsRunningAppObservation? Inspect(nint window, Native native) =>
        WindowsRunningWindowPolicy.Inspect(window, native, 999);

    private sealed class FaultingReader(Exception failure) : IWindowsRunningWindowReader
    {
        public void Enumerate(Func<nint, bool> visitor)
        {
            if (visitor(1)) visitor(2);
        }
        public WindowsRunningAppObservation? Inspect(nint window) =>
            window == 1 ? throw failure : new("healthy", "instance");
    }

    private sealed class Native : IRunningWindowNative
    {
        internal Dictionary<nint, RunningWindowInfo> Windows { get; } = [];
        internal Dictionary<nint, nint> Popups { get; } = [];
        internal Dictionary<uint, WindowsRunningAppObservation?> Packages { get; } = [];
        internal IReadOnlyList<nint>? Children { get; set; } = [];
        internal bool TrustedFrame { get; set; } = true;
        internal Action? OnProcess { get; set; }
        internal List<(uint Pid, bool PackagedOnly)> ProcessReads { get; } = [];
        public nint ShellWindow { get; set; }
        public RunningWindowInfo? Read(nint window) => Windows.GetValueOrDefault(window);
        public nint LastActivePopup(nint window) => Popups.GetValueOrDefault(window, window);
        public bool IsApplicationFrameHost(uint processId) => TrustedFrame;
        public IReadOnlyList<nint>? Descendants(nint window) => Children;
        public WindowsRunningAppObservation? Process(uint processId, bool packagedOnly)
        {
            ProcessReads.Add((processId, packagedOnly));
            OnProcess?.Invoke();
            return packagedOnly ? Packages.GetValueOrDefault(processId) : new("desktop", "instance");
        }
    }
}

