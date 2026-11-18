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
    private readonly Border card = new() { Padding = new(8), BorderThickness = new(1) };
    private readonly Grid stage = new();
    private readonly MediaPlayerElement video = new() { AreTransportControlsEnabled = false, AutoPlay = false, Stretch = Stretch.Uniform };
    private readonly Image image = new() { Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { Text = "Opening media…", TextWrapping = TextWrapping.Wrap };
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
    private sealed record PlaybackMemory(MediaPlayerDefinition Definition, double Position, double Volume, bool Muted, bool Loop, double Rate);
    private PlaybackMemory? playbackMemory;
    private Control PreferredFocus => rememberedControl is { IsEnabled: true, Visibility: Visibility.Visible, IsLoaded: true } remembered &&
        (!IsImage || !imageControls.Contains(remembered) || imageChrome.Visible) ? remembered : IsImage ? imageViewport : play;
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
    internal bool CanControl => player is not null || IsImage && image.Source is not null;
    internal bool IsImage => !imageReplacementPending && resolved is { IsImage: true };
    private MediaPlayer? player;
    private MediaSource? source;
    private MediaPlayerDefinition? definition;
    private HostMediaPlayerSource? resolved;
    internal MediaPlayerDefinition? Definition => definition;
    private bool disposed, active = true, input, checkingAuthority;
    private Func<CancellationToken, Task<HostMediaPlayerSource>>? resolveAuthority;
    private Func<bool>? currentAuthority;
    private Func<string, CancellationToken, Task<HostMediaPlayerSource>>? resolvePlaybackLease;
    private Func<string, Task>? releasePlaybackLease;
    private string? playbackLeaseId;
    private Func<string, Task>? currentPlaybackRelease;
    private Task playbackRetirement = Task.CompletedTask;
    internal Task PlaybackRetirement => playbackRetirement;
    private bool previewPlayback;
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
        if (disposed || lifetime.IsCancellationRequested) return;
        video.PosterSource = poster;
        if (IsImage) return;
        if (poster is not null)
        { video.Visibility = Visibility.Visible; status.Visibility = Visibility.Collapsed; }
        else if (player is null)
        { video.Visibility = Visibility.Collapsed; status.Visibility = Visibility.Visible; }
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
    internal string PrimaryActionLabel => HasOptionsMenu ? "Choose" : IsImage ? ImageActionLabel : XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Control focused && !ReferenceEquals(focused, play) && !ReferenceEquals(focused, position)
        ? ReferenceEquals(focused, volume) ? adjustingVolume ? "Done" : "Adjust volume" : AutomationProperties.GetName(focused)
        : IsEnded ? "Replay" : IsPlaying ? "Pause" : "Play";
    internal Action? InteractionChanged { get; set; }
    // Resolve authority in parallel, but keep decoder creation and its first
    // layout out of the page animation's UI-thread frame budget.
    internal Task PresentationReady { private get; set; } = Task.CompletedTask;
    internal MediaPlayerView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        InitializeImages();
        stage.Children.Add(imageViewport); stage.Children.Add(video); stage.Children.Add(status); body.Children.Add(stage); Grid.SetRowSpan(stage, 2);
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
            ShowMediaFeedback("Volume " + Math.Round(volume.Value) + "%");
        };
        toolbar.VerticalAlignment = VerticalAlignment.Bottom;
        videoControls.Children.Add(toolbar);
        inlineChrome.Children.Add(videoControls); Grid.SetRowSpan(inlineChrome, 2);
        body.Children.Add(inlineChrome); card.Child = body; Content = card;
        Grid.SetRowSpan(imageTools, 2); imageTools.VerticalAlignment = VerticalAlignment.Bottom; imageTools.Margin = new(8); body.Children.Add(imageTools);
        InitializeMediaChrome();

        AutomationProperties.SetName(play, "Play or pause");
        AutomationProperties.SetAutomationId(play, "MediaPlayer.Play");
        play.Click += (_, _) => Toggle();
        card.SizeChanged += (_, _) => ReflowTransport();
        GotFocus += (_, _) =>
        {
            if (changingPlacement) return;
            var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
            if (focused is Control control && controls.Contains(control)) rememberedControl = control;
            else if (ReferenceEquals(focused, this) && input && CanControl) PreferredFocus.Focus(FocusState.Keyboard);
            InteractionChanged?.Invoke();
        };
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { theme?.Dispose(); theme = NativePopupTheme.MediaPlayer(this); EnsureVideoStage(); SynchronizePagePlacement(true); RefreshImageChrome(); if (active && !retainedForPageExit) timer.Start(); };
        LayoutUpdated += (_, _) => EnsureInlineChrome();
        Unloaded += (_, _) => { timer.Stop(); StopMediaChrome(); if (!retainedForPageExit) { player?.Pause(); ReleaseVideoStage(); } theme?.Dispose(); theme = null; };
    }
    internal void SetActive(bool value, bool acceptsInput)
    {
        var previouslyInteractive = input;
        active = value; input = value && acceptsInput && !editorPreviewPaused;
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
            SuspendPlayer(); timer.Stop(); expandedHost?.Close(restoreFocus: false);
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
        if (IsVideo && previewPlayback && !input) { StopMediaChrome(); videoControls.Visibility = Visibility.Collapsed; }
        else if (IsVideo && input && !previouslyInteractive) videoChrome.Reveal();
        if (!IsVideo) videoControls.Visibility = Visibility.Collapsed;
        RefreshImageChrome();
        inlineChrome.IsHitTestVisible = input;
        EnsureInlineChrome();
        if (input && CanControl && XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this))
            PreferredFocus.Focus(FocusState.Keyboard);
    }
    internal void Apply(Func<CancellationToken, Task<HostMediaPlayerSource>> resolve, MediaPlayerDefinition next, Func<bool> current, object? owner = null,
        Func<string, CancellationToken, Task<HostMediaPlayerSource>>? resolveOwned = null, Func<string, Task>? releaseOwned = null,
        ImageSource? initialPoster = null)
    {
        if (disposed || previewPlayback && !active) return;
        var retainImage = Equals(resourceOwner, owner) && image.Source is BitmapImage { PixelWidth: > 0 } &&
            next.Source.Attachment?.ContentType == "image/png";
        if (!Equals(resourceOwner, owner))
        { resourceOwner = owner; playbackMemory = null; definition = null; lifetime.Cancel(); ReleasePlayer(); video.PosterSource = null; }
        resolveAuthority = resolve; currentAuthority = current;
        resolvePlaybackLease = resolveOwned; releasePlaybackLease = releaseOwned;
        if (next == definition) return;
        if (definition is { } previous && previous.Source == next.Source && previous.Revision == next.Revision && (player is not null || image.Source is not null && !imageReplacementPending))
        { definition = next; if (resolved is not null) resolved = resolved with { Media = next }; if (player is not null) ApplyOptions(next.Options); return; }
        expandedHost?.Close(restoreFocus: false);
        ClearPendingImageViewportRestore();
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        ReleasePlayer(); definition = next; resolved = null; adjustingVolume = false; waitingForSlot = false;
        seekAcceleration.Reset(); feedbackUntil = 0;
        video.PosterSource = initialPoster;
        // A decoded frame may bridge an in-place image replacement within the
        // same owner. It is presentation only: input and authority belong to the
        // new source, and failed resolution clears it. Never retain across owners.
        imageReplacementPending = true;
        if (!retainImage) { image.Source = null; imageViewport.Visibility = imageTools.Visibility = Visibility.Collapsed; }
        video.Visibility = video.PosterSource is not null ? Visibility.Visible : Visibility.Collapsed;
        toolbar.Visibility = videoControls.Visibility = Visibility.Collapsed;
        rememberedControl = null;
        status.Visibility = retainImage || video.PosterSource is not null ? Visibility.Collapsed : Visibility.Visible; status.Text = "Opening media…";
        SetActive(active, input);
        if (retainImage) _ = ShowImageLoadingAsync(next, lifetime.Token);
        if (next.Source.Kind == MediaPlayerSourceKind.SavedCapture && resolveOwned is not null && releaseOwned is not null)
        { playbackLeaseId = Guid.NewGuid().ToString("N"); currentPlaybackRelease = releaseOwned; }
        _ = LoadAsync(playbackLeaseId is { } owned ? token => resolveOwned!(owned, token) : resolve, next, current, lifetime.Token);
    }
    private async Task ShowImageLoadingAsync(MediaPlayerDefinition expected, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            if (!disposed && !token.IsCancellationRequested && definition == expected && imageReplacementPending)
            { image.Source = null; status.Visibility = Visibility.Visible; }
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
            if (!current()) { Fail(); return; }
            if (value.Media != next || value.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidOperationException("Media expired.");
            resolved = value;
            var uri = new Uri(value.Uri);
            if (IsVideo)
            {
                imageReplacementPending = false;
                image.Source = null; imageViewport.Visibility = imageTools.Visibility = Visibility.Collapsed;
                if (Interlocked.Increment(ref residentPlayers) > 4)
                {
                    Interlocked.Decrement(ref residentPlayers);
                    waitingForSlot = true; status.Text = "Close another video before opening this one.";
                    return;
                }
                ownsPlayerSlot = true;
                player = new MediaPlayer { AutoPlay = false };
                if (previewPlayback)
                {
                    // Transient previews use our existing local controls. Never
                    // replace the user's music session or consume media hotkeys.
                    player.CommandManager.IsEnabled = false;
                    player.SystemMediaTransportControls.IsEnabled = false;
                }
                ApplyOptions(next.Options);
                EnsureVideoStage();
                source = MediaSource.CreateFromUri(uri); player.Source = source; video.SetMediaPlayer(player);
                player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!disposed && !token.IsCancellationRequested) Fail(); });
                player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (disposed || retainedForPageExit || token.IsCancellationRequested) return;
                    if (player is not null) player.PlaybackSession.PlaybackRate = requestedRate;
                    EnsureVideoStage();
                    status.Visibility = Visibility.Collapsed;
                    if (playbackMemory is { } saved && definition is { } latest && saved.Definition.Source == latest.Source && saved.Definition.Revision == latest.Revision && player is { } resumed)
                    {
                        resumed.Volume = latest.Options.Volume == saved.Definition.Options.Volume ? saved.Volume : latest.Options.Volume;
                        resumed.IsMuted = ForcePreviewMute || (latest.Options.Muted == saved.Definition.Options.Muted ? saved.Muted : latest.Options.Muted);
                        resumed.IsLoopingEnabled = latest.Options.Loop == saved.Definition.Options.Loop ? saved.Loop : latest.Options.Loop;
                        requestedRate = latest.Options.PlaybackRate == saved.Definition.Options.PlaybackRate ? saved.Rate : latest.Options.PlaybackRate;
                        resumed.PlaybackSession.PlaybackRate = requestedRate;
                        if (resumed.PlaybackSession.CanSeek) resumed.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(saved.Position, 0, Duration));
                    }
                    if ((playbackMemory is null || previewPlayback) && definition?.Options.AutoPlay == true && active && !editorPreviewPaused) player?.Play();
                    playbackMemory = null;
                    SetActive(active, input);
                    Refresh(); InteractionChanged?.Invoke();
                });
                player.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!disposed && !token.IsCancellationRequested) { Refresh(); InteractionChanged?.Invoke(); } });
                video.Visibility = toolbar.Visibility = videoControls.Visibility = Visibility.Visible;
                if(input)videoChrome.Reveal();
            }
            else
            {
                var bitmap = new BitmapImage { DecodePixelWidth = value.Width > 0 ? value.Width : 1920 };
                // Capture images resolve to host-authorized local files. Decode
                // offscreen before publishing the new bitmap instead of exposing
                // an empty Image while UriSource completes on a later frame.
                if (!uri.IsFile) throw new InvalidOperationException("Capture image must resolve to a local file.");
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(uri.LocalPath).AsTask(token);
                using (var stream = await file.OpenReadAsync().AsTask(token))
                    await bitmap.SetSourceAsync(stream).AsTask(token);
                if (disposed || retainedForPageExit || token.IsCancellationRequested || definition != next) return;
                if (!current()) { Fail(); return; }
                if (value.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidOperationException("Media expired.");
                video.Visibility = Visibility.Collapsed;
                imageReplacementPending = false;
                image.Source = bitmap;
                imageViewport.Visibility = imageTools.Visibility = Visibility.Visible;
                RefreshImageChrome();
                FitImage();
            }
            if (!IsVideo) status.Visibility = Visibility.Collapsed;
            SetActive(active, input); Refresh();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!disposed && !token.IsCancellationRequested) Fail(); }
    }
    private void ApplyOptions(MediaPlayerOptions options)
    {
        if (player is null) return;
        player.IsMuted = ForcePreviewMute || options.Muted; player.Volume = options.Volume; player.IsLoopingEnabled = options.Loop;
        requestedRate = options.PlaybackRate; player.PlaybackSession.PlaybackRate = requestedRate;
    }
    private void Fail()
    { ClearPendingImageViewportRestore(); lifetime.Cancel(); timer.Stop(); ReleasePlayer(); video.PosterSource = null; imageReplacementPending = false; image.Source = null; imageViewport.Visibility = imageTools.Visibility = Visibility.Collapsed; status.Text = "This media could not be opened. The source may be unavailable or its format unsupported."; status.Visibility = Visibility.Visible; video.Visibility = toolbar.Visibility = videoControls.Visibility = Visibility.Collapsed; }
    private void Refresh()
    {
        ReportAudio();
        if (disposed || retainedForPageExit) return;
        if (currentAuthority?.Invoke() == false) { Fail(); return; }
        if (resolved?.RequiresRevalidation == true && !checkingAuthority && Environment.TickCount64 >= nextAuthorityCheck && resolveAuthority is not null) _ = RefreshAuthorityAsync(lifetime.Token);
        if (resolved?.ExpiresAtUnixMilliseconds is { } expiry && expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { timer.Stop(); Fail(); return; }
        if (waitingForSlot && active && ResidentPlayers < 4 && definition is { } waiting && resolveAuthority is { } resolver && currentAuthority is { } authority)
        { definition = null; waitingForSlot = false; Apply(resolver, waiting, authority, resourceOwner, resolvePlaybackLease, releasePlaybackLease); return; }
        if (player is not { } value) return;
        RefreshEditRange();
        var seconds = Math.Max(0, value.PlaybackSession.Position.TotalSeconds);
        var duration = Duration;
        playLabel.Text = IsEnded ? "Replay" : IsPlaying ? "Pause" : "Play"; playIcon.Symbol = IsEnded ? Symbol.Refresh : IsPlaying ? Symbol.Pause : Symbol.Play;
        if (input && IsPlaying != chromeWasPlaying) videoChrome.Reveal();
        chromeWasPlaying = IsPlaying;
        AutomationProperties.SetName(play, playLabel.Text);
        ToolTipService.SetToolTip(play, playLabel.Visibility == Visibility.Collapsed ? playLabel.Text : null);
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
        ToolTipService.SetToolTip(mute, value.IsMuted ? "Unmute" : "Mute");
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
                    value.ExpiresAtUnixMilliseconds is { } expiry && expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) Fail();
                else resolved = value; // Existing authority probes renew a retained library playback lease.
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!disposed && !token.IsCancellationRequested && definition == expected) Fail(); }
        finally { checkingAuthority = false; }
    }
    private static string FormatTime(double value) => TimeSpan.FromSeconds(value).ToString(value >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static void Label(Button button, string name, string id, bool showToolTip = true)
    { AutomationProperties.SetName(button, name); AutomationProperties.SetAutomationId(button, id); ToolTipService.SetToolTip(button, showToolTip ? name : null); }
    internal void Toggle()
    {
        if (!input || player is null) return;
        if (IsPlaying) player.Pause();
        else { if (player.PlaybackSession.CanSeek && Duration > 0 && player.PlaybackSession.Position.TotalSeconds >= Duration - .05) player.PlaybackSession.Position = TimeSpan.Zero; player.Play(); }
        Refresh();
    }
    internal void ActivateFocusedControl()
    {
        if (!input || XamlRoot is null) return;
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
    private void Replay() { if (!input || player?.PlaybackSession.CanSeek != true) return; player.PlaybackSession.Position = TimeSpan.Zero; player.Play(); }
    private void ToggleLoop() { if (!input || player is null) return; player.IsLoopingEnabled = !player.IsLoopingEnabled; Refresh(); InteractionChanged?.Invoke(); }
    private void ToggleMute() { if (!input || player is null) return; player.IsMuted = !player.IsMuted; Refresh(); InteractionChanged?.Invoke(); }
    internal bool Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase == ControllerEventPhase.Released || button is not (ControllerButton.LeftTrigger or ControllerButton.RightTrigger)) seekAcceleration.Reset();
        if (button == ControllerButton.B && backOwned) { if (phase == ControllerEventPhase.Released) backOwned = false; return true; }
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
        var width = card.ActualWidth;
        var textScale = Math.Max(1, time.FontSize / 14);
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
        videoStage?.SetPageBackground(background);
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
        imageZoom.Foreground = muted; status.Foreground = foreground; time.Foreground = muted;
        position.Foreground = focus; position.Background = border;
        imageZoom.FontSize = status.FontSize = time.FontSize = fontSize; imageZoom.FontWeight = status.FontWeight = time.FontWeight = weight;
        // Reserve one stable status cell for time and brief seek/audio feedback.
        time.Width = fontSize * 9;
        foreach (var control in controls.Concat(imageControls).Append(imageViewport).Append(position))
        {
            control.FocusVisualPrimaryBrush = focus; control.FocusVisualSecondaryBrush = background; control.UseSystemFocusVisuals = true;
        }
        foreach (var button in controls.Concat(imageControls).OfType<Button>())
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
        if (font is not null) { imageZoom.FontFamily = font; status.FontFamily = font; time.FontFamily = font; }
        ReflowTransport(); LayoutImageTools();
        imageViewport.UseSystemFocusVisuals = !IsExpanded;
    }
    private void SuspendPlayer()
    {
        if (definition is null || !previewPlayback && player is null && playbackLeaseId is null) return;
        if (player is not null)
            playbackMemory = new(definition, player.PlaybackSession.Position.TotalSeconds, player.Volume, player.IsMuted, player.IsLoopingEnabled, requestedRate);
        player?.Pause(); lifetime.Cancel(); ReleasePlayer(); definition = null; resolved = null;
    }
    private void ReleasePlayer()
    {
        ClosePlaybackOptions();
        StopMediaChrome();
        editPreviewEnd = null;
        try
        {
            videoStage?.SetCovered(true);
            video.Visibility = Visibility.Collapsed;
            video.SetMediaPlayer(null); player?.Dispose(); source?.Dispose(); ReleaseVideoStage();
        }
        finally
        {
            player = null; source = null; ReportAudio();
            if (ownsPlayerSlot) { ownsPlayerSlot = false; Interlocked.Decrement(ref residentPlayers); }
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
    { if (disposed) return; disposed = true; active = input = false; ClearPendingImageViewportRestore(); expandedHost?.Close(restoreFocus: false); lifetime.Cancel(); lifetime.Dispose(); timer.Stop(); StopMediaChrome(); theme?.Dispose(); ReleasePlayer(); image.Source = null; video.PosterSource = null; }
}
