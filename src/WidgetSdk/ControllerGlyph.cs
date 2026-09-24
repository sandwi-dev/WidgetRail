using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A presentational controller symbol. It never binds input or takes focus.</summary>
public sealed record ControllerGlyphElement : WidgetElement
{
    internal ControllerGlyphElement(ControllerPrompt prompt, string id, string? accessibilityLabel)
        : base(RequireId(id))
    {
        if (!Enum.IsDefined(prompt)) throw new ArgumentOutOfRangeException(nameof(prompt));
        Prompt = prompt;
        AccessibilityLabel = accessibilityLabel;
        RequiredStyleClasses = ["wrail-controller-glyph"];
    }

    public ControllerPrompt Prompt { get; init; }
    /// <summary>Optional descriptive context; omission lets the host name the current controller symbol.</summary>
    public string? AccessibilityLabel { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.ControllerGlyph,
        ControllerPrompt = Prompt,
        AccessibilityLabel = AccessibilityLabel,
        StyleClasses = StyleClasses,
    };
}

public static partial class UI
{
    /// <summary>Creates a themeable controller symbol without a visible action label.</summary>
    public static ControllerGlyphElement ControllerGlyph(
        ControllerPrompt prompt, string id, string? accessibilityLabel = null) =>
        new(prompt, id, accessibilityLabel);

    /// <summary>Creates a symbol for a button. LeftStick and RightStick mean stick presses.</summary>
    public static ControllerGlyphElement ControllerGlyph(
        ControllerButton button, string id, string? accessibilityLabel = null) =>
        new(PromptForButton(button), id, accessibilityLabel);

    /// <summary>Documents a controller action without binding it or taking focus.</summary>
    public static RowElement ControllerHint(ControllerPrompt prompt, string label, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return new RowElement(id,
        [
            ControllerGlyph(prompt, StableIdentifier.Child(id, "key")) with
            {
                RequiredStyleClasses = ["wrail-controller-glyph", "wrail-controller-hint__key"],
            },
            new TextElement(StableIdentifier.Child(id, "label"), label, label)
            {
                RequiredStyleClasses = ["wrail-controller-hint__label"],
            },
        ])
        {
            RequiredStyleClasses = ["wrail-controller-hint"],
        };
    }

    private static ControllerPrompt PromptForButton(ControllerButton button) => button switch
    {
        ControllerButton.A => ControllerPrompt.A,
        ControllerButton.B => ControllerPrompt.B,
        ControllerButton.X => ControllerPrompt.X,
        ControllerButton.Y => ControllerPrompt.Y,
        ControllerButton.LeftBumper => ControllerPrompt.LeftBumper,
        ControllerButton.RightBumper => ControllerPrompt.RightBumper,
        ControllerButton.LeftTrigger => ControllerPrompt.LeftTrigger,
        ControllerButton.RightTrigger => ControllerPrompt.RightTrigger,
        ControllerButton.LeftStick => ControllerPrompt.LeftStickPress,
        ControllerButton.RightStick => ControllerPrompt.RightStickPress,
        ControllerButton.DPadUp => ControllerPrompt.DPadUp,
        ControllerButton.DPadDown => ControllerPrompt.DPadDown,
        ControllerButton.DPadLeft => ControllerPrompt.DPadLeft,
        ControllerButton.DPadRight => ControllerPrompt.DPadRight,
        ControllerButton.View => ControllerPrompt.View,
        ControllerButton.Menu => ControllerPrompt.Menu,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };
}
