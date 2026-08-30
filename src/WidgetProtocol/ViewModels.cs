using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

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
    TextEntry,
    MediaViewport,
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
/// Selects how controller focus interacts with a horizontal Slider. Omission
/// and <see cref="Direct"/> preserve the protocol-v3 behavior.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SliderInteractionMode>))]
public enum SliderInteractionMode
{
    Direct,
    ActivateToAdjust,
}

/// <summary>
/// Selects whether a host-owned TextEntry carries an ordinary authored value
/// or a transient sensitive value that must never enter presentation state.
/// Omission is equivalent to <see cref="Ordinary"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TextEntryInputKind>))]
public enum TextEntryInputKind
{
    Ordinary,
    Sensitive,
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
/// Selects one independent axis policy for the host-owned widget surface.
/// Preferred preserves the authored stable extent, Content measures the
/// immutable view within authored bounds, and FillAvailable consumes the safe
/// work-area extent admitted by the host.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetSurfaceAxisMode>))]
public enum WidgetSurfaceAxisMode
{
    Preferred,
    Content,
    FillAvailable,
}

/// <summary>
/// Host-resolved responsive visibility. Conditional nodes remain in the
/// immutable snapshot, but the host excludes an inactive node and its complete
/// subtree from layout, paint, pointer input, controller focus, shortcuts, and
/// accessibility exposure.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResponsiveVisibility>))]
public enum ResponsiveVisibility
{
    Always,
    CompactOnly,
    ExpandedOnly,
}

/// <summary>
/// Bounded logical-DIP hints for the currently published view. The host may
/// choose any smaller or larger safe size; widgets must remain responsive.
/// Numeric width/height pairs are atomic, while the typed sizing policy for
/// each axis is selected independently.
/// </summary>
public sealed record WidgetSurfaceHints
{
    public WidgetSurfaceMode Mode { get; init; } = WidgetSurfaceMode.Adaptive;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WidgetSurfaceAxisMode WidthMode { get; init; } = WidgetSurfaceAxisMode.Preferred;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WidgetSurfaceAxisMode HeightMode { get; init; } = WidgetSurfaceAxisMode.Preferred;
    public double? PreferredWidth { get; init; }
    public double? PreferredHeight { get; init; }
    public double? MinimumWidth { get; init; }
    public double? MinimumHeight { get; init; }
}

/// <summary>
/// One optional, bounded sizing profile for the host-owned pinned projection.
/// A protocol-v21 layout may additionally carry one declarative projection.
/// It grants no window, renderer, provider, or independent input/focus authority.
/// </summary>
public sealed record PinnedPresentationLayout
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required WidgetSurfaceHints Surface { get; init; }
    public ViewNode? Root { get; init; }
    public string? ActiveInputScopeId { get; init; }
    public string? InitialFocusId { get; init; }
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
    RepeatOne,
    Settings,
    Warning,
    Check,
    Connection,
    Volume,
    Muted,
    Microphone,
    Wifi,
    Ethernet,
    Rewind,
    FastForward,
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

[JsonConverter(typeof(JsonStringEnumConverter<ControllerActionRepeatPolicy>))]
public enum ControllerActionRepeatPolicy
{
    None,
    WhileHeld,
}

public sealed record FocusNeighbors(
    string? Up = null,
    string? Down = null,
    string? Left = null,
    string? Right = null);

public sealed record ControllerShortcut(
    ControllerButton Button,
    string ActionId,
    ControllerEventPhase Phase = ControllerEventPhase.Pressed,
    ControllerActionRepeatPolicy RepeatPolicy = ControllerActionRepeatPolicy.None);

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
    WidgetQuickActionCapability? Capability = null,
    ControllerActionRepeatPolicy RepeatPolicy = ControllerActionRepeatPolicy.None);

[JsonConverter(typeof(JsonStringEnumConverter<VirtualCollectionWindowChange>))]
public enum VirtualCollectionWindowChange
{
    Replace,
    Append,
    Prepend,
}

/// <summary>
/// Protocol-v19 bounded projection of a widget-private collection. The
/// admitted keyed children remain the only live semantic nodes. Known logical
/// positions let the host reserve estimated off-window extent without
/// materializing those private items.
/// </summary>
public sealed record VirtualCollectionWindow
{
    public required long RequestGeneration { get; init; }
    public required VirtualCollectionWindowChange Change { get; init; }
    public long? FirstItemIndex { get; init; }
    public long? TotalItemCount { get; init; }
    public required bool HasBefore { get; init; }
    public required bool HasAfter { get; init; }
    public required double EstimatedItemExtent { get; init; }
}

