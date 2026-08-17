using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

internal static class ProviderLifetimeScenarios
{
    internal static async Task CompositeDisposesOwnedSources()
    {
        var source = new ControlledGameSource("source-composite");
        var provider = new WindowsAppLibraryProvider([source], ImmediateSta.Instance);
        var inert = new InertAudioNetworkBackend();
        await using (var composite = new CompositePlatformBrokerBackend(
                         inert, inert, appLibrary: provider))
        {
            await provider.GetAppsAsync();
        }

        Assert.Equal(1, source.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() =>
            provider.GetAppsAsync().GetAwaiter().GetResult());
    }

    internal static async Task ConcurrentDisposalCleansEverySource()
    {
        var first = new ControlledGameSource("source-first");
        var failing = new ControlledGameSource("source-failing")
        {
            DisposeFailure = new InvalidOperationException("bounded source drain failure"),
        };
        var last = new ControlledGameSource("source-last");
        var provider = new WindowsAppLibraryProvider(
            [first, failing, last], ImmediateSta.Instance);

        var disposalOne = provider.DisposeAsync().AsTask();
        var disposalTwo = provider.DisposeAsync().AsTask();
        var firstFailure = await Assert.ThrowsAsync<AggregateException>(() => disposalOne);
        var secondFailure = await Assert.ThrowsAsync<AggregateException>(() => disposalTwo);

        Assert.True(firstFailure.Message.Contains(
            "bounded terminal cleanup", StringComparison.Ordinal));
        Assert.True(secondFailure.Message.Contains(
            "bounded terminal cleanup", StringComparison.Ordinal));
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, failing.DisposeCalls);
        Assert.Equal(1, last.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() =>
            provider.RefreshAsync().GetAwaiter().GetResult());
        Assert.Equal(0, first.RefreshCalls);
        Assert.Equal(0, failing.RefreshCalls);
        Assert.Equal(0, last.RefreshCalls);
    }

    internal static async Task ActiveWorkCancelsBeforeTerminalCleanup()
    {
        var source = new ControlledGameSource("source-active")
        {
            WaitForCancellation = true,
        };
        var provider = new WindowsAppLibraryProvider([source], ImmediateSta.Instance);
        var refresh = Task.Run(() => provider.RefreshAsync());
        await source.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(1, source.DisposeCalls);
        Assert.True(source.CancellationObserved);
        Assert.False(source.DisposedBeforeCancellation);
    }

    internal static async Task CancellationIgnoringCompletionCannotPublish()
    {
        var source = new ControlledGameSource("source-late")
        {
            IgnoreCancellationUntilReleased = true,
            BlockOnRefreshCall = 2,
        };
        var provider = new WindowsAppLibraryProvider([source], ImmediateSta.Instance);
        _ = await provider.GetAppsAsync();
        var refresh = Task.Run(() => provider.RefreshAsync());
        await source.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposal = provider.DisposeAsync().AsTask();
        await source.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, source.DisposeCalls);
        Assert.True(provider.HasRetainedCatalogState);
        source.ReleaseOperation.Set();

        await Assert.ThrowsAsync<OperationCanceledException>(() => refresh);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, source.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() =>
            provider.GetAppsAsync().GetAwaiter().GetResult());
    }

    internal static async Task UncooperativeWorkTimesOutSafely()
    {
        var source = new ControlledGameSource("source-timeout")
        {
            IgnoreCancellationUntilReleased = true,
            BlockOnRefreshCall = 2,
        };
        var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, TimeSpan.FromMilliseconds(100));
        _ = await provider.GetAppsAsync();
        var refresh = Task.Run(() => provider.RefreshAsync());
        await source.OperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(failure.Flatten().InnerExceptions.Any(exception =>
            exception.Message.Contains("bounded deadline", StringComparison.Ordinal)));
        Assert.Equal(0, source.DisposeCalls);
        Assert.True(provider.HasRetainedCatalogState);
        var repeated = await Assert.ThrowsAsync<AggregateException>(() =>
            provider.DisposeAsync().AsTask());
        Assert.Equal(failure.Message, repeated.Message);

        source.ReleaseOperation.Set();
        await Assert.ThrowsAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(0, source.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() =>
            provider.GetAppLibraryIconAsync("app-any", CancellationToken.None)
                .GetAwaiter().GetResult());
    }

    private sealed class ControlledGameSource(string identity) : IGameLibrarySource
    {
        private int _disposeCalls;
        private int _refreshCalls;

        internal bool WaitForCancellation { get; init; }
        internal bool IgnoreCancellationUntilReleased { get; init; }
        internal int BlockOnRefreshCall { get; init; } = 1;
        internal Exception? DisposeFailure { get; init; }
        internal int DisposeCalls => Volatile.Read(ref _disposeCalls);
        internal int RefreshCalls => Volatile.Read(ref _refreshCalls);
        internal bool CancellationObserved { get; private set; }
        internal bool DisposedBeforeCancellation { get; private set; }
        internal TaskCompletionSource OperationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationSeen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseOperation { get; } = new();

        public string SourceIdentity => identity;
        public string Attribution => identity;
        public GameLibrarySourceSnapshot Snapshot { get; private set; } =
            new(identity, identity, 0, GameLibrarySourceHealth.Unavailable, []);

        public GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _refreshCalls);
            if ((WaitForCancellation || IgnoreCancellationUntilReleased) &&
                call >= BlockOnRefreshCall)
            {
                OperationStarted.TrySetResult();
                using var registration = cancellationToken.Register(() =>
                {
                    CancellationObserved = true;
                    CancellationSeen.TrySetResult();
                });
                if (WaitForCancellation)
                {
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (IgnoreCancellationUntilReleased)
                    ReleaseOperation.Wait();
            }
            cancellationToken.ThrowIfCancellationRequested();
            Snapshot = new(identity, identity, 1, GameLibrarySourceHealth.Healthy,
                [Item(identity)]);
            return Snapshot;
        }

        public GameLibrarySourceItem? ResolveExact(
            GameLibrarySourceItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return item;
        }

        public GameLibraryLaunchResult Launch(
            GameLibrarySourceItem exactItem, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(AppLibraryLaunchObservationState.RequestAccepted,
                GameLibraryLaunchEvidence.None);
        }

        public string? LoadArtwork(
            GameLibrarySourceItem exactItem, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        public void Dispose()
        {
            DisposedBeforeCancellation =
                (WaitForCancellation || IgnoreCancellationUntilReleased) &&
                !CancellationObserved;
            Interlocked.Increment(ref _disposeCalls);
            DisposeStarted.TrySetResult();
            if (DisposeFailure is not null) throw DisposeFailure;
        }

        private static GameLibrarySourceItem Item(string sourceIdentity) => new(
            sourceIdentity,
            sourceIdentity,
            $"stable-{sourceIdentity}",
            sourceIdentity,
            WindowsAppLibraryKind.Game,
            true,
            true,
            GameLibrarySourceActions.Launch,
            $"art-{sourceIdentity}",
            $"item-{sourceIdentity}");
    }

    private sealed class ImmediateSta : IShellStaExecutor
    {
        internal static ImmediateSta Instance { get; } = new();
        public Task<T> RunAsync<T>(
            Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }

    private sealed class InertAudioNetworkBackend :
        IAudioPlatformBrokerBackend,
        INetworkPlatformBrokerBackend
    {
        public event EventHandler<BrokerPlatformEvent>? EventPublished { add { } remove { } }

        public Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
            CancellationToken cancellationToken) => Unsupported<IReadOnlyList<AudioSessionSummary>>();
        public Task SetAudioSessionVolumeAsync(string sessionId, double volume,
            CancellationToken cancellationToken) => Unsupported();
        public Task SetAudioSessionMutedAsync(string sessionId, bool isMuted,
            CancellationToken cancellationToken) => Unsupported();
        public Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken) =>
            Unsupported<AudioOutputSummary>();
        public Task SetAudioOutputVolumeAsync(double volume, CancellationToken cancellationToken) =>
            Unsupported();
        public Task SetAudioOutputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
            Unsupported();
        public Task<IReadOnlyList<AudioDeviceSummary>> GetAudioDevicesAsync(
            CancellationToken cancellationToken) => Unsupported<IReadOnlyList<AudioDeviceSummary>>();
        public Task<AudioInputSummary> GetAudioInputAsync(CancellationToken cancellationToken) =>
            Unsupported<AudioInputSummary>();
        public Task SetAudioInputVolumeAsync(double volume, CancellationToken cancellationToken) =>
            Unsupported();
        public Task SetAudioInputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
            Unsupported();
        public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken) =>
            Unsupported<NetworkStatusSummary>();
        public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(
            CancellationToken cancellationToken) => Unsupported<IReadOnlyList<SavedNetworkProfileSummary>>();
        public Task SwitchSavedNetworkProfileAsync(string profileId,
            CancellationToken cancellationToken) => Unsupported();
        public Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(
            CancellationToken cancellationToken) => Unsupported<AvailableWifiNetworksSummary>();
        public Task RequestWifiScanAsync(CancellationToken cancellationToken) => Unsupported();
        public Task ConnectAvailableWifiNetworkAsync(string networkId,
            CancellationToken cancellationToken) => Unsupported();
        public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken) =>
            Unsupported<WifiRadioSummary>();
        public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken) => Unsupported();

        private static Task Unsupported() => Task.FromException(
            new InvalidOperationException("Inert backend should not be called."));
        private static Task<T> Unsupported<T>() => Task.FromException<T>(
            new InvalidOperationException("Inert backend should not be called."));
    }
}
