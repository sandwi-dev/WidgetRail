using WidgetRail.SpotifyPlayback;

namespace WidgetRail.SpotifyPlaybackHost;

/// <summary>Pure deterministic lifecycle for the single SDK player instance.</summary>
public sealed class SpotifyPlaybackStateMachine
{
    public SpotifyPlaybackLifecycleState State { get; private set; } =
        SpotifyPlaybackLifecycleState.LoadingSdk;

    public SpotifyPlaybackTransition Apply(SpotifyPlaybackSignal signal)
    {
        var previous = State;
        State = Next(previous, signal);
        return new(previous, State, signal, previous != State);
    }

    public static SpotifyPlaybackLifecycleState Next(
        SpotifyPlaybackLifecycleState state,
        SpotifyPlaybackSignal signal)
    {
        if (signal == SpotifyPlaybackSignal.Shutdown)
            return SpotifyPlaybackLifecycleState.Stopped;
        if (state == SpotifyPlaybackLifecycleState.Stopped)
            return state;

        return (state, signal) switch
        {
            (SpotifyPlaybackLifecycleState.LoadingSdk, SpotifyPlaybackSignal.SdkLoaded) =>
                SpotifyPlaybackLifecycleState.Disconnected,
            (SpotifyPlaybackLifecycleState.Disconnected, SpotifyPlaybackSignal.ConnectRequested) =>
                SpotifyPlaybackLifecycleState.Connecting,
            (SpotifyPlaybackLifecycleState.NotReady, SpotifyPlaybackSignal.ConnectRequested) =>
                SpotifyPlaybackLifecycleState.Connecting,
            (SpotifyPlaybackLifecycleState.Faulted, SpotifyPlaybackSignal.ConnectRequested) =>
                SpotifyPlaybackLifecycleState.Connecting,
            (SpotifyPlaybackLifecycleState.Connecting, SpotifyPlaybackSignal.Ready) =>
                SpotifyPlaybackLifecycleState.Ready,
            (SpotifyPlaybackLifecycleState.Ready, SpotifyPlaybackSignal.NotReady) =>
                SpotifyPlaybackLifecycleState.NotReady,
            (SpotifyPlaybackLifecycleState.Connecting, SpotifyPlaybackSignal.NotReady) =>
                SpotifyPlaybackLifecycleState.NotReady,
            (_, SpotifyPlaybackSignal.DisconnectRequested) =>
                SpotifyPlaybackLifecycleState.Disconnecting,
            (SpotifyPlaybackLifecycleState.Disconnecting, SpotifyPlaybackSignal.Disconnected) =>
                SpotifyPlaybackLifecycleState.Disconnected,
            (_, SpotifyPlaybackSignal.InitializationError or
                SpotifyPlaybackSignal.AuthenticationError or
                SpotifyPlaybackSignal.AccountError) =>
                SpotifyPlaybackLifecycleState.Faulted,
            (_, SpotifyPlaybackSignal.PlaybackError or SpotifyPlaybackSignal.AutoplayFailed) =>
                state,
            _ => state,
        };
    }
}
