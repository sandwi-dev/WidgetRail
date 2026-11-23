using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Observes the nearest background surface while its descendant is attached.</summary>
internal sealed class ForegroundPaintObserver : IDisposable
{
    private readonly FrameworkElement element;
    private readonly Action<bool, TimeSpan> changed;
    private WidgetPresentationSurface? owner;
    private bool disposed;
    internal bool Revealed => owner?.IsForegroundRevealed == true;

    internal ForegroundPaintObserver(FrameworkElement element, Action<bool, TimeSpan> changed)
    {
        this.element = element;
        this.changed = changed;
        element.Loaded += Loaded;
        element.Unloaded += Unloaded;
        if (element.IsLoaded) Attach();
    }

    private void Loaded(object sender, RoutedEventArgs e) { if (!disposed) Attach(); }
    private void Unloaded(object sender, RoutedEventArgs e) { if (!element.IsLoaded) Detach(); }
    private void Attach()
    {
        if (owner != null) return;
        for (var parent = PresentationLayers.LogicalParent(element); parent != null; parent = PresentationLayers.LogicalParent(parent))
            if (parent is WidgetPresentationSurface { Fragment: null } surface)
            {
                owner = surface;
                surface.ForegroundPaintChanged += Notify;
                Notify(TimeSpan.Zero);
                break;
            }
    }
    private void Detach()
    {
        if (owner == null) return;
        owner.ForegroundPaintChanged -= Notify;
        owner = null;
        Notify(TimeSpan.Zero);
    }
    private void Notify(TimeSpan duration) => changed(Revealed, duration);
    public void Dispose()
    {
        disposed = true;
        element.Loaded -= Loaded;
        element.Unloaded -= Unloaded;
        Detach();
    }
}
