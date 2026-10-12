using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Presentation-only behavior coordinated by a section or page identity.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetTransitionKind>))]
public enum WidgetTransitionKind
{
    Content,
    Layout,
    Selection,
    Page,
}

[JsonConverter(typeof(JsonStringEnumConverter<WidgetNavigationOperation>))]
public enum WidgetNavigationOperation { Forward, Back, Replace }

/// <summary>
/// Elements with the same GroupId share one host timeline. Key changes start a
/// transition; ordinary snapshots with the same key do not. Section Order selects direction;
/// pages declare a Navigation operation.
/// The declaration does not create another input scope or retain actionable nodes.
/// </summary>
public sealed record WidgetTransition([property: JsonRequired] string GroupId,
    [property: JsonRequired] string Key, [property: JsonRequired] int Order,
    [property: JsonRequired] WidgetTransitionKind Kind = WidgetTransitionKind.Content,
    WidgetNavigationOperation? Navigation = null);
