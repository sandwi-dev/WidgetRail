using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class FocusTargetLookupTests
{
    internal static Task Run()
    {
        ClassifiesSerializedWrappersScopesAndDisabledTargets();
        ClassifiesDuplicateAndMissingTargets();
        ValidatorUsesTheSharedTargetClassification();
        return Task.CompletedTask;
    }

    private static void ClassifiesSerializedWrappersScopesAndDisabledTargets()
    {
        var enabled = UI.Button("Enabled", "enabled", "focus.enabled")
            .FocusBackground(new WidgetArtworkHandle("focus.art"))
            .VisibleWhen(ResponsiveVisibility.CompactOnly);
        var collectionPresentation = UI.Button("Wrapped", "wrapped", "focus.wrapped")
            .CollectionItem(new WidgetCollectionItemKey("focus.wrapped.key"))
            .PresentOnFocus(UI.Text("Focused", "focus.wrapped.presentation"));
        var root = UI.BackgroundSurface(
            UI.Stack("focus.root",
                UI.Text("Copy", "focus.copy"),
                enabled,
                collectionPresentation,
                UI.Button("Disabled", "disabled", "focus.disabled") with { IsDisabled = true },
                UI.Stack("focus.dialog", UI.Button("Dialog", "dialog", "focus.dialog.action"))
                    .InputScope("focus.dialog.scope")),
            "focus.background");

        Equal(WidgetFocusTargetState.Valid,
            WidgetFocusTargetLookup.Resolve(root, "focus.enabled", "focus.background").State);
        Equal(WidgetFocusTargetState.Valid,
            WidgetFocusTargetLookup.Resolve(root, "focus.wrapped", "focus.background").State);
        var disabled = WidgetFocusTargetLookup.Resolve(
            root, "focus.disabled", "focus.background");
        Equal(WidgetFocusTargetState.Disabled, disabled.State);
        True(!disabled.IsEnabled,
            "A disabled target must remain distinct from an enabled focus target.");
        Equal(WidgetFocusTargetState.NotFocusable,
            WidgetFocusTargetLookup.Resolve(root, "focus.copy", "focus.background").State);
        Equal(WidgetFocusTargetState.OutsideActiveScope,
            WidgetFocusTargetLookup.Resolve(root, "focus.dialog.action", "focus.background").State);
        Equal("focus.enabled", WidgetFocusTargetLookup.First(root, "focus.background").Id);
        Equal("focus.enabled", WidgetFocusTargetLookup.First(
            root, "focus.background", includeDisabled: true).Id);

        var disabledOnly = UI.Stack("disabled.root",
            UI.Button("Disabled", "disabled", "disabled.only") with { IsDisabled = true });
        Equal("disabled.only", WidgetFocusTargetLookup.First(
            disabledOnly, "disabled.root", includeDisabled: true).Id);

        var families = UI.FocusPresentationSurface(
            UI.Stack("families.root",
                UI.Button("Button", "button", "families.button"),
                UI.Select("Select", [new SelectOption("one", "One", "select.one", IsSelected: true)], "families.select"),
                UI.TextEntry("", "Search", "search", "families.entry"),
                UI.Slider(1, 0, 2, 1, "volume", "families.slider", "Volume"),
                UI.Scrubber(TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1),
                    "seek", "families.scrubber"),
                UI.ActionSurface("open", "families.surface", "Open", ActionSurfaceOrientation.Vertical,
                    UI.Text("Surface", "families.surface.copy"))),
            UI.Text("Fallback", "families.fallback"),
            "families.presentation");
        foreach (var id in new[]
                 {
                     "families.button", "families.select", "families.entry", "families.slider",
                     "families.scrubber.slider", "families.surface",
                 })
            Equal(WidgetFocusTargetState.Valid,
                WidgetFocusTargetLookup.Resolve(families, id, "families.presentation").State);
    }

    private static void ClassifiesDuplicateAndMissingTargets()
    {
        var root = UI.Stack("duplicate.root",
            UI.Button("First", "first", "duplicate.target"),
            UI.Button("Second", "second", "duplicate.target"));
        Equal(WidgetFocusTargetState.Duplicate,
            WidgetFocusTargetLookup.Resolve(root, "duplicate.target", "duplicate.root").State);
        Equal(WidgetFocusTargetState.Missing,
            WidgetFocusTargetLookup.Resolve(root, "missing.target", "duplicate.root").State);
    }

    private static void ValidatorUsesTheSharedTargetClassification()
    {
        var root = new ViewNode
        {
            Id = "validator.root",
            Kind = ViewNodeKind.Stack,
            Children =
            [
                new ViewNode
                {
                    Id = "validator.duplicate",
                    Kind = ViewNodeKind.Button,
                    Text = "One",
                    ActionId = "one",
                },
                new ViewNode
                {
                    Id = "validator.duplicate",
                    Kind = ViewNodeKind.Button,
                    Text = "Two",
                    ActionId = "two",
                },
            ],
        };
        Equal(ViewFocusTargetState.Duplicate,
            ViewFocusTargetLookup.Resolve(root, "validator.duplicate", "validator.root").State);
        var snapshot = new ViewSnapshot
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            Sequence = 1,
            WidgetInstanceId = "validator.instance",
            ActiveInputScopeId = "validator.root",
            InitialFocusId = "validator.duplicate",
            Root = root,
        };
        True(ViewSnapshotValidator.Validate(snapshot).Any(error =>
                error.Path == "$.initialFocusId" &&
                error.IdentifierContext?.State == ProtocolValidationIdentifierState.Duplicate),
            "Initial-focus validation did not consume the shared duplicate classification.");

    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
