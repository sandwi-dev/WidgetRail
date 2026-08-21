using System.Text.Json;

namespace WidgetRail.WidgetProtocol;

public static class PresentationUpdateValidator
{
    public static IReadOnlyList<ProtocolValidationError> Validate(
        PresentationUpdateBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var errors = new List<ProtocolValidationError>();
        if (batch.ProtocolVersion != ProtocolConstants.AtomicPresentationUpdateVersion)
            Add("$.protocolVersion", "unsupported_update_version",
                $"Expected update protocol {ProtocolConstants.AtomicPresentationUpdateVersion}.");
        CheckIdentifier(batch.WidgetInstanceId, "$.widgetInstanceId");
        if (batch.PresentationGeneration is not { Length: 32 or 64 } ||
            !batch.PresentationGeneration.All(char.IsAsciiHexDigit))
            Add("$.presentationGeneration", "invalid_generation",
                "Presentation generation must be 32 or 64 hexadecimal characters.");
        if (batch.BaseSequence < 0 || batch.Sequence <= batch.BaseSequence)
            Add("$.sequence", "invalid_sequence",
                "Update sequence must be greater than its non-negative base sequence.");
        if (batch.Operations is null)
        {
            Add("$.operations", "required", "Update operations are required.");
            return errors;
        }
        if (batch.Operations.Count > ProtocolConstants.MaximumPresentationUpdateOperations)
            Add("$.operations", "too_many_operations",
                $"At most {ProtocolConstants.MaximumPresentationUpdateOperations} operations are allowed.");

        for (var index = 0; index < batch.Operations.Count; index++)
        {
            var operation = batch.Operations[index];
            var path = $"$.operations[{index}]";
            if (operation is null)
            {
                Add(path, "required", "An update operation cannot be null.");
                continue;
            }
            if (!Enum.IsDefined(operation.Kind))
            {
                Add($"{path}.kind", "unsupported_operation", "Update operation kind is unsupported.");
                continue;
            }

            switch (operation.Kind)
            {
            case PresentationUpdateOperationKind.SetProperties:
                if (operation.Properties is not { Count: > 0 })
                    Add($"{path}.properties", "required", "SetProperties requires changes.");
                else
                {
                    var names = new HashSet<PresentationProperty>();
                    foreach (var (change, propertyIndex) in operation.Properties.Select((value, i) => (value, i)))
                    {
                        var propertyPath = $"{path}.properties[{propertyIndex}]";
                        if (change is null)
                        {
                            Add(propertyPath, "required", "A property change cannot be null.");
                            continue;
                        }
                        if (!Enum.IsDefined(change.Property))
                        {
                            Add($"{propertyPath}.property", "unsupported_property",
                                "Presentation property is unsupported.");
                            continue;
                        }
                        if (!names.Add(change.Property))
                            Add($"{propertyPath}.property", "duplicate_property",
                                "A property may appear only once per operation.");
                        var documentProperty = PresentationPropertyMetadata.IsDocument(change.Property);
                        if (documentProperty != (operation.TargetId is null))
                            Add(propertyPath, "wrong_property_target",
                                documentProperty
                                    ? "Document properties require a null targetId."
                                    : "Node properties require a targetId.");
                        if (!PresentationUpdateMaterializer.IsValidValue(change))
                            Add($"{propertyPath}.value", "invalid_property_value",
                                "Property value does not match the closed property schema.");
                    }
                }
                if (operation.TargetId is not null) CheckIdentifier(operation.TargetId, $"{path}.targetId");
                Reject(operation.ParentId is not null || operation.ChildId is not null ||
                    operation.Index is not null || operation.Subtree is not null, path);
                break;
            case PresentationUpdateOperationKind.InsertChild:
                CheckIdentifier(operation.ParentId, $"{path}.parentId");
                if (operation.Index is null or < 0)
                    Add($"{path}.index", "invalid_index", "InsertChild requires a non-negative index.");
                ValidateSubtree(operation.Subtree, $"{path}.subtree");
                Reject(operation.TargetId is not null || operation.ChildId is not null ||
                    operation.Properties is not null, path);
                break;
            case PresentationUpdateOperationKind.RemoveChild:
                CheckIdentifier(operation.ParentId, $"{path}.parentId");
                CheckIdentifier(operation.ChildId, $"{path}.childId");
                Reject(operation.TargetId is not null || operation.Index is not null ||
                    operation.Properties is not null || operation.Subtree is not null, path);
                break;
            case PresentationUpdateOperationKind.MoveChild:
                CheckIdentifier(operation.ParentId, $"{path}.parentId");
                CheckIdentifier(operation.ChildId, $"{path}.childId");
                if (operation.Index is null or < 0)
                    Add($"{path}.index", "invalid_index", "MoveChild requires a non-negative index.");
                Reject(operation.TargetId is not null || operation.Properties is not null ||
                    operation.Subtree is not null, path);
                break;
            case PresentationUpdateOperationKind.ReplaceSubtree:
                CheckIdentifier(operation.TargetId, $"{path}.targetId");
                ValidateSubtree(operation.Subtree, $"{path}.subtree");
                Reject(operation.ParentId is not null || operation.ChildId is not null ||
                    operation.Index is not null || operation.Properties is not null, path);
                break;
            }
        }
        return errors;

        void Reject(bool rejected, string path)
        {
            if (rejected) Add(path, "unexpected_operation_field",
                "Operation contains a field that does not apply to its kind.");
        }

        void ValidateSubtree(ViewNode? root, string path)
        {
            if (root is null)
            {
                Add(path, "required", "A subtree is required.");
                return;
            }
            var count = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Visit(root, path, 1);
            void Visit(ViewNode node, string nodePath, int depth)
            {
                count++;
                if (count > ProtocolConstants.MaximumNodeCount)
                    Add(path, "too_many_nodes", "Subtree exceeds the node limit.");
                if (depth > ProtocolConstants.MaximumTreeDepth)
                    Add(nodePath, "too_deep", "Subtree exceeds the depth limit.");
                CheckIdentifier(node.Id, $"{nodePath}.id");
                if (!ids.Add(node.Id))
                    Add($"{nodePath}.id", "duplicate_id", "Subtree IDs must be unique.");
                if (node.Children is null)
                {
                    Add($"{nodePath}.children", "required", "Node children cannot be null.");
                    return;
                }
                foreach (var (child, childIndex) in node.Children.Select((value, i) => (value, i)))
                {
                    if (child is null)
                        Add($"{nodePath}.children[{childIndex}]", "required", "A child cannot be null.");
                    else
                        Visit(child, $"{nodePath}.children[{childIndex}]", depth + 1);
                }
            }
        }

        void CheckIdentifier(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
                !value.All(character => char.IsAsciiLetterOrDigit(character) ||
                    character is '-' or '_' or '.'))
                Add(path, "invalid_identifier", "Identifier is missing or invalid.");
        }

