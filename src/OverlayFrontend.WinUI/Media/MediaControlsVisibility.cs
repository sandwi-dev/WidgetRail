using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Shared non-layout-changing chrome visibility policy for media surfaces.</summary>
internal sealed class MediaControlsVisibility(FrameworkElement[] controls, Func<bool> canHide, Action beforeHide)
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool pointerOverControls;
    internal bool Visible => controls[0].Visibility == Visibility.Visible;
    internal void Initialize()
    {
        timer.Tick += (_, _) => { if (!pointerOverControls && canHide()) Hide(); };
        foreach (var control in controls)
        {
            control.PointerEntered += (_, _) => { pointerOverControls = true; timer.Stop(); };
            control.PointerExited += (_, _) => { pointerOverControls = false; Reveal(); };
        }
    }
    internal void Reveal()
    {
        foreach (var control in controls) { control.Visibility = Visibility.Visible; control.IsHitTestVisible = true; }
        timer.Stop();
        if (!pointerOverControls && canHide()) timer.Start();
    }
    internal void Hide()
    {
        timer.Stop(); beforeHide();
        foreach (var control in controls) { control.IsHitTestVisible = false; control.Visibility = Visibility.Collapsed; }
    }
    internal void Stop() { timer.Stop(); pointerOverControls = false; }
}
