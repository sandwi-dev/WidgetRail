using System.Globalization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public enum IconButtonVariant { Default, Primary, Danger, Quiet }
public enum IconButtonSize { Small, Medium, Large }
public enum CardVariant { Raised, Subtle, Transparent }
public enum StatusTone { Neutral, Info, Success, Warning, Danger }
public enum AlertTone { Info, Success, Warning, Danger }
public enum ActionSheetItemTone { Default, Danger }

/// <summary>A single optional action rendered by an alert or empty state.</summary>
public sealed record ComponentAction(string Label, string ActionId, WidgetGlyph? Glyph = null)
{
    public WidgetIcon? Icon { get; init; }
}

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
    bool IsBusy = false)
{
    public WidgetIcon? Icon { get; init; }
}

/// <summary>A stable, single-select option rendered by <see cref="UI.Picker"/>.</summary>
public sealed record PickerOption(
    string Id,
    string Label,
    string ActionId,
    bool IsSelected = false,
    WidgetGlyph? Glyph = null,
    string? AccessibilityLabel = null,
    bool IsDisabled = false,
    bool IsBusy = false)
{
    public WidgetIcon? Icon { get; init; }
}

/// <summary>A stable option in a host-owned anchored <see cref="UI.Select"/> popup.</summary>
public sealed record SelectOption(
    string Id,
    string Label,
    string ActionId,
    bool IsSelected = false,
    WidgetGlyph? Glyph = null,
    string? AccessibilityLabel = null,
    bool IsDisabled = false,
    bool IsBusy = false)
{
    public WidgetIcon? Icon { get; init; }
}

/// <summary>
/// A controller-native media timeline composed from the public Slider and text
/// primitives. Direct scrubbers seek with focused Left and Right. An opt-in
/// activation-first mode leaves D-pad navigation available until A selects the
/// timeline. Requested values are emitted as total milliseconds.
/// </summary>
public sealed record ScrubberElement : WidgetElement
{
    internal ScrubberElement(
        TimeSpan position,
        TimeSpan duration,
        TimeSpan step,
        string valueChangedActionId,
        string id,
        string accessibilityLabel,
        string? elapsedLabel,
        string? durationLabel,
        string? activationActionId) : base(RequireId(id))
    {
        StableIdentifier.Validate(id, nameof(id));
        _ = StableIdentifier.Child(id, "slider");
        _ = StableIdentifier.Child(id, "times");
        _ = StableIdentifier.Child(id, "elapsed");
        _ = StableIdentifier.Child(id, "duration");
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive.");
        if (position < TimeSpan.Zero || position > duration)
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be within the media duration.");
        if (step <= TimeSpan.Zero || step > duration)
            throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive and no greater than the media duration.");
        ArgumentException.ThrowIfNullOrWhiteSpace(valueChangedActionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibilityLabel);
        if (elapsedLabel is not null && string.IsNullOrWhiteSpace(elapsedLabel))
            throw new ArgumentException("An elapsed-time label cannot be empty.", nameof(elapsedLabel));
        if (durationLabel is not null && string.IsNullOrWhiteSpace(durationLabel))
            throw new ArgumentException("A duration label cannot be empty.", nameof(durationLabel));
        if (activationActionId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(activationActionId);

        Position = position;
        Duration = duration;
        Step = step;
        ValueChangedActionId = valueChangedActionId;
        AccessibilityLabel = accessibilityLabel;
        ElapsedLabel = elapsedLabel;
        DurationLabel = durationLabel;
        ActivationActionId = activationActionId;
        RequiredStyleClasses = ["wrail-scrubber"];
    }

    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan Step { get; init; }
    public string ValueChangedActionId { get; init; }
    public string AccessibilityLabel { get; init; }
    public string? ElapsedLabel { get; init; }
    public string? DurationLabel { get; init; }
    public string? ActivationActionId { get; init; }
    public SliderInteractionMode? ControllerInteractionMode { get; init; }
    public bool? IsDisabled { get; init; }
    public bool? IsBusy { get; init; }
    /// <inheritdoc cref="ButtonElement.FocusPersistenceId"/>
    public string? FocusPersistenceId { get; init; }
    public FocusNeighbors? FocusNeighbors { get; init; }

    /// <summary>The stable focus ID owned by the nested Slider.</summary>
    public string SliderId => StableIdentifier.Child(Id, "slider");

    public ScrubberElement FocusUp(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Up = RequireId(id) },
    };

