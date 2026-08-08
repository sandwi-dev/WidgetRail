using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

public static partial class UI
{
    /// <summary>
    /// Maximum diagnostic or command text accepted by <see cref="CodeText"/>.
    /// This intentionally matches the bounded protocol string limit.
    /// </summary>
    public const int MaximumCodeTextCharacters = ProtocolConstants.MaximumStringLength;

    /// <summary>
    /// Creates presentational code, command, or diagnostic text with semantic
    /// monospace styling. The result owns no action, focus stop, shortcut, or
    /// input scope. It preserves whitespace and is deliberately copy-neutral:
    /// authors should expose a separate explicit copy action when copying is a
    /// required workflow rather than making text selection part of controller
    /// navigation.
    /// </summary>
    public static TextElement CodeText(
        string text,
        string id,
        string? accessibilityLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        StableIdentifier.Validate(id, nameof(id));
        if (text.Length > MaximumCodeTextCharacters)
            throw new ArgumentException(
                $"Code text may not exceed {MaximumCodeTextCharacters} characters.",
                nameof(text));
        if (accessibilityLabel is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accessibilityLabel);
            if (accessibilityLabel.Length > ProtocolConstants.MaximumStringLength)
                throw new ArgumentException(
                    $"Accessibility text may not exceed {ProtocolConstants.MaximumStringLength} characters.",
                    nameof(accessibilityLabel));
        }

        return new TextElement(id, text, accessibilityLabel ?? text)
        {
            StyleClasses = ["gbar-code-text"],
        };
    }
}
