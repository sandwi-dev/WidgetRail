# WRSS styling contract

See the [declarative UI reference](declarative-ui.md) for node kinds, stable
IDs, classes, and interaction state. See [troubleshooting](../users/troubleshooting.md)
for common validation and renderer-integration problems. The separate
[settings and global themes](settings-and-themes.md) guide distinguishes this
implemented widget-local language from the global cascade and data-only theme
package workflow.

WRSS is the safe, renderer-neutral styling language for the overlay and host-rendered widgets. The production parser/model lives in `src/WidgetStyling`; the `wrail validate` command consumes the same library.

```css
:root {
  --accent: #8b5cf6;
  --surface: rgba(18, 18, 24, 0.90);
  --motion-fast: 140ms;
}

button.primary:focused {
  background: var(--surface);
  outline-color: var(--accent);
  scale: 1.04;
  transition-duration: var(--motion-fast);
}
```

## Scrollbars

Scrollbars use the `scroll` container's computed style. Their appearance is
independent of its background and text color:

```css
scroll {
  scrollbar-width: 4px;
  scrollbar-track-color: var(--scrollbar-track);
  scrollbar-thumb-color: var(--scrollbar-thumb);
}
```

All built-in themes define these two tokens. The default width is 4 logical
pixels; `scrollbar-width` resolves to 2–8 logical pixels. A separate gutter on
the right reserves the bar's width plus a 6-pixel gap from content and a 2-pixel
outer inset. It stays reserved
while `ShowScrollbar` is enabled, even when content fits. The thumb follows the existing
scroll range, including estimated virtual-list extent, without fetching pages.
Its size can change as the list's known extent changes.

Visibility belongs to the widget's C# `ScrollElement.ShowScrollbar` option,
which defaults to `true`. A bar appears only for overflowing vertical content.
It cannot be focused, clicked or dragged. Host accessibility policy still
applies to its colors and transparency.

## Selectors and cascade

A selector is a single semantic compound: optional role, `#stable-id`, zero or more `.style-classes`, and the pseudo-states `:focused`, `:pressed`, `:selected`, `:disabled`, or `:busy`. Comma-separated selector lists are supported. Descendant/sibling combinators and attribute selectors are not. Disabled and Busy remain controller-focusable; these selectors style unavailable or pending activation without changing navigation membership.

Published nodes may carry at most 32 unique style classes. Each class is at
most 64 ASCII characters, begins with a letter or underscore, and then uses
only letters, digits, underscores, or hyphens. The SDK checks the same grammar
eagerly, and the protocol revalidates untrusted/raw snapshots.

Specificity is deterministic within one style layer: ID, then
classes/pseudo-states, then semantic role. Equal specificity is resolved by
document, rule, and declaration order. Imported documents appear before their
importer. Across the complete product cascade, built-in platform styles supply
the base, the selected global theme supplies defaults, and explicit
widget-package styles win. Resolved property maps enumerate keys in ordinal
order.

Variables are declared in `:root`. `var(--token)` and `var(--token, fallback)`
support forward references; missing variables and cycles prevent theme
publication. Host semantic tokens such as `--accent`, `--surface`, `--text`,
`--text-muted`, and `--focus` have safe defaults. A selected global theme
overrides those defaults, and an explicit widget-package definition overrides
the global value. When a widget omits a token, its declarations inherit the
selected theme or host default.

## Property allowlist

The canonical runtime list is `WrssPropertyCatalog.AllowedProperties`. It currently includes:

- Layout: `width`, `height`, min/max dimensions, `gap`, `padding`, `margin`,
  `align`, `justify`, `direction`, `overflow`, `flex-grow`, `flex-shrink`,
  `flex-basis`, and row-only `flex-wrap` (`nowrap` or `wrap`).
- Typography: `font-family`, `font-size`, `font-weight`, `letter-spacing`,
  `line-height`, `max-lines` (1–128; default 1), text alignment/overflow/transform, and `color`.
  Use a larger `max-lines` for natural wrapping of bounded long paragraphs inside
  scroll containers. The line count is a ceiling, not a reserved height; existing
  declarations keep their current limits.
- Surfaces: `background`, uniform `border-color`/`border-width`, independent
  `border-top|right|bottom|left-color` and
  `border-top|right|bottom|left-width`, outline color/width,
  `corner-radius`, `shape`, and opacity.
- Media: `aspect-ratio`, `object-fit`, `object-position`, `image-tint`, and
  `scrim-color`.
- Scroll indicators: `scrollbar-width`, `scrollbar-track-color`, and
  `scrollbar-thumb-color`.
