using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteProviderRefreshResult { Completed, StillRunning, Failed, Unavailable }
internal sealed record PlayniteChange(long Sequence, string Kind, string? GameId, string? AchievementId, long ObservedAt);
internal sealed record PlayniteEventBatch(string ConnectionId, string InstanceId, bool ResetRequired, long NextSequence,
    IReadOnlyList<PlayniteChange> Changes, IReadOnlyList<string>? RunningGames = null, IReadOnlyDictionary<string, long>? RunningSince = null, IReadOnlyDictionary<string, string>? RunningNames = null,
    IReadOnlyDictionary<string, string>? RunningProcesses = null);

internal sealed partial class PlayniteBridgeClient
{
    public async ValueTask<PlayniteEventBatch?> ReadEventsAsync(long after, CancellationToken token)
    {
        if (!UsesLocalConnection) return null;
        if (after < -1) throw new ArgumentOutOfRangeException(nameof(after));
        var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.IntegrationEvents)
            { AfterSequence = after, WaitMilliseconds = after < 0 ? 0 : 25000 }, token).ConfigureAwait(false);
        // Closing the overlay cancels the client read. HttpListener can keep
        // that wait alive until its deadline. A full wait pool still permits
        // immediate reads with the same cursor, including game-stop events.
        if (after >= 0 && response.StatusCode == 429)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.IntegrationEvents)
                { AfterSequence = after, WaitMilliseconds = 0 }, token).ConfigureAwait(false);
        }
        EnsureSuccess(response);
        using var json = JsonDocument.Parse(response.Body, new() { MaxDepth = 8 });
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || response.ConnectionId is null ||
            OptionalString(root, "instanceId", 36) is not { } instance || !Guid.TryParseExact(instance, "D", out _) ||
            !TryRequiredBoolean(root, "resetRequired", out var reset) || !TryNonNegativeLong(root, "nextSequence", out var next) ||
            !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array || events.GetArrayLength() > 128)
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        var changes = new List<PlayniteChange>(); long previous = -1;
        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryNonNegativeLong(item, "sequence", out var sequence) ||
                sequence <= previous || sequence > next || OptionalString(item, "kind", 64) is not { } kind ||
                !TryNonNegativeLong(item, "observedAt", out var stamp)) throw new PlayniteBridgeDataException("invalid_playnite_data");
            var game = OptionalString(item, "gameId", 36);
            if (game is not null && !Guid.TryParseExact(game, "D", out _)) throw new PlayniteBridgeDataException("invalid_playnite_data");
            changes.Add(new(sequence, kind, game, OptionalString(item, "achievementId", 256), stamp)); previous = sequence;
        }
        List<string>? running = null;
        if (root.TryGetProperty("runningGames", out var active))
        {
            if (active.ValueKind != JsonValueKind.Array) throw new PlayniteBridgeDataException("invalid_playnite_data");
            running = [];
            foreach (var id in active.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String || !Guid.TryParseExact(id.GetString(), "D", out var gameId))
                    throw new PlayniteBridgeDataException("invalid_playnite_data");
                running.Add(gameId.ToString("D"));
            }
        }
        var since = new Dictionary<string, long>(StringComparer.Ordinal);
        if (root.TryGetProperty("runningSince", out var times))
        {
            if (times.ValueKind != JsonValueKind.Object) throw new PlayniteBridgeDataException("invalid_playnite_data");
            foreach (var entry in times.EnumerateObject())
                if (running?.Contains(entry.Name) == true && entry.Value.ValueKind == JsonValueKind.Number &&
                    entry.Value.TryGetInt64(out var stamp) && stamp is > 0 and <= 253402300799999)
                    since[entry.Name] = stamp;
        }
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("runningNames", out var titles))
        {
            if (titles.ValueKind != JsonValueKind.Object) throw new PlayniteBridgeDataException("invalid_playnite_data");
            foreach (var entry in titles.EnumerateObject())
                if (running?.Contains(entry.Name) == true && entry.Value.ValueKind == JsonValueKind.String &&
                    entry.Value.GetString() is { Length: > 0 and <= 256 } name && !name.Any(char.IsControl)) names[entry.Name] = name;
        }
        var processes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("runningProcesses", out var instances) && instances.ValueKind == JsonValueKind.Object)
            foreach (var pair in instances.EnumerateObject())
                if (running?.Contains(pair.Name) == true && pair.Value.ValueKind == JsonValueKind.String &&
                    pair.Value.GetString() is { Length: 64 } key && key.All(char.IsAsciiHexDigitLower))
                    processes[pair.Name] = key;
        return new(response.ConnectionId, instance, reset, next, changes, running, since, names, processes);
    }

    public async ValueTask<PlayniteProviderRefreshResult> RefreshAchievementsAsync(string gameId, CancellationToken token)
    {
        if (!UsesLocalConnection) return PlayniteProviderRefreshResult.Unavailable;
        var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.RefreshAchievements, CanonicalGameId(gameId))
            { CommandId = Guid.NewGuid().ToString("D") }, token).ConfigureAwait(false);
        if (response.StatusCode == 409) return PlayniteProviderRefreshResult.Unavailable;
        EnsureSuccess(response);
        using var json = JsonDocument.Parse(response.Body);
        if (response.StatusCode != 202 || OptionalString(json.RootElement, "operationId", 36) is not { } id || !Guid.TryParseExact(id, "D", out _))
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        // Waiting is cancelable; the provider's accepted network refresh is not.
        // Never resubmit a command because this page or connection went away.
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMinutes(2))
        {
            var status = await SendRequiredAsync(new(PlayniteBridgeCommandKind.OperationStatus) { OperationId = id }, token).ConfigureAwait(false);
            if (status.ConnectionId != response.ConnectionId || status.StatusCode == 404) return PlayniteProviderRefreshResult.Unavailable;
            EnsureSuccess(status);
            using var state = JsonDocument.Parse(status.Body);
            switch (OptionalString(state.RootElement, "state", 32))
            {
                case "completed": return PlayniteProviderRefreshResult.Completed;
                case "failed": case "cancelled": return PlayniteProviderRefreshResult.Failed;
                case "running": break;
                default: throw new PlayniteBridgeDataException("invalid_playnite_data");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        }
        return PlayniteProviderRefreshResult.StillRunning;
    }
}
