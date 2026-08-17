using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

internal static class EpicInstalledGameScenarios
{
    internal static async Task DisabledIsExplicitAndDoesNotRead()
    {
        using var fixture = new EpicFixture();
        fixture.Write("alpha", "Alpha");
        var source = new EpicInstalledGameApplicationSource(
            fixture.Manifests, _ => false,
            _ => throw new InvalidOperationException("Disabled source performed I/O."));
        var candidate = source.Enumerate(default);
        Equal(GameLibrarySourceHealth.Disabled, candidate.Health,
            "Disabled Epic source did not report its explicit state.");
        Equal(0, candidate.Registrations.Count,
            "Disabled Epic source admitted records.");
        await using var provider = new WindowsAppLibraryProvider(
            [new EpicGameLibrarySource(source, new RecordingEpicLauncher())],
            ImmediateSta.Instance);
        var page = await provider.QueryAppLibraryAsync(
            new(new(Kind: AppLibraryKind.Game), null, null, 64, Refresh: true),
            default);
        Equal("source_disabled", page.Sources.Single().StatusCode,
            "Disabled Epic source status was not projected.");
    }

    internal static Task ValidManifestsAreOpaqueBoundedAndLaunchable()
    {
        using var fixture = new EpicFixture();
        fixture.Write("alpha", "Alpha");
        var source = new EpicInstalledGameApplicationSource(
            fixture.Manifests, _ => true);
        var candidate = source.Enumerate(default);
        Equal(GameLibrarySourceHealth.Healthy, candidate.Health,
            "Valid Epic source was not healthy.");
        Equal(1, candidate.Registrations.Count,
            "Valid Epic record was not admitted.");
        var record = candidate.Registrations[0];
        True(record.IdentityKey.StartsWith("epic-", StringComparison.Ordinal),
            "Epic identity was not host-owned.");
        True(!JsonSerializer.Serialize(record.IdentityKey).Contains(
                fixture.Root, StringComparison.OrdinalIgnoreCase),
            "Host identity exposed the install path.");
        var start = WindowsEpicLauncher.CreateStartInfo(
            record.CatalogNamespace, record.CatalogItemId, record.AppName);
        True(start.FileName.StartsWith("com.epicgames.launcher://apps/",
                StringComparison.Ordinal),
            "Epic launch did not use the fixed provider route.");
        True(!start.FileName.Contains(fixture.Root,
                StringComparison.OrdinalIgnoreCase),
            "Epic launch route exposed a manifest path.");
        return Task.CompletedTask;
    }

    internal static Task CorruptDuplicateUnsafeAndPartialFailClosed()
    {
        using var fixture = new EpicFixture();
        fixture.Write("alpha", "Alpha");
        fixture.Write("duplicate", "Duplicate", appName: "alpha");
        var duplicate = new EpicInstalledGameApplicationSource(
            fixture.Manifests, _ => true).Enumerate(default);
        Equal(GameLibrarySourceHealth.Degraded, duplicate.Health,
            "Duplicate Epic identity was not rejected.");
        Equal(0, duplicate.Registrations.Count,
            "Duplicate Epic identity partially published.");

        fixture.ClearManifests();
        fixture.WriteRaw("unknown", "{\"FormatVersion\":99}");
        var unknown = new EpicInstalledGameApplicationSource(
            fixture.Manifests, _ => true).Enumerate(default);
        Equal(GameLibrarySourceHealth.Degraded, unknown.Health,
            "Unknown Epic schema was not rejected.");

        fixture.ClearManifests();
        fixture.Write("unsafe", "Unsafe", launchExecutable: "..\\outside.exe");
        File.WriteAllBytes(Path.Combine(fixture.Root, "outside.exe"), [1]);
        var unsafePath = new EpicInstalledGameApplicationSource(
            fixture.Manifests, _ => true).Enumerate(default);
        Equal(0, unsafePath.Registrations.Count,
            "Escaping Epic executable path was admitted.");

        fixture.ClearManifests();
        var changed = fixture.Write("changed", "Changed");
        var partial = new EpicInstalledGameApplicationSource(
            fixture.Manifests, _ => true,
            path => File.AppendAllText(path, " "));
        var partialResult = partial.Enumerate(default);
        Equal(GameLibrarySourceHealth.Degraded, partialResult.Health,
            "Changed-during-read Epic manifest was not rejected.");
        Equal(0, partialResult.Registrations.Count,
            "Changed-during-read Epic manifest published authority.");
        True(File.Exists(changed), "Partial-write fixture lost its manifest.");
        return Task.CompletedTask;
    }

