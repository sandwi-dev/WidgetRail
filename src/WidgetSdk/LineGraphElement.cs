using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A host-rendered, noninteractive line graph. Null samples represent missing data.</summary>
public sealed record LineGraphElement : WidgetElement
{
    internal LineGraphElement(string id, string accessibleName, LineGraphDefinition graph) : base(RequireId(id))
    {
        if (string.IsNullOrWhiteSpace(accessibleName)) throw new ArgumentException("A graph needs a readable name.", nameof(accessibleName));
        if (graph is null || !graph.IsWellFormed()) throw new ArgumentException("Invalid graph bounds or samples.", nameof(graph));
        AccessibleName = accessibleName;
        Graph = graph with { Points = graph.Points.ToArray() };
    }
    public string AccessibleName { get; init; }
    public LineGraphDefinition Graph { get; init; }
    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id, Kind = ViewNodeKind.LineGraph, Graph = Graph,
        AccessibilityLabel = AccessibleName, StyleClasses = StyleClasses,
    };
}

public static partial class UI
{
    /// <summary>Draw a bounded informational graph without adding a controller focus stop.</summary>
    public static LineGraphElement LineGraph(string accessibleName, LineGraphDefinition graph, string id) => new(id, accessibleName, graph);
}
