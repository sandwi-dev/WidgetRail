using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PlayniteIndexedRowsAsync(JsonElement fixture)
    {
        // The parent and each range were published/acquired through the real SDK.
        // Inspect their native row containers; do not invent an eager page tree or
        // bypass the presentation session required by an actual indexed viewport.
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(fixture.GetProperty("snapshot").GetRawText()));
        var ranges = fixture.GetProperty("ranges").EnumerateArray().Select(value => (
            Range: value.GetProperty("range").Deserialize<IndexedCollectionRange>(options)!,
            Styles: value.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!)).ToArray();
        Check(ViewSnapshotValidator.Validate(snapshot).Count == 0 && ranges.Length > 0, "production indexed parent and acquired ranges are present and valid");
        foreach (var (range, _) in ranges)
            IndexedCollectionContract.ValidateRange(snapshot, new(range.CollectionId, range.Source,
                range.StartIndex, range.Items.Count, range.DemandId, range.PinnedLayoutId), range);
        var parentNodes = Walk(snapshot.Root).ToArray();
        var declaration = parentNodes.Single(node => node.Kind == ViewNodeKind.IndexedCollection);
        Check(declaration.Children.Count == 0 && ranges.Sum(value => value.Range.Items.Count) == declaration.IndexedCollection!.Count,
            "indexed parent keeps total count while poster trees stay in bounded acquired ranges");
        var rows = ranges.SelectMany(value => value.Range.Items.Select(row => (Row: row, value.Range, value.Styles))).ToArray();
        var descriptor = new BridgeWidgetDescriptor { Id = "playnite.indexed-layout", Name = "Playnite acquired row geometry",
            InstanceId = snapshot.WidgetInstanceId, RuntimeGeneration = "fixture", PresentationGeneration = "fixture", Icon = WidgetGlyph.Play, PackageContentDigest = string.Empty };
        foreach (var index in new[] { 0, rows.Length / 2, rows.Length - 1 }.Distinct())
        {
            var entry = rows[index];
            var frame = new WidgetPresentationFrame(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
                1, descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, entry.Styles);
            var fragment = new WidgetViewPresenter(presentationOnly: true);
            fragment.UseIndexedContainerStyles();
            fragment.ResolveArtworkAsync = (_, _) => Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png,
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII=")));
            fragment.ApplyFragment(frame, entry.Row.Root, entry.Range.ScopeId);
            var container = new GridViewItem { Width = 150, Height = 225, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch, Content = fragment };
            host.Children.Add(container);
            using var style = new NativeComputedStyleAdapter(container);
            style.Update(entry.Styles.GetValueOrDefault(entry.Row.Root.Id));
            try
            {
                await Wait(() => fragment.ActualWidth > 0 && fragment.ActualHeight > 0);
                // Home authors 28vh bounded to112..150; Browse uses the viewport's
                // assigned cell width. Resolve Home against the actual native
                // viewport rather than asserting Browse's width for both routes.
                style.RefreshBoxLayout();
                container.UpdateLayout();
                var expectedWidth = declaration.ScrollAxis == ScrollAxis.Horizontal
                    ? Math.Clamp(WidgetViewPresenter.ViewportFor(container).Height * .28, 112, 150) : 150;
                Check(Math.Abs(container.ActualWidth - expectedWidth) < 1 &&
                      fragment.ActualWidth <= container.ActualWidth + 1 && fragment.ActualHeight <= 226,
                    $"acquired {declaration.ScrollAxis} poster {index} fits its native container: expectedWidth={expectedWidth:F1}, actual={container.ActualWidth:F1}x{container.ActualHeight:F1}, fragment={fragment.ActualWidth:F1}x{fragment.ActualHeight:F1}");
                if (Walk(entry.Row.Root).Any(node => node.Kind == ViewNodeKind.Image))
                    await Wait(() => Descendants(fragment).OfType<WidgetArtworkView>().Any(image => image.Source is not null));
                Check(!entry.Styles.Values.SelectMany(value => value.Base.Keys).Any(key => key is "flex-basis" or "flex-shrink" or "flex-wrap"),
                    $"acquired poster {index} uses native layout properties without ignored flex declarations");
                if (entry.Row.Root.FocusPresentation is { } summary)
                {
                    var summaryPresenter = new WidgetViewPresenter(presentationOnly: true) { Width = 520 };
                    summaryPresenter.ApplyFragment(frame, summary, entry.Range.ScopeId);
                    host.Children.Add(summaryPresenter);
                    try
                    {
                        await Wait(() => summaryPresenter.ActualWidth > 0 && summaryPresenter.ActualHeight > 0);
                        Check(summaryPresenter.ActualWidth <= 520 && Descendants(summaryPresenter).OfType<TextBlock>().Any(text => text.Text.Length > 0),
                            $"acquired Home poster {index} retains its readable focused summary at narrow width");
                    }
                    finally { host.Children.Remove(summaryPresenter); await summaryPresenter.DisposeAsync(); }
                }
            }
            finally { host.Children.Remove(container); await fragment.DisposeAsync(); }
        }

        static IEnumerable<ViewNode> Walk(ViewNode node)
        { yield return node; foreach (var child in node.Children) foreach (var nested in Walk(child)) yield return nested; }
        static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        { yield return node; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(node, index))) yield return child; }
    }
}
