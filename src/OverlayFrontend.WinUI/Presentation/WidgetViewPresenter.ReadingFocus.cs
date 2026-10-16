using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private bool readingFocusRefreshQueued;
    private bool readingNeighborsDirty;
    private readonly Dictionary<ScrollViewer,WidgetReadingScrollFocus> readingScrolls=[];
    private Binding? scrollFocusOutline;

    private void QueueReadingFocusRefresh(bool neighborsChanged)
    {
        readingNeighborsDirty|=neighborsChanged;
        if (disposed || readingFocusRefreshQueued) return;
        readingFocusRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            readingFocusRefreshQueued = false;
            if (disposed) return;
            // Geometry/input events only refresh presentation. Candidate lists
            // are rebuilt on a declaration/appearance/responsive update.
            RefreshScrollFocusOutline();
            if(readingNeighborsDirty){readingNeighborsDirty=false;UpdateNativeNeighbors();}
            if (needsEntry) QueueEntryFocus();
        })) readingFocusRefreshQueued = false;
    }

    private void RefreshReadingFocus()
    {
        // Build ancestor membership once per declaration/layout-policy change, never
        // per controller frame. Nested scrolling regions own their own entry.
        var targets = new Dictionary<string,List<FrameworkElement>>(StringComparer.Ordinal);
        foreach (var binding in bindings.Values)
        {
            var node = declarations[binding.Identity.Id].Node;
            if (binding.Identity.Scope != activeScope || node.IsDisabled == true || !IsMemoryVisible(binding.Element) ||
                !(node.IsFocusable || binding.Element is WidgetIndexedCollectionView ||
                    binding.Element is ScrollViewer)) continue;
            for (var parent = declarations[binding.Identity.Id].ParentId; parent is not null; parent = declarations[parent].ParentId)
            {
                if (declarations[parent].Identity.Scope != activeScope) break;
                if(bindings[parent].Element is ScrollViewer)
                {
                    if(!targets.TryGetValue(parent,out var entries))targets[parent]=entries=[];
                    entries.Add(binding.Element);
                }
            }
        }
        foreach (var binding in bindings.Values)
            if (binding.Element is ScrollViewer scroll && readingScrolls.TryGetValue(scroll,out var focus))
                focus.Configure(!presentationOnly && presentationActive && binding.Identity.Scope == activeScope &&
                    declarations[binding.Identity.Id].Node.IsDisabled != true && IsMemoryVisible(scroll),
                    targets.TryGetValue(binding.Identity.Id,out var entries)?entries:Array.Empty<FrameworkElement>());
        RefreshScrollFocusOutline();
    }

    private void RefreshScrollFocusOutline()
    {
        Binding? next=null;
        // A proxy outline belongs only to a settled directional reading gesture.
        // Ordinary control focus can precede BringIntoView: being offscreen during
        // that reveal does not make the viewport the focus target. Free scrolling
        // likewise retains its anchor until SettleScrollFocus runs after release.
        if(scrollGesture is null&&!disposed&&presentationActive&&presentationInputEnabled&&automaticFocusEnabled&&!presentationOnly&&!HasTransientControl&&XamlRoot is not null)
        {
            var focused=FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement;
            var viewport=directionalScroll is {Pending:false} gesture && ReferenceEquals(focused,gesture.NativeAnchor)
                ? gesture.Viewport : null;
            if(viewport is not null&&focused is not null&&!ReferenceEquals(focused,viewport)&&
                !WidgetReadingScrollFocus.IsVisible(focused,viewport)&&FindBinding(viewport) is {} owner&&
                owner.Identity.Scope==activeScope&&IsMemoryVisible(viewport))next=owner;
        }
        if(ReferenceEquals(scrollFocusOutline,next))return;
        var previous=scrollFocusOutline;scrollFocusOutline=next;
        foreach(var binding in new[]{previous,next})
            if(binding is not null&&bindings.TryGetValue(binding.Identity.Id,out var current)&&ReferenceEquals(binding,current))
                ApplyComputedStyles(binding,declarations[binding.Identity.Id].Node);
    }

    private bool VisibleNavigationTarget(Binding target)
    {
        for(var parent=declarations[target.Identity.Id].ParentId;parent is not null;parent=declarations[parent].ParentId)
            if(bindings[parent].Element is ScrollViewer scroll)
                return WidgetReadingScrollFocus.IsVisible(target.Element,scroll);
        return true;
    }

    private Binding? ViewportEntryTarget(Binding target,Binding origin)
    {
        if(VisibleNavigationTarget(target)||SameScrollControlNavigation(origin,target))return target;
        for(var parent=declarations[target.Identity.Id].ParentId;parent is not null;parent=declarations[parent].ParentId)
            if(bindings[parent] is {Element:ScrollViewer {IsTabStop:true}} viewport&&NavigationTarget(viewport)&&
                !IsDescendant(origin.Identity.Id,parent)&&VisibleNavigationTarget(viewport))return viewport;
        return null;
    }

    private bool SameScrollControlNavigation(Binding origin,Binding target)
    {
        // Ordinary button-to-button navigation keeps its existing reveal
        // behavior. A reading viewport instead scrolls until an option appears.
        if(origin.Element is ScrollViewer)return false;
        var scroll=NearestScroll(target.Element);
        if(scroll is null)return false;
        for(DependencyObject? parent=origin.Element;parent is not null&&!ReferenceEquals(parent,this);parent=VisualTreeHelper.GetParent(parent))
            if(ReferenceEquals(parent,scroll))return true;
        return false;
    }
}
