using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

/// <summary>
/// A resolution-independent responsive grid. The host derives the column
/// count from the final available logical-DIP width, the bounded minimum
/// column width, authored row/column gaps, and the optional column cap.
/// Children retain stable document order across every reflow.
/// </summary>
public sealed record GridElement : WidgetElement
{
    internal GridElement(
        string id,
        double minimumColumnWidth,
        int? maximumColumns,
        IReadOnlyList<WidgetElement> children) : base(RequireId(id))
    {
        StableIdentifier.Validate(id, nameof(id));
        if (!double.IsFinite(minimumColumnWidth) ||
            minimumColumnWidth < ProtocolConstants.MinimumGridColumnWidth ||
            minimumColumnWidth > ProtocolConstants.MaximumGridColumnWidth)
            throw new ArgumentOutOfRangeException(
                nameof(minimumColumnWidth),
                $"Minimum column width must be finite and between {ProtocolConstants.MinimumGridColumnWidth} and {ProtocolConstants.MaximumGridColumnWidth} DIPs.");
        if (maximumColumns is < 1 or > ProtocolConstants.MaximumGridColumns)
            throw new ArgumentOutOfRangeException(
                nameof(maximumColumns),
                $"Maximum columns must be between 1 and {ProtocolConstants.MaximumGridColumns}.");
        ArgumentNullException.ThrowIfNull(children);
        if (children.Any(child => child is null))
            throw new ArgumentException("Grid children cannot contain null values.", nameof(children));

        MinimumColumnWidth = minimumColumnWidth;
        MaximumColumns = maximumColumns;
        Children = children.ToArray();
        StyleClasses = ["gbar-responsive-grid"];
    }

    public double MinimumColumnWidth { get; init; }
    public int? MaximumColumns { get; init; }
    public IReadOnlyList<WidgetElement> Children { get; init; }
    public string? InputScopeId { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];

    public GridElement InputScope(string scopeId) => this with
    {
        InputScopeId = RequireId(scopeId),
    };

    public GridElement Shortcut(
        ControllerButton button,
        string actionId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) => this with
        {
            Shortcuts =
            [
                .. Shortcuts,
                new ControllerShortcut(button, RequireId(actionId), phase),
            ],
        };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Grid,
        GridMinimumColumnWidth = MinimumColumnWidth,
        GridMaximumColumns = MaximumColumns,
        InputScopeId = InputScopeId,
        Shortcuts = Shortcuts,
        StyleClasses = StyleClasses,
        Children = Children.Select(child => child.ToProtocolNode()).ToArray(),
    };
}

public static partial class UI
{
    /// <summary>
    /// Creates a responsive grid whose column count is recomputed by the host
    /// from the surface's actual logical-DIP width. An empty grid is valid;
    /// children may be any normal widget elements, including focusable controls.
    /// </summary>
    public static GridElement ResponsiveGrid(
        string id,
        double minimumColumnWidth,
        int? maximumColumns = null,
        params WidgetElement[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return new GridElement(id, minimumColumnWidth, maximumColumns, children.ToArray());
    }
}
