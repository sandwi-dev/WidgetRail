using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Authored paint on the stock selector presenter, without replacing navigation or its template.</summary>
internal sealed class WidgetNativeSelectorPaint : IDisposable
{
    private readonly SelectorItem owner;
    private readonly long templateToken;
    private readonly Dictionary<DependencyProperty, object> original = [];
    private ListViewItemPresenter? target;
    private Brush? background, foreground, border;
    private CornerRadius? radius;
    internal WidgetNativeSelectorPaint(SelectorItem owner)
    {
        this.owner = owner;
        owner.Loaded += Loaded;
        templateToken = owner.RegisterPropertyChangedCallback(Control.TemplateProperty, (_, _) =>
        { Restore(); target = null; Apply(); });
    }
    internal void Update(Brush? background, Brush? foreground, Brush? border, CornerRadius? radius)
    { this.background = background; this.foreground = foreground; this.border = border; this.radius = radius; Apply(); }
    private void Loaded(object sender, RoutedEventArgs args) => Apply();
    private void Apply()
    {
        if (target is null)
        {
            owner.ApplyTemplate();
            target = Find(owner);
        }
        if (target is null) return;
        Put(ContentPresenter.BackgroundProperty, background);
        Put(ContentPresenter.ForegroundProperty, foreground);
        Put(ContentPresenter.BorderBrushProperty, border);
        Put(ContentPresenter.CornerRadiusProperty, radius);
        Put(ListViewItemPresenter.SelectedBackgroundProperty, background);
        Put(ListViewItemPresenter.PointerOverBackgroundProperty, background);
        Put(ListViewItemPresenter.SelectedPointerOverBackgroundProperty, background);
        Put(ListViewItemPresenter.PressedBackgroundProperty, background);
        Put(ListViewItemPresenter.SelectedPressedBackgroundProperty, background);
        Put(ListViewItemPresenter.SelectedDisabledBackgroundProperty, background);
        Put(ListViewItemPresenter.SelectedForegroundProperty, foreground);
        Put(ListViewItemPresenter.PointerOverForegroundProperty, foreground);
    }
    private void Put(DependencyProperty property, object? value)
    {
        if (value is null)
        {
            if (!original.Remove(property, out var prior)) return;
            if (ReferenceEquals(prior, DependencyProperty.UnsetValue)) target!.ClearValue(property);
            else target!.SetValue(property, prior);
        }
        else
        {
            original.TryAdd(property, target!.ReadLocalValue(property));
            if (!Equals(target.GetValue(property), value)) target.SetValue(property, value);
        }
    }
    private void Restore() { foreach (var property in original.Keys.ToArray()) Put(property, null); }
    private static ListViewItemPresenter? Find(DependencyObject node)
    {
        if (node is ListViewItemPresenter presenter) return presenter;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); ++i)
            if (Find(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }
    public void Dispose()
    { owner.Loaded -= Loaded; owner.UnregisterPropertyChangedCallback(Control.TemplateProperty, templateToken); Restore(); target = null; }
}