- Effects: `scale`, `surface-shading`, `background-blur`, shadow color/blur/offset,
  `transition-duration`, and `transition-easing`.

Values are typed before reaching a renderer. Dimensions, spacing, scale, opacity, blur, border widths, and transition durations are bounded. Out-of-range finite values are clamped with a source-located warning; malformed values are errors.

Per-edge borders are additive overrides, not a second box model. An omitted
edge inherits the computed uniform border color/width; an authored edge replaces
only that side. Widths are bounded to 0–16 logical DIPs and colors use the same
safe color grammar as the uniform border. The native renderer resolves and
paints all four sides independently, including transparent/zero-width sides,
without changing focus geometry or rounded clipping. Themes can therefore use
one-sided dividers without nesting extra surfaces.

`font-family` is a bounded family-name field, not browser font loading. The
current native renderer passes one resolved family name to DirectWrite; although
the parser accepts comma-separated names for compatibility, they do not form a
native fallback stack today. The semantic `.wrail-code-text` default therefore
uses the single Windows-baseline `Consolas` family. `UI.CodeText(...)` adds that
class to bounded nonfocusable diagnostics/command text. Package paths, URLs,
generic browser keywords, and arbitrary font bytes remain unsupported.

`transition-duration` and `transition-easing` participate in parsing, cascade,
computed styles, reduced-motion policy, and native rendering. When a stable
declarative node's computed target changes, the host can interpolate its
paint-only `opacity` and `scale` values. The first observation snaps to the
authored target; later changes retarget from the currently presented value.
Durations are capped at 2 seconds, at most 1,024 nodes are tracked, and settled
content schedules no animation work. Reduced motion snaps immediately and
cancels outstanding transitions. Layout, colors, borders, shadows, blur,
progress width, shell placement, and widget replacement are not interpolated
by these WRSS transitions. Host window resizing and background-surface transitions use separate animation paths.

For buttons and action surfaces, `scale` with linear, ease-out or ease-in-out
easing uses one compositor transform for the whole control, including text and
artwork. It keeps layout and hit targets fixed and follows the global widget
animation speed setting. There is no additional SDK scale setting. Put the
duration on the base rule so both focus entry and exit animate:

```wrss
.poster-card { scale: 1; transition-duration: 140ms; transition-easing: ease-in-out; }
.poster-card:focused { scale: 1.04; }
.poster-card:pressed { scale: 0.98; }
```

Leave space for the enlargement inside the parent's clip. Pressed scale replaces
focused scale; the values do not multiply. Reduced motion snaps to the final
scale. Spring and surfaces with live media keep the existing renderer path.
Focus-state `width` and `height` do not resize the native layout; use `scale`
for visual enlargement.

