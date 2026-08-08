# GBSS styling contract

See the [declarative UI reference](declarative-ui.md) for node kinds, stable
IDs, classes, and interaction state. See [troubleshooting](troubleshooting.md)
for common validation and renderer-integration problems. The separate
[settings and global themes](settings-and-themes.md) guide distinguishes this
implemented widget-local language from the global cascade and data-only theme
package workflow.

GBSS is the safe, renderer-neutral styling language for the overlay and host-rendered widgets. The production parser/model lives in `src/WidgetStyling`; the `gbar validate` command consumes the same library.

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

## Selectors and cascade

A selector is a single semantic compound: optional role, `#stable-id`, zero or more `.style-classes`, and the pseudo-states `:focused`, `:pressed`, `:selected`, `:disabled`, or `:busy`. Comma-separated selector lists are supported. Descendant/sibling combinators and attribute selectors are not. Disabled and Busy remain controller-focusable; these selectors style unavailable or pending activation without changing navigation membership.

Published nodes may carry at most 32 unique style classes. Each class is at
most 64 ASCII characters, begins with a letter or underscore, and then uses
only letters, digits, underscores, or hyphens. The SDK checks the same grammar
eagerly, and the protocol revalidates untrusted/raw snapshots.

Specificity is deterministic: ID, then classes/pseudo-states, then semantic role. Equal specificity is resolved by document, rule, and declaration order. Imported documents appear before their importer. Resolved property maps enumerate keys in ordinal order.

Variables are declared in `:root`. `var(--token)` and `var(--token, fallback)` support forward references; missing variables and cycles prevent theme publication. Host semantic tokens such as `--accent`, `--surface`, `--text`, `--text-muted`, and `--focus` have safe defaults and can be overridden by a valid theme.

## Property allowlist

The canonical runtime list is `GbssPropertyCatalog.AllowedProperties`. It currently includes:

- Layout: `width`, `height`, min/max dimensions, `gap`, `padding`, `margin`, `align`, `justify`, `direction`, `overflow`.
- Typography: `font-family`, `font-size`, `font-weight`, `letter-spacing`,
  `line-height`, `max-lines`, text alignment/overflow/transform, and `color`.
- Surfaces: `background`, border/outline color and width, `corner-radius`,
  `shape`, and opacity.
- Media: `aspect-ratio`, `object-fit`, `object-position`, `image-tint`, and
  `scrim-color`.
- Effects: `scale`, `background-blur`, shadow color/blur/offset,
  `transition-duration`, and `transition-easing`.

Values are typed before reaching a renderer. Dimensions, spacing, scale, opacity, blur, border widths, and transition durations are bounded. Out-of-range finite values are clamped with a source-located warning; malformed values are errors.

`transition-duration` and `transition-easing` participate in parsing, cascade,
computed styles, reduced-motion policy, and native rendering. When a stable
declarative node's computed target changes, the host can interpolate its
paint-only `opacity` and `scale` values. The first observation snaps to the
authored target; later changes retarget from the currently presented value.
Durations are capped at 2 seconds, at most 1,024 nodes are tracked, and settled
content schedules no animation work. Reduced motion snaps immediately and
cancels outstanding transitions. Layout, colors, borders, shadows, blur,
progress width, shell placement, and widget replacement are not interpolated
by this version. See [GBA-032](known-issues.md#gba-032--gbss-transition-declarations-do-not-animate)
for the remaining packaged visual/performance evidence.

Untrusted input is bounded before publication: source bytes/characters, statements, imports and import depth, selectors per rule, declarations per rule, raw values, and expanded variable values all have hard limits exposed through `GbssLimits`.

## Imports and safety

`@import "tokens.gbss";` is allowed only before rules. Paths use normalized forward-slash package-relative `.gbss` names. Absolute paths, URI schemes, backslashes, empty/`.`/`..` segments, cycles, missing files, excessive depth, and filesystem reparse points are rejected.

GBSS never evaluates browser content. It rejects URLs and URI schemes, `expression`, `calc`, script/eval functions, JavaScript/VBScript, declaration-level imports, shader/native code, and unknown functions/properties. Images and fonts cannot be loaded by path or URL in this version; future assets must use a verified host asset broker.

Diagnostics provide source, one-based line/column, severity, stable code, and a
readable message. The compiler publishes a `GbssTheme` only for a valid result.
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

Global themes use the same parser and typed property contract. Use `gbar theme
new|validate|preview|pack|inspect|install|list` and read [theme packaging and
distribution](theme-packaging.md); do not hand-author installed directories or
substitute browser CSS tooling.

The current bridge response publishes complete computed `base`, `focused`, and
`pressed` maps for every node. Snapshot `selected`, `disabled`, and `busy`
state participates while computing them. The native host activates `:pressed`
only for the exact physically held controller action, cancels it on focus or
surface changes, and reconciles it when a new snapshot arrives. The other
semantic states remain available to styling, accessibility, and controller
routing.
