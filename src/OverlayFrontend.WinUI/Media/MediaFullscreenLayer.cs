using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>One shell destination and input/focus lifetime for every media renderer.</summary>
internal sealed partial class MediaFullscreenLayer : Grid
{
    private static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached(
        "MediaFullscreenHost", typeof(MediaFullscreenLayer), typeof(MediaFullscreenLayer), new PropertyMetadata(null));
    private Presentation? current;
    private long generation;
    private readonly HashSet<ControllerButton> ownedButtons = [];
    private readonly Grid parking = new() { Visibility = Visibility.Collapsed };
    private IDisposable? theme;
    private bool closing;
    internal bool IsTransitioning { get; private set; }
    internal bool IsClosing => closing;
    internal bool IsOpen => current is not null;
    internal object? Owner => current?.Owner;
    internal Func<bool>? CanRestoreFocus { get; set; }
    internal event Action? Changed;

    internal sealed record Presentation(object Owner, FrameworkElement Content,
        Func<ControllerButton, ControllerEventPhase, bool> Handle,
        Action<FocusNavigationDirection> Navigate, Action Focus, Action Restore, Action ReturnFocus,
        Func<short, short, bool>? AnalogMove = null, Action? Settled = null,
        Rect? Origin = null, Action? Prepare = null, FrameworkElement[]? Chrome = null,
        bool UseXamlTransform = false, Func<Rect?>? MediaBounds = null, FrameworkElement? MotionContent = null)
    {
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal MediaFullscreenLayer()
    {
        IsHitTestVisible = false;
        Children.Add(parking);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        InitializeTransition();
        AutomationProperties.SetAutomationId(this, "Overlay.MediaFullscreenLayer");
        KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape && IsOpen) { args.Handled = true; Close(); }
        };
        Loaded += (_, _) => { theme?.Dispose(); theme = NativePopupTheme.FullscreenMedia(this); };
        Unloaded += (_, _) => { Close(restoreFocus: false); theme?.Dispose(); theme = null; };
    }

    internal void Attach(Grid root) { root.SetValue(HostProperty, this); root.Children.Add(this); }
    internal void Park(FrameworkElement content) => parking.Children.Add(content);
    internal MediaPresentationStage CreateMediaStage(double aspectRatio)
    {
        var stage = new MediaPresentationStage(aspectRatio);
        Children.Insert(Children.IndexOf(backdrop) + 1, stage);
        stage.SetCovered(IsOpen);
        return stage;
    }
    internal static MediaFullscreenLayer? Find(DependencyObject source)
    {
        for (DependencyObject? item = source; item is not null; item = VisualTreeHelper.GetParent(item))
            if (item.GetValue(HostProperty) is MediaFullscreenLayer layer) return layer;
        return null;
    }

    internal Task ShowAsync(Presentation presentation)
    {
        if (IsOpen) throw new InvalidOperationException("A media presentation is already expanded.");
        var version = ++generation;
        current = presentation;
        foreach (var stage in Children.OfType<MediaPresentationStage>())
            stage.SetCovered(!ReferenceEquals(stage, presentation.MotionContent));
        IsTransitioning = true;
        presentation.Content.IsHitTestVisible = false;
        var name = AutomationProperties.GetName(presentation.Owner as DependencyObject ?? presentation.Content);
        AutomationProperties.SetName(this, string.IsNullOrWhiteSpace(name) ? "Fullscreen media" : name);
        if (ReferenceEquals(presentation.Content.Parent, parking)) parking.Children.Remove(presentation.Content);
        Children.Add(presentation.Content);
        IsHitTestVisible = true;
        backdrop.Visibility = Visibility.Visible;
        Changed?.Invoke();
        UpdateLayout();
        PrepareEntrance(presentation, version);
        return presentation.Completion.Task;
    }

    internal void Close(bool restoreFocus = true)
    {
        if (current is not { } previous) return;
        if (closing && restoreFocus) return;
        var version = ++generation;
        closing = true;
        IsTransitioning = true;
        previous.Content.IsHitTestVisible = false;
        Changed?.Invoke();
        if (restoreFocus && !entranceQueued && IsLoaded && CanRestoreFocus?.Invoke() != false)
            _ = CloseTransitionAsync(previous, version);
        else CompleteClose(previous, version, restoreFocus);
    }

    private void CompleteClose(Presentation previous, long version, bool restoreFocus)
    {
        if (version != generation || !ReferenceEquals(current, previous)) return;
        ResetTransition();
        current = null;
        closing = false;
        IsTransitioning = false;
        previous.Content.IsHitTestVisible = true;
        Children.Remove(previous.Content);
        IsHitTestVisible = false;
        backdrop.Visibility = Visibility.Collapsed;
        previous.Restore();
        foreach (var stage in Children.OfType<MediaPresentationStage>()) stage.SetCovered(false);
        Changed?.Invoke();
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (generation == version || !ReferenceEquals(current?.Owner, previous.Owner)) previous.Settled?.Invoke();
            if (restoreFocus && generation == version && current is null && CanRestoreFocus?.Invoke() != false)
                previous.ReturnFocus();
            previous.Completion.TrySetResult();
        })) previous.Completion.TrySetResult();
    }

    internal void FocusContent() { if (!IsTransitioning) current?.Focus(); }
    internal void MoveFocus(FocusNavigationDirection direction) { if (!IsTransitioning) current?.Navigate(direction); }
    internal bool MoveAnalog(short x, short y) => !IsTransitioning && current?.AnalogMove?.Invoke(x, y) == true;
    internal bool Route(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase == ControllerEventPhase.Released && ownedButtons.Remove(button))
        { current?.Handle(button, phase); return true; }
        if (current is not { } active) return false;
        if (phase == ControllerEventPhase.Pressed) ownedButtons.Add(button);
        if (IsTransitioning)
        {
            if (!closing && phase == ControllerEventPhase.Pressed && button is ControllerButton.B or ControllerButton.RightStick) Close();
            return true;
        }
        if (!active.Handle(button, phase) && button == ControllerButton.B && phase == ControllerEventPhase.Pressed) Close();
        return true;
    }
}
