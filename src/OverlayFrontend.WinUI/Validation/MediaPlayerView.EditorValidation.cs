using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal async Task ValidateImageReplacementAsync(string path, List<string> checks)
    {
        var owner = new object();
        var attachment = new CaptureAttachment(new string('a', 32), "image/png", 640, 360,
            new FileInfo(path).Length, 0, long.MaxValue);
        var next = new MediaPlayerDefinition(MediaPlayerSource.FromCapture(attachment), new());
        HostMediaPlayerSource Source(MediaPlayerDefinition media) => new(media, new Uri(path).AbsoluteUri, true, 640, 360);
        SetActive(true, true); Apply(_ => Task.FromResult(Source(next)), next, () => true, owner);
        await Wait(() => IsImage && image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
        var previous = image.Source;
        var ready = new TaskCompletionSource<HostMediaPlayerSource>();
        next = next with { Revision = 2 };
        Apply(_ => ready.Task, next, () => true, owner);
        if (!ReferenceEquals(previous, image.Source) || CanControl || imageViewport.IsEnabled)
            throw new InvalidOperationException("Image replacement lost its decoded frame or kept stale input enabled.");
        checks.Add("In-place image replacement retains decoded pixels with input disabled");
        ready.SetResult(Source(next));
        await Wait(() => IsImage && image.Source is not null && !ReferenceEquals(previous, image.Source));
        checks.Add("Replacement image is decoded before it becomes interactive");
        var optionsLoad = new TaskCompletionSource<HostMediaPlayerSource>();
        var optionsPrior = next with { Revision = 20 };
        Apply(_ => optionsLoad.Task, optionsPrior, () => true, owner);
        var optionsLatest = optionsPrior with { Options = new(Muted: true) };
        Apply(_ => Task.FromResult(Source(optionsLatest)), optionsLatest, () => true, owner);
        await Wait(() => IsImage && image.Source is not null);
        previous = image.Source;
        optionsLoad.SetResult(Source(optionsPrior)); await Task.Delay(50);
        if (!ReferenceEquals(previous, image.Source) || definition != optionsLatest) throw new InvalidOperationException("Options changed during decoding stranded the replacement.");
        checks.Add("Options changes during decoding complete the newest image without stale publication");
        previous = image.Source;
        var delayed = new TaskCompletionSource<HostMediaPlayerSource>();
        next = next with { Revision = 3 };
        Apply(_ => delayed.Task, next, () => true, owner);
        await Wait(() => image.Source is null);
        if (CanControl) throw new InvalidOperationException("Slow replacement retained stale interaction.");
        checks.Add("Slow image replacement retires the old frame after the bounded loading grace");
        var latest = next with { Revision = 4 };
        Apply(_ => Task.FromResult(Source(latest)), latest, () => true, new object());
        if (image.Source is not null) throw new InvalidOperationException("Owner replacement retained another owner's frame.");
        await Wait(() => IsImage && image.Source is not null);
        previous = image.Source;
        delayed.SetResult(Source(next)); await Task.Delay(50);
        if (!ReferenceEquals(previous, image.Source)) throw new InvalidOperationException("Late image load replaced a newer owner.");
        checks.Add("Owner replacement clears retained pixels and rejects late decoded results");
        Apply(_ => Task.FromException<HostMediaPlayerSource>(new InvalidOperationException("fixture denied")), latest with { Revision = 5 }, () => true);
        await Wait(() => image.Source is null && status.Visibility == Microsoft.UI.Xaml.Visibility.Visible);
        checks.Add("Failed source resolution clears retained image pixels");
        static async Task Wait(Func<bool> ready)
        {
            var end = Environment.TickCount64 + 5000;
            while (!ready()) { if (Environment.TickCount64 > end) throw new TimeoutException("Image replacement did not settle."); await Task.Delay(10); }
        }
    }

    internal async Task ValidateEditorPauseAsync(string path, List<string> checks)
    {
        var renewals = 0;
        var definition = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(path), new(AutoPlay: true, Muted: true));
        Task<HostMediaPlayerSource> Resolve(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); ++renewals;
            return Task.FromResult(new HostMediaPlayerSource(definition, new Uri(path).AbsoluteUri,
                ExpiresAtUnixMilliseconds: DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeMilliseconds(), RequiresRevalidation: true));
        }
        SetActive(true, true); Apply(Resolve, definition, () => true);
        await Wait(() => IsPlaying && player?.PlaybackSession.CanSeek == true);
        var original = player;
        SetEditorPreviewPaused(true); SetActive(true, true);
        await Wait(() => !IsPlaying);
        var pausedAt = player!.PlaybackSession.Position;
        await Wait(() => renewals >= 3);
        await Task.Delay(1500); // Outlast the initial five-second authority without changing the normal two-second renewal interval.
        if (!ReferenceEquals(player, original) || IsPlaying || resolved?.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new InvalidOperationException("Pausing for the editor stopped source renewal or lost the original decoder.");
        checks.Add("Editor pause retains the same decoder and renews source authority past its initial expiry");
        SetEditorPreviewPaused(false); SetActive(true, true);
        if (IsPlaying || Math.Abs((player!.PlaybackSession.Position - pausedAt).TotalSeconds) > .2)
            throw new InvalidOperationException("Returning from the editor lost the paused position or resumed playback.");
        checks.Add("Returning from the editor preserves paused position without starting audio");
        SetEditorPreviewPaused(true); SetActive(true, true);
        definition = definition with { Revision = 2 };
        Apply(Resolve, definition, () => true);
        await Wait(() => player?.PlaybackSession.CanSeek == true);
        await Task.Delay(100);
        if (IsPlaying) throw new InvalidOperationException("Late media opening played underneath the editor.");
        checks.Add("A late media opening cannot autoplay beneath the editor");
        SetEditorPreviewPaused(false); SetActive(true, true);
        static async Task Wait(Func<bool> ready)
        {
            var end = Environment.TickCount64 + 10000;
            while (!ready()) { if (Environment.TickCount64 > end) throw new TimeoutException("Editor playback pause did not settle."); await Task.Delay(25); }
        }
    }
}
