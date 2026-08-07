using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

public abstract record WidgetElement(string Id)
{
    public IReadOnlyList<string> StyleClasses { get; init; } = [];
    internal abstract ViewNode ToProtocolNode();

    protected static string RequireId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }
}

public sealed record StackElement : WidgetElement
{
    internal StackElement(string id, IReadOnlyList<WidgetElement> children) : base(RequireId(id)) => Children = children;
    public IReadOnlyList<WidgetElement> Children { get; init; }
    public string? InputScopeId { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];
    public StackElement InputScope(string scopeId) => this with { InputScopeId = RequireId(scopeId) };
    public StackElement Shortcut(
        ControllerButton button,
        string actionId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) => this with
        {
            Shortcuts = [.. Shortcuts, new ControllerShortcut(button, RequireId(actionId), phase)],
        };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Stack,
        StyleClasses = StyleClasses,
        InputScopeId = InputScopeId,
        Shortcuts = Shortcuts,
        Children = Children.Select(child => child.ToProtocolNode()).ToArray(),
    };
}

public sealed record RowElement : WidgetElement
{
    internal RowElement(string id, IReadOnlyList<WidgetElement> children) : base(RequireId(id)) => Children = children;
    public IReadOnlyList<WidgetElement> Children { get; init; }
    public string? InputScopeId { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];
    public RowElement InputScope(string scopeId) => this with { InputScopeId = RequireId(scopeId) };
    public RowElement Shortcut(
        ControllerButton button,
        string actionId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) => this with
        {
            Shortcuts = [.. Shortcuts, new ControllerShortcut(button, RequireId(actionId), phase)],
        };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Row,
        StyleClasses = StyleClasses,
        InputScopeId = InputScopeId,
        Shortcuts = Shortcuts,
        Children = Children.Select(child => child.ToProtocolNode()).ToArray(),
    };
}

/// <summary>
/// A bounded host-owned scroll viewport. Controller focus is automatically
/// revealed and the host restores the offset while this stable ID remains in
/// the same widget instance.
/// </summary>
public sealed record ScrollElement : WidgetElement
{
    internal ScrollElement(string id, ScrollAxis axis, IReadOnlyList<WidgetElement> children)
        : base(RequireId(id))
    {
        if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
        Axis = axis;
        Children = children;
    }

    public ScrollAxis Axis { get; init; }
    public IReadOnlyList<WidgetElement> Children { get; init; }
    public string? InputScopeId { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];
    public ScrollElement InputScope(string scopeId) => this with { InputScopeId = RequireId(scopeId) };
    public ScrollElement Shortcut(
        ControllerButton button,
        string actionId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) => this with
        {
            Shortcuts = [.. Shortcuts, new ControllerShortcut(button, RequireId(actionId), phase)],
        };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Scroll,
        ScrollAxis = Axis,
        StyleClasses = StyleClasses,
        InputScopeId = InputScopeId,
        Shortcuts = Shortcuts,
        Children = Children.Select(child => child.ToProtocolNode()).ToArray(),
    };
}

public sealed record TextElement : WidgetElement
{
    internal TextElement(string id, string text, string? accessibilityLabel) : base(RequireId(id))
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        AccessibilityLabel = accessibilityLabel;
    }

    public string Text { get; init; }
    public string? AccessibilityLabel { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Text,
        Text = Text,
        AccessibilityLabel = AccessibilityLabel,
        StyleClasses = StyleClasses,
    };
}

public sealed record ButtonElement : WidgetElement
{
    internal ButtonElement(string id, string label, string actionId) : base(RequireId(id))
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        ActionId = RequireId(actionId);
    }

    public string Label { get; init; }
    public string ActionId { get; init; }
    public string? AccessibilityLabel { get; init; }
    public WidgetGlyph? Glyph { get; init; }
    public bool? IsDisabled { get; init; }
    public bool? IsSelected { get; init; }
    public bool? IsBusy { get; init; }
    public FocusNeighbors? FocusNeighbors { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];

    public ButtonElement FocusUp(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Up = RequireId(id) } };
    public ButtonElement FocusDown(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Down = RequireId(id) } };
    public ButtonElement FocusLeft(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Left = RequireId(id) } };
    public ButtonElement FocusRight(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Right = RequireId(id) } };
    public ButtonElement Disabled(bool disabled = true) => this with { IsDisabled = disabled ? true : null };
    public ButtonElement Selected(bool selected = true) => this with { IsSelected = selected ? true : null };
    public ButtonElement Busy(bool busy = true) => this with { IsBusy = busy ? true : null };
    /// <summary>
    /// Selects a host-rendered semantic icon. Widgets cannot provide arbitrary
    /// vector paths, fonts, or executable drawing code.
    /// </summary>
    public ButtonElement Icon(WidgetGlyph glyph, string? accessibilityLabel = null) => this with
    {
        Glyph = glyph,
        AccessibilityLabel = accessibilityLabel ?? AccessibilityLabel,
    };

    public ButtonElement Shortcut(
        ControllerButton button,
        ControllerEventPhase phase = ControllerEventPhase.Pressed,
        string? actionId = null) => this with
        {
            Shortcuts = [.. Shortcuts, new ControllerShortcut(button, actionId ?? ActionId, phase)],
        };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Button,
        Text = Label,
        AccessibilityLabel = AccessibilityLabel,
        Glyph = Glyph,
        ActionId = ActionId,
        IsDisabled = IsDisabled,
        IsSelected = IsSelected,
        IsBusy = IsBusy,
        Focus = FocusNeighbors,
        Shortcuts = Shortcuts,
        StyleClasses = StyleClasses,
    };
}

public sealed record ProgressElement : WidgetElement
{
    internal ProgressElement(string id, double value, double maximum, string? accessibilityLabel) : base(RequireId(id))
    {
        Value = value;
        Maximum = maximum;
        AccessibilityLabel = accessibilityLabel;
    }

    public double Value { get; init; }
    public double Maximum { get; init; }
    public string? AccessibilityLabel { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Progress,
        Value = Value,
        Maximum = Maximum,
        AccessibilityLabel = AccessibilityLabel,
        StyleClasses = StyleClasses,
    };
}

public sealed record SpacerElement : WidgetElement
{
    internal SpacerElement(string id) : base(RequireId(id)) { }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Spacer,
        StyleClasses = StyleClasses,
    };
}

public sealed record ImageElement : WidgetElement
{
    internal ImageElement(string id, string source, string accessibilityLabel, ImageFit fit) : base(RequireId(id))
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        AccessibilityLabel = accessibilityLabel ?? throw new ArgumentNullException(nameof(accessibilityLabel));
        Fit = fit;
    }

    public string Source { get; init; }
    public string AccessibilityLabel { get; init; }
    public ImageFit Fit { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Image,
        ImageSource = Source,
        ImageFit = Fit,
        AccessibilityLabel = AccessibilityLabel,
        StyleClasses = StyleClasses,
    };
}

public sealed record IconElement : WidgetElement
{
    internal IconElement(string id, WidgetGlyph glyph, string accessibilityLabel) : base(RequireId(id))
    {
        Glyph = glyph;
        AccessibilityLabel = accessibilityLabel ?? throw new ArgumentNullException(nameof(accessibilityLabel));
    }

    public WidgetGlyph Glyph { get; init; }
    public string AccessibilityLabel { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Icon,
        Glyph = Glyph,
        AccessibilityLabel = AccessibilityLabel,
        StyleClasses = StyleClasses,
    };
}
