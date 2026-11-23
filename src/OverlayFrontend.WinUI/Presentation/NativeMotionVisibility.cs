using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Shared decorative-motion visibility; owns no source or decoder.</summary>
internal sealed class NativeMotionVisibility : IDisposable
{
    // WinUI caches effective viewports on the element and does not replay an
    // unchanged value to a listener attached after asynchronous image decode.
    // Observe when the presentation element is created; source/policy owners
    // can then come and go without losing its native viewport state. Weak keys
    // bind this small observation to the element, never to a decoded image.
    private static readonly ConditionalWeakTable<FrameworkElement, ViewportObservation> observations = new();
    internal static void Prepare(FrameworkElement target) => _ = observations.GetValue(target, static element => new(element));
    private sealed class ViewportObservation
    {
        internal Rect? Viewport { get; private set; }
        internal bool IsAttached { get; private set; }
        internal event Action? Changed;
        internal ViewportObservation(FrameworkElement target)
        {
            // IsLoaded also reflects pending Loaded-event delivery in WinUI.
            // Adding a source-lifetime listener to an already active element can
            // make that property false until a later load cycle. Retain actual
            // attachment events from the element's lifetime instead.
            IsAttached = target.IsLoaded;
            target.Loaded += (_, _) => { IsAttached = true; Changed?.Invoke(); };
            target.Unloaded += (_, _) =>
            { if (!target.IsLoaded) { IsAttached = false; Changed?.Invoke(); } };
            target.SizeChanged += (_, _) => Changed?.Invoke();
            target.EffectiveViewportChanged += (_, args) =>
            { Viewport = args.EffectiveViewport; Changed?.Invoke(); };
        }
    }
    private readonly FrameworkElement target;
    private readonly ViewportObservation observation;
    private readonly Func<bool> allowed;
    private readonly Action<bool> changed;
    private XamlRoot? root;
    private bool disposed;
    internal NativeMotionVisibility(FrameworkElement target, Func<bool> allowed, Action<bool> changed)
    {
        this.target = target; this.allowed = allowed; this.changed = changed;
        observation = observations.GetValue(target, static element => new(element));
        observation.Changed += Refresh;
    }
    internal void Refresh()
    {
        if (disposed) return;
        if (!ReferenceEquals(root, target.XamlRoot))
        {
            if (root is not null) root.Changed -= RootChanged;
            root = target.XamlRoot;
            if (root is not null) root.Changed += RootChanged;
        }
        var visible = observation.Viewport is { } viewport && viewport.Width > 0 && viewport.Height > 0 &&
            viewport.Right > 0 && viewport.Bottom > 0 && viewport.Left < target.ActualWidth && viewport.Top < target.ActualHeight;
        var play = visible && observation.IsAttached && root?.IsHostVisible == true && allowed();
        for (DependencyObject? parent = target; play && parent is not null; parent = PresentationLayers.LogicalParent(parent))
            if (parent is FrameworkElement { Visibility: Visibility.Collapsed }) play = false;
        changed(play);
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Refresh();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        observation.Changed -= Refresh;
        if (root is not null) root.Changed -= RootChanged;
        changed(false);
    }
}
