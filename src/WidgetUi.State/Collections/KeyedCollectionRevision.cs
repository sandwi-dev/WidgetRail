namespace WidgetRail.WidgetUi.State.Collections;

/// <summary>One runtime's logical collection. Create a new model for a new authority.</summary>
public sealed record CollectionAuthority(string RuntimeId, string WidgetInstanceId, string CollectionId);

public sealed record KeyedCollectionItem<TPayload>(string Key, TPayload Value) where TPayload : notnull;

/// <summary>
/// Immutable membership update. Generation advances for a new query/traversal;
/// revision advances for updates within that generation. Payloads must be immutable.
/// </summary>
public sealed class KeyedCollectionRevision<TPayload> where TPayload : notnull
{
    public CollectionAuthority Authority { get; }
    public long Generation { get; }
    public long Revision { get; }
    public IReadOnlyList<KeyedCollectionItem<TPayload>> Items { get; }

    public KeyedCollectionRevision(CollectionAuthority authority, long generation, long revision,
        IEnumerable<KeyedCollectionItem<TPayload>> items)
    {
        ValidateAuthority(authority);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in copy)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Key);
            ArgumentNullException.ThrowIfNull(item.Value);
            if (!keys.Add(item.Key)) throw new ArgumentException("Collection item keys must be unique.", nameof(items));
        }
        Authority = authority;
        Generation = generation;
        Revision = revision;
        Items = Array.AsReadOnly(copy);
    }

    internal static void ValidateAuthority(CollectionAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.RuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.WidgetInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.CollectionId);
    }
}
