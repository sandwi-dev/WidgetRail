using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CodeTextTests
{
    public static Task Run()
    {
        const string command = "  wrail diagnostics --format json\n  status: ready  ";
        var element = UI.CodeText(command, "diagnostics.command", "Diagnostic command and result")
            .AddClasses("diagnostics-command");
        var snapshot = new CodeTextWidget(element).RenderSnapshot("code-text.test", 1);
        var node = snapshot.Root;

        Equal(ViewNodeKind.Text, node.Kind);
        Equal("diagnostics.command", node.Id);
        Equal(command, node.Text);
        Equal("Diagnostic command and result", node.AccessibilityLabel);
        True(node.StyleClasses.SequenceEqual(["wrail-code-text", "diagnostics-command"]));
        True(!node.IsFocusable);
        True(node.ActionId is null && node.ValueChangedActionId is null);
        True(node.Focus is null && node.InputScopeId is null && node.Shortcuts.Count == 0);
        Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);

        var maximum = new string('x', UI.MaximumCodeTextCharacters);
        var maximumSnapshot = new CodeTextWidget(
            UI.CodeText(maximum, "diagnostics.maximum")).RenderSnapshot("code-text.max", 2);
        Equal(maximum, maximumSnapshot.Root.Text);
        Equal(maximum, maximumSnapshot.Root.AccessibilityLabel);
        Equal(0, ViewSnapshotValidator.Validate(maximumSnapshot).Count);

        Throws<ArgumentException>(() => UI.CodeText(" ", "diagnostics.empty"));
        Throws<ArgumentException>(() => UI.CodeText(
            new string('x', UI.MaximumCodeTextCharacters + 1), "diagnostics.too-long"));
        Throws<ArgumentException>(() => UI.CodeText(
            "ready", "diagnostics.bad", new string('a', ProtocolConstants.MaximumStringLength + 1)));
        Throws<ArgumentException>(() => UI.CodeText("ready", "invalid id"));
        return Task.CompletedTask;
    }

    private sealed class CodeTextWidget(TextElement root) : Widget
    {
        public override WidgetView Render() => new(root);
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
