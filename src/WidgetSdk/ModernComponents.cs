using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

public enum IconButtonVariant { Default, Primary, Danger, Quiet }
public enum IconButtonSize { Small, Medium, Large }
public enum CardVariant { Raised, Subtle, Transparent }
public enum StatusTone { Neutral, Info, Success, Warning, Danger }
public enum AlertTone { Info, Success, Warning, Danger }

/// <summary>A single optional action rendered by an alert or empty state.</summary>
public sealed record ComponentAction(string Label, string ActionId, WidgetGlyph? Glyph = null);

/// <summary>
/// Original controller-first composites built only from stable public protocol
/// nodes. Their gbar-* classes are semantic theme hooks, not fixed colors.
/// </summary>
public static partial class UI
{
    public static ButtonElement IconButton(
        WidgetGlyph glyph,
        string action,
        string id,
        string accessibilityLabel,
        IconButtonVariant variant = IconButtonVariant.Default,
        IconButtonSize size = IconButtonSize.Medium)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibilityLabel);
        EnsureDefined(variant, nameof(variant));
        EnsureDefined(size, nameof(size));
        return new ButtonElement(id, string.Empty, action)
        {
            Glyph = glyph,
            AccessibilityLabel = accessibilityLabel,
            StyleClasses =
            [
                "gbar-icon-button",
                $"gbar-icon-button--{Token(size)}",
                $"gbar-icon-button--{Token(variant)}",
            ],
        };
    }

    public static StackElement Card(string id, params WidgetElement[] children) =>
        Card(id, CardVariant.Raised, children);

    public static StackElement Card(
        string id,
        CardVariant variant,
        params WidgetElement[] children)
    {
        EnsureDefined(variant, nameof(variant));
        return new StackElement(id, CopyChildren(children))
        {
            StyleClasses = ["gbar-card", $"gbar-card--{Token(variant)}"],
        };
    }

    public static StackElement SectionHeader(
        string title,
        string id,
        string? eyebrow = null,
        string? description = null,
        WidgetElement? trailing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var contentId = StableIdentifier.Child(id, "content");
        var textId = StableIdentifier.Child(id, "text");
        var titleId = StableIdentifier.Child(id, "title");
        var text = new List<WidgetElement>();
        if (!string.IsNullOrWhiteSpace(eyebrow))
        {
            text.Add(new TextElement(StableIdentifier.Child(id, "eyebrow"), eyebrow, eyebrow)
            {
                StyleClasses = ["gbar-section-header__eyebrow"],
            });
        }
        text.Add(new TextElement(titleId, title, title)
        {
            StyleClasses = ["gbar-section-header__title"],
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Add(new TextElement(StableIdentifier.Child(id, "description"), description, description)
            {
                StyleClasses = ["gbar-section-header__description"],
            });
        }
        var content = new List<WidgetElement>
        {
            new StackElement(textId, text)
            {
                StyleClasses = ["gbar-section-header__text"],
            },
        };
        if (trailing is not null)
        {
            content.Add(new RowElement(StableIdentifier.Child(id, "trailing"), [trailing])
            {
                StyleClasses = ["gbar-section-header__trailing"],
            });
        }
        return new StackElement(id,
        [
            new RowElement(contentId, content)
            {
                StyleClasses = ["gbar-section-header__content"],
            },
        ])
        {
            StyleClasses = ["gbar-section-header"],
        };
    }

    public static RowElement StatusBadge(
        string label,
        StatusTone tone,
        string id,
        WidgetGlyph? glyph = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        EnsureDefined(tone, nameof(tone));
        var labelId = StableIdentifier.Child(id, "label");
        var semanticGlyph = glyph ?? tone switch
        {
            StatusTone.Success => WidgetGlyph.Check,
            StatusTone.Warning or StatusTone.Danger => WidgetGlyph.Warning,
            _ => (WidgetGlyph?)null,
        };
        var children = new List<WidgetElement>();
        if (semanticGlyph is { } resolved)
        {
            children.Add(new IconElement(StableIdentifier.Child(id, "icon"), resolved, $"{tone} status")
            {
                StyleClasses = ["gbar-badge__icon", $"gbar-badge__icon--{Token(tone)}"],
            });
        }
        children.Add(new TextElement(labelId, label, label)
        {
            StyleClasses = ["gbar-badge__label", $"gbar-badge__label--{Token(tone)}"],
        });
        return new RowElement(id, children)
        {
            StyleClasses = ["gbar-badge", $"gbar-badge--{Token(tone)}"],
        };
    }

    public static SpacerElement Divider(string id) => new(id)
    {
        StyleClasses = ["gbar-divider"],
    };

    public static StackElement Alert(
        string title,
        string message,
        AlertTone tone,
        string id,
        ComponentAction? action = null,
        WidgetGlyph? glyph = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        EnsureDefined(tone, nameof(tone));
        var semanticGlyph = glyph ?? tone switch
        {
            AlertTone.Success => WidgetGlyph.Check,
            AlertTone.Warning or AlertTone.Danger => WidgetGlyph.Warning,
            _ => (WidgetGlyph?)null,
        };
        return MessageSurface(
            "gbar-alert",
            title,
            message,
            id,
            semanticGlyph,
            $"{Token(tone)} alert",
            Token(tone),
            action);
    }

    public static StackElement EmptyState(
        string title,
        string message,
        string id,
        ComponentAction? action = null,
        WidgetGlyph glyph = WidgetGlyph.Connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return MessageSurface(
            "gbar-empty-state",
            title,
            message,
            id,
            glyph,
            "Empty state",
            null,
            action);
    }

    private static StackElement MessageSurface(
        string componentClass,
        string title,
        string message,
        string id,
        WidgetGlyph? glyph,
        string iconAccessibilityLabel,
        string? tone,
        ComponentAction? action)
    {
        var titleId = StableIdentifier.Child(id, "title");
        var messageId = StableIdentifier.Child(id, "message");
        var children = new List<WidgetElement>();
        if (glyph is { } semanticGlyph)
        {
            children.Add(new IconElement(
                StableIdentifier.Child(id, "icon"),
                semanticGlyph,
                iconAccessibilityLabel)
            {
                StyleClasses = tone is null
                    ? [$"{componentClass}__icon"]
                    : [$"{componentClass}__icon", $"{componentClass}__icon--{tone}"],
            });
        }
        children.AddRange(
        [
            new TextElement(titleId, title, title)
            {
                StyleClasses = [$"{componentClass}__title"],
            },
            new TextElement(messageId, message, message)
            {
                StyleClasses = [$"{componentClass}__message"],
            },
        ]);
        if (action is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action.Label);
            ArgumentException.ThrowIfNullOrWhiteSpace(action.ActionId);
            children.Add(new ButtonElement(
                StableIdentifier.Child(id, "action"),
                action.Label,
                action.ActionId)
            {
                Glyph = action.Glyph,
                AccessibilityLabel = action.Label,
                StyleClasses = [$"{componentClass}__action"],
            });
        }
        var classes = tone is null
            ? new[] { componentClass }
            : new[] { componentClass, $"{componentClass}--{tone}" };
        return new StackElement(id, children) { StyleClasses = classes };
    }

    private static string Token<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private static void EnsureDefined<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(parameterName);
    }
}
