using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>A layout/input slot borrowing the shell's durable browser.</summary>
internal sealed partial class BrowserSlot : ContentControl
{
    internal Grid Host { get; } = new();
    private readonly Border focusOutline = new() { BorderThickness = new(2), CornerRadius = new(6), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private IDisposable? focusTheme;
    internal bool IsFocusOutlineVisible => focusOutline.Visibility == Visibility.Visible;
    private readonly TextBlock placeholder = new() { Text = "Opening browser…", TextWrapping = TextWrapping.Wrap, Margin = new(16) };
    internal BrowserSurface? Surface { get; private set; }
    internal Action? PlacementChanged { get; set; }
    internal Action? InteractionChanged { get; set; }
    internal bool Active { get; set; }
    internal bool AcceptsInput { get; set; }
    internal bool Pinned { get; set; }
    internal bool Retired { get; private set; }
    internal bool IsProviderDocument { get; set; }
    private Windows.Foundation.Rect providerViewport;
    private bool providerInViewport, providerPlacementQueued;
    internal bool CanDisplay => Active && IsLoaded && !Retired && (!IsProviderDocument || providerInViewport);
    internal bool IsInteracting => Surface?.IsInteracting == true;
    internal bool IsBrowsing => Surface?.IsBrowsing == true;
    internal bool PresentedInPin { get; private set; }

    internal BrowserSlot()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        MinHeight = 200; MinWidth = 200;
        Host.Margin = new(3);
        Host.Children.Add(placeholder);
        var frame = new Grid(); frame.Children.Add(Host); frame.Children.Add(focusOutline); Content = frame;
        Loaded += (_, _) => { focusTheme?.Dispose(); focusTheme = NativePopupTheme.BrowserSlot(this); RefreshFocusPresentation(); PlacementChanged?.Invoke(); };
        EffectiveViewportChanged += (_, args) => { providerViewport = args.EffectiveViewport; RefreshProviderViewport(); };
        SizeChanged += (_, _) => { RefreshProviderViewport(); RefreshProviderHeight(); };
        GotFocus += (_, _) => { Surface?.RefreshInteractionFocus(); RefreshFocusPresentation(); };
        LostFocus += (_, _) => { Surface?.RefreshInteractionFocus(); RefreshFocusPresentation(); DispatcherQueue.TryEnqueue(RefreshFocusPresentation); };
        Unloaded += (_, _) => { focusTheme?.Dispose(); focusTheme = null; focusOutline.Visibility = Visibility.Collapsed; PlacementChanged?.Invoke(); };
    }
    private void RefreshProviderViewport()
    {
        if (!IsProviderDocument) return;
        var bounds = providerViewport;
        bounds.Intersect(new(0, 0, ActualWidth, ActualHeight));
        var value = bounds.Width > 0 && bounds.Height > 0;
        if (value == providerInViewport) return;
        providerInViewport = value;
        // EffectiveViewportChanged runs during layout. Native reparenting must
        // wait for the current measure/arrange pass to settle.
        if (providerPlacementQueued) return;
        providerPlacementQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        { providerPlacementQueued = false; if (!Retired) PlacementChanged?.Invoke(); });
    }
    internal void SetSurface(BrowserSurface? surface)
    {
        if (!ReferenceEquals(Surface, surface) && ReferenceEquals(Surface?.IsInteractionFocused?.Target, this))
        { Surface!.IsInteractionFocused = null; Surface.ProviderHeightChanged = null; }
        Surface = surface;
        if (surface is not null) PresentedInPin = false;
        placeholder.Visibility = surface is null ? Visibility.Visible : Visibility.Collapsed;
        if (surface is not null)
        { surface.IsInteractionFocused = OwnsFocus; surface.FocusInteraction = surface.ExitInteraction = () => Focus(FocusState.Keyboard);
            surface.InteractionChanged = () => { RefreshFocusPresentation(); InteractionChanged?.Invoke(); }; surface.RefreshInteractionFocus();
            surface.ProviderHeightChanged = RefreshProviderHeight; }
        RefreshProviderHeight();
    }
    private void RefreshProviderHeight()
    {
        if (!IsProviderDocument || !double.IsNaN(Height)) { Host.ClearValue(HeightProperty); return; }
        Host.Height = Math.Clamp(Surface?.ProviderContentHeight ?? 72, 0, Math.Max(0, Math.Min(MaxHeight, 4096) - 6));
    }
    internal void ApplyFocusTheme(Brush focus, double radius)
    { focusOutline.BorderBrush = focus; focusOutline.CornerRadius = new(Math.Max(2, radius)); }
    internal void RefreshFocusPresentation() => focusOutline.Visibility = !Retired && IsLoaded && !IsInteracting && OwnsFocus() ? Visibility.Visible : Visibility.Collapsed;
    private bool OwnsFocus()
    {
        if (!AcceptsInput || !CanDisplay || XamlRoot is null) return false;
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, this)) return true;
        return false;
    }
    internal void SetMessage(string message) { placeholder.Text = message; }
    internal void SetBorrowed(bool pinned)
    {
        var changed = PresentedInPin != pinned;
        PresentedInPin = pinned;
        SetMessage(pinned ? "This page is pinned. Press View to interact, or unpin it to browse here." : "This page is open in the main widget.");
        if (changed) InteractionChanged?.Invoke();
    }
    internal void Enter() => Surface?.Enter();
    internal bool HandleButton(ControllerButton button, ControllerEventPhase phase) => Surface?.HandleButton(button, phase) == true;
    internal bool MoveFocus(FocusNavigationDirection direction) => Surface?.MoveFocus(direction) == true;
    internal bool Scroll(double x, double y) => Surface?.Scroll(x, y) == true;
    internal void Retire() { Retired = true; Active = false; AcceptsInput = false; PlacementChanged?.Invoke(); PlacementChanged = null; }
}
