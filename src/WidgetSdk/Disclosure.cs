using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static partial class UI
{
    /// <summary>
    /// Creates an authored-state disclosure header controlling a separate content
    /// subtree. Handlers must set expanded state explicitly, never toggle it.
    /// Omit the complete content subtree while collapsed. The host owns UIA state
    /// and returns focus to this header only when collapse removes focused content.
    /// </summary>
    public static ButtonElement DisclosureHeader(string id, string label, bool expanded,
        string contentId, string expandActionId, string collapseActionId)
    {
        StableIdentifier.Validate(contentId, nameof(contentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(expandActionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(collapseActionId);
        if (expandActionId == collapseActionId)
            throw new ArgumentException("Expand and collapse need distinct idempotent actions.", nameof(collapseActionId));
        return Button(label, expanded ? collapseActionId : expandActionId, id) with
        { Disclosure = new(contentId, expanded) };
    }

    /// <summary>Creates a stable header and lazily authored optional content, using id.header and id.content.</summary>
    public static StackElement Disclosure(string id, string label, bool expanded,
        string expandActionId, string collapseActionId, Func<IReadOnlyList<WidgetElement>> content)
    {
        StableIdentifier.Validate(id, nameof(id));
        ArgumentNullException.ThrowIfNull(content);
        var header = DisclosureHeader(id + ".header", label, expanded, id + ".content", expandActionId, collapseActionId);
        if (!expanded) return Stack(id, header);
        var children = content() ?? throw new InvalidOperationException("Disclosure content must not be null.");
        return Stack(id, header, Stack(id + ".content", children.ToArray()));
    }
}
