namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobDisclosure(string Context, long Token, bool Expanded);

/// <summary>Small presentation-only memory. Context changes retire old toggle actions;
/// recalculation of the same context preserves its disclosure preference.</summary>
internal sealed class PobDisclosureState
{
    internal const int MaximumContexts = 24;
    private readonly LinkedList<(string Context, bool Expanded)> recent = new();
    private long token;
    internal PobDisclosure? Current { get; private set; }
    internal int RetainedCount => recent.Count;
    internal PobDisclosure Select(string context, bool defaultExpanded)
    {
        if (Current?.Context == context) return Current;
        var cached = recent.FirstOrDefault(entry => entry.Context == context);
        var expanded = cached.Context is null ? defaultExpanded : cached.Expanded;
        Current = new(context, ++token, expanded);
        Remember(context, expanded);
        return Current;
    }
    internal bool Toggle(long expectedToken)
    {
        if (Current is not { } current || current.Token != expectedToken) return false;
        Current = current with { Expanded = !current.Expanded };
        Remember(current.Context, !current.Expanded);
        return true;
    }
    private void Remember(string context, bool expanded)
    {
        for (var node = recent.First; node is not null; node = node.Next)
            if (node.Value.Context == context) { recent.Remove(node); break; }
        recent.AddFirst((context, expanded));
        while (recent.Count > MaximumContexts) recent.RemoveLast();
    }
}
