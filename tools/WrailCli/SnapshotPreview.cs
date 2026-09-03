using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WrailCli;

public static class SnapshotPreview
{
    public static string Format(ViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine($"Widget {snapshot.WidgetInstanceId} | protocol {snapshot.ProtocolVersion} | sequence {snapshot.Sequence}");
        writer.WriteLine($"Initial focus: {snapshot.InitialFocusId ?? "(automatic)"}");
        writer.WriteLine($"Active input scope: {snapshot.ActiveInputScopeId}");
        if (snapshot.QuickActions.Count != 0)
            writer.WriteLine($"Dashboard shortcuts: [{string.Join(", ", snapshot.QuickActions.Select(item => $"{item.Button}:{item.ActionId}"))}]");
        WriteNode(writer, snapshot.Root, "", true, snapshot.InitialFocusId);
        return writer.ToString().TrimEnd();
    }

    internal static string FormatPinnedLayouts(ViewSnapshot snapshot, string selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection);
        try
        {
            _ = SnapshotJson.Serialize(snapshot);
        }
        catch (ProtocolValidationException exception)
        {
            var first = exception.Errors[0];
            throw new CliOperationException(
                $"Snapshot is invalid at {first.Path} ({first.Code}): {first.Message}",
                exception);
        }

        IReadOnlyList<PinnedPresentationLayout> selected;
        if (string.Equals(selection, "@all", StringComparison.Ordinal))
        {
            selected = snapshot.PinnedLayouts;
        }
        else
        {
            var layout = snapshot.PinnedLayouts.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, selection, StringComparison.Ordinal));
            if (layout is null)
            {
                var available = snapshot.PinnedLayouts.Count == 0
                    ? "none"
                    : string.Join(", ", snapshot.PinnedLayouts.Select(candidate => candidate.Id));
                throw new CliUsageException(
                    $"Pinned layout '{selection}' was not declared. Available: {available}.");
            }
            selected = [layout];
        }

        var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine(
            $"Pinned layout preview | widget {snapshot.WidgetInstanceId} | sequence {snapshot.Sequence}");
        if (selected.Count == 0)
        {
            writer.WriteLine("Pinned layouts: (none declared)");
        }
        else
        {
            foreach (var layout in selected)
                WritePinnedLayout(writer, layout);
        }

        foreach (var warning in FindEquivalentRootWarnings(snapshot))
            writer.WriteLine(warning);
        return writer.ToString().TrimEnd();
    }

    private static void WritePinnedLayout(TextWriter writer, PinnedPresentationLayout layout)
    {
        writer.WriteLine($"Layout {layout.Id} | {layout.Name}");
        writer.WriteLine(
            $"  Surface: mode={layout.Surface.Mode} width={layout.Surface.WidthMode} height={layout.Surface.HeightMode}");
        writer.WriteLine($"  Preferred extent: {FormatExtent(
            layout.Surface.PreferredWidth, layout.Surface.PreferredHeight)}");
        writer.WriteLine($"  Minimum extent: {FormatExtent(
            layout.Surface.MinimumWidth, layout.Surface.MinimumHeight)}");
        writer.WriteLine($"  Active input scope: {layout.ActiveInputScopeId ?? "(none)"}");
        writer.WriteLine($"  Initial focus: {layout.InitialFocusId ?? "(automatic)"}");
        if (layout.Root is null)
        {
            writer.WriteLine("  Projection root: (not authored)");
            return;
        }
        writer.WriteLine("  Projection root:");
        WriteNode(writer, layout.Root, "  ", true, layout.InitialFocusId);
    }

    private static IEnumerable<string> FindEquivalentRootWarnings(ViewSnapshot snapshot)
    {
        var canonicalRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var layout in snapshot.PinnedLayouts)
        {
            if (layout.Root is null) continue;
            var canonical = Convert.ToBase64String(SnapshotJson.Serialize(snapshot with
            {
                Sequence = 0,
                ActiveInputScopeId = layout.ActiveInputScopeId!,
                InitialFocusId = layout.InitialFocusId,
                QuickActions = [],
                PinnedLayouts = [],
                Surface = null,
                Root = layout.Root,
            }));
            if (canonicalRoots.TryGetValue(canonical, out var existing))
            {
                yield return
                    $"Warning: pinned layouts '{existing}' and '{layout.Id}' have semantically identical roots.";
            }
            else
            {
                canonicalRoots.Add(canonical, layout.Id);
            }
        }
    }

    private static string FormatExtent(double? width, double? height) =>
        width is { } actualWidth && height is { } actualHeight
            ? $"{actualWidth:R} x {actualHeight:R} DIP"
            : "(host selected)";

    private static void WriteNode(TextWriter writer, ViewNode node, string prefix, bool last, string? focusedId)
    {
        writer.Write(prefix);
        writer.Write(last ? "└─ " : "├─ ");
        writer.Write(node.Id == focusedId ? "▶ " : "  ");
        writer.Write(node.Kind);
        writer.Write(" #");
        writer.Write(node.Id);
        if (!string.IsNullOrWhiteSpace(node.Text)) writer.Write($" {Quote(node.Text)}");
        if (!string.IsNullOrWhiteSpace(node.ActionId)) writer.Write($" action={node.ActionId}");
        if (!string.IsNullOrWhiteSpace(node.InputScopeId)) writer.Write($" scope={node.InputScopeId}");
        if (!string.IsNullOrWhiteSpace(node.FocusPersistenceId))
            writer.Write($" focusPersistence={node.FocusPersistenceId}");
        if (!string.IsNullOrWhiteSpace(node.CollectionAnchorKey))
            writer.Write($" collectionAnchor={node.CollectionAnchorKey}");
        if (!string.IsNullOrWhiteSpace(node.CollectionItemKey))
            writer.Write($" collectionItem={node.CollectionItemKey}");
        if (!string.IsNullOrWhiteSpace(node.ArtworkHandle))
            writer.Write($" artworkHandle={node.ArtworkHandle}");
        if (!string.IsNullOrWhiteSpace(node.MediaSessionId))
            writer.Write($" media={node.MediaSessionId} placeholder=native");
        if (!string.IsNullOrWhiteSpace(node.AccessibilityLabel))
            writer.Write($" a11y={Quote(node.AccessibilityLabel)}");
        if (!string.IsNullOrWhiteSpace(node.AccessibilityValue))
            writer.Write($" a11yValue={Quote(node.AccessibilityValue)}");
        if (node.Value is not null) writer.Write($" value={node.Value}/{node.Maximum}");
        if (node.IsDisabled == true) writer.Write(" disabled");
        if (node.IsSelected == true) writer.Write(" selected");
        if (node.IsBusy == true) writer.Write(" busy");
        if (node.Focus is { } focus)
        {
            var edges = new[]
            {
                ("up", focus.Up), ("down", focus.Down),
                ("left", focus.Left), ("right", focus.Right),
            }.Where(item => item.Item2 is not null)
                .Select(item => $"{item.Item1}:{item.Item2}");
            var rendered = string.Join(",", edges);
            if (rendered.Length != 0) writer.Write($" focus=[{rendered}]");
        }
        if (node.Shortcuts.Count != 0)
            writer.Write($" shortcuts=[{string.Join(", ", node.Shortcuts.Select(item => $"{item.Button}:{item.ActionId}"))}]");
        writer.WriteLine();

        var childPrefix = prefix + (last ? "   " : "│  ");
        for (var index = 0; index < node.Children.Count; index++)
            WriteNode(writer, node.Children[index], childPrefix, index == node.Children.Count - 1, focusedId);
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
}
