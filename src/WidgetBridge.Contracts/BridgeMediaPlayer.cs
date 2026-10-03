using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;

public sealed record BridgeMediaPlayerRequest(string WidgetId, BridgeWorkerRun WorkerRun, long SnapshotSequence,
    string ElementId, MediaPlayerDefinition Media, string? PinnedLayoutId = null);
public sealed record HostMediaPlayerSource(MediaPlayerDefinition Media, string Uri, bool IsImage = false,
    int Width = 0, int Height = 0, double DurationSeconds = 0, long? ExpiresAtUnixMilliseconds = null, bool RequiresRevalidation = false);
