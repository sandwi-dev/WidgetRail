# Navigation and scrolling

A controller interface needs a clear answer to two questions: which control is
focused, and what happens when the user moves or activates it?

For a multi-section layout, start with
[NavigationShell](../reference/presentation-composition.md). It composes compact
tabs and an expanded rail around shared content while your widget owns route state.

## Let controls do the ordinary work

Start with SDK buttons, sliders, menus, and collection components. They already
participate in controller navigation. Handle a button's action ID instead of
writing a raw A-button handler for every control.

The host normally uses layout geometry to choose the next focus target. For a
horizontal rail, movement first considers items in that rail, including items
that need scrolling into view. Explicit navigation links can express a deliberate
relationship when the layout alone is ambiguous.

## Keep control identities stable

For a list of games, use IDs derived from the game identity, such as
`game.<stable-id>`. When a loading indicator appears or another page arrives,
the host can still recognize the focused game.

Avoid `game.0`, `game.1`, and so on if sorting or filtering can move items.
Also avoid giving the same ID to unrelated controls on different pages.

`InitialFocusId` suggests where to begin. It should not become a request to move
focus back to your header every time data changes. A loading update should leave
a still-valid focused control alone.

## Actions and shortcuts

```csharp
UI.Button("Refresh", "refresh", "refresh-button")
    .Shortcut(ControllerButton.Y)
```

A shortcut is another way to invoke an action. Keep it consistent with the
control's meaning. The controller guide can expose actions for the focused
control and its scope, so users can discover them as they navigate.

Use a menu or nested page for secondary actions. A Back action should return
through the widget's own pages before returning to the overlay tray.

## Use the shared scrolling behavior

D-pad and left-stick navigation move focus and reveal the target control.
Right-stick scrolling moves the viewport; focus settles after scrolling stops
instead of jumping with every arriving page.

