using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetModalTests
{
    internal static Task Run()
    {
        var background = new WidgetView(UI.Stack("page",
            UI.Button("Game", "game.open", "game"),
            UI.VerticalScroll("library", UI.Button("Next", "next", "next"))).InputScope("page.scope"),
            "game", ActiveInputScopeId: "page.scope",
            Surface: new() { PreferredWidth = 900, PreferredHeight = 700 })
        {
            QuickActions = [new(ControllerButton.X, "game.open", "Open")],
        };
        var modal = new WidgetModal("details", "Game details",
            UI.Stack("details.body", UI.Button("Play", "game.play", "play"),
                UI.TextEntry("", "Notes", "save", "notes"),
                UI.VerticalScroll("details.nested", UI.Button("Nested", "nested", "nested"))),
            "play", "details.close");
        var snapshot = background.WithModal(modal).CreateSnapshot("instance", 1);
        Equal(55, snapshot.ProtocolVersion);
        Equal(ViewNodeKind.ModalLayer, snapshot.Root.Kind);
        Equal("details.scope", snapshot.ActiveInputScopeId);
        Equal("page", snapshot.Root.Children[0].Id);
        Equal(background.Surface, snapshot.Surface);
        Equal(0, snapshot.QuickActions.Count);
        var fullPinned = PinnedSurfaceContract.WithoutModal(snapshot);
        Equal("page.scope", fullPinned.ActiveInputScopeId);
        Equal("page", fullPinned.Root.Id);
        Equal(0, ViewSnapshotValidator.Validate(fullPinned).Count);
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var noBar = background.WithModal(modal with { ShowScrollbar = false }).CreateSnapshot("instance", 2);
        Equal(false, noBar.Root.Children[1].Children[1].ShowScrollbar);
        True(snapshot.Root.Children[1].Shortcuts.Any(s => s.Button == ControllerButton.B && s.ActionId == "details.close"));
        True(ViewSnapshotValidator.Validate(snapshot with { ActiveInputScopeId = "page.scope" })
            .Any(error => error.Code == "invalid_modal_layer"));
        True(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 54 })
            .Any(error => error.Code == "feature_requires_version"));
        var hinted = background.WithModal(modal with
        {
            HeaderActions = UI.Row("details.hints",
                UI.ControllerHint(ControllerButton.Y, "Refresh", "refresh.hint"),
                UI.ControllerHint(ControllerButton.B, "Close", "close.hint")),
        }).CreateSnapshot("instance", 5);
        Equal("details.close", snapshot.Root.Children[1].Children[0].Children[1].Id);
        Equal("details.hints", hinted.Root.Children[1].Children[0].Children[1].Id);
        Equal(0, ViewSnapshotValidator.Validate(hinted).Count);
        True(hinted.Root.Children[1].Shortcuts.Any(s => s.Button == ControllerButton.B && s.ActionId == "details.close"));
        Equal("play", hinted.InitialFocusId);
        Throws<InvalidOperationException>(() => background.WithModal(modal).WithModal(modal));
        var pinned = background with { PinnedLayouts = [WidgetView.PinnedLayout("compact", "Compact",
            new() { PreferredWidth = 400, PreferredHeight = 300 }, UI.Stack("pinned", UI.Text("Now playing", "pinned.text")))] };
        var pinnedSnapshot = pinned.WithModal(modal).CreateSnapshot("instance", 2);
        Equal(1, pinnedSnapshot.PinnedLayouts.Count);
        Equal(ViewNodeKind.Stack, pinnedSnapshot.PinnedLayouts[0].Root!.Kind);
        Equal(0, ViewSnapshotValidator.Validate(pinnedSnapshot).Count);
        Equal("page.scope", background.CreateSnapshot("instance", 3).ActiveInputScopeId);
        var textPage = new WidgetView(UI.Text("Loading", "loading"));
        Equal(0, ViewSnapshotValidator.Validate(textPage.WithModal(modal).CreateSnapshot("instance", 4)).Count);
        return Task.CompletedTask;
    }

    private static void True(bool condition) { if (!condition) throw new InvalidOperationException("Expected true."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
