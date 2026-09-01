using WidgetRail.WidgetProtocol;
using System.Text.Json;

internal static class ProtocolValidationDiagnosticTests
{
    internal static Task Run()
    {
        StructuralIdentifiersCarryClosedContext();
        UpdateElementReferencesCarryClosedContext();
        UnsafeIdentifiersAreNeverRetained();
        return Task.CompletedTask;
    }

    private static void UpdateElementReferencesCarryClosedContext()
    {
        var invalid = new PresentationUpdateBatch
        {
            WidgetInstanceId = "diagnostics.instance",
            PresentationGeneration = new string('a', 32),
            BaseSequence = 1,
            Sequence = 2,
            Operations =
            [
                new()
                {
                    Kind = PresentationUpdateOperationKind.SetProperties,
                    TargetId = "unsafe/path",
                    Properties =
                    [
                        new(PresentationProperty.Text,
                            JsonSerializer.SerializeToElement("Changed")),
                    ],
                },
            ],
        };
        var invalidTarget = PresentationUpdateValidator.Validate(invalid)
            .First(error => error.Code == "invalid_identifier");
        Equal(ProtocolValidationIdentifierKind.ElementReference,
            invalidTarget.IdentifierContext?.FieldKind);
        Equal(ProtocolValidationIdentifierState.UnsafeValue,
            invalidTarget.IdentifierContext?.State);
        Equal<string?>(null, invalidTarget.IdentifierContext?.Identifier);

        var current = Snapshot(Root(Button("first")));
        var missing = invalid with
        {
            Operations =
            [
                invalid.Operations[0] with { TargetId = "missing.target" },
            ],
        };
        try
        {
            _ = PresentationUpdateMaterializer.Apply(
                current, missing, missing.PresentationGeneration);
            throw new InvalidOperationException("Expected missing update target rejection.");
        }
        catch (ProtocolValidationException exception)
        {
            var context = exception.Errors[0].IdentifierContext ??
                throw new InvalidOperationException("Missing update target omitted context.");
            Equal(ProtocolValidationIdentifierKind.ElementReference, context.FieldKind);
            Equal(ProtocolValidationIdentifierState.Missing, context.State);
            Equal("missing.target", context.Identifier);
        }
    }

