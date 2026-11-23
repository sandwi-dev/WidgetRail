using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A saved capture with its scoped thumbnail and widget-owned selection action.</summary>
public sealed record CaptureStripItem(CaptureLibraryItem Capture, WidgetArtworkHandle Thumbnail, string AccessibleName, string OpenActionId);

public static partial class UI
{
    /// <summary>A horizontal thumbnail rail with one optional expanded saved capture directly above it.
    /// Keep the selected capture ID on close to restore focus. Only the expanded original is declared.</summary>
    public static ActionSurfaceElement CaptureStrip(string id, string accessibleName, IReadOnlyList<CaptureStripItem> items,
        string? selectedCaptureId, bool expanded, string closeActionId)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > MediaPlayerDefinition.MaximumGalleryItems)
            throw new ArgumentOutOfRangeException(nameof(items));
        StableIdentifier.Validate(closeActionId, nameof(closeActionId));
        var scope = WidgetIds.Scope(id);
        var tiles = new List<WidgetElement>(items.Count);
        var captures = new HashSet<string>(StringComparer.Ordinal);
        var selected = -1;
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            var capture = item.Capture;
            if (!capture.Available || !MediaPlayerSource.FromSavedCapture(capture.Id, capture.Revision).IsWellFormed() || !captures.Add(capture.Id))
                throw new ArgumentException("Each strip item needs a distinct available saved capture.", nameof(items));
            if (capture.Id == selectedCaptureId) selected = tiles.Count;
            var tile = scope.KeyedId("capture", capture.Id);
            var layers = new List<WidgetElement>
            {
                Artwork(item.Thumbnail, tile + ".thumbnail", item.AccessibleName, ImageFit.Contain).Classes("wrail-capture-strip__thumbnail")
            };
            if (capture.Media.ContentType == "video/mp4")
                layers.Add(Icon(WidgetGlyph.Play, tile + ".play", "Video").Classes("wrail-capture-strip__play")
                    .AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center));
            var action = expanded && capture.Id == selectedCaptureId ? closeActionId : item.OpenActionId;
            tiles.Add(ActionSurface(action, tile, item.AccessibleName, ActionSurfaceOrientation.Vertical,
                Grid(tile + ".picture", [], [], layers.ToArray()).Classes("wrail-capture-strip__picture")).Classes("wrail-capture-strip__tile")
                .Selected(capture.Id == selectedCaptureId));
        }

        // Keep the viewport and every thumbnail keyed identically as the preview opens and closes.
        var rail = HorizontalScroll(scope.Id("strip"), tiles.ToArray()).Classes("wrail-capture-strip__scroll");
        var showPreview = expanded && selected >= 0;
        GridLayoutElement stage;
        if (showPreview)
        {
            var item = items[selected];
            var player = MediaPlayer(MediaPlayerSource.FromSavedCapture(item.Capture.Id, item.Capture.Revision),
                scope.Id("player"), item.AccessibleName, new(Muted: true)).Classes("wrail-capture-strip__player");
            stage = Grid(scope.Id("stage"), [], [], player).Classes("wrail-capture-strip__stage");
        }
        else
        {
            // Keep the rail at the same native child index so WinUI retains its focused ScrollViewer.
            stage = Grid(scope.Id("stage"), [], []).Classes("wrail-capture-strip__empty");
        }
        var group = ReadingGroup(id, accessibleName, ActionSurfaceOrientation.Vertical, stage, rail).Classes("wrail-capture-strip");
        return showPreview ? group.Shortcut(ControllerButton.B, actionId: closeActionId, label: "Close preview") : group;
    }
}