Separately, the host can fade focus-specific backgrounds, border colors,
shading, shadows and outlines in place for fixed-size controls. This uses the final resolved styles
from every WRSS layer and preserves persistent `:selected` styling. Unsupported
decoration combinations use a stationary fallback. See
[focus movement](../developers/navigation.md#focus-movement).

Untrusted input is bounded before publication: source bytes/characters,
statements, imports and import depth, selectors per rule, declarations per
rule, raw values, and expanded variable values all have hard limits exposed
through `WrssLimits`. File-backed sources are strict UTF-8 (an optional UTF-8
BOM is accepted) and the byte ceiling is enforced on bytes consumed from one
restrictively shared handle. Installed widget entries/imports must also match
the exact relative-path/SHA-256 inventory computed with their sealed package
tree; a modified or newly inserted source is treated as missing and the theme
does not publish.

### Surface depth

`surface-shading` is a unitless number from -0.25 to 0.25 (default 0).
Positive values gently lighten the top and darken the bottom of the resolved
background; negative values reverse that lighting for a recessed surface.
The background alpha is preserved. This is paint-only: it does not change
layout, hit targets, scrolling, or focus geometry.

`shadow-color`, `shadow-blur`, `shadow-offset-x` and `shadow-offset-y` render a
soft rounded shadow. Shadows can extend beyond the element but remain clipped
by ancestor containers. They do not reserve layout space. Leave padding around
raised panels if their shadows need room. Native rendering uses bounded, cached
alpha masks, reused across colors and large panel sizes; a theme change does not
require a separate mask per color.

Shared cards, navigation surfaces, controls and dialogs use theme depth tokens.
Custom panels can add the `wrail-surface-raised`, `wrail-surface-inset` or
`wrail-surface-flat` class through the usual C# `Classes`/`AddClasses` methods.
These classes control appearance; they do not introduce a new control type.
A raised custom panel should also define its background and corner geometry.
For direct styling:

```wrss
.game-panel {
  background: var(--surface-raised);
  surface-shading: var(--panel-shading, .03);
  shadow-color: var(--depth-shadow, rgba(0, 0, 0, .28));
  shadow-blur: 8px;
  shadow-offset-y: 2px;
  corner-radius: 12px;
}
```

Theme authors can tune `--panel-shading`, `--control-shading`,
`--focus-shading`, `--inset-shading`, `--depth-shadow`, `--depth-highlight`
and `--depth-lowlight`. Focus uses `--focus` (the theme accent by default),
`--focus-width` and `--focus-offset`. High-contrast mode removes shading and
shadows and retains its stronger contrasting focus outline. Reduced motion
snaps state transitions; reduced transparency makes surface fills opaque.

## Imports and safety

`@import "tokens.wrss";` is allowed only before rules. Paths use normalized forward-slash package-relative `.wrss` names. Absolute paths, URI schemes, backslashes, empty/`.`/`..` segments, cycles, missing files, excessive depth, and filesystem reparse points are rejected.

WRSS never evaluates browser content. It rejects URLs and URI schemes, `expression`, `calc`, script/eval functions, JavaScript/VBScript, declaration-level imports, shader/native code, and unknown functions/properties. Images and fonts cannot be loaded by path or URL in this version; future assets must use a verified host asset broker.

Diagnostics provide source, one-based line/column, severity, stable code, and a
readable message. The compiler publishes a `WrssTheme` only for a valid result.
`IWrssSourceProvider.Read` returns a closed `WrssSourceReadResult`, not a
boolean or raw exception. The loader maps missing, unsafe, oversized, changing,
invalid UTF-8, digest-mismatched, and unavailable sources to the stable
`missing_import`, `unsafe_import`, `source_too_large`, `source_changed`,
`invalid_encoding`, `digest_mismatch`, and `source_unavailable` codes. Messages
never include provider exception text or an absolute provider path.
The managed `PlatformSettings` foundation adds explicit platform, widget, and
user layers; higher layer priority wins before specificity. Its explicit reload
manager atomically publishes only a complete valid snapshot and retains the
last valid revision on failure. The bridge now watches settings/theme files
without polling, debounces changes, globally layers widget snapshots, and
publishes bounded shell appearance revisions. The native host consumes those
revisions and applies supported shell styles, interface scale, backdrop, and
motion while retaining its last good value on failure. It also multiplies shell
DirectWrite role sizes by platform text scale. The host applies supported
accessibility policy after shell/widget style resolution: bounded text scale
adjusts font size/letter spacing and layout without compounding inherited `em`
values; reduced motion removes transitions; reduced transparency removes blur
and makes node surfaces opaque; bold text enforces a minimum weight; and
System/forced high contrast corrects text/focus against the inherited surface
with a geometric focus cue. Themes cannot override that policy. The full
physical 150% text and combined-accessibility visual matrix remains incomplete.

Global themes use the same parser and typed property contract. Use `wrail theme
new|validate|preview|pack|inspect|install|list` and read [theme packaging and
distribution](../developers/theme-packaging.md); do not hand-author installed directories or
substitute browser CSS tooling.

The current bridge response publishes complete computed `base`, `focused`, and
`pressed` maps for every node. Snapshot `selected`, `disabled`, and `busy`
state participates while computing them. The native host activates `:pressed`
only for the exact physically held controller action, cancels it on focus or
surface changes, and reconciles it when a new snapshot arrives. The other
semantic states remain available to styling, accessibility, and controller
routing.

Slider adjustment activity is host interaction state, not a new WRSS
pseudo-state and not a synthetic `:pressed` state. The renderer reuses the
resolved focused `outline-color` for the active track/thumb treatment and adds
a thicker geometric thumb halo, so selected adjustment remains visible in high
contrast and without relying on color alone. Existing `slider:focused` rules
continue to own the theme color.

## Controller glyph styling

Controller glyphs use `.wrail-controller-glyph`; labeled hints additionally use
`.wrail-controller-hint__key`. `font-size` sets their intrinsic height, and
`--controller-glyph-size` customizes the shared default (24px). The host reserves
slightly wider boxes for bumper/trigger symbols and keeps Xbox/PlayStation box
sizes stable when controllers change. `color` and `opacity` affect glyph ink;
`--controller-glyph-color` controls the built-in themes' subdued accent separately
from label text. Flex stretching does not enlarge the ink beyond `font-size`.
Ordinary box properties can decorate the surrounding space. Font family, weight,
letter spacing, and line height never substitute or distort the private symbol
font. Style the separate `.wrail-controller-hint__label` for action text.
