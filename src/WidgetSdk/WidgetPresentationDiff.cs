using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

internal sealed record WidgetPresentationPublication(
    ViewSnapshot Snapshot,
    PresentationUpdateBatch? Update,
    string? FallbackReason)
{
    internal bool IsUpdate => Update is not null;
}

internal static class WidgetPresentationDiff
{
    internal static WidgetPresentationPublication Create(
        ViewSnapshot? previous,
        ViewSnapshot current,
        string presentationGeneration,
        long expectedBaseSequence,
        PresentationUpdateCapabilities capabilities,
        bool requireCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(capabilities);
        current = NormalizeVirtualWindowReentry(previous, current);
        if (requireCheckpoint) return Checkpoint("checkpoint_requested");
        if (!capabilities.SupportsAtomicUpdates) return Checkpoint("capability_unavailable");
        if (previous is null) return Checkpoint("missing_base");
        if (!string.Equals(previous.WidgetInstanceId, current.WidgetInstanceId, StringComparison.Ordinal))
            return Checkpoint("instance_changed");
        if (previous.Sequence != expectedBaseSequence) return Checkpoint("base_mismatch");
        if (current.Sequence <= previous.Sequence) return Checkpoint("sequence_not_advanced");
        if (!ValidGeneration(presentationGeneration)) return Checkpoint("invalid_generation");
        if (current.ProtocolVersion > previous.ProtocolVersion)
            return Checkpoint("snapshot_protocol_advanced");

        current = current with
        {
            ProtocolVersion = Math.Max(
                current.ProtocolVersion, ProtocolConstants.AtomicPresentationUpdateVersion),
        };
        if (!StableRelationships(previous.Root, current.Root))
            return Checkpoint("unstable_identity");

        var operations = new List<PresentationUpdateOperation>();
        AddDocumentChanges(previous, current, operations);
        if (!string.Equals(previous.Root.Id, current.Root.Id, StringComparison.Ordinal))
            return Checkpoint("unstable_root");
        DiffNode(previous.Root, current.Root, operations);

        var operationLimit = Math.Min(
            capabilities.MaximumOperationsPerBatch,
            ProtocolConstants.MaximumPresentationUpdateOperations);
        if (operations.Count > operationLimit) return Checkpoint("operation_limit");
        var batch = new PresentationUpdateBatch
        {
            WidgetInstanceId = current.WidgetInstanceId,
            PresentationGeneration = presentationGeneration,
            BaseSequence = previous.Sequence,
            Sequence = current.Sequence,
            Operations = operations,
        };
        byte[] updateBytes;
        try
        {
            updateBytes = PresentationUpdateJson.Serialize(batch);
        }
        catch (ProtocolValidationException)
        {
            return Checkpoint("unsafe_diff");
        }
        var byteLimit = Math.Min(
            capabilities.MaximumBatchBytes,
            ProtocolConstants.MaximumPresentationUpdateBytes);
        var checkpointBytes = SnapshotJson.Serialize(current);
        if (updateBytes.Length > byteLimit || updateBytes.Length >= checkpointBytes.Length)
            return Checkpoint("checkpoint_smaller");
        return new(current, batch, null);

        WidgetPresentationPublication Checkpoint(string reason) =>
            new(current, null, reason);
    }

    internal static PresentationUpdateBatch? TryCoalesce(
        PresentationUpdateBatch first,
        PresentationUpdateBatch second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (!string.Equals(first.WidgetInstanceId, second.WidgetInstanceId, StringComparison.Ordinal) ||
            !string.Equals(first.PresentationGeneration, second.PresentationGeneration, StringComparison.Ordinal) ||
            first.Sequence != second.BaseSequence ||
            first.Operations.Any(operation => operation.Kind != PresentationUpdateOperationKind.SetProperties) ||
            second.Operations.Any(operation => operation.Kind != PresentationUpdateOperationKind.SetProperties))
            return null;

        var ordered = new List<(string? Target, PresentationPropertyChange Change)>();
        var index = new Dictionary<(string? Target, PresentationProperty Property), int>();
        foreach (var operation in first.Operations.Concat(second.Operations))
        {
            foreach (var change in operation.Properties!)
            {
                var key = (operation.TargetId, change.Property);
                if (index.TryGetValue(key, out var existing))
                    ordered[existing] = (operation.TargetId, change);
                else
                {
                    index.Add(key, ordered.Count);
                    ordered.Add((operation.TargetId, change));
                }
            }
        }
        var operations = ordered
            .GroupBy(item => item.Target, StringComparer.Ordinal)
            .Select(group => new PresentationUpdateOperation
            {
                Kind = PresentationUpdateOperationKind.SetProperties,
                TargetId = group.Key,
                Properties = group.Select(item => item.Change).ToArray(),
            })
            .ToArray();
        var result = first with { Sequence = second.Sequence, Operations = operations };
        try
        {
            _ = PresentationUpdateJson.Serialize(result);
            return result;
        }
        catch (ProtocolValidationException)
        {
            return null;
        }
    }

