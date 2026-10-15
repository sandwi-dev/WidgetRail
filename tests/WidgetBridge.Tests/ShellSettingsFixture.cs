using System.Net;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

// The native shell test crosses both pipes without touching releases or installers.
internal sealed class ShellUpdateApi : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri?.Host != "api.github.com" || !request.RequestUri.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected synthetic update request");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] {
            new { tag_name = "v0.1.0-preview.99", draft = false, prerelease = true, body = "Synthetic update notes", assets = new[] {
                new { name = BridgeAppUpdates.InstallerName("0.1.0-preview.99", "production"), size = 1, digest = "sha256:" + new string('a',64) } } }
        })) });
    }
}

internal sealed class ShellSettingsFixture : Widget
{
    private readonly WidgetModel<bool> updates;
    internal ShellSettingsFixture() => updates = CreateModel(false);
    public override WidgetView Render() => new(UI.Button(updates.Value ? "Synthetic App updates destination" : "Settings fixture", "noop", updates.Value ? "updates" : "home"), updates.Value ? "updates" : "home");
    public override ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ContractId != WidgetIntentContracts.AppUpdates.Id || !request.IsWellFormed()) return ValueTask.FromResult(WidgetIntentResult.Rejected);
        updates.Update(_ => true);
        return ValueTask.FromResult(WidgetIntentResult.Accepted);
    }
}
