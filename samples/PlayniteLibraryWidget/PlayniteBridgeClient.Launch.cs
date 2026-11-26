using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLaunchConflict(string Revision, string Name, int Count);
internal sealed record PlayniteLaunchReceipt(string? Observation, string? Connection, PlayniteLaunchConflict? Conflict = null);
internal sealed record PlayniteLaunchProgress(string State, int ProcessId, long CreatedFileTime, long WindowHandle = 0);
internal sealed record PlayniteWindowBinding(Guid Session, string Connection, bool Remembered);

internal sealed partial class PlayniteBridgeClient
{
    public async ValueTask<bool> SetGameWindowAsync(string gameId, PlayniteWindowBinding run, string match, CancellationToken token)
    {
        if (!UsesLocalConnection) return false;
        var body = JsonSerializer.Serialize(new { session = run.Session.ToString("N"), window = match });
        var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.SetGameWindow, CanonicalGameId(gameId), JsonBody: body)
            { ExpectedConnectionId = run.Connection, CommandId = Guid.NewGuid().ToString("D") }, token).ConfigureAwait(false);
        if (response.StatusCode is 404 or 409 || response.ConnectionId != run.Connection) return false;
        EnsureSuccess(response);
        return true;
    }
    public async ValueTask<PlayniteLaunchReceipt?> BeginObservedGameAsync(string gameId, CancellationToken token, string? approval = null, bool returnOnly = false)
    {
        var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.Launch, CanonicalGameId(gameId))
            { ReturnOnly = returnOnly, RunningApproval = approval ?? "check" }, token).ConfigureAwait(false);
        if (response.StatusCode == 404) return null;
        EnsureSuccess(response);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("conflict", out var revision))
        {
            if (revision.ValueKind != JsonValueKind.String || !Guid.TryParseExact(revision.GetString(), "N", out var key) ||
                OptionalString(root, "name", 1024) is not { } name || !root.TryGetProperty("count", out var count) ||
                !count.TryGetInt32(out var total) || total < 1) throw new PlayniteBridgeDataException("invalid_response");
            return new(null, response.ConnectionId, new(key.ToString("N"), name, total));
        }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("observation", out var value) ||
            value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "N", out var id))
            return new(null, response.ConnectionId);
        return new(id.ToString("N"), response.ConnectionId);
    }

    public async ValueTask<PlayniteLaunchProgress> GetLaunchStatusAsync(string gameId, PlayniteLaunchReceipt receipt, CancellationToken token)
    {
        var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.LaunchStatus, CanonicalGameId(gameId))
            { OperationId = receipt.Observation, ExpectedConnectionId = receipt.Connection }, token).ConfigureAwait(false);
        if (response.StatusCode == 404) return new("unconfirmed", 0, 0);
        EnsureSuccess(response);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("state", out var state) ||
            state.ValueKind != JsonValueKind.String || state.GetString() is not ("pending" or "running" or "ended") ||
            !root.TryGetProperty("processId", out var pid) || pid.ValueKind != JsonValueKind.Number || !pid.TryGetInt32(out var processId) ||
            !root.TryGetProperty("createdFileTime", out var created) || created.ValueKind != JsonValueKind.Number || !created.TryGetInt64(out var start))
            throw new PlayniteBridgeDataException("invalid_response");
        long hwnd = 0;
        if (root.TryGetProperty("windowHandle", out var handle) &&
            (handle.ValueKind != JsonValueKind.Number || !handle.TryGetInt64(out hwnd) || hwnd < 0))
            throw new PlayniteBridgeDataException("invalid_response");
        return new(state.GetString()!, processId, start, hwnd);
    }
}
