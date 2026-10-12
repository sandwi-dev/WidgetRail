using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WidgetRail.OverlayFrontend.WinUI.Motion;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Framework-owned transform for native media placement.</summary>
internal sealed class MediaFullscreenXamlMotion : IDisposable
{
    private readonly FrameworkElement element;
    private readonly object previous;
    private readonly CompositeTransform transform = new();
    private Storyboard? storyboard;
    private TaskCompletionSource? completion;
    private WidgetMotionPose destination;

    internal MediaFullscreenXamlMotion(FrameworkElement element)
    {
        this.element = element;
        previous = element.ReadLocalValue(UIElement.RenderTransformProperty);
        element.RenderTransform = transform;
    }

    internal void Set(WidgetMotionPose pose)
    {
        destination = pose;
        transform.ScaleX = pose.Scale.X; transform.ScaleY = pose.Scale.Y;
        transform.TranslateX = pose.Translation.X; transform.TranslateY = pose.Translation.Y;
    }

    internal Task PlayAsync(WidgetMotionPose target, TimeSpan duration)
    {
        var x = transform.ScaleX; var y = transform.ScaleY;
        var dx = transform.TranslateX; var dy = transform.TranslateY;
        Stop();
        destination = target;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = completion.Task;
        storyboard = new();
        Animate("ScaleX", x, target.Scale.X); Animate("ScaleY", y, target.Scale.Y);
        Animate("TranslateX", dx, target.Translation.X); Animate("TranslateY", dy, target.Translation.Y);
        storyboard.Completed += Completed;
        storyboard.Begin();
        return result;

        void Animate(string property, double from, double to)
        {
            var animation = new DoubleAnimation { From = from, To = to, Duration = duration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(animation, transform); Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }
    }

    private void Completed(object? sender, object args) { Stop(); Set(destination); }
    internal void Cancel() { Stop(); Set(destination); }
    private void Stop()
    {
        if (storyboard is not null) { storyboard.Completed -= Completed; storyboard.Stop(); storyboard = null; }
        completion?.TrySetResult(); completion = null;
    }
    public void Dispose()
    {
        Stop();
        if (ReferenceEquals(previous, DependencyProperty.UnsetValue)) element.ClearValue(UIElement.RenderTransformProperty);
        else element.SetValue(UIElement.RenderTransformProperty, previous);
    }
}
