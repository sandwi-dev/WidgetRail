using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

internal static class WidgetPresentationUpdateTests
{
    private const string Generation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    internal static Task Run()
    {
        PropertyNoOpAndRoundTrip();
        KeyedStructureAndSubtreeReplacement();
        FallbacksAreDeterministic();
        MalformedAndOversizedFailClosed();
        PropertyCoalescingIsBounded();
        DifferIsTrustTierNeutral();
        return Task.CompletedTask;
    }

    private static void PropertyNoOpAndRoundTrip()
    {
        var previous = Snapshot(1, "old");
        var identical = Snapshot(2, "old");
        var noOp = WidgetPresentationDiff.Create(
            previous, identical, Generation, 1,
            PresentationUpdateCapabilities.Current, requireCheckpoint: false);
        True(noOp.IsUpdate, "An identical semantic model should advance through an empty update.");
        Equal(0, noOp.Update!.Operations.Count);
        Equal(2L, PresentationUpdateMaterializer.Apply(previous, noOp.Update, Generation).Sequence);

        var changed = Snapshot(3, "new");
        var publication = WidgetPresentationDiff.Create(
            identical with { ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion },
            changed, Generation, 2,
            PresentationUpdateCapabilities.Current, requireCheckpoint: false);
        True(publication.IsUpdate, "A bounded text change should use SetProperties.");
        var operation = publication.Update!.Operations.Single();
        Equal(PresentationUpdateOperationKind.SetProperties, operation.Kind);
        Equal("text.0", operation.TargetId);
        Equal(PresentationProperty.Text, operation.Properties!.Single().Property);
        var bytes = PresentationUpdateJson.Serialize(publication.Update);
        var roundTrip = PresentationUpdateJson.Deserialize(bytes);
        var materialized = PresentationUpdateMaterializer.Apply(
            identical with { ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion },
            roundTrip, Generation);
        Equal("new", Find(materialized.Root, "text.0").Text);
        True((PresentationPropertyMetadata.Impact(PresentationProperty.Text) &
            PresentationPropertyImpact.MeasureLayout) != 0,
            "Text must never be classified as paint-only.");
        Throws<ProtocolValidationException>(() =>
            PresentationUpdateMaterializer.Apply(
                identical, roundTrip, new string('b', 32)));
    }

    private static void KeyedStructureAndSubtreeReplacement()
    {
        var stableTail = Enumerable.Range(0, 24)
            .Select(index => Text($"stable.{index}", $"unchanged-{index}"))
            .ToArray();
        var previous = SnapshotWithChildren(10,
            [Text("alpha", "A"), Text("bravo", "B"), Text("charlie", "C"),
                .. stableTail]);
        var current = SnapshotWithChildren(11,
            [Text("bravo", "B2"), Text("delta", "D"), Text("alpha", "A"),
                .. stableTail]);
        var publication = WidgetPresentationDiff.Create(
            previous, current, Generation, 10,
            PresentationUpdateCapabilities.Current, requireCheckpoint: false);
        True(publication.IsUpdate, "Stable keyed children should use structural operations.");
        True(publication.Update!.Operations.Any(operation =>
            operation.Kind == PresentationUpdateOperationKind.RemoveChild &&
            operation.ChildId == "charlie"), "Missing child should be removed by stable ID.");
        True(publication.Update.Operations.Any(operation =>
            operation.Kind == PresentationUpdateOperationKind.InsertChild &&
            operation.Subtree?.Id == "delta"), "New child should be inserted as a bounded subtree.");
        True(publication.Update.Operations.Any(operation =>
            operation.Kind == PresentationUpdateOperationKind.MoveChild &&
            operation.ChildId == "bravo"), "Existing child order should use MoveChild.");
        var materialized = PresentationUpdateMaterializer.Apply(previous, publication.Update, Generation);
        Equal("bravo,delta,alpha", string.Join(',',
            materialized.Root.Children.Take(3).Select(child => child.Id)));
        Equal("B2", materialized.Root.Children[0].Text);

        var replacement = current with
        {
            Sequence = 12,
            Root = current.Root with
            {
                Children = current.Root.Children.Select(child => child.Id == "delta"
                    ? child with { Kind = ViewNodeKind.Spacer, Text = null }
                    : child).ToArray(),
            },
        };
        var replaced = WidgetPresentationDiff.Create(
            current with { ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion },
            replacement, Generation, 11,
            PresentationUpdateCapabilities.Current, requireCheckpoint: false);
        True(replaced.Update?.Operations.Any(operation =>
            operation.Kind == PresentationUpdateOperationKind.ReplaceSubtree &&
            operation.TargetId == "delta") == true,
            "A local kind change should use ReplaceSubtree.");
    }

