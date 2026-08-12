using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsAppLibraryProvider;

internal static class GogInstalledGameScenarios
{
    internal static Task DisabledClientMissingEmptyAndCancellationAreExplicit()
    {
        using var fixture = new GogFixture();
        fixture.Add("100", "Alpha");
        var disabledReads = 0;
        var disabled = new GogInstalledGameApplicationSource(
            new FakeRegistry(fixture.Records, () => disabledReads++),
            _ => false, fixture.Client);
        var disabledResult = disabled.Enumerate(default);
        Equal(GameLibrarySourceHealth.Disabled, disabledResult.Health,
            "Disabled GOG source did not report its explicit state.");
        Equal(0, disabledReads, "Disabled GOG source opened its registration surface.");

        var missingClient = new GogInstalledGameApplicationSource(
            new FakeRegistry(fixture.Records), _ => true,
            Path.Combine(fixture.Root, "missing-client.exe")).Enumerate(default);
        Equal(GameLibrarySourceHealth.Unavailable, missingClient.Health,
            "Missing GOG Galaxy client was not isolated as unavailable.");

        var empty = new GogInstalledGameApplicationSource(
            new FakeRegistry([]), _ => true, fixture.Client).Enumerate(default);
        Equal(GameLibrarySourceHealth.Healthy, empty.Health,
            "Empty current GOG registration was not healthy.");
        Equal(0, empty.Registrations.Count, "Empty GOG registration emitted a row.");

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Throws<OperationCanceledException>(() => disabled.Enumerate(canceled.Token),
            "Canceled GOG discovery did not propagate cancellation.");
        return Task.CompletedTask;
    }

    internal static Task ValidRegistrationsAreOpaqueDistinctAndLaunchable()
    {
        using var fixture = new GogFixture();
        fixture.Add("100", "Same title", WindowsGogRegistryReader.Registry32);
        fixture.Add("200", "Same title", WindowsGogRegistryReader.Registry64);
        var source = new GogInstalledGameApplicationSource(
            new FakeRegistry(fixture.Records), _ => true, fixture.Client);
        var candidate = source.Enumerate(default);
        Equal(GameLibrarySourceHealth.Healthy, candidate.Health,
            "Valid GOG registrations were not healthy.");
        Equal(2, candidate.Registrations.Count,
            "Distinct same-title GOG registrations collapsed.");
        True(candidate.Registrations.Select(item => item.IdentityKey).Distinct().Count() == 2,
            "Distinct GOG product IDs did not receive distinct identities.");
        var serialized = JsonSerializer.Serialize(candidate.Registrations.Select(
            item => item.IdentityKey));
        True(!serialized.Contains(fixture.Root, StringComparison.OrdinalIgnoreCase) &&
            !serialized.Contains("100", StringComparison.Ordinal),
            "Projected GOG identity exposed trusted registration evidence.");

        var start = WindowsGogLauncher.CreateStartInfo(
            fixture.Client, "100", fixture.Install("100"));
        Equal(fixture.Client, start.FileName,
            "GOG launch did not target the fixed Galaxy client.");
        Equal("/command=runGame", start.ArgumentList[0],
            "GOG launch command was not fixed.");
        Equal("/gameId=100", start.ArgumentList[1],
            "GOG launch omitted the exact product identity.");
        Equal($"/path={fixture.Install("100")}", start.ArgumentList[2],
            "GOG launch omitted the exact registered install root.");
        return Task.CompletedTask;
    }

    internal static Task MalformedDuplicateAndMixedRegistrationsFailClosed()
    {
        using var fixture = new GogFixture();
        fixture.Add("100", "Valid");
        fixture.Records.Add(new(WindowsGogRegistryReader.Registry32,
            "bad", "bad", "Bad", fixture.Root));
        var mixed = new GogInstalledGameApplicationSource(
            new FakeRegistry(fixture.Records), _ => true, fixture.Client)
            .Enumerate(default);
        Equal(GameLibrarySourceHealth.Degraded, mixed.Health,
            "Mixed valid and malformed GOG registrations were not degraded.");
        Equal("100", mixed.Registrations.Single().ProductId,
            "Malformed GOG registration displaced a valid independent row.");

        fixture.Records.Clear();
        fixture.Add("100", "Same", WindowsGogRegistryReader.Registry32);
        fixture.Add("100", "Same", WindowsGogRegistryReader.Registry64);
        var duplicate = new GogInstalledGameApplicationSource(
            new FakeRegistry(fixture.Records), _ => true, fixture.Client)
            .Enumerate(default);
        Equal(GameLibrarySourceHealth.Degraded, duplicate.Health,
            "Duplicate GOG product identity was not degraded.");
        Equal(0, duplicate.Registrations.Count,
            "Ambiguous duplicate GOG identity partially published.");

        fixture.Records.Clear();
        fixture.Add("300", "Changed");
        var changed = new GogInstalledGameApplicationSource(
            new FakeRegistry(fixture.Records), _ => true, fixture.Client,
            path => File.AppendAllText(path, " ")).Enumerate(default);
        Equal(GameLibrarySourceHealth.Degraded, changed.Health,
            "Changed-during-read GOG info was not rejected.");
        Equal(0, changed.Registrations.Count,
            "Changed-during-read GOG info published launch authority.");
        return Task.CompletedTask;
    }

