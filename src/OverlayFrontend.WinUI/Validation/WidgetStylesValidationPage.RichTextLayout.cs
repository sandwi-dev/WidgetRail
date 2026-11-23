using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task RichTextLayoutAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        try
        {
            presenter.Height = double.NaN;
            var intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                JsonSerializer.SerializeToElement(new { url = "https://example.invalid/" }));
            var root = new ViewNode
            {
                Id = "links.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[new()
                {
                    Id = "links.paragraph", Kind = ViewNodeKind.RichText, Children =
                    (ViewNode[])[new() { Id = "links.prefix", Kind = ViewNodeKind.Text, Text = "Read the details at " },
                     new() { Id = "links.short", Kind = ViewNodeKind.Button, Text = "example.com", ActionId = "short", Intent = intent },
                     new() { Id = "links.separator", Kind = ViewNodeKind.Text, Text = ", or see the full reference: " },
                     new() { Id = "links.long", Kind = ViewNodeKind.Button, Text = "https://example.invalid/" + new string('w', 120), ActionId = "long", Intent = intent }]
                }]
            };
            const string css = "stack { padding:8px; align:start; } text { padding:4px; max-width:640px; font-size:15px; }";
            presenter.Apply(CreateFrame(root, Enumerate(root).ToDictionary(node => node.Id,
                node => Compute(css, node.Id, node.Kind == ViewNodeKind.RichText ? "text" : node.Kind.ToString().ToLowerInvariant()))));
            await Wait(() => Find<HyperlinkButton>("Widget.links.long")?.IsLoaded == true);
            var rich = Find<WidgetRichTextView>("Widget.links.paragraph")!;
            var shortLink = Find<HyperlinkButton>("Widget.links.short")!;
            var longLink = Find<HyperlinkButton>("Widget.links.long")!;
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            foreach (var textScale in new[] { 1d, 1.5 })
            foreach (var width in new[] { 280d, 404, 800, 280 })
            {
                presenter.RequestedTheme = theme;
                presenter.ApplyAppearance(AppearanceSettings.Default with { TextScale = textScale }, false);
                presenter.Width = width;
                presenter.UpdateLayout();
                await Task.Delay(80);
                var height = rich.ActualHeight;
                var linkWidth = longLink.ActualWidth;
                for (var pass = 0; pass < 4; ++pass)
                {
                    rich.InvalidateMeasure();
                    presenter.UpdateLayout();
                    await Task.Delay(20);
                    Check(Math.Abs(rich.ActualHeight - height) <= 1 && Math.Abs(longLink.ActualWidth - linkWidth) <= 1,
                        $"Inline paragraph geometry settles at {width} DIP / {textScale} text / {theme}, pass {pass}");
                }
                Check(longLink.ActualWidth > 0 && longLink.ActualWidth <= width - 16 + 1 && shortLink.ActualHeight < 40 * textScale,
                    $"Long URL wraps within its parent and short citation stays text-sized at {width} / {textScale} / {theme}: link width={longLink.ActualWidth}, short height={shortLink.ActualHeight}");
            }
            Check(shortLink.Focus(FocusState.Keyboard), "Inline link remains focusable after repeated resizes");
            var before = actions;
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(actions == before + 1, "Resized inline link dispatches exactly one controller action");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"),
                JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        }
        if (failure is not null) throw failure;
        static IEnumerable<ViewNode> Enumerate(ViewNode node)
        {
            yield return node;
            foreach (var child in node.Children)
                foreach (var nested in Enumerate(child)) yield return nested;
        }
    }
}
