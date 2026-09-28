using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>Dedicated clip and transform layers; authored controls own their own style/scale.</summary>
internal sealed class WidgetMotionHost : Grid
{
    internal Grid Layer { get; } = new();
    internal Grid SurfaceLayer { get; } = new();
    internal Border? SelectionSurface { get; }
    internal WidgetMotionHost(FrameworkElement content, bool selection = false)
    {
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Stretch;
        if (selection)
        {
            SelectionSurface = new() { IsHitTestVisible = false };
            SurfaceLayer.Children.Add(SelectionSurface); Children.Add(SurfaceLayer);
        }
        Layer.Children.Add(content);
        Children.Add(Layer);
    }
}

/// <summary>Outgoing content paints but contributes no semantic or navigation destinations.</summary>
internal sealed class WidgetOutgoingLayer : Canvas
{
    internal WidgetOutgoingLayer()
    {
        IsHitTestVisible = false;
        GettingFocus += (_, args) => args.TryCancel();
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new EmptyPeer(this);
    private sealed class EmptyPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override List<AutomationPeer> GetChildrenCore() => [];
        protected override bool IsContentElementCore() => false;
        protected override bool IsControlElementCore() => false;
    }
}

/// <summary>Only the current root participates in native measurement.</summary>
internal sealed class WidgetMotionStage : Panel
{
    internal WidgetOutgoingLayer Outgoing { get; } = new();
    internal FrameworkElement? Current { get; private set; }
    internal FrameworkElement? Modal { get; private set; }
    internal WidgetMotionStage() => Children.Add(Outgoing);
    internal void SetModal(FrameworkElement? value)
    {
        if (ReferenceEquals(Modal, value)) return;
        if (Modal is not null) Children.Remove(Modal);
        Modal = value;
        if (value is not null) Children.Insert(Children.Count - 1, value);
        InvalidateMeasure();
    }
    internal void SetCurrent(FrameworkElement? value)
    {
        if (ReferenceEquals(Current, value)) return;
        if (Current is not null) Children.Remove(Current);
        Current = value;
        if (value is not null) Children.Insert(0, value);
        InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Current?.Measure(availableSize);
        Modal?.Measure(availableSize);
        Outgoing.Measure(availableSize);
        return Current?.DesiredSize ?? new();
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Current?.Arrange(new(new(), finalSize));
        Modal?.Arrange(new(new(), finalSize));
        Outgoing.Arrange(new(new(), finalSize));
        return finalSize;
    }
}
