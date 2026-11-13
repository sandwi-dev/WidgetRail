using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task<int> GuideRetirementAsync()
    {
        var owned = new WidgetViewPresenter { Width = 400, Height = 120 };
        host.Children.Add(owned);
        try
        {
            using var payload = System.Text.Json.JsonDocument.Parse("""{"url":"https://example.invalid/"}""");
            owned.Apply(CreateFrame(new ViewNode
            {
                Id = "retirement.root", Kind = ViewNodeKind.Stack,
                Children = (ViewNode[])[new()
                {
                    Id = "retirement.text", Kind = ViewNodeKind.RichText,
                    Children = (ViewNode[])[new() { Id = "retirement.link", Kind = ViewNodeKind.Button,
                        Text = "Inline action", ActionId = "inline.intent",
                        Intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web, payload.RootElement) }]
                }]
            }, new Dictionary<string, WidgetRail.WidgetBridge.BridgeNodeRenderStyles>()));
            await Wait(() => Find<HyperlinkButton>("Widget.retirement.link", owned)?.IsLoaded == true);
            if (!outside.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Could not establish outside focus.");
            await Task.Delay(30);
            var notices = 0;
            owned.ControllerGuideChanged += () => ++notices;
            // Real inline FocusState handling queues a guide notification. Retire
            // in the same dispatcher turn, before that queued callback can run.
            if (!Find<HyperlinkButton>("Widget.retirement.link", owned)!.Focus(FocusState.Keyboard))
                throw new InvalidOperationException("Inline action did not accept native focus.");
            await owned.DisposeAsync();
            var atRetirement = notices;
            await Task.Delay(100);
            if (notices != atRetirement) throw new InvalidOperationException("Queued inline focus published guide state after retirement.");
            return 1;
        }
        finally { host.Children.Remove(owned); await owned.DisposeAsync(); }
    }
}
