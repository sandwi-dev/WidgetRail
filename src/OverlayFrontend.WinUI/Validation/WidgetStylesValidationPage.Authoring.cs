using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AuthoringAdmissionAsync()
    {
        var page = new WidgetViewPresenter();
        host.Children.Add(page);
        var root = new ViewNode { Id = "author-root", Kind = ViewNodeKind.Stack, Children = new ViewNode[] {
            new() { Id = "author-heading", Kind = ViewNodeKind.Text, Text = "Authoring checks" },
            new() { Id = "author-button", Kind = ViewNodeKind.Button, Text = "Retained action", ActionId = "act" } } };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>();
        var legacy = new WidgetView(UI.CollectionList("legacy-list", 72,
            items: [UI.Button("Private display text", "private-action", "legacy-item").CollectionItem(new WidgetCollectionItemKey("key"))]), "legacy-item")
            .CreateSnapshot("authoring", 1);
        try
        {
            page.Apply(CreateFrame(root, styles));
            await Wait(() => Find<Button>("Widget.author-button", page) is { IsLoaded: true });
            var button = Find<Button>("Widget.author-button", page)!;
            button.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            var invalid = CreateFrame(root with { Children = new ViewNode[] { root.Children[0], legacy.Root } }, styles);
            var expected = WidgetTestHost.ValidateWinUiPresentation(invalid.Snapshot);
            Check(expected.Any(error => error.Path == "$.root.children[1].collectionLayout"),
                "author preflight identifies the same nested unsupported declaration before native rendering");
            string? diagnostic = null;
            try { page.Apply(invalid); }
            catch (NotSupportedException error) { diagnostic = error.Message; }
            Check(diagnostic is not null && expected.All(error => diagnostic.Contains(error.Path, StringComparison.Ordinal) &&
                diagnostic.Contains(error.Code, StringComparison.Ordinal)),
                "native admission uses shared field diagnostics and structural paths");
            Check(diagnostic!.Contains("indexed/discovered", StringComparison.Ordinal) &&
                !diagnostic.Contains("Private display text", StringComparison.Ordinal) && !diagnostic.Contains("private-action", StringComparison.Ordinal),
                "native errors include remediation without leaking widget text or action payloads");
            Check(ReferenceEquals(button, Find<Button>("Widget.author-button", page)) && button.IsLoaded && button.FocusState != FocusState.Unfocused,
                "unsupported incoming declarations fail before replacing valid controls or focus");
            page.Apply(CreateFrame(root with { Children = new ViewNode[] { root.Children[0], root.Children[1] with { Text = "Recovered" } } }, styles));
            Check(ReferenceEquals(button, Find<Button>("Widget.author-button", page)),
                "a subsequent valid declaration recovers using the retained native control");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }
    }
}
