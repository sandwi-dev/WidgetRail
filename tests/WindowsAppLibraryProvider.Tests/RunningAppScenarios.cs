using System.Reflection;
using System.Runtime.InteropServices;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

internal static class RunningAppScenarios
{
    internal static Task PackagedIdentityInteropIsExactWideAndBounded()
    {
        var method = typeof(WindowsRunningAppObserver).GetMethod(
            "GetApplicationUserModelId",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(method is not null);
        var import = method!.GetCustomAttribute<DllImportAttribute>();
        Assert.True(import is not null);
        Assert.Equal("GetApplicationUserModelId", import!.EntryPoint);
        Assert.True(import.ExactSpelling);
        Assert.Equal(CharSet.Unicode, import.CharSet);
        Assert.Equal(typeof(char[]), method!.GetParameters()[2].ParameterType);

        const string aumid = "Contoso.Package_abcd!App";
        var buffer = (aumid + '\0').ToCharArray();
        Assert.Equal(aumid, WindowsRunningAppObserver.NormalizePackagedIdentityBuffer(
            buffer, checked((uint)buffer.Length)));
        Assert.Equal<string?>(null,
            WindowsRunningAppObserver.NormalizePackagedIdentityBuffer(
                aumid.ToCharArray(), checked((uint)aumid.Length)));
        Assert.Equal<string?>(null,
            WindowsRunningAppObserver.NormalizePackagedIdentityBuffer(
                "Contoso\0App\0".ToCharArray(), 12));
        Assert.Equal<string?>(null,
            WindowsRunningAppObserver.NormalizePackagedIdentityBuffer(
                new char[131], 131));
        return Task.CompletedTask;
    }

    internal static Task WindowShapeExcludesOnlyDesktopShell()
    {
        var shell = (IntPtr)41;
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            shell, shell, true, IntPtr.Zero, false, "Progman", 7, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, IntPtr.Zero, false,
            "Shell_TrayWnd", 7, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, IntPtr.Zero, false,
            "Shell_SecondaryTrayWnd", 7, 101, 202));
        Assert.True(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, IntPtr.Zero, false,
            "CabinetWClass", 7, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, IntPtr.Zero, false, null, 7, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, false, IntPtr.Zero, false,
            "CabinetWClass", 7, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, (IntPtr)99, false,
            "CabinetWClass", 7, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, IntPtr.Zero, true,
            "CabinetWClass", 7, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, IntPtr.Zero, false,
            "CabinetWClass", 0, 101, 202));
        Assert.False(WindowsRunningAppObserver.HasEligibleTopLevelShape(
            (IntPtr)42, shell, true, IntPtr.Zero, false,
            "CabinetWClass", 7, 202, 202));
        Assert.True(WindowsRunningAppObserver.IsTaskbarWindowClass("Shell_TrayWnd"));
        Assert.True(WindowsRunningAppObserver.IsTaskbarWindowClass(
            "Shell_SecondaryTrayWnd"));
        Assert.False(WindowsRunningAppObserver.IsTaskbarWindowClass("shell_traywnd"));
        Assert.False(WindowsRunningAppObserver.IsTaskbarWindowClass("CabinetWClass"));
        return Task.CompletedTask;
    }

    internal static Task WindowClassInteropIsExactWideAndBounded()
    {
        var method = typeof(WindowsRunningAppObserver).GetMethod(
            "GetClassNameW",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(method is not null);
        var import = method!.GetCustomAttribute<DllImportAttribute>();
        Assert.True(import is not null);
        Assert.Equal("GetClassNameW", import!.EntryPoint);
        Assert.True(import.ExactSpelling);
        Assert.Equal(CharSet.Unicode, import.CharSet);
        Assert.Equal(typeof(char[]), method!.GetParameters()[1].ParameterType);
        Assert.Equal(typeof(int), method.GetParameters()[2].ParameterType);
        Assert.Equal(256, WindowsRunningAppObserver.MaximumWindowClassCharacters);
        return Task.CompletedTask;
    }

    internal static Task NativeCallbackDefersManagedFailure()
    {
        var expected = new OperationCanceledException("deferred");
        var shouldContinue = WindowsRunningAppObserver.TryVisitWindow(
            _ => throw expected, (IntPtr)42, out var failure);
        Assert.False(shouldContinue);
        Assert.True(failure is not null);
        var actual = Assert.Throws<OperationCanceledException>(() => failure!.Throw());
        Assert.True(ReferenceEquals(expected, actual));

        shouldContinue = WindowsRunningAppObserver.TryVisitWindow(
            _ => true, (IntPtr)42, out failure);
        Assert.True(shouldContinue);
        Assert.True(failure is null);
        return Task.CompletedTask;
    }

    internal static async Task MapsOnlyExactCurrentRegistrations()
    {
        var source = new Source(2);
        var observer = new Observer([
            new("stable-000", "instance-a"),
            new("missing", "instance-b"),
        ]);
        await using var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, observer);

        var observed = await provider.ObserveRunningAppsAsync(CancellationToken.None);
        Assert.Equal(1, observed.Items.Count);
        Assert.Equal("stable-000", observed.Items[0].StableProviderIdentity);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(observed)
            .Contains("instance-a", StringComparison.Ordinal));

        source.RejectIdentity = "stable-000";
        var replaced = await provider.ObserveRunningAppsAsync(CancellationToken.None);
        Assert.Equal(0, replaced.Items.Count);
    }

    internal static async Task CollapsesDuplicatesAndBoundsResults()
    {
        var source = new Source(80);
        var observations = Enumerable.Range(0, 80)
            .Select(index => new WindowsRunningAppObservation(
                $"stable-{index:D3}", $"instance-{index:D3}"))
            .Prepend(new("stable-000", "duplicate-window"))
            .ToArray();
        await using var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, new Observer(observations));

        var page = await provider.ObserveRunningAppsAsync(CancellationToken.None);
        Assert.Equal(64, page.Items.Count);
        Assert.Equal(64, page.Items.Select(item => item.StableProviderIdentity)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(page.Revision.StartsWith("running-", StringComparison.Ordinal));
    }

    internal static async Task CancellationIgnoringObservationCannotPublish()
    {
        var observer = new BlockingObserver();
        var source = new Source(1);
        var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, observer, TimeSpan.FromMilliseconds(100));
        _ = await provider.GetAppsAsync();
        var observation = Task.Run(() => provider.ObserveRunningAppsAsync(
            CancellationToken.None));
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var failure = await Assert.ThrowsAsync<AggregateException>(() =>
            provider.DisposeAsync().AsTask());
        Assert.True(failure.Flatten().InnerExceptions.Any(exception =>
            exception.Message.Contains("bounded deadline", StringComparison.Ordinal)));
        Assert.Equal(0, source.DisposeCalls);
        Assert.True(provider.HasRetainedCatalogState);
        observer.Release.Set();
        await Assert.ThrowsAsync<OperationCanceledException>(() => observation);
        Assert.Equal(0, source.DisposeCalls);
    }

    internal static async Task CooperativeObservationDrainsBeforeSourceDisposal()
    {
        var observer = new CooperativeObserver();
        var source = new Source(1)
        {
            DisposalSafe = () => observer.Completed,
        };
        var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, observer, TimeSpan.FromMilliseconds(500));
        _ = await provider.GetAppsAsync();
        var observation = Task.Run(() => provider.ObserveRunningAppsAsync(
            CancellationToken.None));
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<OperationCanceledException>(() => observation);
        Assert.Equal(1, source.DisposeCalls);
        Assert.False(source.DisposedBeforeSafe);
    }

    internal static Task NativeWindowVisitsAreBoundedBeforeEligibility()
    {
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        var bound = WindowsRunningAppObserver.MaximumTopLevelWindowVisits;
        foreach (var count in new[] { bound - 1, bound, bound + 1 })
        {
            var ineligible = new WindowReader(count, eligible: false);
            var observations = new WindowsRunningAppObserver(ineligible)
                .Observe(CancellationToken.None);
            Assert.Equal(Math.Min(count, bound), ineligible.Visited);
            Assert.Equal(0, ineligible.ProcessOpenAttempts);
            Assert.Equal(0, observations.Count);
        }

        var eligible = new WindowReader(bound + 1, eligible: true);
        _ = new WindowsRunningAppObserver(eligible).Observe(CancellationToken.None);
        Assert.Equal(bound, eligible.Visited);
        Assert.Equal(bound, eligible.ProcessOpenAttempts);

        var lateCandidate = new WindowReader(bound + 1, eligible: true,
            candidateWindow: bound + 1);
        var late = new WindowsRunningAppObserver(lateCandidate)
            .Observe(CancellationToken.None);
        Assert.Equal(bound, lateCandidate.Visited);
        Assert.Equal(bound, lateCandidate.ProcessOpenAttempts);
        Assert.Equal(0, late.Count);
        return Task.CompletedTask;
    }

    private sealed class Observer(IReadOnlyList<WindowsRunningAppObservation> values) :
        IWindowsRunningAppObserver
    {
        public IReadOnlyList<WindowsRunningAppObservation> Observe(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return values;
        }
    }

    private sealed class BlockingObserver : IWindowsRunningAppObserver
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();

        public IReadOnlyList<WindowsRunningAppObservation> Observe(
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            Release.Wait();
            return [];
        }
    }

    private sealed class CooperativeObserver : IWindowsRunningAppObserver
    {
        private int _completed;
        internal bool Completed => Volatile.Read(ref _completed) != 0;
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<WindowsRunningAppObservation> Observe(
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return [];
            }
            finally
            {
                Interlocked.Exchange(ref _completed, 1);
            }
        }
    }

    private sealed class WindowReader(
        int count,
        bool eligible,
        int? candidateWindow = null) : IWindowsRunningWindowReader
    {
        internal int Visited { get; private set; }
        internal int ProcessOpenAttempts { get; private set; }

        public void Enumerate(Func<IntPtr, bool> visitor)
        {
            for (var index = 1; index <= count; index++)
            {
                Visited++;
                if (!visitor((IntPtr)index)) break;
            }
        }

        public WindowsRunningAppObservation? Inspect(IntPtr window)
        {
            if (!eligible) return null;
            ProcessOpenAttempts++;
            return window.ToInt32() == candidateWindow
                ? new("stable-late", "instance-late")
                : null;
        }
    }

    private sealed class Source(int count) : IGameLibrarySource
    {
        private int _disposeCalls;
        internal string? RejectIdentity { get; set; }
        internal Func<bool>? DisposalSafe { get; init; }
        internal int DisposeCalls => Volatile.Read(ref _disposeCalls);
        internal bool DisposedBeforeSafe { get; private set; }
        public string SourceIdentity => "running-source";
        public string Attribution => "Windows";
        public GameLibrarySourceSnapshot Snapshot { get; private set; } =
            new("running-source", "Windows", 0, GameLibrarySourceHealth.Unavailable, []);

        public GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken)
        {
            var items = Enumerable.Range(0, count).Select(index => new GameLibrarySourceItem(
                SourceIdentity, Attribution, $"stable-{index:D3}", $"App {index:D3}",
                WindowsAppLibraryKind.Application, true, true,
                GameLibrarySourceActions.Launch, "none", $"item-{index:D3}"))
                .ToArray();
            Snapshot = new(SourceIdentity, Attribution, Snapshot.SourceVersion + 1,
                GameLibrarySourceHealth.Healthy, items);
            return Snapshot;
        }

        public GameLibrarySourceItem? ResolveExact(
            GameLibrarySourceItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return string.Equals(item.StableIdentity, RejectIdentity,
                StringComparison.OrdinalIgnoreCase) ? null : item;
        }

        public GameLibraryLaunchResult Launch(
            GameLibrarySourceItem exactItem, CancellationToken cancellationToken) =>
            new(AppLibraryLaunchObservationState.RequestAccepted, GameLibraryLaunchEvidence.None);
        public string? LoadArtwork(
            GameLibrarySourceItem exactItem, CancellationToken cancellationToken) => null;
        public void Dispose()
        {
            DisposedBeforeSafe = DisposalSafe is not null && !DisposalSafe();
            Interlocked.Increment(ref _disposeCalls);
        }
    }

    private sealed class ImmediateSta : IShellStaExecutor
    {
        internal static ImmediateSta Instance { get; } = new();
        public Task<T> RunAsync<T>(Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) => Task.FromResult(operation(cancellationToken));
    }
}
