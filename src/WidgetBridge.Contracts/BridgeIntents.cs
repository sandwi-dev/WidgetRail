using System.Text.Json.Serialization;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

[JsonConverter(typeof(JsonStringEnumConverter<WidgetIntentLaunchKind>))]
public enum WidgetIntentLaunchKind { Widget, ChooseHandler, ExternalBrowser, Unavailable, Rejected }

public sealed record WidgetIntentDestination(string WidgetId, string Name, bool SupportsPassiveDelivery = false);
public sealed record WidgetIntentPreparation(string SourceWidgetId, string? TicketId,
    WidgetIntentLaunchKind Kind, IReadOnlyList<WidgetIntentDestination> Destinations,
    WidgetIntentPresentation Presentation = WidgetIntentPresentation.PreferExistingSurface);
public sealed record WidgetIntentCompletion(bool Accepted, string? ExternalUrl = null, string? BrowserFallbackUrl = null,
    bool RequiresInteraction = false);

internal sealed record BridgeIntentPrepareRequest(string WidgetId, long SnapshotSequence,
    BridgeWorkerRun WorkerRun, WidgetActionEvent Action, string? PinnedLayoutId = null,
    IndexedCollectionItemReference? IndexedItem = null);
internal sealed record BridgeIntentCommitRequest(string WidgetId, string TicketId,
    string? TargetWidgetId = null, BridgeWorkerRun? TargetWorkerRun = null, BridgeIntentPinnedTarget? PinnedTarget = null);
internal sealed record BridgeIntentPinnedTarget(string LayoutId, long SnapshotSequence);
internal sealed record BridgeIntentCancelRequest(string WidgetId, string TicketId);
