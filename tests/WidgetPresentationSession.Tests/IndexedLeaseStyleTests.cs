using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class IndexedRangeSessionTests
{
    private static Dictionary<string, BridgeNodeRenderStyles> EmptyRangeStyles(IndexedCollectionRange range) =>
        BridgeRenderStyleContract.RangeNodeIds(range).ToDictionary(id => id, _ => new BridgeNodeRenderStyles
        {
            Base = new Dictionary<string, BridgeComputedStyleValue>(),
            Focused = new Dictionary<string, BridgeComputedStyleValue>(),
            Pressed = new Dictionary<string, BridgeComputedStyleValue>(),
        }, StringComparer.Ordinal);

    [TestMethod]
    public async Task IndexedLeaseStylesAreImmutableAndRetainedWithRange()
    {
        await RunAsync(async channel =>
        {
            var acquire = await ReadAsync(channel);
            var request = AcquireRequest(acquire);
            var range = Range(request.Range);
            var styles = EmptyRangeStyles(range);
            styles[range.Items[0].Root.Id] = new()
            {
                Base = new Dictionary<string, BridgeComputedStyleValue>
                { ["color"] = new() { Kind = WrssValueKind.Color, Text = "#123456" } },
                Focused = new Dictionary<string, BridgeComputedStyleValue>
                { ["color"] = new() { Kind = WrssValueKind.Color, Text = "#abcdef" } },
                Pressed = new Dictionary<string, BridgeComputedStyleValue>(),
            };
            await ReplyAsync(channel, acquire.RequestId, BridgeMessageTypes.IndexedLease,
                new BridgeIndexedLeaseResponse(request.WidgetId, request.InstanceId, request.RuntimeGeneration,
                    request.PresentationGeneration, new(Guid.NewGuid().ToString("N"), range), styles));
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            var key = lease.Range.Items[0].Root.Id;
            Assert.AreEqual("#123456", lease.RenderStyles[key].Base["color"].Text);
            Assert.AreEqual("#abcdef", lease.RenderStyles[key].Focused["color"].Text);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, BridgeNodeRenderStyles>)lease.RenderStyles).Clear());
            Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, BridgeComputedStyleValue>)lease.RenderStyles[key].Base).Clear());
        });
    }

    [TestMethod]
    [DataRow("foreign")]
    [DataRow("missing")]
    [DataRow("null-state")]
    [DataRow("bad-kind")]
    [DataRow("oversized-state")]
    public async Task InvalidIndexedLeaseStylesReleaseExactDeliveredDemand(string defect)
    {
        await RunAsync(async channel =>
        {
            var acquire = await ReadAsync(channel);
            var request = AcquireRequest(acquire);
            var range = Range(request.Range);
            var styles = EmptyRangeStyles(range);
            var key = range.Items[0].Root.Id;
            if (defect == "foreign") { styles["parent-root"] = styles[key]; styles.Remove(key); }
            if (defect == "missing") styles.Clear();
            if (defect == "null-state") styles[key] = styles[key] with { Focused = null! };
            if (defect == "bad-kind") styles[key] = styles[key] with
            {
                Base = new Dictionary<string, BridgeComputedStyleValue> { ["color"] = new() { Kind = (WrssValueKind)999, Text = "bad" } },
            };
            if (defect == "oversized-state") styles[key] = styles[key] with
            {
                Base = Enumerable.Range(0, BridgeRenderStyleLimits.MaximumPropertiesPerState + 1).ToDictionary(index => "property-" + index,
                    _ => new BridgeComputedStyleValue { Kind = WrssValueKind.Number, Text = "1", Number = 1 }),
            };
            await ReplyAsync(channel, acquire.RequestId, BridgeMessageTypes.IndexedLease,
                new BridgeIndexedLeaseResponse(request.WidgetId, request.InstanceId, request.RuntimeGeneration,
                    request.PresentationGeneration, new(Guid.NewGuid().ToString("N"), range), styles));
            var cancel = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.CancelIndexedRange, cancel.Type);
            Assert.AreEqual(request, BridgeJson.FromElement<BridgeIndexedRangeRequest>(cancel.Payload));
            await ReplyAsync(channel, cancel.RequestId, BridgeMessageTypes.Acknowledged, new { cancelled = true });
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1));
            await using var recovery = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            Assert.IsTrue(recovery.IsCurrent);
        });
    }

    [TestMethod]
    public void EqualComputedStatesShareImmutableStorageWithinOnePayload()
    {
        var color=new BridgeComputedStyleValue{Kind=WrssValueKind.Color,Text="#123456"};
        var width=new BridgeComputedStyleValue{Kind=WrssValueKind.Length,Text="12px",Number=12,Unit="px"};
        var first=new Dictionary<string,BridgeComputedStyleValue>{{"color",color},{"width",width}};
        var reordered=new Dictionary<string,BridgeComputedStyleValue>{{"width",width with{}},{"color",color with{}}};
        var changed=new Dictionary<string,BridgeComputedStyleValue>(first){["width"]=width with{Number=24}};
        var source=new Dictionary<string,BridgeNodeRenderStyles>
        {
            ["one"]=new(){Base=first,Focused=reordered,Pressed=changed},
            ["two"]=new(){Base=reordered,Focused=first,Pressed=changed}
        };
        var ids=source.Keys.ToHashSet();
        var frozen=BridgeRenderStyleContract.ValidateAndFreeze(source,ids,true);
        Assert.AreSame(frozen["one"].Base,frozen["one"].Focused);
        Assert.AreSame(frozen["one"].Base,frozen["two"].Base);
        Assert.AreNotSame(frozen["one"].Base,frozen["one"].Pressed);
        Assert.AreEqual(24d,frozen["one"].Pressed["width"].Number);
        Assert.AreNotSame(frozen["one"].Base,BridgeRenderStyleContract.ValidateAndFreeze(source,ids,true)["one"].Base);
        first.Clear();reordered.Clear();changed.Clear();source.Clear();
        Assert.AreEqual(12d,frozen["two"].Base["width"].Number);
        Assert.ThrowsExactly<NotSupportedException>(()=>
            ((IDictionary<string,BridgeComputedStyleValue>)frozen["two"].Base).Clear());
    }

    [TestMethod]
    public void ComputedStyleFreezingRejectsBudgetsAndMutatingSourceState()
    {
        var value = new BridgeComputedStyleValue { Kind = WrssValueKind.Color, Text = "#123456" };
        var state = new Dictionary<string, BridgeComputedStyleValue> { ["color"] = value };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>
        { ["node"] = new() { Base = state, Focused = state, Pressed = state, GroupHeader = state } };
        var frozen = BridgeRenderStyleContract.ValidateAndFreeze(styles, new HashSet<string> { "node" }, true);
        state.Clear(); styles.Clear();
        Assert.AreEqual("#123456", frozen["node"].Base["color"].Text);
        Assert.AreEqual("#123456", frozen["node"].GroupHeader!["color"].Text);
        var invalidHeader = frozen["node"] with { GroupHeader = Enumerable.Range(0, BridgeRenderStyleLimits.MaximumPropertiesPerState + 1)
            .ToDictionary(index => "property-" + index, _ => value) };
        Assert.ThrowsExactly<BridgeProtocolException>(() => BridgeRenderStyleContract.ValidateAndFreeze(
            new Dictionary<string, BridgeNodeRenderStyles> { ["node"] = invalidHeader }, new HashSet<string> { "node" }, true));
        var oversized = Enumerable.Range(0, BridgeRenderStyleLimits.MaximumNodes + 1).ToDictionary(index => "n" + index,
            _ => frozen["node"]);
        Assert.ThrowsExactly<BridgeProtocolException>(() => BridgeRenderStyleContract.ValidateAndFreeze(oversized,
            oversized.Keys.ToHashSet(), true));
        var fullState = Enumerable.Range(0, BridgeRenderStyleLimits.MaximumPropertiesPerState).ToDictionary(
            index => "property-" + index, _ => value);
        var cumulative = Enumerable.Range(0, 180).ToDictionary(index => "node-" + index,
            _ => new BridgeNodeRenderStyles { Base = fullState, Focused = fullState, Pressed = fullState });
        Assert.ThrowsExactly<BridgeProtocolException>(() => BridgeRenderStyleContract.ValidateAndFreeze(cumulative,
            cumulative.Keys.ToHashSet(), true));
        var bad = frozen["node"] with { Base = new Dictionary<string, BridgeComputedStyleValue>
        { ["opacity"] = new() { Kind = WrssValueKind.Number, Text = "NaN", Number = double.NaN } } };
        Assert.ThrowsExactly<BridgeProtocolException>(() => BridgeRenderStyleContract.ValidateAndFreeze(
            new Dictionary<string, BridgeNodeRenderStyles> { ["node"] = bad }, new HashSet<string> { "node" }, true));
    }
}
