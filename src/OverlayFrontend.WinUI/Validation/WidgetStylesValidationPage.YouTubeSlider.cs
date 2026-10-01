using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task YouTubeSliderSpacingAsync(string path)
    {
        Exception? failure = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText()));
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
            presenter.Height = 640;
            var descriptor = new BridgeWidgetDescriptor { Id = "youtube-layout", Name = "YouTube layout", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "layout", PresentationGeneration = "layout", Icon = WidgetGlyph.Music, PackageContentDigest = "" };
            presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            await Wait(() => Find<Slider>("Widget.youtube.volume")?.IsLoaded == true);
            foreach (var width in new[] { 820, 1000, 680 })
            {
                presenter.Width = width; presenter.UpdateLayout(); await Task.Delay(80);
                var group = Find<Grid>("Widget.youtube.timeline-group")!;
                var volume = Find<Slider>("Widget.youtube.volume")!;
                var timeline = Find<Slider>("Widget.youtube.timeline")!;
                var controls = Find<Grid>("Widget.youtube.controls")!;
                var controlsBounds = controls.TransformToVisual(presenter).TransformBounds(new(0, 0, controls.ActualWidth, controls.ActualHeight));
                var groupBounds = group.TransformToVisual(presenter).TransformBounds(new(0, 0, group.ActualWidth, group.ActualHeight));
                var volumeBounds = volume.TransformToVisual(presenter).TransformBounds(new(0, 0, volume.ActualWidth, volume.ActualHeight));
                presenter.WriteLayoutDiagnostics(path + "." + width + ".layout.json");
                Check(Math.Abs(volumeBounds.Right - controlsBounds.Right) < 1 && Math.Abs(groupBounds.Right - controlsBounds.Right) < 1,
                    $"YouTube sliders fill the control row without trailing space at width {width}");
                Check(Math.Abs(timeline.ActualWidth / volume.ActualWidth - 3) < .08 && volume.ActualWidth >= 80,
                    $"YouTube timeline and volume maintain 3:1 widths at {width}: {timeline.ActualWidth}/{volume.ActualWidth}");
                foreach (var slider in new[] { timeline, volume })
                {
                    var template = HorizontalTemplate(slider)!;
                    var slot = LayoutInformation.GetLayoutSlot(template);
                    Check(template.ActualHeight <= slot.Height + .01 && template.ActualWidth <= slot.Width + .01,
                        $"YouTube {slider.Tag} native slider template fits its padded layout slot at width {width}");
                    var sliderSlot = LayoutInformation.GetLayoutSlot(slider);
                    Check(slider.ActualHeight <= sliderSlot.Height + .01,
                        $"YouTube slider row reserves the full native height at width {width}");
                }
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally { File.WriteAllText(path + ".result.json", JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() })); }
    }

    private static Grid? HorizontalTemplate(DependencyObject element)
    {
        if (element is Grid { Name: "HorizontalTemplate" } grid) return grid;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (HorizontalTemplate(VisualTreeHelper.GetChild(element, i)) is { } child) return child;
        return null;
    }
}
