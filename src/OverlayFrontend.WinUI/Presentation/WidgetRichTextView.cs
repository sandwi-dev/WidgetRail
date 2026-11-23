using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>WinUI owns paragraph layout. Inline controls keep ordinary presenter action and focus identities.</summary>
internal sealed partial class WidgetRichTextView : Grid
{
    internal RichTextBlock Document { get; } = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
    private readonly Paragraph paragraph = new();
    private readonly Dictionary<FrameworkElement, Inline> spans = [];
    private readonly Dictionary<TextBlock, List<(DependencyProperty Property, long Token)>> watches = [];
    internal WidgetRichTextView()
    {
        Document.Blocks.Add(paragraph);
        Children.Add(Document);
    }
    internal void DetachExcept(Func<FrameworkElement, bool> retain)
    {
        foreach (var (element, inline) in spans.ToArray())
        {
            if (retain(element)) continue;
            NativeTextScaleScope.SetSpanParent(element, null);
            if (element is TextBlock text && watches.Remove(text, out var subscriptions))
                foreach (var (property, token) in subscriptions) text.UnregisterPropertyChangedCallback(property, token);
            if (inline is InlineUIContainer container) container.Child = null;
            paragraph.Inlines.Remove(inline);
            spans.Remove(element);
        }
    }
    internal void UpdateSpans(IReadOnlyList<FrameworkElement> elements)
    {
        DetachExcept(elements.Contains);
        for (var index = 0; index < elements.Count; ++index)
        {
            var element = elements[index];
            if (!spans.TryGetValue(element, out var inline))
            {
                inline = element is TextBlock ? new Run() : new InlineUIContainer { Child = element };
                spans.Add(element, inline);
                if (element is TextBlock source && inline is Run textRun)
                {
                    NativeTextScaleScope.SetSpanParent(source, this);
                    var properties = new[] { TextBlock.TextProperty, TextBlock.ForegroundProperty, TextBlock.FontSizeProperty,
                        TextBlock.FontFamilyProperty, TextBlock.FontWeightProperty, TextBlock.CharacterSpacingProperty,
                        TextBlock.FontStyleProperty, TextBlock.TextDecorationsProperty };
                    watches.Add(source, properties.Select(property => (property,
                        source.RegisterPropertyChangedCallback(property, (_, _) => UpdateRun(source, textRun)))).ToList());
                }
            }
            if (element is TextBlock text && inline is Run run)
                UpdateRun(text, run);
            if (index < paragraph.Inlines.Count && ReferenceEquals(paragraph.Inlines[index], inline)) continue;
            paragraph.Inlines.Remove(inline);
            paragraph.Inlines.Insert(index, inline);
        }
    }
    private static void UpdateRun(TextBlock text, Run run)
    {
        if (run.Text != text.Text) run.Text = text.Text;
        Copy(text, TextBlock.ForegroundProperty, run, TextElement.ForegroundProperty);
        Copy(text, TextBlock.FontSizeProperty, run, TextElement.FontSizeProperty);
        Copy(text, TextBlock.FontFamilyProperty, run, TextElement.FontFamilyProperty);
        Copy(text, TextBlock.FontWeightProperty, run, TextElement.FontWeightProperty);
        Copy(text, TextBlock.CharacterSpacingProperty, run, TextElement.CharacterSpacingProperty);
        Copy(text, TextBlock.FontStyleProperty, run, TextElement.FontStyleProperty);
        Copy(text, TextBlock.TextDecorationsProperty, run, TextElement.TextDecorationsProperty);
    }
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        // An inline control participates in the paragraph's desired width. Using
        // that paragraph's arranged ActualWidth as the control's next constraint
        // feeds the result back into its input and can oscillate as a virtualized
        // row is revealed. Constrain it from the parent's measure budget instead.
        var width = Math.Max(0, availableSize.Width - Padding.Left - Padding.Right -
            BorderThickness.Left - BorderThickness.Right - Document.Margin.Left - Document.Margin.Right);
        width = Math.Min(width, Document.MaxWidth);
        if (double.IsFinite(Document.Width)) width = Math.Min(width, Document.Width);
        var maximum = Math.Max(0, width - Document.Padding.Left - Document.Padding.Right);
        foreach (var link in spans.Keys.OfType<HyperlinkButton>())
            if (link.MaxWidth != maximum) link.MaxWidth = maximum;
        return base.MeasureOverride(availableSize);
    }
    private static void Copy(DependencyObject from, DependencyProperty source, DependencyObject to, DependencyProperty target)
    {
        var value = from.ReadLocalValue(source);
        if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            if (!ReferenceEquals(to.ReadLocalValue(target), DependencyProperty.UnsetValue)) to.ClearValue(target);
        }
        else if (!Equals(to.GetValue(target), value)) to.SetValue(target, value);
    }
}