        void Add(string path, string code, string message) => errors.Add(new(path, code, message));
    }
}

public static class PresentationUpdateMaterializer
{
    public static ViewSnapshot Apply(
        ViewSnapshot current,
        PresentationUpdateBatch batch,
        string expectedGeneration)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(batch);
        var errors = PresentationUpdateValidator.Validate(batch);
        if (errors.Count != 0) throw new ProtocolValidationException(errors);
        if (!string.Equals(current.WidgetInstanceId, batch.WidgetInstanceId, StringComparison.Ordinal))
            throw Error("$.widgetInstanceId", "instance_mismatch", "Update belongs to another widget instance.");
        if (!string.Equals(batch.PresentationGeneration, expectedGeneration, StringComparison.Ordinal))
            throw Error("$.presentationGeneration", "generation_mismatch", "Update belongs to another presentation generation.");
        if (current.Sequence != batch.BaseSequence)
            throw Error("$.baseSequence", "base_mismatch", "Update base does not match the current presentation.");

        var candidate = current;
        DemandStructuralBounds(candidate.Root);
        foreach (var operation in batch.Operations)
        {
            candidate = ApplyOperation(candidate, operation);
            // ApplyOperation searches only the previously bounded candidate.
            // Bound the result iteratively before any later recursive search.
            DemandStructuralBounds(candidate.Root);
        }
        candidate = candidate with
        {
            ProtocolVersion = Math.Max(candidate.ProtocolVersion,
                ProtocolConstants.AtomicPresentationUpdateVersion),
            Sequence = batch.Sequence,
        };
        var candidateErrors = ViewSnapshotValidator.Validate(candidate);
        if (candidateErrors.Count != 0) throw new ProtocolValidationException(candidateErrors);
        return candidate;
    }

    private static void DemandStructuralBounds(ViewNode root)
    {
        var pending = new Stack<(ViewNode Node, int Depth)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        pending.Push((root, 1));
        var count = 0;
        while (pending.Count != 0)
        {
            var (node, depth) = pending.Pop();
            count++;
            if (count > ProtocolConstants.MaximumNodeCount)
                throw Error("$.operations", "too_many_nodes",
                    "An intermediate presentation exceeds the node limit.");
            if (depth > ProtocolConstants.MaximumTreeDepth)
                throw Error("$.operations", "too_deep",
                    "An intermediate presentation exceeds the depth limit.");
            if (!ids.Add(node.Id))
                throw Error("$.operations", "duplicate_id",
                    "An intermediate presentation contains a duplicate node ID.");
            if (node.Children is null)
                throw Error("$.operations", "required",
                    "An intermediate presentation has null children.");
            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                var child = node.Children[index] ?? throw Error(
                    "$.operations", "required",
                    "An intermediate presentation contains a null child.");
                pending.Push((child, depth + 1));
            }
        }
    }

    internal static bool IsValidValue(PresentationPropertyChange change)
    {
        try
        {
            object? parsed = change.Property switch
            {
                PresentationProperty.ActiveInputScopeId => ReadRequired<string>(change.Value),
                PresentationProperty.InitialFocusId => Read<string?>(change.Value),
                PresentationProperty.QuickActions => ReadRequired<IReadOnlyList<WidgetQuickAction>>(change.Value),
                PresentationProperty.Surface => Read<WidgetSurfaceHints?>(change.Value),
                PresentationProperty.AdvancedPresentation => Read<WidgetAdvancedPresentationView?>(change.Value),
                PresentationProperty.VisibleWhen => Read<ResponsiveVisibility?>(change.Value),
                PresentationProperty.Text or PresentationProperty.AccessibilityLabel or
                PresentationProperty.AccessibilityValue or PresentationProperty.ActionId or
                PresentationProperty.TextEntryValue or PresentationProperty.TextEntryPlaceholder or
                PresentationProperty.ValueChangedActionId or PresentationProperty.ImageSource or
                PresentationProperty.ArtworkHandle or PresentationProperty.FocusPersistenceId or
                PresentationProperty.InputScopeId or PresentationProperty.ScrollNearStartActionId or
                PresentationProperty.ScrollNearEndActionId or PresentationProperty.CollectionAnchorKey or
                PresentationProperty.CollectionItemKey => Read<string?>(change.Value),
                PresentationProperty.TextEntryMaximumLength or PresentationProperty.GridMaximumColumns or
                PresentationProperty.ScrollPaginationThreshold => Read<int?>(change.Value),
                PresentationProperty.VirtualCollectionWindow =>
                    Read<VirtualCollectionWindow?>(change.Value),
                PresentationProperty.Value or PresentationProperty.Minimum or PresentationProperty.Maximum or
                PresentationProperty.Step or PresentationProperty.GridMinimumColumnWidth => Read<double?>(change.Value),
                PresentationProperty.SliderInteractionMode => Read<SliderInteractionMode?>(change.Value),
                PresentationProperty.ImageFit => Read<ImageFit?>(change.Value),
                PresentationProperty.Glyph => Read<WidgetGlyph?>(change.Value),
                PresentationProperty.IndicatorSize => Read<LoadingIndicatorSize?>(change.Value),
                PresentationProperty.ActionSurfaceOrientation => Read<ActionSurfaceOrientation?>(change.Value),
                PresentationProperty.IsDisabled or PresentationProperty.IsSelected or
                PresentationProperty.IsBusy => Read<bool?>(change.Value),
                PresentationProperty.Focus => Read<FocusNeighbors?>(change.Value),
                PresentationProperty.ScrollAxis => Read<ScrollAxis?>(change.Value),
                PresentationProperty.AdvancedPresentationSlot => Read<WidgetAdvancedPresentationSlot?>(change.Value),
                PresentationProperty.StyleClasses => ReadRequired<IReadOnlyList<string>>(change.Value),
                PresentationProperty.Shortcuts => ReadRequired<IReadOnlyList<ControllerShortcut>>(change.Value),
                _ => throw new JsonException(),
            };
            _ = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static ViewSnapshot ApplyOperation(
        ViewSnapshot snapshot,
        PresentationUpdateOperation operation)
    {
        return operation.Kind switch
        {
            PresentationUpdateOperationKind.SetProperties =>
                ApplyProperties(snapshot, operation.TargetId, operation.Properties!),
            PresentationUpdateOperationKind.InsertChild => snapshot with
            {
                Root = Transform(snapshot.Root, operation.ParentId!, parent =>
                {
                    if (operation.Index > parent.Children.Count)
                        throw Error("$.operations", "invalid_index", "Insert index exceeds child count.");
                    var children = parent.Children.ToList();
                    children.Insert(operation.Index!.Value, operation.Subtree!);
                    return parent with { Children = children };
                }),
            },
            PresentationUpdateOperationKind.RemoveChild => snapshot with
            {
                Root = Transform(snapshot.Root, operation.ParentId!, parent =>
                {
                    var children = parent.Children.ToList();
                    var index = children.FindIndex(child =>
                        string.Equals(child.Id, operation.ChildId, StringComparison.Ordinal));
                    if (index < 0) throw Error("$.operations", "missing_child", "Remove target is not a direct child.");
                    children.RemoveAt(index);
                    return parent with { Children = children };
                }),
            },
            PresentationUpdateOperationKind.MoveChild => snapshot with
            {
                Root = Transform(snapshot.Root, operation.ParentId!, parent =>
                {
                    var children = parent.Children.ToList();
                    var oldIndex = children.FindIndex(child =>
                        string.Equals(child.Id, operation.ChildId, StringComparison.Ordinal));
                    if (oldIndex < 0) throw Error("$.operations", "missing_child", "Move target is not a direct child.");
                    var child = children[oldIndex];
                    children.RemoveAt(oldIndex);
                    if (operation.Index > children.Count)
                        throw Error("$.operations", "invalid_index", "Move index exceeds child count.");
                    children.Insert(operation.Index!.Value, child);
                    return parent with { Children = children };
                }),
            },
            PresentationUpdateOperationKind.ReplaceSubtree => snapshot with
            {
                Root = Transform(snapshot.Root, operation.TargetId!, _ => operation.Subtree!),
            },
            _ => throw Error("$.operations", "unsupported_operation", "Update operation is unsupported."),
        };
    }

    private static ViewSnapshot ApplyProperties(
        ViewSnapshot snapshot,
        string? targetId,
        IReadOnlyList<PresentationPropertyChange> changes)
    {
        if (targetId is null)
        {
            foreach (var change in changes)
                snapshot = change.Property switch
                {
                    PresentationProperty.ActiveInputScopeId => snapshot with { ActiveInputScopeId = ReadRequired<string>(change.Value) },
                    PresentationProperty.InitialFocusId => snapshot with { InitialFocusId = Read<string?>(change.Value) },
                    PresentationProperty.QuickActions => snapshot with { QuickActions = ReadRequired<IReadOnlyList<WidgetQuickAction>>(change.Value) },
                    PresentationProperty.Surface => snapshot with { Surface = Read<WidgetSurfaceHints?>(change.Value) },
                    PresentationProperty.AdvancedPresentation => snapshot with { AdvancedPresentation = Read<WidgetAdvancedPresentationView?>(change.Value) },
                    _ => throw Error("$.operations", "wrong_property_target", "Node property cannot target the document."),
                };
            return snapshot;
        }
        return snapshot with
        {
            Root = Transform(snapshot.Root, targetId, node => ApplyNodeProperties(node, changes)),
        };
    }

    private static ViewNode ApplyNodeProperties(
        ViewNode node,
        IReadOnlyList<PresentationPropertyChange> changes)
    {
        foreach (var change in changes)
            node = change.Property switch
            {
                PresentationProperty.VisibleWhen => node with { VisibleWhen = Read<ResponsiveVisibility?>(change.Value) },
                PresentationProperty.Text => node with { Text = Read<string?>(change.Value) },
                PresentationProperty.AccessibilityLabel => node with { AccessibilityLabel = Read<string?>(change.Value) },
                PresentationProperty.AccessibilityValue => node with { AccessibilityValue = Read<string?>(change.Value) },
                PresentationProperty.ActionId => node with { ActionId = Read<string?>(change.Value) },
                PresentationProperty.TextEntryValue => node with { TextEntryValue = Read<string?>(change.Value) },
                PresentationProperty.TextEntryPlaceholder => node with { TextEntryPlaceholder = Read<string?>(change.Value) },
                PresentationProperty.TextEntryMaximumLength => node with { TextEntryMaximumLength = Read<int?>(change.Value) },
                PresentationProperty.Value => node with { Value = Read<double?>(change.Value) },
                PresentationProperty.Minimum => node with { Minimum = Read<double?>(change.Value) },
                PresentationProperty.Maximum => node with { Maximum = Read<double?>(change.Value) },
                PresentationProperty.Step => node with { Step = Read<double?>(change.Value) },
                PresentationProperty.ValueChangedActionId => node with { ValueChangedActionId = Read<string?>(change.Value) },
                PresentationProperty.SliderInteractionMode => node with { SliderInteractionMode = Read<SliderInteractionMode?>(change.Value) },
                PresentationProperty.ImageSource => node with { ImageSource = Read<string?>(change.Value) },
                PresentationProperty.ArtworkHandle => node with { ArtworkHandle = Read<string?>(change.Value) },
                PresentationProperty.ImageFit => node with { ImageFit = Read<ImageFit?>(change.Value) },
                PresentationProperty.Glyph => node with { Glyph = Read<WidgetGlyph?>(change.Value) },
                PresentationProperty.IndicatorSize => node with { IndicatorSize = Read<LoadingIndicatorSize?>(change.Value) },
                PresentationProperty.ActionSurfaceOrientation => node with { ActionSurfaceOrientation = Read<ActionSurfaceOrientation?>(change.Value) },
                PresentationProperty.GridMinimumColumnWidth => node with { GridMinimumColumnWidth = Read<double?>(change.Value) },
                PresentationProperty.GridMaximumColumns => node with { GridMaximumColumns = Read<int?>(change.Value) },
                PresentationProperty.IsDisabled => node with { IsDisabled = Read<bool?>(change.Value) },
                PresentationProperty.IsSelected => node with { IsSelected = Read<bool?>(change.Value) },
                PresentationProperty.IsBusy => node with { IsBusy = Read<bool?>(change.Value) },
                PresentationProperty.FocusPersistenceId => node with { FocusPersistenceId = Read<string?>(change.Value) },
                PresentationProperty.Focus => node with { Focus = Read<FocusNeighbors?>(change.Value) },
                PresentationProperty.InputScopeId => node with { InputScopeId = Read<string?>(change.Value) },
                PresentationProperty.ScrollAxis => node with { ScrollAxis = Read<ScrollAxis?>(change.Value) },
                PresentationProperty.ScrollNearStartActionId => node with { ScrollNearStartActionId = Read<string?>(change.Value) },
                PresentationProperty.ScrollNearEndActionId => node with { ScrollNearEndActionId = Read<string?>(change.Value) },
                PresentationProperty.ScrollPaginationThreshold => node with { ScrollPaginationThreshold = Read<int?>(change.Value) },
                PresentationProperty.VirtualCollectionWindow => node with { VirtualCollectionWindow = Read<VirtualCollectionWindow?>(change.Value) },
                PresentationProperty.CollectionAnchorKey => node with { CollectionAnchorKey = Read<string?>(change.Value) },
                PresentationProperty.CollectionItemKey => node with { CollectionItemKey = Read<string?>(change.Value) },
                PresentationProperty.AdvancedPresentationSlot => node with { AdvancedPresentationSlot = Read<WidgetAdvancedPresentationSlot?>(change.Value) },
                PresentationProperty.StyleClasses => node with { StyleClasses = ReadRequired<IReadOnlyList<string>>(change.Value) },
                PresentationProperty.Shortcuts => node with { Shortcuts = ReadRequired<IReadOnlyList<ControllerShortcut>>(change.Value) },
                _ => throw Error("$.operations", "wrong_property_target", "Document property cannot target a node."),
            };
        return node;
    }

    private static ViewNode Transform(ViewNode node, string targetId, Func<ViewNode, ViewNode> transform)
    {
        if (string.Equals(node.Id, targetId, StringComparison.Ordinal)) return transform(node);
        for (var index = 0; index < node.Children.Count; index++)
        {
            if (!Contains(node.Children[index], targetId)) continue;
            var children = node.Children.ToArray();
            children[index] = Transform(children[index], targetId, transform);
            return node with { Children = children };
        }
        throw Error("$.operations", "missing_target", $"Update target '{targetId}' was not found.");
    }

    private static bool Contains(ViewNode node, string targetId) =>
        string.Equals(node.Id, targetId, StringComparison.Ordinal) ||
        node.Children.Any(child => Contains(child, targetId));

    private static T? Read<T>(JsonElement value) =>
        PresentationUpdateJson.Read<T>(value);

    private static T ReadRequired<T>(JsonElement value) where T : class =>
        Read<T>(value) ?? throw new JsonException("A required property value was null.");

    private static ProtocolValidationException Error(string path, string code, string message) =>
        new([new(path, code, message)]);
}