    private static void StructuralIdentifiersCarryClosedContext()
    {
        Context(Snapshot(Root(Button("first"))) with
        {
            InitialFocusId = "missing.initial",
        }, "invalid_focus_target", ProtocolValidationIdentifierKind.InitialFocus,
            ProtocolValidationIdentifierState.Missing, "missing.initial");

        Context(Snapshot(Root(Node("copy", ViewNodeKind.Text))) with
        {
            InitialFocusId = "copy",
        }, "invalid_focus_target", ProtocolValidationIdentifierKind.InitialFocus,
            ProtocolValidationIdentifierState.NotFocusable, "copy");

        var scoped = Root(
            Button("root.action"),
            Node("dialog", ViewNodeKind.Stack) with
            {
                InputScopeId = "dialog",
                Children = [Button("dialog.action")],
            });
        Context(Snapshot(scoped) with
        {
            InitialFocusId = "dialog.action",
        }, "initial_focus_outside_active_scope",
            ProtocolValidationIdentifierKind.InitialFocus,
            ProtocolValidationIdentifierState.OutsideActiveScope, "dialog.action");

        var group = Node("group", ViewNodeKind.Row) with
        {
            InitialChildFocusId = "missing.return",
            Children = [Button("group.action")],
        };
        Context(Snapshot(Root(group)), "invalid_initial_child_focus",
            ProtocolValidationIdentifierKind.ReturnFocus,
            ProtocolValidationIdentifierState.Missing, "missing.return");

        Context(Snapshot(Root(Button("source") with
        {
            Focus = new FocusNeighbors(Right: "missing.target"),
        })), "invalid_focus_target", ProtocolValidationIdentifierKind.ElementReference,
            ProtocolValidationIdentifierState.Missing, "missing.target");

        Context(Snapshot(Root(Button("duplicate"), Button("duplicate"))), "duplicate_id",
            ProtocolValidationIdentifierKind.ElementReference,
            ProtocolValidationIdentifierState.Duplicate, "duplicate");

        Context(Snapshot(Root(Node("missing.action", ViewNodeKind.Button) with
        {
            Text = "Missing action",
        })), "required", ProtocolValidationIdentifierKind.Action,
            ProtocolValidationIdentifierState.Missing, null);

        Context(Snapshot(Root(Node("text.action", ViewNodeKind.Text) with
        {
            Text = "Text",
            ActionId = "unknown.action",
        })), "action_not_allowed", ProtocolValidationIdentifierKind.Action,
            ProtocolValidationIdentifierState.UnknownAction, "unknown.action");

        var contextAction = Node("surface", ViewNodeKind.ActionSurface) with
        {
            ActionId = "surface.open",
            AccessibilityLabel = "Surface",
            ContextActions =
            [
                new("surface.more", "More"),
                new("surface.more", "More again"),
            ],
            Children = [Node("surface.copy", ViewNodeKind.Text) with { Text = "Copy" }],
        };
        Context(Snapshot(Root(contextAction)), "duplicate_context_action",
            ProtocolValidationIdentifierKind.ContextAction,
            ProtocolValidationIdentifierState.Duplicate, "surface.more");

        var disabled = ProtocolValidationIdentifierContext.Create(
            ProtocolValidationIdentifierKind.InitialFocus,
            ProtocolValidationIdentifierState.Disabled,
            "disabled.target");
        Equal(ProtocolValidationIdentifierState.Disabled, disabled.State);
        Equal("disabled.target", disabled.Identifier);
    }

    private static void UnsafeIdentifiersAreNeverRetained()
    {
        string[] unsafeValues =
        [
            "secret value",
            "line\nbreak",
            "C:\\private\\widget.json",
            "https://example.invalid/search?q=secret",
            new('x', ProtocolValidationIdentifierContext.MaximumIdentifierLength + 1),
        ];
        foreach (var value in unsafeValues)
        {
            var context = ProtocolValidationIdentifierContext.Create(
                ProtocolValidationIdentifierKind.InitialFocus,
                ProtocolValidationIdentifierState.Missing,
                value);
            Equal(ProtocolValidationIdentifierState.UnsafeValue, context.State);
            Equal<string?>(null, context.Identifier);
        }
    }

    private static void Context(
        ViewSnapshot snapshot,
        string code,
        ProtocolValidationIdentifierKind kind,
        ProtocolValidationIdentifierState state,
        string? identifier)
    {
        var error = ViewSnapshotValidator.Validate(snapshot).First(item => item.Code == code);
        var context = error.IdentifierContext ??
            throw new InvalidOperationException($"Validation error {code} omitted identifier context.");
        Equal(kind, context.FieldKind);
        Equal(state, context.State);
        Equal(identifier, context.Identifier);
    }

    private static ViewSnapshot Snapshot(ViewNode root) => new()
    {
        ProtocolVersion = ProtocolConstants.CurrentVersion,
        Sequence = 1,
        WidgetInstanceId = "diagnostics.instance",
        ActiveInputScopeId = "root",
        InitialFocusId = root.Children.FirstOrDefault(node => node.IsFocusable)?.Id,
        Root = root,
    };

    private static ViewNode Root(params ViewNode[] children) =>
        Node("root", ViewNodeKind.Stack) with
        {
            InputScopeId = "root",
            Children = children,
        };

    private static ViewNode Button(string id) => Node(id, ViewNodeKind.Button) with
    {
        Text = id,
        ActionId = $"{id}.activate",
    };

    private static ViewNode Node(string id, ViewNodeKind kind) => new()
    {
        Id = id,
        Kind = kind,
        Children = [],
    };

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
