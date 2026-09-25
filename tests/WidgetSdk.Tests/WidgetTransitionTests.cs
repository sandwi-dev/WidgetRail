using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetTransitionTests
{
    internal static Task Run()
    {
        var page = UI.Stack("body", UI.Button("Play", "play", "play"))
            .InputScope("page.scope").TransitionContent("tabs", "home", 0);
        var first = new WidgetView(page, "play").CreateSnapshot("widget", 1);
        Check(first.ProtocolVersion == ProtocolConstants.WidgetTransitionVersion, "version advertisement");
        Check(first.ActiveInputScopeId == "page.scope", "wrapper preserves input scope");
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(first)).Root.Transition == first.Root.Transition,
            "strict JSON round trip");
        var next = first with { Sequence = 2, Root = first.Root with
            { Transition = new("tabs", "library", 1) } };
        Check(ViewSnapshotValidator.Validate(next).Count == 0, "valid section change");
        Check(ViewSnapshotValidator.Validate(next with { ProtocolVersion = 57 }).Count != 0,
            "older hosts cannot silently drop transition contract");
        Check(ViewSnapshotValidator.Validate(next with { Root = next.Root with
            { Transition = new("tabs", "library", 2000) } }).Count != 0, "bounded order");
        Check(ViewSnapshotValidator.Validate(next with { Root = next.Root with
            { Transition = new("tabs", "library", 1, (WidgetTransitionKind)99) } }).Count != 0,
            "closed kind enumeration");
        var shell = UI.NavigationShellParts("tabs", "library", NavigationShellContentEntry.Unavailable, page,
            [new("home", "Home", "home", WidgetGlyph.Play),
             new("library", "Library", "library", WidgetGlyph.Music)]).WithTransitions();
        var navigation = new WidgetView(shell.CompactNavigation.VisibleWhen(ResponsiveVisibility.Always))
            .CreateSnapshot("nav", 1);
        Check(navigation.Root.Children.All(button => button.Transition is
            { GroupId: "tabs", Key: "library", Order: 1, Kind: WidgetTransitionKind.Selection }),
            "navigation shares one section key and direction");
        Check(navigation.Root.StyleClasses.Contains("wrail-navigation-shell__compact"),
            "animation wrappers preserve semantic theme classes");
        var conflicting = new WidgetView(UI.Stack("root",
            UI.Stack("one").TransitionContent("tabs", "home", 0),
            UI.Stack("two").TransitionContent("tabs", "library", 1)));
        try { conflicting.CreateSnapshot("conflict", 1); throw new InvalidOperationException("conflicting group accepted"); }
        catch (ProtocolValidationException error) { Check(error.Errors.Any(item => item.Code == "transition_group_conflict"), "conflict diagnostic"); }
        var nested = new WidgetView(UI.Stack("outer",
            UI.Stack("inner").TransitionContent("inner.tabs", "home", 0)).TransitionContent("outer.tabs", "home", 0));
        try { nested.CreateSnapshot("nested", 1); throw new InvalidOperationException("nested captures accepted"); }
        catch (ProtocolValidationException error) { Check(error.Errors.Any(item => item.Code == "nested_content_transition"), "nested diagnostic"); }
        return Task.CompletedTask;
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
    }
}
