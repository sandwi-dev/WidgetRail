namespace WidgetRail.WidgetProtocol;

/// <summary>Bounded selection of positions in the exact declared indexed query.</summary>
public sealed record IndexedCollectionSelection(bool IsActive, long Epoch, IReadOnlyList<int> Indices, bool LimitReached = false)
{
    public const int MaximumItems = 256;
    [System.Text.Json.Serialization.JsonIgnore]
    public int Count => Indices.Count;
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SupportsHeldSelection { get; init; }
}

public enum IndexedSelectionGesture { Toggle, Replace, Extend, ExtendAdd, SelectAll, Clear, BeginAdd, Add }
public sealed record IndexedSelectionInput(long Epoch, IndexedSelectionGesture Gesture);

public static class IndexedSelectionContract
{
    public static void Validate(IndexedCollectionSelection selection, IndexedCollectionDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(selection);
        IndexedCollectionContract.ValidateDescriptor(source);
        if (source.Discovery is not null || selection.Epoch < 0 || selection.Indices is null ||
            selection.Indices.Count > IndexedCollectionSelection.MaximumItems || !selection.IsActive && selection.Indices.Count != 0)
            throw new ArgumentException("Invalid indexed selection.", nameof(selection));
        var previous = -1;
        foreach (var index in selection.Indices)
        {
            if (index <= previous || index >= source.Count) throw new ArgumentException("Selection indexes must be unique sorted positions in the current query.", nameof(selection));
            previous = index;
        }
    }
}
