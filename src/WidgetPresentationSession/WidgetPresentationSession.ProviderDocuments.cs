using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<BridgeProviderDocument> ResolveProviderDocumentAsync(WidgetPresentationAuthority authority,
        WebBrowserDocument document, CancellationToken token = default)
    {
        DemandCurrent();
        var response = BridgeJson.FromElement<BridgeProviderDocument>((await RequestAsync(BridgeMessageTypes.ResolveProviderDocument,
            new BridgeProviderDocumentRequest(authority.WidgetId, authority.WorkerRun!, document.ProviderDocument!),
            BridgeMessageTypes.ResolveProviderDocument, token).ConfigureAwait(false)).Payload);
        DemandCurrent();
        if (response.Reference != document.ProviderDocument || response.Html is not { Count: > 0 and <= 5 } ||
            response.Html.Any(html => string.IsNullOrWhiteSpace(html) || html.Contains('\0')) || response.Html.Sum(html => (long)html.Length) > 64 * 1024)
            throw new BridgeProtocolException("Provider document response is invalid.");
        return response;
        void DemandCurrent()
        {
            using (_gate.Enter())
            {
                if (document.ProviderDocument?.IsWellFormed() != true || !IsWebBrowserSessionCurrent(authority, document.Id) ||
                    _states.GetValueOrDefault(authority.WidgetId)?.LastGood is not { } frame || !Contains(frame.Snapshot.Root) &&
                    !frame.Snapshot.PinnedLayouts.Any(layout => layout.Root is { } root && Contains(root)))
                    throw new WidgetPresentationSessionException("attribution_stale", "The provider document retired.");
            }
        }
        bool Contains(ViewNode node) => node.WebBrowser is { } current && current.Id == document.Id &&
            current.ProviderDocument == document.ProviderDocument || node.Children.Any(Contains);
    }
}
