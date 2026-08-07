# GBSS styling contract

See the [declarative UI reference](declarative-ui.md) for node kinds, stable
IDs, classes, and interaction state. See [troubleshooting](troubleshooting.md)
for common validation and renderer-integration problems. The separate
[settings and global themes](settings-and-themes.md) guide distinguishes this
implemented widget-local language from the in-progress host-wide cascade.

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

A selector is a single semantic compound: optional role, `#stable-id`, zero or more `.style-classes`, and the pseudo-states `:focused`, `:pressed`, `:selected`, or `:disabled`. Comma-separated selector lists are supported. Descendant/sibling combinators and attribute selectors are not.

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
accessibility policy after widget style resolution: bounded text scale adjusts
generic widget font size/letter spacing and layout without compounding inherited
`em` values. High contrast, other preferences, and the full 150% visual matrix
remain incomplete.

The current bridge response publishes complete computed `base` and `focused`
maps for every node. Snapshot `selected` and `disabled` state participates while
computing both maps, so those rules can affect the current render. The language
also parses `:pressed`, but there is no complete separate pressed/busy/dynamic
state-map pipeline. Do not depend on transient pressed theming yet; semantic
button state remains available to accessibility and controller routing.
