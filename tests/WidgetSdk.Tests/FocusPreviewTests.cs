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
        Check(before.Root.FocusPreview?.SizeToContentHeight == false);
        var enterable = Snapshot(new() { EnterButton = ControllerButton.RightStick });
        Check(enterable.ProtocolVersion == 94 && WinUiPresentationContract.Validate(enterable).Count == 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(enterable)).Root.FocusPreview?.EnterButton == ControllerButton.RightStick);
        Check(ViewSnapshotValidator.Validate(enterable with { ProtocolVersion = 93 }).Any(error => error.Code == "feature_requires_version"));
        Check(!System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(before)).Contains("sizeToContentHeight"));
        var fitted = Snapshot(new() { SizeToContentHeight = true });
        Check(fitted.ProtocolVersion == 93 && WinUiPresentationContract.Validate(fitted).Count == 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(fitted)).Root.FocusPreview?.SizeToContentHeight == true);
        Check(ViewSnapshotValidator.Validate(fitted with { ProtocolVersion = 92 }).Any(error => error.Code == "feature_requires_version"));
        Check(ViewSnapshotValidator.Validate(before with { ProtocolVersion = 91 }).Any(error => error.Code == "feature_requires_version"));
        foreach (var invalid in new[] { new FocusPreviewOptions(Width: double.NaN), new(Height: double.PositiveInfinity),
            new(Width: 159), new(Width: 961), new(Height: 95), new(Height: 801), new(Placement: (AnchorPlacement)99),
            new(OnFocus: false, OnHover: false), new(DelayMilliseconds: -1), new(DelayMilliseconds: 1501),
            new() { EnterButton = ControllerButton.A }, new() { EnterButton = ControllerButton.B }, new() { EnterButton = (ControllerButton)999 } })
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
        var enabled = interactive with { ProtocolVersion = 94, Root = interactive.Root with { FocusPreview = new() { EnterButton = ControllerButton.X } } };
        Check(ViewSnapshotValidator.Validate(enabled).Count == 0);
        foreach (var forbidden in new[] { ViewNodeKind.TextEntry, ViewNodeKind.IndexedCollection, ViewNodeKind.ModalLayer, ViewNodeKind.FocusPresentationSurface })
        {
            var invalid = enabled with { Root = enabled.Root with { Children = [enabled.Root.Children[0] with {
                FocusPresentation = enabled.Root.Children[0].FocusPresentation! with { Kind = forbidden } }] } };
            Check(ViewSnapshotValidator.Validate(invalid).Any(error=>error.Code=="invalid_interactive_preview"));
        }
        const string generation = "abababababababababababababababab";
        var after = fitted with { Sequence = 2, Root = fitted.Root with { FocusPreview = new(420, 320, AnchorPlacement.Above, DelayMilliseconds: 500) { SizeToContentHeight = true } } };
        var update = WidgetPresentationDiff.Create(fitted, after, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null);
        Check(SnapshotJson.Serialize(after).SequenceEqual(SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(fitted, update!, generation))));
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if (!value) throw new Exception("Focus preview contract failed"); }
}
