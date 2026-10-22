using System.Reflection;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetFocusRequestTests
{
    internal static async Task Run()
    {
        using var widget = new FocusWidget();
        await WidgetTestHost.InitializeAsync(widget);
        var explicitRequest = widget.Request();
        ExactTargetsAreScopedAndVersioned(explicitRequest);
        CollectionReturnsAreVersioned(widget, explicitRequest);
        widget.First.NavigateRoot("second", "first.group");
        var first = widget.First.Value.FocusGroupEntryRequest!;
        widget.Second.NavigateRoot("second", "second.group");
        var second = widget.Second.Value.FocusGroupEntryRequest!;
        var reveal = widget.Request().Reveal("scroll", "target");
        Check(explicitRequest.Id < first.RequestId && first.RequestId < second.RequestId &&
            second.RequestId < reveal.RequestId, "Independent sources reused a consumed request ID.");
        Check(explicitRequest.EnterGroup("group") == explicitRequest.EnterGroup("group"),
            "Publishing an intent twice changed its identity.");
        var before = widget.First.Value;
        widget.First.NavigateRoot("second", "first.group");
        Check(widget.First.Value == before, "Repeated navigation emitted another intent.");
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        Check(widget.Request().Id > reveal.RequestId, "Activation reset request identity.");

        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();
        Parallel.For(0, 1000, _ => ids.Add(widget.Request().Id));
        Check(ids.Distinct().Count() == 1000, "Concurrent events reused request identity.");

        // Exercise a protocol bound unreachable in an ordinary interactive test.
        var allocator = new WidgetFocusRequestAllocator();
        typeof(WidgetFocusRequestAllocator).GetField("nextId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(allocator, ProtocolConstants.MaximumFocusGroupEntryRequestId - 1);
        Check(allocator.TryCreate()!.Id == ProtocolConstants.MaximumFocusGroupEntryRequestId,
            "The final representable request was lost.");
        Check(allocator.TryCreate() is null && allocator.TryCreate() is null,
            "Exhaustion wrapped or reused the final request.");
        using var bounded = new WidgetNavigator<string>("bounded", "root", 4, 4, null,
            CancellationToken.None, () => throw new InvalidOperationException("Rejected navigation invalidated."),
            focusRequests: allocator);
        var original = bounded.Value;
        Check(bounded.NavigateRoot("other", "other.group") == WidgetNavigationResult.RejectedCapacity &&
            bounded.Value == original, "Exhaustion partially changed navigation.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static void ExactTargetsAreScopedAndVersioned(WidgetFocusRequest intent)
    {
        var root = UI.Stack("root", UI.Button("Header", "header", "header"),
            UI.Stack("group", UI.Button("First", "first", "first"),
                UI.Button("Second", "second", "second")).RememberChildFocus("second"),
            UI.Stack("foreign", UI.Button("Other", "other", "other")).InputScope("foreign"));
        var snapshot = new WidgetView(root, "header")
        { FocusGroupEntryRequest = intent.FocusElement("group", "first") }.CreateSnapshot("exact", 1);
        Check(snapshot.ProtocolVersion == ProtocolConstants.ExactFocusTargetVersion &&
            ViewSnapshotValidator.Validate(snapshot).Count == 0, "Exact target did not receive its versioned contract.");
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).FocusGroupEntryRequest?.TargetId == "first",
            "Exact target was lost in transport.");
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 98 }).Count > 0,
            "An older host admitted a feature it cannot execute.");
        foreach (var target in new[] { "missing", "header", "other", "group" })
            Check(ViewSnapshotValidator.Validate(snapshot with
            { FocusGroupEntryRequest = intent.FocusElement("group", target) }).Any(error => error.Code == "invalid_exact_focus_target"),
                "Missing, unrelated, inactive, or non-focusable target was admitted: " + target);
        Check(ViewSnapshotValidator.Validate(snapshot with
        { FocusGroupEntryRequest = intent.FocusElement("group", "first") with
            { IndexedItem = new("group", "source", 1, "item", 0) } }).Any(error => error.Code == "conflicting_focus_targets"),
            "Conflicting exact and indexed targets were admitted.");
    }

    private sealed class FocusWidget : Widget, IDisposable
    {
        internal FocusWidget()
        {
            First = CreateNavigator("first", "root");
            Second = CreateNavigator("second", "root");
            Source = CreateLoadedCollection<int, int>("source", "query", 0, [0, 1], new()
            {
                ItemKey = item => new("item." + item),
                RenderItem = (_, item, ids) => UI.Button(item.ToString(), "open", ids.Id("row")),
                OnAction = (_, _, _, _) => ValueTask.CompletedTask,
            });
        }
        internal WidgetLoadedCollection<int, int> Source { get; }
        internal WidgetNavigator<string> First { get; }
        internal WidgetNavigator<string> Second { get; }
        internal WidgetFocusRequest Request() => CreateFocusRequest();
        public override WidgetView Render() => new(UI.Stack("root", UI.Button("Action", "action", "action")), "action");
        public void Dispose() { First.Dispose(); Second.Dispose(); }
    }

    private static void CollectionReturnsAreVersioned(FocusWidget widget, WidgetFocusRequest intent)
    {
        var target = widget.Source.FocusTarget("items", new("item.1"), 1);
        var enter = intent.EnterCollection(widget.Source, "items", target);
        Check(!enter.RestoreViewport, "Ordinary collection entry changed to return behavior.");
        var ordinary = new WidgetView(UI.Stack("root", UI.CollectionList("items", widget.Source, 48, "Items")), "items")
            { FocusGroupEntryRequest = enter }.CreateSnapshot("return", 1);
        // Indexed parents advertise current SDK support for their deferred row
        // templates; the feature's minimum requirement is calculated separately.
        Check(ProtocolVersionRequirements.Calculate(ordinary).RequiredVersion < ProtocolConstants.CollectionReturnViewportVersion &&
            !System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(ordinary)).Contains("restoreViewport", StringComparison.Ordinal),
            "Ordinary collection entry unnecessarily changed its wire contract.");
        var request = intent.ReturnToCollection(widget.Source, "items", target);
        var snapshot = new WidgetView(UI.Stack("root", UI.CollectionList("items", widget.Source, 48, "Items")), "items")
            { FocusGroupEntryRequest = request }.CreateSnapshot("return", 1);
        Check(snapshot.ProtocolVersion == ProtocolConstants.CollectionReturnViewportVersion &&
            ViewSnapshotValidator.Validate(snapshot).Count == 0, "Return viewport contract is invalid or unversioned.");
        Check(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)).FocusGroupEntryRequest == request,
            "Return viewport intent was lost in transport.");
        const string generation = "abababababababababababababababab";
        var entered = snapshot with { FocusGroupEntryRequest = enter };
        var returned = snapshot with { Sequence = 2 };
        var publication = WidgetPresentationDiff.Create(entered, returned, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        Check(publication.FallbackReason == "focus_group_entry_request_changed" && publication.Snapshot.FocusGroupEntryRequest == request,
            "Changed return intent did not use the existing focus-metadata checkpoint contract.");
        var update = WidgetPresentationDiff.Create(returned, returned with { Sequence = 3 }, generation, 2,
            PresentationUpdateCapabilities.Current, WidgetPresentationTransactionKind.IncrementalUpdate).Update!;
        Check(PresentationUpdateMaterializer.Apply(returned, update, generation).FocusGroupEntryRequest == request,
            "Incremental content publication lost the unchanged return intent.");
        var older = entered with { ProtocolVersion = 100 };
        Check(WidgetPresentationDiff.Create(older, returned, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).FallbackReason == "snapshot_protocol_advanced",
            "First return intent did not publish its advanced protocol version as a checkpoint.");
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 100 }).Count > 0,
            "An older host admitted return viewport behavior.");
        Check(ViewSnapshotValidator.Validate(snapshot with { FocusGroupEntryRequest = request with { IndexedItem = null } })
            .Any(error => error.Code == "invalid_return_viewport"), "Untargeted viewport return was admitted.");
        Check(ViewSnapshotValidator.Validate(snapshot with { FocusGroupEntryRequest = request with { TargetId = "root" } })
            .Any(error => error.Code == "invalid_return_viewport"), "Mixed target viewport return was admitted.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
