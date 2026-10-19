namespace WidgetRail.WidgetProtocol;

public enum AnchorPlacement { Auto, Right, Left, Below, Above }

/// <summary>A widget-local anchor. Indexed anchors retain exact occurrence authority, never an index alone.</summary>
public sealed record PresentationAnchor(string? ElementId = null, IndexedCollectionFocusTarget? IndexedItem = null);

/// <summary>Preferred geometry; the host flips or falls back to a centered, viewport-bounded panel.</summary>
public sealed record AnchoredPresentation(PresentationAnchor Anchor,
    double Width = 360, double Height = 280, AnchorPlacement Placement = AnchorPlacement.Auto)
{
    /// <summary>Fit initial content height up to Height. Width stays stable. The host remeasures
    /// for viewport/text-size changes, not late updates to the same inspection. Requires protocol 93.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SizeToContentHeight { get; init; }
}

public static class AnchoredPresentationContract
{
    public static void Validate(AnchoredPresentation value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Anchor is null || (value.Anchor.ElementId is null) == (value.Anchor.IndexedItem is null) ||
            !Enum.IsDefined(value.Placement) || !double.IsFinite(value.Width) || value.Width is < 160 or > 960 ||
            !double.IsFinite(value.Height) || value.Height is < 96 or > 800)
            throw new ArgumentException("Choose one anchor and bounded presentation dimensions.", nameof(value));
        if (value.Anchor.ElementId is { } id && !ProtocolValidationIdentifierContext.IsSafeIdentifier(id))
            throw new ArgumentException("Invalid presentation anchor identifier.", nameof(value));
        if (value.Anchor.IndexedItem is { } item) IndexedCollectionContract.ValidateFocusTargetShape(item, item.CollectionId);
    }
}
