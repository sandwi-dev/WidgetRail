using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WindowPreviewScenarios
{
    internal static Task Contract()
    {
        var preview = UI.WindowPreview("window-test", "preview", "Editor");
        var poster = UI.PosterTile("Editor", "Open", "switch", "poster", preview);
        var snapshot = new WidgetView(poster, "poster").CreateSnapshot("preview.test", 1);
        Check(snapshot.ProtocolVersion == 52);
        Check(snapshot.Root.Children[0].Kind == ViewNodeKind.WindowPreview);
        Check(snapshot.Root.IsFocusable && !snapshot.Root.Children[0].IsFocusable);
        Check(snapshot.Root.Children[0].ImageFit == ImageFit.Contain);
        Check(ViewSnapshotValidator.Validate(snapshot).Count == 0);
        var roundTrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        Check(roundTrip.Root.Children[0].WindowId == "window-test");
        var legacy = new WidgetView(UI.PosterTile("Legacy", "Ready", "open", "poster"))
            .CreateSnapshot("legacy.test", 1);
        Check(legacy.ProtocolVersion < 52);
        foreach (var node in new[]
        {
            snapshot.Root.Children[0] with { PreviewAspectRatio = double.NaN },
            snapshot.Root.Children[0] with { PreviewAspectRatio = 0 },
            snapshot.Root.Children[0] with { ActionId = "activate" },
            snapshot.Root.Children[0] with { WindowId = null },
            snapshot.Root.Children[0] with { Kind = ViewNodeKind.Text },
        })
            Check(ViewSnapshotValidator.Validate(snapshot with { Root = node, InitialFocusId = null }).Count > 0);
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 51 }).Count > 0);
        return Task.CompletedTask;
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Window preview contract failed.");
    }
}
