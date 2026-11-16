using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal Button CollapseProbeButton => IsImage ? imageExpand : expand;
}
