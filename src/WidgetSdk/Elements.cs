using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public abstract record WidgetElement(string Id)
{
    public IReadOnlyList<string> StyleClasses { get; init; } = [];
    internal abstract ViewNode ToProtocolNode();

    protected static string RequireId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }

    /// <summary>
    /// Makes this element and its complete subtree active only in the selected
    /// host-resolved responsive mode. Inactive branches own no geometry,
    /// paint, pointer input, controller focus, shortcuts, or accessibility.
    /// </summary>
    public WidgetElement VisibleWhen(ResponsiveVisibility visibility)
    {
        if (!Enum.IsDefined(visibility))
            throw new ArgumentOutOfRangeException(nameof(visibility));
        return this is ResponsiveBranchElement branch
            ? branch with { Visibility = visibility }
            : new ResponsiveBranchElement(this, visibility);
    }

    /// <summary>Marks one stable item in a protocol-v14 cursor collection.</summary>
    public WidgetElement CollectionItem(WidgetCollectionItemKey key) =>
        new CollectionItemElement(this, key);

}

/// <summary>A serialization-only keyed collection-item modifier.</summary>
public sealed record CollectionItemElement : WidgetElement
{
    internal CollectionItemElement(WidgetElement child, WidgetCollectionItemKey key)
        : base((child ?? throw new ArgumentNullException(nameof(child))).Id)
    {
        StableIdentifier.Validate(key.Value, nameof(key));
        Child = child;
        Key = key;
        StyleClasses = child.StyleClasses;
    }

    public WidgetElement Child { get; init; }
    public WidgetCollectionItemKey Key { get; init; }

    internal override ViewNode ToProtocolNode() => Child.ToProtocolNode() with
    {
        CollectionItemKey = Key.Value,
        StyleClasses = StyleClasses,
    };
}

/// <summary>
/// A serialization-only responsive modifier. It does not introduce a layout
/// node or change the wrapped element's stable ID.
/// </summary>
public sealed record ResponsiveBranchElement : WidgetElement
{
    internal ResponsiveBranchElement(WidgetElement child, ResponsiveVisibility visibility)
        : base((child ?? throw new ArgumentNullException(nameof(child))).Id)
    {
        if (!Enum.IsDefined(visibility))
            throw new ArgumentOutOfRangeException(nameof(visibility));
        Child = child;
        Visibility = visibility;
        StyleClasses = child.StyleClasses;
    }

    public WidgetElement Child { get; init; }
    public ResponsiveVisibility Visibility { get; init; }

    internal override ViewNode ToProtocolNode() => Child.ToProtocolNode() with
    {
        VisibleWhen = Visibility,
        StyleClasses = StyleClasses,
    };
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
    public string? NearStartActionId { get; init; }
    public string? NearEndActionId { get; init; }
    public int? PaginationThreshold { get; init; }
    public string? CollectionAnchorKey { get; init; }
    internal VirtualCollectionWindow? VirtualCollectionWindow { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];
    public ScrollElement InputScope(string scopeId) => this with { InputScopeId = RequireId(scopeId) };
    public ScrollElement Shortcut(
        ControllerButton button,
        string actionId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) => this with
        {
            Shortcuts = [.. Shortcuts, new ControllerShortcut(button, RequireId(actionId), phase)],
        };

    /// <summary>
    /// Requests host-owned focus-edge pagination. When focus enters the first
    /// or last <paramref name="threshold"/> direct items, the corresponding
    /// action is emitted without adding a visible Load more control.
    /// </summary>
    public ScrollElement Paginate(
        string? nearStartActionId,
        string? nearEndActionId,
        int threshold = 2)
    {
        if (nearStartActionId is null && nearEndActionId is null)
            throw new ArgumentException("At least one pagination action is required.");
        if (nearStartActionId is not null) RequireId(nearStartActionId);
        if (nearEndActionId is not null) RequireId(nearEndActionId);
        if (threshold is < 1 or > ProtocolConstants.MaximumScrollPaginationThreshold)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        return this with
        {
            NearStartActionId = nearStartActionId,
            NearEndActionId = nearEndActionId,
            PaginationThreshold = threshold,
        };
    }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Scroll,
        ScrollAxis = Axis,
        ScrollNearStartActionId = NearStartActionId,
        ScrollNearEndActionId = NearEndActionId,
        ScrollPaginationThreshold = PaginationThreshold,
        VirtualCollectionWindow = VirtualCollectionWindow,
        CollectionAnchorKey = CollectionAnchorKey,
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
    public string? LeadingImageSource { get; init; }
    public string? LeadingArtworkHandle { get; init; }
    public ImageFit? LeadingImageFit { get; init; }
    public bool? IsDisabled { get; init; }
    public bool? IsSelected { get; init; }
    public bool? IsBusy { get; init; }
    /// <summary>
    /// Optional identity shared by mutually exclusive presentations of this
    /// same logical focus destination. It is never used for action dispatch.
    /// </summary>
    public string? FocusPersistenceId { get; init; }
    public FocusNeighbors? FocusNeighbors { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];

