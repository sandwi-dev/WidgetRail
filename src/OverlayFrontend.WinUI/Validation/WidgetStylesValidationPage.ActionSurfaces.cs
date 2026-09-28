using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeActionSurfaceMeasureAsync()
    {
        var root = new ViewNode { Id = "tile", Kind = ViewNodeKind.ActionSurface, ActionId = "open", AccessibilityLabel = "Application",
            ActionSurfaceOrientation = ActionSurfaceOrientation.Horizontal, Children = (ViewNode[])[
                new() { Id = "mark", Kind = ViewNodeKind.Spacer },
                new() { Id = "copy", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                    new() { Id = "title", Kind = ViewNodeKind.Text,
                        Text = "A deliberately long application name that must wrap and ellipsize inside the remaining tile width" },
                    new() { Id = "subtitle", Kind = ViewNodeKind.Text, Text = "Application source" }] }] };
        const string css = """
            #tile { width: 300px; min-height: 78px; padding: 9px 10px; gap: 10px; align: center; }
            #mark { width: 56px; height: 56px; }
            #copy { min-width: 0px; flex-grow: 1; gap: 2px; }
            #title { font-size: 15px; max-lines: 2; text-overflow: ellipsis; }
            #subtitle { font-size: 12px; }
            """;
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            ["tile"] = Compute(css, "tile", "actionSurface"), ["mark"] = Compute(css, "mark", "spacer"),
            ["copy"] = Compute(css, "copy", "stack"), ["title"] = Compute(css, "title", "text"),
            ["subtitle"] = Compute(css, "subtitle", "text"),
        };
        foreach (var fragmentOnly in new[] { false, true })
        {
            var page = new WidgetViewPresenter(fragmentOnly) { Width = 300 };
            host.Children.Add(page);
            try
            {
                var frame = CreateFrame(root, styles);
                if (fragmentOnly) page.ApplyFragment(frame, root, root.Id); else page.Apply(frame);
                await Wait(() => Within<TextBlock>(page, "Widget.title")?.ActualWidth > 0);
                var title = Within<TextBlock>(page, "Widget.title")!;
                var mark = Within<Border>(page, "Widget.mark")!;
                var origin = title.TransformToVisual(page).TransformPoint(new(0, 0));
                Check(title.ActualWidth < 240 && origin.X + title.ActualWidth <= page.ActualWidth + 1 && Near(mark.ActualWidth, 56),
                    $"{(fragmentOnly ? "indexed" : "ordinary")} horizontal action surface gives its growing content a finite native width");
                Check(title.ActualHeight > 25 && title.MaxLines == 2 && title.TextTrimming == TextTrimming.CharacterEllipsis,
                    $"{(fragmentOnly ? "indexed" : "ordinary")} application title wraps and trims inside its own box instead of clipping at the tile edge");
            }
            finally { host.Children.Remove(page); await page.DisposeAsync(); }
        }
        var gridPage = new WidgetViewPresenter { Width = 600 };
        host.Children.Add(gridPage);
        try
        {
            gridPage.Apply(CreateFrame(new() { Id = "grid", Kind = ViewNodeKind.Grid, GridMinimumColumnWidth = 160, GridMaximumColumns = 2,
                Children = (ViewNode[])[new() { Id = "short", Kind = ViewNodeKind.Button, Text = "Short", ActionId = "short" },
                    new() { Id = "long", Kind = ViewNodeKind.Button, Text = "A longer catalog title", ActionId = "long" }] },
                new Dictionary<string, BridgeNodeRenderStyles>()));
            await Wait(() => Within<Button>(gridPage, "Widget.short")?.ActualWidth is > 250 and < 310 &&
                Within<Button>(gridPage, "Widget.long")?.ActualWidth is > 250 and < 310);
            var shortWidth = Within<Button>(gridPage, "Widget.short")!.ActualWidth;
            var longWidth = Within<Button>(gridPage, "Widget.long")!.ActualWidth;
            var pixel = 1 / gridPage.XamlRoot.RasterizationScale;
            Check(Math.Abs(shortWidth - longWidth) <= pixel + .001 && Math.Abs(shortWidth - 294) <= pixel,
                $"static native grid cells share their constrained width within one physical pixel ({shortWidth:R}, {longWidth:R})");
        }
        finally { host.Children.Remove(gridPage); await gridPage.DisposeAsync(); }
        var responsivePage = new WidgetViewPresenter { Width = 1020, Height = 600 };
        host.Children.Add(responsivePage);
        try
        {
            responsivePage.Apply(CreateFrame(root with { Children = (ViewNode[])[root.Children[0] with { VisibleWhen = ResponsiveVisibility.ExpandedOnly }, root.Children[1]] }, styles));
            await Wait(() => Within<Border>(responsivePage, "Widget.mark")?.Visibility == Visibility.Visible &&
                Within<TextBlock>(responsivePage, "Widget.title")?.ActualWidth > 0);
            var title = Within<TextBlock>(responsivePage, "Widget.title")!;
            var expandedWidth = title.ActualWidth;
            responsivePage.Width = 800;
            await Wait(() => Within<Border>(responsivePage, "Widget.mark")?.Visibility == Visibility.Collapsed && title.ActualWidth > expandedWidth + 50);
            Check(title.ActualWidth > expandedWidth + 50,
                "resizing an ordinary action surface removes hidden child tracks and their gaps");
            responsivePage.Width = 1020;
            await Wait(() => Within<Border>(responsivePage, "Widget.mark")?.Visibility == Visibility.Visible && Near(title.ActualWidth, expandedWidth));
            Check(Near(title.ActualWidth, expandedWidth), "expanding restores the same constrained action surface layout");
        }
        finally { host.Children.Remove(responsivePage); await responsivePage.DisposeAsync(); }
        static T? Within<T>(DependencyObject owner, string id) where T : FrameworkElement
        {
            if (owner is T match && AutomationProperties.GetAutomationId(match) == id) return match;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); ++index)
                if (Within<T>(VisualTreeHelper.GetChild(owner, index), id) is { } child) return child;
            return null;
        }
    }
}
