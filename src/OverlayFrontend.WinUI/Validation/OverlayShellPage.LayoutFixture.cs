using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void ApplyLayoutFixture(WidgetPresentationFrame frame, AppearanceSettings appearance,
        IReadOnlyList<BridgeWidgetDescriptor> catalog)
    {
        if (startup is not null || owner is not null) throw new InvalidOperationException("Layout fixture cannot alter a running bridge shell.");
        Appearance = appearance;
        authoredSurfaceHints = frame.Snapshot.Surface;
        if (!catalogItems.SequenceEqual(catalog))
        {
            catalogItems.Clear();
            foreach (var descriptor in catalog) catalogItems.Add(descriptor);
        }
        if (surface is null)
        {
            surface = new WidgetViewPresenter();
            surface.SetAutomaticFocusEnabled(false);
            WidgetSurfaces.Children.Add(surface);
        }
        surface.Apply(frame);
        ShowPresentationStatus(frame.Descriptor.Name);
    }
    internal async Task DisposeLayoutFixtureAsync()
    {
        DisposeShellChrome();
        if (surface is not null) { await surface.DisposeAsync(); surface = null; }
        await DisposeAsync();
    }
}
