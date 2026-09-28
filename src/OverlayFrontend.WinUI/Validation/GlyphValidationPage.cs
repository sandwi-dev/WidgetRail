using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed class GlyphValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Glyph checks pending" };
    private Task? running;
    internal GlyphValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Glyph.Result");
        Content = new StackPanel { Spacing = 12, Children = { status, presenter } };
        Loaded += (_, _) => running ??= RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            var prompts = Enum.GetValues<ControllerPrompt>();
            var nodes = prompts.Select(prompt => new ViewNode { Id = "prompt." + prompt, Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = prompt })
                .Concat(Enum.GetValues<WidgetGlyph>().Select(glyph => new ViewNode { Id = "symbol." + glyph, Kind = ViewNodeKind.Icon, Glyph = glyph, AccessibilityLabel = glyph.ToString() })).ToArray();
            var snapshot = new ViewSnapshot { WidgetInstanceId = "glyph.instance", Sequence = 1, ActiveInputScopeId = "root",
                Root = new() { Id = "root", Kind = ViewNodeKind.Stack, Children = nodes.Chunk(8).Select((row, index) => new ViewNode
                    { Id = "row." + index, Kind = ViewNodeKind.Row, Children = row }).ToArray() } };
            var descriptor = new BridgeWidgetDescriptor { Id = "glyph", Name = "Glyphs", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "glyph", PresentationGeneration = "glyph", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
            presenter.SetControllerFamily(ControllerFamily.Xbox);
            var errors = ViewSnapshotValidator.Validate(snapshot);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Path + ": " + error.Message)));
            presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                snapshot.WidgetInstanceId, 1, snapshot.ActiveInputScopeId), descriptor,
                SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>()));
            await Task.Delay(50);
            var icons = Descendants(presenter).OfType<FontIcon>().ToArray();
            Check(icons.Length == nodes.Length && icons.All(icon => icon.ActualWidth > 0 && icon.ActualHeight > 0), "all semantic icons have native bounds");
            var a = icons.Single(icon => AutomationProperties.GetAutomationId(icon) == "Widget.prompt.A");
            Check(a.Glyph == "\uE005" && AutomationProperties.GetName(a) == "A button", "Xbox face and accessible label");
            presenter.SetControllerFamily(ControllerFamily.PlayStation);
            Check(a.Glyph == "\uE04C" && AutomationProperties.GetName(a) == "Cross button", "family change reuses native glyph and updates label");
            presenter.SetControllerFamily(ControllerFamily.Unknown);
            Check(a.Glyph == "\uE04C", "unknown frame retains last physical family");
            Check(icons.Single(icon => AutomationProperties.GetAutomationId(icon) == "Widget.prompt.Guide").FontFamily.Source.Contains("PromptFont", StringComparison.Ordinal), "PS Guide uses licensed fallback font");
            Check(icons.All(icon => !icon.IsHitTestVisible), "glyphs are presentation only");
            var semantic = Descendants(presenter).OfType<WidgetPackageIconView>().Select(view => view.Content).OfType<FontIcon>().ToArray();
            Check(semantic.Length == Enum.GetValues<WidgetGlyph>().Length && semantic.All(icon => icon.FontFamily.Source == "Segoe Fluent Icons"), "controller changes do not alter semantic widget icons");
            status.Text = $"Passed 7 glyph checks; {icons.Length} symbols; PlayStation prompts";
        }
        catch (Exception error) { status.Text = "Failed: " + error; }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    public async ValueTask DisposeAsync() { if (running is not null) await running; await presenter.DisposeAsync(); }
}