    public ScrubberElement FocusDown(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Down = RequireId(id) },
    };

    public ScrubberElement FocusLeft(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Left = RequireId(id) },
    };

    public ScrubberElement FocusRight(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Right = RequireId(id) },
    };

    public ScrubberElement Disabled(bool disabled = true) => this with
    {
        IsDisabled = disabled ? true : null,
    };

    public ScrubberElement Busy(bool busy = true) => this with
    {
        IsBusy = busy ? true : null,
    };
    public ScrubberElement PersistFocusAs(string id)
    {
        StableIdentifier.Validate(id, nameof(id));
        return this with { FocusPersistenceId = id };
    }

    public ScrubberElement Activate(string actionId) => this with
    {
        ActivationActionId = RequireId(actionId),
    };

    /// <inheritdoc cref="SliderElement.RequireControllerActivation"/>
    public ScrubberElement RequireControllerActivation(bool required = true) => this with
    {
        ControllerInteractionMode = required
            ? SliderInteractionMode.ActivateToAdjust
            : null,
    };

    internal override ViewNode ToProtocolNode()
    {
        var showHours = Duration >= TimeSpan.FromHours(1);
        var elapsed = ElapsedLabel ?? FormatTime(Position, showHours);
        var duration = DurationLabel ?? FormatTime(Duration, showHours);
        var accessibleStates = new List<string> { AccessibilityLabel };
        if (IsDisabled is true) accessibleStates.Add("Unavailable");
        if (IsBusy is true) accessibleStates.Add("Busy");

        var slider = new SliderElement(
            SliderId,
            Position.TotalMilliseconds,
            0,
            Duration.TotalMilliseconds,
            Step.TotalMilliseconds,
            ValueChangedActionId,
            string.Join(", ", accessibleStates),
            $"{elapsed} of {duration}",
            ActivationActionId)
        {
            IsDisabled = IsDisabled,
            IsBusy = IsBusy,
            FocusPersistenceId = FocusPersistenceId,
            ControllerInteractionMode = ControllerInteractionMode,
            FocusNeighbors = FocusNeighbors,
            RequiredStyleClasses = ["wrail-scrubber__slider"],
        };

        return new StackElement(Id,
        [
            slider,
            new RowElement(StableIdentifier.Child(Id, "times"),
            [
                new TextElement(StableIdentifier.Child(Id, "elapsed"), elapsed, $"Elapsed {elapsed}")
                {
                    RequiredStyleClasses = ["wrail-scrubber__elapsed"],
                },
                new TextElement(StableIdentifier.Child(Id, "duration"), duration, $"Duration {duration}")
                {
                    RequiredStyleClasses = ["wrail-scrubber__duration"],
                },
            ])
            {
                RequiredStyleClasses = ["wrail-scrubber__times"],
            },
        ])
        {
            RequiredStyleClasses = RequiredStyleClasses,
            AuthorStyleClasses = AuthorStyleClasses,
        }.ToProtocolNode();
    }

    private static string FormatTime(TimeSpan value, bool showHours)
    {
        var totalSeconds = (long)Math.Floor(value.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return showHours
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }
}

/// <summary>
/// Original controller-first composites built only from stable public protocol
/// nodes. Their wrail-* classes are semantic theme hooks, not fixed colors.
/// </summary>
public static partial class UI
{
    /// <summary>The maximum number of actions accepted by one action sheet.</summary>
    public const int MaximumActionSheetItems = 32;

    /// <summary>The maximum number of choices accepted by one picker surface.</summary>
    public const int MaximumPickerOptions = 128;

