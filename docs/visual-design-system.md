# Control Center visual design system

Status: minimalist default foundation implemented; broader product composition
and physical visual matrix remain, 2026-08-07

This system combines two reference directions without reproducing either
product's trade dress:

- The supplied YT Music widget establishes a useful media hierarchy: restrained
  near-black panel, slim identity/status header, square artwork, metadata and
  progress as one group, a dominant circular play control, quieter secondary
  controls, and sparse controller prompts.
- The supplied PS5 Control Center references establish the shell hierarchy:
  leave the game visible, darken mainly toward the bottom, place contextual
  cards or a content-sized flyout above a quiet icon rail, and mark selection
  with a thin light outline rather than loud permanent chrome.

The shipped default uses warm graphite surfaces, warm off-white text, restrained
stone accent, small regular-weight type, 10–12 DIP rounding, thin separators,
and compact controller-safe rhythm. Service widgets may use a scoped brand
accent—YT Music uses coral-pink—but focus remains a host-owned neutral warm
white. The goal is familiar interaction, not visual cloning. This shared
default is implemented in the built-in platform theme and first-party GBSS;
the composition blueprints below still include product targets that need
packaged physical evidence.

## Coordinate system and responsive stage

Author shell geometry in a 1920 × 1080 logical canvas.

```text
layout scale S = viewport height / 1080
type scale   T = clamp(S, 0.875, 2.0)
stage width    = min(viewport width, 1920 × S)
stage origin X = (viewport width - stage width) / 2
horizontal inset = max(24 px, 64 × S)
bottom inset     = max(24 px, 32 × S)
```

Geometry follows `S`; type follows `T` so 720p text never becomes
unreadably small. Focus targets use `max(authored size × S, 44 physical px)`.
The stage is centered on 21:9 and 32:9 displays. Backdrop dimming spans the
whole viewport, but cards, flyouts, rail, prompts, and notifications never
stretch into the ultrawide wings.

Implementation note: the Phase 0 host currently treats the monitor containing
the previously foreground app as the viewport and uses a uniform black backdrop
at alpha 164/255. The layered veil/ramp below remains the product target. The
backdrop is a separate non-activating topmost window; clicking outside the panel
on it closes the overlay, but does not create a widget mouse-input contract.

| Viewport | S | Stage | Stage-side blank | Hero card | Standard card | Media widget | Rail target |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1280 × 720 | .667 | 1280 × 720 | 0 | 272 × 153 | 165 × 153 | 587 × 347 | 44 |
| 1920 × 1080 | 1 | 1920 × 1080 | 0 | 408 × 230 | 248 × 230 | 880 × 520 | 56 |
| 2560 × 1080 | 1 | 1920 × 1080 | 320 each | 408 × 230 | 248 × 230 | 880 × 520 | 56 |
| 2560 × 1440 | 1.333 | 2560 × 1440 | 0 | 544 × 307 | 331 × 307 | 1173 × 693 | 75 |
| 3440 × 1440 | 1.333 | 2560 × 1440 | 440 each | 544 × 307 | 331 × 307 | 1173 × 693 | 75 |
| 3840 × 2160 | 2 | 3840 × 2160 | 0 | 816 × 460 | 496 × 460 | 1760 × 1040 | 112 |

All table values are physical pixels after scaling and rounding to the nearest
pixel. Use a 4-logical-pixel baseline. Round layout origins, dimensions, and
stroke centers to physical pixels after scaling.

For an undersized window below 960 × 540, enter compatibility compact mode:
use a single 16:9 card, horizontally scroll the rail, and stack opened media
art above metadata. This is a fallback, not a primary test target.

## Layer composition

From back to front:

1. **Game:** unchanged and always recognizable.
2. **Global veil:** `rgba(3, 5, 10, .18)`.
3. **Bottom readability ramp:** transparent at 40% viewport height,
   `rgba(3, 5, 10, .62)` at 70%, and `rgba(3, 5, 10, .94)` at the bottom.
4. **Context content:** dashboard cards or one opened flyout/widget.
5. **Controller rail:** stable host controls.
6. **Prompt strip and transient status:** highest non-modal layer.

