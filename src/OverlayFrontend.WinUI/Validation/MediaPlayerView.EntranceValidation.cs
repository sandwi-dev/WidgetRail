using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal async Task ValidatePreviewEntranceAsync(string png, string mp4, List<string> checks)
    {
        if (Parent is not Panel host) throw new InvalidOperationException("Expected native media test host.");
        await Validation.WidgetPreviewMotionValidation.RunAsync(host, checks);
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Full };
        using var entry = new WidgetDialogMotion(this, null, appearance, true, WidgetDialogMotionRole.MediaPreview);
        PresentationReady = entry.Ready;
        var clip = new CaptureAttachment(Guid.NewGuid().ToString("N"), "video/mp4", 640, 360,
            new FileInfo(mp4).Length, 5, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds());
        SetActive(true, true);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(clip, mp4)), clip, () => true);
        SetPoster(new BitmapImage(new Uri(png)));
        Check(!entry.Ready.IsCompleted && player is null && video.PosterSource is not null,
            "Preview source can resolve with its poster while native decoder waits for entry");
        InvalidateMeasure(); UpdateLayout();
        await entry.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        var deadline = Environment.TickCount64 + 10000;
        while (player?.PlaybackSession.NaturalDuration.TotalSeconds is not > 0)
        { if (Environment.TickCount64 > deadline) throw new TimeoutException("Media did not open after preview entry."); await Task.Delay(25); }
        Check(player is not null && entry.Ready.IsCompletedSuccessfully,
            "Existing media loader creates the decoder after preview entry settles");
        var retained = player; entry.ApplyAppearance(appearance, true); UpdateLayout();
        Check(ReferenceEquals(player, retained), "Entry appearance updates retain the same decoder");
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
    }
}
