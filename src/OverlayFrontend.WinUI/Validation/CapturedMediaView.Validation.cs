using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using Windows.Media.Playback;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private CaptureAttachment? attachment => definition?.Source.Attachment;
    internal CaptureAttachment? Attachment => attachment;
    internal void Apply(Func<CancellationToken, Task<HostCaptureAttachment>> resolve, CaptureAttachment next, Func<bool> current) =>
        Apply(async token => { var result = await resolve(token); return new(MediaPlayerDefinition.FromCapture(result.Attachment), new Uri(result.Path).AbsoluteUri,
            result.Attachment.ContentType == "image/png", result.Attachment.Width, result.Attachment.Height, result.Attachment.DurationSeconds, result.Attachment.ExpiresAtUnixMilliseconds, RequiresRevalidation: true); },
            MediaPlayerDefinition.FromCapture(next), current);

    internal async Task ValidateCapturePlaybackAsync(string png, string mp4, List<string> checks)
    {
        var still = AttachmentFor(png, "image/png", 0);
        var clip = AttachmentFor(mp4, "video/mp4", 5);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(still, png)), still, () => true);
        await Until(() => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0);
        Check(image.Visibility == Visibility.Visible && player is null, "Screenshot preview decodes generated PNG without a player");
        SetActive(true, true);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(clip, mp4)), clip, () => true);
        await Until(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 4.9);
        Check(!IsPlaying && player!.IsMuted, "Native video opens muted without autoplay");
        Check(play.IsTabStop && replay.IsTabStop && play.UseSystemFocusVisuals && replay.UseSystemFocusVisuals,
            "Native transport buttons participate in focus and use theme focus visuals");
        Check(play.Focus(FocusState.Keyboard), "Play accepts keyboard/controller focus");
        Check(MoveFocus(FocusNavigationDirection.Right) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), replay) && PrimaryActionLabel == "Replay",
            "Right navigation focuses Replay and updates the A action");
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => IsPlaying);
        Check(MoveFocus(FocusNavigationDirection.Left) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), play),
            "Left navigation returns to Play without leaving the media element");
        Toggle(); await Until(() => !IsPlaying);
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => IsPlaying && player!.PlaybackSession.Position.TotalSeconds > .1);
        Handle(ControllerButton.A, ControllerEventPhase.Released);
        Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        await Until(() => !IsPlaying);
        Check(true, "Controller A plays and pauses native video");
        var before = player!.PlaybackSession.Position.TotalSeconds;
        Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
        await Until(() => player.PlaybackSession.Position.TotalSeconds > before + .3);
        Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
        await Until(() => IsPlaying && player.PlaybackSession.Position.TotalSeconds < 1);
        Check(true, "Native capture supports seeking and replay");
        Check(position.ActualWidth > 120 && toolbar.ActualHeight >= play.ActualHeight,
            "Timeline fills available toolbar width and transport controls fit within the media card");
        SetActive(false, false);
        await Until(() => !IsPlaying);
        Check(!Handle(ControllerButton.A, ControllerEventPhase.Pressed), "Inactive preview pauses and refuses controller actions");
        Check(!Handle(ControllerButton.B, ControllerEventPhase.Pressed), "Preview leaves B to normal host navigation");
        SetActive(true, true);
        var general = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(mp4), new(Volume:.6, PlaybackRate:1.25, Loop:true));
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)), general, () => true);
        await Until(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 4.9);
        Check(Attachment is null && !player!.IsMuted && player.IsLoopingEnabled && Math.Abs(player.Volume-.6)<.01,
            "General local video uses the same player without a capture attachment and honors options");
        await Until(() => Math.Abs(player!.PlaybackSession.PlaybackRate-1.25)<.01);
        Check(true, "Initial playback rate is applied after media opens");
        CycleSpeed(); Check(Math.Abs(player.PlaybackSession.PlaybackRate-1.5)<.01,"Speed control cycles native playback rate");
        ToggleLoop(); Check(!player.IsLoopingEnabled,"Loop control updates native looping");
        ToggleMute(); Check(player.IsMuted,"Mute control updates native audio");
        volume.Value = 35; Check(!player.IsMuted && Math.Abs(player.Volume-.35)<.01,"Volume slider adjusts native audio and unmutes");
        position.Focus(FocusState.Keyboard);
        MoveFocus(FocusNavigationDirection.Right);
        Check(player.PlaybackSession.Position.TotalSeconds>=.4,"Focusable timeline supports controller scrubbing");
        MoveFocus(FocusNavigationDirection.Down);
        Check(controls.Contains(FocusManager.GetFocusedElement(XamlRoot) as Microsoft.UI.Xaml.Controls.Control),"Timeline returns to transport controls");
        var waiting = new TaskCompletionSource<HostMediaPlayerSource>();
        var pendingOptions = general with { Revision = 2 };
        Apply(_ => waiting.Task, pendingOptions, () => true);
        general = pendingOptions with { Options = new(Muted:true) };
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)), general, () => true);
        waiting.SetResult(new(pendingOptions,new Uri(mp4).AbsoluteUri));
        await Until(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 4.9);
        Check(player!.IsMuted,"Changing playback options during source resolution cannot strand or revive the old load");
        Toggle(); await Until(() => IsPlaying);
        var retained = player;
        var expanded = ToggleExpandedAsync();
        await Until(() => expandedDialog is { IsLoaded:true });
        Check(IsPlaying && ReferenceEquals(player,retained) && card.Parent is Microsoft.UI.Xaml.Controls.Grid,"Expanded view retains one player and playback state");
        Handle(ControllerButton.B,ControllerEventPhase.Pressed);
        Handle(ControllerButton.B,ControllerEventPhase.Released);
        await expanded;
        Check(IsPlaying && !HasDialog && ReferenceEquals(Content,card) && ReferenceEquals(player,retained),"B restores inline player without recreating playback");
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)), general, () => true);
        Check(ReferenceEquals(player,retained),"Compatible snapshots do not reopen general media");
        var rememberedPosition = player!.PlaybackSession.Position.TotalSeconds;
        SetActive(false,false); Check(ResidentPlayers==0,"Hiding a player releases its native decoder slot");
        SetActive(true,true);
        Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)),general,()=>true);
        await Until(()=>player?.PlaybackSession.NaturalDuration.TotalSeconds>4.9 && playbackMemory is null);
        Check(!IsPlaying && Math.Abs(player!.PlaybackSession.Position.TotalSeconds-rememberedPosition)<.3,"Same-source reopen restores position without autoplay");
        var extraPlayers = Enumerable.Range(0,4).Select(_ => new MediaPlayerView()).ToArray();
        try
        {
            foreach (var extra in extraPlayers) { extra.SetActive(true,false); extra.Apply(_ => Task.FromResult(new HostMediaPlayerSource(general,new Uri(mp4).AbsoluteUri)),general,()=>true); }
            Check(ResidentPlayers==4 && extraPlayers[3].player is null && extraPlayers[3].waitingForSlot,"Native decoder residency is bounded across media elements");
            extraPlayers[0].Dispose(); extraPlayers[3].Refresh();
            Check(extraPlayers[3].player is not null && ResidentPlayers==4,"Waiting player recovers when another decoder closes");
        }
        finally { foreach (var extra in extraPlayers) extra.Dispose(); }
        Check(ResidentPlayers==1,"Disposing extra players releases every decoder slot");
        var delayed = new TaskCompletionSource<HostCaptureAttachment>();
        var retired = clip with { Id = new string('c', 32) };
        Apply(_ => delayed.Task, retired, () => true);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(still, png)), still, () => true);
        delayed.SetResult(new(retired, mp4));
        await Until(() => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0);
        await Task.Delay(100);
        Check(player is null && attachment == still, "Late load from retired media cannot replace the current preview");
        var denied = false;
        SetActive(true, true);
        Apply(_ => denied ? Task.FromException<HostCaptureAttachment>(new InvalidOperationException("revoked")) :
            Task.FromResult(new HostCaptureAttachment(still, png)), still, () => true);
        denied = true; nextAuthorityCheck = 0;
        await Until(() => image.Source is null && status.Visibility == Visibility.Visible);
        Check(true, "Revoked capture authority clears already-decoded preview pixels");
        Dispose(); Check(player is null && image.Source is null && ResidentPlayers == 0, "Disposal releases native media and image references");

        static CaptureAttachment AttachmentFor(string path, string type, double duration) =>
            new(Guid.NewGuid().ToString("N"), type, 640, 360, new FileInfo(path).Length, duration, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds());
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
        static async Task Until(Func<bool> condition)
        {
            var end = Environment.TickCount64 + 10000;
            while (!condition()) { if (Environment.TickCount64 >= end) throw new TimeoutException("Media preview check timed out."); await Task.Delay(25); }
        }
    }
}
