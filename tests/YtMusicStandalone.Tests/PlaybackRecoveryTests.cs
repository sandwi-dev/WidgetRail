using System.Text.Json;
using WidgetRail.Samples.YtMusicWidget.Standalone;

internal static class PlaybackRecoveryTests
{
    internal static async Task Run()
    {
        var backend = new FakeProcess();
        var player = new FakeProcess();
        await using var service = new MusicService(AppContext.BaseDirectory, 0,
            (path, _) => path.EndsWith("python.exe", StringComparison.Ordinal) ? backend : player);
        var tracks = Enumerable.Range(0, 6).Select(i => new MusicItem($"{i:00000000000}", "song", "Song " + i)).ToArray();
        await service.PlayAsync(tracks, 0, default);
        var first = player.Generation;
        player.Fail(first);
        await Until(() => player.Generation > first);
        Check(backend.Refreshes == 1 && service.State.Index == 0, "first failure refreshes the same song once");
        player.Fail(first); // Duplicate promise rejection / old-element error.
        Check(backend.Refreshes == 1 && service.State.Index == 0, "old failure cannot skip or retry the new load");
        player.Fail(player.Generation);
        await Until(() => service.State.Index == 1);
        Check(service.State.PlaybackNotice?.Contains("Skipped") == true && service.State.Player.Error is null,
            "second failure skips to the next occurrence with a status notice");
        await service.CommandAsync("repeat", null, default);
        await service.CommandAsync("repeat", null, default);
        Check(service.State.Repeat == "one", "repeat-one fixture");
        for (var i = 0; i < 4; i++) player.Fail(player.Generation);
        Check(service.State.Index == 2 && service.State.PlaybackNotice!.Contains("stopped"),
            "three failed songs stop without wrapping, repeating or draining the queue");
        var stopped = player.Generation;
        player.Emit(new { @event = "state", generation = stopped, state = new PlayerState(tracks[2].Id, Playing: true, Duration: 200) });
        Check(!service.State.Player.Playing, "late state cannot undo stopped recovery");
        await service.CommandAsync("play", null, default);
        Check(player.Generation > stopped && service.State.PlaybackNotice is null, "explicit Play starts a fresh attempt");
        player.Fail(player.Generation, "attention");
        Check(service.State.Index == 2 && service.State.PlaybackNotice!.Contains("attention"), "permission failure does not skip songs");
        var attentionGeneration = player.Generation;
        player.Emit(new { @event = "command", generation = attentionGeneration, command = "play" });
        await Until(() => player.Generation > attentionGeneration);
        Check(service.State.PlaybackNotice is null, "Windows media Play retries after stopped recovery");

        await service.PlayAsync(tracks, 5, default);
        player.Fail(player.Generation); player.Fail(player.Generation);
        Check(service.State.Index == 5 && service.State.PlaybackNotice!.Contains("stopped"), "last unavailable track never wraps");

        await service.PlayAsync(tracks, 0, default);
        backend.BlockRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Fail(player.Generation);
        await service.SelectQueueItemAsync(service.State.Queue, 4, default);
        var selected = player.Generation;
        backend.BlockRefresh.SetResult(JsonSerializer.SerializeToElement(new { url = "http://127.0.0.1/old" }));
        await Task.Delay(30);
        Check(service.State.Index == 4 && player.Generation == selected, "a late refreshed stream cannot replace manual selection");

        backend.BlockRefresh = null;
        backend.FailRefresh = true;
        player.Fail(player.Generation);
        await Until(() => service.State.Index == 5);
        Check(service.State.Player.Error is null && service.State.PlaybackNotice!.Contains("Skipped"),
            "resolver failure during recovery also skips safely");

        backend.FailRefresh = false;
        await service.PlayAsync(tracks, 0, default);
        backend.BlockRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Fail(player.Generation);
        var pausedGeneration = player.Generation;
        player.Emit(new { @event = "command", generation = pausedGeneration, command = "pause" });
        backend.BlockRefresh.SetResult(JsonSerializer.SerializeToElement(new { url = "http://127.0.0.1/late" }));
        await Task.Delay(30);
        Check(player.Generation == pausedGeneration && !service.State.Player.Buffering,
            "pausing during recovery prevents a late stream from starting playback");
        backend.BlockRefresh = null;
        player.Emit(new { @event = "command", generation = pausedGeneration, command = "play" });
        await Until(() => player.Generation > pausedGeneration);

        backend.UnavailableId = tracks[1].Id;
        await service.CommandAsync("repeat", null, default);
        Check(service.State.Repeat == "off", "automatic-next fixture has repeat disabled");
        await service.PlayAsync(tracks, 0, default);
        player.Emit(new { @event = "ended", generation = player.Generation });
        await Until(() => service.State.Index == 2);
        Check(service.State.Player.Error is null && service.State.PlaybackNotice!.Contains("Skipped"),
            "ordinary resolution failure on automatic next uses bounded retry and skip");
        backend.UnavailableId = null;
        backend.HelperFailed = true;
        try { await service.PlayAsync(tracks, 0, default); throw new Exception("Helper failure was swallowed"); }
        catch (IOException) { }
        Check(service.State.Index == 0 && service.State.Player.Error is not null,
            "helper infrastructure failure does not masquerade as unavailable content or drain the queue");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(3000);
        while (!ready()) await Task.Delay(5, timeout.Token);
    }

    private sealed class FakeProcess : IMusicProcess
    {
        public event Action<JsonElement>? Event;
        internal long Generation;
        internal int Refreshes;
        internal bool FailRefresh;
        internal string? UnavailableId;
        internal bool HelperFailed;
        internal TaskCompletionSource<JsonElement>? BlockRefresh;
        internal void Emit(object value) => Event?.Invoke(JsonSerializer.SerializeToElement(value, JsonProcess.Json));
        internal void Fail(long generation, string reason = "unsupported") => Emit(new { @event = "playback-failed", generation, reason });
        public Task<JsonElement> CallAsync(string method, object? args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var value = JsonSerializer.SerializeToElement(args, JsonProcess.Json);
            if (method == "load") Generation = value.GetProperty("generation").GetInt64();
            if (method == "resolve" && HelperFailed) throw new IOException("Fixture helper unavailable");
            if (method == "resolve" && value.GetProperty("videoId").GetString() == UnavailableId) throw new MusicStreamUnavailableException();
            if (method == "resolve" && value.GetProperty("refresh").GetBoolean())
            {
                ++Refreshes;
                if (FailRefresh) throw new MusicStreamUnavailableException();
                if (BlockRefresh is not null) return BlockRefresh.Task;
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(new { connected = true, url = "http://127.0.0.1/fixture" }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
