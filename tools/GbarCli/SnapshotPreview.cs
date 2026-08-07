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
        if (!string.IsNullOrWhiteSpace(node.Text)) writer.Write($" \"{node.Text}\"");
        if (!string.IsNullOrWhiteSpace(node.ActionId)) writer.Write($" action={node.ActionId}");
        if (node.Value is not null) writer.Write($" value={node.Value}/{node.Maximum}");
        if (node.Shortcuts.Count != 0)
            writer.Write($" shortcuts=[{string.Join(", ", node.Shortcuts.Select(item => $"{item.Button}:{item.ActionId}"))}]");
        writer.WriteLine();

        var childPrefix = prefix + (last ? "   " : "│  ");
        for (var index = 0; index < node.Children.Count; index++)
            WriteNode(writer, node.Children[index], childPrefix, index == node.Children.Count - 1, focusedId);
    }
}
