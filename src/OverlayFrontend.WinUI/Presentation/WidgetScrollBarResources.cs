using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>WRSS scrollbar paint on native ScrollViewer/ListView templates.</summary>
internal static class WidgetScrollBarResources
{
    private static readonly string[] ThumbKeys = [
        "ScrollBarThumbFill", "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed",
        "ScrollBarThumbBackground", "ScrollBarPanningThumbBackground",
        "ScrollBarThumbBackgroundThemeBrush", "ScrollBarThumbPointerOverBackgroundThemeBrush",
        "ScrollBarThumbPressedBackgroundThemeBrush", "ScrollBarPanningThumbForegroundThemeBrush",
    ];
    private static readonly string[] TrackKeys = [
        "ScrollBarTrackFill", "ScrollBarTrackFillPointerOver", "ScrollBarTrackStroke",
        "ScrollBarTrackStrokePointerOver", "ScrollBarTrackBackgroundThemeBrush", "ScrollBarTrackBorderThemeBrush",
    ];
    internal static void Apply(ResourceDictionary resources, Brush? thumb, Brush? track, double? width)
    {
        foreach (var key in ThumbKeys) Put(key, thumb);
        foreach (var key in TrackKeys) Put(key, track);
        Put("ScrollBarThumbBackgroundColor", (thumb as SolidColorBrush)?.Color);
        Put("ScrollBarPanningThumbBackgroundColor", (thumb as SolidColorBrush)?.Color);
        // Preserve native pointer hit area and automation. Only the painted
        // thumb's cross-axis size follows the authored thin scrollbar width.
        Put("ScrollBarVerticalThumbMinWidth", width);
        Put("ScrollBarHorizontalThumbMinHeight", width);
        void Put(string key, object? value) => WidgetNativeResource.Set(resources, key, value);
    }
}
