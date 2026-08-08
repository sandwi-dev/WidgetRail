using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

public enum IconButtonVariant { Default, Primary, Danger, Quiet }
public enum IconButtonSize { Small, Medium, Large }
public enum CardVariant { Raised, Subtle, Transparent }
public enum StatusTone { Neutral, Info, Success, Warning, Danger }
public enum AlertTone { Info, Success, Warning, Danger }
public enum ActionSheetItemTone { Default, Danger }

/// <summary>A single optional action rendered by an alert or empty state.</summary>
public sealed record ComponentAction(string Label, string ActionId, WidgetGlyph? Glyph = null);

/// <summary>A stable, controller-addressable option in a segmented tab row.</summary>
public sealed record SegmentedTab(
    string Id,
    string Label,
    string ActionId,
    string? AccessibilityLabel = null,
    bool IsDisabled = false);

/// <summary>A bounded, stable controller action presented in an action sheet.</summary>
public sealed record ActionSheetItem(
    string Id,
    string Label,
    string ActionId,
    WidgetGlyph? Glyph = null,
    string? AccessibilityLabel = null,
    ActionSheetItemTone Tone = ActionSheetItemTone.Default,
    bool IsDisabled = false,
    bool IsBusy = false);

/// <summary>
/// Original controller-first composites built only from stable public protocol
/// nodes. Their gbar-* classes are semantic theme hooks, not fixed colors.
/// </summary>
public static partial class UI
{
    /// <summary>The maximum number of actions accepted by one action sheet.</summary>
    public const int MaximumActionSheetItems = 32;

