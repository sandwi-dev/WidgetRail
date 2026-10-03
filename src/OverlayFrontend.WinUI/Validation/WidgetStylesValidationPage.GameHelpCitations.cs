using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task GameHelpCitationsAsync(string directory)
    {
        Exception? failure = null;
        try
        {
            var scale = XamlRoot.RasterizationScale;
            App.Window.AppWindow.Resize(new((int)(1160 * scale), (int)(1000 * scale)));
            presenter.Height = 760; presenter.Width = 1040;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            ViewSnapshot ApplyFixture(string name)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, name + ".json")));
                var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText())) with { Sequence = ++sequence };
                var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
                var descriptor = new BridgeWidgetDescriptor { Id = "gamehelp.citations", Name = "Game Help citations", InstanceId = snapshot.WidgetInstanceId,
                    RuntimeGeneration = "fixture", PresentationGeneration = "fixture", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
                presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                presenter.UpdateLayout();
                return snapshot;
            }
            ApplyFixture("collapsed");
            await Wait(() => Find<Button>("Widget.help.message.2.sources-toggle")?.IsLoaded == true);
            Check(Find<Button>("Widget.help.message.2.source.0") is null, "Aggregate sources start collapsed");
            var solution = ApplyFixture("solution");
            var citations = Nodes(solution.Root).Where(node => node.Kind == ViewNodeKind.Button && node.Id.Contains(".citation.") && node.Intent is not null).ToArray();
            Check(citations.Length == 8, "Captured response renders all eight source associations");
            var paragraph = Find<Presentation.WidgetRichTextView>("Widget.help.message.2.solution-text.paragraph")!;
            var run = ((Microsoft.UI.Xaml.Documents.Paragraph)paragraph.Document.Blocks[0]).Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().First();
            var normalFont = run.FontSize;
            presenter.ApplyAppearance(WidgetRail.PlatformSettings.AppearanceSettings.Default with { TextScale = 1.25 }, false);
            await Task.Delay(80);
            Check(Math.Abs(run.FontSize / normalFont - 1.25) < .02, "Inline text inherits the presenter's text scale");
            presenter.ApplyAppearance(WidgetRail.PlatformSettings.AppearanceSettings.Default, false);
            await Task.Delay(80);
            foreach (var width in new[] { 1040, 560 })
            {
                presenter.Width = width; presenter.UpdateLayout(); await Task.Delay(100);
                var rich = Find<Presentation.WidgetRichTextView>("Widget.help.message.2.solution-text.paragraph")!;
                Check(rich.RowDefinitions.Count == 0 && rich.ColumnDefinitions.Count == 0,
                    "Native paragraph spans never create separate layout tracks");
                var scroll = Find<ScrollViewer>("Widget.help.transcript")!;
                Check(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"Citation text wraps without horizontal overflow at {width}");
                foreach (var node in citations)
                {
                    var button = Find<HyperlinkButton>("Widget." + node.Id)!;
                    var bounds = button.TransformToVisual(scroll).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                    Check(button.IsTabStop && button.IsEnabled && button.ActualWidth > 0 && bounds.Left >= -1 && bounds.Right <= scroll.ViewportWidth + 1,
                        $"Citation {node.Id} remains focusable and within the reading width at {width}");
                    Check(button.MinHeight == 0 && button.ActualHeight <= button.FontSize * 1.8,
                        $"Citation {node.Id} retains text-sized height after resize to {width}");
                }
            }
            var first = Find<HyperlinkButton>("Widget." + citations[0].Id)!;
            first.Focus(FocusState.Keyboard);
            await Wait(() => first.FocusState != FocusState.Unfocused);
            var before = actions;
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(actions == before + 1, "Controller A dispatches exactly one citation intent action");
            presenter.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right);
            var second = Find<HyperlinkButton>("Widget." + citations[1].Id)!;
            Check(second.FocusState != FocusState.Unfocused, "Controller Right reaches the next link in the same paragraph");
            Check(first.Focus(FocusState.Keyboard), "Inline link accepts native keyboard focus");
            ApplyFixture("sources");
            Check(ReferenceEquals(first, Find<HyperlinkButton>("Widget." + citations[0].Id)), "Inline control identity survives an adjacent expansion");
            File.WriteAllText(Path.Combine(directory, "focus-after-expansion.json"), presenter.FocusDiagnostics());
            await Wait(() => first.FocusState != FocusState.Unfocused);
            using (var focus = JsonDocument.Parse(presenter.FocusDiagnostics()))
                Check(focus.RootElement.GetProperty("remembered").GetProperty("Id").GetString() == citations[0].Id,
                    "Inline focus updates the host's remembered focus identity");
            Check(ReferenceEquals(first, Find<HyperlinkButton>("Widget." + citations[0].Id)) && first.FocusState != FocusState.Unfocused,
                "Unchanged inline links retain native focus when adjacent content expands");
            await Wait(() => Find<Button>("Widget.help.message.2.source.1")?.IsLoaded == true);
            Check(Find<Button>("Widget.help.message.2.source.0") is { IsEnabled: true, IsTabStop: true }, "Expanded numbered source links are actionable");
            var toggle = Find<Button>("Widget.help.message.2.sources-toggle")!;
            toggle.Focus(FocusState.Keyboard);
            ApplyFixture("closedSources");
            await Wait(() => Find<Button>("Widget.help.message.2.source.0") is null);
            Check(ReferenceEquals(toggle, Find<Button>("Widget.help.message.2.sources-toggle")) && toggle.FocusState != FocusState.Unfocused,
                "Collapsing sources preserves focus on its toggle");
            Check(Find<HyperlinkButton>("Widget." + citations[0].Id) is not null, "Collapsing sources preserves inline citations");
            var retired = first.Command;
            before = actions;
            ApplyFixture("collapsed");
            retired.Execute(null);
            await Task.Delay(50);
            Check(actions == before, "A retired inline link command cannot dispatch an action");
            ApplyFixture("closedSources");
            presenter.Width = 1040; presenter.UpdateLayout();
        }
        catch (Exception error) { failure = error; throw; }
        finally { File.WriteAllText(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() })); }
        static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    }
}
