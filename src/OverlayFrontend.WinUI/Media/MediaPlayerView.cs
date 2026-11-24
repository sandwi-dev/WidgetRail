using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Core;
using Windows.Media.Playback;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Shared native player; source resolution and authority are owned by the host.</summary>
internal sealed partial class MediaPlayerView : ContentControl, IDisposable
{
    private readonly Grid body = new() { RowSpacing = 8 };
    private readonly Grid mediaFrame = new();
    private readonly Border card = new() { Padding = new(8), BorderThickness = new(1) };
    private readonly Grid stage = new();
    private readonly MediaPlayerElement video = new() { AreTransportControlsEnabled = false, AutoPlay = false, Stretch = Stretch.Uniform };
    private readonly Image image = new() { Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid toolbar = new() { ColumnSpacing = 12, Padding = new(8) };
    private readonly Button play = new() { IsTabStop = true, UseSystemFocusVisuals = true, MinHeight = 44, Padding = new(12, 8, 12, 8) };
    private readonly SymbolIcon playIcon = new(Symbol.Play);
    private readonly TextBlock playLabel = new() { Text = "Play", VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider position = new() { Minimum = 0, Maximum = 1, StepFrequency = .1, SmallChange = .5, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock time = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 84, TextAlignment = TextAlignment.Right };
    private Control? rememberedControl;
    private readonly Button playbackOptions = new() { Content = new SymbolIcon(Symbol.More) };
    private readonly SymbolIcon muteIcon = new(Symbol.Volume);
    private readonly Button mute = new();
    private readonly Button expand = new() { Content = new SymbolIcon(Symbol.FullScreen) };
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, Value = 100, StepFrequency = 5, SmallChange = 5, LargeChange = 10, MinWidth = 48, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private Control[] controls = [];
    private bool refreshing, adjustingVolume, backOwned, chromeWasPlaying;
    private double requestedRate = 1;
    private object? resourceOwner;
    private sealed record PlaybackMemory(MediaPlayerDefinition Definition, double Position, double Volume, bool Muted, bool Loop, double Rate,
        bool AnimationWantsPlayback, bool AnimationManualPlayback);
    private PlaybackMemory? playbackMemory;
    private NativePublicMediaSource? publicMedia;
    private NativeDashMediaSource? dashMedia;
    private Control PreferredFocus => embeddedReady ? PreferredEmbeddedControl : IsInlinePoster ? inlinePlay :
        rememberedControl is { IsEnabled: true, Visibility: Visibility.Visible, IsLoaded: true } remembered &&
        (IsImage ? imageControls.Contains(remembered) && imageChrome.Visible : controls.Contains(remembered) || ReferenceEquals(remembered, position))
            ? remembered : IsImage ? (IsExpanded ? imageFit : imageViewport) : play;
    private MediaFullscreenLayer? expandedHost;
    internal bool IsAdjustingVolume => adjustingVolume;
    internal bool IsExpanded => expandedHost is not null;
    internal bool IsMovingPresentation => changingPlacement;
    internal bool CanExpand => IsExpanded || MediaFullscreenLayer.Find(this) is not null;
    private double Duration => player?.PlaybackSession.NaturalDuration.TotalSeconds is > 0 and var seconds && double.IsFinite(seconds) ? seconds : resolved?.DurationSeconds ?? 0;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private CancellationTokenSource lifetime = new();
    private IDisposable? theme;
    private Brush? normalBrush, normalButtonBrush, selectedBrush;
    private bool highContrastChrome;
    private static int residentPlayers;
    internal static int ResidentPlayers => Volatile.Read(ref residentPlayers);
    private bool ownsPlayerSlot, waitingForSlot;
    private bool imageReplacementPending;
    private bool parkingSourceFocus;
    internal bool CanControl => embeddedReady || IsInlinePoster || player is not null || IsImage && image.Source is not null;
    internal bool IsImage => !imageReplacementPending && resolved is { IsImage: true };
    private MediaPlayer? player;
    private MediaSource? source;
    private MediaPlayerDefinition? definition;
    private HostMediaPlayerSource? resolved;
    internal MediaPlayerDefinition? Definition => definition;
    private bool disposed, active = true, input, requestedInput, checkingAuthority;
    private Func<CancellationToken, Task<HostMediaPlayerSource>>? resolveAuthority;
    private Func<bool>? currentAuthority;
    private Func<string, CancellationToken, Task<HostMediaPlayerSource>>? resolvePlaybackLease;
    private Func<string, Task>? releasePlaybackLease;
    private string? playbackLeaseId;
    private Func<string, Task>? currentPlaybackRelease;
    private Task playbackRetirement = Task.CompletedTask;
    internal Task PlaybackRetirement => playbackRetirement;
    private bool previewPlayback;
    internal bool IsModalPlayback { get; set; }
    private bool systemControlsSuppressed;

    private void ApplySystemMediaPolicy(MediaPlayerDefinition media)
    {
        // Fullscreen reuses this player and its originating playback role.
        bool suppress = previewPlayback || IsModalPlayback || galleryDeclaration is not null ||
            media.Presentation == MediaPlayerPresentation.Inline || media.Options.IsAnimation ||
            media.Source.Kind is MediaPlayerSourceKind.Capture or MediaPlayerSourceKind.SavedCapture;
        if (player is null || systemControlsSuppressed == suppress) return;
        player.CommandManager.IsEnabled = !suppress;
        player.SystemMediaTransportControls.IsEnabled = !suppress;
        systemControlsSuppressed = suppress;
    }
    internal Func<bool>? RequestPreviewInteraction { get; set; }
    private bool ForcePreviewMute => previewPlayback && !input;
    internal void SetPreviewPlayback(bool enabled)
    {
        previewPlayback = enabled;
        if (ForcePreviewMute && player is not null) player.IsMuted = true;
    }
    internal void SetPoster(ImageSource? poster)
    {
        // Artwork demand owns resolution, size and stale-result rejection. The
        // platform player owns the poster-to-video loading transition.
        if (disposed || lifetime.IsCancellationRequested && !IsInlinePoster) return;
        video.PosterSource = poster;
        if (IsImage) return;
        if (poster is not null)
        { video.Visibility = Visibility.Visible; HideMediaStatus(); }
        else if (player is null)
        { video.Visibility = Visibility.Collapsed; ShowLoading(!PassiveAnimation && (!IsInlineVideo || inlineLoadStarted)); }
    }
    private long nextAuthorityCheck;
    internal bool IsVideo => resolved is { IsImage: false };
    internal Action<bool>? AudioChanged { get; set; }
    private bool reportedAudio;
    private void ReportAudio()
    {
        var playing = !disposed && IsPlaying && player is { IsMuted: false, Volume: > 0 };
        if (playing == reportedAudio) return;
        reportedAudio = playing; AudioChanged?.Invoke(playing);
    }
    internal bool IsPlaying => player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    private bool IsEnded => player?.PlaybackSession.CanSeek == true && !IsPlaying && Duration > 0 && player.PlaybackSession.Position.TotalSeconds >= Duration - .05;
    internal string PrimaryActionLabel => GalleryActionLabel ?? (HasOptionsMenu ? "Choose" : IsImage ? ImageActionLabel : XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Control focused && !ReferenceEquals(focused, play) && !ReferenceEquals(focused, position)
        ? ReferenceEquals(focused, volume) ? adjustingVolume ? "Done" : "Adjust volume" : AutomationProperties.GetName(focused)
        : IsEnded ? "Replay" : IsPlaying ? "Pause" : "Play");
    internal Action? InteractionChanged { get; set; }
    // Resolve authority in parallel, but keep decoder creation and its first
    // layout out of the page animation's UI-thread frame budget.
    internal Task PresentationReady { private get; set; } = Task.CompletedTask;
    internal MediaPlayerView()
    {
        AutomationProperties.SetAutomationId(status, "MediaPlayer.Status");
        AutomationProperties.SetAutomationId(loading, "MediaPlayer.Loading");
        AutomationProperties.SetName(loading, "Loading media");
        NativeMotionVisibility.Prepare(image);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        InitializeImages();
        AutomationProperties.SetAutomationId(feedback, "MediaPlayer.Feedback");
        feedback.Children.Add(loading); feedback.Children.Add(status);
        stage.Children.Add(imageViewport); stage.Children.Add(video); stage.Children.Add(feedback); body.Children.Add(stage); Grid.SetRowSpan(stage, 2);
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowSpacing = 8;
        for (var i = 0; i < 5; i++) toolbar.ColumnDefinitions.Add(new() { Width = i == 2 ? new(1, GridUnitType.Star) : GridLength.Auto });
        mute.HorizontalAlignment = HorizontalAlignment.Right;
        mute.Content = muteIcon;
        var playContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        playContent.Children.Add(playIcon); playContent.Children.Add(playLabel); play.Content = playContent;
        var timeline = new Grid { ColumnSpacing = 12 };
        timeline.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); timeline.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        timeline.Children.Add(position); Grid.SetColumn(time, 1); timeline.Children.Add(time); Grid.SetColumnSpan(timeline, 5); toolbar.Children.Add(timeline);
        controls = [play, mute, volume, playbackOptions, expand];
        for (var i = 0; i < controls.Length; i++)
        {
            Grid.SetColumn(controls[i], i); Grid.SetRow(controls[i], 1); toolbar.Children.Add(controls[i]);
            var control = controls[i];
            control.GotFocus += (_, _) => { seekAcceleration.Reset(); if (XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), control)) rememberedControl = control; InteractionChanged?.Invoke(); };
            controls[i].UseSystemFocusVisuals = true; controls[i].IsTabStop = true;
            if (control is Button button) { button.MinHeight = 44; button.MinWidth = 44; button.Padding = new(8); }
        }
        position.IsTabStop = true; position.UseSystemFocusVisuals = true;
        position.GotFocus += (_, _) => seekAcceleration.Reset();
        position.PointerPressed += (_, _) => seekAcceleration.Reset();
        position.ThumbToolTipValueConverter = new PlaybackTimeConverter();
        AutomationProperties.SetName(position, "Playback position"); AutomationProperties.SetAutomationId(position, "MediaPlayer.Position");
        AutomationProperties.SetName(volume, "Volume"); AutomationProperties.SetAutomationId(volume, "MediaPlayer.Volume");
        Label(playbackOptions, "Playback options", "MediaPlayer.Options");
        Label(mute, "Mute", "MediaPlayer.Mute"); Label(expand, "Expand player", "MediaPlayer.Expand");
        playbackOptions.Click += (_, _) => OpenPlaybackOptions(); mute.Click += (_, _) => ToggleMute();
        expand.Click += async (_, _) => await ToggleExpandedAsync();
        position.ValueChanged += (_, _) =>
        {
            if (refreshing || !input || player?.PlaybackSession.CanSeek != true) return;
            player.PlaybackSession.Position = TimeSpan.FromSeconds(position.Value);
            ShowMediaFeedback("Seek " + FormatTime(position.Value));
        };
        volume.ValueChanged += (_, _) =>
        {
            if (refreshing || !input || player is null) return;
            player.Volume = volume.Value / 100; player.IsMuted = false;
            RememberAudioPreferences();
            ShowMediaFeedback("Volume " + Math.Round(volume.Value) + "%");
        };
        toolbar.VerticalAlignment = VerticalAlignment.Bottom;
        videoControls.Children.Add(toolbar);
        inlineChrome.Children.Add(videoControls); Grid.SetRowSpan(inlineChrome, 2);
        body.Children.Add(inlineChrome);
        mediaFrame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        mediaFrame.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        mediaFrame.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(body, 1); mediaFrame.Children.Add(body); card.Child = mediaFrame; Content = card;
        Grid.SetRow(imageTools, 1); imageTools.VerticalAlignment = VerticalAlignment.Bottom; imageTools.Margin = new(8); mediaFrame.Children.Add(imageTools);
        InitializeMediaChrome();
        InitializeGallery();
        ObserveMediaPlacement(this);
        InitializeInlineVideo();

