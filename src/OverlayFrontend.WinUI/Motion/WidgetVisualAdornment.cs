using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>One owned XAML child slot with independent ordered decoration leases.</summary>
internal sealed class WidgetVisualAdornment : IDisposable
{
    internal enum Layer { Depth, Focus }
    private static readonly ConditionalWeakTable<FrameworkElement, Owner> owners = new();
    private readonly Owner owner;
    private readonly Layer layer;
    private bool disposed;
    internal ContainerVisual Visual { get; }
    internal static bool CanAttach(FrameworkElement element) =>
        ElementCompositionPreview.GetElementChildVisual(element) is not { } child ||
        owners.TryGetValue(element, out var owner) && ReferenceEquals(child, owner.Root);

    internal static WidgetVisualAdornment? Acquire(FrameworkElement element, Layer layer)
    {
        if (!owners.TryGetValue(element, out var owner))
        {
            // Foreign child visuals belong to media or other native owners.
            if (ElementCompositionPreview.GetElementChildVisual(element) is not null) return null;
            owner = new(element);
            owners.Add(element, owner);
        }
        if (!ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(element), owner.Root) || owner.Leases.ContainsKey(layer))
            return null;
        return new(owner, layer);
    }

    private WidgetVisualAdornment(Owner owner, Layer layer)
    {
        this.owner = owner; this.layer = layer;
        Visual = owner.Root.Compositor.CreateContainerVisual();
        if (layer == Layer.Depth) owner.Root.Children.InsertAtBottom(Visual);
        else owner.Root.Children.InsertAtTop(Visual);
        owner.Leases.Add(layer, this);
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        owner.Root.Children.Remove(Visual);
        Visual.Children.RemoveAll(); Visual.Dispose();
        owner.Leases.Remove(layer);
        if (owner.Leases.Count != 0) return;
        if (ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(owner.Element), owner.Root))
            ElementCompositionPreview.SetElementChildVisual(owner.Element, null);
        owners.Remove(owner.Element); owner.Root.Dispose();
    }

    private sealed class Owner
    {
        internal FrameworkElement Element { get; }
        internal ContainerVisual Root { get; }
        internal Dictionary<Layer, WidgetVisualAdornment> Leases { get; } = [];
        internal Owner(FrameworkElement element)
        {
            Element = element;
            // No GetElementVisual: facade Scale/ScaleTransition keep their ownership.
            Root = CompositionTarget.GetCompositorForCurrentThread().CreateContainerVisual();
            ElementCompositionPreview.SetElementChildVisual(element, Root);
        }
    }
}
