using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeLabelAlignmentAsync()
    {
        const string copy = "A long control label that wraps across several lines with a short final phrase";
        const string metrics = "width: 210px; font-size: 18px; max-lines: 3; line-height: 1.25; overflow-wrap: anywhere;";
        var nodes = new ViewNode[]
        {
            new() { Id = "aligned-button", Kind = ViewNodeKind.Button, Text = copy, ActionId = "activate" },
            new() { Id = "aligned-glyph", Kind = ViewNodeKind.Button, Text = copy, ActionId = "activate", Glyph = WidgetGlyph.Music },
            new() { Id = "aligned-select", Kind = ViewNodeKind.Select, Text = copy, AccessibilityLabel = "Choice",
                AccessibilityValue = copy, SelectOptions = new WidgetSelectOption[] { new("only", copy, "choose", IsSelected: true) } },
            new() { Id = "aligned-entry", Kind = ViewNodeKind.TextEntry, Text = copy, TextEntryValue = copy,
                TextEntryPlaceholder = "Enter text", TextEntryMaximumLength = 96, AccessibilityLabel = "Text", AccessibilityValue = copy, ActionId = "commit" },
        };
        foreach (var node in nodes)
        {
            var page = new WidgetViewPresenter { Width = 220 };
            page.SetAutomaticFocusEnabled(false);
            host.Children.Add(page);
            try
            {
                Render("text-align: center;", "text-align: end;", "text-align: start;");
                await Wait(() => Find<Button>("Widget." + node.Id, page)?.IsLoaded == true);
                var button = Find<Button>("Widget." + node.Id, page)!;
                var label = button.Content is TextBlock text ? text : ((Grid)button.Content).Children.OfType<TextBlock>().Single();
                outside.Focus(FocusState.Programmatic);
                await Wait(() => label.TextAlignment == TextAlignment.Center);
                page.UpdateLayout();
                Check(label.TextWrapping == TextWrapping.Wrap && label.ActualHeight > 23 && label.TextAlignment == TextAlignment.Center,
                    node.Id + " centers individual lines inside its wrapped native label");
                button.Focus(FocusState.Keyboard);
                await Wait(() => label.TextAlignment == TextAlignment.Right);
                Check(label.TextAlignment == TextAlignment.Right,
                    node.Id + " focused end alignment reaches the native text layout");
                var adapter = NativeComputedStyleAdapter.For(button)!;
                adapter.SetControllerPressed(true);
                Check(label.TextAlignment == TextAlignment.Left,
                    node.Id + " pressed start alignment reaches the same retained label");
                adapter.SetControllerPressed(false);
                Render("");
                Check(ReferenceEquals(label, button.Content is TextBlock plain ? plain : ((Grid)button.Content).Children.OfType<TextBlock>().Single()) &&
                    ReferenceEquals(label.ReadLocalValue(TextBlock.TextAlignmentProperty), DependencyProperty.UnsetValue) && label.TextAlignment == TextAlignment.Left,
                    node.Id + " removing text-align restores native alignment without replacing its label");
                page.FlowDirection = FlowDirection.RightToLeft;
                Render("text-align: end;");
                Check(label.TextAlignment == TextAlignment.Left,
                    node.Id + " RTL end alignment reuses the shared logical-direction mapping");
                Render("text-align: start;");
                Check(label.TextAlignment == TextAlignment.Right,
                    node.Id + " RTL start alignment reuses the shared logical-direction mapping");

                void Render(string normal, string focused = "", string pressed = "") => page.Apply(CreateFrame(node,
                    new Dictionary<string, BridgeNodeRenderStyles> { [node.Id] = Compute(
                        "#" + node.Id + " { " + metrics + normal + " } #" + node.Id + ":focused { " + focused + " } #" + node.Id + ":pressed { " + pressed + " }", node.Id, "button") }));
            }
            finally { host.Children.Remove(page); await page.DisposeAsync(); }
        }

        var authored = new TextBlock { Text = copy, TextAlignment = TextAlignment.Right };
        using (var adapter = new NativeComputedStyleAdapter(authored, false))
        {
            adapter.Update(Compute("#local-alignment { text-align: center; }", "local-alignment", "text"));
            Check(authored.TextAlignment == TextAlignment.Center, "plain text uses the same line-alignment owner as control labels");
            adapter.Update(null);
            Check(authored.TextAlignment == TextAlignment.Right, "removing text-align restores an existing native local value");
        }

        var fragment = new WidgetViewPresenter(presentationOnly: true) { Width = 220 };
        fragment.UseIndexedContainerStyles(); host.Children.Add(fragment);
        try
        {
            var child = new ViewNode { Id = "aligned-descendant", Kind = ViewNodeKind.Text, Text = copy };
            var root = new ViewNode { Id = "aligned-item", Kind = ViewNodeKind.ActionSurface,
                ActionSurfaceOrientation = WidgetRail.WidgetProtocol.ActionSurfaceOrientation.Vertical,
                ActionId = "open", AccessibilityLabel = "Item", Children = new ViewNode[] { child } };
            var styles = new Dictionary<string, BridgeNodeRenderStyles>
            {
                [root.Id] = Compute("#aligned-item { text-align: end; } #aligned-item:focused { text-align: start; }", root.Id, "button"),
                [child.Id] = Compute("#aligned-descendant { " + metrics + "text-align: center; }", child.Id, "text"),
            };
            fragment.ApplyFragment(CreateFrame(root, styles), root, root.Id);
            await Wait(() => Find<TextBlock>("Widget." + child.Id, fragment)?.IsLoaded == true);
            var label = Find<TextBlock>("Widget." + child.Id, fragment)!;
            fragment.SetIndexedRootInteraction(true, false);
            Check(label.TextAlignment == TextAlignment.Center, "indexed descendant keeps its authored alignment when the item root gains focus");
            styles.Remove(child.Id);
            fragment.ApplyFragment(CreateFrame(root, styles), root, root.Id);
            Check(ReferenceEquals(label.ReadLocalValue(TextBlock.TextAlignmentProperty), DependencyProperty.UnsetValue) && label.TextAlignment == TextAlignment.Left,
                "indexed descendant releases its own alignment when that declaration is removed");
        }
        finally { host.Children.Remove(fragment); await fragment.DisposeAsync(); }
    }
}
