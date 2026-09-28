using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Resolved shell roles on ordinary native controls; no replacement item template or input policy.</summary>
internal sealed class ShellChromeStyles : IDisposable
{
    private readonly Dictionary<FrameworkElement, (string Role, NativeComputedStyleAdapter Style)> elements = [];
    private readonly Dictionary<SelectorItem, (NativeComputedStyleAdapter Style, long Selection)> items = [];
    private readonly List<ListView> trays = [];
    private IReadOnlyDictionary<string, BridgeNodeRenderStyles>? palette;
    private bool disposed;

    internal void Register(FrameworkElement element, string role)
    {
        if (elements.Remove(element, out var previous)) previous.Style.Dispose();
        var style = new NativeComputedStyleAdapter(element);
        elements.Add(element, (role, style)); style.Update(palette?.GetValueOrDefault(role));
    }

    internal void Attach(ListView tray)
    {
        trays.Add(tray);
        tray.ContainerContentChanging += ContainerChanged;
        Register(tray, "tray");
    }

    private void ContainerChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not SelectorItem item) return;
        if (args.InRecycleQueue) { Remove(item); return; }
        TrackItem(item);
    }

    internal void TrackItem(SelectorItem item)
    {
        if (!items.ContainsKey(item))
        {
            var style = new NativeComputedStyleAdapter(item);
            var selection = item.RegisterPropertyChangedCallback(SelectorItem.IsSelectedProperty, (_, _) => UpdateItem(item));
            items.Add(item, (style, selection));
        }
        UpdateItem(item);
    }

    internal void Update(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? next, AppearanceSettings settings, bool animationsEnabled)
    {
        if (disposed) return;
        NativeComputedStyleAdapter.SetTextScale(settings.TextScale);
        NativeComputedStyleAdapter.SetAccessibilityPolicy(settings);
        NativeComputedStyleAdapter.SetMotionPolicy(settings, animationsEnabled);
        if (ReferenceEquals(next, palette)) return;
        palette = next;
        foreach (var value in elements.Values) value.Style.Update(palette?.GetValueOrDefault(value.Role));
        foreach (var item in items.Keys) UpdateItem(item);
    }

    private void UpdateItem(SelectorItem item)
    {
        if (disposed || !items.TryGetValue(item, out var owner)) return;
        var basic = palette?.GetValueOrDefault(item.IsSelected ? "tray-item:selected" : "tray-item")?.Base;
        var focused = palette?.GetValueOrDefault(item.IsSelected ? "tray-item:selected:focused" : "tray-item:focused")?.Base;
        owner.Style.Update(basic is null ? null : new() { Base = basic, Focused = focused ?? basic, Pressed = focused ?? basic });
    }

    private void Remove(SelectorItem item)
    {
        if (!items.Remove(item, out var owner)) return;
        item.UnregisterPropertyChangedCallback(SelectorItem.IsSelectedProperty, owner.Selection);
        owner.Style.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var tray in trays) tray.ContainerContentChanging -= ContainerChanged;
        foreach (var item in items.Keys.ToArray()) Remove(item);
        foreach (var element in elements.Values) element.Style.Dispose();
        trays.Clear(); elements.Clear(); palette = null;
    }
}
