using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class NativePopupTheme
{
    internal static IDisposable Preview(WidgetPreviewFrame frame, FrameworkElement origin,
        IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, AppearanceSettings appearance, Action metricsChanged)
    {
        var owner = Find(origin);
        if (owner is null) { owner = new(); owner.Update(styles, appearance); }
        return owner.Track(() =>
        {
            if (frame.Apply(PreviewFrameStyle.Resolve(owner.styles,
                owner.palette ?? ShellChromePalette.Resolve(owner.styles, owner.appearance), owner.appearance))) metricsChanged();
        });
    }
}
