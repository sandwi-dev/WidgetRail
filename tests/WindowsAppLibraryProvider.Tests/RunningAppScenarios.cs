using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsAppLibraryProvider;

internal static class RunningAppScenarios
{
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
        var provider = new WindowsAppLibraryProvider(
            [new Source(1)], ImmediateSta.Instance, observer);
        var observation = Task.Run(() => provider.ObserveRunningAppsAsync(
            CancellationToken.None));
        await observer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disposal = provider.DisposeAsync().AsTask();
        observer.Release.Set();
        await Assert.ThrowsAsync<OperationCanceledException>(() => observation);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
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
        internal string? RejectIdentity { get; set; }
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
        public void Dispose() { }
    }

    private sealed class ImmediateSta : IShellStaExecutor
    {
        internal static ImmediateSta Instance { get; } = new();
        public Task<T> RunAsync<T>(Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) => Task.FromResult(operation(cancellationToken));
    }
}
