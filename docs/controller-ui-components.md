# Controller UI component patterns

Status: Slider v3, the Audio Mixer reference composition, and the first modern
SDK composite set are implemented; the inventory below distinguishes current
public helpers from later semantic candidates.

Game Bar Alternative components are semantic, controller-first contracts. A
widget publishes intent and state; the host owns rendering, accessibility,
focus, animation, DPI, and work-area adaptation. Components are not miniature
web views and cannot introduce HTML, JavaScript, arbitrary SVG, or their own
focus engine.

## Design sources and originality boundary

The inventory uses the public category names in the [Tailwind Plus Application
UI catalog](https://tailwindcss.com/plus/ui-blocks) to avoid overlooking common
application patterns. Interaction and accessibility thinking is also informed
by the public [Headless UI component
catalog](https://headlessui.com/), where behavior and state are separated from
appearance.

Those sites are taxonomy and interaction inspiration only. This project does
not copy Tailwind Plus component source, layouts, assets, or styles; does not
ship Tailwind or Headless UI; and does not derive a competing web UI kit from
commercial examples. Every component here is an original native protocol/SDK/
GBSS design constrained by controller navigation and overlay performance.

## Current foundation

| Need | Current public surface | Notes |
| --- | --- | --- |
| Layout | `Stack`, `Row`, `Scroll`, `Spacer` | Host layout, clipping, and focus-follow; no widget pixel scrolling. |
| Content | `Text`, `Image`, `Icon` | Bounded semantic content and a closed glyph vocabulary. |
| Discrete action | `Button`, button glyph, shortcut | One focus stop; A activates; scoped shortcuts remain explicit. |
| Two-state action | `ToggleButton`, selected Button | Visible and accessible state remains widget-owned. |
| Read-only value | `Progress` | Not focusable and never accepts controller changes. |
| Stepped setting | `Stepper` | Separate decrement/increment focus stops; useful when each action must be explicit. |
| Direct value | `Slider` | Protocol v3, one focus stop, L/R adjustment, optional A action. |
| Nested surface | container input scope + scope shortcut | Dialog/detail behavior is modeled without allowing shortcut leakage. |
| Icon action | `IconButton` | One closed semantic glyph, required accessible name, controller target size/variant classes. |
| Grouping | `Card`, `SectionHeader`, `Divider` | Nonfocusable visual hierarchy with stable generated child IDs. |
| Status | `StatusBadge`, `Alert`, `EmptyState` | Non-color state cues and at most one explicit recovery action. |
| Tabs | `SegmentedTabs` | Stable author IDs, semantic selected state, explicit Left/Right neighbors. |
| Switch | `Switch` | One focus stop with visible/audible On/Off state; Disabled remains focusable. |
| Scoped dialog | `ScopedDialog` | Nested input scope and focus-independent B action; widget publishes active scope/focus. |
| Read-only metadata row | `ValueRow` | Flat label/value hierarchy with optional description and semantic glyph; never enters focus. |
| Full-row choice | `ChoiceRow` | One stable 44-DIP-or-larger focus target with selected, Disabled, and Busy semantics. |
| Controller help | `ControllerHint` | Nonfocusable semantic key/label pair; documents but never implicitly binds input. |

These primitives are deliberately small. Reusable components should normally
be SDK composition helpers that emit the same bounded tree rather than new
protocol kinds. A new protocol kind is justified only when the host must own
unique input, accessibility, or rendering behavior—as with Slider.

The current helpers are a functional contract, not the final visual language.
The restrained minimalist default hierarchy, density, typography, radii,
focus treatment, and state motion are tracked together as
[GBA-031](known-issues.md#gba-031--default-components-need-a-minimalist-visual-system)
so Community authors do not need per-widget repairs.

### Modern composite helpers

The implemented helpers emit baseline protocol nodes and add stable `gbar-*`
semantic classes. They do not add worker code, polling, or a new native node:

- `UI.IconButton(...)` provides Default, Primary, Danger, and Quiet variants
  plus Small, Medium, and Large controller-safe sizes. Its visible label is
  intentionally empty, so a nonblank accessibility label is required.
- `UI.Card(...)` provides Raised, Subtle, and Transparent structural variants.
  Cards are not implicitly actionable; put a real Button inside when an action
  is needed.
- `UI.SectionHeader(...)` provides optional eyebrow, description, and trailing
  content with documented stable child-ID suffixes.
- `UI.StatusBadge(...)` provides Neutral, Info, Success, Warning, and Danger
  tones. The text must name the state; color is supplementary.
- `UI.Divider(...)` is decorative spacing/separation and never enters focus.
- `UI.Alert(...)` and `UI.EmptyState(...)` provide concise title/message content
  and zero or one `ComponentAction`. That action is their only focus stop.
- `UI.SegmentedTabs(...)` accepts two or more stable `SegmentedTab` records,
  links every button explicitly for cyclic Left/Right navigation, and publishes
  selected state without owning or hiding the selected content. Boundary input
  therefore remains in the tab group instead of escaping through geometry.
  The built-in theme reserves a nonshrinking 50-DIP group around its 44-DIP
  tab targets. Native renderer regressions cover the normal compact surface and
  a constrained high-interface/high-text-scale surface, requiring each tab and
  focus ring to remain wholly inside the viewport.
- `UI.Switch(...)` is one stable Button focus stop with visible and accessible
  On/Off state. Disabled suppresses activation but does not remove focus.
- `UI.ScopedDialog(...)` creates a styled nested input scope with B bound on the
  scope container. When shown, publish its `scopeId` as `ActiveInputScopeId` and
  a focusable descendant as `InitialFocusId`; restore the opener when dismissed.
- `UI.ValueRow(...)` creates a flat read-only metadata row with optional glyph
  and description. It is intentionally nonfocusable; do not use it to disguise
  an action. Its trailing value has a separate accessible label for units or
  localized pronunciation.
- `UI.ChoiceRow(...)` creates one full-width Button focus stop for a list choice
  or action. Selected, Disabled, and Busy state stay on that same ID across
  rerenders. The default selected glyph is Check, but authors may supply a more
  meaningful semantic leading glyph while selected state remains explicit.
- `UI.ControllerHint(...)` creates a restrained key-cap and label from the
  closed `ControllerButton` enum. It is display-only: authors must still bind
  the matching shortcut to the active input scope or focused control. Compose
  hints in a Row or Stack appropriate to the current surface width rather than
  assuming one unbroken desktop-width footer.

Use `.AddClasses(...)` to add widget-specific styling while preserving and
deduplicating required component classes. `.Classes(...)` remains the explicit
replacement API for compatibility and low-level primitives.

### Generated child-ID contract

Composite helpers reserve the following exact suffixes under the caller's
stable `id`. These names are public compatibility surface: authors may use them
for `InitialFocusId`, explicit focus neighbors, snapshot assertions, and
diagnostics, but must not reuse them for another node in the same snapshot.

| Helper | Generated IDs |
| --- | --- |
| `IconButton(id: id)` | None; the Button itself uses `id`. |
| `Card(id, children)` | None; the Stack uses `id` and supplied children retain their IDs. |
| `SectionHeader(title, id, ...)` | `id.content`, `id.text`, `id.title`; optional `id.eyebrow`, `id.description`, and `id.trailing`. The author-supplied trailing element retains its own ID inside `id.trailing`. |
| `StatusBadge(label, tone, id, glyph?)` | `id.label` and, when a semantic glyph is present, `id.icon`. |
| `Divider(id)` | None; the Spacer itself uses `id`. |
| `Alert(..., id, action?, glyph?)` | `id.title`, `id.message`; optional `id.icon` and `id.action`. The optional recovery Button is `id.action`. |
| `EmptyState(..., id, action?, glyph)` | `id.icon`, `id.title`, `id.message`; optional recovery Button `id.action`. |
| `SegmentedTabs(id, selectedTabId, tabs)` | The Row uses `id`; each Button uses its author-provided `SegmentedTab.Id`. |
| `Switch(..., id)` | None; the Button itself uses `id`. |
| `ScopedDialog(..., id, scopeId, ...)` | `id.title` and `id.content`; supplied children retain their IDs. |
| `ValueRow(..., id, description?, glyph?)` | `id.text`, `id.label`, and `id.value`; optional `id.icon` and `id.description`. |
| `ChoiceRow(..., id)` | None; the Button itself uses `id`. |
| `ControllerHint(button, label, id)` | `id.key` and `id.label`. |

Generated IDs use the same 128-character stable-ID grammar as ordinary nodes.
The helper validates the parent and complete generated IDs eagerly, so leave
room for the longest suffix instead of using a 128-character parent ID.

## Audio icon-Slider-percentage pattern

The first-party Audio Mixer is the reference for an adjustable value with one
closely related binary action:

```text
┌ Application name                                      ACTIVE ┐
│  [volume/muted icon]  [========= Slider =========]       75% │
└──────────────────────────────────────────────────────────────┘
```

- The leading `Icon` is non-focusable and changes between the semantic Volume
  and Muted glyphs. It communicates state without creating a tiny extra target.
- The Slider is the row's only focus stop. Left/Right changes volume, Up/Down
  selects the previous/next row, and A invokes the optional mute action.
- The trailing percentage is ordinary text with a stable width, so values do
  not make the track jump horizontally.
- The Slider's accessibility label includes audible/muted state and the A
  action; `AccessibilityValue` publishes the localized percentage.
- Master output and every application use the same structure. Explicit
  Up/Down neighbors connect the master Slider to the first application and
  adjacent application Sliders.
- The complete Audio Mixer surface is one `UI.VerticalScroll`, so master,
  device, microphone, and application controls remain revealable even when the
  host clamps the panel. The widget does not invent nested list paging or
  LT/RT session selection.
- Publish one explicit Up/Down chain from the true master top through the last
  application. Remember the last semantic master/microphone/opaque-session
  target from normal controller input, then republish it as initial focus after
  reopen. If session churn removes it, choose the nearest surviving row; do not
  reset focus to master merely because mute or volume rerendered.

Representative construction:

```csharp
UI.Row(ids.Controls,
    UI.Icon(
            session.IsMuted ? WidgetGlyph.Muted : WidgetGlyph.Volume,
            ids.MuteIcon,
            session.IsMuted ? "Application muted" : "Application audible")
        .Classes("audio-mute-icon", session.IsMuted ? "is-muted" : "is-audible"),
    UI.Slider(
            session.Volume, 0, 1, 0.05,
            ids.VolumeSet,
            ids.VolumeSlider,
            $"{session.DisplayName} volume. Press A to toggle mute",
            accessibilityValue: $"{percent}%",
            activationAction: ids.Mute)
        .FocusUp(previousSliderId)
        .FocusDown(nextSliderId)
        .Classes("audio-volume-slider"),
    UI.Text($"{percent}%", ids.Value, $"Volume {percent} percent")
        .Classes("audio-volume-value"))
```

Volume input is optimistic and latest-wins: publish the newest requested value
immediately, keep the Slider adjustable, and style the enclosing row with a
pending class. Do not mark the Slider Busy merely because a volume provider
write is outstanding—the SDK queue and widget provider state perform bounded
coalescing. A mute request is discrete, so the reference marks the Slider Busy
until that mute intent is confirmed; Busy suppresses duplicate A/adjustment
but retains the exact focus target.

Keep IDs derived from a stable provider identity, never display name or list
position. When a provider event arrives, ignore stale values that predate the
pending intent; apply external authoritative changes normally once no command
is pending. Cancel provider operations when the active lifecycle ends.

Treat optional providers as independent feature slices. Loss or denial of
device enumeration or microphone control must disable/explain only those rows;
working master output and per-application sessions stay usable, and focus stays
on the same stable target. This partial-degradation/focus contract is tracked as
[GBA-035](known-issues.md).

## Interaction-state contract

Focusable does not mean actionable:

| State | In focus order | Activation/value change | Typical presentation |
| --- | --- | --- | --- |
| Normal | Yes | Dispatched | Normal/focused surface. |
| Selected | Yes | Dispatched | Explicit on/checked state. |
| Disabled | Yes | Suppressed | Lower emphasis plus an accessible unavailable state. |
| Busy | Yes | Suppressed | Stable focus plus bounded pending feedback. |
| Removed from tree | No | Impossible | Focus restores to a stable surviving fallback. |

Disabled and Busy must never cause focus to jump. Use Disabled for a currently
unavailable action and Busy for work already accepted. Neither is a visibility
or navigation API. Avoid disabling a whole card or list because one child has
work in flight; track pending state at the narrowest owning feature.

## Remaining component design queue

The following are design candidates, not current `UI.*` APIs. Implement them as
tested composition helpers or native semantics before authors depend on names:

1. **Status line and loading state** — bounded live state without making
   metadata focusable or inventing a polling contract.
2. **Rich media row** — artwork, multi-line metadata, and a full-row action
   require a future host semantic or composition contract beyond the flat
   `ValueRow` and single-label `ChoiceRow` primitives.
3. **Select/listbox helper** — opens a nested scrollable scope instead of
   cycling hidden values with bumpers or triggers.
4. **Toast/notification model** — host-announced, time-bounded feedback that
   never steals focus; persistent failures remain in the owning surface.

Each candidate must ship with protocol/SDK validation, controller routing,
screen-reader semantics, GBSS roles/classes, compact and wide layouts, 720p and
high-DPI evidence, high-contrast/reduced-motion behavior, and regression tests.
Visual polish alone is not a component contract.

## Author checklist

- Give every focusable intent one stable ID across labels, values, Busy, and
  Disabled changes.
- Prefer one meaningful focus stop over multiple tiny controls.
- Use semantic state and host glyphs instead of encoding state only in color.
- Keep full-width focus outlines inside the clipped surface; do not scale a row
  beyond its Scroll/container bounds.
- Put long collections in Scroll and let host focus reveal the selected row.
- Use a nested input scope for dialogs/detail panes and bind B to one-level
  Back in that scope.
- Put window-wide shortcuts on the active scope-root container. Button-local
  shortcuts are exact-focus-only; sibling controls are never searched.
- Publish immediate local feedback, then reconcile provider truth with bounded
  cancellation and stale-event protection.
- Test controller navigation at minimum width, maximum text scale, empty/
  loading/error state, dynamic item removal, and rapid repeated input.

See [Declarative UI](declarative-ui.md), [Controller input](controller-input.md),
[GBSS](gbss.md), [Visual design system](visual-design-system.md), and
[Performance](performance.md) for the underlying contracts.
