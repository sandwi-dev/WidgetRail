using System.Text.Json.Serialization;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

[JsonConverter(typeof(JsonStringEnumConverter<WidgetIntentLaunchKind>))]
public enum WidgetIntentLaunchKind { Widget, ChooseHandler, ExternalBrowser, Unavailable, Rejected }

public sealed record WidgetIntentDestination(string WidgetId, string Name);
public sealed record WidgetIntentPreparation(string SourceWidgetId, string? TicketId,
    WidgetIntentLaunchKind Kind, IReadOnlyList<WidgetIntentDestination> Destinations);
public sealed record WidgetIntentCompletion(bool Accepted, string? ExternalUrl = null);

internal sealed record BridgeIntentPrepareRequest(string WidgetId, long SnapshotSequence,
    BridgeWorkerRun WorkerRun, WidgetActionEvent Action);
internal sealed record BridgeIntentCommitRequest(string WidgetId, string TicketId,
    string? TargetWidgetId = null, BridgeWorkerRun? TargetWorkerRun = null);
internal sealed record BridgeIntentCancelRequest(string WidgetId, string TicketId);
