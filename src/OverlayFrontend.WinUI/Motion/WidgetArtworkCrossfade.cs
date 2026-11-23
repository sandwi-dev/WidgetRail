using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System.Numerics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>
/// Native pixels only: accepts already decoded images and never selects or loads a source.
/// Two painted images and at most one latest pending reference are retained.
/// </summary>
internal sealed class WidgetArtworkCrossfade : IDisposable
{
    private static AppearanceSettings appearance = AppearanceSettings.Default;
    private static readonly object environmentGate = new();
    private static bool systemAnimations = true;
    private static bool systemHighContrast;
    private static event Action? PreferencesChanged;
    internal static void SetAppearance(AppearanceSettings value, bool animationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (environmentGate)
        {
            if (appearance == value && systemAnimations == animationsEnabled) return;
            appearance = value; systemAnimations = animationsEnabled;
        }
        PreferencesChanged?.Invoke();
    }
    internal static void SetHighContrast(bool value)
    {
        lock (environmentGate)
        {
            if (systemHighContrast == value) return;
            systemHighContrast = value;
        }
        PreferencesChanged?.Invoke();
    }
    internal static TimeSpan Duration(double milliseconds)
    {
        lock (environmentGate)
        {
            var highContrast = appearance.Contrast == ContrastPreference.High || appearance.Contrast == ContrastPreference.System && systemHighContrast;
            return highContrast || appearance.Transparency == TransparencyPreference.Reduced ? TimeSpan.Zero :
                WidgetMotionOptions.From(appearance, systemAnimations).Duration(milliseconds);
        }
    }

    private sealed class Layer
    {
        internal readonly ImageBrush Brush = new() { Stretch = Stretch.UniformToFill };
        internal readonly Grid Frame = new();
        internal readonly Border Picture;
        internal Layer()
        {
            Picture = new() { Background = Brush };
            Frame.Children.Add(Picture);
        }
        internal void StopDrift()
        {
            if (!HasDrift) return;
            var visual = ElementCompositionPreview.GetElementVisual(Picture);
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
            HasDrift = false;
        }
        internal bool HasDrift { get; private set; }
        internal void StartDrift(WallpaperMotionStyle style, Vector2 size)
        {
            StopDrift();
            var duration = Duration(style.DriftMs);
            if (!style.PanZoom || duration == TimeSpan.Zero || size.X <= 0 || size.Y <= 0 || Brush.ImageSource is null) return;
            var visual = ElementCompositionPreview.GetElementVisual(Picture);
            visual.CenterPoint = new(size * style.Origin, 0);
            visual.Scale = new(style.ToScale, style.ToScale, 1);
            HasDrift = true;
            if (style.FromScale == style.ToScale) return;
            using var drift = visual.Compositor.CreateVector3KeyFrameAnimation();
            using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new(.3f,.1f), new(.5f,1));
            drift.InsertKeyFrame(0, new(style.FromScale, style.FromScale, 1));
            drift.InsertKeyFrame(1, visual.Scale, easing);
            drift.Duration = duration;
            visual.StartAnimation("Scale", drift);
        }
        internal void Pause(bool pause)
        {
            if (!HasDrift) return;
            var controller = ElementCompositionPreview.GetElementVisual(Picture).TryGetAnimationController("Scale");
            if (pause) controller?.Pause(); else controller?.Resume();
        }
    }
    private Layer back = new();
    private Layer front = new();
    private ImageBrush committed => back.Brush;
    private ImageBrush incoming => front.Brush;
    private WallpaperMotionStyle style = WallpaperMotionStyle.Default;
    private FocusNavigationDirection direction;
    private bool suspended;
    private bool CanAnimate => !suspended && View.IsLoaded;
    private Vector2 Size => new((float)View.ActualWidth, (float)View.ActualHeight);
    private TimeSpan FadeDuration => style.Transition == "none" ? TimeSpan.Zero : Duration(style.FadeMs);
    private string? logicalArtwork;
    private readonly RectangleGeometry clip = new();
#if ENABLE_WIDGET_VALIDATION
    internal Vector2 LastTravel { get; private set; }
    internal bool HasDrift => back.HasDrift || front.HasDrift;
