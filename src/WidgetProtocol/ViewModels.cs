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
    BackgroundSurface,
    FocusPresentationSurface,
    Select,
}

/// <summary>One bounded option in a host-owned anchored Select popup.</summary>
public sealed record WidgetSelectOption(
    string Id,
    string Label,
    string ActionId,
    bool IsSelected = false,
    WidgetGlyph? Glyph = null,
    string? AccessibilityLabel = null,
    bool IsDisabled = false,
    bool IsBusy = false)
{
    public WidgetPackageIcon? PackageIcon { get; init; }
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

/// <summary>
/// Selects one closed host-owned visual composition for an ActionSurface.
/// Omission preserves the ordinary flow layout. Poster composes one optional
/// Cover artwork child behind one bounded bottom content subtree while the
/// ActionSurface remains the only interaction and accessibility target.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActionSurfacePresentation>))]
public enum ActionSurfacePresentation
{
    Standard,
    Poster,
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
/// A widget's preferred host-owned surface treatment. This is a bounded hint:
/// user and accessibility policy remain authoritative and may choose a safer
/// effective treatment.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetSurfaceAppearance>))]
public enum WidgetSurfaceAppearance
{
    Theme,
    Transparent,
    Solid,
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
    public WidgetSurfaceAppearance Appearance { get; init; } = WidgetSurfaceAppearance.Theme;
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
/// Widgets may select these native glyphs or reference a separately admitted
/// manifest SVG; they never supply inline paths, fonts, or executable drawing payloads.
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
    Fullscreen,
}

/// <summary>Controls whether a declared package SVG keeps its authored colors or supplies a reusable alpha mask.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetPackageIconColorMode>))]
public enum WidgetPackageIconColorMode
{
    OriginalColor,
    ThemeTint,
}

/// <summary>
/// References one manifest-declared static SVG by logical ID. The sibling
/// glyph remains the semantic fallback and is required whenever this value is
/// present. Package paths and XML never enter a widget snapshot.
/// </summary>
public sealed record WidgetPackageIcon(string AssetId, WidgetPackageIconColorMode ColorMode);

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

/// <summary>
/// Binds one controller input to one action. An optional bounded label lets the
/// host describe that exact binding in the open-widget controller guide; it
/// grants no additional input or action authority.
/// </summary>
public sealed record ControllerShortcut(
    ControllerButton Button,
    string ActionId,
    ControllerEventPhase Phase = ControllerEventPhase.Pressed,
    ControllerActionRepeatPolicy RepeatPolicy = ControllerActionRepeatPolicy.None,
    string? Label = null);

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

[JsonConverter(typeof(JsonStringEnumConverter<WidgetContextActionStyle>))]
public enum WidgetContextActionStyle
{
    Default,
    Danger,
}

/// <summary>
/// Protocol-v34 bounded secondary action for one ActionSurface. The host owns
/// menu placement, focus, dismissal, and accessibility; invoking an enabled
/// item emits the same typed action event as ordinary widget activation.
/// </summary>
public sealed record WidgetContextAction(
    string ActionId,
    string Label,
    WidgetContextActionStyle Style = WidgetContextActionStyle.Default,
    bool IsDisabled = false,
    bool IsBusy = false);

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
    public IReadOnlyList<WidgetContextAction> ContextActions { get; init; } = [];
    public IReadOnlyList<WidgetSelectOption> SelectOptions { get; init; } = [];
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
    /// Optional protocol-v39 trusted artwork selected only while this exact
    /// focusable node owns host focus inside an opted-in BackgroundSurface.
    /// </summary>
    public string? FocusBackgroundArtworkHandle { get; init; }
    /// <summary>
    /// Protocol-v23 identity of the one top-level EmbeddedMedia declaration
    /// whose pixels are placed inside this native layout node.
    /// </summary>
    public string? MediaSessionId { get; init; }
    public ImageFit? ImageFit { get; init; }
    public WidgetGlyph? Glyph { get; init; }
    public WidgetPackageIcon? PackageIcon { get; init; }
    public LoadingIndicatorSize? IndicatorSize { get; init; }
    /// <summary>
    /// Primary content direction for an ActionSurface. It is intentionally
    /// semantic rather than pixel geometry; responsive wrapping remains a
    /// host/theme concern.
    /// </summary>
    public ActionSurfaceOrientation? ActionSurfaceOrientation { get; init; }
    /// <summary>
    /// Optional protocol-v37 ActionSurface visual composition. Omission is
    /// equivalent to <see cref="WidgetProtocol.ActionSurfacePresentation.Standard"/>.
    /// </summary>
    public ActionSurfacePresentation? ActionSurfacePresentation { get; init; }
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
    /// Protocol-v39 opt-in for a BackgroundSurface to consume the exact
    /// focused descendant's focus-background artwork declaration.
    /// </summary>
    public bool? UsesFocusedDescendantArtwork { get; init; }
    /// <summary>
    /// Protocol-v40 bounded presentation-only fragment selected when this
    /// exact focusable node owns host focus inside its nearest enclosing
    /// FocusPresentationSurface. It grants no action, focus, scope, shortcut,
    /// scrolling, media, or alternate input authority.
    /// </summary>
    public ViewNode? FocusPresentation { get; init; }
    /// <summary>
    /// Protocol-v40 required fallback fragment for a FocusPresentationSurface.
    /// The host projects it when the exact focused descendant has no associated
    /// fragment or belongs to a nested consumer.
    /// </summary>
    public ViewNode? DefaultFocusPresentation { get; init; }
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
        ViewNodeKind.TextEntry or ViewNodeKind.Select;
}

public sealed record ViewSnapshot
{
    public int ProtocolVersion { get; init; } = ProtocolConstants.CurrentVersion;
    public required long Sequence { get; init; }
    public required string WidgetInstanceId { get; init; }
    /// <summary>The public ID of the one input surface currently accepting open-widget input.</summary>
    public required string ActiveInputScopeId { get; init; }
    public string? InitialFocusId { get; init; }
    /// <summary>
    /// Optional protocol-v44 one-shot request to enter an authored
    /// remembered-child focus group. The host consumes each positive request
    /// ID at most once for the current widget runtime and instance.
    /// </summary>
    public FocusGroupEntryRequest? FocusGroupEntryRequest { get; init; }
    public IReadOnlyList<WidgetQuickAction> QuickActions { get; init; } = [];
    /// <summary>Versioned, host-clamped sizing hints for this exact view.</summary>
    public WidgetSurfaceHints? Surface { get; init; }
    public IReadOnlyList<PinnedPresentationLayout> PinnedLayouts { get; init; } = [];
    /// <summary>
    /// Optional protocol-v42 declaration for one host-owned embedded-media
    /// session. The host retains controller/document lifetime, HWND,
    /// composition, input, focus, accessibility, geometry, navigation, and
    /// teardown authority. Adapter files are normalized package-relative
    /// references resolved only through the installed package's verified
    /// content inventory. Omitting this declaration closes the session;
    /// declaring it without a matching MediaViewport parks an existing session.
    /// </summary>
    public EmbeddedMediaSession? EmbeddedMediaSession { get; init; }
    public required ViewNode Root { get; init; }
}

public sealed record FocusGroupEntryRequest
{
    public required long RequestId { get; init; }
    public required string GroupId { get; init; }
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

/// <summary>Closed host-owned visual presentations supported by a media session.</summary>
public enum MediaPresentationKind
{
    OverlayFullscreen,
    CompactPinned,
}

/// <summary>One monotonic, bounded package request for the current media session.</summary>
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
    public required string SessionId { get; init; }
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
/// A closed, package-local adapter contract for one durable host-owned media
/// session. This is not a browser, navigation, DOM, or script API.
/// </summary>
public sealed record EmbeddedMediaSession
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
    /// <summary>Alternative host-owned presentations this session supports.</summary>
    public IReadOnlyList<MediaPresentationKind> SupportedPresentations { get; init; } = [];
    /// <summary>
    /// Optional bounded scrub step for every host-owned media presentation that
    /// seeks on the widget's behalf: the compact pinned player and overlay
    /// fullscreen. The host default applies when omitted.
    /// </summary>
    public double? MediaSeekStepSeconds { get; init; }
}
