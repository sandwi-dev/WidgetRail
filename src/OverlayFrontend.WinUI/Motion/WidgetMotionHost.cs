using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>Dedicated clip and transform layers; authored controls own their own style/scale.</summary>
internal sealed partial class WidgetMotionHost : Grid
{
    internal Grid Layer { get; }
    internal Grid Viewport { get; }
    internal Grid? ArtworkLayer { get; }
    internal WidgetOutgoingLayer? Outgoing { get; private set; }
    internal WidgetOutgoingLayer EnsureOutgoing()
    {
        if (Outgoing is not null) return Outgoing;
        Outgoing = new();
        Children.Add(Outgoing);
        return Outgoing;
    }
    internal void ClearOutgoing()
    {
        if (Outgoing is null) return;
        Outgoing.Children.Clear(); Children.Remove(Outgoing); Outgoing = null;
    }
    internal Grid SurfaceLayer { get; } = new();
    private (Grid Shadow, Grid Edges)? depthSlots;
    internal (Grid Shadow, Grid Edges) EnsureDepthSlots()
    {
        if (depthSlots is { } current) return current;
        var shadow = new Grid { IsHitTestVisible = false };
        var edges = new Grid { IsHitTestVisible = false };
        var owner = SelectionSurface is null ? Layer : SurfaceLayer;
        owner.Children.Insert(0, shadow); owner.Children.Add(edges);
        return (depthSlots = (shadow, edges)).Value;
    }
    internal Border? SelectionSurface { get; }
    internal WidgetMotionHost(FrameworkElement content, bool selection = false, bool animated = true)
    {
        Layer = animated ? new Grid() : this;
        Viewport = animated ? new Grid() : this;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Stretch;
        if (selection)
        {
            SelectionSurface = new() { IsHitTestVisible = false };
            SurfaceLayer.Children.Add(SelectionSurface); Children.Add(SurfaceLayer);
        }
        if (content is Presentation.WidgetArtworkView)
        {
            ArtworkLayer = new();
            ArtworkLayer.Children.Add(content);
            Layer.Children.Add(ArtworkLayer);
        }
        else Layer.Children.Add(content);
        if (animated) { Viewport.Children.Add(Layer); Children.Add(Viewport); }
    }
}

/// <summary>Outgoing content paints but contributes no semantic or navigation destinations.</summary>
internal sealed partial class WidgetOutgoingLayer : Canvas
{
    internal WidgetOutgoingLayer()
    {
        IsHitTestVisible = false;
        GettingFocus += (_, args) => args.TryCancel();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new EmptyPeer(this);
    private sealed partial class EmptyPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override List<AutomationPeer> GetChildrenCore() => [];
        protected override bool IsContentElementCore() => false;
        protected override bool IsControlElementCore() => false;
    }
}

/// <summary>Only the current root participates in native measurement.</summary>
internal sealed partial class WidgetMotionStage : Panel
{
    internal WidgetOutgoingLayer Outgoing { get; } = new();
    internal FrameworkElement? Current { get; private set; }
    internal FrameworkElement? Modal { get; private set; }
    private PresentationPortal? modalPortal;
    internal WidgetMotionStage()
    {
        Children.Add(Outgoing);
        Loaded += (_, _) => MountModal();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new StagePeer(this);
    private sealed partial class StagePeer(WidgetMotionStage owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
        protected override List<AutomationPeer> GetChildrenCore()
        {
            var active = owner.Modal ?? owner.Current;
            if (owner.modalPortal is not null && owner.Modal is { } modal && CreatePeerForElement(modal) is { } peer)
                return [peer];
            // Reuse native descendant peers; the stable visual parent remains
            // mounted but is not an accessibility destination behind a modal.
            return base.GetChildrenCore()?.Where(peer => peer is FrameworkElementAutomationPeer element &&
                InActiveBranch(element.Owner, active)).ToList() ?? [];
        }
        private static bool InActiveBranch(DependencyObject? element, FrameworkElement? active)
        {
            if (active is null) return false;
            while (element is not null)
            {
                if (ReferenceEquals(element, active)) return true;
                element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
            }
            return false;
        }
    }
    internal void SetModal(FrameworkElement? value)
    {
        if (ReferenceEquals(Modal, value)) return;
        modalPortal?.Dispose(); modalPortal = null;
        if (Modal is not null) Children.Remove(Modal);
        Modal = value;
        MountModal();
        FrameworkElementAutomationPeer.FromElement(this)?.InvalidatePeer();
        InvalidateMeasure();
    }
    private void MountModal()
    {
        if (Modal is null || modalPortal is not null) return;
        if (PresentationLayers.Find(this) is { } layers)
        {
            Children.Remove(Modal);
            modalPortal = new(layers, this, Modal);
        }
        else if (!Children.Contains(Modal)) Children.Insert(Children.Count - 1, Modal);
    }
    internal PresentationPortal? RetainModalPortal()
    {
        if (modalPortal is not { } portal) return null;
        modalPortal = null; Modal = null;
        portal.RetainForExit();
        FrameworkElementAutomationPeer.FromElement(this)?.InvalidatePeer();
        return portal;
    }
    internal void SetCurrent(FrameworkElement? value)
    {
        if (ReferenceEquals(Current, value)) return;
        if (Current is not null) Children.Remove(Current);
        Current = value;
        if (value is not null) Children.Insert(0, value);
        FrameworkElementAutomationPeer.FromElement(this)?.InvalidatePeer();
        InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Current?.Measure(availableSize);
        if (modalPortal is null) Modal?.Measure(availableSize);
        Outgoing.Measure(availableSize);
        return Current?.DesiredSize ?? new();
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Current?.Arrange(new(new(), finalSize));
        if (modalPortal is null) Modal?.Arrange(new(new(), finalSize));
        Outgoing.Arrange(new(new(), finalSize));
        return finalSize;
    }
}
