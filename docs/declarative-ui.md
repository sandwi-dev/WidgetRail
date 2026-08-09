# Declarative UI reference

Status: protocol, managed SDK, and generic native rendering implemented as an
integrated prototype; the physical accessibility/resolution matrix remains a
release evidence gate

Widgets return a semantic tree from `Widget.Render()`. The host owns layout,
pixels, focus presentation, accessibility, and controller dispatch. Widgets
cannot submit HTML, JavaScript, SVG, font glyphs, or arbitrary drawing paths.

The native renderer has a bounded accessibility-geometry path for visible
Text, Button, Slider, ActionSurface, Image, Icon, Progress, and LoadingIndicator
nodes. A pure host builder combines that final clipped geometry with the exact
widget/runtime/snapshot identity, active input scope, accessible names and
values, disabled/busy/selected/focused state, Invoke metadata, and Slider range.
ActionSurface descendants remain presentation-only and inactive responsive or
modal scopes are omitted. Collection is dormant until the HWND is first queried
by a UIA client, avoiding per-frame accessibility allocations for sessions that
never use the provider. Once active, a projection tracker rebuilds only for
snapshot, focus, scope, viewport, DPI, appearance, or accessibility-policy
changes. Paint-only motion retains the immutable tree and publishes final
geometry once when the transition settles.

The shipping HWND now publishes this immutable open-widget surface through
`WM_GETOBJECT` and Windows UI Automation. Button and ActionSurface nodes expose
Invoke, Slider exposes writable RangeValue, and bounded Progress exposes
read-only RangeValue. Provider reads never call the worker. Invoke, SetValue,
and SetFocus enter one bounded host queue; the window thread revalidates widget,
runtime generation, snapshot sequence, active scope, node identity, action ID,
and enabled state before routing controller input or changing focus. RangeValue
updates are quantized and coalesced latest-wins per Slider. A real
`IUIAutomation` client test covers `WM_GETOBJECT`, fragment traversal, names,
geometry, patterns, and stale-generation rejection.

Publication diffs the last announced immutable tree against the newest tree and
coalesces intervening renders behind one posted window message. The window
thread raises UIA structure invalidation, logical-focus changes, and closed
property changes for name, help text, enabled/selected state, RangeValue
value/limits/step/read-only state, and physical screen bounds. DPI, window-
origin, and root-size changes update root and node bounds. Event planning and
UIA calls run outside paint, and a real client-handler test covers structure,
property, and focus delivery.

Every provider captures one HWND binding generation. Before window destruction,
the host disconnects UIA, clears message/action authority, and invalidates that
generation; retained roots and fragments remain unavailable even if Windows
reuses the same handle. Root focus and visibility are published by the window
thread rather than queried with thread-local Win32 state from a free-threaded
callback. Clearing or hiding the semantic tree does not synthesize a focus event
on the custom root.

While controller focus is on the dashboard/open-widget tray, the provider
publishes the exact visible carousel window instead of inactive widget controls.
Each tile is a ListItem with single-selection and Invoke patterns, its selected,
focused, and enabled states, and the shared paint/pointer rectangle. Select and
Invoke carry a closed host action plus stable widget target; the UI thread exits
reorder mode, revalidates the current host-tree sequence, and uses the existing
tray state machine rather than parsing command strings.

This is not yet the complete screen-reader ship gate. Dashboard tiles are not
fully described by title/status nodes, legacy MSAA is not implemented, and a
packaged Narrator smoke test remains pending. Until those close, treat UIA as an
implemented preview and keep deterministic semantic snapshots as the primary
accessibility contract.

## Elements

