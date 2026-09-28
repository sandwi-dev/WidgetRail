using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task LoadingIndicatorGeometryAsync()
    {
        foreach (var (size, extent) in new[] { (LoadingIndicatorSize.Compact, 16d), (LoadingIndicatorSize.Standard, 32d), (LoadingIndicatorSize.Large, 48d) })
        {
            presenter.Apply(CreateFrame(new() { Id = "loading.root", Kind = ViewNodeKind.Row, Children = [
                new() { Id = "loading.title", Kind = ViewNodeKind.Text, Text = "Loading music" },
                new() { Id = "loading.ring", Kind = ViewNodeKind.LoadingIndicator, IndicatorSize = size, AccessibilityLabel = "Loading music" }] },
                new Dictionary<string, BridgeNodeRenderStyles>()));
            await Wait(() => Find<ProgressRing>("Widget.loading.ring") is { } current && Near(current.ActualWidth, extent));
            var ring = Find<ProgressRing>("Widget.loading.ring")!;
            Check(Near(ring.Width, extent) && Near(ring.Height, extent) && Near(ring.ActualWidth, extent), "loading indicator keeps finite authored intrinsic size " + size);
        }
    }
}
