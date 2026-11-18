using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class RichTextImageTests
{
    internal static Task Run()
    {
        var rich = UI.RichText("message", UI.Text("Hello ", "text"),
            UI.Image("https://cdn.discordapp.com/emojis/123.png?size=32", "emoji", ":wave:", ImageFit.Contain),
            UI.Text(" 👨‍👩‍👧‍👦", "unicode"));
        var snapshot = new WidgetView(rich).CreateSnapshot("rich-image", 1);
        Check(snapshot.ProtocolVersion == ProtocolConstants.InlineRichImageVersion && ViewSnapshotValidator.Validate(snapshot).Count == 0,
            "Inline images must negotiate and validate their additive protocol");
        Check(snapshot.Root.Children[1].StyleClasses.Contains("wrail-inline-image") && snapshot.Root.Children[1].AccessibilityLabel == ":wave:",
            "Inline image lost default sizing or accessible text");
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = ProtocolConstants.InlineRichImageVersion - 1 }).Any(error => error.Code == "feature_requires_version"),
            "Older host must not silently accept image spans");
        var row = new WidgetView(UI.ReadingSurface("row", "Hello wave", ActionSurfaceOrientation.Vertical, rich)).CreateSnapshot("rich-row", 1);
        Check(ViewSnapshotValidator.Validate(row).Count == 0, "Presentational inline images should work in indexed reading rows");
        var bad = snapshot with { Root = snapshot.Root with { Children = [snapshot.Root.Children[1] with { ActionId = "hidden-action" }] } };
        Check(ViewSnapshotValidator.Validate(bad).Any(error => error.Code == "invalid_rich_text"), "Inline image may not smuggle interaction");
        try
        {
            UI.RichText("overflow", Enumerable.Range(0, 17).Select(index => (WidgetElement)UI.Image("https://example.test/emoji.png", "emoji." + index, "emoji")).ToArray());
            throw new Exception("Inline image budget not enforced");
        }
        catch (ArgumentException) { }
        return Task.CompletedTask;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