    /// <summary>
    /// Creates a responsive setting summary with exactly one controller focus
    /// stop. Descriptive text remains in its own vertical flow so long labels,
    /// values, and status copy can reflow without shrinking the 44-DIP action.
    /// The stable focus ID is <c>{id}.action</c>.
    /// </summary>
    public static StackElement SettingsRow(
        string label,
        ComponentAction action,
        string id,
        string? description = null,
        string? value = null,
        string? status = null,
        StatusTone statusTone = StatusTone.Neutral,
        bool isDisabled = false,
        bool isBusy = false,
        WidgetGlyph? glyph = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Label);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.ActionId);
        EnsureDefined(statusTone, nameof(statusTone));
        StableIdentifier.Validate(id, nameof(id));

        var content = new List<WidgetElement>();
        if (glyph is { } semanticGlyph)
        {
            content.Add(new IconElement(
                StableIdentifier.Child(id, "icon"), semanticGlyph, label)
            {
                StyleClasses = ["gbar-settings-row__icon"],
            });
        }

        var copy = new List<WidgetElement>
        {
            new TextElement(StableIdentifier.Child(id, "label"), label, label)
            {
                StyleClasses = ["gbar-settings-row__label"],
            },
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            copy.Add(new TextElement(
                StableIdentifier.Child(id, "description"), description, description)
            {
                StyleClasses = ["gbar-settings-row__description"],
            });
        }
        content.Add(new StackElement(StableIdentifier.Child(id, "copy"), copy)
        {
            StyleClasses = ["gbar-settings-row__copy"],
        });

        var metadata = new List<WidgetElement>();
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata.Add(new TextElement(
                StableIdentifier.Child(id, "value"), value, $"{label}: {value}")
            {
                StyleClasses = ["gbar-settings-row__value"],
            });
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            metadata.Add(StatusBadge(
                status,
                statusTone,
                StableIdentifier.Child(id, "status"))
                .AddClasses("gbar-settings-row__status"));
        }

        var accessibleParts = new List<string> { label };
        if (!string.IsNullOrWhiteSpace(description)) accessibleParts.Add(description);
        if (!string.IsNullOrWhiteSpace(value)) accessibleParts.Add($"Current value: {value}");
        if (!string.IsNullOrWhiteSpace(status)) accessibleParts.Add($"Status: {status}");
        accessibleParts.Add(action.Label);
        if (isDisabled) accessibleParts.Add("Unavailable");
        if (isBusy) accessibleParts.Add("Busy");

        var children = new List<WidgetElement>
        {
            new RowElement(StableIdentifier.Child(id, "content"), content)
            {
                StyleClasses = ["gbar-settings-row__content"],
            },
        };
        if (metadata.Count > 0)
        {
            children.Add(new StackElement(StableIdentifier.Child(id, "metadata"), metadata)
            {
                StyleClasses = ["gbar-settings-row__metadata"],
            });
        }
        children.Add(new ButtonElement(
            StableIdentifier.Child(id, "action"), action.Label, action.ActionId)
        {
            AccessibilityLabel = string.Join(". ", accessibleParts),
            Glyph = action.Glyph,
            IsDisabled = isDisabled ? true : null,
            IsBusy = isBusy ? true : null,
            StyleClasses = ["gbar-settings-row__action"],
        });

        return new StackElement(id, children)
        {
            StyleClasses = ["gbar-settings-row"],
        };
    }

    /// <summary>
    /// Creates a bounded, vertically scrollable nested action scope. Each item
    /// keeps its author-supplied focus ID, disabled and busy actions remain in
    /// the focus graph, and B always resolves to <paramref name="backAction"/>
    /// without depending on which item has focus.
    /// </summary>
    public static StackElement ActionSheet(
        string title,
        string id,
        string scopeId,
        string backAction,
        IReadOnlyList<ActionSheetItem> items,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(backAction);
        ArgumentNullException.ThrowIfNull(items);
        StableIdentifier.Validate(id, nameof(id));
        StableIdentifier.Validate(scopeId, nameof(scopeId));
        if (items.Count is < 1 or > MaximumActionSheetItems)
            throw new ArgumentOutOfRangeException(
                nameof(items),
                $"An action sheet requires between 1 and {MaximumActionSheetItems} items.");
        if (items.Any(item => item is null))
            throw new ArgumentException("Action-sheet items cannot contain null values.", nameof(items));
        if (items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new ArgumentException("Action-sheet item IDs must be unique.", nameof(items));

        var buttons = new ButtonElement[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            StableIdentifier.Validate(item.Id, nameof(items));
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Label);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.ActionId);
            EnsureDefined(item.Tone, nameof(items));

            var accessibleParts = new List<string>
            {
                string.IsNullOrWhiteSpace(item.AccessibilityLabel)
                    ? item.Label
                    : item.AccessibilityLabel,
            };
            if (item.Tone == ActionSheetItemTone.Danger) accessibleParts.Add("Destructive action");
            if (item.IsDisabled) accessibleParts.Add("Unavailable");
            if (item.IsBusy) accessibleParts.Add("Busy");

            var button = new ButtonElement(item.Id, item.Label, item.ActionId)
            {
                AccessibilityLabel = string.Join(", ", accessibleParts),
                Glyph = item.Glyph,
                IsDisabled = item.IsDisabled ? true : null,
                IsBusy = item.IsBusy ? true : null,
                StyleClasses =
                [
                    "gbar-action-sheet__item",
                    item.Tone == ActionSheetItemTone.Danger
                        ? "gbar-action-sheet__item--danger"
                        : "gbar-action-sheet__item--default",
                ],
            };
            if (index > 0) button = button.FocusUp(items[index - 1].Id);
            if (index + 1 < items.Count) button = button.FocusDown(items[index + 1].Id);
            buttons[index] = button;
        }

        var children = new List<WidgetElement>
        {
            new TextElement(StableIdentifier.Child(id, "title"), title, title)
            {
                StyleClasses = ["gbar-action-sheet__title"],
            },
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            children.Add(new TextElement(
                StableIdentifier.Child(id, "description"), description, description)
            {
                StyleClasses = ["gbar-action-sheet__description"],
            });
        }
        children.Add(new ScrollElement(
            StableIdentifier.Child(id, "list"), ScrollAxis.Vertical, buttons)
        {
            StyleClasses = ["gbar-action-sheet__list"],
        });

        return new StackElement(id, children)
        {
            InputScopeId = scopeId,
            Shortcuts = [new ControllerShortcut(ControllerButton.B, backAction)],
            StyleClasses = ["gbar-action-sheet"],
        };
    }

    /// <summary>
    /// Creates a flat, read-only label/value row. The row never enters focus;
    /// use <see cref="ChoiceRow"/> when the whole row represents an action.
    /// </summary>
    public static RowElement ValueRow(
        string label,
        string value,
        string id,
        string? description = null,
        WidgetGlyph? glyph = null,
        string? valueAccessibilityLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var children = new List<WidgetElement>();
        if (glyph is { } semanticGlyph)
        {
            children.Add(new IconElement(
                StableIdentifier.Child(id, "icon"),
                semanticGlyph,
                label)
            {
                StyleClasses = ["gbar-value-row__icon"],
            });
        }

        var textChildren = new List<WidgetElement>
        {
            new TextElement(StableIdentifier.Child(id, "label"), label, label)
            {
                StyleClasses = ["gbar-value-row__label"],
            },
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            textChildren.Add(new TextElement(
                StableIdentifier.Child(id, "description"),
                description,
                description)
            {
                StyleClasses = ["gbar-value-row__description"],
            });
        }

        children.Add(new StackElement(StableIdentifier.Child(id, "text"), textChildren)
        {
            StyleClasses = ["gbar-value-row__text"],
        });
        children.Add(new TextElement(
            StableIdentifier.Child(id, "value"),
            value,
            string.IsNullOrWhiteSpace(valueAccessibilityLabel)
                ? $"{label}: {value}"
                : valueAccessibilityLabel)
        {
            StyleClasses = ["gbar-value-row__value"],
        });

        return new RowElement(id, children)
        {
            StyleClasses = ["gbar-value-row"],
        };
    }

    /// <summary>
    /// Creates one full-row controller focus stop for a choice or list action.
    /// Selected, Disabled, and Busy state remain semantic protocol state, so
    /// changing any of them never changes the stable focus ID.
    /// </summary>
    public static ButtonElement ChoiceRow(
        string label,
        string action,
        string id,
        bool isSelected = false,
        bool isDisabled = false,
        bool isBusy = false,
        WidgetGlyph? glyph = null,
        string? accessibilityLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var accessibleName = string.IsNullOrWhiteSpace(accessibilityLabel)
            ? label
            : accessibilityLabel;
        var states = new List<string>
        {
            isSelected ? "Selected" : "Not selected",
        };
        if (isDisabled) states.Add("Unavailable");
        if (isBusy) states.Add("Busy");

        return new ButtonElement(id, label, action)
        {
            AccessibilityLabel = $"{accessibleName}, {string.Join(", ", states)}",
            Glyph = glyph ?? (isSelected ? WidgetGlyph.Check : null),
            IsSelected = isSelected ? true : null,
            IsDisabled = isDisabled ? true : null,
            IsBusy = isBusy ? true : null,
            StyleClasses =
            [
                "gbar-choice-row",
                isSelected ? "gbar-choice-row--selected" : "gbar-choice-row--idle",
            ],
        };
    }

    /// <summary>
    /// Creates a nonfocusable, themeable controller-help pair. It documents an
    /// action but never registers the shortcut; bind input explicitly on the
    /// owning button or input-scope container.
    /// </summary>
    public static RowElement ControllerHint(
        ControllerButton button,
        string label,
        string id)
    {
        EnsureDefined(button, nameof(button));
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var (shortName, spokenName) = ControllerButtonNames(button);
        return new RowElement(id,
        [
            new TextElement(StableIdentifier.Child(id, "key"), shortName, spokenName)
            {
                StyleClasses = ["gbar-controller-hint__key"],
            },
            new TextElement(StableIdentifier.Child(id, "label"), label, label)
            {
                StyleClasses = ["gbar-controller-hint__label"],
            },
        ])
        {
            StyleClasses = ["gbar-controller-hint"],
        };
    }

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

    /// <summary>
    /// Creates a one-dimensional tab row. Left and Right stay inside the row,
    /// selected state is semantic, and each item keeps the author-provided ID.
    /// The selected tab's content remains widget-owned.
    /// </summary>
    public static RowElement SegmentedTabs(
        string id,
        string selectedTabId,
        params SegmentedTab[] tabs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedTabId);
        ArgumentNullException.ThrowIfNull(tabs);
        if (tabs.Length < 2)
            throw new ArgumentException("A segmented tab row requires at least two tabs.", nameof(tabs));
        if (tabs.Any(tab => tab is null))
            throw new ArgumentException("Tabs cannot contain null values.", nameof(tabs));
        if (tabs.Select(tab => tab.Id).Distinct(StringComparer.Ordinal).Count() != tabs.Length)
            throw new ArgumentException("Tab IDs must be unique.", nameof(tabs));
        if (!tabs.Any(tab => string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal)))
            throw new ArgumentException("The selected tab ID must identify a tab in the row.", nameof(selectedTabId));

        var buttons = new ButtonElement[tabs.Length];
        for (var index = 0; index < tabs.Length; index++)
        {
            var tab = tabs[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(tab.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(tab.Label);
            ArgumentException.ThrowIfNullOrWhiteSpace(tab.ActionId);
            var selected = string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal);
            var state = selected ? "Selected" : "Not selected";
            var button = new ButtonElement(tab.Id, tab.Label, tab.ActionId)
            {
                AccessibilityLabel = !string.IsNullOrWhiteSpace(tab.AccessibilityLabel)
                    ? $"{tab.AccessibilityLabel}, {state}"
                    : $"{tab.Label}, {state}",
                IsSelected = selected ? true : null,
                IsDisabled = tab.IsDisabled ? true : null,
                StyleClasses =
                [
                    "gbar-segmented-tabs__tab",
                    selected ? "gbar-segmented-tabs__tab--selected" : "gbar-segmented-tabs__tab--idle",
                ],
            };
            button = button
                .FocusLeft(tabs[(index - 1 + tabs.Length) % tabs.Length].Id)
                .FocusRight(tabs[(index + 1) % tabs.Length].Id);
            buttons[index] = button;
        }

        return new RowElement(id, buttons)
        {
            StyleClasses = ["gbar-segmented-tabs"],
        };
    }

    /// <summary>
    /// Creates a modern two-state setting with a single stable focus stop.
    /// Disabled switches remain focusable under the platform interaction-state
    /// contract, while activation is suppressed by the host.
    /// </summary>
    public static ButtonElement Switch(
        string label,
        bool isOn,
        string action,
        string id,
        bool isDisabled = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var state = isOn ? "On" : "Off";
        return new ButtonElement(id, $"{label}  {state}", action)
        {
            AccessibilityLabel = $"{label}, {state}",
            Glyph = isOn ? WidgetGlyph.Check : null,
            IsSelected = isOn ? true : null,
            IsDisabled = isDisabled ? true : null,
            StyleClasses = ["gbar-switch", isOn ? "gbar-switch--on" : "gbar-switch--off"],
        };
    }

    /// <summary>
    /// Creates a nested controller surface with a focus-independent B action.
    /// Publish the returned scope ID as WidgetView.ActiveInputScopeId and set
    /// InitialFocusId to a focusable descendant when the dialog is shown.
    /// </summary>
    public static StackElement ScopedDialog(
        string title,
        string id,
        string scopeId,
        string backAction,
        params WidgetElement[] children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(backAction);
        var content = CopyChildren(children);
        return new StackElement(id,
        [
            new TextElement(StableIdentifier.Child(id, "title"), title, title)
            {
                StyleClasses = ["gbar-dialog__title"],
            },
            new StackElement(StableIdentifier.Child(id, "content"), content)
            {
                StyleClasses = ["gbar-dialog__content"],
            },
        ])
        {
            InputScopeId = scopeId,
            Shortcuts = [new ControllerShortcut(ControllerButton.B, backAction)],
            StyleClasses = ["gbar-dialog"],
        };
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

    private static (string ShortName, string SpokenName) ControllerButtonNames(
        ControllerButton button) => button switch
    {
        ControllerButton.A => ("A", "A button"),
        ControllerButton.B => ("B", "B button"),
        ControllerButton.X => ("X", "X button"),
        ControllerButton.Y => ("Y", "Y button"),
        ControllerButton.LeftBumper => ("LB", "Left bumper"),
        ControllerButton.RightBumper => ("RB", "Right bumper"),
        ControllerButton.LeftTrigger => ("LT", "Left trigger"),
        ControllerButton.RightTrigger => ("RT", "Right trigger"),
        ControllerButton.DPadUp => ("D-pad up", "D-pad up"),
        ControllerButton.DPadDown => ("D-pad down", "D-pad down"),
        ControllerButton.DPadLeft => ("D-pad left", "D-pad left"),
        ControllerButton.DPadRight => ("D-pad right", "D-pad right"),
        ControllerButton.LeftStick => ("LS", "Left stick button"),
        ControllerButton.RightStick => ("RS", "Right stick button"),
        ControllerButton.Menu => ("Menu", "Menu button"),
        ControllerButton.View => ("View", "View button"),
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    private static void EnsureDefined<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(parameterName);
    }
}
