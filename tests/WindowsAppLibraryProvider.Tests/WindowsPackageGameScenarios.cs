using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsAppLibraryProvider;

internal static class WindowsPackageGameScenarios
{
    internal static Task AdmitsOnlyExactGameEvidence()
    {
        using var fixture = new PackageFixture();
        var game = fixture.Add("Game.Package_abcd", "Game.Package_1.0.0.0_x64__abcd",
            [("Game.Package_abcd!Game", "Installed Game")], ["Game"]);
        _ = fixture.Add("App.Package_abcd", "App.Package_1.0.0.0_x64__abcd",
            [("App.Package_abcd!App", "Ordinary Application")], executableIds: null);
        _ = fixture.Add("Mismatch.Package_abcd",
            "Mismatch.Package_1.0.0.0_x64__abcd",
            [("Mismatch.Package_abcd!App", "Mismatched Application")], ["Game"]);
        var source = new WindowsPackageGameApplicationSource(fixture.Catalog);

        var registrations = source.Enumerate(CancellationToken.None);

        Assert.Equal(1, registrations.Count);
        Assert.Equal(game.PackageFullName, registrations[0].PackageFullName);
        Assert.Equal("Installed Game", registrations[0].DisplayName);
        Assert.Equal(WindowsAppsFolderApplicationSource.IdentityFor(
            "Game.Package_abcd!Game"), registrations[0].IdentityKey);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(registrations[0])
            .Contains(game.InstalledLocation, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    internal static async Task DeduplicatesAppsFolderAndPreservesVariants()
    {
        using var fixture = new PackageFixture();
        var package = fixture.Add("Twin.Package_abcd",
            "Twin.Package_1.0.0.0_x64__abcd",
            [("Twin.Package_abcd!Game", "Twin Game"),
             ("Twin.Package_abcd!Editor", "Twin Editor")],
            ["Game", "Editor"]);
        var packageSource = new WindowsPackageGameLibrarySource(
            new WindowsPackageGameApplicationSource(fixture.Catalog),
            new PackageLauncher(), new PackageIcon());
        var apps = new AppsFolderSource(
            AppRegistration("Twin.Package_abcd!Game", "Twin Game"),
            AppRegistration("Twin.Package_abcd!Editor", "Twin Editor"));
        var windowsSource = new WindowsInstalledGameLibrarySource(
            new EmptyStartMenu(), apps, new ShellLauncher(),
            new PackageLauncher(), new PackageIcon());
        await using var provider = new WindowsAppLibraryProvider(
            [windowsSource, packageSource], new InlineSta());

        var page = await provider.QueryAppLibraryAsync(new(
            new(InstalledOnly: true, Kind: null, SourceAttribution: null,
                AppLibrarySortOrder.DisplayName), null, null, 64, Refresh: true),
            CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.True(page.Items.All(item => item.Kind == AppLibraryKind.Game));
        Assert.True(page.Items.All(item =>
            item.SourceIdentity == WindowsPackageGameLibrarySource.StableSourceIdentity));
        Assert.Equal(2, page.Items.Select(item => item.StableProviderIdentity)
            .Distinct().Count());
        Assert.False(System.Text.Json.JsonSerializer.Serialize(page)
            .Contains(package.PackageFullName, StringComparison.Ordinal));
    }

    internal static Task CatalogChurnRetainsOnlyDisplayOnFailure()
    {
        using var fixture = new PackageFixture();
        var first = fixture.Add("Churn.Package_abcd",
            "Churn.Package_1.0.0.0_x64__abcd",
            [("Churn.Package_abcd!Game", "Game One")], ["Game"]);
        var adapter = new WindowsPackageGameApplicationSource(fixture.Catalog);
        using var source = new WindowsPackageGameLibrarySource(
            adapter, new PackageLauncher(), new PackageIcon());

        var initial = source.Refresh(CancellationToken.None);
        Assert.Equal("Game One", initial.Items.Single().DisplayName);
        fixture.Catalog.Items.Clear();
        var removed = source.Refresh(CancellationToken.None);
        Assert.Equal(0, removed.Items.Count);

        var updated = fixture.Add("Churn.Package_abcd",
            "Churn.Package_2.0.0.0_x64__abcd",
            [("Churn.Package_abcd!Game", "Game Two")], ["Game"]);
        var changed = source.Refresh(CancellationToken.None);
        Assert.Equal("Game Two", changed.Items.Single().DisplayName);
        Assert.False(string.Equals(initial.Items.Single().ArtworkRevision,
            changed.Items.Single().ArtworkRevision, StringComparison.Ordinal));

        fixture.Catalog.Failure = new IOException("fixture failure");
        var failed = source.Refresh(CancellationToken.None);
        Assert.Equal(GameLibrarySourceHealth.Unavailable, failed.Health);
        Assert.Equal("Game Two", failed.Items.Single().DisplayName);
        Assert.True(source.ResolveExact(
            failed.Items.Single(), CancellationToken.None) is null);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(failed)
            .Contains(updated.InstalledLocation, StringComparison.OrdinalIgnoreCase));
        _ = first;
        return Task.CompletedTask;
    }

    internal static Task LaunchRevalidatesCurrentRegistration()
    {
        using var fixture = new PackageFixture();
        var package = fixture.Add("Launch.Package_abcd",
            "Launch.Package_1.0.0.0_x64__abcd",
            [("Launch.Package_abcd!Game", "Launch Game")], ["Game"]);
        var launcher = new PackageLauncher();
        using var source = new WindowsPackageGameLibrarySource(
            new WindowsPackageGameApplicationSource(fixture.Catalog),
            launcher, new PackageIcon());
        var item = source.Refresh(CancellationToken.None).Items.Single();
        var exact = source.ResolveExact(item, CancellationToken.None);
        Assert.True(exact is not null);
        source.Launch(exact!, CancellationToken.None);
        Assert.Equal("Launch.Package_abcd!Game", launcher.Aumids.Single());

        fixture.Catalog.Items.Clear();
        _ = fixture.Add("Launch.Package_abcd",
            "Launch.Package_2.0.0.0_x64__abcd",
            [("Launch.Package_abcd!Game", "Launch Game")], ["Game"]);
        Assert.True(source.ResolveExact(item, CancellationToken.None) is null);
        Assert.Equal(1, launcher.Aumids.Count);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(item)
            .Contains(package.PackageFullName, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    internal static Task EnumerationIsBoundedAndCancelable()
    {
        using var fixture = new PackageFixture();
        var source = new WindowsPackageGameApplicationSource(fixture.Catalog);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            source.Enumerate(canceled.Token));

        fixture.Catalog.OverrideCount =
            WindowsPackageGameApplicationSource.MaximumPackages + 1;
        Assert.Throws<WindowsPackageEnumerationException>(() =>
            source.Enumerate(CancellationToken.None));
        return Task.CompletedTask;
    }

    private static AppsFolderRegistration AppRegistration(
        string aumid, string displayName) => new(
        WindowsAppsFolderApplicationSource.IdentityFor(aumid), displayName, aumid,
        "apps-" + aumid);

    private sealed class PackageFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "gba-windows-package-games", Guid.NewGuid().ToString("N"));

        internal PackageFixture() => Directory.CreateDirectory(_root);
        internal PackageCatalog Catalog { get; } = new();

        internal WindowsPackageRegistrationCandidate Add(
            string family,
            string fullName,
            IReadOnlyList<(string Aumid, string DisplayName)> applications,
            IReadOnlyList<string>? executableIds)
        {
            var location = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(location);
            if (executableIds is not null)
            {
                var executables = string.Concat(executableIds.Select(id =>
                    $"<Executable Name=\"{id}.exe\" Id=\"{id}\" />"));
                File.WriteAllText(Path.Combine(location, "MicrosoftGame.config"),
                    $"<Game><ExecutableList>{executables}</ExecutableList></Game>");
            }
            var candidate = new WindowsPackageRegistrationCandidate(
                family, fullName, location,
                applications.Select(item => new WindowsPackageApplicationRegistration(
                    item.Aumid, item.DisplayName)).ToArray());
            Catalog.Items.Add(candidate);
            return candidate;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class PackageCatalog : IWindowsPackageRegistrationCatalog
    {
        internal List<WindowsPackageRegistrationCandidate> Items { get; } = [];
        internal Exception? Failure { get; set; }
        internal int? OverrideCount { get; set; }

        public IReadOnlyList<WindowsPackageRegistrationCandidate> Enumerate(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            if (OverrideCount is { } count)
                return Enumerable.Repeat(new WindowsPackageRegistrationCandidate(
                    "family", "full", @"C:\Package", []), count).ToArray();
            return Items.ToArray();
        }

        public WindowsPackageRegistrationCandidate? ReadExact(
            string packageFullName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            return Items.SingleOrDefault(item => string.Equals(
                item.PackageFullName, packageFullName, StringComparison.Ordinal));
        }
    }

    private sealed class EmptyStartMenu : IStartMenuApplicationSource
    {
        public IReadOnlyList<StartMenuRegistration> Enumerate(
            CancellationToken cancellationToken) => [];
        public StartMenuRegistration? ReadExact(
            string shortcutPath, StartMenuScope scope,
            CancellationToken cancellationToken) => null;
    }

    private sealed class AppsFolderSource(params AppsFolderRegistration[] items) :
        IAppsFolderApplicationSource
    {
        public IReadOnlyList<AppsFolderRegistration> Enumerate(
            CancellationToken cancellationToken) => items;
        public AppsFolderRegistration? ReadExact(
            string aumid, CancellationToken cancellationToken) => items.SingleOrDefault(
            item => string.Equals(item.Aumid, aumid, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PackageLauncher : IWindowsPackagedAppLauncher
    {
        internal List<string> Aumids { get; } = [];
        public void Launch(string exactAumid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Aumids.Add(exactAumid);
        }
    }

    private sealed class ShellLauncher : IWindowsShellLauncher
    {
        public void Launch(string shortcutPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected shortcut launch.");
    }

    private sealed class PackageIcon : IWindowsAppIconSource
    {
        public string? TryRasterizePngBase64(
            string shortcutPath, CancellationToken cancellationToken) => null;
        public string? TryRasterizeAppsFolderPngBase64(
            string aumid, CancellationToken cancellationToken) => null;
    }

    private sealed class InlineSta : IShellStaExecutor
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }
}