    private static bool ValidGeneration(string value) =>
        value is { Length: 32 or 64 } && value.All(char.IsAsciiHexDigit);

    private static ViewSnapshot NormalizeVirtualWindowReentry(
        ViewSnapshot? previous,
        ViewSnapshot current)
    {
        var previousIds = new HashSet<string>(StringComparer.Ordinal);
        if (previous is not null) AddIds(previous.Root);
        var root = Normalize(current.Root);
        return ReferenceEquals(root, current.Root) ? current : current with { Root = root };

        void AddIds(ViewNode node)
        {
            previousIds.Add(node.Id);
            foreach (var child in node.Children) AddIds(child);
        }

        ViewNode Normalize(ViewNode node)
        {
            ViewNode[]? children = null;
            for (var index = 0; index < node.Children.Count; index++)
            {
                var child = Normalize(node.Children[index]);
                if (children is null && !ReferenceEquals(child, node.Children[index]))
                    children = node.Children.ToArray();
                if (children is not null) children[index] = child;
            }

            var window = node.VirtualCollectionWindow;
            var normalizedWindow = window is { Change: not VirtualCollectionWindowChange.Replace } &&
                !previousIds.Contains(node.Id)
                ? window with { Change = VirtualCollectionWindowChange.Replace }
                : window;
            return children is null && ReferenceEquals(window, normalizedWindow)
                ? node
                : node with
                {
                    VirtualCollectionWindow = normalizedWindow,
                    Children = children ?? node.Children,
                };
        }
    }

    private static bool StableRelationships(ViewNode previous, ViewNode current)
    {
        var previousParents = Parents(previous);
        var currentParents = Parents(current);
        return previousParents.Keys.Intersect(currentParents.Keys, StringComparer.Ordinal)
            .All(id => string.Equals(previousParents[id], currentParents[id], StringComparison.Ordinal));

        static Dictionary<string, string?> Parents(ViewNode root)
        {
            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            Visit(root, null);
            return result;
            void Visit(ViewNode node, string? parent)
            {
                result[node.Id] = parent;
                foreach (var child in node.Children) Visit(child, node.Id);
            }
        }
    }

    private static void AddDocumentChanges(
        ViewSnapshot previous,
        ViewSnapshot current,
        List<PresentationUpdateOperation> operations)
    {
        var changes = new List<PresentationPropertyChange>();
        Add(PresentationProperty.ActiveInputScopeId, previous.ActiveInputScopeId, current.ActiveInputScopeId);
        Add(PresentationProperty.InitialFocusId, previous.InitialFocusId, current.InitialFocusId);
        Add(PresentationProperty.QuickActions, previous.QuickActions, current.QuickActions);
        Add(PresentationProperty.Surface, previous.Surface, current.Surface);
        Add(PresentationProperty.AdvancedPresentation, previous.AdvancedPresentation, current.AdvancedPresentation);
        if (changes.Count != 0)
            operations.Add(new()
            {
                Kind = PresentationUpdateOperationKind.SetProperties,
                Properties = changes,
            });

        void Add<T>(PresentationProperty property, T before, T after)
        {
            var beforeJson = PresentationUpdateJson.Value(before);
            var afterJson = PresentationUpdateJson.Value(after);
            if (!string.Equals(
                    beforeJson.GetRawText(), afterJson.GetRawText(), StringComparison.Ordinal))
                changes.Add(new(property, afterJson));
        }
    }

    private static void DiffNode(
        ViewNode previous,
        ViewNode current,
        List<PresentationUpdateOperation> operations)
    {
        if (previous.Kind != current.Kind)
        {
            operations.Add(new()
            {
                Kind = PresentationUpdateOperationKind.ReplaceSubtree,
                TargetId = previous.Id,
                Subtree = current,
            });
            return;
        }

        var changes = NodeChanges(previous, current);
        if (changes.Count != 0)
            operations.Add(new()
            {
                Kind = PresentationUpdateOperationKind.SetProperties,
                TargetId = current.Id,
                Properties = changes,
            });

        var previousById = previous.Children.ToDictionary(child => child.Id, StringComparer.Ordinal);
        var currentIds = current.Children.Select(child => child.Id).ToHashSet(StringComparer.Ordinal);
        var working = previous.Children.Select(child => child.Id).ToList();
        for (var index = working.Count - 1; index >= 0; index--)
        {
            if (currentIds.Contains(working[index])) continue;
            operations.Add(new()
            {
                Kind = PresentationUpdateOperationKind.RemoveChild,
                ParentId = current.Id,
                ChildId = working[index],
            });
            working.RemoveAt(index);
        }
        for (var targetIndex = 0; targetIndex < current.Children.Count; targetIndex++)
        {
            var desired = current.Children[targetIndex];
            var currentIndex = working.IndexOf(desired.Id);
            if (currentIndex < 0)
            {
                operations.Add(new()
                {
                    Kind = PresentationUpdateOperationKind.InsertChild,
                    ParentId = current.Id,
                    Index = targetIndex,
                    Subtree = desired,
                });
                working.Insert(targetIndex, desired.Id);
            }
            else if (currentIndex != targetIndex)
            {
                operations.Add(new()
                {
                    Kind = PresentationUpdateOperationKind.MoveChild,
                    ParentId = current.Id,
                    ChildId = desired.Id,
                    Index = targetIndex,
                });
                working.RemoveAt(currentIndex);
                working.Insert(targetIndex, desired.Id);
            }
            if (previousById.TryGetValue(desired.Id, out var previousChild))
                DiffNode(previousChild, desired, operations);
        }
    }

