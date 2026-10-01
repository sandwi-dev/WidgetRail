using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.Browser;

/// <summary>Intent receiver and address entry; all web content and browser interaction stay in the host.</summary>
public sealed class BrowserWidget : Widget
{
    private sealed record State(WebBrowserDocument? Document = null, string? Error = null);
    private readonly WidgetModel<State> model;
    private static readonly CompiledWidgetIntentContract contract = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web);
    public BrowserWidget() => model = CreateModel(new State());

    public override WidgetView Render()
    {
        var state = model.Value;
        WidgetElement page = state.Document is { } document
            ? UI.WebBrowser(document, "browser.page")
            : UI.Text("Open a guide link or enter a web address to start browsing.", "browser.empty");
        return new(UI.Grid("browser.root", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            UI.Stack("browser.address-row", UI.TextEntry("", "Open a web address (https://…)", "browser.open", "browser.address", ProtocolConstants.MaximumTextEntryLength),
                UI.Text(state.Error ?? "", "browser.error")).InGrid(0), page.InGrid(1)),
            state.Document is null ? "browser.address" : "browser.page", Surface: new()
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

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsActive && action.ActionId == "browser.open" && action.CommittedText is { } address && !Open(address.Trim()))
            model.Update(state => state with { Error = "Enter a valid HTTP or HTTPS web address." });
        return ValueTask.CompletedTask;
    }

    private bool Open(string? url)
    {
        if (!WebBrowserDocument.IsWebUrl(url)) return false;
        // Render never creates a navigation. Only a new user action or admitted
        // intent advances this revision, even when opening the same URL again.
        return model.Update(state =>
        {
            var sequence = state.Document?.NavigationId ?? 0;
            if (sequence == WebBrowserDocument.MaximumNavigationId) return state;
            return new(new("browser.primary", sequence + 1, url!, "Web browser"));
        }).Changed;
    }
}
