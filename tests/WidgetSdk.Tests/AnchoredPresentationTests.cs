using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class AnchoredPresentationTests
{
    private static ViewSnapshot Snapshot(AnchoredPresentation? anchored) => new WidgetView(
        UI.Stack("page", UI.Button("Inspect", "inspect", "opener")).InputScope("page"), "opener")
        .WithModal(new("inspect", "Inspect", UI.Button("Done", "close", "done"), "done", "close")
        { AnchoredPresentation = anchored }).CreateSnapshot("anchored.widget", 1);

    internal static Task Contract()
    {
        var before = Snapshot(new(new("opener")));
        Check(before.ProtocolVersion == 92 && before.Root.AnchoredPresentation?.Width == 360);
        Check(before.InitialFocusId == "done" && before.ActiveInputScopeId != "page");
        Check(before.Root.Children[1].Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.B && shortcut.ActionId == "close"));
        Check(WinUiPresentationContract.Validate(before).Count == 0);
        Check(ViewSnapshotValidator.Validate(before with { ProtocolVersion = 91 }).Any(error => error.Code == "feature_requires_version"));
        var read = SnapshotJson.Deserialize(SnapshotJson.Serialize(before));
        Check(read.Root.AnchoredPresentation == before.Root.AnchoredPresentation);
        Check(read.Root.AnchoredPresentation?.SizeToContentHeight == false);
        Check(!System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(before)).Contains("sizeToContentHeight"));
        var fitted = Snapshot(new(new("opener")) { SizeToContentHeight = true });
        Check(fitted.ProtocolVersion == 93 && WinUiPresentationContract.Validate(fitted).Count == 0);
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(fitted)).Root.AnchoredPresentation?.SizeToContentHeight == true);
        Check(ViewSnapshotValidator.Validate(fitted with { ProtocolVersion = 92 }).Any(error => error.Code == "feature_requires_version"));
        Check(Snapshot(null).ProtocolVersion < 92);
        var preview = new WidgetView(UI.Button("Choose", "choose", "source"))
            .WithInteractivePreview(new("interactive", "Details", UI.Button("Apply", "apply", "apply"), "apply", "back", new("source")))
            .CreateSnapshot("interactive.widget", 1);
        Check(preview.ProtocolVersion == 94 && preview.Root.AnchoredPresentation?.IsInteractivePreview == true);
        Check(WinUiPresentationContract.Validate(preview).Count == 0);
        Check(preview.Root.Children[1].Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.B && shortcut.ActionId == "back"));
        Check(!preview.Root.Children[1].Children[0].Children.Any(node => node.Kind == ViewNodeKind.Button));
        Check(ViewSnapshotValidator.Validate(preview with { ProtocolVersion = 93 }).Any(error => error.Code == "feature_requires_version"));
        Check(ViewSnapshotValidator.Validate(Snapshot(new(new("removed-anchor")))).Count == 0);
        // Missing/recycled indexed occurrences fall back without granting index-only authority.
        Check(ViewSnapshotValidator.Validate(Snapshot(new(new(IndexedItem: new("collection", "source", 4, "exact-key", 75))))).Count == 0);
        Check(ViewSnapshotValidator.Validate(before with { Root = before.Root with {
            AnchoredPresentation = new(new(IndexedItem: new("opener", "source", 4, "exact-key", 75))) } })
            .Any(error => error.Code == "invalid_presentation_anchor"));
        Check(ViewSnapshotValidator.Validate(before with { Root = before.Root with {
            AnchoredPresentation = new(new("done")) } }).Any(error => error.Code == "invalid_presentation_anchor"));
        foreach (var invalid in new[] {
            new AnchoredPresentation(new()), new(new("bad id")), new(null!),
            new(new("opener"), Width: double.NaN), new(new("opener"), Height: double.PositiveInfinity),
            new(new("opener"), Width: 961), new(new("opener"), Height: 0),
            new(new("opener"), Placement: (AnchorPlacement)99),
            new(new("opener", new("collection", "source", 1, "key", 0))) })
        {
            try { Snapshot(invalid); throw new Exception("Invalid popover accepted"); }
            catch (ArgumentException) { }
        }
        var misplaced = before with { Root = before.Root with { Children = [before.Root.Children[0] with {
            AnchoredPresentation = new(new("opener")) }, before.Root.Children[1]] } };
        Check(ViewSnapshotValidator.Validate(misplaced).Any(error => error.Code == "anchored_presentation_not_allowed"));
        const string generation = "abababababababababababababababab";
        var after = fitted with { Sequence = 2, Root = fitted.Root with { AnchoredPresentation = new(new("opener"), 420, 360, AnchorPlacement.Left) { SizeToContentHeight = true } } };
        var update = WidgetPresentationDiff.Create(fitted, after, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null);
        var applied = PresentationUpdateMaterializer.Apply(fitted, update!, generation);
        Check(SnapshotJson.Serialize(after).SequenceEqual(SnapshotJson.Serialize(applied)));
        Check(PresentationPropertyMetadata.Impact(PresentationProperty.AnchoredPresentation).HasFlag(PresentationPropertyImpact.MeasureLayout));
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if (!value) throw new Exception("Anchored presentation contract failed"); }
}
