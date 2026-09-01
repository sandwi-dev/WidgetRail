using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using System.Text.Json;

internal static class FocusPresentationTests
{
    internal static Task Run()
    {
        var snapshot = new WidgetView(
            UI.FocusPresentationSurface(
                UI.Row("focus.content",
                    UI.Button("First", "first", "focus.first")
                        .PresentOnFocus(UI.Stack("focus.first.presentation",
                            UI.Text("First details", "focus.first.label"))),
                    UI.Button("Second", "second", "focus.second")
                        .PresentOnFocus(UI.Stack("focus.second.presentation",
                            UI.Text("Second details", "focus.second.label")))),
                UI.Stack("focus.default.presentation",
                    UI.Text("Choose an item", "focus.default.label")),
                "focus.surface"),
            InitialFocusId: "focus.first").CreateSnapshot("focus.instance", 1);

        Equal(ProtocolConstants.FocusAssociatedPresentationVersion, snapshot.ProtocolVersion);
        Equal(ViewNodeKind.FocusPresentationSurface, snapshot.Root.Kind);
        Equal("focus.default.presentation", snapshot.Root.DefaultFocusPresentation?.Id);
        Equal("focus.first.presentation",
            snapshot.Root.Children[0].Children[0].FocusPresentation?.Id);
        Equal("focus.second.presentation",
            snapshot.Root.Children[0].Children[1].FocusPresentation?.Id);
        True(!snapshot.Root.IsFocusable,
            "The presentation consumer gained focus authority.");
        True(!snapshot.Root.DefaultFocusPresentation!.IsFocusable &&
             !snapshot.Root.Children[0].Children[0].FocusPresentation!.IsFocusable,
            "A presentation fragment gained focus authority.");
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);

        var outsideConsumer = snapshot with
        {
            Root = snapshot.Root.Children[0],
        };
        HasError(outsideConsumer, "focus_presentation_consumer_required");

        var interactiveFragment = snapshot with
        {
            Root = snapshot.Root with
            {
                DefaultFocusPresentation = new ViewNode
                {
                    Id = "focus.invalid.action",
                    Kind = ViewNodeKind.Button,
                    Text = "Unsafe",
                    ActionId = "unsafe",
                },
            },
        };
        HasError(interactiveFragment, "interactive_focus_presentation_fragment");

        var nonFocusableSource = snapshot with
        {
            Root = snapshot.Root with
            {
                Children = [snapshot.Root.Children[0] with
                {
                    FocusPresentation = new ViewNode
                    {
                        Id = "focus.invalid.source",
                        Kind = ViewNodeKind.Text,
                        Text = "Invalid",
                    },
                }],
            },
        };
        HasError(nonFocusableSource, "focus_presentation_on_non_focusable_node");

        var tooLargeChildren = Enumerable.Range(
                0, ProtocolConstants.MaximumFocusPresentationNodes)
            .Select(index => new ViewNode
            {
                Id = $"focus.bound.{index}",
                Kind = ViewNodeKind.Text,
                Text = index.ToString(),
            })
            .ToArray();
        var tooLarge = snapshot with
        {
            Root = snapshot.Root with
            {
                DefaultFocusPresentation = new ViewNode
                {
                    Id = "focus.bound.root",
                    Kind = ViewNodeKind.Stack,
                    Children = tooLargeChildren,
                },
            },
        };
        HasError(tooLarge, "focus_presentation_too_large");

        const string generation = "0123456789abcdef0123456789abcdef";
        var replacement = new ViewNode
        {
            Id = "focus.first.presentation.replacement",
            Kind = ViewNodeKind.Text,
            Text = "Replacement details",
        };
        var update = new PresentationUpdateBatch
        {
            ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion,
            WidgetInstanceId = snapshot.WidgetInstanceId,
            PresentationGeneration = generation,
            BaseSequence = snapshot.Sequence,
            Sequence = snapshot.Sequence + 1,
            Operations =
            [
                new PresentationUpdateOperation
                {
                    Kind = PresentationUpdateOperationKind.SetProperties,
                    TargetId = "focus.first",
                    Properties =
                    [
                        new PresentationPropertyChange(
                            PresentationProperty.FocusPresentation,
                            PresentationUpdateJson.Value<ViewNode?>(replacement)),
                    ],
                },
            ],
        };
        Equal(0, PresentationUpdateValidator.Validate(update).Count);
        var updated = PresentationUpdateMaterializer.Apply(snapshot, update, generation);
        Equal("focus.first.presentation.replacement",
            updated.Root.Children[0].Children[0].FocusPresentation?.Id);

        return Task.CompletedTask;
    }

    private static void HasError(ViewSnapshot snapshot, string code) =>
        True(ViewSnapshotValidator.Validate(snapshot).Any(error => error.Code == code),
            $"Expected validation error '{code}'.");

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
