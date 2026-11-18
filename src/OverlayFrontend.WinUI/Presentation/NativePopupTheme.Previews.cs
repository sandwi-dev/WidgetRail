using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class NativePopupTheme
{
    internal static Lease Preview(WidgetPreviewFrame frame, FrameworkElement origin,
        IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, AppearanceSettings appearance, Action metricsChanged,
        Func<BridgeNodeRenderStyles?>? partStyles = null)
    {
        var owner = Find(origin);
        if (owner is null) { owner = new(); owner.Update(styles, appearance); }
        return owner.Track(() =>
        {
            var parts = partStyles?.Invoke();
            var changed = frame.Apply(PreviewFrameStyle.ResolveRole(parts?.PreviewFrame is { Count: > 0 } frameStyle ? frameStyle : owner.styles?.GetValueOrDefault("preview")?.Base,
                owner.palette ?? ShellChromePalette.Resolve(owner.styles, owner.appearance), owner.appearance));
            changed |= frame.ApplyFooterStyle(parts?.PreviewFooter is { Count: > 0 } footerStyle ? footerStyle : owner.styles?.GetValueOrDefault("preview-footer")?.Base);
            if (changed) metricsChanged();
        });
    }
}
