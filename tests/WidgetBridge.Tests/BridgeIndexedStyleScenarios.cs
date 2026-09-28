using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

internal static class BridgeIndexedStyleScenarios
{
    internal static Task RangeStylesMatchSnapshotCascadeAndFragments()
    {
        var parsed = WrssParser.Parse("button.row { color: #123456; } button.row:focused { color: #abcdef; } button.row:pressed { opacity: 0.75; } text.fragment { font-size: 18px; }", "indexed.wrss");
        var compiled = WrssThemeCompiler.Compile([parsed.Document]);
        Check(compiled.IsValid, "Indexed fixture style did not compile.");
        var root = new ViewNode
        {
            Id = "row", Kind = ViewNodeKind.Button, ActionId = "open", CollectionItemKey = "item", StyleClasses = ["row"],
            FocusPresentation = new() { Id = "focused-detail", Kind = ViewNodeKind.Text, Text = "Details", StyleClasses = ["fragment"] },
            DefaultFocusPresentation = new() { Id = "default-detail", Kind = ViewNodeKind.Text, Text = "Default", StyleClasses = ["fragment"] },
        };
        var range = new IndexedCollectionRange("instance", "items", new("source", 1, 0, 1), "page", 0, "demand", [new("item", root)]);
        var actual = BridgeRenderStyleResolver.ResolveRange(range, compiled.Theme);
        var parent = new ViewSnapshot { WidgetInstanceId = "instance", Sequence = 1, ActiveInputScopeId = "page",
            Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = [root] } };
        var expected = BridgeRenderStyleResolver.Resolve(parent, compiled.Theme);
        Check(actual.Count == 3 && !actual.ContainsKey("page"), "Range styling included unrelated parent or missed fragments.");
        Check(actual["row"].Base["color"].Text == "#123456", "Row class style missing.");
        Check(actual["row"].Focused["color"].Text == "#abcdef", "Focused state missing.");
        Check(actual["row"].Pressed["opacity"].Number == 0.75, "Pressed state missing.");
        Check(actual["focused-detail"].Base["font-size"].Number == 18, "Focused fragment style missing.");
        foreach (var (id, value) in actual)
            Check(BridgeJson.ToElement(value).GetRawText() == BridgeJson.ToElement(expected[id]).GetRawText(), "Range and snapshot style cascade differ.");
        var frozen = BridgeRenderStyleContract.ValidateAndFreeze(actual, BridgeRenderStyleContract.RangeNodeIds(range), true);
        Check(frozen.Count == 3, "Range style map failed shared validation.");
        return Task.CompletedTask;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

internal static partial class BridgeIndexedLeaseRegistryScenarios
{
    internal static async Task StylePreparationFailureReleasesExactRuntimeLease()
    {
        await using var fixture = await Fixture.Start();
        _ = await fixture.Snapshot();
        await Reject(() => fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("bad-style"), CancellationToken.None,
            (_, _) => throw new BridgeProtocolException("Fixture style failure.")));
        Check(fixture.Client.Leases.Single().Disposed == 1, "Style failure leaked acquired runtime semantics.");
        using var next = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("next"), CancellationToken.None);
        Check(next.Value.RenderStyles.Count == 2 && next.Value.RenderStyles.ContainsKey("item.image"),
            "Successful acquisition did not include every range node style.");
        await fixture.Registry.ReleaseIndexedLeaseAsync(fixture.Release(next.Value));
    }
}
