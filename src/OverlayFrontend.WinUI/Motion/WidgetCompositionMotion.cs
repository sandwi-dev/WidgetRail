using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

internal enum WidgetMotionOutcome { Completed, Superseded, Canceled, Disposed }
internal readonly record struct WidgetMotionPlayback(WidgetCompositionTarget Target, WidgetMotionRecipe Recipe);

/// <summary>
/// Owns presentation channels on dedicated motion layers, never layout Offset on
/// a XAML-owned visual. The caller retains controls/actions independently.
/// </summary>
internal sealed class WidgetCompositionTarget : IDisposable
{
    private readonly Visual visual;
    private readonly Visual viewport;
    private readonly CompositionObject translation;
    private readonly string translationProperty;
    private readonly InsetClip clip;
    private readonly Vector3 originalTranslation;
    private readonly Vector3 originalScale;
    private readonly Vector3 originalCenter;
    private readonly float originalOpacity;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private bool disposed;
    internal Compositor Compositor => visual.Compositor;
    internal bool IsDisposed => disposed;

    // These must be dedicated parent/content layers: the wrapper has no existing
    // clip, and the content's transform/opacity channels have no other animator.
    internal WidgetCompositionTarget(Visual content, Visual clipViewport, Vector2 size)
        : this(content, clipViewport, size, xamlTranslation: false) { }

    private WidgetCompositionTarget(Visual content, Visual clipViewport, Vector2 size, bool xamlTranslation)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(clipViewport);
        if (ReferenceEquals(content, clipViewport) || clipViewport.Clip is not null ||
            content.Compositor != clipViewport.Compositor || !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0)
            throw new ArgumentException("Motion requires separate, unclipped viewport/content layers in one compositor and a finite size.");
        visual = content; viewport = clipViewport;
        originalScale = visual.Scale; originalOpacity = visual.Opacity; originalCenter = visual.CenterPoint;
        translation = xamlTranslation ? visual.Properties : visual;
        translationProperty = xamlTranslation ? "Translation" : nameof(Visual.Offset);
        if (xamlTranslation) visual.Properties.TryGetVector3("Translation", out originalTranslation);
        else originalTranslation = visual.Offset;
        visual.CenterPoint = new(size / 2, 0);
        clip = Compositor.CreateInsetClip(); viewport.Clip = clip;
    }

    /// <summary>Pass dedicated XAML motion wrappers, not a button whose text/style must remain unchanged.</summary>
    internal static WidgetCompositionTarget ForElement(UIElement contentLayer, UIElement viewportLayer, Vector2 size)
    {
        ArgumentNullException.ThrowIfNull(contentLayer); ArgumentNullException.ThrowIfNull(viewportLayer);
        ElementCompositionPreview.SetIsTranslationEnabled(contentLayer, true);
        return new(ElementCompositionPreview.GetElementVisual(contentLayer), ElementCompositionPreview.GetElementVisual(viewportLayer), size, true);
    }

    internal void Start(WidgetMotionRecipe recipe, bool fromCurrent)
    {
        Check();
        Validate(recipe);
        if (!fromCurrent) Set(recipe.From);
        if (recipe.Duration == TimeSpan.Zero) { Set(recipe.To); return; }
        using var easing = Compositor.CreateCubicBezierEasingFunction(new(1f / 3, 0), new(2f / 3, 1));
        AnimateVector(translation, translationProperty, originalTranslation + recipe.To.Translation);
        AnimateVector(visual, nameof(Visual.Scale), originalScale * recipe.To.Scale);
        AnimateScalar(visual, nameof(Visual.Opacity), originalOpacity * recipe.To.Opacity);
        AnimateScalar(clip, nameof(InsetClip.LeftInset), recipe.To.Insets.X);
        AnimateScalar(clip, nameof(InsetClip.TopInset), recipe.To.Insets.Y);
        AnimateScalar(clip, nameof(InsetClip.RightInset), recipe.To.Insets.Z);
        AnimateScalar(clip, nameof(InsetClip.BottomInset), recipe.To.Insets.W);

        void AnimateVector(CompositionObject target, string property, Vector3 to)
        {
            using var animation = Compositor.CreateVector3KeyFrameAnimation();
            animation.Duration = recipe.Duration;
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, to, easing);
            target.StartAnimation(property, animation);
        }
        void AnimateScalar(CompositionObject target, string property, float to)
        {
            using var animation = Compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = recipe.Duration;
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, to, easing);
            target.StartAnimation(property, animation);
        }
    }

    internal void Set(WidgetMotionPose pose)
    {
        Check(); Stop();
        if (ReferenceEquals(translation, visual)) visual.Offset = originalTranslation + pose.Translation;
        else visual.Properties.InsertVector3(translationProperty, originalTranslation + pose.Translation);
        visual.Scale = originalScale * pose.Scale; visual.Opacity = originalOpacity * pose.Opacity;
        clip.LeftInset = pose.Insets.X; clip.TopInset = pose.Insets.Y;
        clip.RightInset = pose.Insets.Z; clip.BottomInset = pose.Insets.W;
    }

    private void Stop()
    {
        translation.StopAnimation(translationProperty);
        visual.StopAnimation(nameof(Visual.Scale)); visual.StopAnimation(nameof(Visual.Opacity));
        clip.StopAnimation(nameof(InsetClip.LeftInset)); clip.StopAnimation(nameof(InsetClip.TopInset));
        clip.StopAnimation(nameof(InsetClip.RightInset)); clip.StopAnimation(nameof(InsetClip.BottomInset));
    }
    private void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Motion layers require their UI dispatcher.");
    }
    internal static void Validate(WidgetMotionRecipe recipe)
    {
        if (recipe.Duration < TimeSpan.Zero || recipe.Duration > TimeSpan.FromSeconds(2))
            throw new ArgumentOutOfRangeException(nameof(recipe));
        CheckPose(recipe.From); CheckPose(recipe.To);
        static void CheckPose(WidgetMotionPose pose)
        {
            if (!float.IsFinite(pose.Translation.X) || !float.IsFinite(pose.Translation.Y) || !float.IsFinite(pose.Translation.Z) ||
                !float.IsFinite(pose.Scale.X) || !float.IsFinite(pose.Scale.Y) || !float.IsFinite(pose.Scale.Z) ||
                pose.Scale.X <= 0 || pose.Scale.Y <= 0 || pose.Scale.Z <= 0 || !float.IsFinite(pose.Opacity) || pose.Opacity is < 0 or > 1 ||
                !float.IsFinite(pose.Insets.X) || !float.IsFinite(pose.Insets.Y) || !float.IsFinite(pose.Insets.Z) || !float.IsFinite(pose.Insets.W) ||
                pose.Insets.X < 0 || pose.Insets.Y < 0 || pose.Insets.Z < 0 || pose.Insets.W < 0)
                throw new ArgumentOutOfRangeException(nameof(pose));
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        Check(); Set(WidgetMotionPose.Identity);
        visual.CenterPoint = originalCenter;
        viewport.Clip = null;
        clip.Dispose(); disposed = true;
    }
}

