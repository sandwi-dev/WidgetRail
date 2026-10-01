using System.Text.Json;
using WidgetRail.Samples.YtMusicWidget.Standalone;

internal static class VolumePreferenceTests
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-volume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "volume.txt");
        try
        {
            var player = new FakeProcess();
            await using (var service = Create(path, player))
            {
                Check(service.State.Player.Volume == .5, "Fresh profile uses default volume");
                for (var i = 0; i <= 20; i++) await service.CommandAsync("volume", i / 20.0, default);
                await service.CommandAsync("volume", .25, default);
                await service.CommandAsync("volume", double.NaN, default);
                await service.PlayAsync([new("track", "song", "Fixture")], 0, default);
                Check(player.LoadVolume == .25, "First load must use the latest selected volume");
                player.Emit(new { @event = "state", generation = player.Generation,
                    state = new PlayerState("track", Volume: .5) });
                Check(service.State.Player.Volume == .25, "Provider startup defaults cannot replace user volume");
                await service.DisconnectAsync(default);
                Check(service.State.Player.Volume == .25, "Disconnect preserves volume preference");
            }
            await using (var restarted = Create(path, new()))
                Check(restarted.State.Player.Volume == .25, "Latest selected volume survives restart");
            Check(Directory.GetFiles(root).Length == 1, "Atomic saves leave no temporary files");
            foreach (var invalid in new[] { "bad", "NaN", "Infinity", "1.5", "-1", new string('1', 100) })
            {
                await File.WriteAllTextAsync(path, invalid);
                await using var service = Create(path, new());
                Check(service.State.Player.Volume == .5, "Invalid preferences safely use default volume");
            }
            foreach (var edge in new[] { 0.0, 1.0 })
            {
                await using (var service = Create(path, new())) await service.CommandAsync("volume", edge, default);
                await using var restarted = Create(path, new());
                Check(restarted.State.Player.Volume == edge, "Mute and maximum volume survive restart");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static MusicService Create(string path, FakeProcess player) =>
        new(AppContext.BaseDirectory, .5, (executable, _) => executable.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase)
            ? new FakeProcess() : player, path);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeProcess : IMusicProcess
    {
        public event Action<JsonElement>? Event;
        internal long Generation;
        internal double LoadVolume;
        internal void Emit(object value) => Event?.Invoke(JsonSerializer.SerializeToElement(value, JsonProcess.Json));
        public Task<JsonElement> CallAsync(string method, object? arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (method == "load")
            {
                var value = JsonSerializer.SerializeToElement(arguments, JsonProcess.Json);
                Generation = value.GetProperty("generation").GetInt64();
                LoadVolume = value.GetProperty("volume").GetDouble();
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(new { connected = true, url = "http://127.0.0.1/fixture" }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
