using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;
using WidgetRail.OverlayFrontend.WinUI.Capture;
namespace WidgetRail.OverlayFrontend.WinUI.Media;
internal sealed partial class MediaPlayerView
{
    internal async Task ValidateActivityAsync(string path, List<string> checks)
    {
        bool reported = false;
        AudioChanged = value => reported = value;
        SetActive(true, true);
        var definition = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(path), new(Volume: .1, Loop: true));
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(definition, new Uri(path).AbsoluteUri)), definition, () => true);
        await Wait(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 2);
        Toggle(); await Wait(() => reported); checks.Add("Native playback reports audio");
        ToggleMute(); await Wait(() => !reported); checks.Add("Native mute clears audio");
        ToggleMute(); await Wait(() => reported);
        volume.Value = 0; await Wait(() => !reported); checks.Add("Native zero volume clears audio");
        volume.Value = 10; await Wait(() => reported);
        Toggle(); await Wait(() => !reported); checks.Add("Native pause clears audio");
        Toggle(); await Wait(() => reported);
        ReleasePlayer();
        if (reported) throw new InvalidOperationException("Native player retirement retained audio");
        checks.Add("Native retirement clears its activity contribution");
        static async Task Wait(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!ready()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Native audio badge did not settle"); await Task.Delay(20); }
        }
    }
}
