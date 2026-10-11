using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class LineGraphPresentationTests
{
    internal static Task Contract()
    {
        var samples = new GraphPoint[] { new(-60, 10), new(-20, null), new(0, 30) };
        var element = UI.LineGraph("Load", new(samples, -60, 0, 0, 100, "%"), "load");
        samples[0] = new(-60, 99);
        var before = new WidgetView(element).CreateSnapshot("graph-test", 1);
        Check(before.Root.Graph!.Points[0].Value == 10, "Caller mutated retained graph data");
        var after = before with { Sequence = 2, Root = before.Root with {
            Graph = new([new(-60, 20), new(-20, null), new(0, 40)], -60, 0, 0, 100, "%") } };
        var generation = new string('b', 32);
        var update = WidgetPresentationDiff.Create(before, after, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Check(update is not null, "Graph data did not produce an incremental update");
        var roundtrip = PresentationUpdateJson.Deserialize(PresentationUpdateJson.Serialize(update!));
        var applied = PresentationUpdateMaterializer.Apply(before, roundtrip, generation);
        Check(SnapshotJson.Serialize(applied).SequenceEqual(SnapshotJson.Serialize(after)), "Graph data lost during diff/transport/materialization");
        Check(new LineGraphDefinition([new(0, -1)], -1, 0, -10).EffectiveMaximum > -1,
            "Automatic scale clips a negative-valued series");
        return Task.CompletedTask;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