Do not place a border around the full overlay. Do not blur the entire game.
Surfaces may request up to 24 logical pixels of local background blur; fall
back to a more opaque surface when blur is unavailable or reduced transparency
is enabled.

## Color tokens

All colors are sRGB. Alpha is applied after color interpolation.

| Token | Value | Use |
| --- | --- | --- |
| `--canvas-veil` | `rgba(0, 0, 0, .70)` | Full viewport dim |
| `--surface` | `rgba(21, 21, 20, .98)` | Flyout/widget body |
| `--surface-raised` | `rgba(29, 29, 27, .98)` | Rows and buttons |
| `--surface-subtle` | `rgba(39, 39, 36, .92)` | Secondary groups and tracks |
| `--surface-active` | `rgba(48, 47, 43, .98)` | Focused or selected surface |
| `--text` | `#f2efe8` | Primary copy |
| `--text-muted` | `#aaa69d` | Secondary copy |
| `--text-subdued` | `#7d7a72` | Timestamps and quiet metadata |
| `--accent` | `#b8ae92` | Restrained host accent |
| `--brand-media` | `#ff3b67` | YT Music-scoped progress/play |
| `--focus` | `#f4f0e8` | Mandatory focus ring |
| `--success` | `#8eb49a` | Connected/available |
| `--warning` | `#c8a66a` | Degraded state |
| `--error` | `#c98181` | Failure/destructive warning |
| `--scrim` | `rgba(0, 0, 0, .42)` | Text over imagery |

Large image cards must add a bottom scrim before placing text. Brand color
cannot replace error/success semantics or the focus ring.

## Typography

Use `Segoe UI Variable Display` for 24 logical pixels and above, and
`Segoe UI Variable Text` below 24. Fall back to `Segoe UI`, then the host
sans-serif. Never package or load a font through GBSS.

| Style | Size / line | Weight | Tracking | Maximum |
| --- | --- | ---: | ---: | --- |
| Display time | 48 / 56 | 600 | -0.4 | 1 line |
| Widget title | 28 / 34 | 600 | -0.2 | 1 line |
| Media title | 24 / 30 | 700 | -0.1 | 2 lines |
| Card title | 20 / 26 | 600 | 0 | 2 lines |
| Body/action | 16 / 24 | 400/600 | 0 | 3 lines |
| Metadata | 14 / 20 | 400 | .1 | 2 lines |
| Eyebrow | 12 / 16 | 700 | .8 | 1 line, uppercase |
| Prompt label | 12 / 16 | 600 | .4 | 1 line |

Ellipsize only after the maximum line count. Never marquee by default.
Auto-scroll is an accessibility setting with slow/medium/fast speeds, not a
theme animation.

The default is a controller utility surface, not a web landing page or desktop
skin. Do not use staggered list entrances, ambient looping/pulsing, decorative
parallax, editorial-serif hierarchy, faux-macOS traffic-light/chrome motifs, or
oversized hero whitespace. Optional packaged fonts are lower priority and
security-sensitive; the baseline remains the Windows UI family until an
immutable, licensed, bounded host asset contract exists.

The remaining component gap is functional as well as visual. `SettingsRow`,
nested `ActionSheet`, and the single-select `Picker` now have shared public
contracts. Future increments should cover non-focus-stealing toast, controller
scrubber, `MediaTile`/`AppTile`, per-edge borders, responsive grid/wrap, and
semantic monospace. First-party widgets must not invent private substitutes
unavailable to Community authors.

## Dashboard geometry

The dashboard sits directly above the rail. Its bottom edge is
`bottom inset + 88 × S`. Cards form one horizontal row:

- Hero/context card: 408 × 230 logical, 16:9 artwork.
- Standard card: 248 × 230 logical.
- Gap: 16 logical.
- Radius: 16 logical.
- Inner padding: 16 logical.
- Card artwork scrim: bottom 58% of image.
- Up to two cards at 720p/1080p; a third may become visible only when the stage
  has room. Horizontal overflow scrolls one complete card per D-pad press.

Cards expose one primary datum and at most three quick actions. A selected card
uses a 2-logical-pixel white outline at 2-logical-pixel offset, scale 1.025,
and raises its text/image contrast. Unselected cards use 88% opacity. Keep the
selected card fully inside the stage; outline and scale cannot clip.

