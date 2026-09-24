using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ControllerGlyphTests
{
    internal static Task Run()
    {
        foreach (var prompt in Enum.GetValues<ControllerPrompt>())
        {
            var snapshot = new WidgetView(UI.ControllerGlyph(prompt, "glyph"))
                .CreateSnapshot("controller.test", 1);
            Check(snapshot.ProtocolVersion == ProtocolConstants.ControllerGlyphVersion, "Glyph version negotiation");
            Check(snapshot.Root.Kind == ViewNodeKind.ControllerGlyph && snapshot.Root.ControllerPrompt == prompt, "Typed prompt identity");
            Check(!snapshot.Root.IsFocusable && snapshot.Root.ActionId is null && snapshot.Root.Children.Count == 0, "Glyph is input inert");
            Check(ViewSnapshotValidator.Validate(snapshot).Count == 0, "Prompt snapshot is valid");
            Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).Root.ControllerPrompt == prompt, "Prompt round trip");
            Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 53 }).Count != 0, "Old versions reject new glyphs");
        }
        foreach (var button in Enum.GetValues<ControllerButton>())
            _ = UI.ControllerHint(button, "Action", "hint").ToString();
        Check(UI.ControllerGlyph(ControllerButton.RightStick, "click").Prompt == ControllerPrompt.RightStickPress, "Button means stick press");
        Check(UI.ControllerGlyph(ControllerPrompt.RightStickMove, "move").Prompt != ControllerPrompt.RightStickPress, "Movement is distinct");
        var hint = UI.ControllerHint(ControllerButton.Menu, "Options", "hint")
            .ContextMenu(ControllerButton.Menu, new WidgetContextAction("open", "Open"));
        var view = new WidgetView(UI.Stack("root", hint,
            UI.ActionSurface("save", "save", "Save", ActionSurfaceOrientation.Horizontal,
                UI.ControllerHint(ControllerButton.Y, "Save", "save.hint"))));
        var composed = view.CreateSnapshot("composed", 1);
        Check(ViewSnapshotValidator.Validate(composed).Count == 0, "Glyphs compose with action surfaces and context hints");
        Check(hint.Children[1] is TextElement && hint.Children[0] is ControllerGlyphElement, "Hint keeps key/label child contract");
        Check(hint.Children[0].Classes("custom").StyleClasses.Contains("wrail-controller-hint__key"), "Hint key theme hook cannot be replaced");
        Check(composed.Root.Children[0].ContextMenuButton == ControllerButton.Menu, "Menu declarations retained");

        var before = new WidgetView(UI.ControllerGlyph(ControllerPrompt.A, "glyph")).CreateSnapshot("update", 1);
        foreach (var invalid in new[]
                 {
                     before.Root with { ControllerPrompt = (ControllerPrompt)999 },
                     before.Root with { ControllerPrompt = null },
                     before.Root with { ActionId = "activate" },
                     before.Root with { Glyph = WidgetGlyph.Play },
                     before.Root with { Text = "A" },
                     before.Root with { Children = [new() { Id = "child", Kind = ViewNodeKind.Text, Text = "Child" }] },
                     before.Root with { Kind = ViewNodeKind.Text, Text = "Misplaced prompt" },
                 })
            Check(ViewSnapshotValidator.Validate(before with { Root = invalid }).Count != 0, "Malformed glyph rejected");
        var after = before with { Sequence = 2, Root = before.Root with { ControllerPrompt = ControllerPrompt.RightStickMove } };
        const string generation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var update = WidgetPresentationDiff.Create(before, after, generation, 1,
            PresentationUpdateCapabilities.Current, WidgetPresentationTransactionKind.IncrementalUpdate);
        Check(update.Update?.Operations.Single().Properties?.Single().Property == PresentationProperty.ControllerPrompt, "Prompt changes are explicit deltas");
        Check(PresentationUpdateMaterializer.Apply(before, update.Update!, generation).Root.ControllerPrompt ==
            ControllerPrompt.RightStickMove, "Prompt delta materialization");
        if (Environment.GetEnvironmentVariable("WRAIL_CONTROLLER_GLYPH_FIXTURE") is { Length: > 0 } fixture)
        {
            var demo = new WidgetView(UI.Stack("root",
                UI.Row("top", UI.ControllerHint(ControllerButton.Y, "Refresh", "refresh"),
                    UI.ControllerHint(ControllerButton.Menu, "More", "more")).Classes("demo-row"),
                UI.Row("nav", UI.ControllerGlyph(ControllerButton.LeftTrigger, "previous"),
                    UI.Text("Home     Library     Search", "navigation"),
                    UI.ControllerGlyph(ControllerButton.RightTrigger, "next")).Classes("demo-row"),
                UI.Row("movement", UI.ControllerHint(ControllerPrompt.RightStickMove, "Scroll", "scroll"),
                    UI.ControllerHint(ControllerPrompt.RightStickPress, "Press stick", "press")).Classes("demo-row")))
                .CreateSnapshot("controller.demo", 1);
            File.WriteAllBytes(fixture, SnapshotJson.Serialize(demo));
        }
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
