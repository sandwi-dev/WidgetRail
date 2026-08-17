namespace WidgetRail.WidgetSdk;

/// <summary>Stable logical identity for one cursor-collection item.</summary>
public readonly record struct WidgetCollectionItemKey
{
    public WidgetCollectionItemKey(string value)
    {
        StableIdentifier.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>Opaque provider cursor. It is equality data, never an offset.</summary>
public readonly record struct WidgetCollectionCursor
{
    public WidgetCollectionCursor(string value)
    {
        StableIdentifier.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

/// <summary>
/// Opaque lazy artwork identity. It grants no URL, path, file, network, or
/// decode authority and is resolved only by the trusted host artwork service.
/// </summary>
public readonly record struct WidgetArtworkHandle
{
    public WidgetArtworkHandle(string value)
    {
        StableIdentifier.Validate(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
