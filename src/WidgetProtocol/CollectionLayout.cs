using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CollectionLayoutKind>))]
public enum CollectionLayoutKind { List, AdaptiveGrid }

/// <summary>
/// Explicit host realization contract for a Scroll whose direct children are
/// keyed Buttons or ActionSurfaces. Items remain logical members when their UI
/// is not realized. An estimate is not a fixed item size or exact scroll extent.
/// </summary>
public sealed record CollectionLayout
{
    public required CollectionLayoutKind Kind { get; init; }
    /// <summary>Main-axis item extent excluding the collection gap, in DIPs.</summary>
    public required double EstimatedItemExtent { get; init; }
    public double? MinimumColumnWidth { get; init; }
    public int? MaximumColumns { get; init; }
}