/// <summary>One native batch per semantic group. No managed frame loop, polling or repainting.</summary>
internal sealed class WidgetCompositionMotion : IDisposable
{
    private sealed class Run(WidgetMotionPlayback[] playbacks, CompositionScopedBatch batch)
    {
        internal readonly WidgetMotionPlayback[] Playbacks = playbacks;
        internal readonly CompositionScopedBatch Batch = batch;
        internal readonly TaskCompletionSource<WidgetMotionOutcome> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Cancellation;
        internal bool Finished;
        internal Windows.Foundation.TypedEventHandler<object, CompositionBatchCompletedEventArgs>? Handler;
    }
    private readonly Compositor compositor;
    private readonly DispatcherQueue dispatcher;
    private Run? active;
    private bool disposed;

    internal WidgetCompositionMotion(Compositor compositor, DispatcherQueue dispatcher)
    { this.compositor = compositor; this.dispatcher = dispatcher; }

    internal Task<WidgetMotionOutcome> PlayAsync(IEnumerable<WidgetMotionPlayback> playbacks, CancellationToken token = default)
    {
        Check();
        if (token.IsCancellationRequested) return Task.FromResult(WidgetMotionOutcome.Canceled);
        var values = playbacks.ToArray();
        if (values.Length is < 1 or > 66 || values.Select(value => value.Target).Distinct().Count() != values.Length ||
            values.Any(value => value.Target.IsDisposed || value.Target.Compositor != compositor)) throw new ArgumentException("A motion group needs unique live targets in one compositor.", nameof(playbacks));
        foreach (var value in values) WidgetCompositionTarget.Validate(value.Recipe);
        var continued = active?.Playbacks.Select(value => value.Target).ToHashSet() ?? [];
        if (active is { } previous)
        {
            foreach (var value in previous.Playbacks)
                if (!values.Any(next => ReferenceEquals(next.Target, value.Target))) value.Target.Set(value.Recipe.To);
            Finish(previous, WidgetMotionOutcome.Superseded, settle: false);
        }
        var run = new Run(values, compositor.CreateScopedBatch(CompositionBatchTypes.Animation));
        active = run;
        run.Handler = (_, _) => { if (ReferenceEquals(active, run)) Finish(run, WidgetMotionOutcome.Completed, settle: true); };
        run.Batch.Completed += run.Handler;
        try
        {
            foreach (var value in values) value.Target.Start(value.Recipe, continued.Contains(value.Target));
            run.Batch.End();
            if (values.All(value => value.Recipe.Duration == TimeSpan.Zero)) Finish(run, WidgetMotionOutcome.Completed, settle: true);
            else run.Cancellation = token.Register(() => dispatcher.TryEnqueue(() =>
            {
                if (ReferenceEquals(active, run)) Finish(run, WidgetMotionOutcome.Canceled, settle: true);
            }));
            return run.Completion.Task;
        }
        catch
        {
            Finish(run, WidgetMotionOutcome.Canceled, settle: true);
            throw;
        }
    }

    internal void Cancel()
    {
        Check();
        if (active is { } run) Finish(run, WidgetMotionOutcome.Canceled, settle: true);
    }

    private void Finish(Run run, WidgetMotionOutcome outcome, bool settle)
    {
        if (run.Finished) return;
        run.Finished = true;
        if (ReferenceEquals(active, run)) active = null;
        run.Cancellation.Dispose();
        run.Batch.Completed -= run.Handler;
        run.Batch.Dispose();
        if (settle) foreach (var value in run.Playbacks)
            if (!value.Target.IsDisposed) value.Target.Set(value.Recipe.To);
        run.Completion.TrySetResult(outcome);
    }
    private void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Motion groups require their UI dispatcher.");
    }
    public void Dispose()
    {
        if (disposed) return;
        Check();
        if (active is { } run) Finish(run, WidgetMotionOutcome.Disposed, settle: true);
        disposed = true;
    }
}
