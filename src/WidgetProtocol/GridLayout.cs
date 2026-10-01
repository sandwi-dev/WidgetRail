using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Native Grid track sizing in logical DIPs or proportional star units.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GridTrackSizing>))]
public enum GridTrackSizing { Auto, Pixel, Star }

/// <summary>A bounded native row or column definition. Auto uses Value = 1.</summary>
public sealed record GridTrackDefinition
{
    [JsonConstructor]
    public GridTrackDefinition(GridTrackSizing sizing = GridTrackSizing.Star, double value = 1, double minimum = 0, double? maximum = null)
    { Sizing = sizing; Value = value; Minimum = minimum; Maximum = maximum; }
    public GridTrackSizing Sizing { get; init; } = GridTrackSizing.Star;
    public double Value { get; init; } = 1;
    public double Minimum { get; init; }
    public double? Maximum { get; init; }
}

/// <summary>Native Grid tracks and gaps. Empty axes use one implicit star track. Owns its definition lists.</summary>
public sealed record GridLayoutDefinition
{
    private IReadOnlyList<GridTrackDefinition> rows = [];
    private IReadOnlyList<GridTrackDefinition> columns = [];
    public IReadOnlyList<GridTrackDefinition> Rows
    {
        get => rows;
        init => rows = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public IReadOnlyList<GridTrackDefinition> Columns
    {
        get => columns;
        init => columns = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    public double RowSpacing { get; init; }
    public double ColumnSpacing { get; init; }
}

/// <summary>Attached placement on a direct child of an explicit Grid. Overlap is allowed.</summary>
public sealed record GridCellPlacement
{
    [JsonConstructor]
    public GridCellPlacement(int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1)
    { Row = row; Column = column; RowSpan = rowSpan; ColumnSpan = columnSpan; }
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
}

internal static class GridLayoutValidation
{
    internal static void Validate(ViewNode node, ViewNode? parent, string path,
        Action<string, string, string> add)
    {
        if (node.GridLayout is { } layout)
        {
            if (node.Kind != ViewNodeKind.Grid)
                add(path + ".gridLayout", "grid_layout_not_allowed", "Explicit grid layout requires a Grid node.");
            if (node.GridMinimumColumnWidth is not null || node.GridMaximumColumns is not null)
                add(path + ".gridLayout", "mixed_grid_layout", "Explicit and responsive grid declarations cannot be combined.");
            Tracks(layout.Rows, path + ".gridLayout.rows");
            Tracks(layout.Columns, path + ".gridLayout.columns");
            Dip(layout.RowSpacing, path + ".gridLayout.rowSpacing");
            Dip(layout.ColumnSpacing, path + ".gridLayout.columnSpacing");
        }
        if (node.GridCell is { } cell)
        {
            if (parent is not { Kind: ViewNodeKind.Grid, GridLayout: { } parentLayout })
                add(path + ".gridCell", "grid_cell_not_allowed", "Grid cell placement requires a direct explicit Grid parent.");
            else
            {
                Axis(cell.Row, cell.RowSpan, Math.Max(1, parentLayout.Rows?.Count ?? 0), path + ".gridCell.row");
                Axis(cell.Column, cell.ColumnSpan, Math.Max(1, parentLayout.Columns?.Count ?? 0), path + ".gridCell.column");
            }
        }

        void Tracks(IReadOnlyList<GridTrackDefinition>? tracks, string trackPath)
        {
            if (tracks is null) { add(trackPath, "required", "Grid track lists cannot be null."); return; }
            if (tracks.Count > ProtocolConstants.MaximumGridLayoutTracks)
                add(trackPath, "too_many_grid_tracks", $"An axis may declare at most {ProtocolConstants.MaximumGridLayoutTracks} tracks.");
            for (var i = 0; i < Math.Min(tracks.Count, ProtocolConstants.MaximumGridLayoutTracks); i++)
            {
                var itemPath = $"{trackPath}[{i}]";
                if (tracks[i] is not { } track) { add(itemPath, "required", "A grid track cannot be null."); continue; }
                if (!Enum.IsDefined(track.Sizing))
                    add(itemPath + ".sizing", "invalid_grid_track_sizing", "Grid track sizing must be Auto, Pixel or Star.");
                if (!double.IsFinite(track.Value) || (track.Sizing switch
                    {
                        GridTrackSizing.Auto => track.Value != 1,
                        GridTrackSizing.Pixel => track.Value < 0 || track.Value > ProtocolConstants.MaximumGridLayoutLength,
                        GridTrackSizing.Star => track.Value <= 0 || track.Value > ProtocolConstants.MaximumGridStarWeight,
                        _ => false,
                    }))
                    add(itemPath + ".value", "invalid_grid_track_value", "Auto uses value 1; Pixel uses bounded nonnegative DIPs; Star uses a bounded positive weight.");
                Dip(track.Minimum, itemPath + ".minimum");
                if (track.Maximum is { } maximum)
                {
                    Dip(maximum, itemPath + ".maximum");
                    if (maximum < track.Minimum)
                        add(itemPath + ".maximum", "invalid_grid_track_bounds", "Maximum must be at least Minimum.");
                }
            }
        }
        void Dip(double value, string valuePath)
        {
            if (!double.IsFinite(value) || value < 0 || value > ProtocolConstants.MaximumGridLayoutLength)
                add(valuePath, "invalid_grid_length", $"Grid lengths must be finite between 0 and {ProtocolConstants.MaximumGridLayoutLength} DIPs.");
        }
        void Axis(int index, int span, int count, string axisPath)
        {
            if (index < 0 || span < 1 || index >= count || span > count - index)
                add(axisPath, "invalid_grid_cell", "Grid index and span must fit the declared or implicit axis.");
        }
    }
}
