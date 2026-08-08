using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<ViewNodeKind>))]
public enum ViewNodeKind
{
    Stack,
    Row,
    Scroll,
    Text,
    Button,
    Progress,
    Slider,
    Spacer,
    Image,
    Icon,
    LoadingIndicator,
    ActionSurface,
    Grid,
}

/// <summary>
/// Selects the bounded primary layout direction for an ActionSurface. The
/// host lays out the complete child subtree first, then uses its final bounds
/// as one focus, pointer, pressed, and activation target.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionSurfaceOrientation>))]
public enum ActionSurfaceOrientation
{
    Horizontal,
    Vertical,
}

[JsonConverter(typeof(JsonStringEnumConverter<LoadingIndicatorSize>))]
public enum LoadingIndicatorSize
{
    Compact,
    Standard,
    Large,
}

/// <summary>The single logical axis owned by a host-rendered scroll container.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScrollAxis>))]
public enum ScrollAxis
{
    Vertical,
    Horizontal,
}

/// <summary>
/// A semantic sizing class, not a window size. The host resolves it against
/// the current work area, DPI, accessibility scale, and shell chrome.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetSurfaceMode>))]
public enum WidgetSurfaceMode
{
    Adaptive,
    Compact,
    Standard,
    Wide,
}

/// <summary>
/// Bounded logical-DIP hints for the currently published view. The host may
/// choose any smaller or larger safe size; widgets must remain responsive.
/// Width/height pairs are atomic so partially specified geometry cannot leak
/// into placement policy.
/// </summary>
public sealed record WidgetSurfaceHints
{
    public WidgetSurfaceMode Mode { get; init; } = WidgetSurfaceMode.Adaptive;
    public double? PreferredWidth { get; init; }
    public double? PreferredHeight { get; init; }
    public double? MinimumWidth { get; init; }
    public double? MinimumHeight { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ImageFit>))]
public enum ImageFit
{
    Contain,
    Cover,
    Fill,
}

/// <summary>
/// A closed set of semantic glyphs that the host maps to its native icon set.
/// Widgets never supply SVG, font names, paths, or executable drawing payloads.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetGlyph>))]
public enum WidgetGlyph
{
    Music,
    Play,
    Pause,
    Previous,
    Next,
    Refresh,
    Shuffle,
    Like,
    Dislike,
    Repeat,
    Settings,
    Warning,
    Check,
    Connection,
    Volume,
    Muted,
    Microphone,
    Wifi,
    Ethernet,
}

[JsonConverter(typeof(JsonStringEnumConverter<ControllerButton>))]
public enum ControllerButton
{
    A,
    B,
    X,
    Y,
    LeftBumper,
    RightBumper,
    LeftTrigger,
    RightTrigger,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    LeftStick,
    RightStick,
    Menu,
    View,
}

[JsonConverter(typeof(JsonStringEnumConverter<ControllerEventPhase>))]
public enum ControllerEventPhase
{
    Pressed,
    Released,
    Repeated,
}

public sealed record FocusNeighbors(
    string? Up = null,
    string? Down = null,
    string? Left = null,
    string? Right = null);

public sealed record ControllerShortcut(
    ControllerButton Button,
    string ActionId,
    ControllerEventPhase Phase = ControllerEventPhase.Pressed);

/// <summary>
/// Names the one exact control-capability operation a dashboard quick action may
/// invoke. This metadata is only a request for host authority: the package must
/// still declare the capability and the user must still grant consent.
/// </summary>
public sealed record WidgetQuickActionCapability(
    string CapabilityId,
    string OperationId);

/// <summary>
/// A bounded, non-navigation action the host may offer while this widget's dashboard card is selected.
/// The host owns dashboard navigation and decides how to present the label/button prompt.
/// </summary>
public sealed record WidgetQuickAction(
    ControllerButton Button,
    string ActionId,
    string Label,
    WidgetQuickActionCapability? Capability = null);

/// <summary>A renderer-neutral node. Properties that do not apply to Kind must be null.</summary>
public sealed record ViewNode
{
    public required string Id { get; init; }
    public required ViewNodeKind Kind { get; init; }
    public string? Text { get; init; }
    public string? AccessibilityLabel { get; init; }
    /// <summary>A localized, human-readable value announced for value controls.</summary>
    public string? AccessibilityValue { get; init; }
    public string? ActionId { get; init; }
    public double? Value { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Step { get; init; }
    /// <summary>Receives a quantized absolute RequestedValue when a Slider changes.</summary>
    public string? ValueChangedActionId { get; init; }
    public string? ImageSource { get; init; }
    public ImageFit? ImageFit { get; init; }
    public WidgetGlyph? Glyph { get; init; }
    public LoadingIndicatorSize? IndicatorSize { get; init; }
    /// <summary>
    /// Primary content direction for an ActionSurface. It is intentionally
    /// semantic rather than pixel geometry; responsive wrapping remains a
    /// host/theme concern.
    /// </summary>
    public ActionSurfaceOrientation? ActionSurfaceOrientation { get; init; }
    /// <summary>
    /// Smallest desired logical-DIP column width for a responsive Grid. The
    /// host computes a stable column count from the grid's actual content
    /// width; it never treats this value as a physical-pixel measurement.
    /// </summary>
    public double? GridMinimumColumnWidth { get; init; }
    /// <summary>
    /// Optional author cap on responsive columns. Omitting it lets the host use
    /// any safe count allowed by the protocol and available width.
    /// </summary>
    public int? GridMaximumColumns { get; init; }
    public bool? IsDisabled { get; init; }
    public bool? IsSelected { get; init; }
    public bool? IsBusy { get; init; }
    public FocusNeighbors? Focus { get; init; }
    /// <summary>
    /// Starts a nested controller input surface. Only stack, row, scroll, and grid containers
    /// may declare one; the root is always the default surface.
    /// </summary>
    public string? InputScopeId { get; init; }
    /// <summary>
    /// Selects the bounded axis for a Scroll node. The host owns the offset,
    /// clips descendants, and reveals controller focus; widgets never publish pixels.
    /// </summary>
    public ScrollAxis? ScrollAxis { get; init; }
    public IReadOnlyList<string> StyleClasses { get; init; } = [];
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];
    public IReadOnlyList<ViewNode> Children { get; init; } = [];

    [JsonIgnore]
    public bool IsFocusable => Kind is
        ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface;
}

public sealed record ViewSnapshot
{
    public int ProtocolVersion { get; init; } = ProtocolConstants.CurrentVersion;
    public required long Sequence { get; init; }
    public required string WidgetInstanceId { get; init; }
    /// <summary>The public ID of the one input surface currently accepting open-widget input.</summary>
    public required string ActiveInputScopeId { get; init; }
    public string? InitialFocusId { get; init; }
    public IReadOnlyList<WidgetQuickAction> QuickActions { get; init; } = [];
    /// <summary>Version-2, host-clamped sizing hints for this exact view.</summary>
    public WidgetSurfaceHints? Surface { get; init; }
    public required ViewNode Root { get; init; }
}
