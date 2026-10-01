using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>
/// Native focus-event adapter for a host-styled decoration. Place Adornment over
/// its control in the same layout cell. No button content/style transforms are owned here.
/// </summary>
internal sealed class WidgetFocusMotion : IDisposable
{
    private readonly Control control;
    private readonly Grid layer = new();
    private readonly bool systemFocusVisuals;
    private WidgetMotionOptions options;
    private WidgetCompositionTarget? target;
    private WidgetCompositionMotion? motion;
    private Vector2 size;
    private bool focused;
    private bool disposed;
    internal Grid Adornment { get; } = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    internal WidgetFocusMotion(Control control, UIElement decoration, AppearanceSettings appearance, bool systemAnimationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(control); ArgumentNullException.ThrowIfNull(decoration);
        this.control = control; options = WidgetMotionOptions.From(appearance, systemAnimationsEnabled);
        systemFocusVisuals = control.UseSystemFocusVisuals;
        control.UseSystemFocusVisuals = false;
        layer.Children.Add(decoration); Adornment.Children.Add(layer);
        AutomationProperties.SetAccessibilityView(Adornment, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(decoration, AccessibilityView.Raw);
        control.GotFocus += FocusChanged; control.LostFocus += FocusChanged;
        // Use the owner's geometry: a collapsed adornment cannot measure itself.
        control.SizeChanged += SizeChanged;
        control.Loaded += Loaded; control.Unloaded += Unloaded;
        if (control.IsLoaded) Initialize();
    }

    internal void ApplyAppearance(AppearanceSettings appearance, bool systemAnimationsEnabled)
    {
        Check();
        var next = WidgetMotionOptions.From(appearance, systemAnimationsEnabled);
        if (next == options) return;
        options = next;
        motion?.Cancel();
        focused = OwnsFocus();
        target?.Set(WidgetMotionPose.Identity with { Opacity = focused ? 1 : 0 });
    }

    private void Loaded(object sender, RoutedEventArgs args) => Initialize();
    private void Unloaded(object sender, RoutedEventArgs args) => Retire();
    private void SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (size == new Vector2((float)args.NewSize.Width, (float)args.NewSize.Height)) return;
        Retire(); Initialize();
    }

    private void Initialize()
    {
        if (disposed || target is not null || !control.IsLoaded || control.ActualWidth <= 0 || control.ActualHeight <= 0) return;
        size = new((float)control.ActualWidth, (float)control.ActualHeight);
        target = WidgetCompositionTarget.ForElement(layer, Adornment, size);
        motion = new(ElementCompositionPreview.GetElementVisual(layer).Compositor, control.DispatcherQueue);
        focused = OwnsFocus();
        target.Set(WidgetMotionPose.Identity with { Opacity = focused ? 1 : 0 });
        Adornment.Visibility = Visibility.Visible;
    }

    private void FocusChanged(object sender, RoutedEventArgs args)
    {
        if (disposed) return;
        Initialize();
        var next = OwnsFocus();
        if (next == focused) return;
        focused = next;
        if (target is not null && motion is not null)
            _ = motion.PlayAsync((WidgetMotionPlayback[])[new(target, WidgetMotionPolicy.Focus(options, size, focused))]);
    }

    private bool OwnsFocus()
    {
        if (control.XamlRoot is null) return false;
        for (var current = FocusManager.GetFocusedElement(control.XamlRoot) as DependencyObject;
             current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, control)) return true;
        return false;
    }

    private void Retire()
    {
        motion?.Dispose(); motion = null;
        target?.Dispose(); target = null;
        Adornment.Visibility = Visibility.Collapsed;
    }
    private void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!control.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Focus motion requires the control's dispatcher.");
    }
    public void Dispose()
    {
        if (disposed) return;
        Check();
        control.GotFocus -= FocusChanged; control.LostFocus -= FocusChanged;
        control.SizeChanged -= SizeChanged; control.Loaded -= Loaded; control.Unloaded -= Unloaded;
        Retire(); control.UseSystemFocusVisuals = systemFocusVisuals;
        layer.Children.Clear(); Adornment.Children.Clear(); disposed = true;
    }
}
