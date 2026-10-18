using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Focus policy for a native reading viewport; no extra layout control.</summary>
internal sealed class WidgetReadingScrollFocus : IDisposable
{
    private readonly ScrollViewer scroll;
    private readonly Action<bool> changed;
    private readonly long heightToken, widthToken, focusToken;
    private bool admitted;
    private IReadOnlyList<FrameworkElement> targets=Array.Empty<FrameworkElement>();
    internal static bool HasOverflow(ScrollViewer scroll) =>
        scroll.HorizontalScrollMode != ScrollMode.Disabled && scroll.ScrollableWidth > .5 ||
        scroll.VerticalScrollMode != ScrollMode.Disabled && scroll.ScrollableHeight > .5;

    internal WidgetReadingScrollFocus(ScrollViewer scroll, Action<bool> changed)
    {
        this.scroll=scroll;this.changed=changed;
        scroll.IsTabStop=false;scroll.UseSystemFocusVisuals=true;
        heightToken=scroll.RegisterPropertyChangedCallback(ScrollViewer.ScrollableHeightProperty,(_,_)=>GeometryChanged());
        widthToken=scroll.RegisterPropertyChangedCallback(ScrollViewer.ScrollableWidthProperty,(_,_)=>GeometryChanged());
        focusToken=scroll.RegisterPropertyChangedCallback(Control.FocusStateProperty,(_,_)=>GeometryChanged());
        scroll.Loaded+=Loaded;scroll.LostFocus+=LostFocus;scroll.ViewChanged+=ViewChanged;
    }
    private void Loaded(object sender,RoutedEventArgs args)=>GeometryChanged();
    private void LostFocus(object sender,RoutedEventArgs args)=>changed(Refresh());
    private void ViewChanged(object? sender,ScrollViewerViewChangedEventArgs args)=>changed(Refresh());
    private void GeometryChanged()=>changed(Refresh());
    internal void Configure(bool enabled,IReadOnlyList<FrameworkElement> candidates)
    {admitted=enabled;targets=candidates;Refresh();}
    internal static bool IsVisible(FrameworkElement element,ScrollViewer viewport)
    {
        if(!element.IsLoaded||element.XamlRoot!=viewport.XamlRoot||element.ActualWidth<=0||element.ActualHeight<=0||
            element is Control {IsEnabled:false})return false;
        for(DependencyObject? parent=element;parent is not null;parent=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
        {
            if(parent is FrameworkElement {Visibility:Visibility.Collapsed})return false;
            if(parent is ScrollViewer clip)
            {
                var bounds=element.TransformToVisual(clip).TransformBounds(new(0,0,element.ActualWidth,element.ActualHeight));
                var visible = WidgetScrollViewport.ContentBounds(clip);
                if(bounds.Right<=visible.Left+.5||bounds.Bottom<=visible.Top+.5||bounds.X>=visible.Right-.5||bounds.Y>=visible.Bottom-.5)return false;
                if(ReferenceEquals(clip,viewport))return true;
            }
        }
        return false;
    }
    private bool Refresh()
    {
        // Shrinking content must not strand the current navigation origin.
        var visibleTarget=targets.Any(target=>(target is not ScrollViewer nested||nested.IsTabStop||HasOverflow(nested))&&IsVisible(target,scroll));
        var value=admitted&&(!visibleTarget&&HasOverflow(scroll)||scroll.FocusState!=FocusState.Unfocused);
        var different=scroll.IsTabStop!=value;scroll.IsTabStop=value;return different;
    }
    public void Dispose()
    {
        scroll.UnregisterPropertyChangedCallback(ScrollViewer.ScrollableHeightProperty,heightToken);
        scroll.UnregisterPropertyChangedCallback(ScrollViewer.ScrollableWidthProperty,widthToken);
        scroll.UnregisterPropertyChangedCallback(Control.FocusStateProperty,focusToken);
        scroll.Loaded-=Loaded;scroll.LostFocus-=LostFocus;scroll.ViewChanged-=ViewChanged;
    }
}
