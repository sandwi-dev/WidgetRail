using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SdkGalleryWidget;

public sealed partial class SdkGalleryWidget
{
    private bool _reverseGridProportions;

    private WidgetElement NativeGridDemo() => UI.Card("gallery.grid.card", CardVariant.Subtle,
        UI.Text("Grid layout", "gallery.grid.title"),
        UI.Text("A fixed first column, proportional remaining columns, and spanning rows. Swap proportions while keeping focus on the same button.",
            "gallery.grid.description"),
        UI.Grid("gallery.grid",
            [GridTrack.Auto(), GridTrack.Pixel(68), GridTrack.Auto()],
            [GridTrack.Pixel(96), GridTrack.Star(_reverseGridProportions ? 1 : 2), GridTrack.Star(_reverseGridProportions ? 2 : 1)],
            UI.Text("Auto-sized heading spanning three columns", "gallery.grid.heading")
                .InGrid(columnSpan: 3),
            UI.Text("96 DIP", "gallery.grid.fixed")
                .InGrid(row: 1),
            UI.Button(_reverseGridProportions ? "1 part" : "2 parts", "gallery.grid.swap", "gallery.grid.first")
                .Classes("gallery-grid-cell").InGrid(row: 1, column: 1),
            UI.Button(_reverseGridProportions ? "2 parts" : "1 part", "gallery.grid.swap", "gallery.grid.second")
                .Classes("gallery-grid-cell").InGrid(row: 1, column: 2),
            UI.Button("Swap proportions", "gallery.grid.swap", "gallery.grid.swap-button")
                .Classes("gallery-grid-cell").InGrid(row: 2, columnSpan: 3))
            .Spacing(8, 8).Classes("gallery-explicit-grid"));
}