    private static IReadOnlyList<PresentationPropertyChange> NodeChanges(
        ViewNode before,
        ViewNode after)
    {
        var changes = new List<PresentationPropertyChange>();
        Add(PresentationProperty.VisibleWhen, before.VisibleWhen, after.VisibleWhen);
        Add(PresentationProperty.Text, before.Text, after.Text);
        Add(PresentationProperty.AccessibilityLabel, before.AccessibilityLabel, after.AccessibilityLabel);
        Add(PresentationProperty.AccessibilityValue, before.AccessibilityValue, after.AccessibilityValue);
        Add(PresentationProperty.ActionId, before.ActionId, after.ActionId);
        Add(PresentationProperty.TextEntryValue, before.TextEntryValue, after.TextEntryValue);
        Add(PresentationProperty.TextEntryPlaceholder, before.TextEntryPlaceholder, after.TextEntryPlaceholder);
        Add(PresentationProperty.TextEntryMaximumLength, before.TextEntryMaximumLength, after.TextEntryMaximumLength);
        Add(PresentationProperty.Value, before.Value, after.Value);
        Add(PresentationProperty.Minimum, before.Minimum, after.Minimum);
        Add(PresentationProperty.Maximum, before.Maximum, after.Maximum);
        Add(PresentationProperty.Step, before.Step, after.Step);
        Add(PresentationProperty.ValueChangedActionId, before.ValueChangedActionId, after.ValueChangedActionId);
        Add(PresentationProperty.SliderInteractionMode, before.SliderInteractionMode, after.SliderInteractionMode);
        Add(PresentationProperty.ImageSource, before.ImageSource, after.ImageSource);
        Add(PresentationProperty.ArtworkHandle, before.ArtworkHandle, after.ArtworkHandle);
        Add(PresentationProperty.ImageFit, before.ImageFit, after.ImageFit);
        Add(PresentationProperty.Glyph, before.Glyph, after.Glyph);
        Add(PresentationProperty.IndicatorSize, before.IndicatorSize, after.IndicatorSize);
        Add(PresentationProperty.ActionSurfaceOrientation, before.ActionSurfaceOrientation, after.ActionSurfaceOrientation);
        Add(PresentationProperty.GridMinimumColumnWidth, before.GridMinimumColumnWidth, after.GridMinimumColumnWidth);
        Add(PresentationProperty.GridMaximumColumns, before.GridMaximumColumns, after.GridMaximumColumns);
        Add(PresentationProperty.IsDisabled, before.IsDisabled, after.IsDisabled);
        Add(PresentationProperty.IsSelected, before.IsSelected, after.IsSelected);
        Add(PresentationProperty.IsBusy, before.IsBusy, after.IsBusy);
        Add(PresentationProperty.FocusPersistenceId, before.FocusPersistenceId, after.FocusPersistenceId);
        Add(PresentationProperty.Focus, before.Focus, after.Focus);
        Add(PresentationProperty.InputScopeId, before.InputScopeId, after.InputScopeId);
        Add(PresentationProperty.ScrollAxis, before.ScrollAxis, after.ScrollAxis);
        Add(PresentationProperty.ScrollNearStartActionId, before.ScrollNearStartActionId, after.ScrollNearStartActionId);
        Add(PresentationProperty.ScrollNearEndActionId, before.ScrollNearEndActionId, after.ScrollNearEndActionId);
        Add(PresentationProperty.ScrollPaginationThreshold, before.ScrollPaginationThreshold, after.ScrollPaginationThreshold);
        Add(PresentationProperty.VirtualCollectionWindow, before.VirtualCollectionWindow, after.VirtualCollectionWindow);
        Add(PresentationProperty.CollectionAnchorKey, before.CollectionAnchorKey, after.CollectionAnchorKey);
        Add(PresentationProperty.CollectionItemKey, before.CollectionItemKey, after.CollectionItemKey);
        Add(PresentationProperty.AdvancedPresentationSlot, before.AdvancedPresentationSlot, after.AdvancedPresentationSlot);
        Add(PresentationProperty.StyleClasses, before.StyleClasses, after.StyleClasses);
        Add(PresentationProperty.Shortcuts, before.Shortcuts, after.Shortcuts);
        return changes;

        void Add<T>(PresentationProperty property, T previous, T current)
        {
            var previousJson = PresentationUpdateJson.Value(previous);
            var currentJson = PresentationUpdateJson.Value(current);
            if (!string.Equals(
                    previousJson.GetRawText(), currentJson.GetRawText(), StringComparison.Ordinal))
                changes.Add(new(property, currentJson));
        }
    }
}