    public ButtonElement FocusUp(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Up = RequireId(id) } };
    public ButtonElement FocusDown(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Down = RequireId(id) } };
    public ButtonElement FocusLeft(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Left = RequireId(id) } };
    public ButtonElement FocusRight(string id) => this with { FocusNeighbors = (FocusNeighbors ?? new()) with { Right = RequireId(id) } };
    public ButtonElement Disabled(bool disabled = true) => this with { IsDisabled = disabled ? true : null };
    public ButtonElement Selected(bool selected = true) => this with { IsSelected = selected ? true : null };
    public ButtonElement Busy(bool busy = true) => this with { IsBusy = busy ? true : null };
    public ButtonElement PersistFocusAs(string id)
    {
        StableIdentifier.Validate(id, nameof(id));
        return this with { FocusPersistenceId = id };
    }
    /// <summary>
    /// Selects a host-rendered semantic icon. Widgets cannot provide arbitrary
    /// vector paths, fonts, or executable drawing code.
    /// </summary>
    public ButtonElement Icon(WidgetGlyph glyph, string? accessibilityLabel = null) => this with
    {
        Glyph = glyph,
        LeadingImageSource = null,
        LeadingArtworkHandle = null,
        LeadingImageFit = null,
        AccessibilityLabel = accessibilityLabel ?? AccessibilityLabel,
    };

    /// <summary>Uses a lazy host-resolved artwork handle without fetch authority.</summary>
    public ButtonElement LeadingArtwork(
        WidgetArtworkHandle handle,
        ImageFit fit = ImageFit.Contain,
        string? accessibilityLabel = null)
    {
        StableIdentifier.Validate(handle.Value, nameof(handle));
        return this with
        {
            Glyph = null,
            LeadingImageSource = null,
            LeadingArtworkHandle = handle.Value,
            LeadingImageFit = fit,
            AccessibilityLabel = accessibilityLabel ?? AccessibilityLabel,
        };
    }

    /// <summary>
    /// Uses a bounded trusted PNG as leading artwork inside this complete
    /// focus target. Paths and arbitrary image formats remain unavailable.
    /// </summary>
    public ButtonElement LeadingInlinePng(
        string pngBase64,
        ImageFit fit = ImageFit.Contain,
        string? accessibilityLabel = null) => this with
        {
            Glyph = null,
            LeadingImageSource = UI.CanonicalInlinePngSource(pngBase64, nameof(pngBase64)),
            LeadingArtworkHandle = null,
            LeadingImageFit = fit,
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
        ImageSource = LeadingImageSource,
        ArtworkHandle = LeadingArtworkHandle,
        ImageFit = LeadingImageFit,
        ActionId = ActionId,
        IsDisabled = IsDisabled,
        IsSelected = IsSelected,
        IsBusy = IsBusy,
        FocusPersistenceId = FocusPersistenceId,
        Focus = FocusNeighbors,
        Shortcuts = Shortcuts,
        StyleClasses = StyleClasses,
    };
}

