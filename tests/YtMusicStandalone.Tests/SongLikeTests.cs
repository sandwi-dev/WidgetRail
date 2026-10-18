using System.Text.Json;
using WidgetRail.Samples.YtMusicWidget.Standalone;

internal static class SongLikeTests
{
    internal static async Task Run()
    {
        var process = new Process();
        await using var service = new MusicService(AppContext.BaseDirectory, .5, (_, _) => process,
            _ => Task.FromResult<IMusicProcess>(process));
        var item = new MusicItem("M7lc1UVf-VE", "song", "Song");
        await service.InitializeAsync(default);
        await service.ReadSongLikeAsync(item, default);
        await service.ReadSongLikeAsync(item, default);
        Check(process.Ratings == 1 && service.State.LikeFor(item) == false, "Known rating cached without another read");
        process.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = service.SetSongLikeAsync(item, true, default);
        await service.SetSongLikeAsync(item, true, default);
        Check(process.Ratings == 2 && service.State.PendingLikes.Contains(item.Id) && service.State.LikeFor(item) == false,
            "Pending write is coalesced and not optimistically acknowledged");
        await service.CommandAsync("volume", .25, default);
        Check(service.State.Player.Volume == .25, "Account write blocks playback controls");
        process.Pending.SetResult(JsonSerializer.SerializeToElement(new { liked = true }));
        await write;
        Check(service.State.LikeFor(item) == true && service.State.PendingLikes.Count == 0, "Acknowledged write did not publish state");
        Check(process.Account == 7, "Typed adapter did not bind request to account generation");

        process.Pending = null; process.BadResponse = true;
        try { await service.SetSongLikeAsync(item, false, default); throw new Exception("Expected invalid acknowledgement"); }
        catch (IOException) { }
        Check(service.State.LikeFor(item) == true && service.State.PendingLikes.Count == 0, "Failure corrupts acknowledged state or remains busy");
        process.BadResponse = false;
        process.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        write = service.SetSongLikeAsync(item, false, default);
        await service.DisconnectAsync(default);
        process.Pending.SetResult(JsonSerializer.SerializeToElement(new { liked = false }));
        await write;
        Check(!service.State.Connected && service.State.SongLikes.Count == 0 && service.State.PendingLikes.Count == 0,
            "Late old-account write enters disconnected state");

        process.Pending = null;
        await service.SignInAsync(default);
        await service.SetSongLikeAsync(item, true, default);
        Check(process.Account == 8 && service.State.LikeFor(item) == true, "Reconnected account uses fresh identity");
        var other = new MusicItem("different11", "song", "Other", Liked: true);
        process.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = new CancellationTokenSource();
        var read = service.ReadSongLikeAsync(other, canceled.Token);
        canceled.Cancel();
        process.Pending.SetResult(JsonSerializer.SerializeToElement(new { liked = true }));
        try { await read; throw new Exception("Expected canceled read"); } catch (OperationCanceledException) { }
        Check(service.State.LikeFor(other) is null, "Canceled read or stale item metadata supplies rating");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Process : IMusicProcess
    {
        public event Action<JsonElement>? Event { add { } remove { } }
        public int Ratings;
        public long Account;
        public bool BadResponse;
        public TaskCompletionSource<JsonElement>? Pending;
        public Task<JsonElement> CallAsync(string method, object? args, CancellationToken token)
        {
            if (method == "song_like")
            {
                Ratings++;
                var value = JsonSerializer.SerializeToElement(args);
                Account = value.GetProperty("accountGeneration").GetInt64();
                if (Pending is not null) return Pending.Task; // Deliberately ignores cancellation.
                if (BadResponse) return Task.FromResult(JsonSerializer.SerializeToElement(new { acknowledged = false }));
                return Task.FromResult(JsonSerializer.SerializeToElement(new { liked = value.TryGetProperty("liked", out var liked) && liked.GetBoolean() }));
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(new { connected = true, accountGeneration = method == "signin" ? 8 : 7 }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
