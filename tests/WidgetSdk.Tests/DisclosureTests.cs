using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class DisclosureTests
{
    internal static Task Run()
    {
        var calls = 0;
        ViewSnapshot View(bool expanded, long sequence) => new WidgetView(UI.Disclosure("details", "Details", expanded,
            "expand", "collapse", () => { calls++; return [UI.Button("Child", "child", "child")]; }), "details.header")
            .CreateSnapshot("disclosure", sequence);
        var collapsed = View(false, 1);
        Equal(0, calls);
        Equal(100, collapsed.ProtocolVersion);
        Equal(1, collapsed.Root.Children.Count);
        Equal("expand", collapsed.Root.Children[0].ActionId);
        Equal(false, collapsed.Root.Children[0].Disclosure!.IsExpanded);
        var expanded = View(true, 2);
        Equal(1, calls);
        Equal("collapse", expanded.Root.Children[0].ActionId);
        Equal("details.content", expanded.Root.Children[1].Id);
        Equal(0, ViewSnapshotValidator.Validate(expanded).Count);
        Equal(SnapshotJson.Serialize(expanded), SnapshotJson.Serialize(SnapshotJson.Deserialize(SnapshotJson.Serialize(expanded))));
        const string generation = "abababababababababababababababab";
        var update = WidgetPresentationDiff.Create(collapsed, expanded, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update!;
        Equal(SnapshotJson.Serialize(expanded), SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(collapsed, update, generation)));
        var closed = View(false, 3);
        update = WidgetPresentationDiff.Create(expanded, closed, generation, 2, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update!;
        Equal(SnapshotJson.Serialize(closed), SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(expanded, update, generation)));
        var normal = closed with { Sequence = 4, Root = closed.Root with { Children = [closed.Root.Children[0] with { Disclosure = null }] } };
        update = WidgetPresentationDiff.Create(closed, normal, generation, 3, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update!;
        Equal(SnapshotJson.Serialize(normal), SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(closed, update, generation)));
        Reject(expanded with { ProtocolVersion = 99 });
        Reject(expanded with { Root = expanded.Root with { Children = [expanded.Root.Children[0]] } });
        Reject(expanded with { Root = expanded.Root with { Children = [expanded.Root.Children[0] with { Disclosure = new("details", true) }, expanded.Root.Children[1]] } });
        Reject(expanded with { Root = expanded.Root with { Children = [expanded.Root.Children[0] with { Disclosure = new("details.header", true) }, expanded.Root.Children[1]] } });
        Reject(expanded with { Root = expanded.Root with { Children = [expanded.Root.Children[0], expanded.Root.Children[1] with { InputScopeId = "foreign" }] } });
        Reject(expanded with { Root = expanded.Root with { Children = [expanded.Root.Children[0], expanded.Root.Children[1], expanded.Root.Children[0] with { Id = "duplicate-header" }] } });
        Reject(expanded with { Root = expanded.Root with { Children = [expanded.Root.Children[0] with { Disclosure = new(null!, true) }, expanded.Root.Children[1]] } });
        Reject(expanded with { Root = expanded.Root with { Disclosure = new("details.content", true) } });
        Reject(expanded with { Root = expanded.Root with { Children = [collapsed.Root.Children[0], expanded.Root.Children[1]] } });
        var nested = new WidgetView(UI.Disclosure("outer", "Outer", true, "outer.expand", "outer.collapse",
            () => [UI.Disclosure("inner", "Inner", false, "inner.expand", "inner.collapse", () => throw new Exception("Hidden factory executed"))]))
            .CreateSnapshot("nested", 1);
        Equal(0, ViewSnapshotValidator.Validate(nested).Count);
        var preview = new WidgetView(UI.Stack("preview-root",
            UI.FocusPresentationSurface(UI.Button("Source", "source", "source").PresentOnFocus(
                UI.Disclosure("preview-details", "Details", true, "preview-expand", "preview-collapse", () => [UI.Button("Inside", "inside", "inside")])),
                UI.Text("", "empty"), "preview-surface") with { Preview = new() { EnterButton = ControllerButton.X } },
            UI.Stack("outside-content", UI.Text("Outside", "outside-text")))).CreateSnapshot("preview", 1);
        Equal(0, ViewSnapshotValidator.Validate(preview).Count);
        var surface = preview.Root.Children[0];
        var source = surface.Children[0];
        // Header stays in its preview, while the purported content is in the parent page.
        var foreign = source.FocusPresentation! with { Children = [source.FocusPresentation!.Children[0] with
            { Disclosure = new("outside-content", true) }] };
        Reject(preview with { Root = preview.Root with { Children = [surface with { Children = [source with { FocusPresentation = foreign }] }, preview.Root.Children[1]] } });
        try { UI.DisclosureHeader("header", "Header", false, "content", "toggle", "toggle"); }
        catch (ArgumentException) { return Task.CompletedTask; }
        throw new Exception("Toggle-only disclosure actions were accepted.");
    }
    private static void Reject(ViewSnapshot snapshot)
    {
        if (ViewSnapshotValidator.Validate(snapshot).Count == 0) throw new Exception("Invalid disclosure was accepted.");
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (expected is byte[] left && actual is byte[] right && left.SequenceEqual(right)) return;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
    }
}
