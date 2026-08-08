using GameBarAlternative.PlatformSettings;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Configuration is isolated by package and publisher", IdentityIsolation),
    ("Configuration updates and removals are durable", DurableMutation),
    ("Unsafe identities keys and values are rejected", Validation),
    ("Malformed and oversized documents fail closed", InvalidDocuments),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task IdentityIsolation()
{
    using var temp = new TemporaryDirectory();
    var store = new WidgetConfigurationStore(new PlatformSettingsPaths(temp.Path));
    await store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "client-id", "alpha");
    await store.SetAsync("org.gbar.samples.spotify", "org.another.publisher", "client-id", "beta");

    var first = await store.ReadAsync("org.gbar.samples.spotify", "org.gbar.samples");
    var second = await store.ReadAsync("org.gbar.samples.spotify", "org.another.publisher");
    Assert.Equal("alpha", first.Values["client-id"]);
    Assert.Equal("beta", second.Values["client-id"]);
    Assert.Equal(2, Directory.EnumerateFiles(
        Path.Combine(temp.Path, "widget-config"), "*.json").Count());
}

static async Task DurableMutation()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    var store = new WidgetConfigurationStore(paths);
    await store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "client-id", "first");
    await store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "client-id", "second");
    await store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "region", "US");

    var reloaded = await new WidgetConfigurationStore(paths)
        .ReadAsync("org.gbar.samples.spotify", "org.gbar.samples");
    Assert.Equal("second", reloaded.Values["client-id"]);
    Assert.Equal("US", reloaded.Values["region"]);

    var removed = await store.RemoveAsync(
        "org.gbar.samples.spotify", "org.gbar.samples", "region");
    Assert.True(!removed.Values.ContainsKey("region"), "Removed value was retained.");
    var cleared = await store.ClearAsync("org.gbar.samples.spotify", "org.gbar.samples");
    Assert.Equal(0, cleared.Values.Count);
}

static async Task Validation()
{
    using var temp = new TemporaryDirectory();
    var store = new WidgetConfigurationStore(new PlatformSettingsPaths(temp.Path));
    await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        store.SetAsync("../spotify", "org.gbar.samples", "client-id", "value"));
    await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "../secret", "value"));
    await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "client-id", "bad\rvalue"));
    await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "client-id",
            new string('x', WidgetConfigurationStore.MaximumValueCharacters + 1)));
}

static async Task InvalidDocuments()
{
    using var temp = new TemporaryDirectory();
    var paths = new PlatformSettingsPaths(temp.Path);
    var store = new WidgetConfigurationStore(paths);
    await store.SetAsync("org.gbar.samples.spotify", "org.gbar.samples", "client-id", "valid");
    var file = Directory.EnumerateFiles(paths.WidgetConfigurationDirectory, "*.json").Single();
    await File.WriteAllTextAsync(file, "{\"schemaVersion\":1,\"schemaVersion\":1}");
    await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        store.ReadAsync("org.gbar.samples.spotify", "org.gbar.samples"));

    await File.WriteAllBytesAsync(file, new byte[WidgetConfigurationStore.MaximumDocumentBytes + 1]);
    var oversized = await Assert.ThrowsAsync<PlatformSettingsException>(() =>
        store.ReadAsync("org.gbar.samples.spotify", "org.gbar.samples"));
    Assert.Equal("widget_configuration_too_large", oversized.Code);
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "gbar-widget-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
