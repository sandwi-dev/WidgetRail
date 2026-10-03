using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task GalleryScrollRevealAsync(string directory)
    {
        Exception? failure = null;
        var observations = new List<object>();
        try
        {
            var scale = XamlRoot.RasterizationScale;
            App.Window.AppWindow.Resize(new((int)(1000 * scale), (int)(850 * scale)));
            presenter.Height = 600; presenter.Width = 760;
            presenter.ApplyAppearance(WidgetRail.PlatformSettings.AppearanceSettings.Default, false);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            void ApplyFixture(int index)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, index + ".json")));
                var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText())) with { Sequence = ++sequence };
                var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
                var descriptor = new BridgeWidgetDescriptor { Id = "gallery.scroll", Name = "Gallery scroll", InstanceId = snapshot.WidgetInstanceId,
                    RuntimeGeneration = "fixture", PresentationGeneration = "fixture", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
                presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            }
            ApplyFixture(0);
            await Wait(() => Find<Button>("Widget.gallery.message")?.IsLoaded == true);
            presenter.UpdateLayout();
            var viewport = Find<ScrollViewer>("Widget.gallery.page-scroll")!;
            var button = Find<Button>("Widget.gallery.message")!;
            button.Focus(FocusState.Keyboard); button.StartBringIntoView();
            await Task.Delay(250);
            for (var i = 1; i <= 2; i++)
            {
                var before = viewport.VerticalOffset;
                ApplyFixture(i);
                await Task.Delay(500);
                var target = Find<TextBlock>("Widget.gallery.message." + i)!;
                var y = target.TransformToVisual(viewport).TransformPoint(new(0, 0)).Y;
                observations.Add(new { index = i, before, offset = viewport.VerticalOffset, y, viewport.ViewportHeight, viewport.ScrollableHeight,
                    focus = button.FocusState.ToString(), diagnostics = presenter.ScrollRevealDiagnostics() });
                Check(viewport.VerticalOffset > before + 1 && y >= -1 && y + target.ActualHeight <= viewport.ViewportHeight + 1 &&
                    (y < 8 || Math.Abs(viewport.VerticalOffset - viewport.ScrollableHeight) < 1),
                    "Appending message " + i + " aligns it with the viewport start");
                Check(button.FocusState != FocusState.Unfocused, "Reveal retains append-button focus");
                viewport.ChangeView(null, Math.Max(0, before - 50), null, true);
                await Task.Delay(100);
                var manuallyScrolled = viewport.VerticalOffset;
                ApplyFixture(i);
                await Task.Delay(100);
                Check(Math.Abs(viewport.VerticalOffset - manuallyScrolled) < 1, "A repeated request is not replayed");
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally { File.WriteAllText(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, observations, error = failure?.ToString() })); }
    }
}
