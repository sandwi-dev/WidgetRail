namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    internal int FocusedViewportRevealCount { get; private set; }
    partial void ObserveFocusedViewportReveal() => FocusedViewportRevealCount++;
}