/// <summary>
/// Opens a host-owned bounded text-entry modal. Widgets receive only the final
/// committed value in <see cref="WidgetActionEvent.CommittedText"/>; raw keys,
/// native handles, and intermediate edits never cross the host boundary.
/// </summary>
public sealed record TextEntryElement : WidgetElement
{
    internal TextEntryElement(
        string id, string value, string placeholder, int maximumLength, string actionId,
        TextEntryInputKind inputKind)
        : base(RequireId(id))
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(placeholder);
        if (maximumLength is < 1 or > ProtocolConstants.MaximumTextEntryLength)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        if (value.Length > maximumLength)
            throw new ArgumentException("The text-entry value exceeds its maximum length.", nameof(value));
        if (!Enum.IsDefined(inputKind))
            throw new ArgumentOutOfRangeException(nameof(inputKind));
        if (inputKind == TextEntryInputKind.Sensitive && value.Length != 0)
            throw new ArgumentException(
                "Sensitive text entry cannot publish an authored value.", nameof(value));
        Value = value;
        Placeholder = placeholder;
        MaximumLength = maximumLength;
        ActionId = RequireId(actionId);
        InputKind = inputKind;
    }

    public string Value { get; init; }
    public string Placeholder { get; init; }
    public int MaximumLength { get; init; }
    public string ActionId { get; init; }
    public TextEntryInputKind InputKind { get; init; }
    public string? AccessibilityLabel { get; init; }
    public bool IsDisabled { get; init; }
    public FocusNeighbors? FocusNeighbors { get; init; }

    public TextEntryElement Disabled(bool disabled = true) => this with
        { IsDisabled = disabled };

    public TextEntryElement FocusUp(string id) => this with
        { FocusNeighbors = (FocusNeighbors ?? new()) with { Up = RequireId(id) } };
    public TextEntryElement FocusDown(string id) => this with
        { FocusNeighbors = (FocusNeighbors ?? new()) with { Down = RequireId(id) } };
    public TextEntryElement FocusLeft(string id) => this with
        { FocusNeighbors = (FocusNeighbors ?? new()) with { Left = RequireId(id) } };
    public TextEntryElement FocusRight(string id) => this with
        { FocusNeighbors = (FocusNeighbors ?? new()) with { Right = RequireId(id) } };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.TextEntry,
        Text = Value.Length == 0 ? Placeholder : Value,
        AccessibilityLabel = AccessibilityLabel ?? Placeholder,
        AccessibilityValue = InputKind == TextEntryInputKind.Sensitive ? null : Value,
        ActionId = ActionId,
        TextEntryValue = Value,
        TextEntryPlaceholder = Placeholder,
        TextEntryMaximumLength = MaximumLength,
        TextEntryInputKind = InputKind == TextEntryInputKind.Ordinary ? null : InputKind,
        IsDisabled = IsDisabled,
        Focus = FocusNeighbors,
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

/// <summary>
/// A host-rendered controller value control. By default, focused Left and
/// Right emit quantized absolute value-change actions. Widgets may opt into
/// activation-first adjustment so all directions remain focus navigation
/// until A enters the host-owned adjustment mode.
/// </summary>
public sealed record SliderElement : WidgetElement
{
    internal SliderElement(
        string id,
        double value,
        double minimum,
        double maximum,
        double step,
        string valueChangedActionId,
        string accessibilityLabel,
        string accessibilityValue,
        string? activationActionId) : base(RequireId(id))
    {
        Value = value;
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        ValueChangedActionId = RequireId(valueChangedActionId);
        AccessibilityLabel = accessibilityLabel ?? throw new ArgumentNullException(nameof(accessibilityLabel));
        AccessibilityValue = accessibilityValue ?? throw new ArgumentNullException(nameof(accessibilityValue));
        ActivationActionId = activationActionId is null ? null : RequireId(activationActionId);
    }

