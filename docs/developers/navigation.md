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

When there is no further focusable control in the requested direction but the
current container has more content, D-pad/left-stick navigation scrolls that
content. Reversing scrolls back toward the remembered control. Editors, menus,
and sliders being adjusted keep their own input. This works without a widget
repeat loop or an extra permission. The legacy **Hold D-pad to scroll** setting
is not exposed by the WinUI host. See [WinUI navigation](../reference/winui-authoring.md#native-control-mapping).

Scroll positions belong to the widget instance, the container's own input scope,
and its stable ID. Changing the active scope to a modal does not reset the parent
page's viewport. The dialog keeps independent scroll state. Explicit collection
resets still reset that container; removing it from a scope that remains in the
view clears its state. Other page scopes retain their positions for returning.

Overflowing vertical containers also show a thin scroll indicator by default.
Use `with { ShowScrollbar = false }` on a scroll element to hide it for that
container and release its reserved gutter. Scrolling and focus-follow remain enabled.
See [Scroll indicators](../reference/declarative-ui.md#scroll-indicators).

For long lists and grids, publish an indexed source with a known count or a
discovered source with an opaque continuation token. WinUI realizes items around
the viewport and requests ranges or discovery as needed, including after resize.
Keep collection IDs and occurrence keys stable. Provider-side cursor helpers
remain available, but their legacy eager pagination metadata is not a WinUI
presentation contract. See [Collections](../reference/collections.md) and
[Data and lifecycle](data-and-lifecycle.md).

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

The host applies section motion after native layout. The default Slide preset
moves pages horizontally; Paging, Vertical slide, Reveal, Cover slide and None
are also available. Moving section presets use 208 ms at normal speed. Widget
code declares semantic groups and keys, not animation frames.

The presenter retains outgoing XAML presentation only for the transition,
revokes its input/accessibility ownership, and gives the incoming page authority.
It disposes the retained controls when the transition finishes or is superseded.
Same-key content updates do not start another transition. A subtree with a live
browser, media or window-preview surface does not participate in retained section
content motion; its independent surface owner keeps placement and input authority.
Header and selection motion can still run.

Content transition containers cannot be nested. Each group has one content
container and at most one selected surface per responsive mode. Keep persistent
players and toolbars outside the changing content container. Reduced motion,
owner retirement and invalidated layout settle or retire obsolete motion.

### Host animation policy

`WidgetMotionPolicy` and `WidgetCompositionMotion` in the WinUI frontend own
recipes and their Microsoft.UI.Composition execution. There is no retired native
renderer, pixel-capture pool, or per-frame widget rendering in this path.
See [WinUI motion](../maintainers/winui-motion.md) for implementation ownership.

Settings exposes focus, section and dialog animation choices. The defaults are
Settle, Slide and Zoom. Dialog animation is enabled by default. Existing saved
choices remain respected. `appearance.widgetAnimationSpeed` ranges from 0.5 to 2
and divides animation duration; reduced motion settles immediately.

### Focus movement

Settle (default), Fade and None affect the themed focus decoration. Settle grows
the outline from an inset of up to 6 DIPs over 220 ms, starting at 65% opacity.
Fade changes opacity over 240 ms. These timings precede the global speed setting.
Text and artwork do not move with the decoration. `WidgetFocusMotion` connects
native focus events to the composition owner and retires resources on unload.
Reduced motion and None present the destination without interpolation.

Focus borders on Browser and provider-document elements indicate navigation
focus before interaction; they are hidden while the user interacts with the page.
Controller hints update with the current action authority.

### Authored control scaling

WRSS `scale` controls visual enlargement without changing layout or input bounds.
On buttons and action surfaces, one transform contains the control and its focus
decoration. Pressed scale replaces focused scale rather than multiplying it.
Busy and disabled controls cannot acquire the pressed state. Keep room for
enlargement inside ancestor clips.

`WidgetControlScaleMotion` supports linear, ease-out, ease-in-out and bounded
spring curves in the compositor. `transition-duration` is capped at two seconds
before the global speed multiplier. Initial placement, unload, size and policy
changes settle the transform; subsequent changes retarget from the displayed
scale. Reduced motion snaps immediately. Native popups have their own WinUI
ownership and are not old renderer capture surfaces.

## Widget modals

Use `WidgetView.WithModal(new WidgetModal(...))` for a dialog above the current
page (presentation protocol 55). Retain the page and its stable element IDs;
return it without `WithModal` when the dismiss action runs.

The host owns modal motion and applies the global dialog preset, speed and
reduced-motion settings. Opening uses a 260 ms recipe at normal speed; Zoom
combines translation and scale, while Lift uses translation. The scrim does not
scale. The presenter owns dismissal and focus restoration; widgets return the
underlying page instead of running an exit timer.

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
