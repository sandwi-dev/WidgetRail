using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
namespace WidgetRail.Samples.YtMusicWidget.Standalone;
public sealed partial class StandaloneMusicWidget
{
    private WidgetActivity? desiredActivity;
    private void ObservePlaybackActivity()
    {
        var player = _service.State.Player;
        var activity = new WidgetActivity(LocalAudio: player.Playing && !player.Buffering && player.Volume > 0 && player.Error is null);
        lock (_gate) { if (desiredActivity == activity) return; desiredActivity = activity; }
        Operations.RunLatest("music.activity", async context =>
        {
            try
            {
                var result = await HostServices.Activity.SetAsync("player", activity, context.CancellationToken);
                if (!result.Accepted && !result.Superseded) lock (_gate) { if (desiredActivity == activity) desiredActivity = null; }
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or OperationCanceledException)
            { lock (_gate) { if (desiredActivity == activity && context.IsCurrent) desiredActivity = null; } }
        }, WidgetOperationLifetime.Widget);
    }
}
