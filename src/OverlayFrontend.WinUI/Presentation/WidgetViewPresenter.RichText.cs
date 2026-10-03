using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private readonly Dictionary<HyperlinkButton, long> inlineFocusSubscriptions = [];
    private void ConfigureInlineLinkInput(HyperlinkButton link)
    {
        // Text-hosted controls do not route every focus/key event through the
        // presenter. Use the same host handlers at the native inline boundary.
        inlineFocusSubscriptions.Add(link, link.RegisterPropertyChangedCallback(Control.FocusStateProperty, (_, _) =>
        {
            if (link.FocusState == FocusState.Unfocused) return;
            // Text-hosted controls do not reliably raise routed GotFocus. The
            // native FocusState property identifies the actual destination.
            if (link.Tag is WidgetElementIdentity identity && bindings.TryGetValue(identity.Id, out var binding) && ReferenceEquals(binding.Element, link))
                RememberFocus(binding);
            DispatcherQueue.TryEnqueue(NotifyControllerGuideChanged);
        }));
        link.GettingFocus += OnGettingFocus;
        link.PreviewKeyDown += DirectionalKeyDown;
        link.PointerPressed += (_, _) => { CancelGroupEntry(); CancelScrollReveal(); };
    }
    private void RetireInlineLink(Binding binding)
    {
        if (binding.Element is HyperlinkButton link && inlineFocusSubscriptions.Remove(link, out var subscription))
            link.UnregisterPropertyChangedCallback(Control.FocusStateProperty, subscription);
    }
    private static bool IsInlineLink(Declaration declaration, Dictionary<string, Declaration> plan) =>
        declaration.Node.Kind == WidgetRail.WidgetProtocol.ViewNodeKind.Button && declaration.ParentId is { } parent &&
        plan[parent].Node.Kind == WidgetRail.WidgetProtocol.ViewNodeKind.RichText;

    private void RefreshRichText()
    {
        foreach (var (id, binding) in bindings)
            if (binding.Element is WidgetRichTextView rich)
            {
                rich.UpdateSpans(declarations[id].Node.Children.Select(node => bindings[node.Id].LayoutElement).ToArray());
                foreach (var child in declarations[id].Node.Children)
                    if (bindings[child.Id].Element is TextBlock) ApplyComputedStyles(bindings[child.Id], child);
            }
    }
    private static void UpdateInlineLink(HyperlinkButton link, string text)
    {
        if (link.Content is not TextBlock label)
            link.Content = label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        WidgetTextStyleAdapter.SetSource(label, text);
        link.Padding = new Thickness(0);
        link.BorderThickness = new Thickness(0);
        link.MinHeight = link.MinWidth = 0;
    }
}
