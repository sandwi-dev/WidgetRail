using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

// Mirrors the Game Details Home journal: three dates of sixteen captures, an
// expanding saved-media stage, and a navigation bar between Play and the list.
internal sealed class CaptureRailFocusFixture : Widget
{
    private sealed record Selection(int Row = -1, string? Capture = null, bool Expanded = false);
    private static readonly int[] Dates = [0, 1, 2];
    private readonly WidgetLoadedCollection<Selection, int> journal;
    private FocusGroupEntryRequest? entry;

    internal CaptureRailFocusFixture()
    {
        journal = CreateLoadedCollection("journal.source", "game", new Selection(), Dates,
            new WidgetLoadedCollectionOptions<Selection, int>
            {
                PreserveOccurrences = true, ItemKey = row => new("day." + row),
                RenderItem = RenderDate, OnAction = SelectCapture,
                ResolveArtwork = (_, _, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(null)
            });
    }

    private static WidgetElement RenderDate(Selection selection, int row, WidgetIndexedItemContext ids)
    {
        var items = Enumerable.Range(0, MediaPlayerDefinition.MaximumGalleryItems).Select(index =>
        {
            var id = (row * 16 + index + 1).ToString("x32");
            var attachment = new CaptureAttachment(id, "image/png", 640, 360, 128, 0, long.MaxValue);
            return new CaptureStripItem(new(id, attachment, 100) { Revision = 1 }, new("thumb." + id),
                $"Capture {row}/{index}", "open." + id);
        }).ToArray();
        return UI.CaptureStrip(ids.Id("row"), "Day " + row, items, selection.Row == row ? selection.Capture : null,
            selection.Row == row && selection.Expanded, "close");
    }

    private ValueTask SelectCapture(Selection previous, int row, WidgetActionEvent action, CancellationToken token)
    {
        var selection = action.ActionId == "close" ? previous with { Expanded = false } :
            new Selection(row, action.ActionId["open.".Length..], true);
        journal.Publish("game", selection, Dates);
        entry = CreateFocusRequest().EnterCollection(journal, "journal", journal.FocusTarget("journal", new("day." + row), row));
        Invalidate();
        return ValueTask.CompletedTask;
    }

    public override WidgetView Render()
    {
        var list = UI.CollectionList("journal", journal, 120, "Recent activity")
            .WithDirectionalAnchor(FocusAnchorPoint.Leading, 25)
            .Grouped(Dates.Select(i => new IndexedCollectionGroup("d" + i, "Day " + i, 1)).ToArray())
            .FocusUp("tab").Classes("probe-list");
        var grid = UI.Grid("frame", [GridTrack.Auto(), GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            UI.Button("Play", "play", "play").Classes("probe-play").FocusDown("tab"),
            UI.Button("Home", "tab", "tab").FocusUp("play").FocusDown("journal").InGrid(row: 1),
            list.InGrid(row: 2)).RevealBodyOnFocus().Classes("probe-root");
        return new(grid.InputScope("journal.scope"), "tab", ActiveInputScopeId: "journal.scope") { FocusGroupEntryRequest = entry };
    }
}
