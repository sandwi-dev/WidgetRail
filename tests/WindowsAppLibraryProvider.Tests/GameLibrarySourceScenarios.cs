using System.Text.Json;
using GameBarAlternative.WindowsAppLibraryProvider;

internal static class GameLibrarySourceScenarios
{
    internal static Task NormalizedAdaptersOwnExactAuthority()
    {
        const string shortcut = @"C:\Menu\Twin.lnk";
        const string aumid = "Contoso.Twin_abcd!Game";
        var start = new SourceStartMenu(
            new StartMenuRegistration(
                "start-twin", "Twin", StartMenuScope.CurrentUser, shortcut, "lnk-one"));
        var apps = new SourceAppsFolder(
            new AppsFolderRegistration(
                "apps-twin", "Twin", aumid, "aumid-one"));
        var shell = new SourceShellLauncher();
        var packaged = new SourcePackagedLauncher();
        using var windows = new WindowsInstalledGameLibrarySource(
            start, apps, shell, packaged, new SourceIcon());

        var windowsSnapshot = windows.Refresh(CancellationToken.None);
        Assert.Equal(WindowsInstalledGameLibrarySource.StableSourceIdentity,
            windowsSnapshot.SourceIdentity);
        Assert.Equal("Windows", windowsSnapshot.Attribution);
        Assert.Equal(1L, windowsSnapshot.SourceVersion);
        Assert.Equal(GameLibrarySourceHealth.Healthy, windowsSnapshot.Health);
        Assert.Equal(2, windowsSnapshot.Items.Count);
        Assert.True(windowsSnapshot.Items.All(item =>
            item.Installed && item.Available &&
            item.SupportedActions.HasFlag(GameLibrarySourceActions.Launch) &&
            item.SupportedActions.HasFlag(GameLibrarySourceActions.Artwork)));

        var projectedJson = JsonSerializer.Serialize(windowsSnapshot.Items);
        Assert.False(projectedJson.Contains(shortcut, StringComparison.OrdinalIgnoreCase));
        Assert.False(projectedJson.Contains(aumid, StringComparison.OrdinalIgnoreCase));

        foreach (var item in windowsSnapshot.Items)
        {
            var exact = windows.ResolveExact(item, CancellationToken.None);
            Assert.True(exact is not null);
            windows.Launch(exact!, CancellationToken.None);
        }
        Assert.Equal(shortcut, shell.Paths.Single());
        Assert.Equal(aumid, packaged.Aumids.Single());

        var manifest = Path.Combine(Path.GetTempPath(), "appmanifest_620.acf");
        var steamSource = new SourceSteam(
            new SteamRegistration(
                "steam-620", "Twin", "620", manifest, "acf-one"));
        var steamLauncher = new SourceSteamLauncher();
        using var steam = new SteamGameLibrarySource(steamSource, steamLauncher);
        var steamSnapshot = steam.Refresh(CancellationToken.None);
        Assert.Equal(SteamGameLibrarySource.StableSourceIdentity,
            steamSnapshot.SourceIdentity);
        Assert.Equal("Steam", steamSnapshot.Attribution);
        Assert.Equal(WindowsAppLibraryKind.Game, steamSnapshot.Items.Single().Kind);
        Assert.False(steamSnapshot.Items.Single().SupportedActions.HasFlag(
            GameLibrarySourceActions.Artwork));
        var exactSteam = steam.ResolveExact(
            steamSnapshot.Items.Single(), CancellationToken.None);
        Assert.True(exactSteam is not null);
        steam.Launch(exactSteam!, CancellationToken.None);
        Assert.Equal("620", steamLauncher.AppIds.Single());
        return Task.CompletedTask;
    }

    internal static async Task FailureIsolationPreservesLastGood()
    {
        var start = new SourceStartMenu(new StartMenuRegistration(
            "start-one", "Windows One", StartMenuScope.CurrentUser,
            @"C:\Menu\One.lnk", "lnk-one"));
        using var windows = new WindowsInstalledGameLibrarySource(
            start, new SourceAppsFolder(), new SourceShellLauncher(),
            new SourcePackagedLauncher(), new SourceIcon());
        var manifest = Path.Combine(Path.GetTempPath(), "appmanifest_730.acf");
        var steamComponent = new SourceSteam(new SteamRegistration(
            "steam-730", "Steam One", "730", manifest, "acf-one"));
        using var steam = new SteamGameLibrarySource(
            steamComponent, new SourceSteamLauncher());
        var provider = new WindowsAppLibraryProvider(
            [windows, steam], ImmediateStaExecutor.Instance);

        var first = await provider.GetAppsAsync();
        Assert.Equal(2, first.Count);
        start.Items = [new StartMenuRegistration(
            "start-two", "Windows Two", StartMenuScope.CurrentUser,
            @"C:\Menu\Two.lnk", "lnk-two")];
        steamComponent.Fail = true;
        var refreshed = await provider.RefreshAsync();
        Assert.True(refreshed.Any(item => item.DisplayName == "Windows Two"));
        Assert.True(refreshed.Any(item => item.DisplayName == "Steam One"));
        Assert.Equal(GameLibrarySourceHealth.Unavailable, steam.Snapshot.Health);
        Assert.Equal(2L, steam.Snapshot.SourceVersion);
    }

