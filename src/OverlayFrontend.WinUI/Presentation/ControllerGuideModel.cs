using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// Labels are presentation only. Invoking a hint routes the button again through
// the current host/presenter input path, never through a captured action id.
internal sealed record ControllerGuideHint(ControllerPrompt Prompt, string Label,
    ControllerButton? Button = null, bool Required = false, string? Group = null);

internal static class ControllerGuideModel
{
    internal static IReadOnlyList<ControllerGuideHint> TrayHints(bool reordering) => reordering
        ? (ControllerGuideHint[])[new(ControllerPrompt.DPadHorizontal, "Move widget", Required: true), new(ControllerPrompt.A, "Done", ControllerButton.A),
            new(ControllerPrompt.B, "Done", ControllerButton.B, Required: true), new(ControllerPrompt.Y, "Done", ControllerButton.Y)]
        : (ControllerGuideHint[])[new(ControllerPrompt.A, "Open widget", ControllerButton.A, Required: true),
            new(ControllerPrompt.Y, "Reorder · hold to restart", ControllerButton.Y),
            new(ControllerPrompt.Menu, "Commands", ControllerButton.Menu), new(ControllerPrompt.B, "Close", ControllerButton.B, Required: true)];
    private static readonly ControllerButton[] order = [ControllerButton.X, ControllerButton.LeftTrigger,
        ControllerButton.RightTrigger, ControllerButton.LeftBumper, ControllerButton.RightBumper,
        ControllerButton.Y, ControllerButton.Menu, ControllerButton.View, ControllerButton.LeftStick, ControllerButton.RightStick];

    internal static IReadOnlyList<ControllerGuideHint> Resolve(IReadOnlyList<ViewNode> path,
        IReadOnlySet<ControllerButton> contextButtons, bool activation, IReadOnlySet<ControllerButton>? unavailableContextButtons = null)
    {
        var hints = new List<ControllerGuideHint>();
        foreach (var button in order)
        {
            if (unavailableContextButtons?.Contains(button) == true) continue;
            if (contextButtons.Contains(button)) { hints.Add(new(Prompt(button), "Options", button)); continue; }
            var resolved = ControllerShortcutResolver.ResolvePath(path, button, ControllerEventPhase.Pressed);
            if (resolved.Status != ControllerShortcutResolutionStatus.Resolved) continue;
            var label = Normalize(!string.IsNullOrWhiteSpace(resolved.Shortcut!.Label) ? resolved.Shortcut.Label :
                !string.IsNullOrWhiteSpace(resolved.Owner!.Text) ? resolved.Owner.Text : resolved.Owner.AccessibilityLabel);
            if (label.Length == 0) continue;
            hints.Add(new(Prompt(button), label, button, Group: button switch
            {
                ControllerButton.LeftTrigger or ControllerButton.RightTrigger => "triggers",
                ControllerButton.LeftBumper or ControllerButton.RightBumper => "bumpers", _ => null,
            }));
        }
        if (activation) hints.Add(new(ControllerPrompt.A, "Select", ControllerButton.A));
        return hints;
    }

    // Match the shell's routing order: tray ownership precedes recovery, and
    // interactive recovery consumes all widget input until it is dismissed.
    // Null selects the normal/reorder tray guide; an empty list still keeps Back/Close.
    internal static IReadOnlyList<ControllerGuideHint>? ResolveShellHints(bool interactive,
        bool recoveryVisible, bool retryVisible, bool trayMenuOpen,
        Func<IReadOnlyList<ControllerGuideHint>> captureWidget)
    {
        if (!interactive) return trayMenuOpen ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A)] : null;
        if (recoveryVisible) return retryVisible ? (ControllerGuideHint[])[new(ControllerPrompt.A, "Retry", ControllerButton.A)] : [];
        return captureWidget();
    }

    internal static IReadOnlyList<ControllerGuideHint> WithHost(IReadOnlyList<ControllerGuideHint> hints) =>
        (ControllerGuideHint[])[.. hints,
            .. (hints.Any(hint => hint.Prompt == ControllerPrompt.B) ? [] : new ControllerGuideHint[] { new(ControllerPrompt.B, "Back", ControllerButton.B, Required: true) }),
            .. (hints.Any(hint => hint.Prompt == ControllerPrompt.Guide) ? [] : new ControllerGuideHint[] { new(ControllerPrompt.Guide, "Close", Required: true) })];

    // Native view supplies actual desired widths. Never approximate string width
    // or truncate a label; paired section actions are admitted together.
    internal static IReadOnlyList<int> Fit(IReadOnlyList<ControllerGuideHint> hints, IReadOnlyList<double> widths,
        double availableWidth, double spacing)
    {
        var selected = Enumerable.Range(0, hints.Count).Where(i => hints[i].Required).ToList();
        double Width(IEnumerable<int> indices) { var values = indices.ToArray(); return values.Sum(i => widths[i]) + Math.Max(0, values.Length - 1) * spacing; }
        var considered = new HashSet<int>(selected);
        for (var i = 0; i < hints.Count; ++i)
        {
            if (!considered.Add(i)) continue;
            var group = hints[i].Group is { } key
                ? Enumerable.Range(i, hints.Count - i).Where(j => hints[j].Group == key && !hints[j].Required).ToArray() : [i];
            foreach (var index in group) considered.Add(index);
            if (Width(selected.Concat(group)) <= availableWidth) selected.AddRange(group);
        }
        selected.Sort();
        return selected;
    }

    internal static string Normalize(string? value) => string.Join(" ", (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    internal static ControllerPrompt Prompt(ControllerButton button) => button switch
    {
        ControllerButton.LeftStick => ControllerPrompt.LeftStickPress,
        ControllerButton.RightStick => ControllerPrompt.RightStickPress,
        _ => Enum.Parse<ControllerPrompt>(button.ToString()),
    };
}
