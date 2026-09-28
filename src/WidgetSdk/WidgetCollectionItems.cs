using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// Reuses immutable keyed item declarations across collection captures. Store
/// one instance on the widget. TItem must contain every render input (including
/// selection/playback flags); the factory must not read changing ambient state.
/// Equality determines whether the item needs rebuilding. Mutable inputs require
/// an explicit Clear before capture. This cache owns no data loading or host UI.
/// </summary>
public sealed class WidgetCollectionItems<TItem> where TItem : notnull
{
    private sealed record Entry(TItem Input, WidgetElement Element);
    private readonly object _gate = new();
    private readonly Func<TItem, WidgetCollectionItemKey> _key;
    private readonly Func<TItem, WidgetElement> _render;
    private readonly IEqualityComparer<TItem> _comparer;
    private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _capturing;

    public WidgetCollectionItems(Func<TItem, WidgetCollectionItemKey> itemKey,
        Func<TItem, WidgetElement> renderItem, IEqualityComparer<TItem>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(itemKey);
        ArgumentNullException.ThrowIfNull(renderItem);
        _key = itemKey;
        _render = renderItem;
        _comparer = comparer ?? EqualityComparer<TItem>.Default;
    }

    /// <summary>
    /// Atomically captures one bounded logical window, retaining only its items.
    /// Reordering reuses declarations by key. A failed key/factory/validation step
    /// leaves the previous cache intact. Previously returned captures stay valid.
    /// </summary>
    public IReadOnlyList<WidgetElement> Capture(IReadOnlyList<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > ProtocolConstants.MaximumCursorCollectionItems)
            throw new ArgumentOutOfRangeException(nameof(items), "Collection exceeds the retained descriptor limit.");
        lock (_gate)
        {
            if (_capturing) throw new InvalidOperationException("Collection factories cannot recursively capture or clear their cache.");
            _capturing = true;
            try
            {
                var inputs = items.ToArray();
                if (inputs.Length > ProtocolConstants.MaximumCursorCollectionItems)
                    throw new ArgumentOutOfRangeException(nameof(items));
                var keys = new string[inputs.Length];
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < inputs.Length; index++)
                {
                    ArgumentNullException.ThrowIfNull(inputs[index]);
                    keys[index] = _key(inputs[index]).Value;
                    StableIdentifier.Validate(keys[index], nameof(items));
                    if (!unique.Add(keys[index])) throw new ArgumentException("Collection item keys must be unique.", nameof(items));
                }
                var next = new Dictionary<string, Entry>(inputs.Length, StringComparer.Ordinal);
                var elements = new WidgetElement[inputs.Length];
                for (var index = 0; index < inputs.Length; index++)
                {
                    var key = keys[index];
                    if (!_entries.TryGetValue(key, out var entry) || !_comparer.Equals(entry.Input, inputs[index]))
                    {
                        var element = _render(inputs[index]) ?? throw new InvalidOperationException("Collection factory returned null.");
                        var node = element.ToProtocolNode();
                        if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.ActionSurface) ||
                            node.VisibleWhen is not (null or ResponsiveVisibility.Always) ||
                            (node.CollectionItemKey is { } declaredKey && declaredKey != key))
                            throw new ArgumentException("Collection factories must produce one always-present button or action surface with the selected key.", nameof(items));
                        var count = 0;
                        node = WidgetDeclarationSnapshot.Freeze(node with { CollectionItemKey = key }, 1, ref count);
                        entry = new Entry(inputs[index], new CapturedElement(element, node));
                    }
                    next.Add(key, entry);
                    elements[index] = entry.Element;
                }
                _entries = next;
                return Array.AsReadOnly(elements);
            }
            finally { _capturing = false; }
        }
    }

    /// <summary>Releases retained declarations; existing captures remain immutable.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_capturing) throw new InvalidOperationException("Collection factories cannot clear their cache.");
            _entries = new(StringComparer.Ordinal);
        }
    }

    private sealed record CapturedElement : WidgetElement
    {
        private readonly ViewNode _node;
        internal CapturedElement(WidgetElement source, ViewNode node) : base(source.Id)
        {
            _node = node;
            RequiredStyleClasses = source.RequiredStyleClasses;
            AuthorStyleClasses = source.AuthorStyleClasses;
        }
        internal override ViewNode ToProtocolNode() => _node with { Id = Id, StyleClasses = StyleClasses };
    }
}
