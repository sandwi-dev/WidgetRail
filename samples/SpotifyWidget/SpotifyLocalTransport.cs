namespace WidgetRail.Samples.SpotifyWidget;

// Observation only: state and transport permissions come from the same SDK event.
public sealed record SpotifyLocalTransportObservation(
    bool IsPlaying, bool PausingDisallowed, bool ResumingDisallowed);

public interface ISpotifyLocalTransport
{
    event EventHandler? LocalTransportChanged;
    SpotifyLocalTransportObservation? GetLocalTransport();
}
