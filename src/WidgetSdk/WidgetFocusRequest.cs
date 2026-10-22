using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// One explicit focus or scroll intent. Create once when handling an event, retain
/// it while its destination loads, and reuse it across renders. Creation does not
/// invalidate the widget or move focus; the published view carries the request.
/// </summary>
public sealed class WidgetFocusRequest
{
    internal WidgetFocusRequest(long id) => Id = id;

    /// <summary>Widget-instance identity, also usable to reject superseded intents.</summary>
    public long Id { get; }

    /// <summary>Enter the group's remembered or default child.</summary>
    public FocusGroupEntryRequest EnterGroup(string groupId)
    {
        StableIdentifier.Validate(groupId, nameof(groupId));
        return new() { RequestId = Id, GroupId = groupId };
    }

    /// <summary>Enter a collection, optionally at an exact current occurrence.</summary>
    public FocusGroupEntryRequest EnterCollection<TQuery, TItem>(
        WidgetIndexedCollection<TQuery, TItem> source, string collectionId,
        IndexedCollectionFocusTarget? item = null) where TQuery : notnull where TItem : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Enter(collectionId, Id, item);
    }

    /// <summary>Enter the discovered prefix without fetching the entire source.</summary>
    public FocusGroupEntryRequest EnterCollection<TQuery, TItem>(
        WidgetDiscoveredCollection<TQuery, TItem> source, string collectionId,
        IndexedCollectionFocusTarget? item = null) where TQuery : notnull where TItem : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Enter(collectionId, Id, item);
    }

    /// <summary>Reveal a declared element without moving focus.</summary>
    public ScrollRevealRequest Reveal(string scrollId, string targetId)
    {
        StableIdentifier.Validate(scrollId, nameof(scrollId));
        StableIdentifier.Validate(targetId, nameof(targetId));
        return new(Id, scrollId, targetId);
    }

    /// <summary>Reveal an exact collection occurrence without moving focus.</summary>
    public ScrollRevealRequest RevealCollection<TQuery, TItem>(
        WidgetIndexedCollection<TQuery, TItem> source, string collectionId,
        IndexedCollectionFocusTarget item) where TQuery : notnull where TItem : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Reveal(collectionId, Id, item);
    }
}

internal sealed class WidgetFocusRequestAllocator
{
    private long nextId;

    internal WidgetFocusRequest? TryCreate()
    {
        while (true)
        {
            var previous = Interlocked.Read(ref nextId);
            if (previous >= ProtocolConstants.MaximumFocusGroupEntryRequestId) return null;
            if (Interlocked.CompareExchange(ref nextId, previous + 1, previous) == previous)
                return new(previous + 1);
        }
    }
}

public abstract partial class Widget
{
    private readonly WidgetFocusRequestAllocator focusRequestAllocator = new();

    /// <summary>
    /// Allocates one intent from the sequence shared by this widget and all its
    /// navigators. Never call on every render. The sequence survives deactivation
    /// and modal/page changes; existing explicit-ID APIs remain supported.
    /// </summary>
    protected WidgetFocusRequest CreateFocusRequest() =>
        focusRequestAllocator.TryCreate() ?? throw new InvalidOperationException(
            "The widget focus request sequence is exhausted.");
}
