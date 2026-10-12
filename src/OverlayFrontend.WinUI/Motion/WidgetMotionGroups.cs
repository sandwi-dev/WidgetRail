using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

internal sealed record WidgetMotionGroupChange(string GroupId, string PreviousKey, string Key, int Direction);

/// <summary>One update for all section/header/selection declarations in a committed page.</summary>
internal sealed class WidgetMotionGroups
{
    private Dictionary<string, (string Key, int Order, WidgetNavigationOperation? Navigation)> previous = new(StringComparer.Ordinal);

    internal IReadOnlyList<WidgetMotionGroupChange> Update(IEnumerable<WidgetTransition> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var next = new Dictionary<string, (string Key, int Order, WidgetNavigationOperation? Navigation)>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            var value = (declaration.Key, declaration.Order, declaration.Navigation);
            if (next.TryGetValue(declaration.GroupId, out var existing) && existing != value)
                throw new InvalidOperationException("A motion group must have one key and order per committed page.");
            next[declaration.GroupId] = value;
            if (next.Count > ProtocolConstants.MaximumWidgetTransitionGroups)
                throw new InvalidOperationException("The page exceeds the motion-group budget.");
        }
        var changes = next.Where(pair => previous.TryGetValue(pair.Key, out var old) && old.Key != pair.Value.Key)
            .Select(pair => new WidgetMotionGroupChange(pair.Key, previous[pair.Key].Key, pair.Value.Key,
                pair.Value.Navigation is { } operation ? operation == WidgetNavigationOperation.Back ? -1 : operation == WidgetNavigationOperation.Replace ? 0 : 1 : pair.Value.Order < previous[pair.Key].Order ? -1 : 1)).ToArray();
        previous = next;
        return Array.AsReadOnly(changes);
    }

    internal void Reset() => previous.Clear();
}
