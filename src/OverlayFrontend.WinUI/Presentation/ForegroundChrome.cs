using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Keeps the header's layout, focus targets and pointer hit area live while its paint fades.</summary>
internal sealed class ForegroundChrome : IDisposable
{
    private readonly FrameworkElement element;
    private readonly Action<double?> paint;
    private readonly ScalarTransition? originalTransition;
    private readonly ForegroundPaintObserver observer;
    private double? authoredOpacity;
    private bool pointerInside, hidden, revealed;

    internal ForegroundChrome(FrameworkElement element, Action<double?> paint)
    {
        this.element = element;
        this.paint = paint;
        originalTransition = element.OpacityTransition;
        observer = new(element, OnPaint);
        element.PointerEntered += Enter;
        element.PointerExited += Exit;
        element.GotFocus += FocusChanged;
        element.LostFocus += FocusChanged;
    }

    internal void Apply(double? opacity)
    {
        authoredOpacity = opacity;
        paint(hidden ? 0 : opacity);
    }

    private void OnPaint(bool visibleGame, TimeSpan duration)
    {
        revealed = visibleGame;
        if (!element.IsLoaded) pointerInside = false;
        Refresh(duration);
    }
    private void Enter(object sender, PointerRoutedEventArgs e) { pointerInside = true; Refresh(TimeSpan.Zero); }
    private void Exit(object sender, PointerRoutedEventArgs e) { pointerInside = false; Refresh(TimeSpan.Zero); }
    private void FocusChanged(object sender, RoutedEventArgs e) => Refresh(TimeSpan.Zero);

    private void Refresh(TimeSpan duration)
    {
        var next = revealed && !pointerInside && !DialogChoiceNavigation.ContainsFocus(element);
        if (hidden == next) return;
        hidden = next;
        element.OpacityTransition = duration > TimeSpan.Zero ? new() { Duration = duration } : null;
        paint(hidden ? 0 : authoredOpacity);
    }
    public void Dispose()
    {
        observer.Dispose();
        element.OpacityTransition = originalTransition;
        element.PointerEntered -= Enter;
        element.PointerExited -= Exit;
        element.GotFocus -= FocusChanged;
        element.LostFocus -= FocusChanged;
    }
}
