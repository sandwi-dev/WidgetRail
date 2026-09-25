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
        var plain = UI.Stack("plain", UI.Button("Play", "play", "play"));
        NavigationShellDestination[] destinations = [new("home", "Home", "home", WidgetGlyph.Play),
            new("library", "Library", "library", WidgetGlyph.Music)];
        var parts = UI.NavigationShellParts("composed", "home", "play", plain, destinations);
        var legacy = new WidgetView(UI.NavigationShell("composed", "home", "play", plain, destinations)).CreateSnapshot("same", 1);
        var composed = new WidgetView(parts.Compose()).CreateSnapshot("same", 1);
        Check(SnapshotJson.Serialize(legacy).SequenceEqual(SnapshotJson.Serialize(composed)), "Compose preserves the existing shell contract");
        var animated = new WidgetView(parts.WithTransitions().Compose()).CreateSnapshot("animated", 1);
        Check(ViewSnapshotValidator.Validate(animated).Count == 0, "composed animated shell validates");
        var selected = new WidgetView((UI.Button("Home", "home", "home") with { IsSelected = true })
            .TransitionSelection("custom", "home", 0)).CreateSnapshot("custom", 1);
        Check(selected.Root.Transition?.Kind == WidgetTransitionKind.Selection && selected.Root.IsSelected == true,
            "custom tab selection retains its selected state");
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