    private static void FallbacksAreDeterministic()
    {
        var previous = Snapshot(20, "old");
        var current = Snapshot(21, "new");
        Equal("capability_unavailable", WidgetPresentationDiff.Create(
            previous, current, Generation, 20,
            PresentationUpdateCapabilities.None, false).FallbackReason);
        Equal("base_mismatch", WidgetPresentationDiff.Create(
            previous, current, Generation, 19,
            PresentationUpdateCapabilities.Current, false).FallbackReason);
        Equal("checkpoint_requested", WidgetPresentationDiff.Create(
            previous, current, Generation, 20,
            PresentationUpdateCapabilities.Current, true).FallbackReason);
        var tiny = PresentationUpdateCapabilities.Current with { MaximumBatchBytes = 1 };
        Equal("checkpoint_smaller", WidgetPresentationDiff.Create(
            previous, current, Generation, 20, tiny, false).FallbackReason);

        var moved = SnapshotWithChildren(22,
            new ViewNode
            {
                Id = "new-parent",
                Kind = ViewNodeKind.Stack,
                Children = [Text("text.0", "old")],
            });
        Equal("unstable_identity", WidgetPresentationDiff.Create(
            previous, moved, Generation, 20,
            PresentationUpdateCapabilities.Current, false).FallbackReason);
    }

    private static void MalformedAndOversizedFailClosed()
    {
        var valid = new PresentationUpdateBatch
        {
            WidgetInstanceId = "update.test",
            PresentationGeneration = Generation,
            BaseSequence = 1,
            Sequence = 2,
            Operations = [],
        };
        var unknown = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(PresentationUpdateJson.Serialize(valid))
                .Replace("\"operations\":[]", "\"operations\":[],\"unknown\":true", StringComparison.Ordinal));
        Throws<JsonException>(() => PresentationUpdateJson.Deserialize(unknown));

        var duplicate = valid with
        {
            Operations =
            [
                new()
                {
                    Kind = PresentationUpdateOperationKind.SetProperties,
                    TargetId = "text.0",
                    Properties =
                    [
                        new(PresentationProperty.Text, JsonSerializer.SerializeToElement("a")),
                        new(PresentationProperty.Text, JsonSerializer.SerializeToElement("b")),
                    ],
                },
            ],
        };
        True(PresentationUpdateValidator.Validate(duplicate).Any(error =>
            error.Code == "duplicate_property"), "Duplicate property writes must fail complete-batch validation.");

        var tooMany = valid with
        {
            Operations = Enumerable.Range(0,
                ProtocolConstants.MaximumPresentationUpdateOperations + 1)
                .Select(_ => new PresentationUpdateOperation
                {
                    Kind = PresentationUpdateOperationKind.RemoveChild,
                    ParentId = "root",
                    ChildId = "child",
                }).ToArray(),
        };
        True(PresentationUpdateValidator.Validate(tooMany).Any(error =>
            error.Code == "too_many_operations"), "Operation count must be bounded before materialization.");

        var malformedGeneration = valid with { PresentationGeneration = "abc" };
        True(PresentationUpdateValidator.Validate(malformedGeneration).Any(error =>
            error.Code == "invalid_generation"), "Generation evidence must be exact and bounded.");

