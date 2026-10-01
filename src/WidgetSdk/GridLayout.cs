using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Factories for bounded native Grid tracks in logical DIPs or star weights.</summary>
public static class GridTrack
{
    public static GridTrackDefinition Auto(double minimum = 0, double? maximum = null) => Create(GridTrackSizing.Auto, 1, minimum, maximum);
    public static GridTrackDefinition Pixel(double value, double minimum = 0, double? maximum = null) => Create(GridTrackSizing.Pixel, value, minimum, maximum);
    public static GridTrackDefinition Star(double weight = 1, double minimum = 0, double? maximum = null) => Create(GridTrackSizing.Star, weight, minimum, maximum);

    private static GridTrackDefinition Create(GridTrackSizing sizing, double value, double minimum, double? maximum)
    {
        var track = new GridTrackDefinition { Sizing = sizing, Value = value, Minimum = minimum, Maximum = maximum };
        Validate(track);
        return track;
    }

    internal static void Validate(GridTrackDefinition track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!Enum.IsDefined(track.Sizing)) throw new ArgumentOutOfRangeException(nameof(track));
        if (!double.IsFinite(track.Value) || (track.Sizing switch
            {
                GridTrackSizing.Auto => track.Value != 1,
                GridTrackSizing.Pixel => track.Value < 0 || track.Value > ProtocolConstants.MaximumGridLayoutLength,
                GridTrackSizing.Star => track.Value <= 0 || track.Value > ProtocolConstants.MaximumGridStarWeight,
                _ => true,
            })) throw new ArgumentOutOfRangeException(nameof(track), "Track value is outside its sizing bounds.");
        ValidateLength(track.Minimum, nameof(track));
        if (track.Maximum is { } maximum)
        {
            ValidateLength(maximum, nameof(track));
            if (maximum < track.Minimum) throw new ArgumentOutOfRangeException(nameof(track), "Maximum must be at least Minimum.");
        }
    }

    internal static void ValidateLength(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > ProtocolConstants.MaximumGridLayoutLength)
            throw new ArgumentOutOfRangeException(name, $"Expected finite DIPs from 0 to {ProtocolConstants.MaximumGridLayoutLength}.");
    }
}

/// <summary>An explicit native Grid. Empty axes use a single implicit star track.</summary>
public sealed record GridLayoutElement : ContainerElement
{
    internal GridLayoutElement(string id, IReadOnlyList<GridTrackDefinition> rows,
        IReadOnlyList<GridTrackDefinition> columns, IReadOnlyList<WidgetElement> children) : base(id, children)
    {
        StableIdentifier.Validate(id, nameof(id));
        ValidateTracks(rows, nameof(rows));
        ValidateTracks(columns, nameof(columns));
        Layout = new() { Rows = rows, Columns = columns };
    }

    public GridLayoutDefinition Layout { get; init; }

    public GridLayoutElement Spacing(double row, double column)
    {
        GridTrack.ValidateLength(row, nameof(row));
        GridTrack.ValidateLength(column, nameof(column));
        return this with { Layout = Layout with { RowSpacing = row, ColumnSpacing = column } };
    }

    public new GridLayoutElement InputScope(string scopeId) => this with { InputScopeId = RequireId(scopeId) };
    public new GridLayoutElement RememberChildFocus(string initialChildFocusId) => this with { InitialChildFocusId = RequireInitialChildFocusId(initialChildFocusId) };
    public new GridLayoutElement Shortcut(ControllerButton button, string actionId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed, ControllerActionRepeatPolicy repeatPolicy = ControllerActionRepeatPolicy.None) =>
        this with { Shortcuts = [.. Shortcuts, new(button, RequireId(actionId), phase, repeatPolicy)] };
    public new GridLayoutElement Shortcut(ControllerButton button, string actionId, string label,
        ControllerEventPhase phase = ControllerEventPhase.Pressed, ControllerActionRepeatPolicy repeatPolicy = ControllerActionRepeatPolicy.None) =>
        this with { Shortcuts = [.. Shortcuts, new(button, RequireId(actionId), phase, repeatPolicy, label)] };

    internal override ViewNode ToProtocolNode() => ToContainerProtocolNode(ViewNodeKind.Grid) with { GridLayout = Layout };

    private static void ValidateTracks(IReadOnlyList<GridTrackDefinition> tracks, string name)
    {
        ArgumentNullException.ThrowIfNull(tracks, name);
        if (tracks.Count > ProtocolConstants.MaximumGridLayoutTracks) throw new ArgumentOutOfRangeException(name);
        foreach (var track in tracks) GridTrack.Validate(track);
    }
}

/// <summary>A serialization-only attached placement; it adds no layout, input or focus node.</summary>
public sealed record GridCellElement : WidgetElement
{
    internal GridCellElement(WidgetElement child, GridCellPlacement placement) : base(child.Id)
    {
        Child = child;
        Placement = placement;
        RequiredStyleClasses = child.RequiredStyleClasses;
        AuthorStyleClasses = child.AuthorStyleClasses;
    }
    public WidgetElement Child { get; init; }
    public GridCellPlacement Placement { get; init; }
    internal override ViewNode ToProtocolNode() => Child.ToProtocolNode() with { GridCell = Placement, StyleClasses = StyleClasses };
}

public static partial class UI
{
    public static GridLayoutElement Grid(string id, IReadOnlyList<GridTrackDefinition> rows,
        IReadOnlyList<GridTrackDefinition> columns, params WidgetElement[] children) => new(id, rows, columns, children);
}
