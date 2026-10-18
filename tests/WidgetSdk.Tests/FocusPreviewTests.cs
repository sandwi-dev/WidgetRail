using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class FocusPreviewTests
{
    private static ViewSnapshot Snapshot(FocusPreviewOptions? options = null, bool retain = false) =>
        new WidgetView(UI.FocusPresentationSurface(
            UI.Button("Inspect", "inspect", "preview.source").PresentOnFocus(UI.Text("Details", "preview.details")),
            UI.Text("Fallback", "preview.empty"), "preview.surface") with { Preview = options, RetainLastPresentation = retain })
        .CreateSnapshot("preview.widget", 1);

    internal static Task Contract()
    {
        var before = Snapshot(new());
        Check(before.ProtocolVersion == 92 && before.Root.FocusPreview?.DelayMilliseconds == 350);
        Check(WinUiPresentationContract.Validate(before).Count == 0);
        Check(Snapshot().ProtocolVersion < 92);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(before)).Root.FocusPreview == before.Root.FocusPreview);
        Check(ViewSnapshotValidator.Validate(before with { ProtocolVersion = 91 }).Any(error => error.Code == "feature_requires_version"));
        foreach (var invalid in new[] { new FocusPreviewOptions(Width: double.NaN), new(Height: double.PositiveInfinity),
            new(Width: 159), new(Width: 961), new(Height: 95), new(Height: 801), new(Placement: (AnchorPlacement)99),
            new(OnFocus: false, OnHover: false), new(DelayMilliseconds: -1), new(DelayMilliseconds: 1501) })
        {
            try { Snapshot(invalid); throw new Exception("Invalid preview accepted"); }
            catch (ProtocolValidationException error) when (error.Errors.Any(item => item.Code == "invalid_focus_preview")) { }
        }
        Check(WinUiPresentationContract.Validate(Snapshot(new(DelayMilliseconds: 0, OnFocus: false))).Count == 0);
        Check(WinUiPresentationContract.Validate(Snapshot(new(DelayMilliseconds: 1500, OnHover: false))).Count == 0);
        try { Snapshot(new(), retain: true); throw new Exception("Stale preview retention accepted"); }
        catch (ProtocolValidationException error) when (error.Errors.Any(item => item.Code == "focus_preview_retention_not_allowed")) { }
        var wrongNode = before with { Root = before.Root.Children[0] with { FocusPresentation = null, FocusPreview = new() } };
        Check(ViewSnapshotValidator.Validate(wrongNode).Any(error => error.Code == "focus_preview_not_allowed"));
        var interactive = before with { Root = before.Root with { Children = [before.Root.Children[0] with {
            FocusPresentation = new() { Id = "invalid.preview", Kind = ViewNodeKind.Button, Text = "Click", ActionId = "action" } }] } };
        Check(ViewSnapshotValidator.Validate(interactive).Any(error => error.Code == "interactive_focus_presentation_fragment"));
        const string generation = "abababababababababababababababab";
        var after = before with { Sequence = 2, Root = before.Root with { FocusPreview = new(420, 320, AnchorPlacement.Above, DelayMilliseconds: 500) } };
        var update = WidgetPresentationDiff.Create(before, after, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null);
        Check(SnapshotJson.Serialize(after).SequenceEqual(SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(before, update!, generation))));
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if (!value) throw new Exception("Focus preview contract failed"); }
}
