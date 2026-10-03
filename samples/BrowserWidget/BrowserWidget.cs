using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.Browser;

/// <summary>Intent receiver and address entry; all web content and browser interaction stay in the host.</summary>
public sealed class BrowserWidget : Widget
{
    private sealed record State(WebBrowserDocument Document);
    private readonly WidgetModel<State> model;
    private static readonly CompiledWidgetIntentContract contract = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web);
    public BrowserWidget() => model = CreateModel(new State(new("browser.primary", 1, WebBrowserDocument.StartPage, "Web browser")));

    public override WidgetView Render()
    {
        var state = model.Value;
        var page = UI.WebBrowser(state.Document, BrowserInteractionMode.InteractOnFocus, "browser.page");
        var root = UI.Grid("browser.root", [GridTrack.Star()], [GridTrack.Star()], page);
        return new(root,
            "browser.page", Surface: new()
            {
                Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.Preferred,
                HeightMode = WidgetSurfaceAxisMode.Preferred, PreferredWidth = 1000, PreferredHeight = 720,
                MinimumWidth = 420, MinimumHeight = 360,
            });
    }

    public override ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsActive || !request.IsWellFormed() || request.ContractId != contract.Id || request.Version != contract.Version ||
            request.SchemaDigest != contract.SchemaDigest || !contract.Accepts(request.Payload))
            return ValueTask.FromResult(WidgetIntentResult.Rejected);
        return ValueTask.FromResult(Open(request.Payload.GetProperty("url").GetString())
            ? WidgetIntentResult.Accepted : WidgetIntentResult.Rejected);
    }

    private bool Open(string? url)
    {
        if (!WebBrowserDocument.IsWebUrl(url)) return false;
        // Render never creates a navigation. Only a new user action or admitted
        // intent advances this revision, even when opening the same URL again.
        return model.Update(state =>
        {
            var sequence = state.Document.NavigationId;
            if (sequence == WebBrowserDocument.MaximumNavigationId) return state;
            return new(new("browser.primary", sequence + 1, url!, "Web browser"));
        }).Changed;
    }
}
