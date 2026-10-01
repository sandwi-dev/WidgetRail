using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>
/// Catalog content only: the containing native control owns focus, selection and input.
/// A radial or list item can reuse the icon without retaining another input surface.
/// </summary>
internal sealed partial class WidgetCatalogItemContent : ContentControl, IDisposable
{
    private readonly Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> resolve;
    private readonly StackPanel panel = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Grid stage = new();
    private readonly Border selectedMark = new() { Height = 4, CornerRadius = new(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private SelectorItem? selectionOwner;
    private long selectionToken;
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
    private WidgetPackageIconView? icon;
    private BridgeWidgetDescriptor? item;
    private bool disposed;
    private double iconSize = 24;
    internal double TileSize
    {
        set
        {
            var extent = Math.Max(1, value);
            Width = Height = extent;
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.VerticalAlignment = VerticalAlignment.Center;
            selectedMark.Width = Math.Max(1, extent - 2 * Math.Min(18, extent * .28));
            selectedMark.Height = Math.Min(4, extent * .12);
        }
    }
    internal double IconSize
    {
        get => iconSize;
        set
        {
            iconSize = double.IsFinite(value) ? Math.Clamp(value, 1, 128) : 24;
            if (icon is not null) { icon.Width = icon.Height = icon.FontSize = iconSize; }
        }
    }

    internal WidgetCatalogItemContent(
        Func<BridgeWidgetDescriptor, string, CancellationToken, Task<WidgetPresentationPackageIcon>> resolve)
    {
        this.resolve = resolve;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false; IsHitTestVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        panel.Children.Add(label); stage.Children.Add(panel); stage.Children.Add(selectedMark); Content = stage;
        selectedMark.SetBinding(Border.BackgroundProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = this, Path = new PropertyPath(nameof(Foreground)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        DataContextChanged += (_, args) => SetItem(args.NewValue as BridgeWidgetDescriptor);
        Loaded += (_, _) => { PresentIcon(); AttachSelection(); };
        Unloaded += (_, _) => { ReleaseIcon(); DetachSelection(); };
    }

    // Hosts can hide the label for radial items; their button supplies the accessible name.
    internal bool ShowLabel { get => label.Visibility == Visibility.Visible; set => label.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }

    internal void SetItem(BridgeWidgetDescriptor? descriptor)
    {
        if (disposed) return;
        item = descriptor;
        label.Text = descriptor?.Name ?? string.Empty;
        AutomationProperties.SetName(this, label.Text);
        if (descriptor is null) ReleaseIcon();
        else PresentIcon();
    }

    private void PresentIcon()
    {
        if (disposed || !IsLoaded || item is not { } captured) return;
        if (icon is null)
        {
            icon = new() { Width = IconSize, Height = IconSize, FontSize = IconSize };
            panel.Children.Insert(0, icon);
        }
        var asset = captured.IconAssets.FirstOrDefault(candidate => candidate.AssetId == captured.PackageIcon?.AssetId);
        var generation = $"{captured.Id}|{captured.InstanceId}|{captured.RuntimeGeneration}|{captured.PresentationGeneration}|{captured.PackageContentDigest}|{asset?.SourceSha256}|{asset?.NormalizedSha256}";
        icon.Update(new() { Id = "catalog.icon", Kind = ViewNodeKind.Icon, Glyph = captured.Icon,
            PackageIcon = captured.PackageIcon, AccessibilityLabel = captured.Name },
            (assetId, token) => resolve(captured, assetId, token), generation, null);
    }

    private void ReleaseIcon()
    {
        if (icon is null) return;
        icon.Dispose(); panel.Children.Remove(icon); icon = null;
    }

    private void AttachSelection()
    {
        DetachSelection();
        for (var ancestor = VisualTreeHelper.GetParent(this); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is SelectorItem item)
            {
                selectionOwner = item;
                selectionToken = item.RegisterPropertyChangedCallback(SelectorItem.IsSelectedProperty, (_, _) => UpdateSelection());
                UpdateSelection(); break;
            }
    }
    private void UpdateSelection() => selectedMark.Visibility = !ShowLabel && selectionOwner?.IsSelected == true ? Visibility.Visible : Visibility.Collapsed;
    private void DetachSelection()
    {
        selectionOwner?.UnregisterPropertyChangedCallback(SelectorItem.IsSelectedProperty, selectionToken);
        selectionOwner = null; selectedMark.Visibility = Visibility.Collapsed;
    }

    public void Dispose() { if (disposed) return; disposed = true; ReleaseIcon(); DetachSelection(); item = null; }
}
