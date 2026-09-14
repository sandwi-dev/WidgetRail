namespace WidgetRail.WidgetSdk;

public sealed record WidgetDisplayProfileMonitor(string Id, string Name, int Width, int Height,
    int X, int Y, double RefreshRate, string Orientation, bool IsPrimary);
public sealed record WidgetDisplayProfileSummary(string Id, string Name, string Mode,
    IReadOnlyList<WidgetDisplayProfileMonitor> Displays, bool MatchesCurrent, bool Available, string? UnavailableReason);
public sealed record WidgetDisplayProfileRestore(string Id, string ProfileName, DateTimeOffset Deadline);
public sealed record WidgetDisplayProfilesState(string Mode, IReadOnlyList<WidgetDisplayProfileMonitor> Displays,
    IReadOnlyList<WidgetDisplayProfileSummary> Profiles, WidgetDisplayProfileRestore? PendingRestore, string? Outcome);
public sealed record WidgetDisplayProfileRequest(string? ProfileId = null, string? Name = null, string? RestoreId = null);

public static class WidgetDisplayProfilesCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetDisplayProfilesState> Get { get; } =
        new("system.displays.read.v1", "display-profiles.get");
    public static WidgetCapabilityEvent<WidgetCapabilityAcknowledgement> Changed { get; } =
        new("system.displays.read.v1", "display-profiles.changed");
    public static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Save { get; } = Control("save");
    public static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Rename { get; } = Control("rename");
    public static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Replace { get; } = Control("replace");
    public static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Delete { get; } = Control("delete");
    public static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Apply { get; } = Control("apply");
    public static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Keep { get; } = Control("keep");
    public static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Revert { get; } = Control("revert");
    private static WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> Control(string name) =>
        new("system.displays.control.v1", "display-profiles." + name);
}

/// <summary>Capture named display setups and restore them with an independent confirmation timeout.
/// Windows UI scaling is left unchanged. Raw display modes and monitor handles remain host-owned.</summary>
public sealed class WidgetDisplayProfilesService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetDisplayProfilesService(IWidgetCapabilityClient client) => _client = client;
    public async ValueTask<WidgetDisplayProfilesState> GetAsync(CancellationToken cancellationToken = default) =>
        await _client.InvokeAsync(WidgetDisplayProfilesCapabilities.Get, new(), cancellationToken).ConfigureAwait(false)
        ?? throw new WidgetCapabilityException("malformed_response", "Display profiles are unavailable.");
    public ValueTask<WidgetDisplayProfilesState> SaveAsync(string name, CancellationToken cancellationToken = default) =>
        Send(WidgetDisplayProfilesCapabilities.Save, new(Name: name), cancellationToken);
    public ValueTask<WidgetDisplayProfilesState> RenameAsync(string id, string name, CancellationToken cancellationToken = default) =>
        Send(WidgetDisplayProfilesCapabilities.Rename, new(id, name), cancellationToken);
    public ValueTask<WidgetDisplayProfilesState> ReplaceAsync(string id, CancellationToken cancellationToken = default) =>
        Send(WidgetDisplayProfilesCapabilities.Replace, new(id), cancellationToken);
    public ValueTask<WidgetDisplayProfilesState> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Send(WidgetDisplayProfilesCapabilities.Delete, new(id), cancellationToken);
    public ValueTask<WidgetDisplayProfilesState> ApplyAsync(string id, CancellationToken cancellationToken = default) =>
        Send(WidgetDisplayProfilesCapabilities.Apply, new(id), cancellationToken);
    public ValueTask<WidgetDisplayProfilesState> KeepAsync(string restoreId, CancellationToken cancellationToken = default) =>
        Send(WidgetDisplayProfilesCapabilities.Keep, new(RestoreId: restoreId), cancellationToken);
    public ValueTask<WidgetDisplayProfilesState> RevertAsync(string restoreId, CancellationToken cancellationToken = default) =>
        Send(WidgetDisplayProfilesCapabilities.Revert, new(RestoreId: restoreId), cancellationToken);
    private async ValueTask<WidgetDisplayProfilesState> Send(
        WidgetCapabilityOperation<WidgetDisplayProfileRequest, WidgetDisplayProfilesState> operation,
        WidgetDisplayProfileRequest request, CancellationToken token) =>
        await _client.InvokeAsync(operation, request, token).ConfigureAwait(false)
        ?? throw new WidgetCapabilityException("malformed_response", "The display request was not acknowledged.");
}
