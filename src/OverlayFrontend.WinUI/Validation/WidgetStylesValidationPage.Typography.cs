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
    private async Task NativeTypographyAsync()
    {
        const string source = "Several words followed by unbreakablelongcatalogidentifier";
        var text = new TextBlock { Text = source, Width = 140, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        host.Children.Add(text);
        using (var adapter = new NativeComputedStyleAdapter(text, false))
        {
            try
            {
                adapter.Update(Compute("#text { font-size: 20px; line-height: 1.5; max-lines: 2; overflow-wrap: anywhere; text-overflow: ellipsis; text-transform: uppercase; letter-spacing: 2px; }", "text", "text"));
                text.UpdateLayout();
                Check(text.Text == source.ToUpperInvariant() && text.MaxLines == 2 && text.TextTrimming == TextTrimming.CharacterEllipsis,
                    "text casing, line cap and ellipsis apply through native TextBlock");
                Check(text.TextWrapping == TextWrapping.Wrap && Near(text.LineHeight, 30) && text.LineStackingStrategy == LineStackingStrategy.BlockLineHeight,
                    "anywhere wrapping and authored uniform line height use native text layout");
                Check(text.DesiredSize.Height > 30 && text.DesiredSize.Height <= 61,
                    $"two capped native lines occupy their line budget ({text.DesiredSize.Height:R})");

                WidgetTextStyleAdapter.SetSource(text, "New Mixed source");
                Check(text.Text == "NEW MIXED SOURCE", "retained text replacement is transformed from its latest source");
                WidgetTextStyleAdapter.SetSource(text, "NEW MIXED SOURCE");
                adapter.Update(Compute("#text { font-size: 20px; max-lines: 1; overflow-wrap: anywhere; text-overflow: clip; text-transform: lowercase; }", "text", "text"));
                Check(text.Text == "new mixed source" && text.TextWrapping == TextWrapping.NoWrap && text.TextTrimming == TextTrimming.Clip,
                    "one line disables wrapping even with anywhere; clip removes ellipsis");
                adapter.Update(null);
                Check(text.Text == "NEW MIXED SOURCE" && text.TextWrapping == TextWrapping.Wrap && text.MaxLines == 0 && text.FontSize == 12 &&
                    ReferenceEquals(text.ReadLocalValue(TextBlock.LineHeightProperty), DependencyProperty.UnsetValue),
                    "style removal restores latest authored source even when its previous display string was unchanged");

                adapter.Update(Compute("#text { font-size: 20px; line-height: 1.5; max-lines: 2; overflow-wrap: normal; letter-spacing: 2px; }", "text", "text"));
                Check(text.TextWrapping == TextWrapping.Wrap && text.CharacterSpacing == 100,
                    "normal wrapping retains native overflow fallback and pixel tracking matches authored spacing");
                NativeComputedStyleAdapter.SetTextScale(1.5);
                await Wait(() => text.FontSize == 30 && Near(text.LineHeight, 45));
                Check(text.CharacterSpacing == 100, "text scale increases pixel tracking and line height together with the font");
            }
            finally { NativeComputedStyleAdapter.SetTextScale(1); host.Children.Remove(text); }
        }

        var page = new WidgetViewPresenter { Width = 180 };
        host.Children.Add(page);
        try
        {
            var buttonNode = new ViewNode { Id = "label-button", Kind = ViewNodeKind.Button, Text = source, ActionId = "activate" };
            var style = Compute("#label-button { font-size: 20px; line-height: 1.5; max-lines: 2; text-overflow: ellipsis; overflow-wrap: anywhere; text-transform: uppercase; } " +
                "#label-button:focused { text-transform: lowercase; }", "label-button", "button");
            var styles = new Dictionary<string, BridgeNodeRenderStyles> { [buttonNode.Id] = style };
            page.Apply(CreateFrame(buttonNode, styles));
            await Wait(() => page.IsLoaded);
            var button = (Button)FindTypographyElement(page, "Widget.label-button")!;
            outside.Focus(FocusState.Programmatic);
            await Wait(() => button.FocusState == FocusState.Unfocused);
            var label = (TextBlock)button.Content;
            await Wait(() => label.Text == source.ToUpperInvariant());
            Check(label.Text == source.ToUpperInvariant() && label.MaxLines == 2 && label.TextTrimming == TextTrimming.CharacterEllipsis && Near(label.LineHeight, 30),
                $"ordinary button labels share text layout, clipping and case styles (lines={label.MaxLines}, trim={label.TextTrimming}, height={label.LineHeight:R})");
            button.Focus(FocusState.Programmatic);
            await Wait(() => label.Text == source.ToLowerInvariant());
            Check(AutomationProperties.GetName(button) == source, "display casing does not rewrite accessibility or action declarations");
            page.Apply(CreateFrame(buttonNode with { Text = "New Label" }, styles));
            Check(ReferenceEquals(label, button.Content) && label.Text == "new label",
                "focused button refresh retains its label and uses the fresh authored text");

            page.Apply(CreateFrame(buttonNode with { Glyph = WidgetGlyph.Music }, styles));
            button = (Button)FindTypographyElement(page, "Widget.label-button")!;
            var grid = (Grid)button.Content;
            var glyphLabel = grid.Children.OfType<TextBlock>().Single();
            page.UpdateLayout();
            Check(grid.ColumnDefinitions.Count == 2 && grid.ColumnDefinitions[1].Width.IsStar && glyphLabel.MaxLines == 2 &&
                glyphLabel.ActualWidth > 0 && glyphLabel.ActualWidth <= button.ActualWidth,
                "glyph button gives its label finite native Grid space for wrapping and trimming");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }

        var fragment = new WidgetViewPresenter(presentationOnly: true);
        fragment.UseIndexedContainerStyles(); host.Children.Add(fragment);
        try
        {
            var node = new ViewNode { Id = "indexed-label", Kind = ViewNodeKind.Button, Text = "Shared catalog label", ActionId = "activate" };
            var styles = new Dictionary<string, BridgeNodeRenderStyles> { [node.Id] = Compute(
                "#indexed-label { max-lines: 2; text-overflow: ellipsis; text-transform: uppercase; } #indexed-label:focused { text-transform: lowercase; max-lines: 1; }", node.Id, "button") };
            fragment.ApplyFragment(CreateFrame(node, styles), node, node.Id);
            var label = (TextBlock)fragment.Content;
            fragment.SetIndexedRootInteraction(true, false);
            Check(label.Text == "shared catalog label" && label.MaxLines == 1 && label.TextWrapping == TextWrapping.NoWrap,
                "indexed fragment focus updates text styles without transferring box ownership");
            fragment.SetIndexedRootInteraction(false, false);
            Check(label.Text == "SHARED CATALOG LABEL" && label.MaxLines == 2 && label.TextTrimming == TextTrimming.CharacterEllipsis,
                "indexed fragment focus exit restores base typography");
        }
        finally { host.Children.Remove(fragment); await fragment.DisposeAsync(); }
    }

    private static FrameworkElement? FindTypographyElement(DependencyObject root, string id)
    {
        if (root is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            if (FindTypographyElement(VisualTreeHelper.GetChild(root, index), id) is { } match) return match;
        return null;
    }
}