/// <summary>A renderer-neutral node. Properties that do not apply to Kind must be null.</summary>
public sealed record ViewNode
{
    public required string Id { get; init; }
    public required ViewNodeKind Kind { get; init; }
    /// <summary>
    /// Optional protocol-v9 responsive condition. Omission is equivalent to
    /// <see cref="ResponsiveVisibility.Always"/>.
    /// </summary>
    public ResponsiveVisibility? VisibleWhen { get; init; }
    public string? Text { get; init; }
    public string? AccessibilityLabel { get; init; }
    /// <summary>A localized, human-readable value announced for value controls.</summary>
    public string? AccessibilityValue { get; init; }
    public string? ActionId { get; init; }
    /// <summary>Current bounded value for a host-owned text-entry modal.</summary>
    public string? TextEntryValue { get; init; }
    public string? TextEntryPlaceholder { get; init; }
    public int? TextEntryMaximumLength { get; init; }
    public TextEntryInputKind? TextEntryInputKind { get; init; }
    public double? Value { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Step { get; init; }
    /// <summary>Receives a quantized absolute RequestedValue when a Slider changes.</summary>
    public string? ValueChangedActionId { get; init; }
    /// <summary>
    /// Optional protocol-v10 controller interaction policy. ActivateToAdjust
    /// reserves A and B for entering and leaving host-owned adjustment mode.
    /// </summary>
    public SliderInteractionMode? SliderInteractionMode { get; init; }
    public string? ImageSource { get; init; }
    /// <summary>
    /// Optional protocol-v14 host-resolved artwork identity. It is opaque to
    /// widgets and grants no path, URL, file, network, or decode authority.
    /// </summary>
    public string? ArtworkHandle { get; init; }
    /// <summary>
    /// Protocol-v23 identity of the one top-level EmbeddedMedia declaration
    /// whose pixels are placed inside this native layout node.
    /// </summary>
    public string? MediaSurfaceId { get; init; }
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
    /// <summary>
    /// Optional protocol-v13 identity shared only by mutually exclusive
    /// presentations of one logical focus destination. It does not identify an
    /// action and must not be used for dispatch or domain routing.
    /// </summary>
    public string? FocusPersistenceId { get; init; }
    public FocusNeighbors? Focus { get; init; }
    /// <summary>
    /// Starts a nested controller input surface. Only stack, row, scroll, and grid containers
    /// may declare one; the root is always the default surface.
    /// </summary>
    public string? InputScopeId { get; init; }
    /// <summary>
    /// Protocol-v33 opt-in remembered-child focus entry. Only Stack, Row,
    /// Scroll, and Grid containers may name an always-available focusable
    /// descendant in their own input scope.
    /// </summary>
    public string? InitialChildFocusId { get; init; }
    /// <summary>
    /// Selects the bounded axis for a Scroll node. The host owns the offset,
    /// clips descendants, and reveals controller focus; widgets never publish pixels.
    /// </summary>
    public ScrollAxis? ScrollAxis { get; init; }
    /// <summary>
    /// Optional protocol-v11 actions emitted once controller or keyboard focus
    /// approaches the leading or trailing edge of this scroll container.
    /// The host owns detection; widgets own paging and cache policy.
    /// </summary>
    public string? ScrollNearStartActionId { get; init; }
    public string? ScrollNearEndActionId { get; init; }
    public int? ScrollPaginationThreshold { get; init; }
    /// <summary>
    /// Optional protocol-v19 logical presentation window. Only the admitted
    /// keyed descendants are semantic; the host owns estimated off-window
    /// extent, scrolling, clipping, focus, layout, and accessibility.
    /// </summary>
    public VirtualCollectionWindow? VirtualCollectionWindow { get; init; }
    /// <summary>
    /// Protocol-v14 keyed collection anchor retained at the same viewport
    /// position when children are appended, prepended, refreshed, or evicted.
    /// </summary>
    public string? CollectionAnchorKey { get; init; }
    /// <summary>
    /// Protocol-v14 stable identity for one direct collection item. This key
    /// is presentation identity only and never authorizes an action.
    /// </summary>
    public string? CollectionItemKey { get; init; }
    public IReadOnlyList<string> StyleClasses { get; init; } = [];
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];
    public IReadOnlyList<ViewNode> Children { get; init; } = [];

