using System.Text.Json;

namespace WidgetRail.PlatformBroker;

public sealed record DisplayProfileMonitor(string Id, string Name, int Width, int Height,
    int X, int Y, double RefreshRate, string Orientation, bool IsPrimary);
public sealed record DisplayProfileSummary(string Id, string Name, string Mode,
    IReadOnlyList<DisplayProfileMonitor> Displays, bool MatchesCurrent, bool Available, string? UnavailableReason);
public sealed record DisplayProfileRestore(string Id, string ProfileName, DateTimeOffset Deadline);
public sealed record DisplayProfilesState(string Mode, IReadOnlyList<DisplayProfileMonitor> Displays,
    IReadOnlyList<DisplayProfileSummary> Profiles, DisplayProfileRestore? PendingRestore, string? Outcome);
public sealed record DisplayProfileRequest(string? ProfileId = null, string? Name = null, string? RestoreId = null);
public enum DisplayProfileCommand { Save, Rename, Replace, Delete, Apply, Keep, Revert }

public interface IDisplayProfilesPlatformBackend : IPlatformBrokerEventSource
{
    Task<DisplayProfilesState> GetDisplayProfilesAsync(CancellationToken token) =>
        Task.FromException<DisplayProfilesState>(new BrokerException("display_unavailable", "Display profiles are unavailable."));
    Task<DisplayProfilesState> ChangeDisplayProfileAsync(DisplayProfileCommand command,
        DisplayProfileRequest request, BrokerWidgetIdentity identity, CancellationToken token) =>
        Task.FromException<DisplayProfilesState>(new BrokerException("display_unavailable", "Display profiles are unavailable."));
}

internal sealed class DisplayProfilesCapabilityDomain(IPlatformBrokerBackend backend)
{
    internal static JsonElement ProjectEvent(string eventType) => eventType switch
    {
        // Invalidation only: display names, paths and native structures must
        // never be forwarded from a provider event into a widget subscription.
        PlatformCapabilities.DisplayProfilesChanged => BrokerJson.ToElement(new { acknowledged = true }),
        _ => throw new BrokerException("invalid_backend_data", "Display event type is invalid.")
    };

    internal async Task<JsonElement> ExecuteAsync(string operation, JsonElement payload,
        BrokerWidgetIdentity identity, CancellationToken token)
    {
        if (operation == PlatformCapabilities.DisplayProfilesGet)
        {
            BrokerCapabilityDomains.DemandEmptyPayload(payload);
            return BrokerJson.ToElement(await backend.GetDisplayProfilesAsync(token).ConfigureAwait(false));
        }
        var command = operation switch
        {
            PlatformCapabilities.DisplayProfilesSave => DisplayProfileCommand.Save,
            PlatformCapabilities.DisplayProfilesRename => DisplayProfileCommand.Rename,
            PlatformCapabilities.DisplayProfilesReplace => DisplayProfileCommand.Replace,
            PlatformCapabilities.DisplayProfilesDelete => DisplayProfileCommand.Delete,
            PlatformCapabilities.DisplayProfilesApply => DisplayProfileCommand.Apply,
            PlatformCapabilities.DisplayProfilesKeep => DisplayProfileCommand.Keep,
            PlatformCapabilities.DisplayProfilesRevert => DisplayProfileCommand.Revert,
            _ => throw new BrokerException("unsupported_operation", "Unknown display profile operation."),
        };
        var request = BrokerJson.ParsePayload<DisplayProfileRequest>(payload);
        if (command is DisplayProfileCommand.Save or DisplayProfileCommand.Rename)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 60 || request.Name.Any(char.IsControl))
                throw new BrokerException("invalid_payload", "Use a profile name of 1 to 60 characters.");
        }
        else if (request.Name is not null) throw new BrokerException("invalid_payload", "This operation does not accept a name.");
        if (command is DisplayProfileCommand.Keep or DisplayProfileCommand.Revert)
        {
            if (!Guid.TryParseExact(request.RestoreId, "N", out _) || request.ProfileId is not null)
                throw new BrokerException("invalid_payload", "Invalid restore identity.");
        }
        else
        {
            if (request.RestoreId is not null || (command == DisplayProfileCommand.Save
                ? request.ProfileId is not null : !Guid.TryParseExact(request.ProfileId, "N", out _)))
                throw new BrokerException("invalid_payload", "Invalid profile identity.");
        }
        return BrokerJson.ToElement(await backend.ChangeDisplayProfileAsync(command, request, identity, token).ConfigureAwait(false));
    }
}