    /// <summary>
    /// Creates a responsive controller-first media timeline. The returned
    /// component has one focus stop at <c>{id}.slider</c>; Left and Right emit
    /// quantized absolute millisecond targets through
    /// <paramref name="valueChangedAction"/>, while Up and Down follow focus
    /// links configured on the returned element.
    /// </summary>
    public static ScrubberElement Scrubber(
        TimeSpan position,
        TimeSpan duration,
        TimeSpan step,
        string valueChangedAction,
        string id,
        string accessibilityLabel = "Playback position",
        string? elapsedLabel = null,
        string? durationLabel = null,
        string? activationAction = null) => new(
            position,
            duration,
            step,
            valueChangedAction,
            id,
            accessibilityLabel,
            elapsedLabel,
            durationLabel,
            activationAction);

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
        => SettingsRowCore(
            label, action, id, description, value, status, statusTone,
            isDisabled, isBusy, WidgetIconMaterializer.Resolve(null, glyph));

    public static StackElement SettingsRow(
        WidgetIcon icon,
        string label,
        ComponentAction action,
        string id,
        string? description = null,
        string? value = null,
        string? status = null,
        StatusTone statusTone = StatusTone.Neutral,
        bool isDisabled = false,
        bool isBusy = false)
        => SettingsRowCore(
            label, action, id, description, value, status, statusTone,
            isDisabled, isBusy, icon);

