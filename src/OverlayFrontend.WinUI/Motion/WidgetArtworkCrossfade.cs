using Microsoft.UI.Xaml;
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
    private static TimeSpan Duration(double milliseconds)
    {
        lock (environmentGate)
        {
            var highContrast = appearance.Contrast == ContrastPreference.High || appearance.Contrast == ContrastPreference.System && systemHighContrast;
            return highContrast || appearance.Transparency == TransparencyPreference.Reduced ? TimeSpan.Zero :
                WidgetMotionOptions.From(appearance, systemAnimations).Duration(milliseconds);
        }
    }

    private readonly ImageBrush committed = new() { Stretch = Stretch.UniformToFill };
    private readonly ImageBrush incoming = new() { Stretch = Stretch.UniformToFill };
    private readonly Border committedLayer;
    private readonly Border incomingLayer;
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
        committedLayer = new() { Background = committed };
        incomingLayer = new() { Background = incoming };
        View.Children.Add(committedLayer); View.Children.Add(incomingLayer);
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
    internal void SetSource(ImageSource? value, Stretch fit)
    {
        if (disposed) return;
        SetFit(fit);
        if (ReferenceEquals(Source, value)) return;
        Source = value;
        CancelSettle();
        if (value is null || committed.ImageSource is null || !View.IsLoaded || Duration(400) == TimeSpan.Zero)
        { ShowLatest(); return; }
        settle = new();
        _ = PrepareLatestAsync(settle.Token);
    }
    private async Task PrepareLatestAsync(CancellationToken token)
    {
        try
        {
            // A bounded one-shot settling delay coalesces rapidly changing focus.
            // It never schedules frames or queues intermediate destinations.
            await Task.Delay(Duration(150), token);
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
        if (Source is null || !View.IsLoaded || View.ActualWidth <= 0 || View.ActualHeight <= 0 || Duration(400) == TimeSpan.Zero)
        { ShowLatest(); return; }
        incoming.ImageSource = Source;
        incoming.Stretch = requestedFit;
        // XAML continues arranging both image layers during a content resize.
        // This recipe changes only opacity, so its captured center is irrelevant
        // and no size-dependent clip exists. Keep the batch alive on SizeChanged:
        // settling there would skip the blend and expose a newer pending source.
        var size = new System.Numerics.Vector2((float)View.ActualWidth, (float)View.ActualHeight);
        committedTarget = WidgetCompositionTarget.ForClippedDialog(committedLayer, size);
        incomingTarget = WidgetCompositionTarget.ForClippedDialog(incomingLayer, size);
        motion = new(CompositionTarget.GetCompositorForCurrentThread(), View.DispatcherQueue);
        var duration = Duration(400);
        var recipe = WidgetMotionPolicy.ArtworkCrossfade(duration);
        var version = ++epoch;
        ++StartedCount;
        // Keep the previous pixels behind the incoming image until it covers
        // them. Fading both layers exposes the underlying surface at mid-blend
        // (two half-opaque images provide only 75 percent combined coverage).
        Playback = motion.PlayAsync((WidgetMotionPlayback[])[new(committedTarget, recipe.Outgoing), new(incomingTarget, recipe.Incoming)]);
        _ = CompleteAsync(Playback, version);
    }
    private async Task CompleteAsync(Task<WidgetMotionOutcome> playback, long version)
    {
        try
        {
            await playback;
            if (disposed || epoch != version) return;
            var finished = incoming.ImageSource;
            RetireMotion();
            committed.ImageSource = finished;
            committed.Stretch = incoming.Stretch;
            incoming.ImageSource = null;
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
        committed.ImageSource = Source; committed.Stretch = requestedFit;
        incoming.ImageSource = null;
    }
    private void EnvironmentChanged()
    {
        if (disposed) return;
        if (View.DispatcherQueue.HasThreadAccess) ShowLatest();
        else View.DispatcherQueue.TryEnqueue(() => { if (!disposed) ShowLatest(); });
    }
    private void Unloaded(object sender, RoutedEventArgs args) { if (!View.IsLoaded) ShowLatest(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; PreferencesChanged -= EnvironmentChanged;
        View.Unloaded -= Unloaded;
        CancelSettle(); RetireMotion();
        Source = null; committed.ImageSource = incoming.ImageSource = null;
        View.Children.Clear();
    }
}