    internal static async Task GenerationAndExactLaunchAreCurrent()
    {
        using var fixture = new EpicFixture();
        var manifest = fixture.Write("alpha", "Alpha");
        var adapter = new EpicInstalledGameApplicationSource(
            fixture.Manifests, _ => true);
        var launcher = new RecordingEpicLauncher();
        await using var provider = new WindowsAppLibraryProvider(
            [new EpicGameLibrarySource(adapter, launcher)], ImmediateSta.Instance);
        var first = await provider.QueryAppLibraryAsync(
            new(new(Kind: AppLibraryKind.Game), null, null, 64, Refresh: true),
            default);
        Equal(1, first.Items.Count, "Epic provider omitted a valid record.");
        var selected = first.Items[0];
        File.Delete(manifest);
        await ThrowsBroker(() => provider.LaunchAppLibraryItemAsync(
                selected.ProviderAppId, default),
            "Removed Epic manifest still authorized launch.");
        Equal(0, launcher.Count, "Stale Epic authority reached the launcher.");

        fixture.Write("beta", "Beta");
        var refreshed = await provider.QueryAppLibraryAsync(
            new(new(Kind: AppLibraryKind.Game), null, null, 64, Refresh: true),
            default);
        Equal("Beta", refreshed.Items.Single().DisplayName,
            "Epic add/remove refresh did not publish the current generation.");
        var beta = refreshed.Items.Single();
        Directory.Delete(fixture.Manifests, recursive: true);
        var unavailable = await provider.QueryAppLibraryAsync(
            new(new(Kind: AppLibraryKind.Game), null, null, 64, Refresh: true),
            default);
        Equal("Beta", unavailable.Items.Single().DisplayName,
            "Unavailable Epic refresh did not retain bounded display truth.");
        Equal("source_unavailable", unavailable.Sources.Single().StatusCode,
            "Unavailable Epic source status was not projected.");
        await ThrowsBroker(() => provider.LaunchAppLibraryItemAsync(
                beta.ProviderAppId, default),
            "Unavailable last-good Epic display retained launch authority.");
    }

    private static async Task ThrowsBroker(Func<Task> action, string message)
    {
        try { await action(); }
        catch (BrokerException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected {expected}; actual {actual}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingEpicLauncher : IWindowsEpicLauncher
    {
        internal int Count { get; private set; }
        public void Launch(string catalogNamespace, string catalogItemId,
            string appName, CancellationToken cancellationToken) => Count++;
    }

    private sealed class ImmediateSta : IShellStaExecutor
    {
        internal static ImmediateSta Instance { get; } = new();
        public Task<T> RunAsync<T>(Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }

    private sealed class EpicFixture : IDisposable
    {
        internal EpicFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "wrail-epic-" + Guid.NewGuid().ToString("N"));
            Manifests = Path.Combine(Root, "manifests");
            Directory.CreateDirectory(Manifests);
        }

        internal string Root { get; }
        internal string Manifests { get; }

        internal string Write(string file, string displayName,
            string? appName = null, string launchExecutable = "game.exe")
        {
            var install = Path.Combine(Root, "install-" + file);
            Directory.CreateDirectory(install);
            if (!launchExecutable.Contains("..", StringComparison.Ordinal))
            {
                var executable = Path.Combine(install, launchExecutable);
                Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
                File.WriteAllBytes(executable, [1, 2, 3]);
            }
            var path = Path.Combine(Manifests, file + ".item");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                FormatVersion = 0,
                AppName = appName ?? file,
                DisplayName = displayName,
                CatalogNamespace = "namespace",
                CatalogItemId = "catalog-" + (appName ?? file),
                InstallLocation = install,
                LaunchExecutable = launchExecutable,
                bIsApplication = false,
            }));
            return path;
        }

        internal void WriteRaw(string file, string json) =>
            File.WriteAllText(Path.Combine(Manifests, file + ".item"), json);

        internal void ClearManifests()
        {
            foreach (var path in Directory.EnumerateFiles(Manifests)) File.Delete(path);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