    [JsonIgnore]
    public bool IsFocusable => Kind is
        ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface or
        ViewNodeKind.TextEntry;
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
    /// <summary>Versioned, host-clamped sizing hints for this exact view.</summary>
    public WidgetSurfaceHints? Surface { get; init; }
    public IReadOnlyList<PinnedPresentationLayout> PinnedLayouts { get; init; } = [];
    /// <summary>
    /// Optional protocol-v22 declaration for one host-owned embedded-media
    /// surface. The host retains HWND, composition, input, focus,
    /// accessibility, geometry, navigation, and teardown authority. Adapter
    /// files are normalized package-relative references resolved only through
    /// the installed package's verified content inventory.
    /// </summary>
    public EmbeddedMediaSurface? EmbeddedMedia { get; init; }
    public required ViewNode Root { get; init; }
}

public enum EmbeddedMediaCommand
{
    Previous,
    Next,
    Activate,
    Back,
    TogglePlayback,
    SeekBackward,
    SeekForward,
}

public enum EmbeddedMediaPlaybackCommandKind
{
    Load,
    Cue,
    Play,
    Pause,
    Seek,
    SetVolume,
    SetPlaybackRate,
    SetMuted,
    SetLoop,
}

public enum EmbeddedMediaPlaybackState
{
    Loading,
    Ready,
    Playing,
    Paused,
    Ended,
    Error,
}

/// <summary>One monotonic, bounded package request for the current media surface.</summary>
public sealed record EmbeddedMediaPlaybackCommand
{
    public required long Sequence { get; init; }
    public required EmbeddedMediaPlaybackCommandKind Kind { get; init; }
    public required string MediaKey { get; init; }
    public double? PositionSeconds { get; init; }
    public double? Volume { get; init; }
    public double? PlaybackRate { get; init; }
    public bool? Muted { get; init; }
    public bool? Loop { get; init; }
}

/// <summary>A validated host observation from the exact current embedded-media adapter.</summary>
public sealed record EmbeddedMediaPlaybackEvent
{
    public required string SurfaceId { get; init; }
    public required long Sequence { get; init; }
    public required long CommandSequence { get; init; }
    public required string MediaKey { get; init; }
    public required EmbeddedMediaPlaybackState State { get; init; }
    public required double PositionSeconds { get; init; }
    public required double DurationSeconds { get; init; }
    public required double Volume { get; init; }
    public double PlaybackRate { get; init; } = 1.0;
    public bool Muted { get; init; }
    public bool Loop { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record EmbeddedMediaResource
{
    public required string Path { get; init; }
    public required string ContentType { get; init; }
}

/// <summary>
/// A closed, package-local adapter contract for a single host-owned media
/// surface. This is not a browser, navigation, DOM, or script API.
/// </summary>
public sealed record EmbeddedMediaSurface
{
    public required string Id { get; init; }
    public required string AccessibleName { get; init; }
    public required string EntryAsset { get; init; }
    public required WidgetSurfaceHints Surface { get; init; }
    public required double AspectRatio { get; init; }
    public IReadOnlyList<EmbeddedMediaResource> Resources { get; init; } = [];
    public IReadOnlyList<EmbeddedMediaCommand> Commands { get; init; } = [];
    public IReadOnlyList<string> AllowedFrameOrigins { get; init; } = [];
    public IReadOnlyList<string> AllowedFrameDomainFamilies { get; init; } = [];
    public EmbeddedMediaPlaybackCommand? PendingCommand { get; init; }
    /// <summary>Opts the pinned surface into the host-owned compact media player.</summary>
    public bool CompactPinnedPresentation { get; init; }
    /// <summary>
    /// Optional bounded scrub step for every host-owned media presentation that
    /// seeks on the widget's behalf: the compact pinned player and overlay
    /// fullscreen. The host default applies when omitted.
    /// </summary>
    public double? MediaSeekStepSeconds { get; init; }
    /// <summary>
    /// Retains an already-resident controller/document while this snapshot
    /// intentionally contains no MediaViewport. It does not create a hidden
    /// viewport or permit an undeclared background session.
    /// </summary>
    public bool RetainSessionWhenHidden { get; init; }
    /// <summary>
    /// Declares that this media surface may be presented fullscreen inside the
    /// existing overlay window. This is a capability, not a state: the host owns
    /// whether fullscreen is currently active, entering it through the reserved
    /// enter action and leaving it on B. The webpage remains a bounded pixel
    /// plane and can neither request nor exit fullscreen itself.
    /// </summary>
    public bool OverlayFullscreenCapable { get; init; }
}