| SDK call | Protocol kind | Purpose |
| --- | --- | --- |
| `UI.Stack(id, children)` | `stack` | Vertical semantic container. |
| `UI.Row(id, children)` | `row` | Horizontal semantic container. |
| `element.VisibleWhen(mode)` / `UI.ResponsiveBranch(mode, element)` | unchanged wrapped kind | Protocol-v9 host-resolved conditional subtree; adds no layout container. |
| `UI.ResponsiveGrid(id, minimumColumnWidth, maximumColumns?, children)` | `grid` | Protocol-v8 row-major container that derives bounded columns from available logical width. |
| `UI.VerticalScroll(id, children)` | `scroll` | Host-owned vertical viewport with controller focus-follow. |
| `UI.HorizontalScroll(id, children)` | `scroll` | Host-owned horizontal viewport with controller focus-follow. |
| `UI.Scroll(id, axis, children)` | `scroll` | Axis-explicit form of the same bounded viewport. |
| `UI.Text(text, id, accessibilityLabel?)` | `text` | Non-interactive text. |
| `UI.CodeText(text, id, accessibilityLabel?)` | `text` | Bounded non-interactive diagnostics/command text with the semantic monospace class. |
| `UI.Button(label, action, id)` | `button` | Focusable action control; may include one semantic glyph or bounded leading PNG inside the same focus target. |
| `UI.ToggleButton(label, isOn, action, id)` | `button` | Controller-ready two-state button composed from existing button semantics. |
| `UI.Stepper(label, value, decrementAction, incrementAction, id, canDecrement?, canIncrement?)` | `row`, `text`, `button` | Label/value row with separate bounded decrement and increment actions. |
| `UI.Progress(value, maximum, id, accessibilityLabel?)` | `progress` | Read-only bounded progress where `0 <= value <= maximum` and `maximum > 0`. |
| `UI.Slider(value, minimum, maximum, step, valueChangedAction, id, accessibilityLabel, accessibilityValue?, activationAction?)` | `slider` | Protocol-v3 controller value control with absolute requested values. |
| `UI.Scrubber(position, duration, step, valueChangedAction, id, ...)` | `stack`, `slider`, `row`, `text` | Media seek composition with one Slider focus stop, absolute millisecond targets, and responsive elapsed/duration labels. |
| `UI.Spacer(id)` | `spacer` | Layout spacing node. |
| `UI.Image(httpsSource, id, accessibilityLabel, fit?)` | `image` | HTTPS image with required accessible alternative text. |
| `UI.InlinePngImage(pngBase64, id, accessibilityLabel, fit?)` | `image` | Protocol-v6 bounded host-decoded PNG pixels for trusted broker artwork; never a native path. |
| `UI.Icon(glyph, id, accessibilityLabel)` | `icon` | Host-rendered semantic vector icon from a closed enum. |
| `UI.LoadingIndicator(id, accessibilityLabel, size?)` | `loadingIndicator` | Protocol-v5 nonfocusable native activity arc with Compact, Standard, or Large sizing. |
| `UI.ActionSurface(action, id, accessibilityLabel, orientation, children...)` | `actionSurface` | Protocol-v7 rich full-surface action whose bounded descendants are presentation only. |
| `UI.MediaTile(...)`, `UI.AppTile(...)` | `actionSurface` | Controller-first tile compositions with optional artwork, multiline copy, visible state, and one full-tile target. |
| `UI.Toast(title, message, tone, id, duration?, glyph?)` | baseline `row`, `stack`, `text`, `icon` | Nonfocusable lifecycle-owned notification with bounded copy, tone, and duration metadata. |
| `UI.IconButton(glyph, action, id, accessibilityLabel, variant?, size?)` | `button` | Accessible icon-only action with controller-safe semantic classes. |
| `UI.SettingsRow(label, action, id, ...)` | `stack`, `row`, `text`, `button` | Responsive setting summary whose `id.action` Button is its only focus stop. |
| `UI.ActionSheet(title, id, scopeId, backAction, items, description?)` | `stack`, `scroll`, `button` | Bounded 1–32 item nested action scope with stable item focus IDs and scope-owned B. |
| `UI.Picker(title, id, scopeId, backAction, options, description?)` | `stack`, `scroll`, `button` | Bounded 1–128 option single-select scope with explicit selected state, stable option IDs, and scope-owned B. |
| `UI.Card(id, variant?, children...)` | `stack` | Nonfocusable raised/subtle/transparent grouping surface. |
| `UI.SectionHeader(title, id, eyebrow?, description?, trailing?)` | `stack`, `row`, `text` | Stable title hierarchy with optional trailing content. |
| `UI.StatusBadge(label, tone, id, glyph?)` | `row`, `icon`, `text` | Nonfocusable status that never relies on color alone. |
| `UI.Divider(id)` | `spacer` | Decorative themeable separator. |
| `UI.Alert(...)`, `UI.EmptyState(...)` | `stack`, content, optional `button` | Bounded guidance with zero or one recovery focus stop. |
| `UI.SegmentedTabs(...)` | `row`, `button` | Bounded selected tab row with stable author IDs and explicit horizontal neighbors. |
| `UI.Switch(...)` | `button` | One focus stop with visible and accessible On/Off state. |
| `UI.ScopedDialog(...)` | `stack` plus supplied content | Nested input scope with scope-owned B and stable child IDs. |
| `UI.ValueRow(...)` | `row`, `stack`, `text`, optional `icon` | Read-only label/value metadata that never enters focus. |
| `UI.ChoiceRow(...)` | `button` | One full-row choice/action target with selected, Disabled, and Busy semantics. |
| `UI.ControllerHint(...)` | `row`, `text` | Display-only key/label pair; does not bind controller input. |

Stack, Row, Grid, and Scroll containers may call `.InputScope("scope-id")` to start a nested
controller input surface. The root is always the default input scope, so a
simple widget does not need to declare one. Those containers may also call
`.Shortcut(button, actionId)` for a surface-level action that must work without
focused content, such as B to dismiss a modal.

`LoadingIndicator` is indeterminate presentation, not a worker lifecycle or
polling mechanism. Its accessible label is required, and the protocol rejects
actions, focus neighbors, interaction state, and arbitrary numeric geometry on
the node. The host draws and advances the arc only while its visible geometry
intersects the widget viewport. Reduced-motion mode uses a static incomplete
arc, preserving the honest "work is in progress" meaning without movement.
Prefer keeping cached content in place for fast refreshes; do not flash a
single-frame indicator when work normally finishes before the next paint.
The bridge maps this node to the distinct `loadingIndicator` render role, not a
container role, so GBSS role selectors and native activity geometry agree.

## Rich action surfaces and tiles (protocol v7)

Use `UI.ActionSurface(...)` when a rich group must behave as one controller and
pointer target. The ActionSurface owns focus, A activation, pressed/selected/
disabled/busy state, optional shortcuts, and its accessible name. Its children
are visual content only: they cannot own actions, value changes, focus links,
input scopes, shortcuts, interaction state, nested Scroll, Slider, Button, or
another ActionSurface. The host clips the subtree to the surface and uses the
surface's complete border box for focus, hit testing, pressed feedback, and
state cues.

```csharp
var tile = UI.MediaTile(
    title: track.Title,
    stateLabel: track.IsPlaying ? "Playing" : "Paused",
    action: "track.open",
    id: $"track.{track.Id}",
    subtitle: track.Artist,
    metadata: track.Album,
    artwork: TileArtwork.FromHttps(track.ArtworkUrl, $"Artwork for {track.Title}"),
    accessibilityLabel: $"Open {track.Title} by {track.Artist}");
```

`UI.MediaTile(...)` and `UI.AppTile(...)` build that safe structure for common
content. Artwork is optional and accepts exactly one closed semantic glyph,
absolute credential-free HTTPS image, or canonical bounded inline PNG through
`TileArtwork.FromGlyph`, `FromHttps`, or `FromInlinePng`. Both tiles require a
title, visible state label, action, and stable base ID; subtitle and metadata
are optional. Horizontal is the default orientation and Vertical is available
for tall compositions.