        var oversized = new byte[ProtocolConstants.MaximumPresentationUpdateBytes + 1];
        Throws<ProtocolValidationException>(() => PresentationUpdateJson.Deserialize(oversized));
    }

    private static void PropertyCoalescingIsBounded()
    {
        var first = Batch(1, 2, "one");
        var second = Batch(2, 3, "two");
        var coalesced = WidgetPresentationDiff.TryCoalesce(first, second)
            ?? throw new InvalidOperationException("Compatible property batches did not coalesce.");
        Equal(1L, coalesced.BaseSequence);
        Equal(3L, coalesced.Sequence);
        Equal("two", coalesced.Operations.Single().Properties!.Single().Value.GetString());
        var structural = second with
        {
            Operations =
            [
                new()
                {
                    Kind = PresentationUpdateOperationKind.RemoveChild,
                    ParentId = "root",
                    ChildId = "text.0",
                },
            ],
        };
        True(WidgetPresentationDiff.TryCoalesce(first, structural) is null,
            "Structural transitions must never be dropped by coalescing.");
        Equal(4, ProtocolConstants.MaximumPendingPresentationUpdates);
        Equal(512 * 1024, ProtocolConstants.MaximumPendingPresentationUpdateBytes);
    }

    private static void DifferIsTrustTierNeutral()
    {
        var alpha = WidgetPresentationDiff.Create(
            Snapshot(1, "old", "sandbox.alpha"), Snapshot(2, "new", "sandbox.alpha"),
            Generation, 1, PresentationUpdateCapabilities.Current, false);
        var beta = WidgetPresentationDiff.Create(
            Snapshot(1, "old", "fulltrust.beta"), Snapshot(2, "new", "fulltrust.beta"),
            Generation, 1, PresentationUpdateCapabilities.Current, false);
        Equal(alpha.Update!.Operations.Select(operation => operation.Kind).ToArray(),
            beta.Update!.Operations.Select(operation => operation.Kind).ToArray());
        Equal(alpha.Update.Operations.Single().Properties!.Single().Property,
            beta.Update.Operations.Single().Properties!.Single().Property);
    }

    private static PresentationUpdateBatch Batch(long before, long after, string text) => new()
    {
        WidgetInstanceId = "update.test",
        PresentationGeneration = Generation,
        BaseSequence = before,
        Sequence = after,
        Operations =
        [
            new()
            {
                Kind = PresentationUpdateOperationKind.SetProperties,
                TargetId = "text.0",
                Properties =
                [
                    new(PresentationProperty.Text,
                        PresentationUpdateJson.Value<string?>(text)),
                ],
            },
        ],
    };

    private static ViewSnapshot Snapshot(
        long sequence,
        string firstText,
        string instance = "update.test") =>
        SnapshotWithChildren(sequence, instance,
            Enumerable.Range(0, 20)
                .Select(index => Text($"text.{index}", index == 0 ? firstText : $"stable-{index}"))
                .ToArray());

    private static ViewSnapshot SnapshotWithChildren(
        long sequence,
        params ViewNode[] children) => SnapshotWithChildren(sequence, "update.test", children);

    private static ViewSnapshot SnapshotWithChildren(
        long sequence,
        string instance,
        params ViewNode[] children) => new()
        {
            ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion,
            Sequence = sequence,
            WidgetInstanceId = instance,
            ActiveInputScopeId = "root",
            Root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack, Children = children },
        };

    private static ViewNode Text(string id, string text) => new()
    {
        Id = id,
        Kind = ViewNodeKind.Text,
        Text = text,
    };

    private static ViewNode Find(ViewNode node, string id) =>
        node.Id == id ? node : node.Children.Select(child => FindOrNull(child, id))
            .First(found => found is not null)!;

    private static ViewNode? FindOrNull(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
            if (FindOrNull(child, id) is { } found) return found;
        return null;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (expected is Array expectedArray && actual is Array actualArray)
        {
            if (expectedArray.Cast<object?>().SequenceEqual(actualArray.Cast<object?>())) return;
        }
        else if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