## Bottom control rail

The rail is centered within the stage at the bottom inset.

- Target: 56 × 56 logical, minimum 44 physical.
- Icon: 24 × 24 logical.
- Gap: 12 logical; insert a 20-logical gap between widget controls and
  safety/system controls.
- Selected target: `--surface-raised`, pill/circle shape, 2-pixel focus ring.
- Unselected icon: 68% white; notification dot and count remain full contrast.
- Selected label appears in a content-sized tooltip 12 logical pixels above the
  target after 300 ms. Focus indication itself is immediate.

The rail is icon-only at rest. It must not become a second taskbar with
permanent labels or app windows.

## Opened flyouts and widgets

A compact list/settings flyout is anchored 16 logical pixels above its selected
rail target, then shifted to remain inside the stage inset:

- Width: 420 logical.
- Height: content-sized, maximum 520 logical.
- Radius: 18 logical.
- Padding: 16 logical.
- Row: minimum 64 logical high, 12 horizontal padding, 8 gap.
- Selected row: 2-pixel light outline plus a leading 3 × 28 accent marker.
- Dividers: 1 physical pixel at 14% white.

Party/music lists may be dense, but status, avatar/art, name, one metadata line,
and trailing state must still have separate alignment columns. Context menus
such as power remain narrow (360 logical) and show descriptions only for the
selected consequential action.

Opened full widgets are positioned above the rail, centered horizontally in the
stage by default. They remain content-shaped; they do not fill the viewport.
`B`, bumpers, triggers, and face buttons belong to the widget while open.

## Media widget blueprint

The first-party media widget uses an 880 × 520 logical surface, 20 radius, and
24 padding.

```text
┌ identity/status header (48) ─────────────────────────── settings 40 ┐
│                                                                     │
│ art 184 × 184   title (2 lines)                                     │
│ radius 16       artist · album                                      │
│                 progress 4 + elapsed / remaining                     │
│                 previous 52   play 72   next 52                      │
│                 shuffle / like / dislike / repeat, each 40           │
│                                                                     │
├ prompt strip 44 ─────────────────────────────────────────────────────┤
└─────────────────────────────────────────────────────────────────────┘
```

- Header brand marker: 3 × 24, radius 2; connected dot: 8 diameter.
- Art uses `aspect-ratio: 1/1`, `object-fit: cover`, radius 16.
- Art-to-metadata gap: 20.
- Progress track: 4 high, minimum 160 wide; scrub focus expands it to 8.
- Play is the only brand-filled control. Previous/next use raised graphite.
- Utility controls are 40 circular targets with 20 icons and 10 gap.
- Metadata and transport share the remaining width; no control overlaps art.
- At less than 760 logical pixels of available widget width, art becomes
  120 × 120 and the prompt strip wraps to two groups. Below 560 logical pixels,
  stack art above metadata and expand to the available safe width.

The dashboard media card is not this full layout. It uses 112-square art,
title/artist, one 3-pixel progress bar, and the declared LB/X/RB quick-action
prompts only.

## Controller prompts

Prompt order follows physical reading order, not registration order:
`D-pad`, sticks, `A`, `B`, `X`, `Y`, `LB/RB`, triggers, Menu/View.

- Glyph well: minimum 24 × 20 logical, pill surface, 12-pixel glyph.
- Label: prompt style, uppercase only for short verbs.
- Glyph-to-label gap: 6; prompt-to-prompt gap: 20.
- Maximum four visible prompt groups. Move overflow into a Menu/View help
  surface.
- Left side holds navigation/primary actions; right side holds refresh,
  options, or state-specific secondary actions.
- Never show a shortcut that is not currently available.
- The Guide/Home close prompt may appear at the far right for the first
  3 seconds after opening. Screen-reader mode keeps it persistent.

Prompts use the active controller glyph family but never change the underlying
semantic label.

## Motion

The implemented declarative renderer animates paint-only `opacity` and `scale`
for stable nodes from GBSS transition declarations. It retargets without a
visible jump, caps duration/node count, reuses the visible controller frame
cadence, and stops invalidating after every transition settles. Reduced motion
snaps and cancels. Layout, color, progress width, shell open/close, flyout
translation, reorder, pressed state, and artwork crossfade in the table below
remain product targets unless separately documented as implemented.

