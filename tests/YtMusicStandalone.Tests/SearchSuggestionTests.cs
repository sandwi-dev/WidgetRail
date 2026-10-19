using System.Text.Json;
using WidgetRail.Samples.YtMusicWidget.Standalone;

internal static class SearchSuggestionTests
{
    internal static async Task Run()
    {
        var process = new Process();
        await using var service = new MusicService(AppContext.BaseDirectory, .5, (_, _) => process,
            _ => Task.FromResult<IMusicProcess>(process));
        await service.InitializeAsync(default);
        var result = await service.SearchSuggestionsAsync("fade", default);
        Check(result.SequenceEqual(new[] { "Faded", "Faded artist" }) && process.Account == 7,
            "Provider adapter lost order, sanitation or account identity");
        process.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = service.SearchSuggestionsAsync("fade", default);
        await service.DisconnectAsync(default);
        process.Pending.SetResult(JsonSerializer.SerializeToElement(new[] { "Old account result" }));
        Check((await request).Count == 0, "Old-account suggestions escaped");
        process.Pending = null;
        result = await service.SearchSuggestionsAsync("fade", default);
        Check(result.Count == 2 && process.Account == 8, "Signed-out search did not use the new public session");
        process.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        request = service.SearchSuggestionsAsync("fade", cancellation.Token);
        cancellation.Cancel();
        try { await request; throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        // A provider timeout returns in time for the widget's local fallback.
        Check((await service.SearchSuggestionsAsync("fade", default)).Count == 0, "Timed-out provider did not permit local fallback");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Process : IMusicProcess
    {
        public event Action<JsonElement>? Event { add { } remove { } }
        public long Account;
        public TaskCompletionSource<JsonElement>? Pending;
        public Task<JsonElement> CallAsync(string method, object? args, CancellationToken token)
        {
            if (method == "search_suggestions")
            {
                Account = JsonSerializer.SerializeToElement(args).GetProperty("accountGeneration").GetInt64();
                return Pending?.Task.WaitAsync(token) ?? Task.FromResult(JsonSerializer.SerializeToElement(new object[] { "Faded", "Faded", 3, "bad\nvalue", "Faded artist" }));
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(new { connected = method != "disconnect", accountGeneration = method == "disconnect" ? 8 : 7 }));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
