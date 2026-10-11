using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WidgetPermissionState>))]
public enum WidgetPermissionState { NotDecided, Granted, Denied, Unavailable }
[JsonConverter(typeof(JsonStringEnumConverter<WidgetPermissionOutcome>))]
public enum WidgetPermissionOutcome { Pending, Granted, Denied, Cancelled, Unavailable }
public sealed record WidgetPermissionQuery(IReadOnlyList<string> CapabilityIds)
{
    public bool IsWellFormed() => CapabilityIds is { Count: > 0 and <= 8 } &&
        CapabilityIds.All(id => id is { Length: > 0 and <= 160 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':')) &&
        CapabilityIds.Distinct(StringComparer.Ordinal).Count() == CapabilityIds.Count;
}
public sealed record WidgetPermissionStatus(string CapabilityId, WidgetPermissionState State);
public sealed record WidgetPermissionStatuses(IReadOnlyList<WidgetPermissionStatus> Permissions);
public sealed record WidgetPermissionTicket(string RequestId);
public sealed record WidgetPermissionRequestState(WidgetPermissionOutcome Outcome, string? RequestId = null);