#endif

    internal void Configure(WallpaperMotionStyle next)
    {
        if (style == next) return;
        style = next;
        UpdateGeometry();
        ShowLatest();
        if (CanAnimate) back.StartDrift(style, Size);
    }
    internal void SetSuspended(bool value)
    {
        if (suspended == value) return;
        suspended = value;
        if (value && (motion is not null || settle is not null)) ShowLatest();
        if (!value && CanAnimate && !back.HasDrift) back.StartDrift(style, Size);
        else back.Pause(value);
    }
    private void UpdateGeometry()
    {
        var custom = style.PanZoom || style.Transition == "directional-fade";
        if (custom && !ReferenceEquals(View.Clip, clip)) View.Clip = clip;
        else if (!custom && View.Clip is not null) View.Clip = null;
        if (!custom) return;
        var size = Size;
        clip.Rect = new(0, 0, size.X, size.Y);
        foreach (var layer in new[] { back, front })
            if (layer.HasDrift) ElementCompositionPreview.GetElementVisual(layer.Picture).CenterPoint = new(size * style.Origin, 0);
    }
    private WidgetCompositionMotion? motion;
    private WidgetCompositionTarget? committedTarget;
    private WidgetCompositionTarget? incomingTarget;
    private CancellationTokenSource? settle;
    private long epoch;
    private bool pendingReady;
    private bool disposed;
    internal Grid View { get; } = new() { IsHitTestVisible = false };
    internal ImageSource? Source { get; private set; }
    internal ImageSource? OutgoingSource => motion is null ? null : committed.ImageSource;
    internal ImageSource? IncomingSource => incoming.ImageSource;
    internal int RetainedImageCount => new[] { Source, committed.ImageSource, incoming.ImageSource }.OfType<ImageSource>()
        .Distinct(ReferenceEqualityComparer.Instance).Count();
    internal Task<WidgetMotionOutcome>? Playback { get; private set; }
    internal long StartedCount { get; private set; }
    internal Exception? Failure { get; private set; }
    private Stretch requestedFit = Stretch.UniformToFill;

    internal WidgetArtworkCrossfade()
    {
        View.Children.Add(back.Frame); View.Children.Add(front.Frame);
        View.SizeChanged += (_, _) => { if (!disposed) UpdateGeometry(); };
        View.Loaded += (_, _) =>
        {
            if (disposed || !CanAnimate) return;
            if (back.HasDrift) back.Pause(false); else back.StartDrift(style, Size);
        };
        View.Unloaded += Unloaded;
        PreferencesChanged += EnvironmentChanged;
    }
    internal void SetFit(Stretch value)
    {
        if (disposed || requestedFit == value) return;
        requestedFit = value;
        committed.Stretch = incoming.Stretch = value;
    }
    internal void SetAlignment(AlignmentX x, AlignmentY y)
    {
        if (disposed) return;
        committed.AlignmentX = incoming.AlignmentX = x;
        committed.AlignmentY = incoming.AlignmentY = y;
    }
    internal void SnapSource(ImageSource value, Stretch fit, string? key)
    {
        SetFit(fit);
        Source = value; logicalArtwork = key;
        ShowLatest();
        if (CanAnimate) back.StartDrift(style, Size);
    }

    internal void SetSource(ImageSource? value, Stretch fit, FocusNavigationDirection movement = FocusNavigationDirection.None, string? key = null)
    {
        if (disposed) return;
        SetFit(fit);
        if (ReferenceEquals(Source, value)) return;
        var previous = Source;
        Source = value;
        if (previous is not null && value is not null && key is not null && logicalArtwork == key)
        {
            // A new decode size or renewed lease can supply the same logical artwork.
            if (ReferenceEquals(committed.ImageSource, previous)) committed.ImageSource = value;
            if (ReferenceEquals(incoming.ImageSource, previous)) incoming.ImageSource = value;
            return;
        }
        logicalArtwork = key;
        direction = movement;
        CancelSettle();
        if (value is null || committed.ImageSource is null || !CanAnimate || FadeDuration == TimeSpan.Zero)
        { ShowLatest(); if (CanAnimate) back.StartDrift(style, Size); return; }
        settle = new();
        _ = PrepareLatestAsync(settle.Token);
    }
    private async Task PrepareLatestAsync(CancellationToken token)
    {
        try
        {
            // A bounded one-shot settling delay coalesces rapidly changing focus.
            // It never schedules frames or queues intermediate destinations.
            await Task.Delay(Duration(style.Transition == "directional-fade" ? 100 : 150), token);
            if (disposed || token.IsCancellationRequested) return;
            pendingReady = true;
            if (motion is null) BeginLatest();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Failure = error; if (!disposed) ShowLatest(); }
    }
    private void BeginLatest()
    {
        if (disposed || !pendingReady || motion is not null) return;
        pendingReady = false;
        if (ReferenceEquals(Source, committed.ImageSource)) return;
        if (Source is null || !CanAnimate || View.ActualWidth <= 0 || View.ActualHeight <= 0 || FadeDuration == TimeSpan.Zero)
        { ShowLatest(); return; }
        incoming.ImageSource = Source;
        incoming.Stretch = requestedFit;
        var size = Size;
        committedTarget = WidgetCompositionTarget.ForClippedDialog(back.Frame, size);
        incomingTarget = WidgetCompositionTarget.ForClippedDialog(front.Frame, size);
        motion = new(CompositionTarget.GetCompositorForCurrentThread(), View.DispatcherQueue);
        var duration = FadeDuration;
        var recipe = WidgetMotionPolicy.ArtworkCrossfade(duration);
        var delta = style.Transition != "directional-fade" ? Vector2.Zero : direction switch
        {
            FocusNavigationDirection.Right => new(size.X * style.Travel.X, 0),
            FocusNavigationDirection.Left => new(-size.X * style.Travel.X, 0),
            FocusNavigationDirection.Down => new(0, size.Y * style.Travel.Y),
            FocusNavigationDirection.Up => new(0, -size.Y * style.Travel.Y),
            _ => Vector2.Zero,
        };
        // Enlarge only during the nudge so neither edge can uncover the canvas.
        var overscan = 1 + 2 * Math.Max(Math.Abs(delta.X / size.X), Math.Abs(delta.Y / size.Y));
#if ENABLE_WIDGET_VALIDATION
        LastTravel = delta;
#endif
        var offsetPose = WidgetMotionPose.Identity with { Scale = new(overscan, overscan, 1), Translation = new(delta, 0) };
        var incomingAbove = ReferenceEquals(View.Children[1], front.Frame);
        var enter = recipe.Incoming with { From = offsetPose with { Opacity = incomingAbove ? 0 : 1 } };
        var leave = recipe.Outgoing with { To = offsetPose with { Translation = new(-delta, 0), Opacity = incomingAbove ? 1 : 0 } };
        front.StartDrift(style, size);
        var version = ++epoch;
        ++StartedCount;
        // Keep the previous pixels behind the incoming image until it covers
        // them. Fading both layers exposes the underlying surface at mid-blend
        // (two half-opaque images provide only 75 percent combined coverage).
        Playback = motion.PlayAsync((WidgetMotionPlayback[])[new(committedTarget, leave), new(incomingTarget, enter)]);
        _ = CompleteAsync(Playback, version);
    }
    private async Task CompleteAsync(Task<WidgetMotionOutcome> playback, long version)
    {
        try
        {
            await playback;
            if (disposed || epoch != version) return;
            RetireMotion();
            back.StopDrift();
            committed.ImageSource = null;
            (back, front) = (front, back);
            // Finish the visible blend without snapping back to the older base.
            // A newer ready proposal replaces the pending slot and starts next;
            // obsolete completion never overwrites logical Source.
            BeginLatest();
        }
        catch (Exception error) { Failure = error; if (!disposed) ShowLatest(); }
    }
    private void CancelSettle()
    {
        settle?.Cancel(); settle?.Dispose(); settle = null; pendingReady = false;
    }
    private void RetireMotion()
    {
        ++epoch;
        motion?.Dispose(); motion = null;
        committedTarget?.Dispose(); committedTarget = null;
        incomingTarget?.Dispose(); incomingTarget = null;
    }
    private void ShowLatest()
    {
        CancelSettle(); RetireMotion();
        if (!ReferenceEquals(committed.ImageSource, Source)) back.StopDrift();
        front.StopDrift();
        committed.ImageSource = Source; committed.Stretch = requestedFit;
        incoming.ImageSource = null;
    }
    private void EnvironmentChanged()
    {
        if (disposed) return;
        void ApplyPolicy()
        {
            if (disposed) return;
            ShowLatest();
            if (Duration(style.DriftMs) == TimeSpan.Zero) back.StopDrift();
        }
        if (View.DispatcherQueue.HasThreadAccess) ApplyPolicy();
        else View.DispatcherQueue.TryEnqueue(ApplyPolicy);
    }
    private void Unloaded(object sender, RoutedEventArgs args) { if (!View.IsLoaded) { ShowLatest(); back.Pause(true); } }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; PreferencesChanged -= EnvironmentChanged;
        View.Unloaded -= Unloaded;
        CancelSettle(); RetireMotion();
        back.StopDrift(); front.StopDrift();
        Source = null; committed.ImageSource = incoming.ImageSource = null;
        View.Children.Clear();
    }
}