The generated root classes are `.gbar-action-surface`, `.gbar-tile`, and
`.gbar-media-tile` or `.gbar-app-tile`. Generated child IDs are
`id.artwork` when present, `id.content`, `id.title`, optional `id.subtitle`,
optional `id.metadata`, and `id.state`; matching generic and media/app-specific
`gbar-*` classes are stable theme hooks. Keep the base ID stable because it is
the only focus/action identity.

ActionSurface content is intentionally bounded to 1–8 direct children, at most
32 total descendants, and at most four descendant levels relative to the
surface. Invalid trees fail in the SDK and protocol validator. A snapshot that
contains an ActionSurface automatically selects protocol v7. Older snapshots
continue using the lowest version their features require; an older host rejects
v7 instead of interpreting the rich action as a different control.

## Toast feedback

`UI.Toast(...)` creates brief, non-interactive feedback without adding a focus
stop or shortcut. Tones are Neutral, Info, Success, Warning, and Danger; text
must communicate the state because color is supplementary. Title and message
are bounded to 120 and 512 characters. Duration defaults to five seconds and
must be between two and thirty seconds.

Duration is author intent, not a host timer. Keep the Toast in widget state for
that duration and remove it through the widget's normal lifecycle-aware update.
Do not start a worker, ticker, or hidden-background residency solely to dismiss
or animate it. Persistent errors belong in the owning surface. Themes may use
short appearance/removal motion, but reduced motion must suppress or shorten it.
Generated IDs are `id.icon` when a semantic icon is present, `id.copy`,
`id.title`, and `id.message`; stable classes include `.gbar-toast`, the tone
modifier, and matching `__icon`, `__copy`, `__title`, and `__message` hooks.

## Controller scroll containers

Use a semantic Scroll container when a surface can contain more controller
targets than its clamped viewport. Do not implement a hidden index, LB/RB
cycling, or widget-owned pixel offsets:

```csharp
var sessions = UI.VerticalScroll("audio.sessions",
    sessionRows.Select(BuildSessionRow).ToArray())
    .Classes("session-list");
```

The host measures the full content extent along the declared axis, clips it to
the container's content box, and clamps its offset to that measured extent.
Offscreen buttons remain valid D-pad/analog navigation targets. When focus
moves, the host scrolls the nearest edge just far enough to make the complete
control visible before painting it. A widget never receives or publishes the
offset.

Keep the Scroll ID and descendant button IDs stable across snapshots. Offset
memory is isolated by exact widget runtime instance, active input scope, and
Scroll container ID, so closing/reopening a widget or nested surface restores
the focused row without leaking state to another version or modal. If a
dynamic item disappears, host focus memory selects the focusable control nearest
its prior tree position and the Scroll container reveals it. Runtime
replacement clears both focus and scroll state.

Scroll containers may start an input scope and declare scope shortcuts exactly
like Stack/Row. GBSS can target their `scroll` role or a stable ID/class. The
axis is semantic and cannot be changed by GBSS; this prevents a theme from
breaking controller navigation. Non-finite, negative, or excessive internal
offsets are clamped by the native layout engine, and an unknown/missing axis is
rejected before publication.

Protocol v11 optionally adds host-owned focus-edge actions through
`ScrollElement.Paginate(nearStartActionId, nearEndActionId, threshold)`. After
controller focus moves into the first/last threshold direct child in the
matching direction, the host sends the configured action with the Scroll ID as
its source. It does not fetch, cache, or append widget data and does not add a
visible **Load more** control.

