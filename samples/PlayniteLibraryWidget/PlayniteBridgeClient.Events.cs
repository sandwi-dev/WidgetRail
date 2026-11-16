using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteProviderRefreshResult { Completed, StillRunning, Failed, Unavailable }
internal sealed record PlayniteChange(long Sequence, string Kind, string? GameId, string? AchievementId, long ObservedAt);
internal sealed record PlayniteEventBatch(string ConnectionId, string InstanceId, bool ResetRequired, long NextSequence,
    IReadOnlyList<PlayniteChange> Changes);

internal sealed partial class PlayniteBridgeClient
{
    public async ValueTask<PlayniteEventBatch?> ReadEventsAsync(long after, CancellationToken token)
    {
        if (!UsesLocalConnection) return null;
        if (after < -1) throw new ArgumentOutOfRangeException(nameof(after));
        var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.IntegrationEvents)
            { AfterSequence = after, WaitMilliseconds = after < 0 ? 0 : 25000 }, token).ConfigureAwait(false);
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
        return new(response.ConnectionId, instance, reset, next, changes);
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
