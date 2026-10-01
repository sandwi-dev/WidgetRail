using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeStyleParityAsync()
    {
        NativeComputedStyleAdapter.SetAccessibilityPolicy(WidgetRail.PlatformSettings.AppearanceSettings.Default with
            { BoldText = false, Contrast = WidgetRail.PlatformSettings.ContrastPreference.Standard });
        var groupHeader = new WidgetGroupHeader { Text = "Recommended albums", HeaderStyle =
            Compute("#header { color: #4abbee; font-size: 21px; font-weight: 500; }", "header", "text") };
        host.Children.Add(groupHeader);
        var headerText = (TextBlock)groupHeader.Content;
        await Wait(() => NativeComputedStyleAdapter.For(headerText) is not null);
        Check(headerText.FontSize == 21 && headerText.FontWeight.Weight == 500 && ColorOf(headerText.Foreground) == Windows.UI.Color.FromArgb(255, 0x4a, 0xbb, 0xee),
            $"collection group header uses shared resolved typography and theme color [size={headerText.FontSize}, weight={headerText.FontWeight.Weight}, color={ColorOf(headerText.Foreground)}]");
        groupHeader.HeaderStyle = Compute("#header { color: #eeaa44; font-size: 22px; }", "header", "text");
        Check(headerText.FontSize == 22 && ColorOf(headerText.Foreground) == Windows.UI.Color.FromArgb(255, 0xee, 0xaa, 0x44),
            "collection group header updates with theme without recreating its native label");
        host.Children.Remove(groupHeader);

        var originalWidth = presenter.Width; var originalHeight = presenter.Height;
        presenter.SetSurfaceCornerRadius(12);
        var shellClip = (Microsoft.UI.Composition.CompositionGeometricClip)Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(presenter).Clip;
        var shellGeometry = (Microsoft.UI.Composition.CompositionRoundedRectangleGeometry)shellClip.Geometry;
        presenter.Width = 480; presenter.Height = 240;
        await Wait(() => shellGeometry.Size.X == 480 && shellGeometry.Size.Y == 240);
        Check(shellGeometry.CornerRadius.X == 12, "shell clips the complete widget tree to the same rounded perimeter as its backdrop");
        presenter.SetSurfaceCornerRadius(20);
        Check(shellGeometry.CornerRadius.X == 20, "widget outer clip follows theme radius without replacing content");
        presenter.SetSurfaceCornerRadius(0);
        presenter.Width = originalWidth; presenter.Height = originalHeight;

        var buttonNode = new ViewNode { Id = "round", Kind = ViewNodeKind.Button,
            Glyph = WidgetGlyph.Play, Text = "", AccessibilityLabel = "Play", ActionId = "play" };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["round"] = Compute("#round { width: 60px; height: 60px; shape: circle; padding: 12px; background: #226688; }", "round", "button")
        };
        presenter.Apply(CreateFrame(buttonNode, styles));
        await Wait(() => Find<Button>("Widget.round") is { ActualWidth: 60, CornerRadius.TopLeft: 30 });
        var button = Find<Button>("Widget.round")!;
        var panel = (Grid)button.Content;
        var icon = panel.Children.OfType<WidgetPackageIconView>().Single();
        var bounds = icon.TransformToVisual(button).TransformBounds(new(0, 0, icon.ActualWidth, icon.ActualHeight));
        Check(Math.Abs(bounds.Left + bounds.Width / 2 - 30) < 1 && Math.Abs(bounds.Top + bounds.Height / 2 - 30) < 1,
            "icon-only circular transport glyph is centered without an empty label column");
        styles["round"] = Compute("#round { width: 80px; height: 80px; shape: circle; padding: 12px; }", "round", "button");
        presenter.Apply(CreateFrame(buttonNode, styles));
        await Wait(() => button.CornerRadius.TopLeft == 40);
        Check(ReferenceEquals(button, Find<Button>("Widget.round")), "circle radius follows resize without replacing the control");
        presenter.Apply(CreateFrame(buttonNode with { Text = "Play" }, styles));
        Check(panel.ColumnSpacing == 8 && panel.Children.OfType<TextBlock>().Single().Visibility == Visibility.Visible,
            "adding a button label restores its separate text column");

        var root = new ViewNode { Id = "bounded", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
            new() { Id = "strip", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Horizontal, Children = (ViewNode[])[
                new() { Id = "session", Kind = ViewNodeKind.Button, Text = "Session", ActionId = "session" }] },
            new() { Id = "following", Kind = ViewNodeKind.Text, Text = "Now Playing" }] };
        styles = new()
        {
            ["bounded"] = Compute("#bounded { height: 400px; gap: 8px; }", "bounded", "stack"),
            ["strip"] = Compute("#strip { min-height: 46px; max-height: 46px; }", "strip", "scroll")
        };
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<ScrollViewer>("Widget.strip") is { IsLoaded: true, ActualHeight: > 0 });
        presenter.UpdateLayout();
        var strip = Find<ScrollViewer>("Widget.strip")!;
        var parent = Find<Grid>("Widget.bounded")!;
        var following = Find<TextBlock>("Widget.following")!;
        var y = following.TransformToVisual(parent).TransformPoint(new()).Y;
        // Native layout rounds fractional DIPs to this monitor's pixel grid.
        Check(Math.Abs(strip.ActualHeight - 46) <= 1 / XamlRoot.RasterizationScale && y >= 53 && y < 56 && parent.RowDefinitions[0].Height.IsAuto,
            $"bounded session strip does not consume a star row or create a Now Playing gap [height={strip.ActualHeight}, following={y}, track={parent.RowDefinitions[0].Height}]");

        styles["bounded"] = Compute("#bounded { direction: row; height: 100px; gap: 8px; }", "bounded", "stack");
        presenter.Apply(CreateFrame(root, styles)); presenter.UpdateLayout();
        Check(parent.RowDefinitions.Count == 0 && parent.ColumnDefinitions.Count == 3,
            "authored direction controls container flow independently of its semantic stack node");

        var selector = new ListViewItem { Width = 64, Height = 64, Padding = new(0), Content = new Grid() };
        host.Children.Add(selector);
        using var paint = new NativeComputedStyleAdapter(selector);
        try
        {
            var itemStyle = Compute("#tile { background: #224466; corner-radius: 12px; } #tile:focused { background: #335577; }", "tile", "button");
            paint.Update(itemStyle);
            await Wait(() => selector.IsLoaded && ScrollParts(selector).OfType<ListViewItemPresenter>().Any());
            foreach (var selected in new[] { true, false, true })
            {
                selector.IsSelected = selected;
                outside.Focus(FocusState.Keyboard);
                await Task.Delay(30);
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(selector);
                var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                var at = ((bitmap.PixelHeight / 2) * bitmap.PixelWidth + bitmap.PixelWidth / 2) * 4;
                Check(pixels[at] == 0x66 && pixels[at + 1] == 0x44 && pixels[at + 2] == 0x22 && pixels[at + 3] == 255,
                    "native selector raster keeps authored background through selected/unselected transitions");
            }
            Check(ScrollParts(selector).OfType<ListViewItemPresenter>().Single().CornerRadius.TopLeft == 12,
                "stock selector template uses authored rounded corners");
        }
        finally { host.Children.Remove(selector); }
    }
}
