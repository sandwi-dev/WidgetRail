using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;

public sealed record BridgeMediaPlayerRequest(string WidgetId, BridgeWorkerRun WorkerRun, long SnapshotSequence,
    string ElementId, MediaPlayerDefinition Media, string? PinnedLayoutId = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BridgeMediaPlayerIndexedOrigin? IndexedOrigin { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaybackLeaseId { get; init; }
}
public sealed record BridgeMediaPlayerIndexedOrigin(string InstanceId, string RuntimeGeneration, string PresentationGeneration,
    IndexedCollectionItemReference Item);
public sealed record BridgeMediaPlayerRelease(string PlaybackLeaseId);
public sealed record HostMediaPlayerSource(MediaPlayerDefinition Media, string Uri, bool IsImage = false,
    int Width = 0, int Height = 0, double DurationSeconds = 0, long? ExpiresAtUnixMilliseconds = null, bool RequiresRevalidation = false)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PlaybackLeaseId { get; init; }
}
