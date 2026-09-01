using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetPresentationUpdateTests
{
    private const string Generation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    internal static Task Run()
    {
        PropertyNoOpAndRoundTrip();
        SelectOptionsUpdateAtomically();
        VirtualCollectionWindowUpdatesAtomically();
        PublicationProtocolAndVirtualReentryMatrix();
        TransactionKindsRetainExactAuthority();
        KeyedStructureAndSubtreeReplacement();
        FallbacksAreDeterministic();
        MalformedAndOversizedFailClosed();
        IntermediateStructureBoundsFailClosed();
        PropertyCoalescingIsBounded();
        DifferIsTrustTierNeutral();
        return Task.CompletedTask;
    }

    private static void TransactionKindsRetainExactAuthority()
    {
        var previous = Snapshot(40, "old");
        var current = Snapshot(41, "new");
        var incremental = WidgetPresentationDiff.Create(
            previous, current, Generation, 40,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        Equal(WidgetPresentationTransactionKind.IncrementalUpdate,
            incremental.TransactionKind);
        Equal(40L, incremental.RequestBaseSequence);
        Equal(0L, incremental.RecoveryOriginSequence);

        var ordinary = WidgetPresentationDiff.Create(
            previous, current, Generation, 0,
            PresentationUpdateCapabilities.None,
            WidgetPresentationTransactionKind.OrdinaryCheckpoint);
        Equal(WidgetPresentationTransactionKind.OrdinaryCheckpoint,
            ordinary.TransactionKind);
        Equal(0L, ordinary.RequestBaseSequence);
        Equal(0L, ordinary.RecoveryOriginSequence);

        var recovery = WidgetPresentationDiff.Create(
            previous, current, Generation, 0,
            PresentationUpdateCapabilities.None,
            WidgetPresentationTransactionKind.RecoveryCheckpoint,
            recoveryOriginSequence: 40);
        Equal(WidgetPresentationTransactionKind.RecoveryCheckpoint,
            recovery.TransactionKind);
        Equal(0L, recovery.RequestBaseSequence);
        Equal(40L, recovery.RecoveryOriginSequence);

        Throws<ArgumentException>(() => WidgetPresentationDiff.Create(
            previous, current, Generation, 0,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate));
        Throws<ArgumentException>(() => WidgetPresentationDiff.Create(
            previous, current, Generation, 40,
            PresentationUpdateCapabilities.None,
            WidgetPresentationTransactionKind.RecoveryCheckpoint,
            recoveryOriginSequence: 40));
    }

    private static void VirtualCollectionWindowUpdatesAtomically()
    {
        static ViewNode Item(int index) => new()
        {
            Id = $"virtual.item.{index}",
            Kind = ViewNodeKind.Button,
            Text = $"Item {index}",
            AccessibilityLabel = $"Item {index}",
            ActionId = "virtual.open",
            CollectionItemKey = $"item.{index}",
        };
        static VirtualCollectionWindow Window(long generation, int count) => new()
        {
            RequestGeneration = generation,
            Change = generation == 1
                ? VirtualCollectionWindowChange.Replace
                : VirtualCollectionWindowChange.Append,
            FirstItemIndex = 0,
            TotalItemCount = 10_000,
            HasBefore = false,
            HasAfter = true,
            EstimatedItemExtent = 52,
        };
        static ViewSnapshot VirtualSnapshot(long sequence, long generation, int count) => new()
        {
            ProtocolVersion = ProtocolConstants.VirtualCollectionWindowVersion,
            Sequence = sequence,
            WidgetInstanceId = "virtual.update",
            ActiveInputScopeId = "virtual.scroll",
            InitialFocusId = "virtual.item.0",
            Root = new ViewNode
            {
                Id = "virtual.scroll",
                Kind = ViewNodeKind.Scroll,
                ScrollAxis = ScrollAxis.Vertical,
                ScrollNearEndActionId = "virtual.next",
                ScrollPaginationThreshold = 2,
                CollectionAnchorKey = "item.0",
                VirtualCollectionWindow = Window(generation, count),
                Children = Enumerable.Range(0, count).Select(Item).ToArray(),
            },
        };

        var previous = VirtualSnapshot(1, 1, 32);
        var current = VirtualSnapshot(2, 2, 34);
        var publication = WidgetPresentationDiff.Create(
            previous, current, Generation, 1,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        True(publication.IsUpdate,
            "A bounded virtual append should remain one atomic update candidate.");
        True(publication.Update!.Operations.Any(operation =>
                operation.Properties?.Any(change =>
                    change.Property == PresentationProperty.VirtualCollectionWindow) == true),
            "Virtual window authority was omitted from the atomic update.");
        var admitted = PresentationUpdateMaterializer.Apply(
            previous, publication.Update, Generation);
        Equal(34, admitted.Root.Children.Count);
        Equal(2L, admitted.Root.VirtualCollectionWindow!.RequestGeneration);
        True((PresentationPropertyMetadata.Impact(
                  PresentationProperty.VirtualCollectionWindow) &
              PresentationPropertyImpact.MeasureLayout) != 0,
            "Virtual window changes must invalidate native layout safely.");
    }

    private static void PublicationProtocolAndVirtualReentryMatrix()
    {
        static ViewSnapshot Virtual(
            long sequence,
            long generation,
            VirtualCollectionWindowChange change) => new()
        {
            ProtocolVersion = ProtocolConstants.VirtualCollectionWindowVersion,
            Sequence = sequence,
            WidgetInstanceId = "virtual.reentry",
            ActiveInputScopeId = "virtual.scroll",
            InitialFocusId = "virtual.item.12",
            Root = new ViewNode
            {
                Id = "virtual.scroll",
                Kind = ViewNodeKind.Scroll,
                ScrollAxis = ScrollAxis.Vertical,
                ScrollNearStartActionId = "virtual.previous",
                ScrollNearEndActionId = "virtual.next",
                ScrollPaginationThreshold = 2,
                CollectionAnchorKey = "item.12",
                VirtualCollectionWindow = new()
                {
                    RequestGeneration = generation,
                    Change = change,
                    FirstItemIndex = 12,
                    TotalItemCount = 10_000,
                    HasBefore = true,
                    HasAfter = true,
                    EstimatedItemExtent = 52,
                },
                Children =
                [
                    new ViewNode
                    {
                        Id = "virtual.item.12",
                        Kind = ViewNodeKind.Button,
                        Text = "Item 12",
                        AccessibilityLabel = "Item 12",
                        ActionId = "virtual.open",
                        CollectionItemKey = "item.12",
                    },
                ],
            },
        };

        var legacy = Snapshot(1, "loading", "virtual.reentry");
        var firstVirtual = Virtual(2, 20, VirtualCollectionWindowChange.Append);
        var firstVirtualErrors = ViewSnapshotValidator.Validate(firstVirtual);
        True(firstVirtualErrors.Count == 0, string.Join("; ",
            firstVirtualErrors.Select(error =>
                $"{error.Path} {error.Code}: {error.Message}")));
        var protocolAdvance = WidgetPresentationDiff.Create(
            legacy, firstVirtual, Generation, legacy.Sequence,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        True(!protocolAdvance.IsUpdate,
            "A snapshot-protocol advance must publish a complete checkpoint.");
        Equal("snapshot_protocol_advanced", protocolAdvance.FallbackReason);
        Equal(VirtualCollectionWindowChange.Replace,
            protocolAdvance.Snapshot.Root.VirtualCollectionWindow!.Change);

        var routeWithoutWindow = firstVirtual with
        {
            Sequence = 3,
            ActiveInputScopeId = "other.route",
            InitialFocusId = null,
            Root = new ViewNode { Id = "other.route", Kind = ViewNodeKind.Stack },
        };
        var reintroduced = WidgetPresentationDiff.Create(
            routeWithoutWindow, Virtual(4, 21, VirtualCollectionWindowChange.Append),
            Generation, routeWithoutWindow.Sequence,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        Equal(VirtualCollectionWindowChange.Replace,
            reintroduced.Snapshot.Root.VirtualCollectionWindow!.Change);

        var unchangedGeneration = WidgetPresentationDiff.Create(
            reintroduced.Snapshot,
            Virtual(5, 21, VirtualCollectionWindowChange.Append),
            Generation, reintroduced.Snapshot.Sequence,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        Equal(VirtualCollectionWindowChange.Replace,
            unchangedGeneration.Snapshot.Root.VirtualCollectionWindow!.Change);

        var newerGeneration = WidgetPresentationDiff.Create(
            unchangedGeneration.Snapshot,
            Virtual(6, 22, VirtualCollectionWindowChange.Append),
            Generation, unchangedGeneration.Snapshot.Sequence,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        Equal(VirtualCollectionWindowChange.Append,
            newerGeneration.Snapshot.Root.VirtualCollectionWindow!.Change);
    }

    private static void PropertyNoOpAndRoundTrip()
    {
        var previous = Snapshot(1, "old");
        var identical = Snapshot(2, "old");
        var noOp = WidgetPresentationDiff.Create(
            previous, identical, Generation, 1,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        True(noOp.IsUpdate, "An identical semantic model should advance through an empty update.");
        Equal(0, noOp.Update!.Operations.Count);
        Equal(2L, PresentationUpdateMaterializer.Apply(previous, noOp.Update, Generation).Sequence);

        var changed = Snapshot(3, "new");
        var publication = WidgetPresentationDiff.Create(
            identical with { ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion },
            changed, Generation, 2,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
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

    private static void SelectOptionsUpdateAtomically()
    {
        static ViewNode Select(IReadOnlyList<WidgetSelectOption> options, string value) => new()
        {
            Id = "select.output",
            Kind = ViewNodeKind.Select,
            Text = $"Output: {value}",
            AccessibilityLabel = "Output",
            AccessibilityValue = value,
            SelectOptions = options,
        };

        var priorOptions = new WidgetSelectOption[]
        {
            new("speakers", "Speakers", "output.speakers", true),
            new("headset", "Headset", "output.headset"),
        };
        var currentOptions = new WidgetSelectOption[]
        {
            new("speakers", "Speakers", "output.speakers.retired", false,
                IsDisabled: true),
            new("headset", "Headset", "output.headset.current", true,
                IsBusy: true),
        };
        var stable = Enumerable.Range(0, 24)
            .Select(index => Text($"stable.{index}", $"stable-{index}"))
            .ToArray();
        var previous = SnapshotWithChildren(1,
            [Select(priorOptions, "Speakers"), .. stable]) with
        {
            ProtocolVersion = ProtocolConstants.AnchoredSelectVersion,
        };
        var current = SnapshotWithChildren(2,
            [Select(currentOptions, "Headset"), .. stable]) with
        {
            ProtocolVersion = ProtocolConstants.AnchoredSelectVersion,
        };

        var publication = WidgetPresentationDiff.Create(
            previous, current, Generation, previous.Sequence,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        True(publication.IsUpdate,
            "A bounded Select authority change must remain one atomic update.");
        var operation = publication.Update!.Operations.Single(item =>
            item.TargetId == "select.output");
        True(operation.Properties!.Any(change =>
                change.Property == PresentationProperty.SelectOptions),
            "The exact Select option authority was omitted from the update.");
        Equal(
            PresentationPropertyImpact.Authority |
            PresentationPropertyImpact.Interaction |
            PresentationPropertyImpact.Paint |
            PresentationPropertyImpact.Accessibility,
            PresentationPropertyMetadata.Impact(PresentationProperty.SelectOptions));

        var bytes = PresentationUpdateJson.Serialize(publication.Update);
        var roundTrip = PresentationUpdateJson.Deserialize(bytes);
        var materialized = PresentationUpdateMaterializer.Apply(
            previous, roundTrip, Generation);
        var admitted = Find(materialized.Root, "select.output");
        Equal(0, ViewSnapshotValidator.Validate(materialized).Count);
        Equal("Headset", admitted.AccessibilityValue);
        Equal(currentOptions.Length, admitted.SelectOptions.Count);
        for (var index = 0; index < currentOptions.Length; index++)
            Equal(currentOptions[index], admitted.SelectOptions[index]);
        True(admitted.SelectOptions.All(option =>
                option.ActionId is not "output.speakers" and not "output.headset"),
            "Stale Select option action authority survived materialization.");
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
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
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
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
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
            PresentationUpdateCapabilities.None,
            WidgetPresentationTransactionKind.IncrementalUpdate).FallbackReason);
        Equal("base_mismatch", WidgetPresentationDiff.Create(
            previous, current, Generation, 19,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).FallbackReason);
        Equal("checkpoint_requested", WidgetPresentationDiff.Create(
            previous, current, Generation, 0,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.OrdinaryCheckpoint).FallbackReason);
        var tiny = PresentationUpdateCapabilities.Current with { MaximumBatchBytes = 1 };
        Equal("checkpoint_smaller", WidgetPresentationDiff.Create(
            previous, current, Generation, 20, tiny,
            WidgetPresentationTransactionKind.IncrementalUpdate).FallbackReason);

        var moved = SnapshotWithChildren(22,
            new ViewNode
            {
                Id = "new-parent",
                Kind = ViewNodeKind.Stack,
                Children = [Text("text.0", "old")],
            });
        Equal("unstable_identity", WidgetPresentationDiff.Create(
            previous, moved, Generation, 20,
            PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).FallbackReason);
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

    private static void IntermediateStructureBoundsFailClosed()
    {
        var parent = new ViewNode
        {
            Id = "parent",
            Kind = ViewNodeKind.Stack,
            Children = [],
        };
        var current = SnapshotWithChildren(30, parent);

        ViewNode deep = Text("deep.30", "tail");
        for (var depth = 29; depth >= 0; depth--)
        {
            deep = new ViewNode
            {
                Id = $"deep.{depth}",
                Kind = ViewNodeKind.Stack,
                Children = [deep],
            };
        }
        AssertIntermediateFailure("too_deep", current, deep);

        var wide = new ViewNode
        {
            Id = "wide.root",
            Kind = ViewNodeKind.Stack,
            Children = Enumerable.Range(0, ProtocolConstants.MaximumNodeCount - 1)
                .Select(index => Text($"wide.{index}", "row"))
                .ToArray(),
        };
        AssertIntermediateFailure("too_many_nodes", current, wide);
    }

    private static void AssertIntermediateFailure(
        string expectedCode,
        ViewSnapshot current,
        ViewNode transientSubtree)
    {
        var batch = new PresentationUpdateBatch
        {
            WidgetInstanceId = current.WidgetInstanceId,
            PresentationGeneration = Generation,
            BaseSequence = current.Sequence,
            Sequence = current.Sequence + 1,
            Operations =
            [
                new PresentationUpdateOperation
                {
                    Kind = PresentationUpdateOperationKind.InsertChild,
                    ParentId = "parent",
                    Index = 0,
                    Subtree = transientSubtree,
                },
                new PresentationUpdateOperation
                {
                    Kind = PresentationUpdateOperationKind.RemoveChild,
                    ParentId = "parent",
                    ChildId = transientSubtree.Id,
                },
            ],
        };

        try
        {
            _ = PresentationUpdateMaterializer.Apply(current, batch, Generation);
        }
        catch (ProtocolValidationException exception)
        {
            True(exception.Errors.Any(error => error.Code == expectedCode),
                $"Expected intermediate failure '{expectedCode}', received " +
                string.Join(',', exception.Errors.Select(error => error.Code)));
            return;
        }
        throw new InvalidOperationException(
            $"An intermediate '{expectedCode}' violation reached the later removal operation.");
    }

    private static void DifferIsTrustTierNeutral()
    {
        var alpha = WidgetPresentationDiff.Create(
            Snapshot(1, "old", "sandbox.alpha"), Snapshot(2, "new", "sandbox.alpha"),
            Generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        var beta = WidgetPresentationDiff.Create(
            Snapshot(1, "old", "fulltrust.beta"), Snapshot(2, "new", "fulltrust.beta"),
            Generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
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
