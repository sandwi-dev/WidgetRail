using System.Text.Json;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.GbarCli;

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
