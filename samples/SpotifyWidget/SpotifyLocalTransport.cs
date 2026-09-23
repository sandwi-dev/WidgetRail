namespace WidgetRail.Samples.SpotifyWidget;

// One complete, timestamped observation from the local SDK. Never combine
// transport flags for one track with cloud metadata for a different track.
public sealed record SpotifyLocalTransportObservation(SpotifyPlaybackSummary Playback);

public interface ISpotifyLocalTransport
{
    event EventHandler? LocalTransportChanged;
    SpotifyLocalTransportObservation? GetLocalTransport();
}