    internal static async Task LateGenerationCannotReplaceCurrent()
    {
        var component = new SequencedAppsFolderSource();
        using var source = new WindowsInstalledGameLibrarySource(
            new SourceStartMenu(), component, new SourceShellLauncher(),
            new SourcePackagedLauncher(), new SourceIcon());
        var first = Task.Run(() => source.Refresh(CancellationToken.None));
        await component.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = await Task.Run(() => source.Refresh(CancellationToken.None));
        Assert.Equal("New", second.Items.Single().DisplayName);
        component.ReleaseFirst.Set();
        var stale = await first.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("New", stale.Items.Single().DisplayName);
        Assert.Equal("New", source.Snapshot.Items.Single().DisplayName);
        Assert.Equal(2L, source.Snapshot.SourceVersion);
        component.Fail = true;
        var degraded = source.Refresh(CancellationToken.None);
        Assert.Equal("New", degraded.Items.Single().DisplayName);
        Assert.Equal(GameLibrarySourceHealth.Degraded, degraded.Health);
        Assert.Equal(3L, degraded.SourceVersion);
    }

    internal static async Task DisposeCancelsAndDrains()
    {
        var component = new CancelableStartMenuSource();
        var source = new WindowsInstalledGameLibrarySource(
            component, new SourceAppsFolder(), new SourceShellLauncher(),
            new SourcePackagedLauncher(), new SourceIcon());
        var refresh = Task.Run(() => source.Refresh(CancellationToken.None));
        await component.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var dispose = Task.Run(source.Dispose);
        try
        {
            await refresh.WaitAsync(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException("Expected canceled source work.");
        }
        catch (OperationCanceledException)
        {
        }
        await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Throws<ObjectDisposedException>(() =>
            source.Refresh(CancellationToken.None));
    }

    private sealed class SourceStartMenu(params StartMenuRegistration[] items) :
        IStartMenuApplicationSource
    {
        internal IReadOnlyList<StartMenuRegistration> Items { get; set; } = items;
        public IReadOnlyList<StartMenuRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Items;
        }
        public StartMenuRegistration? ReadExact(
            string shortcutPath, StartMenuScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Items.SingleOrDefault(item => item.Scope == scope &&
                string.Equals(item.ShortcutPath, shortcutPath,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class SourceAppsFolder(params AppsFolderRegistration[] items) :
        IAppsFolderApplicationSource
    {
        public IReadOnlyList<AppsFolderRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return items;
        }
        public AppsFolderRegistration? ReadExact(
            string aumid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return items.SingleOrDefault(item => string.Equals(
                item.Aumid, aumid, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class SourceSteam(params SteamRegistration[] items) :
        ISteamApplicationSource
    {
        internal bool Fail { get; set; }
        public IReadOnlyList<SteamRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new IOException("Simulated Steam failure.");
            return items;
        }
        public SteamRegistration? ReadExact(
            string steamAppId, string manifestPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return items.SingleOrDefault(item => item.SteamAppId == steamAppId &&
                string.Equals(item.ManifestPath, manifestPath,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class SequencedAppsFolderSource : IAppsFolderApplicationSource
    {
        private int _calls;
        internal bool Fail { get; set; }
        internal TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseFirst { get; } = new();
        public IReadOnlyList<AppsFolderRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            if (Fail)
                throw new AppsFolderEnumerationException("Simulated AppsFolder failure.");
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                FirstStarted.TrySetResult();
                ReleaseFirst.Wait();
                return [new AppsFolderRegistration(
                    "old", "Old", "Contoso.Old_abcd!App", "old")];
            }
            return [new AppsFolderRegistration(
                "new", "New", "Contoso.New_abcd!App", "new")];
        }
        public AppsFolderRegistration? ReadExact(
            string aumid, CancellationToken cancellationToken) => null;
    }

    private sealed class CancelableStartMenuSource : IStartMenuApplicationSource
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<StartMenuRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }
        public StartMenuRegistration? ReadExact(
            string shortcutPath, StartMenuScope scope, CancellationToken cancellationToken) =>
            null;
    }

    private sealed class SourceShellLauncher : IWindowsShellLauncher
    {
        internal List<string> Paths { get; } = [];
        public void Launch(string exactShortcutPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(exactShortcutPath);
        }
    }

    private sealed class SourcePackagedLauncher : IWindowsPackagedAppLauncher
    {
        internal List<string> Aumids { get; } = [];
        public void Launch(string exactAumid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Aumids.Add(exactAumid);
        }
    }

    private sealed class SourceSteamLauncher : IWindowsSteamLauncher
    {
        internal List<string> AppIds { get; } = [];
        public void Launch(string exactSteamAppId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppIds.Add(exactSteamAppId);
        }
    }

    private sealed class SourceIcon : IWindowsAppIconSource
    {
        public string? TryRasterizePngBase64(
            string shortcutPath, CancellationToken cancellationToken) => null;
    }

    private sealed class ImmediateStaExecutor : IShellStaExecutor
    {
        internal static ImmediateStaExecutor Instance { get; } = new();
        public Task<T> RunAsync<T>(
            Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }
}