    internal static async Task RefreshAndLaunchAreGenerationExact()
    {
        using var fixture = new GogFixture();
        fixture.Add("100", "Alpha");
        var registry = new FakeRegistry(fixture.Records);
        var adapter = new GogInstalledGameApplicationSource(
            registry, _ => true, fixture.Client);
        var launcher = new RecordingGogLauncher();
        await using var provider = new WindowsAppLibraryProvider(
            [new GogGameLibrarySource(adapter, launcher)], ImmediateSta.Instance);
        var first = await Query(provider, refresh: true);
        var alpha = first.Items.Single();
        Equal("GOG", alpha.SourceAttribution,
            "GOG attribution was not normalized.");

        fixture.Records.Clear();
        await ThrowsBroker(() => provider.LaunchAppLibraryItemAsync(
                alpha.ProviderAppId, default),
            "Removed GOG registration still authorized launch.");
        Equal(0, launcher.Count, "Stale GOG authority reached the launcher.");

        fixture.Add("200", "Beta");
        var second = await Query(provider, refresh: true);
        var beta = second.Items.Single();
        Equal("Beta", beta.DisplayName,
            "Refreshed GOG registration was not published.");
        await provider.LaunchAppLibraryItemAsync(beta.ProviderAppId, default);
        Equal(1, launcher.Count, "Current exact GOG registration did not launch.");

        fixture.Rewrite("200", "Beta replacement");
        await ThrowsBroker(() => provider.LaunchAppLibraryItemAsync(
                beta.ProviderAppId, default),
            "Replaced GOG registration retained old launch authority.");

        registry.Available = false;
        var unavailable = await Query(provider, refresh: true);
        Equal("Beta", unavailable.Items.Single().DisplayName,
            "Unavailable GOG refresh did not retain bounded display truth.");
        Equal("source_unavailable", unavailable.Sources.Single().StatusCode,
            "Unavailable GOG status was not projected.");
        await ThrowsBroker(() => provider.LaunchAppLibraryItemAsync(
                beta.ProviderAppId, default),
            "Unavailable GOG display truth retained launch authority.");
    }

    private static async Task<AppLibraryBackendCursorPage> Query(
        WindowsAppLibraryProvider provider,
        bool refresh) => await provider.QueryAppLibraryAsync(
        new(new(Kind: AppLibraryKind.Game), null, null, 64, refresh), default);

    private static async Task ThrowsBroker(Func<Task> action, string message)
    {
        try { await action(); }
        catch (BrokerException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
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

    private sealed class FakeRegistry(
        IReadOnlyList<GogRegistryRecord> records,
        Action? onRead = null) : IGogRegistryReader
    {
        internal bool Available { get; set; } = true;

        public GogRegistrySnapshot Enumerate(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onRead?.Invoke();
            return new(Available, Available ? records.ToArray() : []);
        }

        public GogRegistryRecord? ReadExact(
            string registryView,
            string keyName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onRead?.Invoke();
            if (!Available) return null;
            return records.SingleOrDefault(record =>
                string.Equals(record.RegistryView, registryView, StringComparison.Ordinal) &&
                string.Equals(record.KeyName, keyName, StringComparison.Ordinal));
        }
    }

    private sealed class RecordingGogLauncher : IWindowsGogLauncher
    {
        internal int Count { get; private set; }
        public void Launch(string productId, string installLocation,
            CancellationToken cancellationToken) => Count++;
    }

    private sealed class ImmediateSta : IShellStaExecutor
    {
        internal static ImmediateSta Instance { get; } = new();
        public Task<T> RunAsync<T>(Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }

    private sealed class GogFixture : IDisposable
    {
        internal GogFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "gbar-gog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Client = Path.Combine(Root, "GOG Galaxy", "GalaxyClient.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(Client)!);
            File.WriteAllBytes(Client, [1, 2, 3]);
        }

        internal string Root { get; }
        internal string Client { get; }
        internal List<GogRegistryRecord> Records { get; } = [];

        internal string Install(string id) => Path.Combine(Root, "install-" + id);

        internal void Add(string id, string name,
            string view = WindowsGogRegistryReader.Registry32)
        {
            var install = Install(id);
            Directory.CreateDirectory(install);
            WriteInfo(id, name);
            Records.Add(new(view, id, id, name, install));
        }

        internal void Rewrite(string id, string name)
        {
            var index = Records.FindIndex(record => record.KeyName == id);
            var current = Records[index];
            Records[index] = current with { GameName = name };
            WriteInfo(id, name);
        }

        private void WriteInfo(string id, string name) =>
            File.WriteAllText(Path.Combine(Install(id), $"goggame-{id}.info"),
                JsonSerializer.Serialize(new
                {
                    gameId = id,
                    rootGameId = id,
                    name,
                    playTasks = Array.Empty<object>(),
                }));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