        AutomationProperties.SetName(play, "Play or pause");
        AutomationProperties.SetAutomationId(play, "MediaPlayer.Play");
        play.Click += (_, _) => Toggle();
        card.SizeChanged += (_, _) => ReflowTransport();
        GotFocus += (_, _) =>
        {
            if (changingPlacement || parkingSourceFocus) return;
            var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
            if (input && !IsExpanded && focused is Control && !ReferenceEquals(focused, this))
                rememberedThumbnail = FocusedThumbnail?.Tag as string;
            if (focused is Control control && controls.Contains(control)) rememberedControl = control;
            else if (ReferenceEquals(focused, this) && input) FocusMediaEntry();
            InteractionChanged?.Invoke();
        };
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { theme?.Dispose(); theme = NativePopupTheme.MediaPlayer(this); RefreshImageChrome(); RefreshLoadingActivity(); if (active && !retainedForPageExit) timer.Start(); };
        Unloaded += (_, _) => { ForgetInlineResume(); loading.IsActive = false; timer.Stop(); StopMediaChrome(); if (!retainedForPageExit) { player?.Pause(); } theme?.Dispose(); theme = null; };
    }
    internal void SetActive(bool value, bool acceptsInput)
    {
        var previouslyInteractive = input;
        requestedInput = acceptsInput;
        active = value; input = value && acceptsInput && !editorPreviewPaused && !PassiveAnimation;
        if (previouslyInteractive && !input) ResetMediaEntry();
        RefreshLoadingActivity();
        if (!input || !previouslyInteractive) seekAcceleration.Reset();
        if (!input) ClearPendingImageViewportRestore();
        if (ForcePreviewMute && player is not null) player.IsMuted = true;
        imageViewport.IsHitTestVisible = input && IsImage;
        imageViewport.IsEnabled = input && IsImage;
        foreach (var control in imageControls) control.IsEnabled = input && IsImage;
        foreach (var control in controls) control.IsEnabled = input && player is not null;
        video.IsHitTestVisible = input; video.IsTabStop = input && IsVideo;
        if (!input) { adjustingVolume = false; ClosePlaybackOptions(); expandedHost?.Close(restoreFocus: false); }
        if (!active)
        {
            StopGalleryImageTransition();
            CloseEmbedded();
            ForgetInlineResume();
            if (IsInlineVideo) SuspendInlineVideo();
            else if (PassiveAnimation) animationVisibility?.Refresh(); else SuspendPlayer();
            timer.Stop(); expandedHost?.Close(restoreFocus: false);
            if (previewPlayback)
            {
                image.Source = null; video.PosterSource = null;
                imageViewport.Visibility = imageTools.Visibility = Visibility.Collapsed;
            }
        }
        else if (IsLoaded && !lifetime.IsCancellationRequested) timer.Start();
        expand.IsEnabled = input && player is not null && CanExpand;
        imageExpand.IsEnabled = input && IsImage && CanExpand;
        position.IsEnabled = input && player?.PlaybackSession.CanSeek == true;
        if (IsVideo && (previewPlayback || PassiveAnimation) && !input) { StopMediaChrome(); videoControls.Visibility = Visibility.Collapsed; }
        else if (IsVideo && input && !previouslyInteractive) videoChrome.Reveal();
        if (!IsVideo) videoControls.Visibility = Visibility.Collapsed;
        RefreshImageChrome();
        imageAnimation?.Refresh(); RefreshImageAnimationControl();
        animationVisibility?.Refresh();
        RefreshInlineVideo();
        inlineChrome.IsHitTestVisible = input;
        RefreshGalleryControls();
        RefreshEmbedded();
        if (input && XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this))
            FocusMediaEntry();
    }
    private void ApplySource(Func<CancellationToken, Task<HostMediaPlayerSource>> resolve, MediaPlayerDefinition next, Func<bool> current, object? owner = null,
        Func<string, CancellationToken, Task<HostMediaPlayerSource>>? resolveOwned = null, Func<string, Task>? releaseOwned = null,
        ImageSource? initialPoster = null, bool preserveExpanded = false)
    {
        if (disposed || previewPlayback && !active) return;
        var keepGalleryImage = CanBridgeGallerySelection(owner);
        var retainImage = keepGalleryImage || (galleryDeclaration is null || galleryDeclaration.InlineCarousel && initialPoster is null) &&
            Equals(resourceOwner, owner) && image.Source is BitmapImage { PixelWidth: > 0 } &&
            (next.Source.Attachment?.ContentType == "image/png" || next.Source.Kind == MediaPlayerSourceKind.WebImage);
        if (!Equals(resourceOwner, owner))
        { resourceOwner = owner; playbackMemory = null; definition = null; lifetime.Cancel(); ReleasePlayer(); video.PosterSource = null; }
        resolveAuthority = resolve; currentAuthority = current;
        resolvePlaybackLease = resolveOwned; releasePlaybackLease = releaseOwned;
        if (next == definition)
        {
            ApplySystemMediaPolicy(next);
            return;
        }
        ForgetInlineResume();
        if (definition is { } previous && previous.Source == next.Source && previous.Revision == next.Revision &&
            previous.Presentation == next.Presentation && previous.Options.IsAnimation == next.Options.IsAnimation &&
            (player is not null || image.Source is not null && !imageReplacementPending))
        { definition = next; if (resolved is not null) resolved = resolved with { Media = next }; if (player is not null) { ApplySystemMediaPolicy(next); ApplyOptions(next.Options); } return; }
        if (!preserveExpanded) expandedHost?.Close(restoreFocus: false);
        ParkSourceFocus();
        ResetMediaEntry();
        ClearPendingImageViewportRestore();
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        ReleasePlayer(); definition = next; resolved = null; adjustingVolume = false; waitingForSlot = false;
        ConfigureAnimation(next);
        ConfigureInlineVideo();
        RefreshGalleryFullscreen();
        seekAcceleration.Reset(); feedbackUntil = 0;
        video.PosterSource = initialPoster;
        // A decoded frame may bridge an in-place image replacement within the
        // same owner. It is presentation only: input and authority belong to the
        // new source, and failed resolution clears it. Never retain across owners.
        imageReplacementPending = true;
        if (!retainImage) { displayedGalleryImage = null; image.Source = null; imageViewport.Visibility = imageTools.Visibility = Visibility.Collapsed; }
        video.Visibility = video.PosterSource is not null ? Visibility.Visible : Visibility.Collapsed;
        toolbar.Visibility = videoControls.Visibility = Visibility.Collapsed;
        rememberedControl = null;
        ShowLoading(!PassiveAnimation && !retainImage && video.PosterSource is null);
        SetActive(active, requestedInput);
        if (retainImage) _ = ShowImageLoadingAsync(next, keepGalleryImage, lifetime.Token);
        if (!IsInlineVideo && next.Source.Kind == MediaPlayerSourceKind.SavedCapture && resolveOwned is not null && releaseOwned is not null)
        { playbackLeaseId = Guid.NewGuid().ToString("N"); currentPlaybackRelease = releaseOwned; }
        if (next.Source.Kind == MediaPlayerSourceKind.EmbeddedSession) OpenEmbedded();
        else if (IsInlineVideo) RefreshInlineVideo();
        else if (PassiveAnimation) animationVisibility?.Refresh();
        else _ = LoadAsync(playbackLeaseId is { } owned ? token => resolveOwned!(owned, token) : resolve, next, current, lifetime.Token);
    }
    private void ParkSourceFocus()
    {
        if (!input || XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not Control focused ||
            !(ReferenceEquals(focused,inlinePlay)||ReferenceEquals(focused,imageViewport)||ReferenceEquals(focused,video)||ReferenceEquals(focused,position)||
                controls.Contains(focused)||imageControls.Contains(focused)||galleryButtons.Contains(focused)||
                ReferenceEquals(focused,embeddedWake)||embeddedControls.Contains(focused))) return;
        // Native child teardown can otherwise move focus outside an entered
        // preview before its replacement loads. Keep it on the stable media
        // owner; SetActive restores ready controls only if we still own focus.
        var tabStop=IsTabStop;
        parkingSourceFocus=true;
        try { IsTabStop=true; Focus(FocusState.Programmatic); }
        finally { IsTabStop=tabStop; parkingSourceFocus=false; }
    }
    private async Task ShowImageLoadingAsync(MediaPlayerDefinition expected, bool keepGalleryImage, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            if (disposed || token.IsCancellationRequested || definition != expected || !imageReplacementPending) return;
            if (!keepGalleryImage || galleryCurrent?.Invoke() != true) image.Source = null;
            ShowLoading();
            // A retained image remains subject to its own authorization deadline.
            if (keepGalleryImage && galleryImageExpiry != long.MaxValue)
            {
                while (imageReplacementPending && definition == expected && !token.IsCancellationRequested)
                {
                    var remaining = galleryImageExpiry - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (remaining <= 0) { image.Source = null; return; }
                    await Task.Delay((int)Math.Min(remaining, int.MaxValue), token);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task LoadAsync(Func<CancellationToken, Task<HostMediaPlayerSource>> resolve, MediaPlayerDefinition next, Func<bool> current, CancellationToken token)
    {
        try
        {
            var value = await resolve(token);
            await PresentationReady.WaitAsync(token);
            if (disposed || retainedForPageExit || token.IsCancellationRequested || definition != next) return;
            if (!current()) { EndUnavailableSource(showFailure:false); return; }
            if (value.Media != next || value.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidOperationException("Media expired.");
            resolved = value;
            var uri = new Uri(value.Uri);
            if (IsVideo)
            {
                imageReplacementPending = false;
                image.Source = null; imageViewport.Visibility = imageTools.Visibility = Visibility.Collapsed;
                if (!TryAcquirePlayerSlot())
                {
                    waitingForSlot = true; ShowMediaStatus("Close another video before opening this one.", !PassiveAnimation);
                    return;
                }
#if ENABLE_WIDGET_VALIDATION
                uri = ValidationVideoSource?.Invoke(uri) ?? uri;
#endif
                if (next.Source.IsPublicMedia)
                {
                    if (uri.AbsoluteUri != new Uri(next.Source.Location!).AbsoluteUri)
                        throw new InvalidOperationException("Public media authority changed.");
                    var acquisition = AttachPublicSourceAsync(uri, next, current, token);
                    playbackRetirement = Task.WhenAll(playbackRetirement, ObservePublicAcquisitionAsync(acquisition));
                    if (!await acquisition) return;
                }
                else source = MediaSource.CreateFromUri(uri);
                // Keep the existing unavailable-controls state while checked
                // acquisition/metadata is pending; no second readiness flag.
                player = new MediaPlayer { AutoPlay = false };
                systemControlsSuppressed = false;
                ApplySystemMediaPolicy(next);
                ApplyOptions(next.Options);
                player.MediaFailed += (failedPlayer, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (!disposed && !token.IsCancellationRequested && ReferenceEquals(player,failedPlayer))
                        EndUnavailableSource(current());
                });
                player.MediaOpened += (openedPlayer, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (disposed || retainedForPageExit || token.IsCancellationRequested || !ReferenceEquals(player,openedPlayer)) return;
                    if (!current()) { EndUnavailableSource(showFailure:false); return; }
                    SizeGalleryVideo();
                    if (player is not null) player.PlaybackSession.PlaybackRate = requestedRate;
                    HideMediaStatus();
                    if (playbackMemory is { } saved && definition is { } latest && saved.Definition.Source == latest.Source && saved.Definition.Revision == latest.Revision && player is { } resumed)
                    {
                        resumed.Volume = latest.Options.Volume == saved.Definition.Options.Volume ? saved.Volume : latest.Options.Volume;
                        resumed.IsMuted = ForcePreviewMute || (latest.Options.Muted == saved.Definition.Options.Muted ? saved.Muted : latest.Options.Muted);
                        resumed.IsLoopingEnabled = latest.Options.Loop == saved.Definition.Options.Loop ? saved.Loop : latest.Options.Loop;
                        requestedRate = latest.Options.PlaybackRate == saved.Definition.Options.PlaybackRate ? saved.Rate : latest.Options.PlaybackRate;
                        resumed.PlaybackSession.PlaybackRate = requestedRate;
                        if (resumed.PlaybackSession.CanSeek) resumed.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(saved.Position, 0, Duration));
                    }
                    ApplyAudioPreferences(next.Options, useDefaults: false);
                    if (!next.Options.IsAnimation && (playbackMemory is null || previewPlayback) && definition?.Options.AutoPlay == true && active && !editorPreviewPaused) player?.Play();
                    if (IsInlineVideo && inlinePlayRequested && active && !editorPreviewPaused) player?.Play();
                    inlinePlayRequested = false;
                    animationVisibility?.Refresh();
                    playbackMemory = null;
                    SetActive(active, requestedInput);
                    Refresh(); InteractionChanged?.Invoke();
                });
                player.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!disposed && !token.IsCancellationRequested) { Refresh(); InteractionChanged?.Invoke(); } });
                player.Source = source;
                if (galleryVideoFit is not null)
                    player.PlaybackSession.NaturalVideoSizeChanged += (session, _) => DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!disposed && !token.IsCancellationRequested && session == player?.PlaybackSession)
                            SizeGalleryVideo();
                    });
                video.SetMediaPlayer(player);
                RefreshInlineVideo();
                video.Visibility = Visibility.Visible;
                toolbar.Visibility = PassiveAnimation ? Visibility.Collapsed : Visibility.Visible;
                if (input && IsInlineVideo && inlinePlayRequested) videoChrome.Enter();
                else if (input) videoChrome.Reveal();
            }
            else
            {
                BitmapImage bitmap;
                if (next.Source.Kind == MediaPlayerSourceKind.WebImage)
                {
                    // The authored source and resolved authority must identify the
                    // same HTTPS image. Use the existing size/stream bounds; URL
                    // extensions and advertised provider dimensions are not trusted.
                    if (uri.Scheme != Uri.UriSchemeHttps || uri.AbsoluteUri != new Uri(next.Source.Location!).AbsoluteUri)
                        throw new InvalidOperationException("Remote image authority changed.");
                    var payload = new NativeArtworkPayload(ReadOnlyMemory<byte>.Empty, uri, IsPublicMedia: next.Source.IsPublicMedia);
#if ENABLE_WIDGET_VALIDATION
                    payload = ValidationImagePayload?.Invoke(uri) ?? payload;
#endif
                    var decoded = await NativeArtworkDecoder.DecodeAsync(payload,
                        new(0, 0), ImageFit.Contain, token, naturalSize: true, autoPlay: false)
                        ?? throw new InvalidDataException("Image is unavailable.");
                    bitmap = decoded.Image;
                    value = value with { Width = decoded.Source.Width, Height = decoded.Source.Height };
                }
                else
                {
                    bitmap = new BitmapImage { AutoPlay = false, DecodePixelWidth = value.Width > 0 ? value.Width : 1920 };
                    // Capture images resolve to host-authorized local files. Decode
                    // offscreen before publishing the new bitmap instead of exposing
                    // an empty Image while UriSource completes on a later frame.
                    if (!uri.IsFile) throw new InvalidOperationException("Capture image must resolve to a local file.");
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(uri.LocalPath).AsTask(token);
                    using (var stream = await file.OpenReadAsync().AsTask(token))
                        await bitmap.SetSourceAsync(stream).AsTask(token);
                }
                if (disposed || retainedForPageExit || token.IsCancellationRequested || definition != next) return;
                if (!current()) { EndUnavailableSource(showFailure:false); return; }
                if (value.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidOperationException("Media expired.");
                resolved = value;
                video.Visibility = Visibility.Collapsed;
                imageReplacementPending = false;
                PresentGalleryImage(bitmap);
                StartImageAnimation(bitmap);
                imageViewport.Visibility = Visibility.Visible;
                RefreshImageChrome();
                FitImage();
            }
            if (!IsVideo) HideMediaStatus();
            SetActive(active, requestedInput); Refresh();
            RefreshGalleryFullscreen();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!disposed && !token.IsCancellationRequested) EndUnavailableSource(current());
        }
    }
    private void ApplyOptions(MediaPlayerOptions options)
    {
        if (player is null) return;
        ApplyAudioPreferences(options); player.IsLoopingEnabled = options.Loop;
        requestedRate = options.PlaybackRate; player.PlaybackSession.PlaybackRate = requestedRate;
    }
    private async Task<bool> AttachPublicSourceAsync(Uri uri, MediaPlayerDefinition expected, Func<bool> current, CancellationToken token)
    {
        if (expected.Source.IsAdaptiveDash)
        {
            var dash = await NativeDashMediaSource.OpenAsync(uri, token);
            if (disposed || retainedForPageExit || token.IsCancellationRequested || definition != expected || !current())
            { await dash.DisposeAsync(); return false; }
            dashMedia = dash;
            source = dash.Source;
            return true;
        }
        var acquired = await NativePublicMediaSource.OpenAsync(uri, token, IsInlineVideo ? TakeInlineResume(resolved) : null);
        var transferred = false;
        try
        {
            if (disposed || retainedForPageExit || token.IsCancellationRequested || definition != expected) return false;
            if (!current()) { EndUnavailableSource(showFailure:false); return false; }
            if (resolved?.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            { EndUnavailableSource(showFailure:true); return false; }
            publicMedia = acquired;
            source = acquired.Source;
            transferred = true;
            return true;
        }
        finally { if (!transferred) await acquired.DisposeAsync(); }
    }
    private static async Task ObservePublicAcquisitionAsync(Task acquisition)
    {
        // LoadAsync owns error presentation. Retirement waits for late acquisition
        // cleanup without turning an ordinary unavailable attachment into a
        // persistent teardown fault.
        try { await acquisition; }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }
    private void EndUnavailableSource(bool showFailure)
    {
        ForgetInlineResume();
        showFailure &= active && !retainedForPageExit;
        if (showFailure && AdvancePastUnavailableItem()) return;
        if (!showFailure)
        {
            // A recycled row or outgoing section no longer owns these pixels.
            // A subsequent binding of the same source must be able to start afresh.
            definition=null; resolved=null; playbackMemory=null; currentAuthority=null; resolveAuthority=null;
        }
        inlineFailed = showFailure; inlineLoadStarted = inlinePlayRequested = false; inlinePlay.Visibility = Visibility.Collapsed;
        ClearPendingImageViewportRestore(); lifetime.Cancel(); timer.Stop(); ReleasePlayer(); video.PosterSource = null;
        imageReplacementPending = false; image.Source = null; imageViewport.Visibility = imageTools.Visibility = Visibility.Collapsed;
        video.Visibility = toolbar.Visibility = videoControls.Visibility = Visibility.Collapsed;
        if (showFailure && exhaustedGallery)
        {
            HideMediaStatus();
            expandedHost?.Close(restoreFocus: false);
            ClearThumbnails();
            Visibility = Visibility.Collapsed;
        }
        else if(showFailure) ShowMediaStatus("This media could not be opened. The source may be unavailable or its format unsupported.");
        else HideMediaStatus();
    }
    private void Refresh()
    {
        if (embeddedViewport is not null) { RefreshEmbedded(); return; }
        animationVisibility?.Refresh();
        RefreshImageAnimationControl();
        ReportAudio();
        if (disposed || retainedForPageExit) return;
        if (currentAuthority?.Invoke() == false) { EndUnavailableSource(showFailure:false); return; }
        if (resolved?.RequiresRevalidation == true && !checkingAuthority && Environment.TickCount64 >= nextAuthorityCheck && resolveAuthority is not null) _ = RefreshAuthorityAsync(lifetime.Token);
        if (resolved?.ExpiresAtUnixMilliseconds is { } expiry && expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { EndUnavailableSource(showFailure:true); return; }
        if (waitingForSlot && PassiveAnimation && active && ResidentAnimations < 2)
        { waitingForSlot = false; animationDemandStarted = false; animationVisibility?.Refresh(); return; }
        if (waitingForSlot && !PassiveAnimation && active && ResidentPlayers < 4 && definition is { } waiting && resolveAuthority is { } resolver && currentAuthority is { } authority)
        {
            waitingForSlot = false;
            if (IsInlineVideo) { inlineLoadStarted = false; StartInlineVideo(); }
            else { definition = null; Apply(resolver, waiting, authority, resourceOwner, resolvePlaybackLease, releasePlaybackLease); }
            return;
        }
        if (player is not { } value) return;
        RefreshEditRange();
        var seconds = Math.Max(0, value.PlaybackSession.Position.TotalSeconds);
        var duration = Duration;
        playLabel.Text = IsEnded ? "Replay" : IsPlaying ? "Pause" : "Play"; playIcon.Symbol = IsEnded ? Symbol.Refresh : IsPlaying ? Symbol.Pause : Symbol.Play;
        if (input && IsPlaying != chromeWasPlaying) videoChrome.Reveal();
        chromeWasPlaying = IsPlaying;
        AutomationProperties.SetName(play, playLabel.Text);
        NativePopupTheme.SetToolTip(play, playLabel.Visibility == Visibility.Collapsed ? playLabel.Text : null);
        refreshing = true;
        try
        {
            position.Maximum = Math.Max(duration, 1);
            if (!Input.PointerCaptureLifetime.HasCaptureWithin(position)) position.Value = Math.Clamp(seconds, 0, position.Maximum);
            if (!Input.PointerCaptureLifetime.HasCaptureWithin(volume)) volume.Value = value.Volume * 100;
        }
        finally { refreshing = false; }
        position.IsEnabled = input && value.PlaybackSession.CanSeek;
        mute.Background = value.IsMuted ? selectedBrush : normalButtonBrush;
        muteIcon.Symbol = value.IsMuted ? Symbol.Mute : Symbol.Volume;
        AutomationProperties.SetName(mute, value.IsMuted ? "Unmute" : "Mute");
        NativePopupTheme.SetToolTip(mute, value.IsMuted ? "Unmute" : "Mute");
        if (value.PlaybackSession.PlaybackState == MediaPlaybackState.Buffering) time.Text = "Buffering…";
        else if (Environment.TickCount64 >= feedbackUntil)
            time.Text = FormatTime(seconds) + " / " + (duration > 0 ? FormatTime(duration) : "Live");
    }
    private async Task RefreshAuthorityAsync(CancellationToken token)
    {
        var expected = definition;
        checkingAuthority = true; nextAuthorityCheck = Environment.TickCount64 + 2000;
        try
        {
            var value = playbackLeaseId is { } lease && resolvePlaybackLease is { } owned
                ? await owned(lease, token) : await resolveAuthority!(token);
            if (!disposed && !token.IsCancellationRequested && definition == expected)
            {
                if (value.Media != expected || resolved is not { } prior || value.Uri != prior.Uri ||
                    value.ExpiresAtUnixMilliseconds is { } expiry && expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    EndUnavailableSource(currentAuthority?.Invoke()==true);
                else resolved = value; // Existing authority probes renew a retained library playback lease.
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!disposed && !token.IsCancellationRequested && definition == expected) EndUnavailableSource(currentAuthority?.Invoke()==true);
        }
        finally { checkingAuthority = false; }
    }
    private static string FormatTime(double value) => TimeSpan.FromSeconds(value).ToString(value >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static void Label(Button button, string name, string id, bool showToolTip = true)
    {
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, id);
        NativePopupTheme.SetToolTip(button, showToolTip ? name : null);
    }
    internal void Toggle()
    {
        if (IsInlinePoster) { StartInlineVideo(); return; }
        if (!input || player is null) return;
        if (definition?.Options.IsAnimation == true)
        {
            animationWantsPlayback = !IsPlaying;
            animationManualPlayback = animationWantsPlayback;
            animationVisibility?.Refresh(); Refresh(); return;
        }
        if (IsPlaying) player.Pause();
        else { if (player.PlaybackSession.CanSeek && Duration > 0 && player.PlaybackSession.Position.TotalSeconds >= Duration - .05) player.PlaybackSession.Position = TimeSpan.Zero; player.Play(); }
        Refresh();
    }
    internal void ActivateFocusedControl()
    {
        if (!input || XamlRoot is null) return;
        if (ActivateGalleryControl()) return;
        if (IsVideo && RevealVideoControls()) return;
        if (embeddedReady)
        {
            if (RevealEmbeddedControls(enter: true)) return;
            var target = FocusManager.GetFocusedElement(XamlRoot);
            if (ReferenceEquals(target, embeddedControls[1])) EmbeddedCommand(EmbeddedMediaCommand.ToggleMute);
            else if (ReferenceEquals(target, embeddedControls[2])) _ = ToggleExpandedAsync();
            else EmbeddedCommand(EmbeddedMediaCommand.TogglePlayback);
            return;
        }
        if (IsImage) { ActivateImageControl(); return; }
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, playbackOptions)) OpenPlaybackOptions();
        else if (ReferenceEquals(focused, mute)) ToggleMute();
        else if (ReferenceEquals(focused, expand)) _ = ToggleExpandedAsync();
        else if (ReferenceEquals(focused, volume)) { adjustingVolume = !adjustingVolume; InteractionChanged?.Invoke(); }
        else Toggle();
    }
    internal bool MoveFocus(FocusNavigationDirection direction, bool repeated = false)
    {
        if (HasOptionsMenu) { optionsNavigation?.Move(direction); return true; }
        if (embeddedReady && embeddedWake is not null && XamlRoot is not null &&
            ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), embeddedWake) && RevealEmbeddedControls(enter: true)) return true;
        if (XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), video) && RevealVideoControls()) return true;
        if (MoveGalleryFocus(direction)) return true;
        if (embeddedReady) { RevealEmbeddedControls(); return MoveEmbeddedFocus(direction); }
        if (IsImage) return MoveImageFocus(direction);
        if (!input || player is null || XamlRoot is null) return false;
        if (RevealVideoControls()) return true;
        videoChrome.Reveal();
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, video)) return position.Focus(FocusState.Keyboard);
        if (ReferenceEquals(focused, position))
        {
            if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right) { SeekWithRepeat(direction == FocusNavigationDirection.Left ? -1 : 1, repeated); return true; }
            if (direction == FocusNavigationDirection.Down) return PreferredFocus.Focus(FocusState.Keyboard);
            if (IsExpanded && direction == FocusNavigationDirection.Up) return video.Focus(FocusState.Keyboard);
            return IsExpanded;
        }
        seekAcceleration.Reset();
        if (direction == FocusNavigationDirection.Up && position.IsEnabled) { adjustingVolume = false; return position.Focus(FocusState.Keyboard); }
        if (direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right)) { adjustingVolume = false; return IsExpanded; }
        if (ReferenceEquals(focused, volume) && adjustingVolume)
        { volume.Value = Math.Clamp(volume.Value + (direction == FocusNavigationDirection.Left ? -5 : 5), 0, 100); return true; }
        var available = controls.Where(control => control.IsEnabled && control.Visibility == Visibility.Visible).ToArray();
        var index = Array.IndexOf(available, focused as Control);
        var next = index + (direction == FocusNavigationDirection.Left ? -1 : 1);
        return next >= 0 && next < available.Length ? available[next].Focus(FocusState.Keyboard) : IsExpanded;
    }
    private double SeekStep => Duration is > 0 and <= 10 ? .5 : 5;
    private void Seek(double seconds)
    {
        if (!input || player?.PlaybackSession.CanSeek != true) return;
        var target = Math.Clamp(player.PlaybackSession.Position.TotalSeconds + seconds, 0, Duration);
        player.PlaybackSession.Position = TimeSpan.FromSeconds(target);
        ShowMediaFeedback("Seek " + FormatTime(target));
    }
    private void Replay()
    {
        if (!input || player?.PlaybackSession.CanSeek != true) return;
        player.PlaybackSession.Position = TimeSpan.Zero;
        if (definition?.Options.IsAnimation == true)
        { animationWantsPlayback = animationManualPlayback = true; animationVisibility?.Refresh(); }
        else player.Play();
    }
    private void ToggleLoop() { if (!input || player is null) return; player.IsLoopingEnabled = !player.IsLoopingEnabled; Refresh(); InteractionChanged?.Invoke(); }
    private void ToggleMute() { if (!input || player is null) return; player.IsMuted = !player.IsMuted; RememberAudioPreferences(); Refresh(); InteractionChanged?.Invoke(); }
    internal bool Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase == ControllerEventPhase.Released || button is not (ControllerButton.LeftTrigger or ControllerButton.RightTrigger)) seekAcceleration.Reset();
        if (button == ControllerButton.B && backOwned) { if (phase == ControllerEventPhase.Released) backOwned = false; return true; }
        if (input && UsesInlineThumbnailNavigation && !HasOptionsMenu && button is ControllerButton.LeftBumper or ControllerButton.RightBumper)
            return false;
        if (input && HasGallery && !HasOptionsMenu && button is ControllerButton.LeftBumper or ControllerButton.RightBumper)
        {
            if (phase == ControllerEventPhase.Pressed) SelectGalleryOffset(button == ControllerButton.LeftBumper ? -1 : 1);
            return true;
        }
        if (input && HasGallery && phase == ControllerEventPhase.Pressed && button == ControllerButton.A && ActivateGalleryControl()) return true;
        if (input && IsExpanded && !CanControl && button == ControllerButton.B)
        { if (phase == ControllerEventPhase.Pressed) { backOwned = true; expandedHost?.Close(); } return true; }
        if (input && IsInlinePoster)
        {
            if (button is not (ControllerButton.A or ControllerButton.X)) return false;
            if (phase == ControllerEventPhase.Pressed) StartInlineVideo();
            return true;
        }
        if (!input || !CanControl) return false;
        if (HasOptionsMenu)
        {
            if (phase == ControllerEventPhase.Pressed)
            {
                if (button == ControllerButton.B) { backOwned = true; if (optionsNavigation?.Back() != true) ClosePlaybackOptions(restoreFocus: true); }
                else if (button == ControllerButton.A) _ = optionsNavigation?.ActivateAsync();
            }
            return true;
        }
        if (button == ControllerButton.B && IsExpanded && !adjustingVolume)
        { if (phase == ControllerEventPhase.Pressed) expandedHost?.Close(); return true; }
        if (button == ControllerButton.B && adjustingVolume)
        {
            if (phase == ControllerEventPhase.Pressed) { backOwned = true; if (adjustingVolume) adjustingVolume = false; else expandedHost?.Close(); }
            return true;
        }
        if (IsImage) return HandleImage(button, phase);
        if (embeddedReady)
        {
            if (phase == ControllerEventPhase.Pressed)
            {
                if (button != ControllerButton.A) RevealEmbeddedControls();
                if (button == ControllerButton.A) ActivateFocusedControl();
                else if (button == ControllerButton.X) EmbeddedCommand(EmbeddedMediaCommand.TogglePlayback);
                else if (button == ControllerButton.RightStick) _ = ToggleExpandedAsync();
                else if (button == ControllerButton.LeftTrigger) EmbeddedCommand(EmbeddedMediaCommand.SeekBackward);
                else if (button == ControllerButton.RightTrigger) EmbeddedCommand(EmbeddedMediaCommand.SeekForward);
            }
            return button is ControllerButton.A or ControllerButton.X or ControllerButton.RightStick or ControllerButton.LeftTrigger or ControllerButton.RightTrigger;
        }
        if (button is not (ControllerButton.A or ControllerButton.X or ControllerButton.LeftTrigger or ControllerButton.RightTrigger or ControllerButton.LeftBumper or ControllerButton.RightBumper or ControllerButton.RightStick)) return IsExpanded;
        if (phase == ControllerEventPhase.Released) return true;
        if (button == ControllerButton.A && RevealVideoControls()) return true;
        videoChrome.Reveal();
        if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed) ActivateFocusedControl();
        else if (button == ControllerButton.X && phase == ControllerEventPhase.Pressed) Toggle();
        else if (button == ControllerButton.LeftBumper && phase == ControllerEventPhase.Pressed) Replay();
        else if (button == ControllerButton.RightBumper && phase == ControllerEventPhase.Pressed) ToggleLoop();
        else if (button == ControllerButton.RightStick && phase == ControllerEventPhase.Pressed) _ = ToggleExpandedAsync();
        else if (button is ControllerButton.LeftTrigger or ControllerButton.RightTrigger) SeekWithRepeat(button == ControllerButton.LeftTrigger ? -1 : 1, phase == ControllerEventPhase.Repeated);
        return true;
    }
    private void ReflowTransport()
    {
        var width = fullscreenGallery?.ActualWidth ?? carousel?.Center.ActualWidth ?? card.ActualWidth;
        var textScale = Math.Max(1, time.FontSize / 14);
        // An inline player already lives inside a message. Keep accessible
        // native hit targets, but remove the surrounding panel's extra padding
        // and rounded-card treatment so chrome occupies a shallow bottom strip.
        var inline = IsInlineVideo && !IsExpanded;
        toolbar.Padding = inline ? new(6, 0, 6, 0) : new(8);
        toolbar.RowSpacing = inline ? 0 : 8;
        videoControls.Padding = inline || highContrastChrome ? new(0) : new(0, 12, 0, 0);
        videoControls.Margin = inline ? new(0) : IsExpanded ? new(24) : new(8);
        toolbar.CornerRadius = videoControls.CornerRadius = inline ? new(0) : inlineRadius;
        playLabel.Visibility = width < 640 * textScale ? Visibility.Collapsed : Visibility.Visible;
        volume.Visibility = width < 400 * textScale ? Visibility.Collapsed : Visibility.Visible;
        volume.Width = Math.Clamp(width * .18, 72, 160);
        toolbar.ColumnSpacing = width < 420 * textScale ? 6 : 12;
        if (input && !changingPlacement && XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Control focused &&
            controls.Contains(focused) && focused.Visibility != Visibility.Visible) play.Focus(FocusState.Keyboard);
    }
    internal void ApplyTheme(Brush background, Brush foreground, Brush muted, Brush selected, Brush border, Brush focus, double fontSize,
        Windows.UI.Text.FontWeight weight, FontFamily? font, double radius, bool highContrast = false)
    {
        normalBrush = background; selectedBrush = selected;
        highContrastChrome = highContrast;
        themeFrameBorder = border; inlineRadius = new(radius);
        ApplyFrameLayout(IsExpanded);
        // Keep text fully opaque; translucency belongs only to the backing fill.
        // Contrast themes retain their system surfaces without a scrim.
        var chromeBackground = !highContrast && background is SolidColorBrush solid
            ? new SolidColorBrush(solid.Color with { A = 204 }) : background;
        toolbar.Background = highContrast ? background : null;
        imageTools.Background = chromeBackground;
        toolbar.BorderBrush = imageTools.BorderBrush = border;
        toolbar.BorderThickness = imageTools.BorderThickness = new(highContrast ? 1 : 0);
        videoControls.Background = chromeBackground;
        videoControls.Padding = highContrast ? new(0) : new(0,12,0,0);
        normalButtonBrush = highContrast ? background : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (!highContrast && background is SolidColorBrush surface)
        {
            var shade = surface.Color;
            videoControls.Background = new LinearGradientBrush
            {
                StartPoint = new(0, 0), EndPoint = new(0, 1),
                GradientStops = [new() { Color = shade with { A = 0 }, Offset = 0 },
                    new() { Color = shade with { A = 184 }, Offset = .3 },
                    new() { Color = shade with { A = 218 }, Offset = 1 }]
            };
        }
        toolbar.CornerRadius = imageTools.CornerRadius = videoControls.CornerRadius = new(radius);
        galleryChromeBackground = chromeBackground; galleryControls.CornerRadius = new(radius);
        galleryArrowBackground = !highContrast && background is SolidColorBrush plate
            ? new SolidColorBrush(plate.Color with { A = 166 }) : background;
        if (embeddedToolbar is not null)
        {
            embeddedToolbar.Background = chromeBackground;
            embeddedToolbar.CornerRadius = new(radius);
        }
        galleryControls.BorderBrush = border; galleryControls.BorderThickness = new(highContrast ? 1 : 0);
        galleryLabel.Foreground = foreground; galleryLabel.FontSize = fontSize; galleryLabel.FontWeight = weight;
        // System ink needs its paired surface even when the attachment stage
        // is transparent. Limit that surface to the transient feedback itself.
        feedback.Background = highContrast ? background : null;
        feedback.Padding = highContrast ? new(12, 8, 12, 8) : new(0);
        feedback.CornerRadius = new(radius);
        if (font is not null) galleryLabel.FontFamily = font; else galleryLabel.ClearValue(TextBlock.FontFamilyProperty);
        imageZoom.Foreground = muted; status.Foreground = foreground; loading.Foreground = foreground; time.Foreground = muted;
        position.Foreground = focus; position.Background = border;
        imageZoom.FontSize = status.FontSize = time.FontSize = fontSize; imageZoom.FontWeight = status.FontWeight = time.FontWeight = weight;
        // Reserve one stable status cell for time and brief seek/audio feedback.
        time.Width = fontSize * 9;
        foreach (var control in controls.Concat(imageControls).Concat(galleryButtons).Append(inlinePlay).Append(imageViewport).Append(position))
        {
            control.FocusVisualPrimaryBrush = focus; control.FocusVisualSecondaryBrush = background; control.UseSystemFocusVisuals = true;
        }
        foreach (var button in controls.Concat(imageControls).Concat(galleryButtons).Concat(embeddedControls).Append(inlinePlay).OfType<Button>())
        {
            button.Background = normalButtonBrush; button.Foreground = foreground; button.BorderBrush = border;
            button.BorderThickness = new(highContrast ? 1 : 0);
            button.FontSize = fontSize; button.FontWeight = weight; button.CornerRadius = new(radius);
            button.FocusVisualPrimaryBrush = focus; button.FocusVisualSecondaryBrush = background;
            button.FocusVisualPrimaryThickness = new(2); button.FocusVisualSecondaryThickness = new(1); button.FocusVisualMargin = new(-3);
            button.Resources["ButtonBackgroundPointerOver"] = selected; button.Resources["ButtonBackgroundPressed"] = selected;
            button.Resources["ButtonForegroundDisabled"] = muted;
            if (font is not null) button.FontFamily = font; else button.ClearValue(Control.FontFamilyProperty);
        }
        playLabel.FontSize = fontSize; playLabel.FontWeight = weight; if (font is not null) playLabel.FontFamily = font;
        // A compact chat may reveal just this button inside a taller poster.
        // Keep its focus ring inside the revealed native control bounds.
        inlinePlay.FocusVisualMargin = new(2);
        if (font is not null) { imageZoom.FontFamily = font; status.FontFamily = font; time.FontFamily = font; }
        ReflowTransport(); LayoutImageChrome(); LayoutImageTools(); RefreshGalleryControls();
        imageViewport.UseSystemFocusVisuals = !IsExpanded;
    }
    private void SuspendPlayer()
    {
        if (definition is null || !previewPlayback && player is null && playbackLeaseId is null &&
            definition.Source.IsAdaptiveDash == false && galleryDeclaration?.Gallery?[0].PublicThumbnail is null) return;
        if (player is not null)
            playbackMemory = new(definition, player.PlaybackSession.Position.TotalSeconds, player.Volume, player.IsMuted, player.IsLoopingEnabled, requestedRate,
                animationWantsPlayback, animationManualPlayback);
        player?.Pause(); lifetime.Cancel(); ReleasePlayer(); definition = null; resolved = null;
    }
    private void ReleasePlayer()
    {
        StopGalleryImageTransition();
        CloseEmbedded();
        // Clear owned references before calling WinRT: detach/close can deliver
        // layout and media callbacks synchronously. They must not close the same
        // player twice or mistake a retiring source for a newly admitted one.
        var retiringPlayer = player; player = null;
        var retiringSource = source; source = null;
        var retiringPublicMedia = publicMedia; publicMedia = null;
        var retiringDash = dashMedia; dashMedia = null;
        StopImageAnimation();
        ClosePlaybackOptions();
        StopMediaChrome();
        editPreviewEnd = null;
        try
        {
            try
            {
                video.Visibility = Visibility.Collapsed;
                video.SetMediaPlayer(null);
            }
            finally
            {
                try
                {
                    if (retiringPlayer is not null)
                    {
                        try { retiringPlayer.Source = null; }
                        finally { retiringPlayer.Dispose(); }
                    }
                }
                finally
                {
                    if (retiringPublicMedia is not null)
                        playbackRetirement = Task.WhenAll(playbackRetirement, retiringPublicMedia.DisposeAsync().AsTask());
                    else if (retiringDash is not null)
                        playbackRetirement = Task.WhenAll(playbackRetirement, retiringDash.DisposeAsync().AsTask());
                    else retiringSource?.Dispose();
                }
            }
        }
        finally
        {
            ReportAudio();
            if (ownsPlayerSlot) { ownsPlayerSlot = false; Interlocked.Decrement(ref residentPlayers); }
            if (ownsAnimationSlot) { ownsAnimationSlot = false; Interlocked.Decrement(ref residentAnimations); }
            if (playbackLeaseId is { } id && currentPlaybackRelease is { } release)
            {
                playbackLeaseId = null; currentPlaybackRelease = null;
                playbackRetirement = Task.WhenAll(playbackRetirement, ReleasePlaybackAsync(id, release));
            }
        }
    }
    private static async Task ReleasePlaybackAsync(string id, Func<string, Task> release)
    {
        try { await release(id); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Diagnostics.FrontendFailureLog.Current.Write("media-playback-release", error); }
    }
    public void Dispose()
    { if (disposed) return; disposed = true; active = input = false; ClearThumbnails(); ForgetInlineResume(); loading.IsActive = false; animationVisibility?.Dispose(); animationVisibility = null; ClearPendingImageViewportRestore(); expandedHost?.Close(restoreFocus: false); lifetime.Cancel(); lifetime.Dispose(); timer.Stop(); StopMediaChrome(); theme?.Dispose(); ReleasePlayer(); image.Source = null; video.PosterSource = null; }
}
