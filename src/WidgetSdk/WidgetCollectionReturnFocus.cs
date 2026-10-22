using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// A one-shot return intent captured by a navigator. Retains occurrence identity
/// only, never a query, item, native control, or viewport. Resolve against the
/// current destination source on render; obsolete queries return no override.
/// </summary>
public sealed class WidgetCollectionReturnFocus
{
    internal WidgetCollectionReturnFocus(long requestId, IndexedCollectionFocusTarget item)
    { RequestId = requestId; Item = item; }

    public long RequestId { get; }
    public IndexedCollectionFocusTarget Item { get; }

    /// <summary>
    /// Returns null for a different collection/query or removed index, leaving
    /// normal host focus/viewport memory in charge. Content-only refreshes retain
    /// the intent. Semantic remapping after membership changes remains widget policy.
    /// </summary>
    public FocusGroupEntryRequest? Resolve(string collectionId, IndexedCollectionDescriptor source)
    {
        StableIdentifier.Validate(collectionId, nameof(collectionId));
        IndexedCollectionContract.ValidateDescriptor(source);
        return Item.CollectionId == collectionId && Item.SourceId == source.SourceId &&
            Item.QueryGeneration == source.QueryGeneration && Item.Index < source.Count
            ? new() { RequestId = RequestId, GroupId = collectionId, IndexedItem = Item } : null;
    }
}
