using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Copies mutable declaration lists before retaining or publishing item trees.</summary>
internal static class WidgetDeclarationSnapshot
{
    internal static ViewNode Freeze(ViewNode node, int depth, ref int count)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Children is null || node.StyleClasses is null || node.Shortcuts is null || node.ContextActions is null || node.SelectOptions is null)
            throw new ArgumentException("Declaration collections cannot be null.");
        if (node.Children.Count > ProtocolConstants.MaximumNodeCount - count)
            throw new ArgumentException("Declaration exceeds the protocol tree bound.");
        if (depth > ProtocolConstants.MaximumTreeDepth || ++count > ProtocolConstants.MaximumNodeCount)
            throw new ArgumentException("Collection item declaration exceeds the protocol tree bound.");
        var children = new ViewNode[node.Children.Count];
        for (var index = 0; index < children.Length; index++)
            children[index] = Freeze(node.Children[index], depth + 1, ref count);
        return node with
        {
            Children = Array.AsReadOnly(children),
            StyleClasses = Array.AsReadOnly(node.StyleClasses.ToArray()),
            Shortcuts = Array.AsReadOnly(node.Shortcuts.ToArray()),
            ContextActions = Array.AsReadOnly(node.ContextActions.ToArray()),
            SelectOptions = Array.AsReadOnly(node.SelectOptions.ToArray()),
            FocusPresentation = node.FocusPresentation is { } focus ? Freeze(focus, depth + 1, ref count) : null,
            DefaultFocusPresentation = node.DefaultFocusPresentation is { } fallback ? Freeze(fallback, depth + 1, ref count) : null,
        };
    }

}