Target motion language:

| Event | Duration | Easing | Motion |
| --- | ---: | --- | --- |
| Overlay open | 160 ms | ease-out | Veil fade; content .985 → 1 scale |
| Overlay close | 100 ms | ease-in-out | Content 1 → .99; fade |
| Focus change | 90 ms | ease-out | Ring crossfade; card 1 → 1.025 |
| Flyout replace | 120 ms | ease-out | 8-logical-pixel vertical settle + fade |
| Widget open | 160 ms | ease-out | Card-to-surface crossfade; no long zoom |
| Reorder move | 140 ms | spring | One-slot translation; neighbors settle |
| Press | 60/90 ms | ease-out | 1 → .96 on press, release to focused scale |
| Progress | 100 ms | linear | Width interpolation only |

No idle pulsing, parallax, automatic carousels, or decorative particle motion.
Artwork changes crossfade for 180 ms; never rotate or zoom album art.

Reduced motion sets all durations to 0 except an optional 80 ms opacity fade,
removes scale/translation, and updates progress discretely. Focus ring state
changes immediately.

## Accessibility overrides

The host applies these after GBSS, so themes cannot defeat them:

- Text scales: 100%, 115%, 130%, 150%. At 130% and above, media art steps down
  one size; at 150%, metadata/controls may stack and flyouts may use stage
  height minus rail/prompt safe areas.
- Text contrast: 4.5:1 minimum; large text and essential icons: 3:1. Muted text
  that fails becomes `--text`.
- Focus: minimum 2 physical pixels and 3:1 against adjacent colors. High
  contrast uses a 3-pixel dual ring (black inner, white outer), plus the accent
  marker/shape change so color is never the only cue.
- Reduced transparency makes surfaces `#0b0c10`, removes blur, and increases
  dividers to 28% white.
- Bold text maps body 400 → 600 and headings 600/700 → 700 without changing
  geometry until remeasurement.
- Enabled/selected states add a checkmark or explicit word; dots and color
  alone never communicate state.
- Screen reader order follows visual groups: title/status, content, actions,
  prompts. Decorative art is hidden; meaningful art uses service + title alt
  text.
- Auto-scroll defaults off. Magnification retains focused content inside the
  stage and rail anchors.
- All animation and text layout is tested at 720p, 150% text, high contrast,
  reduced motion, and reduced transparency together.

## GBSS expression

The typed property catalog can express the media surfaces without arbitrary
CSS or asset access:

```css
image.album-art {
  width: 24vw;
  min-width: 120px;
  max-width: 184px;
  aspect-ratio: 1/1;
  object-fit: cover;
  object-position: center;
  shape: rounded;
  corner-radius: 16px;
  flex-grow: 0;
  flex-shrink: 0;
  flex-basis: 184px;
}

text.track-title {
  color: var(--text);
  font-size: 24px;
  font-weight: 700;
  line-height: 1.2;
  max-lines: 2;
  text-overflow: ellipsis;
  flex-grow: 1;
  flex-shrink: 1;
  flex-basis: auto;
}

button.transport:focused {
  outline-color: var(--focus);
  outline-width: 2px;
  outline-offset: 2px;
  scale: 1.025;
  transition-duration: 90ms;
  transition-easing: ease-out;
}
```

GBSS controls presentation only. The widget protocol supplies verified image
content; `object-fit` and `object-position` control already-brokered pixels.
There is still no `url()`, file path, network fetch, shader, arbitrary
function, or script value.

## Acceptance checklist

- Game title/HUD remains recognizable behind every non-modal surface.
- One focused item is unambiguous in grayscale and high contrast.
- Dashboard shows no more than three quick actions per card.
- Open widgets retain their action buttons; B reaches the active nested scope
  before the root-level Back fallback.
- 720p has no clipped art, prompts, focus rings, or text below 14 physical px.
- 21:9 and 32:9 keep interaction within the centered 16:9 stage.
- 150% text produces reflow rather than overlap or silent truncation.
- Reduced motion/transparency require no alternate widget implementation.
- Every renderer-used visual value comes from a typed, bounded GBSS value or a
  host-enforced design token.
