using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal bool ImageDecodedForValidation => image.Source is not null && IsImage;
    internal Microsoft.UI.Xaml.Controls.Image MainImageForValidation => image;
    internal object ImageGeometryForValidation => new
    {
        viewportWidth = imageViewport.ActualWidth, viewportHeight = imageViewport.ActualHeight,
        canvasWidth = imageCanvas.ActualWidth, canvasHeight = imageCanvas.ActualHeight,
        imageWidth = image.ActualWidth, imageHeight = image.ActualHeight,
        imageBounds = image.TransformToVisual(this).TransformBounds(new(0, 0, image.ActualWidth, image.ActualHeight)),
    };
    internal bool PreviewMutedForValidation => player?.IsMuted == true;
    internal bool PreviewSystemControlsDisabledForValidation => player is not null && !player.CommandManager.IsEnabled && !player.SystemMediaTransportControls.IsEnabled;
    internal bool HasPosterForValidation => video.PosterSource is not null;
    internal bool HasPlaybackLeaseForValidation => playbackLeaseId is not null;
    internal Microsoft.UI.Xaml.FrameworkElement PreviewVideoForValidation => video;
    internal object? PlaybackPlayerForValidation => player;
    internal bool PosterLoadingForValidation => video.PosterSource is not null && video.Visibility == Microsoft.UI.Xaml.Visibility.Visible &&
        status.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed && player is null;
    internal Microsoft.UI.Xaml.Controls.Control PreviewPlayControlForValidation => play;
    internal Microsoft.UI.Xaml.Controls.Control PreviewExpandControlForValidation => expand;
    internal Microsoft.UI.Xaml.Controls.Control PreviewOptionsControlForValidation => playbackOptions;
    internal bool PreviewOptionsLoadedForValidation => optionsMenu?.Items.FirstOrDefault()?.IsLoaded == true;
    internal Microsoft.UI.Xaml.Controls.Control? PreviewOptionsFirstForValidation => optionsMenu?.Items.FirstOrDefault();
    internal Microsoft.UI.Xaml.Controls.Slider PreviewVolumeForValidation => volume;
    internal bool PreviewChromeVisibleForValidation => videoChrome.Visible;
    internal void HidePreviewChromeForValidation() => videoChrome.Hide();
    internal Windows.Foundation.Rect PreviewVideoBoundsForValidation => MediaTransitionGeometry.Fit(video.TransformToVisual(XamlRoot.Content)
        .TransformBounds(new(0, 0, video.ActualWidth, video.ActualHeight)), VideoAspectRatio);
    internal Windows.Foundation.Rect ExpectedPreviewVideoBoundsForValidation => MediaTransitionGeometry.Fit(
        stage.TransformToVisual(XamlRoot.Content).TransformBounds(new(0, 0, stage.ActualWidth, stage.ActualHeight)), VideoAspectRatio);
    internal Windows.Foundation.Rect UnobscuredPreviewVideoBoundsForValidation
    {
        get
        {
            var bounds = PreviewVideoBoundsForValidation;
            if (!videoChrome.Visible || toolbar.Visibility != Microsoft.UI.Xaml.Visibility.Visible || !toolbar.IsLoaded) return bounds;
            var chrome = toolbar.TransformToVisual(XamlRoot.Content).TransformBounds(new(0, 0, toolbar.ActualWidth, toolbar.ActualHeight));
            if (chrome.Left < bounds.Right && chrome.Right > bounds.Left && chrome.Top > bounds.Top && chrome.Top < bounds.Bottom)
                bounds.Height = chrome.Top - bounds.Top;
            return bounds;
        }
    }
    internal void FocusPreviewVolumeForValidation() => volume.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
    internal async Task ValidatePreviewPlaybackAsync(string png, string path, List<string> checks)
    {
        var baseline = ResidentPlayers;
        var live = new HashSet<string>();
        var released = new List<string>();
        var owner = new object();
        var first = new MediaPlayerDefinition(MediaPlayerSource.FromSavedCapture(new string('a', 32), 1), new(AutoPlay: true, Loop: true, Muted: false));
        var second = first with { Source = MediaPlayerSource.FromSavedCapture(new string('b', 32), 1) };
        Task Release(string id) { live.Remove(id); released.Add(id); return Task.CompletedTask; }
        var imageDefinition = new MediaPlayerDefinition(MediaPlayerSource.FromSavedCapture(new string('c', 32), 1), new());
        var imageReads = 0;
        HostMediaPlayerSource ImageSource(MediaPlayerDefinition definition, string id) => new(definition, new Uri(png).AbsoluteUri,
            IsImage: true, Width: 320, Height: 180, ExpiresAtUnixMilliseconds: long.MaxValue) { PlaybackLeaseId = id };
        void ApplyImage(MediaPlayerDefinition definition) => Apply(_ => throw new InvalidOperationException("Image preview bypassed its lease."),
            definition, () => true, owner, (id, _) => { ++imageReads; live.Add(id); return Task.FromResult(ImageSource(definition, id)); }, Release);
        SetPreviewPlayback(true); SetActive(true, false); ApplyImage(imageDefinition);
        await Until(() => image.Source is not null && IsImage);
        Check(player is null && ResidentPlayers == baseline && live.Count == 1,
            "Screenshot preview decodes one active image without allocating a video decoder");
        UpdateLayout();
        var passiveHeight=imageViewport.ActualHeight;
        Check(imageTools.Visibility==Microsoft.UI.Xaml.Visibility.Collapsed && !imageTools.IsHitTestVisible, "Passive screenshot preview has no visible or interactive image toolbar");
        var decodedImage = image.Source;
        SetActive(true, true);
        UpdateLayout();
        Check(Math.Abs(imageViewport.ActualHeight-passiveHeight)<1,"Entering screenshot controls does not shrink or resize the image viewport");
        var imageBounds=imageViewport.TransformToVisual(card).TransformBounds(new(0,0,imageViewport.ActualWidth,imageViewport.ActualHeight));
        var toolBounds=imageTools.TransformToVisual(card).TransformBounds(new(0,0,imageTools.ActualWidth,imageTools.ActualHeight));
        Check(toolBounds.Top>=imageBounds.Top && toolBounds.Bottom<=imageBounds.Bottom && Microsoft.UI.Xaml.Controls.Grid.GetRowSpan(stage)==2,
            "Inline image toolbar floats inside the full image viewport without a reserved strip");
        Check(imageTools.Visibility == Microsoft.UI.Xaml.Visibility.Visible && imageTools.Opacity==1 && imageExpand.IsEnabled,
            "Entering screenshot preview exposes the existing image controls");
        imageViewport.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
        imageChrome.Reveal();
        await Until(() => !imageChrome.Visible);
        UpdateLayout();
        Check(Math.Abs(imageViewport.ActualHeight-passiveHeight)<1 && !imageTools.IsHitTestVisible,
            "Idle screenshot controls hide without resizing the canvas or retaining invisible hit targets");
        Check(MoveImageFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down),
            "Controller navigation reveals the hidden screenshot toolbar");
        imageChrome.Hide();
        Check(imageChrome.Visible && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot),imageFit),
            "Screenshot controls stay visible while their native control owns focus");
        imageViewport.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
        SetPoster(decodedImage);
        SetActive(false, false); await PlaybackRetirement;
        Check(image.Source is null && video.PosterSource is null && definition is null && resolved is null && live.Count == 0,
            "Screenshot withdrawal clears decoded image and poster pixels and releases its original-file demand");
        ApplyImage(imageDefinition);
        Check(imageReads == 1 && live.Count == 0, "Inactive screenshot declaration does not reacquire image or file resources");
        SetActive(true, false); ApplyImage(imageDefinition);
        await Until(() => image.Source is not null && IsImage);
        Check(imageReads == 2 && !ReferenceEquals(image.Source, decodedImage) && ResidentPlayers == baseline,
            "Screenshot reopening reacquires its current image without a cached native decoder");
        SetActive(false, false); await PlaybackRetirement;
        var delayedImage = new TaskCompletionSource<HostMediaPlayerSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? delayedImageId = null;
        SetActive(true, false);
        Apply(_ => throw new InvalidOperationException("Unexpected image resolution."), imageDefinition, () => true, owner,
            (id, _) => { delayedImageId = id; live.Add(id); return delayedImage.Task; }, Release);
        var nextImage = imageDefinition with { Source = MediaPlayerSource.FromSavedCapture(new string('d', 32), 1) };
        ApplyImage(nextImage); await PlaybackRetirement;
        delayedImage.TrySetResult(ImageSource(imageDefinition, delayedImageId!));
        await Until(() => image.Source is not null && definition == nextImage && IsImage);
        Check(live.Count == 1 && released.Contains(delayedImageId!) && ResidentPlayers == baseline,
            "Late screenshot A cannot replace screenshot B or retain its retired file demand");
        SetActive(false, false); await PlaybackRetirement;
        released.Clear();
        HostMediaPlayerSource Source(MediaPlayerDefinition definition, string id) => new(definition, new Uri(path).AbsoluteUri,
            Width: 1280, Height: 720, DurationSeconds: 5, ExpiresAtUnixMilliseconds: long.MaxValue) { PlaybackLeaseId = id };
        void ApplyImmediate(MediaPlayerDefinition definition) => Apply(_ => throw new InvalidOperationException("Saved preview bypassed its playback lease."),
            definition, () => true, owner, (id, _) => { live.Add(id); return Task.FromResult(Source(definition, id)); }, Release);
        SetPreviewPlayback(true); SetActive(true, false); ApplyImmediate(first);
        await Until(() => IsPlaying && player is { IsMuted: true });
        Check(ResidentPlayers == baseline + 1 && live.Count == 1, "One passive preview owns one native decoder and one playback lease");
        Check(player!.CommandManager.IsEnabled == false && player.SystemMediaTransportControls.IsEnabled == false,
            "Preview playback does not register automatic system media controls or hotkeys");
        Check(videoControls.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Passive preview hides inactive native controls");
        SetActive(true, true); ToggleMute();
        await Until(() => player is { IsMuted: false });
        Check(play.IsEnabled && position.IsEnabled, "Explicit preview entry retains normal play and seek controls");
        SetActive(true, false);
        Check(player!.IsMuted, "Leaving interactive preview immediately restores mute");
        SetActive(true, true); ToggleMute();
        Check(!player!.IsMuted, "Explicit user unmute remains available before withdrawal");
        SetActive(false, false); await PlaybackRetirement;
        Check(ResidentPlayers == baseline && live.Count == 0 && player is null, "Preview withdrawal drains decoder and playback lease");
        Check(playbackMemory?.Definition == first && playbackMemory.Definition.Source.Attachment is null,
            "Retained playback memory contains only declaration identity and scalar state");
        // Withdrawal now mutes before recording state. Also exercise restoration
        // of an older unmuted scalar snapshot, which must not bypass passive mute.
        playbackMemory = playbackMemory! with { Muted = false };
        SetActive(true, false); ApplyImmediate(first);
        await Until(() => IsPlaying && player is { IsMuted: true });
        Check(live.Count == 1 && released.Count == 1, "Reopening reacquires playback without restoring unmuted memory");
        SetActive(false, false); await PlaybackRetirement;

        var pending = new TaskCompletionSource<HostMediaPlayerSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? oldId = null;
        SetActive(true, false);
        Apply(_ => throw new InvalidOperationException("Unexpected unleased resolve."), first, () => true, owner,
            (id, _) => { oldId = id; live.Add(id); return pending.Task; }, Release);
        Check(oldId is not null, "Delayed preview A acquired a tracked demand");
        ApplyImmediate(second);
        await PlaybackRetirement;
        pending.TrySetResult(Source(first, oldId!));
        await Until(() => Definition?.Source == second.Source && IsPlaying);
        Check(player!.IsMuted && live.Count == 1 && ResidentPlayers == baseline + 1 && released.Contains(oldId!),
            "Late A cannot replace B, retain its lease or create another decoder");
        Dispose(); await PlaybackRetirement;
        Check(live.Count == 0 && ResidentPlayers == baseline, "Disposal drains the active preview decoder and file demand");
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        static async Task Until(Func<bool> ready)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!ready()) await Task.Delay(20, deadline.Token);
        }
    }
}
