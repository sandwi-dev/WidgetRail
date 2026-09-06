using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// A resolution-independent responsive grid. The host derives the column
/// count from the final available logical-DIP width, the bounded minimum
/// column width, authored row/column gaps, and the optional column cap.
/// Children retain stable document order across every reflow.
/// </summary>
public sealed record GridElement : ContainerElement
{
    internal GridElement(
        string id,
        double minimumColumnWidth,
        int? maximumColumns,
        IReadOnlyList<WidgetElement> children) : base(RequireId(id), children)
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
        MinimumColumnWidth = minimumColumnWidth;
        MaximumColumns = maximumColumns;
        RequiredStyleClasses = ["wrail-responsive-grid"];
    }

    public double MinimumColumnWidth { get; init; }
    public int? MaximumColumns { get; init; }
    public new GridElement InputScope(string scopeId) => this with
    {
        InputScopeId = RequireId(scopeId),
    };

    public new GridElement RememberChildFocus(string initialChildFocusId) => this with
    {
        InitialChildFocusId = RequireInitialChildFocusId(initialChildFocusId),
    };

    public new GridElement Shortcut(
        ControllerButton button,
        string actionId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed,
        ControllerActionRepeatPolicy repeatPolicy = ControllerActionRepeatPolicy.None) => this with
        {
            Shortcuts =
            [
                .. Shortcuts,
                new ControllerShortcut(button, RequireId(actionId), phase, repeatPolicy),
            ],
        };

    public new GridElement Shortcut(
        ControllerButton button,
        string actionId,
        string label,
        ControllerEventPhase phase = ControllerEventPhase.Pressed,
        ControllerActionRepeatPolicy repeatPolicy = ControllerActionRepeatPolicy.None) => this with
        {
            Shortcuts =
            [
                .. Shortcuts,
                new ControllerShortcut(button, RequireId(actionId), phase, repeatPolicy, label),
            ],
        };

    internal override ViewNode ToProtocolNode() => ToContainerProtocolNode(ViewNodeKind.Grid) with
    {
        GridMinimumColumnWidth = MinimumColumnWidth,
        GridMaximumColumns = MaximumColumns,
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
