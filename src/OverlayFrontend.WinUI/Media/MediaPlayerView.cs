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
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock status = new() { Text = "Opening media…", TextWrapping = TextWrapping.Wrap };
    private readonly Grid toolbar = new() { ColumnSpacing = 12, Padding = new(8) };
    private readonly Button play = new() { IsTabStop = true, UseSystemFocusVisuals = true, MinHeight = 44, Padding = new(12, 8, 12, 8) };
    private readonly Button replay = new() { IsTabStop = true, UseSystemFocusVisuals = true, MinHeight = 44, Padding = new(12, 8, 12, 8) };
    private readonly SymbolIcon playIcon = new(Symbol.Play);
    private readonly TextBlock playLabel = new() { Text = "Play", VerticalAlignment = VerticalAlignment.Center };
    private readonly SymbolIcon replayIcon = new(Symbol.RepeatAll);
    private readonly TextBlock replayLabel = new() { Text = "Replay", VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider position = new() { Minimum = 0, Maximum = 1, StepFrequency = .1, SmallChange = .5, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock time = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 84, TextAlignment = TextAlignment.Right };
    private Control? rememberedControl;
    private readonly Button loop = new() { Content = new SymbolIcon(Symbol.RepeatAll) };
    private readonly Button speed = new() { Content = "1×" };
    private readonly Button mute = new() { Content = new SymbolIcon(Symbol.Volume) };
    private readonly Button expand = new() { Content = new SymbolIcon(Symbol.FullScreen) };
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, Value = 100, StepFrequency = 5, SmallChange = 5, LargeChange = 10, MinWidth = 48, VerticalAlignment = VerticalAlignment.Center };
    private Control[] controls = [];
    private bool refreshing, adjustingVolume, backOwned;
    private double requestedRate = 1;
    private object? resourceOwner;
    private sealed record PlaybackMemory(MediaPlayerDefinition Definition, double Position, double Volume, bool Muted, bool Loop, double Rate);
    private PlaybackMemory? playbackMemory;
    private Control PreferredFocus => rememberedControl is { IsEnabled: true, Visibility: Visibility.Visible } remembered ? remembered : play;
    private ContentDialog? expandedDialog;
    internal bool IsAdjustingVolume => adjustingVolume;
    internal bool HasDialog => expandedDialog is not null;
    private double Duration => player?.PlaybackSession.NaturalDuration.TotalSeconds is > 0 and var seconds && double.IsFinite(seconds) ? seconds : resolved?.DurationSeconds ?? 0;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private CancellationTokenSource lifetime = new();
    private IDisposable? theme;
    private Brush? normalBrush, selectedBrush;
    private static int residentPlayers;
    internal static int ResidentPlayers => Volatile.Read(ref residentPlayers);
    private bool ownsPlayerSlot, waitingForSlot;
    internal bool CanControl => player is not null;
    private MediaPlayer? player;
    private MediaSource? source;
    private MediaPlayerDefinition? definition;
    private HostMediaPlayerSource? resolved;
    internal MediaPlayerDefinition? Definition => definition;
    private bool disposed, active = true, input, checkingAuthority;
    private Func<CancellationToken, Task<HostMediaPlayerSource>>? resolveAuthority;
    private Func<bool>? currentAuthority;
    private long nextAuthorityCheck;
    internal bool IsVideo => resolved is { IsImage: false };
    internal bool IsPlaying => player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
    internal string PrimaryActionLabel => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Control focused && !ReferenceEquals(focused, play) && !ReferenceEquals(focused, position)
        ? ReferenceEquals(focused, volume) ? adjustingVolume ? "Done" : "Adjust volume" : AutomationProperties.GetName(focused)
        : IsPlaying ? "Pause" : "Play";
    internal Action? InteractionChanged { get; set; }
    internal MediaPlayerView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        stage.Children.Add(image); stage.Children.Add(video); stage.Children.Add(status); body.Children.Add(stage);
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowSpacing = 8;
        for (var i = 0; i < 7; i++) toolbar.ColumnDefinitions.Add(new() { Width = i == 5 ? new(1, GridUnitType.Star) : GridLength.Auto });
        var playContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        playContent.Children.Add(playIcon); playContent.Children.Add(playLabel); play.Content = playContent;
        var replayContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        replayContent.Children.Add(replayIcon); replayContent.Children.Add(replayLabel); replay.Content = replayContent;
        var timeline = new Grid { ColumnSpacing = 12 };
        timeline.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); timeline.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        timeline.Children.Add(position); Grid.SetColumn(time, 1); timeline.Children.Add(time); Grid.SetColumnSpan(timeline, 7); toolbar.Children.Add(timeline);
        controls = [play, replay, loop, speed, mute, volume, expand];
        for (var i = 0; i < controls.Length; i++)
        {
            Grid.SetColumn(controls[i], i); Grid.SetRow(controls[i], 1); toolbar.Children.Add(controls[i]);
            var control = controls[i];
            control.GotFocus += (_, _) => { if (XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), control)) rememberedControl = control; InteractionChanged?.Invoke(); };
            controls[i].UseSystemFocusVisuals = true; controls[i].IsTabStop = true;
            if (controls[i] is Button button) { button.MinHeight = 40; button.MinWidth = 36; button.Padding = new(8); }
        }
        position.IsTabStop = true; position.UseSystemFocusVisuals = true;
        AutomationProperties.SetName(position, "Playback position"); AutomationProperties.SetAutomationId(position, "MediaPlayer.Position");
        AutomationProperties.SetName(volume, "Volume"); AutomationProperties.SetAutomationId(volume, "MediaPlayer.Volume");
        Label(loop, "Loop playback", "MediaPlayer.Loop"); Label(speed, "Playback speed", "MediaPlayer.Speed");
        Label(mute, "Mute", "MediaPlayer.Mute"); Label(expand, "Expand player", "MediaPlayer.Expand");
        loop.Click += (_, _) => ToggleLoop(); speed.Click += (_, _) => CycleSpeed(); mute.Click += (_, _) => ToggleMute();
        expand.Click += async (_, _) => await ToggleExpandedAsync();
        position.ValueChanged += (_, _) => { if (!refreshing && input && player?.PlaybackSession.CanSeek == true) player.PlaybackSession.Position = TimeSpan.FromSeconds(position.Value); };
        volume.ValueChanged += (_, _) => { if (!refreshing && input && player is not null) { player.Volume = volume.Value / 100; player.IsMuted = false; } };
        Grid.SetRow(toolbar, 1); body.Children.Add(toolbar); card.Child = body; Content = card;
        AutomationProperties.SetName(play, "Play or pause"); AutomationProperties.SetName(replay, "Replay");
        AutomationProperties.SetAutomationId(play, "MediaPlayer.Play"); AutomationProperties.SetAutomationId(replay, "MediaPlayer.Replay");
        play.Click += (_, _) => Toggle(); replay.Click += (_, _) => Replay();
        card.SizeChanged += (_, _) =>
        {
            var width = card.ActualWidth;
            playLabel.Visibility = replayLabel.Visibility = width < 640 ? Visibility.Collapsed : Visibility.Visible;
            replay.Visibility = loop.Visibility = width < 360 ? Visibility.Collapsed : Visibility.Visible;
            volume.Visibility = width < 400 ? Visibility.Collapsed : Visibility.Visible;
            toolbar.ColumnSpacing = width < 420 ? 6 : 12;
            if (input && XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Control focused && controls.Contains(focused) && focused.Visibility != Visibility.Visible)
                play.Focus(FocusState.Keyboard);
        };
        GotFocus += (_, _) =>
        {
            var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
            if (focused is Control control && controls.Contains(control)) rememberedControl = control;
            else if (ReferenceEquals(focused, this) && input && player is not null) PreferredFocus.Focus(FocusState.Keyboard);
            InteractionChanged?.Invoke();
        };
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { theme?.Dispose(); theme = NativePopupTheme.MediaPlayer(this); if (active) timer.Start(); };
        Unloaded += (_, _) => { timer.Stop(); player?.Pause(); theme?.Dispose(); theme = null; };
    }
    internal void SetActive(bool value, bool acceptsInput)
    {
        active = value; input = value && acceptsInput;
        video.IsHitTestVisible = input; video.IsTabStop = false;
        if (!input) { adjustingVolume = false; expandedDialog?.Hide(); }
        if (!active) { SuspendPlayer(); timer.Stop(); expandedDialog?.Hide(); } else if (IsLoaded && !lifetime.IsCancellationRequested) timer.Start();
        foreach (var control in controls) control.IsEnabled = input && player is not null;
        position.IsEnabled = replay.IsEnabled = input && player?.PlaybackSession.CanSeek == true;
        if (input && player is not null && XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this))
            PreferredFocus.Focus(FocusState.Keyboard);
    }
    internal void Apply(Func<CancellationToken, Task<HostMediaPlayerSource>> resolve, MediaPlayerDefinition next, Func<bool> current, object? owner = null)
    {
        if (disposed) return;
        if (!Equals(resourceOwner, owner))
        { resourceOwner = owner; playbackMemory = null; definition = null; lifetime.Cancel(); ReleasePlayer(); }
        resolveAuthority = resolve; currentAuthority = current;
        if (next == definition) return;
        if (definition is { } previous && previous.Source == next.Source && previous.Revision == next.Revision && (player is not null || image.Source is not null))
        { definition = next; if (resolved is not null) resolved = resolved with { Media = next }; if (player is not null) ApplyOptions(next.Options); return; }
        expandedDialog?.Hide();
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        ReleasePlayer(); definition = next; resolved = null; adjustingVolume = false; waitingForSlot = false;
        image.Source = null; image.Visibility = video.Visibility = toolbar.Visibility = Visibility.Collapsed;
        status.Visibility = Visibility.Visible; status.Text = "Opening media…";
        _ = LoadAsync(resolve, next, current, lifetime.Token);
    }
    private async Task LoadAsync(Func<CancellationToken, Task<HostMediaPlayerSource>> resolve, MediaPlayerDefinition next, Func<bool> current, CancellationToken token)
    {
        try
        {
            var value = await resolve(token);
            if (disposed || token.IsCancellationRequested || definition != next || !current()) return;
            if (value.Media != next || value.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) throw new InvalidOperationException("Media expired.");
            resolved = value;
            var uri = new Uri(value.Uri);
            if (IsVideo)
            {
                if (Interlocked.Increment(ref residentPlayers) > 4)
                {
                    Interlocked.Decrement(ref residentPlayers);
                    waitingForSlot = true; status.Text = "Close another video before opening this one.";
                    return;
                }
                ownsPlayerSlot = true;
                player = new MediaPlayer { AutoPlay = false };
                ApplyOptions(next.Options);
                source = MediaSource.CreateFromUri(uri); player.Source = source; video.SetMediaPlayer(player);
                player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!disposed && !token.IsCancellationRequested) Fail(); });
                player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (disposed || token.IsCancellationRequested) return;
                    if (player is not null) player.PlaybackSession.PlaybackRate = requestedRate;
                    status.Visibility = Visibility.Collapsed;
                    if (playbackMemory is { } saved && definition is { } latest && saved.Definition.Source == latest.Source && saved.Definition.Revision == latest.Revision && player is { } resumed)
                    {
                        resumed.Volume = latest.Options.Volume == saved.Definition.Options.Volume ? saved.Volume : latest.Options.Volume;
                        resumed.IsMuted = latest.Options.Muted == saved.Definition.Options.Muted ? saved.Muted : latest.Options.Muted;
                        resumed.IsLoopingEnabled = latest.Options.Loop == saved.Definition.Options.Loop ? saved.Loop : latest.Options.Loop;
                        requestedRate = latest.Options.PlaybackRate == saved.Definition.Options.PlaybackRate ? saved.Rate : latest.Options.PlaybackRate;
                        resumed.PlaybackSession.PlaybackRate = requestedRate;
                        if (resumed.PlaybackSession.CanSeek) resumed.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(saved.Position, 0, Duration));
                    }
                    else if (definition?.Options.AutoPlay == true && active) player?.Play();
                    playbackMemory = null;
                    SetActive(active, input);
                    Refresh(); InteractionChanged?.Invoke();
                });
                player.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!disposed && !token.IsCancellationRequested) { Refresh(); InteractionChanged?.Invoke(); } });
                video.Visibility = toolbar.Visibility = Visibility.Visible;
            }
            else
            {
                var bitmap = new BitmapImage { DecodePixelWidth = value.Width > 0 ? Math.Min(value.Width, 1920) : 1920 };
                bitmap.ImageFailed += (_, _) => { if (!disposed && !token.IsCancellationRequested) Fail(); };
                bitmap.UriSource = uri; image.Source = bitmap; image.Visibility = Visibility.Visible;
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
        player.IsMuted = options.Muted; player.Volume = options.Volume; player.IsLoopingEnabled = options.Loop;
        requestedRate = options.PlaybackRate; player.PlaybackSession.PlaybackRate = requestedRate;
    }
    private void Fail()
    { lifetime.Cancel(); timer.Stop(); ReleasePlayer(); image.Source = null; image.Visibility = Visibility.Collapsed; status.Text = "This media could not be opened. The source may be unavailable or its format unsupported."; status.Visibility = Visibility.Visible; video.Visibility = toolbar.Visibility = Visibility.Collapsed; }
    private void Refresh()
    {
        if (disposed) return;
        if (currentAuthority?.Invoke() == false) { Fail(); return; }
        if (resolved?.RequiresRevalidation == true && !checkingAuthority && Environment.TickCount64 >= nextAuthorityCheck && resolveAuthority is not null) _ = RefreshAuthorityAsync(lifetime.Token);
        if (resolved?.ExpiresAtUnixMilliseconds is { } expiry && expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { timer.Stop(); Fail(); return; }
        if (waitingForSlot && active && ResidentPlayers < 4 && definition is { } waiting && resolveAuthority is { } resolver && currentAuthority is { } authority)
        { definition = null; waitingForSlot = false; Apply(resolver, waiting, authority, resourceOwner); return; }
        if (player is not { } value) return;
        var seconds = Math.Max(0, value.PlaybackSession.Position.TotalSeconds);
        var duration = Duration;
        playLabel.Text = IsPlaying ? "Pause" : "Play"; playIcon.Symbol = IsPlaying ? Symbol.Pause : Symbol.Play;
        AutomationProperties.SetName(play, playLabel.Text);
        refreshing = true;
        try
        {
            position.Maximum = Math.Max(duration, 1); position.Value = Math.Clamp(seconds, 0, position.Maximum);
            volume.Value = value.Volume * 100;
        }
        finally { refreshing = false; }
        position.IsEnabled = replay.IsEnabled = input && value.PlaybackSession.CanSeek;
        speed.Content = value.PlaybackSession.PlaybackRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×";
        AutomationProperties.SetName(speed, "Playback speed " + speed.Content);
        AutomationProperties.SetName(loop, value.IsLoopingEnabled ? "Turn looping off" : "Turn looping on");
        loop.Background = value.IsLoopingEnabled ? selectedBrush : normalBrush;
        mute.Background = value.IsMuted ? selectedBrush : normalBrush;
        mute.Content = new SymbolIcon(value.IsMuted ? Symbol.Mute : Symbol.Volume);
        AutomationProperties.SetName(mute, value.IsMuted ? "Unmute" : "Mute");
        time.Text = FormatTime(seconds) + " / " + (duration > 0 ? FormatTime(duration) : "Live");
    }
    private async Task RefreshAuthorityAsync(CancellationToken token)
    {
        var expected = definition;
        checkingAuthority = true; nextAuthorityCheck = Environment.TickCount64 + 2000;
        try
        {
            var value = await resolveAuthority!(token);
            if (!disposed && !token.IsCancellationRequested && definition == expected && value.Media != expected) Fail();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!disposed && !token.IsCancellationRequested && definition == expected) Fail(); }
        finally { checkingAuthority = false; }
    }
    private static string FormatTime(double value) => TimeSpan.FromSeconds(value).ToString(value >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    private static void Label(Button button, string name, string id)
    { AutomationProperties.SetName(button, name); AutomationProperties.SetAutomationId(button, id); ToolTipService.SetToolTip(button, name); }
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
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (HasDialog && focused is Button && !controls.Contains(focused)) { expandedDialog?.Hide(); return; }
        if (ReferenceEquals(focused, replay)) Replay();
        else if (ReferenceEquals(focused, loop)) ToggleLoop();
        else if (ReferenceEquals(focused, speed)) CycleSpeed();
        else if (ReferenceEquals(focused, mute)) ToggleMute();
        else if (ReferenceEquals(focused, expand)) _ = ToggleExpandedAsync();
        else if (ReferenceEquals(focused, volume)) { adjustingVolume = !adjustingVolume; InteractionChanged?.Invoke(); }
        else Toggle();
    }
    internal bool MoveFocus(FocusNavigationDirection direction)
    {
        if (!input || player is null || XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, position))
        {
            if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right) { Seek(direction == FocusNavigationDirection.Left ? -SeekStep : SeekStep); return true; }
            if (direction == FocusNavigationDirection.Down) return PreferredFocus.Focus(FocusState.Keyboard);
            return HasDialog;
        }
        if (direction == FocusNavigationDirection.Up && position.IsEnabled) { adjustingVolume = false; return position.Focus(FocusState.Keyboard); }
        if (direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right)) { adjustingVolume = false; return HasDialog; }
        if (ReferenceEquals(focused, volume) && adjustingVolume)
        { volume.Value = Math.Clamp(volume.Value + (direction == FocusNavigationDirection.Left ? -5 : 5), 0, 100); return true; }
        var available = controls.Where(control => control.IsEnabled && control.Visibility == Visibility.Visible).ToArray();
        var index = Array.IndexOf(available, focused as Control);
        var next = index + (direction == FocusNavigationDirection.Left ? -1 : 1);
        return next >= 0 && next < available.Length ? available[next].Focus(FocusState.Keyboard) : HasDialog;
    }
    private double SeekStep => Duration is > 0 and <= 10 ? .5 : 5;
    private void Seek(double seconds) { if (input && player?.PlaybackSession.CanSeek == true) player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(player.PlaybackSession.Position.TotalSeconds + seconds, 0, Duration)); }
    private void Replay() { if (!input || player?.PlaybackSession.CanSeek != true) return; player.PlaybackSession.Position = TimeSpan.Zero; player.Play(); }
    private void ToggleLoop() { if (!input || player is null) return; player.IsLoopingEnabled = !player.IsLoopingEnabled; Refresh(); InteractionChanged?.Invoke(); }
    private void ToggleMute() { if (!input || player is null) return; player.IsMuted = !player.IsMuted; Refresh(); InteractionChanged?.Invoke(); }
    private void CycleSpeed()
    {
        if (!input || player is null) return;
        double[] rates = [.5, .75, 1, 1.25, 1.5, 2];
        requestedRate = rates.FirstOrDefault(rate => rate > requestedRate + .01, .5);
        player.PlaybackSession.PlaybackRate = requestedRate; Refresh(); InteractionChanged?.Invoke();
    }
    internal bool Handle(ControllerButton button, ControllerEventPhase phase)
    {
        if (button == ControllerButton.B && backOwned) { if (phase == ControllerEventPhase.Released) backOwned = false; return true; }
        if (!input || !IsVideo) return false;
        if (button == ControllerButton.B && (adjustingVolume || HasDialog))
        {
            if (phase == ControllerEventPhase.Pressed) { backOwned = true; if (adjustingVolume) adjustingVolume = false; else expandedDialog?.Hide(); }
            return true;
        }
        if (button is not (ControllerButton.A or ControllerButton.LeftTrigger or ControllerButton.RightTrigger or ControllerButton.LeftBumper or ControllerButton.RightBumper or ControllerButton.RightStick)) return HasDialog;
        if (phase == ControllerEventPhase.Released) return true;
        if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed) ActivateFocusedControl();
        else if (button == ControllerButton.LeftBumper && phase == ControllerEventPhase.Pressed) Replay();
        else if (button == ControllerButton.RightBumper && phase == ControllerEventPhase.Pressed) ToggleLoop();
        else if (button == ControllerButton.RightStick && phase == ControllerEventPhase.Pressed) _ = ToggleExpandedAsync();
        else if (button is ControllerButton.LeftTrigger or ControllerButton.RightTrigger) Seek(button == ControllerButton.LeftTrigger ? -SeekStep : SeekStep);
        return true;
    }
    private async Task ToggleExpandedAsync()
    {
        if (expandedDialog is { } existing) { existing.Hide(); return; }
        if (!input || disposed || XamlRoot is null) return;
        var root = XamlRoot;
        var dialog = new ContentDialog { Title = "Media player", CloseButtonText = "Back", DefaultButton = ContentDialogButton.None, XamlRoot = root };
        Input.GamepadKeyBoundary.ObserveDialog(dialog);
        var availableWidth = Math.Max(280, Math.Min(1280, root.Size.Width - 96));
        dialog.Resources["ContentDialogMaxWidth"] = availableWidth + 48;
        var holder = new Grid { Width = availableWidth, Height = Math.Max(180, Math.Min(800, root.Size.Height - 240)) };
        var returnControl = FocusManager.GetFocusedElement(root) as Control;
        expandedDialog = dialog; Content = null; holder.Children.Add(card); dialog.Content = holder;
        dialog.Opened += (_, _) => play.Focus(FocusState.Keyboard);
        InteractionChanged?.Invoke();
        try { using var colors = NativePopupTheme.Dialog(dialog, this); await dialog.ShowAsync(); }
        catch (Exception error) when (error is not OutOfMemoryException) { Diagnostics.FrontendFailureLog.Current.Write("media-player-expand", error); }
        finally
        {
            holder.Children.Remove(card); dialog.Content = null; expandedDialog = null;
            if (!disposed) { Content = card; if (input) (returnControl is { IsLoaded: true, IsEnabled: true, Visibility: Visibility.Visible } ? returnControl : play).Focus(FocusState.Keyboard); }
            InteractionChanged?.Invoke();
        }
    }
    internal void ApplyTheme(Brush background, Brush foreground, Brush muted, Brush selected, Brush border, Brush focus, double fontSize,
        Windows.UI.Text.FontWeight weight, FontFamily? font, double radius)
    {
        normalBrush = background; selectedBrush = selected;
        card.Background = background; card.BorderBrush = border; card.CornerRadius = new(radius);
        toolbar.Background = background; status.Foreground = foreground; time.Foreground = muted;
        position.Foreground = focus; position.Background = border;
        status.FontSize = time.FontSize = fontSize; status.FontWeight = time.FontWeight = weight;
        foreach (var control in controls.Append(position))
        {
            control.FocusVisualPrimaryBrush = focus; control.FocusVisualSecondaryBrush = background; control.UseSystemFocusVisuals = true;
        }
        foreach (var button in controls.OfType<Button>())
        {
            button.Background = background; button.Foreground = foreground; button.BorderBrush = border; button.BorderThickness = new(1);
            button.FontSize = fontSize; button.FontWeight = weight; button.CornerRadius = new(radius);
            button.FocusVisualPrimaryBrush = focus; button.FocusVisualSecondaryBrush = background;
            button.FocusVisualPrimaryThickness = new(2); button.FocusVisualSecondaryThickness = new(1); button.FocusVisualMargin = new(-3);
            button.Resources["ButtonBackgroundPointerOver"] = selected; button.Resources["ButtonBackgroundPressed"] = selected;
            button.Resources["ButtonForegroundDisabled"] = muted;
            if (font is not null) button.FontFamily = font; else button.ClearValue(Control.FontFamilyProperty);
        }
        foreach (var label in new[] { playLabel, replayLabel }) { label.FontSize = fontSize; label.FontWeight = weight; if (font is not null) label.FontFamily = font; }
        if (font is not null) { status.FontFamily = font; time.FontFamily = font; }
    }
    private void SuspendPlayer()
    {
        if (player is null || definition is null) return;
        playbackMemory = new(definition, player.PlaybackSession.Position.TotalSeconds, player.Volume, player.IsMuted, player.IsLoopingEnabled, requestedRate);
        player.Pause(); lifetime.Cancel(); ReleasePlayer(); definition = null; resolved = null;
    }
    private void ReleasePlayer()
    {
        try { video.SetMediaPlayer(null); player?.Dispose(); source?.Dispose(); }
        finally
        {
            player = null; source = null;
            if (ownsPlayerSlot) { ownsPlayerSlot = false; Interlocked.Decrement(ref residentPlayers); }
        }
    }
    public void Dispose()
    { if (disposed) return; disposed = true; expandedDialog?.Hide(); lifetime.Cancel(); lifetime.Dispose(); timer.Stop(); theme?.Dispose(); ReleasePlayer(); image.Source = null; }
}
