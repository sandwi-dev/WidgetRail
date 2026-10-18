using System.Text.Json;
using WidgetRail.Samples.YtMusicWidget.Standalone;

internal static class QueueRemovalTests
{
    internal static async Task Run()
    {
        var backend = new Process();
        var player = new Process { IsPlayer = true };
        await using var service = new MusicService(AppContext.BaseDirectory, .5, (_, _) => backend,
            _ => Task.FromResult<IMusicProcess>(player));
        var duplicate = new MusicItem("duplicate", "song", "Repeated song");
        await service.PlayAsync([duplicate, duplicate, new("last", "song", "Last")], 1, default);
        var initial = service.State.Queue;
        Check(!ReferenceEquals(initial[0], initial[1]), "Repeated input object has distinct queue occurrences");
        var current = service.State.Current;
        var loads = player.Loads;
        await service.RemoveQueueItemAsync(initial, 0, default);
        Check(ReferenceEquals(service.State.Current, current) && service.State.Index == 0 && player.Loads == loads,
            "Removing before current preserves exact duplicate and playback");
        await service.RemoveQueueItemAsync(initial, 1, default);
        Check(service.State.Queue.Count == 2, "Stale removal does not remove shifted occurrence");
        await service.RemoveQueueItemAsync(service.State.Queue, 1, default);
        Check(service.State.Queue.Count == 1 && player.Loads == loads, "Removing after current does not restart playback");
        await service.RemoveQueueItemAsync(service.State.Queue, 0, default);
        Check(service.State.Current is null && service.State.Index == -1 && !service.State.Player.Playing && player.Stops == 1,
            "Removing only current stops and clears queue");

        foreach (var repeat in new[] { "off", "all", "one" })
        {
            while (service.State.Repeat != repeat) await service.CommandAsync("repeat", null, default);
            await service.PlayAsync([duplicate, new("last", "song", "Last")], 1, default);
            loads = player.Loads;
            await service.RemoveQueueItemAsync(service.State.Queue, 1, default);
            Check(repeat == "all" ? service.State.Index == 0 && player.Loads == loads + 1
                : service.State.Current is null && player.Loads == loads,
                "Last removal wraps only for Repeat All: " + repeat);
        }
        await service.PlayAsync([duplicate, new("next", "song", "Next")], 0, default);
        await service.RemoveQueueItemAsync(service.State.Queue, 0, default);
        Check(service.State.Current?.Id == "next" && service.State.Player.Playing, "Current removal advances while playing even under Repeat One");
        await service.PlayAsync([duplicate, new("next", "song", "Next")], 0, default);
        player.EmitState(false);
        loads = player.Loads;
        await service.RemoveQueueItemAsync(service.State.Queue, 0, default);
        Check(service.State.Current?.Id == "next" && !service.State.Player.Playing && !service.State.Player.Buffering && player.Loads == loads,
            "Paused removal selects successor without starting it");
        await service.CommandAsync("play", null, default);
        Check(service.State.Player.Playing && player.Loads == loads + 1, "Play resolves selected paused successor");

        await service.PlayAsync([duplicate, new("next", "song", "Next")], 0, default);
        player.PendingStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var removing = service.RemoveQueueItemAsync(service.State.Queue, 0, default);
        loads = player.Loads;
        await service.CommandAsync("pause", null, default);
        player.PendingStop.SetResult(JsonSerializer.SerializeToElement(new { }));
        await removing;
        player.PendingStop = null;
        Check(player.Loads == loads && !service.State.Player.Playing, "Pause while removal awaits player stop cancels automatic successor playback");

        var tracks = Enumerable.Range(0, MusicQueue.MaximumItems).Select(i => new MusicItem("song" + i, "song", "Song " + i)).ToArray();
        await service.PlayAsync(tracks, 10, default);
        var original = service.State.Queue.ToArray();
        current = service.State.Current;
        await service.CommandAsync("shuffle", null, default);
        var removed = service.State.Queue[20];
        await service.RemoveQueueItemAsync(service.State.Queue, 20, default);
        await service.CommandAsync("shuffle", null, default);
        Check(service.State.Queue.SequenceEqual(original.Where(item => !ReferenceEquals(item, removed))) && ReferenceEquals(current, service.State.Current),
            "Removal preserves original order and current identity through shuffle at maximum capacity");
        var beforeCancel = service.State.Queue;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await service.RemoveQueueItemAsync(beforeCancel, 0, canceled.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
        }
        Check(ReferenceEquals(beforeCancel, service.State.Queue), "Canceled removal leaves queue untouched");
        await service.DisconnectAsync(default);
        await service.RemoveQueueItemAsync(beforeCancel, 0, default);
        Check(service.State.Queue.Count == 0 && !service.State.Connected, "Old account occurrence cannot affect disconnected queue");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Process : IMusicProcess
    {
        public bool IsPlayer;
        public int Loads, Stops;
        public TaskCompletionSource<JsonElement>? PendingStop;
        private long generation;
        private string track = "";
        public event Action<JsonElement>? Event;
        public void EmitState(bool playing) => Event?.Invoke(JsonSerializer.SerializeToElement(new
        { @event = "state", generation, state = new PlayerState(track, playing, Duration: 200) }));
        public Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsPlayer && method == "load")
            {
                var data = JsonSerializer.SerializeToElement(parameters);
                generation = data.GetProperty("generation").GetInt64(); track = data.GetProperty("trackId").GetString()!;
                Loads++; EmitState(true);
            }
            if (method == "stop")
            {
                Stops++;
                // Real player publishes a stopped state before acknowledging stop.
                if (parameters is not null)
                {
                    generation = JsonSerializer.SerializeToElement(parameters).GetProperty("generation").GetInt64();
                    EmitState(false);
                }
                if (PendingStop is not null) return PendingStop.Task;
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(method switch
            {
                "status" => (object)new { connected = true },
                "resolve" => new { url = "http://127.0.0.1/fixture" },
                _ => new { }
            }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
