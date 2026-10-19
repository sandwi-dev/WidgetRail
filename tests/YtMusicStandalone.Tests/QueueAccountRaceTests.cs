using System.Text.Json;
using WidgetRail.Samples.YtMusicWidget.Standalone;

internal static class QueueAccountRaceTests
{
    internal static async Task Run()
    {
        foreach (var kind in new[] { "playlist", "album", "radio" })
        foreach (var reconnect in new[] { false, true })
        {
            var process = new Process();
            await using var service = new MusicService(AppContext.BaseDirectory, .5, (_, _) => process,
                _ => Task.FromResult<IMusicProcess>(process));
            await service.InitializeAsync(default);
            var item = new MusicItem("collection", kind == "radio" ? "song" : kind, "Old collection");
            Task pending = kind == "radio" ? service.RadioAsync(item, default) : service.PlayNextAsync(item, default);
            if (process.BrowseCount != 1) throw new Exception("Expected one pending provider request");
            await service.DisconnectAsync(default);
            if (reconnect) await service.SignInAsync(default);
            var before = service.State;
            process.Pending.SetResult(JsonSerializer.SerializeToElement(new MusicPage("Old account", [new("old-song", "song", "Old song")])));
            try { await pending; throw new Exception("Old-account queue request was not cancelled"); }
            catch (OperationCanceledException) { }
            if (!ReferenceEquals(before.Queue, service.State.Queue) || service.State.Queue.Count != 0 || process.Resolves != 0)
                throw new Exception("Old-account results mutated or started the new queue");
        }
        foreach (var retire in new[] { "none", "cancel", "dispose" })
        {
            var process = new Process();
            var service = new MusicService(AppContext.BaseDirectory, .5, (_, _) => process,
                _ => Task.FromResult<IMusicProcess>(process));
            using var cancellation = new CancellationTokenSource();
            try
            {
                var pending = service.PlayNextAsync(new("playlist", "playlist", "Collection"), cancellation.Token);
                if (retire == "cancel") cancellation.Cancel();
                if (retire == "dispose") await service.DisposeAsync();
                process.Pending.SetResult(JsonSerializer.SerializeToElement(new MusicPage("Collection",
                    [new("first", "song", "First"), new("second", "song", "Second")])));
                if (retire == "none")
                {
                    var added = await pending;
                    if (added.AddedCount != 2 || !service.State.Queue.Select(item => item.Id).SequenceEqual(new[] { "first", "second" }) || process.Resolves != 1)
                        throw new Exception("Current-account Play next lost source order or normal playback");
                }
                else
                {
                    try { await pending; throw new Exception("Retired queue request was not cancelled"); }
                    catch (OperationCanceledException) { }
                    if (service.State.Queue.Count != 0 || process.Resolves != 0)
                        throw new Exception("Cancelled or disposed request changed the queue");
                }
            }
            finally { if (retire != "dispose") await service.DisposeAsync(); }
        }
    }

    private sealed class Process : IMusicProcess
    {
        public event Action<JsonElement>? Event { add { } remove { } }
        public TaskCompletionSource<JsonElement> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BrowseCount, Resolves;
        public Task<JsonElement> CallAsync(string method, object? args, CancellationToken token)
        {
            if (method == "browse") { BrowseCount++; return Pending.Task; } // Deliberately ignores cancellation.
            if (method == "resolve") Resolves++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new { connected = method != "disconnect", accountGeneration = method == "signin" ? 2 : 1, url = "http://127.0.0.1/fixture" }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

