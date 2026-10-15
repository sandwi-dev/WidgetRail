namespace WidgetRail.WidgetProtocol;

/// <summary>Current activity owned by this widget. Remote playback does not imply local audio output.</summary>
public sealed record WidgetActivity(bool LocalAudio = false, bool RemotePlayback = false, bool Recording = false);
public sealed record WidgetActivityReceipt(bool Accepted, bool Superseded = false);
