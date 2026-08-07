using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<ViewNodeKind>))]
public enum ViewNodeKind
{
    Stack,
    Row,
    Text,
    Button,
    Progress,
    Spacer,
    Image,
    Icon,
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
/// A bounded, non-navigation action the host may offer while this widget's dashboard card is selected.
/// The host owns dashboard navigation and decides how to present the label/button prompt.
/// </summary>
public sealed record WidgetQuickAction(
    ControllerButton Button,
    string ActionId,
    string Label);

/// <summary>A renderer-neutral node. Properties that do not apply to Kind must be null.</summary>
public sealed record ViewNode
{
    public required string Id { get; init; }
    public required ViewNodeKind Kind { get; init; }
    public string? Text { get; init; }
    public string? AccessibilityLabel { get; init; }
    public string? ActionId { get; init; }
    public double? Value { get; init; }
    public double? Maximum { get; init; }
    public string? ImageSource { get; init; }
    public ImageFit? ImageFit { get; init; }
    public WidgetGlyph? Glyph { get; init; }
    public bool? IsDisabled { get; init; }
    public bool? IsSelected { get; init; }
    public bool? IsBusy { get; init; }
    public FocusNeighbors? Focus { get; init; }
    /// <summary>
    /// Starts a nested controller input surface. Only stack and row containers
    /// may declare one; the root is always the default surface.
    /// </summary>
    public string? InputScopeId { get; init; }
    public IReadOnlyList<string> StyleClasses { get; init; } = [];
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];
    public IReadOnlyList<ViewNode> Children { get; init; } = [];

    [JsonIgnore]
    public bool IsFocusable => Kind is ViewNodeKind.Button;
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
    public required ViewNode Root { get; init; }
}