Users can enable **Hold D-pad to scroll** in Settings → Controllers. Taps still
move focus normally; holding Up/Down scrolls the active container and release
lands on a visible item in the originating column where possible. The host owns this gesture,
including its loading-boundary and scope handling. Widgets need no repeat loop
or additional capability. See the [collection contract](../reference/collections.md#rendering-and-controller-scrolling-contract).

Scroll positions belong to the widget instance, the container's own input scope,
and its stable ID. Changing the active scope to a modal does not reset the parent
page's viewport. The dialog keeps independent scroll state. Explicit collection
resets still reset that container; removing it from a scope that remains in the
view clears its state. Other page scopes retain their positions for returning.

Overflowing vertical containers also show a thin scroll indicator by default.
Use `with { ShowScrollbar = false }` on a scroll element to hide it for that
container and release its reserved gutter. Scrolling and focus-follow remain enabled.
See [Scroll indicators](../reference/declarative-ui.md#scroll-indicators).

For long lists and responsive grids, use the cursor resource and matching
collection metadata. The host can ask for adjacent pages near an edge or when
loaded items do not yet fill the viewport. A responsive grid may need more
items after a resize, so a fixed item threshold is not a substitute for viewport demand.

The shared collection can show a loading indicator while fetching. Page eviction
must not remove the visible range just to meet a preferred retention count.
See [Data and lifecycle](data-and-lifecycle.md) before building a custom paging loop.

For API signatures and detailed targeting rules, see
[Controller components](../reference/controller-ui-components.md),
[Controller input](../reference/controller-input.md), and
[UI elements](../reference/declarative-ui.md).


## Coordinated section transitions

Use `UI.NavigationShellParts(...).WithTransitions()` to animate navigation
positions and the shell-owned content together. Existing navigation overloads
keep their static presentation. For a custom arrangement, apply
`TransitionContent(shellId, selectedSectionId, selectedIndex)` to the changing
content container; keep persistent players, toolbars and navigation outside it.

```csharp
var parts = UI.NavigationShellParts(
    "library.nav", selectedSection, contentEntryId, content, destinations)
    .WithTransitions();
var root = UI.Stack("library.root", parts.CompactNavigation, parts.Body);
```

For the standard shell layout, use `parts.Compose()` instead of arranging its
parts manually. Custom segmented tabs can use `TransitionSelection` on each tab
control with the same group/key/order as the content; the selected surface moves
while labels remain stationary.

`TransitionLayout(groupId, sectionKey, sectionOrder)` opts another element into
position motion on that same timeline. Group/key identities are stable identifiers;
order is a bounded integer (-1024 to 1024), normally the destination's index.
Only a changed section key starts motion. Playback ticks, cursor pages, artwork
completion and loading-state updates keep the same key and do not restart it.
The first committed presentation snaps into place. A section needing data can
show its loading content immediately; navigation never waits for a provider.

The default Slide preset moves full pages horizontally without a crossfade.
At normal speed, sections share a 208 ms timeline with gentle acceleration and
deceleration. Paging is an alternative: the incoming page rises from below while
the outgoing page recedes to 96% of its size and moves slightly upward. Both pages
keep their original opacity, with coordinated reveal clipping to prevent old
content showing through transparent incoming content. The themed selection surface moves
behind stationary labels; the labels and their input targets do not move merely
because selection changed. Actual layout position changes use the same clock.
Rapid navigation retargets from the
visible presentation. The destination owns input immediately; retained outgoing
pixels have no actions, accessibility nodes or focus memory. Existing scopes and
cursor restoration remain authoritative. The host paints content into separate
DirectComposition layers and submits position and opacity curves together.
Animation frames do not rerun widget layout, paint, or upload pixels. Content
updates repaint the affected scene without restarting its timeline. An interrupted
section transition captures its current appearance once before retargeting.
Motion uses final layout geometry, so focus-follow cannot compensate for a slide.
Pointer input is mapped through the current incoming transform; outgoing pixels
are never interactive. Native context menus and dropdowns snap motion while open
so their anchors remain stationary.

Content motion clips to its container. Use a stack, row, grid or
focus-presentation surface. Content transition containers cannot be nested; each
group has one content container and at most one selected surface per responsive mode. A presentation supports
seven section groups and 64 moving layout elements. Pixel retention uses a
64 MiB source-scene budget and a bounded reusable capture pool. Source pixels plus
retained outgoing pixels are also checked against 64 MiB; exceeding that budget
snaps motion. Oversized scenes fall back to ordinary rendering. Graphics failures
use the host's graphics recovery path. Live embedded-media
and window-preview subtrees are not captured. Colors, typography and surfaces
come from the ordinary theme; no animation-specific color palette is introduced.
Reduced motion snaps to the destination and drops retained transition pixels.
Hiding, replacing runtime authority, resizing/scaling or losing the graphics
device retires obsolete motion. Hosts without composition render the destination
immediately. Section declarations require protocol 58; animation presets do not
change the widget API.

### Host animation policy

`WidgetAnimationPolicy.h` owns animation recipes: incoming/outgoing geometry,
opacity, reveal clips, duration and easing. The compositor presenter owns the
visual resources and lifecycle, without embedding preset-specific movement.
CPU sampling and DirectComposition polynomial emission share the same curve;
interruption capture and pointer mapping use the same transform definition.

`DeclarativeRenderOptions.widgetAnimations` carries host preferences into each
scene. Section presets have stable settings IDs: `slide` (default), `paging`,
`verticalslide`, `reveal`, `coverslide`, and `none`. All moving section presets
use 208 ms smoothstep timing at normal speed, without page-wide fading.
Slide and Vertical slide push both pages in navigation order; Paging lifts the
new page while the old page recedes. Reveal clips stationary pages, while Cover
slide moves the incoming page over the stationary outgoing page. Complementary
clips prevent translucent incoming content from exposing the old page beneath it.
The policy parser defaults unknown IDs to Slide; settings validation and bridge admission reject
unsupported values. The options also allow modal motion to be disabled independently.
Changing preferences settles the current scene and discards old motion. Reduced
motion takes precedence over every preset. **Settings Ã¢â€ â€™ Overlay** exposes the
Focus animation, Section animation and Dialog animation dropdowns and Animate widget dialogs switch.
The platform settings document persists `appearance.sectionAnimation`,
`appearance.modalAnimation` (`Lift` or `Zoom`), and `appearance.animateWidgetModals`.
Missing values default to Slide, Zoom and enabled dialog motion;
explicit saved choices remain respected. `appearance.widgetAnimationSpeed`
is a global multiplier from 0.5 to 2, defaulting to 1. Duration is divided by this
value for sections, navigation and modals together; invalid values are rejected.
The bridge publishes the section choice using its stable
lowercase ID and delivers updates through the existing appearance-revision path.
Widgets continue to declare semantic transitions rather than choosing host effects.

### Focus movement

The native host animates themed focus decorations using DirectComposition.
Widgets need no transition declaration. Settings offers Fade (default), Settle
and None for focus highlights. Focus changes stay attached to their control;
text and artwork do not travel with the highlight. Legacy focus Slide settings
migrate to Fade without changing section animations or other preferences.

Settle expands the destination outline from up to 6 DIPs inside the control over
220 ms and raises its opacity from 65% to full. The inset is bounded for small
controls. It remains visible even when a row clips exactly to the control's bounds;
content, layout and clipping stay stationary. The resolved background fades on the same clock.
Existing WRSS scale remains a parent transform, so scaling and outline settling
compose without competing layout changes. A rapid return to a fading outline resumes
its sampled pose rather than restarting its expansion. Controller hints remain immediate.
Fade uses a 240 ms smooth in-place transition for outgoing/incoming outlines and supported
resolved focus surfaces. Surface colors are interpolated with premultiplied
alpha, preserving translucent themes. Controller hints appear immediately and
retain their control's scale. Rapid changes sample the current fade weight;
removed or moved cursor items cannot retain an outgoing outline. None snaps
highlight changes; authored control scaling and pressed feedback remain independent.

`WidgetInteractionMotion.h` owns the in-place focus recipes. The scene carries a
bounded list of visible focus identities and geometry, without retaining widget
nodes or cursor pages. Removed, recycled, scrolled or reflowed controls retire
outgoing decoration. Same-target updates do not restart the clock. The focus
layer does not change input mapping or publish an animation repaint timer.
Live external media surfaces retain their stationary fallback.

Focus surface separation uses final resolved styles, not widget IDs or selector
names. Fade and Settle support background, shading, soft shadows, border color
and rounded-shape changes. Both states use the same shadow-expanded capture bounds
so translucent pixels interpolate consistently. Background blur, border-width/layout
changes and semantic navigation-selection surfaces retain their ordinary surface
painting. Poster surfaces remain underneath full-bleed artwork; their borders
are not lifted above that artwork.

### Authored control scaling

WRSS `scale` is the sole opt-in for visual size changes; there is no second SDK
flag or host zoom preset. On buttons and action surfaces, supported scaling runs
as one compositor transform containing the background, text, artwork and focus
decoration. Focus decoration remains attached to the same scaled control. Base,
focused and pressed styles resolve through the existing cascade;
pressed scale replaces focused scale rather than multiplying another transform.
The platform gives buttons and action surfaces a `:pressed` scale of `0.96`.
If a package overrides `scale` in its base or focused styles, it must also author
its pressed value: package layers take precedence over platform rules. Disabled
and busy controls cannot acquire the pressed state. Releasing, changing focus,
replacing a snapshot or closing the surface clears it without delaying actions.
Layout and input rectangles stay fixed. Authors must provide room inside ancestor
clips for enlargement; paint order and clipping are preserved.
Focus-follow reveals the final scaled bounds without chasing animation frames.
Trailing item margins remain part of the scroll extent, so that reserved room is
reachable at the end of a list or rail. Right-stick free scrolling still suppresses
focus-follow.

Host context menus and dropdowns share a separate compositor surface above their
content or tray layer. A new opening grows from 92% to full size over 160 ms at
1x, anchored toward the triggering control, without changing opacity. Option
highlights and content updates repaint that surface without restarting its clock.
Pointer coordinates follow the current transform; controller navigation and
accessibility retain their existing semantic targets. Dismissal removes the
surface immediately. Resize or animation-policy changes snap, Reduced Motion
disables entry motion, and capture failure falls back to ordinary popup paint.

Use `transition-duration` and `transition-easing` with `scale`. Linear, ease-out
and ease-in-out control-scale curves run in the compositor and honor the widget
animation speed preference. Spring retains its existing renderer evaluator.
Reduced Motion snaps to the authored scale. First frames, cursor identity/scope
changes and geometry changes also snap, while rapid focus/press changes retarget
from the currently displayed transform. Focused width/height overrides do not
change the native base-style layout contract.

## Widget modals

Use `WidgetView.WithModal(new WidgetModal(...))` for a dialog above the current
page (presentation protocol 55). Retain the page and its stable element IDs;
return it without `WithModal` when the dismiss action runs.

The host fades the themed backdrop and slides the panel by 14 DIP on the same
260 ms smoothstep timeline at normal speed when opening or closing. The optional
Zoom preset also grows the panel from 90% to full size, centered horizontally,
and reverses that motion on closing. The backdrop never moves or scales.
Closing changes input authority
immediately; only panel pixels survive until the exit finishes. Reopening during
exit continues from the displayed opacity. A modal already present on the first
frame after resuming or resizing does not replay its entrance. No worker timers
or new modal API are needed, and reduced motion disables these transitions.

```csharp
var page = new WidgetView(BuildLibrary(), "library.first");
return detailsOpen
    ? page.WithModal(new WidgetModal(
        "game.details", "Game details", BuildGameDetails(),
        "game.details.play", "game.details.close"))
    : page;
```

The host centers and clamps the modal **inside the widget**, including when the
widget is aligned to a screen corner. The background alone determines the widget's
surface size. DPI, overlay scale, and viewport changes use the normal layout path.
A themed header includes Close by default; the body is an ordinary vertical scroll
container. Set `WidgetModal.HeaderActions` to ordinary declarative content to
replace the Close button with controls or controller hints. The title and B-to-dismiss
shortcut remain. Header content belongs to the modal input scope; use stable,
unique IDs and declare any additional shortcuts on the relevant content ancestor.
Text, artwork, grids, tiles, sliders, text entry, and nested scrollers use their
normal component contracts. Select/context popups and the host keyboard retain
their own higher input layer. The keyboard still uses its existing placement.

Only the dialog's input scope accepts main-widget input. B closes the topmost
host popup before reaching the dialog dismiss action. Background scroll offsets
and focus memory are retained; dismissal restores a valid remembered target,
falling back to the page's initial or surviving focusable element if it disappeared.
Do not dispatch background actions directly while displaying a modal. Cancel or
ignore asynchronous results after the owning route/lifecycle is retired, and
clear widget-owned modal state when leaving its page. Hiding the overlay does not
itself dismiss a modal: the widget chooses whether to retain it while loaded. To
resume one, retain its modal ID and content IDs, cancel active work on deactivation,
and resume interrupted reads on activation. Never automatically replay mutations.
A new explicit opening may use a fresh modal ID to reset focus and scrolling;
ordinary refreshes and hide/reopen should retain that ID when preserving position.

One widget modal is supported at a time. Nested `WithModal` calls and embedded
media sessions are rejected with an authoring error; media composition needs its
own layering contract. Pinned layouts remain supported and do not display the
modal. The full-widget pinned projection uses the underlying page; independently
authored pinned layouts stay unchanged. Do not put a modal in a pinned projection.

Themes style `.wrail-modal-layer` (scrim), `.wrail-modal` (panel),
`.wrail-modal__header`, `.wrail-modal__title`, and `.wrail-modal__scroll` separately.
Panel width/height are preferred bounds and are always clamped to the widget.
Scrollbar styling follows the ordinary container contract. Set `WidgetModal.ShowScrollbar`
to false to hide the body indicator and remove its reserved gutter.
