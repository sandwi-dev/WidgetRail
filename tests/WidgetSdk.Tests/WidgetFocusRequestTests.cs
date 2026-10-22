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

    private sealed class FocusWidget : Widget, IDisposable
    {
        internal FocusWidget()
        {
            First = CreateNavigator("first", "root");
            Second = CreateNavigator("second", "root");
        }
        internal WidgetNavigator<string> First { get; }
        internal WidgetNavigator<string> Second { get; }
        internal WidgetFocusRequest Request() => CreateFocusRequest();
        public override WidgetView Render() => new(UI.Stack("root", UI.Button("Action", "action", "action")), "action");
        public void Dispose() { First.Dispose(); Second.Dispose(); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