    private static StackElement SettingsRowCore(
        string label,
        ComponentAction action,
        string id,
        string? description,
        string? value,
        string? status,
        StatusTone statusTone,
        bool isDisabled,
        bool isBusy,
        WidgetIcon? icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Label);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.ActionId);
        EnsureDefined(statusTone, nameof(statusTone));
        StableIdentifier.Validate(id, nameof(id));

        var content = new List<WidgetElement>();
        if (icon is not null)
        {
            content.Add(new IconElement(
                StableIdentifier.Child(id, "icon"), icon, label)
            {
                RequiredStyleClasses = ["wrail-settings-row__icon"],
            });
        }

        var copy = new List<WidgetElement>
        {
            new TextElement(StableIdentifier.Child(id, "label"), label, label)
            {
                RequiredStyleClasses = ["wrail-settings-row__label"],
            },
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            copy.Add(new TextElement(
                StableIdentifier.Child(id, "description"), description, description)
            {
                RequiredStyleClasses = ["wrail-settings-row__description"],
            });
        }
        content.Add(new StackElement(StableIdentifier.Child(id, "copy"), copy)
        {
            RequiredStyleClasses = ["wrail-settings-row__copy"],
        });

        var metadata = new List<WidgetElement>();
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata.Add(new TextElement(
                StableIdentifier.Child(id, "value"), value, $"{label}: {value}")
            {
                RequiredStyleClasses = ["wrail-settings-row__value"],
            });
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusBadge = StatusBadge(
                status,
                statusTone,
                StableIdentifier.Child(id, "status"));
            metadata.Add(statusBadge with
            {
                RequiredStyleClasses =
                [
                    .. statusBadge.RequiredStyleClasses,
                    "wrail-settings-row__status",
                ],
            });
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
                RequiredStyleClasses = ["wrail-settings-row__content"],
            },
        };
        if (metadata.Count > 0)
        {
            children.Add(new StackElement(StableIdentifier.Child(id, "metadata"), metadata)
            {
                RequiredStyleClasses = ["wrail-settings-row__metadata"],
            });
        }
        children.Add(new ButtonElement(
            StableIdentifier.Child(id, "action"), action.Label, action.ActionId)
        {
            AccessibilityLabel = string.Join(". ", accessibleParts),
            Glyph = action.Icon?.FallbackGlyph ?? action.Glyph,
            PackageIcon = action.Icon is null ? null : WidgetIconMaterializer.PackageIcon(action.Icon),
            IsDisabled = isDisabled ? true : null,
            IsBusy = isBusy ? true : null,
            RequiredStyleClasses = ["wrail-settings-row__action"],
        });

        return new StackElement(id, children)
        {
            RequiredStyleClasses = ["wrail-settings-row"],
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
                Glyph = item.Icon?.FallbackGlyph ?? item.Glyph,
                PackageIcon = item.Icon is null ? null : WidgetIconMaterializer.PackageIcon(item.Icon),
                IsDisabled = item.IsDisabled ? true : null,
                IsBusy = item.IsBusy ? true : null,
                RequiredStyleClasses =
                [
                    "wrail-action-sheet__item",
                    item.Tone == ActionSheetItemTone.Danger
                        ? "wrail-action-sheet__item--danger"
                        : "wrail-action-sheet__item--default",
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
                RequiredStyleClasses = ["wrail-action-sheet__title"],
            },
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            children.Add(new TextElement(
                StableIdentifier.Child(id, "description"), description, description)
            {
                RequiredStyleClasses = ["wrail-action-sheet__description"],
            });
        }
        children.Add(new ScrollElement(
            StableIdentifier.Child(id, "list"), ScrollAxis.Vertical, buttons)
        {
            RequiredStyleClasses = ["wrail-action-sheet__list"],
        });

        return new StackElement(id, children)
        {
            InputScopeId = scopeId,
            Shortcuts = [new ControllerShortcut(ControllerButton.B, backAction)],
            RequiredStyleClasses = ["wrail-action-sheet"],
        };
    }

    /// <summary>
    /// Creates a bounded single-select surface with one stable focus target per
    /// option. The selected option remains explicit protocol state, unavailable
    /// options stay in the focus graph, and B resolves on the nested scope even
    /// when focus is temporarily absent. At most one option may be selected.
    /// </summary>
    public static StackElement Picker(
        string title,
        string id,
        string scopeId,
        string backAction,
        IReadOnlyList<PickerOption> options,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(backAction);
        ArgumentNullException.ThrowIfNull(options);
        StableIdentifier.Validate(id, nameof(id));
        StableIdentifier.Validate(scopeId, nameof(scopeId));
        if (options.Count is < 1 or > MaximumPickerOptions)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"A picker requires between 1 and {MaximumPickerOptions} options.");
        if (options.Any(option => option is null))
            throw new ArgumentException("Picker options cannot contain null values.", nameof(options));
        if (options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != options.Count)
            throw new ArgumentException("Picker option IDs must be unique.", nameof(options));
        if (options.Count(option => option.IsSelected) > 1)
            throw new ArgumentException("A single-select picker cannot contain multiple selected options.", nameof(options));

        var buttons = new ButtonElement[options.Count];
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            StableIdentifier.Validate(option.Id, nameof(options));
            ArgumentException.ThrowIfNullOrWhiteSpace(option.Label);
            ArgumentException.ThrowIfNullOrWhiteSpace(option.ActionId);

            var accessibleParts = new List<string>
            {
                string.IsNullOrWhiteSpace(option.AccessibilityLabel)
                    ? option.Label
                    : option.AccessibilityLabel,
                option.IsSelected ? "Selected" : "Not selected",
            };
            if (option.IsDisabled) accessibleParts.Add("Unavailable");
            if (option.IsBusy) accessibleParts.Add("Busy");

            var button = new ButtonElement(option.Id, option.Label, option.ActionId)
            {
                AccessibilityLabel = string.Join(", ", accessibleParts),
                Glyph = option.Icon?.FallbackGlyph ?? option.Glyph ??
                    (option.IsSelected ? WidgetGlyph.Check : null),
                PackageIcon = option.Icon is null ? null : WidgetIconMaterializer.PackageIcon(option.Icon),
                IsSelected = option.IsSelected ? true : null,
                IsDisabled = option.IsDisabled ? true : null,
                IsBusy = option.IsBusy ? true : null,
                RequiredStyleClasses =
                [
                    "wrail-picker__option",
                    option.IsSelected
                        ? "wrail-picker__option--selected"
                        : "wrail-picker__option--idle",
                ],
            };
            if (index > 0) button = button.FocusUp(options[index - 1].Id);
            if (index + 1 < options.Count) button = button.FocusDown(options[index + 1].Id);
            buttons[index] = button;
        }

        var children = new List<WidgetElement>
        {
            new TextElement(StableIdentifier.Child(id, "title"), title, title)
            {
                RequiredStyleClasses = ["wrail-picker__title"],
            },
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            children.Add(new TextElement(
                StableIdentifier.Child(id, "description"), description, description)
            {
                RequiredStyleClasses = ["wrail-picker__description"],
            });
        }
        children.Add(new ScrollElement(
            StableIdentifier.Child(id, "options"), ScrollAxis.Vertical, buttons)
        {
            RequiredStyleClasses = ["wrail-picker__options"],
        });

        return new StackElement(id, children)
        {
            InputScopeId = scopeId,
            Shortcuts = [new ControllerShortcut(ControllerButton.B, backAction)],
            RequiredStyleClasses = ["wrail-picker"],
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
        => ValueRowCore(
            label, value, id, description,
            WidgetIconMaterializer.Resolve(null, glyph), valueAccessibilityLabel);

    public static RowElement ValueRow(
        WidgetIcon icon,
        string label,
        string value,
        string id,
        string? description = null,
        string? valueAccessibilityLabel = null)
        => ValueRowCore(label, value, id, description, icon, valueAccessibilityLabel);

    private static RowElement ValueRowCore(
        string label,
        string value,
        string id,
        string? description,
        WidgetIcon? icon,
        string? valueAccessibilityLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var children = new List<WidgetElement>();
        if (icon is not null)
        {
            children.Add(new IconElement(
                StableIdentifier.Child(id, "icon"),
                icon,
                label)
            {
                RequiredStyleClasses = ["wrail-value-row__icon"],
            });
        }

        var textChildren = new List<WidgetElement>
        {
            new TextElement(StableIdentifier.Child(id, "label"), label, label)
            {
                RequiredStyleClasses = ["wrail-value-row__label"],
            },
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            textChildren.Add(new TextElement(
                StableIdentifier.Child(id, "description"),
                description,
                description)
            {
                RequiredStyleClasses = ["wrail-value-row__description"],
            });
        }

        children.Add(new StackElement(StableIdentifier.Child(id, "text"), textChildren)
        {
            RequiredStyleClasses = ["wrail-value-row__text"],
        });
        children.Add(new TextElement(
            StableIdentifier.Child(id, "value"),
            value,
            string.IsNullOrWhiteSpace(valueAccessibilityLabel)
                ? $"{label}: {value}"
                : valueAccessibilityLabel)
        {
            RequiredStyleClasses = ["wrail-value-row__value"],
        });

        return new RowElement(id, children)
        {
            RequiredStyleClasses = ["wrail-value-row"],
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
        => ChoiceRowCore(
            label, action, id, isSelected, isDisabled, isBusy,
            WidgetIconMaterializer.Resolve(null, glyph), accessibilityLabel);

    public static ButtonElement ChoiceRow(
        WidgetIcon icon,
        string label,
        string action,
        string id,
        bool isSelected = false,
        bool isDisabled = false,
        bool isBusy = false,
        string? accessibilityLabel = null)
        => ChoiceRowCore(
            label, action, id, isSelected, isDisabled, isBusy,
            icon, accessibilityLabel);

    private static ButtonElement ChoiceRowCore(
        string label,
        string action,
        string id,
        bool isSelected,
        bool isDisabled,
        bool isBusy,
        WidgetIcon? icon,
        string? accessibilityLabel)
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
            Glyph = icon?.FallbackGlyph ?? (isSelected ? WidgetGlyph.Check : null),
            PackageIcon = icon is null ? null : WidgetIconMaterializer.PackageIcon(icon),
            IsSelected = isSelected ? true : null,
            IsDisabled = isDisabled ? true : null,
            IsBusy = isBusy ? true : null,
            RequiredStyleClasses =
            [
                "wrail-choice-row",
                isSelected ? "wrail-choice-row--selected" : "wrail-choice-row--idle",
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
        string id) => ControllerHint(PromptForButton(button), label, id);

    public static ButtonElement IconButton(
        WidgetGlyph glyph,
        string action,
        string id,
        string accessibilityLabel,
        IconButtonVariant variant = IconButtonVariant.Default,
        IconButtonSize size = IconButtonSize.Medium)
        => IconButton(
            WidgetIcon.Glyph(glyph), action, id, accessibilityLabel, variant, size);

    public static ButtonElement IconButton(
        WidgetIcon icon,
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
            Glyph = WidgetIconMaterializer.Glyph(icon),
            PackageIcon = WidgetIconMaterializer.PackageIcon(icon),
            AccessibilityLabel = accessibilityLabel,
            RequiredStyleClasses =
            [
                "wrail-icon-button",
                $"wrail-icon-button--{Token(size)}",
                $"wrail-icon-button--{Token(variant)}",
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
            RequiredStyleClasses = ["wrail-card", $"wrail-card--{Token(variant)}"],
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
                RequiredStyleClasses = ["wrail-section-header__eyebrow"],
            });
        }
        text.Add(new TextElement(titleId, title, title)
        {
            RequiredStyleClasses = ["wrail-section-header__title"],
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Add(new TextElement(StableIdentifier.Child(id, "description"), description, description)
            {
                RequiredStyleClasses = ["wrail-section-header__description"],
            });
        }
        var content = new List<WidgetElement>
        {
            new StackElement(textId, text)
            {
                RequiredStyleClasses = ["wrail-section-header__text"],
            },
        };
        if (trailing is not null)
        {
            content.Add(new RowElement(StableIdentifier.Child(id, "trailing"), [trailing])
            {
                RequiredStyleClasses = ["wrail-section-header__trailing"],
            });
        }
        return new StackElement(id,
        [
            new RowElement(contentId, content)
            {
                RequiredStyleClasses = ["wrail-section-header__content"],
            },
        ])
        {
            RequiredStyleClasses = ["wrail-section-header"],
        };
    }

    public static RowElement StatusBadge(
        string label,
        StatusTone tone,
        string id,
        WidgetGlyph? glyph = null)
        => StatusBadgeCore(
            label, tone, id,
            glyph is { } semanticGlyph
                ? WidgetIcon.Glyph(semanticGlyph)
                : null);

    public static RowElement StatusBadge(
        WidgetIcon icon,
        string label,
        StatusTone tone,
        string id) => StatusBadgeCore(label, tone, id, icon);

    private static RowElement StatusBadgeCore(
        string label,
        StatusTone tone,
        string id,
        WidgetIcon? icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        EnsureDefined(tone, nameof(tone));
        var labelId = StableIdentifier.Child(id, "label");
        icon ??= tone switch
        {
            StatusTone.Success => WidgetIcon.Glyph(WidgetGlyph.Check),
            StatusTone.Warning or StatusTone.Danger =>
                WidgetIcon.Glyph(WidgetGlyph.Warning),
            _ => null,
        };
        var children = new List<WidgetElement>();
        if (icon is not null)
        {
            children.Add(new IconElement(
                StableIdentifier.Child(id, "icon"), icon, $"{tone} status")
            {
                RequiredStyleClasses = ["wrail-badge__icon", $"wrail-badge__icon--{Token(tone)}"],
            });
        }
        children.Add(new TextElement(labelId, label, label)
        {
            RequiredStyleClasses = ["wrail-badge__label", $"wrail-badge__label--{Token(tone)}"],
        });
        return new RowElement(id, children)
        {
            RequiredStyleClasses = ["wrail-badge", $"wrail-badge--{Token(tone)}"],
        };
    }

    public static SpacerElement Divider(string id) => new(id)
    {
        RequiredStyleClasses = ["wrail-divider"],
    };

    public static StackElement Alert(
        string title,
        string message,
        AlertTone tone,
        string id,
        ComponentAction? action = null,
        WidgetGlyph? glyph = null)
        => AlertCore(
            title, message, tone, id, action,
            glyph is { } semanticGlyph ? WidgetIcon.Glyph(semanticGlyph) : null);

    public static StackElement Alert(
        WidgetIcon icon,
        string title,
        string message,
        AlertTone tone,
        string id,
        ComponentAction? action = null)
        => AlertCore(title, message, tone, id, action, icon);

    private static StackElement AlertCore(
        string title,
        string message,
        AlertTone tone,
        string id,
        ComponentAction? action,
        WidgetIcon? icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        EnsureDefined(tone, nameof(tone));
        icon ??= tone switch
        {
            AlertTone.Success => WidgetIcon.Glyph(WidgetGlyph.Check),
            AlertTone.Warning or AlertTone.Danger =>
                WidgetIcon.Glyph(WidgetGlyph.Warning),
            _ => null,
        };
        return MessageSurface(
            "wrail-alert",
            title,
            message,
            id,
            icon,
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
        => EmptyStateCore(
            title, message, id, action, WidgetIcon.Glyph(glyph));

    public static StackElement EmptyState(
        WidgetIcon icon,
        string title,
        string message,
        string id,
        ComponentAction? action = null)
        => EmptyStateCore(title, message, id, action, icon);

    private static StackElement EmptyStateCore(
        string title,
        string message,
        string id,
        ComponentAction? action,
        WidgetIcon icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return MessageSurface(
            "wrail-empty-state",
            title,
            message,
            id,
            icon,
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
                RequiredStyleClasses =
                [
                    "wrail-segmented-tabs__tab",
                    selected ? "wrail-segmented-tabs__tab--selected" : "wrail-segmented-tabs__tab--idle",
                ],
            };
            button = button
                .FocusLeft(tabs[(index - 1 + tabs.Length) % tabs.Length].Id)
                .FocusRight(tabs[(index + 1) % tabs.Length].Id);
            buttons[index] = button;
        }

        return new RowElement(id, buttons)
        {
            RequiredStyleClasses = ["wrail-segmented-tabs"],
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
            IsSelected = isOn ? true : null,
            IsDisabled = isDisabled ? true : null,
            RequiredStyleClasses = ["wrail-switch", isOn ? "wrail-switch--on" : "wrail-switch--off"],
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
                RequiredStyleClasses = ["wrail-dialog__title"],
            },
            new StackElement(StableIdentifier.Child(id, "content"), content)
            {
                RequiredStyleClasses = ["wrail-dialog__content"],
            },
        ])
        {
            InputScopeId = scopeId,
            Shortcuts = [new ControllerShortcut(ControllerButton.B, backAction)],
            RequiredStyleClasses = ["wrail-dialog"],
        };
    }

    private static StackElement MessageSurface(
        string componentClass,
        string title,
        string message,
        string id,
        WidgetIcon? icon,
        string iconAccessibilityLabel,
        string? tone,
        ComponentAction? action)
    {
        var titleId = StableIdentifier.Child(id, "title");
        var messageId = StableIdentifier.Child(id, "message");
        var children = new List<WidgetElement>();
        if (icon is not null)
        {
            children.Add(new IconElement(
                StableIdentifier.Child(id, "icon"),
                icon,
                iconAccessibilityLabel)
            {
                RequiredStyleClasses = tone is null
                    ? [$"{componentClass}__icon"]
                    : [$"{componentClass}__icon", $"{componentClass}__icon--{tone}"],
            });
        }
        children.AddRange(
        [
            new TextElement(titleId, title, title)
            {
                RequiredStyleClasses = [$"{componentClass}__title"],
            },
            new TextElement(messageId, message, message)
            {
                RequiredStyleClasses = [$"{componentClass}__message"],
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
                Glyph = action.Icon?.FallbackGlyph ?? action.Glyph,
                PackageIcon = action.Icon is null ? null : WidgetIconMaterializer.PackageIcon(action.Icon),
                AccessibilityLabel = action.Label,
                RequiredStyleClasses = [$"{componentClass}__action"],
            });
        }
        var classes = tone is null
            ? new[] { componentClass }
            : new[] { componentClass, $"{componentClass}--{tone}" };
        return new StackElement(id, children) { RequiredStyleClasses = classes };
    }

    private static string Token<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private static void EnsureDefined<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(parameterName);
    }
}