For bounded offset-based provider data, use the public
`WidgetPagedResource<TItem>` documented in the [widget authoring
guide](widget-authoring-guide.md#bounded-offset-paged-resources). Its
`Paginate(scroll)` and `TryHandlePagination(...)` helpers bind this same v11
contract while the SDK owns page state, invalidation, Latest coordination,
bounded LRU caching, and entering-edge focus. It is an SDK state helper, not a
new declarative node. Cursor and append/infinite-feed resources are not part of
the current API.

## Per-view surface hints

`WidgetView.Surface` describes the useful shape of the *current view* without
requesting a window size. The host remains authoritative:

```csharp
return new WidgetView(
    root,
    InitialFocusId: "master-mute",
    Surface: new WidgetSurfaceHints
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 560,
        PreferredHeight = 420,
        MinimumWidth = 360,
        MinimumHeight = 260,
    });
```

Modes are `Adaptive`, `Compact`, `Standard`, and `Wide`. Explicit preferred and
minimum dimensions are optional logical-DIP pairs: specify both width and
height or neither. Accepted widths are 240–1600 DIPs and heights are 180–1200
DIPs; values must be finite, and a minimum cannot exceed its preferred value.
These bounds protect placement math, not the monitor: the shell may render
smaller than a requested minimum when the current work area or accessibility
scale leaves no alternative.

Suggested host defaults are 560×420 for Compact, 880×520 for Standard, and
1120×620 for Wide. Adaptive asks the shell to select from content/shell policy.
Explicit values refine the selected mode but do not bypass work-area, DPI,
text-scale, tray/footer, or minimum-control-size constraints. Widgets must
still reflow and use Scroll for overflow after the host clamps the surface.

### Responsive visibility (protocol v9)

Use `.VisibleWhen(ResponsiveVisibility.CompactOnly)` or `ExpandedOnly` when the
semantic hierarchy, not merely its spacing, must change with the final widget
surface. `UI.ResponsiveBranch(mode, element)` is the equivalent non-fluent
form. The modifier adds no native container and preserves the wrapped element's
kind, ID, classes, and complete subtree:

```csharp
UI.Stack("player.root",
    BuildCompactTransport()
        .VisibleWhen(ResponsiveVisibility.CompactOnly),
    BuildExpandedTransport()
        .VisibleWhen(ResponsiveVisibility.ExpandedOnly));
```

The host selects compact when the final surface is less than 960 logical DIPs
wide or 540 DIPs high. An inactive subtree has no layout, paint, pointer target,
controller focus, shortcut, or accessibility exposure. Keep the root
unconditional, give mutually exclusive branches distinct stable IDs, and do
not assume the worker knows which branch is active. Initial focus must have a
valid active fallback. Prefer ordinary Row wrapping or `ResponsiveGrid` when
the same semantic children only need a different arrangement.

### Explicit responsive focus persistence (protocol v13)

When two mutually exclusive responsive controls represent the same logical
focus destination, give them distinct element IDs and one shared bounded focus
identity:

```csharp
var compactPlay = UI.Button("Play", "transport.toggle", "compact.play")
    .PersistFocusAs("transport.play");
var expandedPlay = UI.Button("Play", "transport.toggle", "expanded.play")
    .PersistFocusAs("transport.play");
```

The host uses `focusPersistenceId` only when the prior presentation becomes
responsive-inactive and exactly one active control in the same input scope has
the same key. Ambiguous and cross-scope matches fail closed. Never use an action
ID as this key: action IDs describe routing and may legitimately be shared by
unrelated controls that distinguish `SourceElementId`. `UI.NavigationShell`
authors these persistence IDs automatically. Snapshots that omit the field keep
their earlier protocol version and exact-ID then deterministic fallback behavior.

### Responsive row wrapping

GBSS can reflow a semantic Row without publishing a different widget tree:

```css
row.quick-actions {
  flex-wrap: wrap;
  gap: 8px 12px;
  align: start;
}

.quick-action {
  flex-basis: 168px;
  min-width: 120px;
  flex-grow: 1;
}
```

`flex-wrap` accepts only `nowrap` (the default) or `wrap`. It applies to
non-scroll row containers; column and Scroll layouts ignore it with a native
diagnostic. The host forms lines from each child's bounded preferred width,
then applies grow/shrink and `justify` independently to each line. For wrapped
rows, a two-value `gap` is `row-gap column-gap`, so the example uses 8 DIPs
between lines and 12 DIPs between items. Intrinsic row height includes every
line and row gap.

Wrapped children keep their stable IDs and remain normal pointer/controller
targets. Use wrapping for a bounded action or metadata group whose items all
belong on one surface. Use a semantic Scroll, Picker, or nested page for an
unbounded collection; `wrap-reverse`, column wrapping, and browser-style
`align-content` are deliberately outside this bounded overlay contract.

### Presentation translation

GBSS `translate-x` and `translate-y` apply bounded presentation offsets without
changing a node's static layout allocation:

```css
.card { translate-y: 8px; }
.card:focused {
  translate-x: 0.5em;
  translate-y: 0px;
  transition-duration: 120ms;
}
```

Lengths accept the normal safe length units. Author values are bounded by unit
(`px` ±4096, `em`/`rem` ±16, and `%`/`vw`/`vh` ±100), then resolved and clamped
to ±4096 DIPs per axis. Percentages resolve against the corresponding parent
axis. Parent and child offsets accumulate within that final bound.

Translation moves the complete presented subtree. Paint, descendant clips,
pointer targets, focus outlines, controller-navigation boxes, accessibility
geometry, and Scroll focus-follow all use the same translated geometry. Layout
width/height and sibling allocation remain unchanged. Stable IDs animate
translation through the same bounded timeline as opacity/scale; rapid changes
retarget from the presented value, reduced motion snaps and cancels, a replaced
widget identity does not inherit stale motion, and settled or hidden content
does not keep requesting frames.

### Responsive Grid (protocol v8)

Use `UI.ResponsiveGrid(...)` when a bounded set of peer controls should reflow
as complete columns rather than wrap according to each child's preferred width:

```csharp
UI.ResponsiveGrid(
    id: "settings.category-grid",
    minimumColumnWidth: 250,
    maximumColumns: 2,
    appearance, accessibility, overlay, installedWidgets);
```

The minimum column width is 44–1600 logical DIPs and the optional cap is 1–32
columns. The host derives the actual count from the Grid's final content width
and authored row/column gap, clamps it to the number of children, and lays out
children in stable row-major document order. Mixed-height items share the
maximum row height. Empty grids are valid. A Grid may own an input scope and
scope shortcut, but the Grid itself is not a focus stop; its normal focusable
children retain their IDs and controller graph through every reflow.

Grid owns its responsive wrapping. Applying `flex-wrap` to it is ignored with
a diagnostic; use Row wrapping for a small flexible action group and Grid for
bounded peer tiles/categories. Put an unbounded collection inside a semantic
Scroll around the Grid. A snapshot containing Grid automatically negotiates
protocol v8; v7 hosts reject it rather than treating it as a Stack or Row.

### Semantic code and diagnostic text

`UI.CodeText(text, id, accessibilityLabel?)` emits one ordinary, nonfocusable
Text node with the stable `.gbar-code-text` class. It preserves whitespace and
eagerly bounds content/accessibility text to 4,096 characters. It owns no
action, selection, copy command, scope, or shortcut; add a separate explicit
Button when copying is a required workflow. The built-in theme uses the single
Windows-baseline `Consolas` family, `min-width: 0`, and up to eight wrapped
lines. GBSS currently passes one resolved family to DirectWrite, so comma-
separated browser-style fallback stacks must not be presented as native font
fallback support.

### Intrinsic text sizing and width constraints

Auto-height Text and Button leaves are measured at the content width they will
actually receive. An authored `width` or `max-width` is applied before the host
calculates wrapping and intrinsic height; node padding is excluded from that
content width and then added back to the border box. This makes a bounded
centered title, detail paragraph, or labeled action grow to its wrapped line
count instead of being measured as one wide line and clipped after layout.

`min-height` remains a lower bound, not a single-line height override. Use an
explicit `height` only when a deliberately fixed/clipped box is part of the
design. `max-lines` still caps layout and paint, while surfaces whose complete
content can exceed the host-clamped viewport must use a semantic Scroll; extra
spacers or margins are not a reliable overflow mechanism.

Button content uses one shared native placement rule. Text-only labels and
icon-plus-label groups are centered as a visual group; a trailing selected/
state cue reserves equal space on both sides so it cannot shift or overlap that
group. Widgets should use semantic `.Icon(...)`, label, and selected state
instead of compensating with private padding or spacer nodes.

Image fit is `Contain`, `Cover` (default), or `Fill`. General image sources must
be absolute HTTPS URLs with a host and no embedded credentials. A trusted
platform service may instead return PNG pixels for `UI.InlinePngImage`; protocol
v6 accepts only canonical RGBA8 PNG data up to 12 KiB and 64 by 64 pixels. The
host decodes those pixels in memory and never sends them through WinHTTP.
Redirect, download-size, decode-size, MIME, and cache policy are enforced by the
host image service; widgets never receive native image handles or filesystem paths.

For an application/media list action, attach those same trusted pixels to the
Button itself so icon, label, state cue, pointer hit area, and controller outline
remain one semantic target:

```csharp
UI.Button(app.DisplayName, "launch", app.Id)
    .LeadingInlinePng(app.IconPngBase64, ImageFit.Contain,
        $"Launch {app.DisplayName}");
```

`LeadingInlinePng` replaces `.Icon(...)`; the validator rejects two competing
leading visuals. Use a semantic `.Icon(...)` fallback when pixels are absent.

The closed `WidgetGlyph` set is `Music`, `Play`, `Pause`, `Previous`, `Next`,
`Refresh`, `Shuffle`, `Like`, `Dislike`, `Repeat`, `RepeatOne`, `Settings`, `Warning`,
`Check`, `Connection`, `Volume`, `Muted`, `Microphone`, `Wifi`, and `Ethernet`.
`RepeatOne` negotiates protocol v12 and renders an explicit numeral inside the
repeat mark so single-track repeat never depends on color alone.

The same closed glyphs can decorate a button without turning the button into
an arbitrary drawing surface:

```csharp
UI.Button("Play", "toggle", "play")
    .Icon(WidgetGlyph.Play, "Play or pause")
    .Shortcut(ControllerButton.X)
```

`ButtonElement.Icon(glyph, accessibilityLabel?)` keeps the visible text and
action semantics. Supplying an accessibility label replaces the button's
optional explicit label; otherwise the visible button text remains its name.
Glyphs are valid only on `icon` and `button` nodes.

## Controller-ready setting composites

`ToggleButton` and `Stepper` are SDK composition helpers, not new protocol node
kinds. They produce the same bounded semantic nodes as hand-authored UI, so
focus, validation, GBSS, accessibility, and controller dispatch do not need a
special renderer path.

```csharp
UI.Stack("appearance-settings",
    UI.ToggleButton(
        "Reduced motion",
        isOn: _reducedMotion,
        action: "toggle-reduced-motion",
        id: "reduced-motion"),
    UI.Stepper(
        "Text scale",
        $"{_textScale:P0}",
        decrementAction: "text-scale-down",
        incrementAction: "text-scale-up",
        id: "text-scale",
        canDecrement: _textScale > 0.85,
        canIncrement: _textScale < 1.50))
```

`ToggleButton` renders visible `On`/`Off` text, a matching accessibility label,
`.setting-toggle`, and selected state while on. The widget still owns the
value: handle its action, update state, and call `Invalidate()`.

`Stepper` creates stable child IDs by appending `.label`, `.decrement`,
`.value`, and `.increment` to its base ID. Keep the resulting IDs within the
128-character protocol limit and never change the base ID when the displayed
value changes. The two buttons expose independent action IDs, explicit
left/right focus neighbors, accessible `Decrease <label>` / `Increase <label>`
names, and disabled state at a bound. Its semantic classes are:

- `.setting-stepper` on the row;
- `.setting-stepper-label` and `.setting-stepper-value` on text;
- `.setting-stepper-button` on both buttons; and
- `.setting-stepper-decrement` / `.setting-stepper-increment` on the respective
  action.

The helper does not parse, clamp, persist, or mutate the displayed value.
Validate the domain in widget/host logic, then rebuild the composite from that
authoritative value. A remains the activation button; D-pad and analog focus
navigation continue through the normal host routing.

The modern component helpers and their stable `gbar-*` class contracts are
documented in [Controller UI component patterns](controller-ui-components.md).
Use `.AddClasses(...)` to augment those semantic classes. `.Classes(...)`
deliberately replaces the complete class list and is intended for primitives
or authors who explicitly take over the component contract.

Build and install the
[SDK Gallery Community addon](../samples/SdkGalleryWidget/README.md) to inspect
the components together under the real theme, Scroll, controller-focus, and
generic Community-worker contracts. It is a development reference rather than
a built-in tray widget and declares no platform capabilities.

## Controller-native Slider (protocol v3 and v10)

Use `UI.Slider` for a value the controller can change directly. `Progress` is
read-only and `Stepper` creates two focus stops; neither should be repurposed as
a draggable or controller-adjustable track.

```csharp
var volume = UI.Slider(
        value: _volume,
        minimum: 0,
        maximum: 1,
        step: 0.05,
        valueChangedAction: "player.volume.set",
        id: "player.volume",
        accessibilityLabel: "Player volume. Press A to mute",
        accessibilityValue: $"{Math.Round(_volume * 100)}%",
        activationAction: "player.mute.toggle")
    .FocusUp("player.output")
    .FocusDown("player.balance")
    .Classes("volume-slider");
```

While the Slider is focused:

- D-pad or left-stick Left/Right belongs to the Slider and adjusts by one
  minimum-anchored step. It never escapes into horizontal focus navigation,
  including at a range bound or while the control is unavailable.
- Up/Down remains normal focus navigation. A Slider may declare only
  `.FocusUp(...)` and `.FocusDown(...)`; horizontal focus neighbors are
  rejected.
- A invokes the optional `activationAction`. Without one, A is unhandled by
  the Slider. This lets one focus stop expose a related discrete action such
  as mute without adding an adjacent controller target.

That direct mode is appropriate for volume and values where focused Left/Right
unambiguously means adjustment. For a seek bar or another control that users
should traverse without changing, opt into protocol-v10 activation-first mode:

```csharp
var timeline = UI.Scrubber(
        position, duration, TimeSpan.FromSeconds(5),
        "player.seek", "player.timeline")
    .RequireControllerActivation()
    .FocusLeft("player.previous")
    .FocusRight("player.next");
```

Before activation, D-pad and left-stick directions use normal focus navigation,
including the optional horizontal neighbors. A enters a transient host-owned
adjustment mode; Left/Right then adjusts, and A or B exits without sending an
activation action. Focus change, surface close, or widget replacement also
clears the mode. `.RequireControllerActivation()` is available on both
`SliderElement` and `ScrubberElement`. It cannot be combined with `.Activate`
or `activationAction` because A and B are reserved for mode control. Disabled
or Busy controls remain focusable but cannot enter or operate adjustment mode.

The host clamps and quantizes a transient presentation target, repaints it
immediately, and sends that **absolute** target as
`WidgetActionEvent.RequestedValue`. Authors must assign the requested value;
do not increment the last widget value again:

```csharp
public override async ValueTask OnActionAsync(
    WidgetActionEvent action,
    CancellationToken cancellationToken = default)
{
    if (action.ActionId == "player.volume.set" &&
        action.RequestedValue is { } requested)
    {
        _volume = requested; // already an absolute, quantized target
        Invalidate();        // publish optimistic feedback before slow I/O
        await _player.SetVolumeAsync(requested, cancellationToken);
    }
}
```

Ranges require finite `minimum < maximum`, an in-range value, and
`0 < step <= maximum - minimum`. The accessibility label and localized,
human-readable accessibility value are required. The SDK rejects a stale
snapshot sequence, wrong input scope, wrong focus ID, non-finite/out-of-range
target, a target off the declared step grid, or a value moving opposite the
reported direction.

Repeated input can arrive faster than a provider round trip. The SDK serializes
controller actions and replaces only a contiguous pending tail for the exact
same `(input scope, Slider ID, value-changed action ID)` with its newest
absolute target. It never coalesces across a button action or another Slider,
so action ordering is preserved. Leaving the active Visible/Interactive
lifetime cancels the running action and discards pending values. Provider work
must honor the supplied cancellation token and reconcile later authoritative
events without letting stale data overwrite the pending intent.

## Stable IDs and limits

Every node requires a unique stable ID containing only ASCII letters, digits,
`.`, `-`, and `_`, up to 128 characters. IDs connect focus, styling,
accessibility, action sources, and host persistence; changing one is a state
migration.

The current snapshot limits include:

- protocol versions 1–11. Plain Stack/Row views remain v1; Scroll or explicit
  surface hints opt into v2, Slider into v3, capability-backed dashboard
  gestures into v4, LoadingIndicator into v5, inline PNG into v6, and
  ActionSurface into v7, responsive Grid into v8, responsive visibility into
  v9, activate-to-adjust Slider behavior into v10, and Scroll focus-edge
  pagination into v11. These additive snapshot features do not change package
  host API 1;
- at most 2,048 nodes;
- at most 32 levels of tree depth;
- strings up to 4,096 characters; and
- at most three dashboard quick actions.

Collections cannot be null. Unknown JSON members, unsupported enum values,
duplicate IDs, unsafe focus targets, and malformed node-specific properties
are rejected before publication.

## Focus and actions

Buttons, Sliders, and ActionSurfaces are focusable elements. Buttons and
ActionSurfaces use `.FocusUp(id)`,
`.FocusDown(id)`, `.FocusLeft(id)`, and `.FocusRight(id)` when automatic spatial
navigation would be ambiguous. Sliders accept only Up/Down neighbors because
they own Left/Right adjustment. `WidgetView.InitialFocusId` must name a
focusable node.

The host tries an explicit focusable neighbor first. Disabled and busy controls
remain focusable, so an asynchronous state change cannot teleport focus. If no
neighbor was declared, the native renderer falls back to geometry from the
last render. It favors a target
whose perpendicular span overlaps the current button, then forward distance,
lateral distance, and stable ID. It does not wrap focus at an edge.

```csharp
return new WidgetView(
    UI.Stack("player-window",
            UI.Row("transport",
                UI.Button("Previous", "previous", "previous")
                    .Icon(WidgetGlyph.Previous)
                    .FocusRight("play"),
                UI.Button("Play", "toggle", "play")
                    .Icon(WidgetGlyph.Play, "Play or pause")
                    .FocusLeft("previous")
                    .FocusRight("next"),
                UI.Button("Next", "next", "next")
                    .Icon(WidgetGlyph.Next)
                    .FocusLeft("play")))
        .InputScope("player-window")
        .Shortcut(ControllerButton.LeftBumper, "previous")
        .Shortcut(ControllerButton.X, "toggle")
        .Shortcut(ControllerButton.RightBumper, "next"),
    InitialFocusId: "play");
```

A pressed A activates a focused Button through its `ActionId`, or a Slider's
optional activation action. A shortcut can use the button's action or an explicit alternate action. Input resolves
against the latest host-rendered snapshot, not an unrendered state.

Non-A shortcuts are scoped to the explicitly active input surface. Set
`WidgetView.ActiveInputScopeId` when presenting a nested window; it defaults to
the root's public scope ID. The SDK first checks the focused node, then the
active scope-root container. It never searches an arbitrary unfocused child and
does not leak to a parent or sibling scope. A node may bind a button/phase only
once; separate focused controls and separate nested scopes may reuse the same
button because exact focus and active scope disambiguate them:

A Button-local shortcut is available only while that exact Button is focused.
Declare a window-wide shortcut once on the active scope-root container, as in
the transport example above; copying it across siblings is not a substitute.

```csharp
var root = UI.Stack("root",
    UI.Button("Refresh", "refresh-root", "refresh-root")
        .Shortcut(ControllerButton.Y),
    UI.Stack("dialog",
        UI.Text("Apply changes?", "dialog-title"),
        UI.Button("Confirm", "confirm", "confirm")
            .Shortcut(ControllerButton.Y))
        .InputScope("confirm-dialog")
        .Shortcut(ControllerButton.B, "close-dialog"));

return new WidgetView(
    root,
    InitialFocusId: _showDialog ? "confirm" : "refresh-root",
    ActiveInputScopeId: _showDialog ? "confirm-dialog" : "root");
```

When `confirm-dialog` is active, Y and the container's focusless B resolve only
inside that scope. The root binding cannot fire. Dashboard quick actions use
their separate bounded contract, and Guide/Home is never part of a widget input
scope. The MVP accepts only Pressed shortcuts and rejects A or D-pad shortcut
bindings because those buttons are reserved for activation and navigation.

Every snapshot contains a concrete `ActiveInputScopeId`; `WidgetView` defaults
it to the root container's `.InputScope(...)` value or root node ID. Every
open-widget controller event carries that ID and the rendered snapshot's
sequence. The SDK rejects stale sequences, a different active scope, or a focus
ID outside the published scope before it resolves an action.

The host owns current focus and remembers it per widget and scope. A widget
should keep IDs stable and publish an `InitialFocusId` for fallback, not attempt
to serialize current focus into its own state. On a new snapshot the host tries
the remembered focusable Button or Slider, then `InitialFocusId`, then the
first focusable control in the active scope. A scope with no focusable controls
may remain focusless; its Stack/Row/Grid/Scroll shortcut still resolves.

## Interaction state

Buttons support `.Disabled(condition)`, `.Selected(condition)`, and
`.Busy(condition)`. Sliders support Disabled and Busy. The states are
renderer-neutral and allow the host to provide native visual and accessibility
semantics. Disabled and busy controls remain in controller focus order, but the
default SDK router suppresses their A activation, shortcut, and value-change
actions. Selected buttons remain activatable.

Use Disabled for an action that is unavailable because of product state or
permissions. Use Busy for bounded work already in flight. Neither state means
hidden or non-navigable, and neither may trigger focus fallback. If a control
must leave navigation, remove it from the semantic tree and provide a stable
replacement focus target. A focused ID that survives a snapshot must retain
focus through ordinary disabled/busy transitions.

State properties are invalid on other node kinds. A true stateful button must
have visible text or an accessibility label; every Slider requires both an
accessible name and value.

For a network-backed toggle, update the intended value immediately, render it
with `.Selected(newValue).Busy(true)`, and call `Invalidate()` before awaiting
the remote command. Busy prevents duplicate activation while the selected state
gives immediate controller feedback. Keep that optimistic state through stale
polls until authoritative confirmation or a bounded deadline; then clear Busy.
On command failure, restore the prior selected value, clear Busy, publish a
short status, and invalidate again. Mutually exclusive toggles such as
Like/Dislike should share a busy feature so they cannot race each other.

## Dashboard quick actions

`WidgetView.QuickActions` exposes up to three bounded actions while a widget's
dashboard card is selected:

```csharp
QuickActions:
[
    new WidgetQuickAction(ControllerButton.LeftBumper, "previous", "Previous"),
    new WidgetQuickAction(ControllerButton.X, "toggle", "Play or pause"),
    new WidgetQuickAction(ControllerButton.RightBumper, "next", "Next"),
]
```

Allowed dashboard buttons are X, LB, RB, LT, RT, both stick clicks, Menu, and
View. A, B, Y, D-pad, and Guide/Home are reserved by the dashboard. See the
[controller input model](controller-input.md).

## State changes and invalidation

Handle actions in `OnActionAsync`. After changing anything visible, call the
protected `Invalidate()` method. The host receives a monotonically increasing
revision and requests a new snapshot.

Ordinary slow provider work should be awaited directly. Every host action
ingress first enters one bounded active-lifetime FIFO, so the host acknowledges
admission without waiting for this method to finish:

```csharp
public override async ValueTask OnActionAsync(
    WidgetActionEvent action,
    CancellationToken cancellationToken = default)
{
    if (action.ActionId != "refresh") return;
    var next = await HostServices.Media.GetSessionsAsync(cancellationToken);
    lock (_gate) _sessions = next;
    Invalidate();
}
```

Do not create a detached action task or private action semaphore just to keep
input responsive. The runtime owns serial ordering, a 16-pending-item bound,
coalescing for compatible slider tails, cancellation and drain on Background,
and later failure observation. A separate retained task is appropriate only
when its documented lifetime differs from the active action lifetime.

Lifecycle is explicit and is not the same as worker process lifetime. The
host-authoritative states are `Created`, `Background`, `Visible`,
`Interactive`, and `Destroying`. The current native host publishes `Visible`
while a bridge widget's dashboard card is selected, `Interactive` while its
full surface is open, and `Background` when selection moves away or the overlay
hides. A launched worker remains resident in `Background` under the default
`keep-alive` policy; lifecycle state never implies process residency by itself.

Use the protected lifecycle API for work with the matching lifetime:

```csharp
private Task _service = Task.CompletedTask;
private Task _stateWork = Task.CompletedTask;
private Task _visibleUpdates = Task.CompletedTask;

protected override ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
{
    // Requires a declared/granted background capability in a production host.
    _service = RunBrokeredNotificationServiceAsync(widgetLifetime);
    return ValueTask.CompletedTask;
}

protected override ValueTask OnLifecycleStateChangedAsync(
    WidgetLifecycleState previous,
    WidgetLifecycleState current,
    CancellationToken stateLifetime)
{
    if (current == WidgetLifecycleState.Interactive)
        _stateWork = ObserveInteractiveSessionAsync(stateLifetime);
    return ValueTask.CompletedTask;
}

protected override ValueTask OnActivatedAsync(CancellationToken visibleLifetime)
{
    // This helper uses ActiveCancellationToken and spans Visible + Interactive.
    _visibleUpdates = RunPeriodicUpdatesWhileActiveAsync(
        TimeSpan.FromSeconds(1), RefreshPreviewAsync, tickImmediately: true);
    return ValueTask.CompletedTask;
}

protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
{
    // Widget, state, and visible-lifetime tokens are already canceled.
    await Task.WhenAll(_service, _stateWork, _visibleUpdates)
        .WaitAsync(shutdownToken);
}
```

The example helpers treat cancellation of their supplied lifetime as normal;
real widgets must retain and observe every returned task so other failures stay
visible. `OnDestroyingAsync` is final bounded cleanup, not a place to begin new
long-running work.

`LifecycleState`, `WidgetLifetimeToken`, and `StateLifetimeToken` expose the
current state and its lifetimes. The widget token lasts until `Destroying`; the
state token is replaced on every stable-state change. The previous state token
is canceled before `OnLifecycleStateChangedAsync` receives the new state's
token. `OnCreatedAsync` runs exactly once before the automatic
`Created -> Background` callback. `OnDestroyingAsync` runs during bounded
shutdown after widget, state, and visible-lifetime tokens are canceled.

`IsActive`, `ActiveCancellationToken`, `OnActivatedAsync`, and
`OnDeactivatedAsync` remain compatibility APIs for the shared visible lifetime.
That lifetime spans both `Visible` and `Interactive`, so it does not restart as
a selected card opens or an open widget returns to its card. It starts on entry
from `Background` and cancels before the callback returning to `Background`.
Use `StateLifetimeToken` when work must be exclusive to `Visible` or
`Interactive`.

Code that is explicitly designed to be process-lifetime work may use the widget
token under `keep-alive`; do not assume `Background` unloads that policy.
Conversely, ordinary refresh, animation, controller/UI polling, and broker
subscriptions must not escape the appropriate visible or state lifetime. The
broker grants no new ordinary manifest-declared capability request in
Background. The narrow exception is an already-started Spotify authorization
Connect lease created by an explicit Interactive action. That action
acknowledges immediately, while its authorization task uses the widget's
Created-to-Destroying lifetime as browser foreground moves it through Visible/
Background. The temporary callback listener exists only for that attempt; it
cannot start inactive work and is canceled by Destroying, revoke, caller/pipe
cancellation, or its bounded timeout. Spotify 0.1.7 explicitly selects
`keep-alive` so idle unload cannot destroy the already-started task while the
browser owns foreground; its polling, progress, rendering, and ordinary broker
work still obey visible/state lifetimes. The
bounded host-granted private-state service is the explicit persistence
exception; it does not authorize hidden provider work. Hooks must start work
and return promptly.

Manifest `residencyPolicy` schema 1 controls the process separately:

- `keep-alive` (default) preserves the Background process;
- `suspend-when-hidden` cooperatively uses Background cancellation and blocks
  hidden renders, invalidations, interaction, and ordinary declared broker
  capabilities; and
- `unload-after-idle` additionally requires `idleSeconds` from 5 through
  86,400, caches the last validated snapshot, sends `Destroying`, and recreates
  the worker lazily on its next visible transition.

No policy suspends OS threads. Authors remain responsible for responding to
lifecycle callbacks/tokens. An unloaded worker is a new object, so reconstruct
durable state from `HostServices.PrivateState` or provider state; restore it on
first activation rather than creation. Stable element IDs let the host restore
focus against the fresh snapshot. See [Private widget
state](private-widget-state.md).

`RunPeriodicUpdatesWhileActiveAsync` serializes callbacks, prevents overlap,
and optionally invalidates after each tick. Accepted intervals are 250 ms
through one hour; values outside that range are rejected.
`InvalidatePeriodicallyWhileActiveAsync` is the
render-only convenience form. Visible-lifetime cancellation completes ticker
tasks normally; callback failures fault them, so retain and observe returned
tasks. Keep network and device work out of `Render()` and make lifecycle
cleanup bounded.

## Styling

Use `.Classes("primary", "danger")` to replace the semantic GBSS classes on a
primitive. Use `.AddClasses("widget-accent")` for SDK composites so required
`gbar-*` hooks are preserved. Both APIs enforce the GBSS identifier grammar,
64-character class limit, 32-class node limit, and deterministic duplicate
policy before snapshot publication; the protocol independently revalidates
raw workers. The host parses no CSS; the managed bridge compiles safe GBSS and
returns typed computed values. Read the [GBSS reference](gbss.md) for selectors,
allowed properties, imports, safety limits, and renderer-state behavior.