    public double Value { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Step { get; init; }
    public string ValueChangedActionId { get; init; }
    public string AccessibilityLabel { get; init; }
    public string AccessibilityValue { get; init; }
    /// <summary>Optional A-button action while this slider has focus.</summary>
    public string? ActivationActionId { get; init; }
    public SliderInteractionMode? ControllerInteractionMode { get; init; }
    public bool? IsDisabled { get; init; }
    public bool? IsBusy { get; init; }
    /// <inheritdoc cref="ButtonElement.FocusPersistenceId"/>
    public string? FocusPersistenceId { get; init; }
    public FocusNeighbors? FocusNeighbors { get; init; }

    public SliderElement FocusUp(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Up = RequireId(id) },
    };
    public SliderElement FocusDown(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Down = RequireId(id) },
    };
    public SliderElement FocusLeft(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Left = RequireId(id) },
    };
    public SliderElement FocusRight(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Right = RequireId(id) },
    };
    public SliderElement Disabled(bool disabled = true) => this with
    {
        IsDisabled = disabled ? true : null,
    };
    public SliderElement Busy(bool busy = true) => this with
    {
        IsBusy = busy ? true : null,
    };
    public SliderElement PersistFocusAs(string id)
    {
        StableIdentifier.Validate(id, nameof(id));
        return this with { FocusPersistenceId = id };
    }
    public SliderElement Activate(string actionId) => this with
    {
        ActivationActionId = RequireId(actionId),
    };
    /// <summary>
    /// Requires A to enter controller adjustment mode. While inactive, D-pad
    /// directions navigate normally; while active, Left/Right adjust and A or
    /// B exits without emitting a separate activation action.
    /// </summary>
    public SliderElement RequireControllerActivation(bool required = true) => this with
    {
        ControllerInteractionMode = required
            ? SliderInteractionMode.ActivateToAdjust
            : null,
    };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Slider,
        Value = Value,
        Minimum = Minimum,
        Maximum = Maximum,
        Step = Step,
        ValueChangedActionId = ValueChangedActionId,
        SliderInteractionMode = ControllerInteractionMode,
        ActionId = ActivationActionId,
        AccessibilityLabel = AccessibilityLabel,
        AccessibilityValue = AccessibilityValue,
        IsDisabled = IsDisabled,
        IsBusy = IsBusy,
        FocusPersistenceId = FocusPersistenceId,
        Focus = FocusNeighbors,
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

/// <summary>
/// One native layout-owned viewport for the host-owned embedded-media plane.
/// The browser supplies pixels only; WidgetRail retains layout, clipping,
/// focus, accessibility, input, and teardown authority.
/// </summary>
public sealed record MediaViewportElement : WidgetElement
{
    internal MediaViewportElement(string id, EmbeddedMediaSurface surface)
        : base(RequireId(id))
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        StableIdentifier.Validate(surface.Id, nameof(surface));
        if (string.IsNullOrWhiteSpace(surface.AccessibleName))
            throw new ArgumentException(
                "An embedded media viewport requires an accessible surface name.",
                nameof(surface));
    }

    public EmbeddedMediaSurface Surface { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.MediaViewport,
        MediaSurfaceId = Surface.Id,
        AccessibilityLabel = Surface.AccessibleName,
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
    public string? ArtworkHandle { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.Image,
        ImageSource = Source.Length == 0 ? null : Source,
        ArtworkHandle = ArtworkHandle,
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

/// <summary>
/// A non-interactive, host-rendered indeterminate activity indicator. It never
/// enters controller focus or emits actions. The host animates it only while
/// visible and presents an honest static indicator when reduced motion is on.
/// </summary>
public sealed record LoadingIndicatorElement : WidgetElement
{
    internal LoadingIndicatorElement(
        string id,
        string accessibilityLabel,
        LoadingIndicatorSize size) : base(RequireId(id))
    {
        AccessibilityLabel = string.IsNullOrWhiteSpace(accessibilityLabel)
            ? throw new ArgumentException("A loading indicator requires an accessibility label.", nameof(accessibilityLabel))
            : accessibilityLabel;
        if (!Enum.IsDefined(size)) throw new ArgumentOutOfRangeException(nameof(size));
        Size = size;
    }

    public string AccessibilityLabel { get; init; }
    public LoadingIndicatorSize Size { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.LoadingIndicator,
        AccessibilityLabel = AccessibilityLabel,
        IndicatorSize = Size,
        StyleClasses =
        [
            "loading-indicator",
            Size switch
            {
                LoadingIndicatorSize.Compact => "loading-indicator-compact",
                LoadingIndicatorSize.Large => "loading-indicator-large",
                _ => "loading-indicator-standard",
            },
            .. StyleClasses.Where(item =>
                !string.Equals(item, "loading-indicator", StringComparison.Ordinal) &&
                !item.StartsWith("loading-indicator-", StringComparison.Ordinal)),
        ],
    };
}
