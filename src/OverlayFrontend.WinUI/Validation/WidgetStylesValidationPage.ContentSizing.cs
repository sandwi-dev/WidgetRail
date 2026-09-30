using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task ContentSizingAsync()
    {
        presenter.Width = 820; presenter.Height = 640;
        var leaf = new ViewNode { Id = "sizing.button", Kind = ViewNodeKind.Button, Text = "Search", ActionId = "search" };
        var body = new ViewNode { Id = "sizing.body", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[leaf] };
        var root = new ViewNode { Id = "sizing.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[body] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        {
            [body.Id] = Compute("#body { flex-grow: 1; }", "body", "stack"),
            [leaf.Id] = Compute("#button { height: 80px; }", "button", "button"),
        };
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => presenter.ActualHeight == 640 && Find<Button>("Widget.sizing.button")?.IsLoaded == true);
        var measured = presenter.MeasureSurfaceContent(new(820, 640), new() { HeightMode = WidgetSurfaceAxisMode.Content });
        Check(measured.Height < 160, $"Content height ignores surplus flex space: {measured.Height}");
        Check(presenter.Width == 820 && presenter.Height == 640, "Intrinsic probe restores the committed presenter dimensions");
        if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--content-sizing-fixture") is { } fixture)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fixture));
            var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText()));
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var resolvedStyles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
            presenter.Apply(CreateFrame(snapshot.Root, resolvedStyles, snapshot.ActiveInputScopeId));
            presenter.UpdateLayout();
            var extent = Shell.OverlaySurfaceSizing.Resolve(snapshot.Surface, new(1000, 800), new(0, 0), 1,
                limit => { var result = presenter.MeasureSurfaceContent(new(limit.Width, limit.Height), snapshot.Surface); return new(result.Width, result.Height); });
            Check(extent.Height < snapshot.Surface!.PreferredHeight,
                $"Actual empty YouTube search shrinks below its preferred height: {extent.Width}x{extent.Height}");
        }
    }
}
