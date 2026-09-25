using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Presentation-only behavior coordinated by a section identity.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetTransitionKind>))]
public enum WidgetTransitionKind
{
    Content,
    Layout,
    Selection,
}

/// <summary>
/// Elements with the same GroupId share one host timeline. Key changes start a
/// transition; ordinary snapshots with the same key do not. Order selects direction.
/// The declaration does not create another input scope or retain actionable nodes.
/// </summary>
public sealed record WidgetTransition([property: JsonRequired] string GroupId,
    [property: JsonRequired] string Key, [property: JsonRequired] int Order,
    [property: JsonRequired] WidgetTransitionKind Kind = WidgetTransitionKind.Content);
