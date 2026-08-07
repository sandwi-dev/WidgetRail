# Settings and global themes

Status: **controller Settings and global shell appearance pipeline
implemented**. The first-party Settings worker,
strict settings store, versioned development-theme catalog, explicit cascade
layers, no-poll watcher, last-good revisions, and globally layered widget
styles are implemented and covered by Release tests. The native host consumes
live shell styles, interface scale, shell text scale, backdrop opacity, and
motion revisions. A theme installer/distribution package, scaffold/preview
tooling, and complete accessibility preferences are not implemented. The full
150% text-scale visual matrix is not yet verified.
Sections labeled **Target contract** remain requirements, not supported
commands.

This page separates three concerns that must not be conflated:

1. A widget supplies semantic UI through the [declarative UI
   contract](declarative-ui.md).
2. A widget may ship package-relative GBSS for its own nodes through the
   currently implemented [GBSS styling contract](gbss.md).
3. The global-theme pipeline composes platform, widget, and user layers for
   widget snapshots and publishes a bounded shell appearance consumed by the
   native host.

Accessibility policy remains host-owned and wins after every theme layer.

## Availability at a glance

| Capability | Status now | Author action today |
| --- | --- | --- |
| Validate a widget `.gbss` file | Implemented | Run `gbar validate <style.gbss>`. |
| Package-relative imports and variables | Implemented | Keep imports inside the widget package and use the GBSS allowlist. |
| Widget-computed `base` and `focused` styles | Implemented | Use semantic roles, stable IDs, and classes; see current renderer limits. |
| Controller toggle and stepper composites | Implemented | Use `UI.ToggleButton` and `UI.Stepper`; state and persistence remain the caller's responsibility. |
| Strict appearance settings store | Implemented | The first-party Settings worker persists through it and the shell consumes its bounded appearance revision. |
| Versioned theme directory discovery | Implemented library | There is no public install command; this is not a distribution workflow. |
| Platform → widget → user cascade | Implemented in bridge snapshots | Explicit user-layer priority beats widget selector specificity. |
| No-poll reload and last-good revision | Implemented in bridge | `FileSystemWatcher` events are debounced; invalid reloads retain the prior snapshot. |
| Controller Settings widget | Implemented | Open the first-party Settings card; it uses the generic worker/SDK/renderer path. |
| Select a discovered theme | Implemented | Settings pins an exact valid ID/version; there is still no theme installer CLI. |
| Native shell appearance | Implemented | `OverlayHost` applies live shell styles, interface scale, shell DirectWrite text scale, backdrop opacity, and motion. |
| Declarative widget text scale | Implemented | The host applies bounded text scale after GBSS resolution and remeasures/reflows generic widget content without compounding inherited `em` sizes. |
| Accessibility preferences UI | Partial | Text scale and motion controls exist; high contrast, bold text, reduced transparency, and auto-scroll do not. |

## Current contract: widget GBSS

The current bridge loads the trusted `styleFile` configured for a widget,
resolves package-relative imports, compiles the bounded language, and returns
typed property maps to the native host. The current end-to-end bridge publishes
`base` and `focused` maps; complete native publication of `pressed`, `selected`,
and `disabled` maps remains incomplete.

GBSS is deliberately not CSS. It cannot fetch a URL, load a font or file by
path, execute a script, invoke a command, provide a shader, or create native
controls. Widget authors should use:

- semantic node roles rather than renderer-specific element names;
- stable IDs only when one element truly needs a local exception;
- reusable classes for visual variants;
- host semantic variables with safe fallbacks; and
- logical, bounded dimensions that can reflow across monitor sizes.

`gbar validate` validates the language and imports. It does not preview the
managed global cascade or prove that every computed state is rendered by the
current native host.

## Current managed settings foundation

`PlatformSettingsStore` persists strict schema-version-1 JSON at
`%LOCALAPPDATA%\GameBarAlternative\platform-settings.json` by default. Missing
settings return safe defaults without creating a file. Updates validate the
whole document, serialize through in-process and bounded cross-process locks,
flush a uniquely named sibling temporary file, and atomically replace the
previous document. The file is capped at 64 KiB. Duplicate, unknown,
wrong-case, missing, malformed, non-finite, out-of-range, and integer-enum input
fails closed with a stable error code.

The implemented document is equivalent to:

```json
{
  "schemaVersion": 1,
  "appearance": {
    "themeId": "builtin.default",
    "themeVersion": "1.0.0",
    "interfaceScale": 1.0,
    "textScale": 1.0,
    "backdropOpacity": 0.64,
    "motion": "system"
  }
}
```

| Implemented property | Accepted values | Default |
| --- | --- | --- |
| `themeId` | Lowercase portable ID, 1–128 characters | `builtin.default` |
| `themeVersion` | Canonical dotted numeric version, up to 64 characters | `1.0.0` |
| `interfaceScale` | Finite 0.80–1.25 | 1.0 |
| `textScale` | Finite 0.85–1.50 | 1.0 |
| `backdropOpacity` | Finite 0.35–0.80 | 0.64 |
| `motion` | `system`, `full`, `reduced` | `system` |

These values are persisted and validated by the managed library. The native
overlay applies theme shell styles, interface scale, backdrop opacity, motion,
and `textScale`; the latter multiplies themed or fallback DirectWrite font sizes
for the shell's title/body/hint roles. The same bounded value enters the native
accessibility policy after widget/theme style resolution, scales generic widget
font size and letter spacing, and participates in layout remeasurement/reflow.
Inherited `em` context is adjusted so descendants do not compound the platform
scale. Editing the JSON is not a supported end-user settings experience.

## Current controller Settings widget

Settings is a first-party out-of-process worker registered in the trusted
bridge catalog and packaged by the native build. Its entire view uses the same
public declarative nodes, composites, focus rules, scoped shortcuts, lifecycle,
bridge, and generic native renderer path as other widgets. Trusted first-party
logic writes the platform settings store; community widgets do not inherit that
authority.

The implemented root categories are:

- **Appearance:** current theme and a versioned theme picker. Valid themes can
  be selected; invalid themes remain visible but disabled with a diagnostic.
- **Accessibility:** text scale in 5% steps, Follow Windows motion, and Reduced
  motion. With neither motion toggle selected, the explicit preference is
  `full`.
- **Overlay:** interface scale and backdrop darkness in 5% steps.
- **Permissions & capabilities:** installed packages, their supported required/
  optional declarations, and explicit Grant/Deny/Not decided state.
- **Diagnostics:** settings validity, total/invalid themes, and schema version.
- **Reset:** a confirmation surface that atomically restores built-in theme,
  sizing, backdrop, and motion defaults.

The theme picker shows at most five entries per page. LB/RB move between pages
inside its nested input scope; they never change the dashboard widget. The
selected theme persists both ID and canonical version.

Controller behavior follows the platform model:

- D-pad and left stick move focus.
- A selects or toggles the focused setting.
- B returns one level inside Settings; it does not close the overlay.
- Guide/Home remains the only controller input that opens or closes the
  overlay from any depth.
- Reset uses a confirmation surface with explicit Reset and Cancel actions.

The Settings surface requires no mouse, keyboard, hover state, or text entry.
Each nested page owns B to return one level. Guide/Home remains the only
overlay-wide close control.

The SDK now provides verified `UI.ToggleButton(...)` and `UI.Stepper(...)`
helpers for this interaction model. They are available to any widget and are
documented in the [declarative UI reference](declarative-ui.md#controller-ready-setting-composites).
The Settings worker supplies platform range validation and persistence around
these generic helpers; the helpers themselves remain state-neutral SDK
composites.

Settings loads once when entering a new Visible/Interactive lifetime. Moving
between Visible and Interactive does not reload, Background cancels the active
lifetime, and there is no periodic worker poll. Saves expose busy/completion or
bounded error feedback. Malformed settings show safe defaults and provide a
confirmed reset recovery path.

The permission page uses three nested controller scopes: package list, package
capabilities, and a capability decision/confirmation page. Package pages show
five entries; capability pages show four; LB/RB paginate only within the active
scope and B returns one level. Grant is accepted only from the active explicit
confirmation page. Deny/revoke is immediate there. Decisions are atomically
stored by package ID, publisher ID, and capability ID. Missing/malformed
catalog or consent state disables actions and shows sanitized diagnostics;
unknown declarations and stale/undeclared decisions are hidden. Required
capabilities are not auto-granted, including for first-party packages.

Installed package/permission state is refreshed with the other Settings state
on activation, not by a polling loop. Manifest/catalog changes still require an
overlay/bridge restart before they change a running worker's authenticated
declaration set. Consent decisions are separate and do not require restart.
See [widget capabilities](capabilities.md) for author behavior and the security
boundary.

The manifest requests no permissions, budgets 32 MB and 1 Hz, and declares
`suspend` background policy metadata. Lifecycle-policy enforcement remains a
separate platform limitation; the widget itself performs no background loop.

## Current development theme directory

`ThemeCatalog` implements bounded discovery for a built-in `builtin.default`
1.0.0 theme and development directories with this exact layout:

```text
%LOCALAPPDATA%\GameBarAlternative\themes\
  dev.example.slate\
    1.2.3\
      theme.json
      theme.gbss
      tokens.gbss
```

The version directory and strict `theme.json` identity must agree:

```json
{
  "schemaVersion": 1,
  "id": "dev.example.slate",
  "name": "Slate",
  "version": "1.2.3",
  "entryFile": "theme.gbss"
}
```

The manifest is limited to 64 KiB, rejects duplicate/unknown/wrong-case
properties, uses a printable 1–80-character name, and requires a normalized
package-relative `.gbss` entry. ID/version directories, manifests, entries,
and imports are checked against reparse points and containment. Discovery is
bounded to 128 versioned user themes and returns sanitized diagnostics instead
of executing theme content.

This is an immutable-ready read contract, not a theme installer. There is no
supported archive extension, pack/install/select CLI, staging workflow,
signature, or product UI. Do not distribute a hand-built theme directory as if
it were a stable public package.

## Target contract: safe theme distribution package

The future distribution format must be data-only and immutable. At minimum it must
provide:

- a strict root manifest with format version, reverse-DNS theme ID, publisher,
  canonical version, display name, and one package-relative GBSS entry;
- exact-case, normalized archive paths and immutable installation by
  `<id>/<version>`;
- the same containment, collision, reparse-point, entry-count, path-length,
  expanded-size, staging, and atomic-move defenses as `.gbarwidget` packages;
- GBSS imports that cannot leave the theme package; and
- no executable assemblies, scripts, commands, browser content, native
  extensions, shaders, arbitrary filesystem paths, or remote style resources.

Images, custom fonts, and other theme assets are out of scope until a verified
host asset broker defines type, decoded-size, dimension, and lifetime limits.
Adding `url()` or general file paths to GBSS is not an acceptable workaround.

Theme validation establishes structure and bounded data; it does not establish
publisher identity. Distribution must eventually use the same integrity,
review, signing, and revocation principles described in [security and
trust](security-and-trust.md).

## Current global cascade and ownership

`ThemeLayerCompiler` now uses explicit trusted layer priorities rather than
plain import order. The implemented managed precedence, from lowest to highest,
is:

1. built-in platform theme;
2. the widget package's own GBSS; and
3. the selected user theme.

Layer precedence must be deterministic and stronger than selector specificity:
a widget ID selector cannot defeat a user-theme semantic-role rule. Specificity
and source order continue to resolve conflicts *within* one layer. This
behavior has a regression test.

The bridge uses this cascade when computing every widget snapshot. It caches a
widget's layered theme by global revision and clears the cache only after a
valid appearance publication. Consequently, user semantic rules override even
a more-specific widget ID rule without giving native code a CSS parser.

The bridge also resolves bounded typed shell roles for canvas, backdrop, panel,
tray, tray-item states, title, body, hint, and status. The native host requests
that payload without starting a widget worker, adapts it through the native
style policy, and applies supported colors, typography, radii, and focus
outlines.

The native widget style resolver applies its supported accessibility policy after
computed style resolution. Complete host integration must preserve that final,
non-GBSS safety layer so no theme selector can defeat an accessibility
invariant.

The implementation still needs to freeze:

- semantic shell roles/classes available to global themes;
- whether a user can opt to preserve service brand accents;
- namespacing for a global rule that intentionally targets one widget ID; and
- how default variables are inherited without exposing private widget data.

Globally layered widget styles travel through the existing `base` and `focused`
maps and generic renderer. Static `selected`/`disabled` snapshot state
participates in those maps. A separate complete family of pressed, busy,
selected, and disabled runtime maps is not published; pressed-state theming and
all dynamic semantic-state transitions remain incomplete.

## Current persistence and remaining accessibility settings

The managed store already provides versioning, strict validation, bounded
cross-process updates, and atomic replacement. Theme selection is pinned by ID
and canonical version rather than a loose path. Monitor-specific pixel
coordinates are not settings fields.

The visual specification additionally defines these target preferences, which
are not in schema version 1:

| Setting | Target values | Safe default |
| --- | --- | --- |
| Bold text | Off, On | Off |
| High contrast | Off, On | Off |
| Reduced transparency | Off, On | Off |
| Auto-scroll | Off, Slow, Medium, Fast | Off |

These targets come from the [visual design system](visual-design-system.md).
Adding them requires a schema evolution, controller UI, renderer application,
and combined accessibility tests. Migration must be explicit by document
version; silently reinterpreting a value is not allowed.

## Current no-poll reload and last-good behavior

`ThemeManager.ReloadAsync` serializes a reload, reads the version-
pinned selection, compiles a complete platform/user snapshot, and publishes it
with a monotonically increasing revision only when valid. Missing/invalid
settings, theme lookup failure, unsafe imports, or GBSS errors retain the prior
snapshot and revision while updating bounded diagnostics. `CompileForWidget`
adds the widget layer using the same priorities.

`PlatformAppearanceService` adds no-poll `FileSystemWatcher` subscriptions for
the settings file and versioned theme tree. It coalesces write/create/delete/
rename bursts for 200 ms, then asks the manager to reload. A valid publication
clears layered-widget caches and emits `platform-appearance-changed` with the
new revision. An appearance query returns the exact theme ID/version, scales,
backdrop opacity, motion preference, and bounded typed shell styles without
launching any widget worker.

An invalid edit produces bounded diagnostics and retains the prior immutable
snapshot/revision. Watcher errors schedule the same safe reload path. There is
no timer or scan loop while files are unchanged.

The native client parses `platform-appearance-changed`, coalesces the latest
announced revision, requests a complete appearance only when it is newer, and
ignores stale revisions. The host retains its last good appearance on bridge or
parse failure, rebuilds native shell styles, applies bounded interface scale,
shell DirectWrite text scale, backdrop color/opacity, and motion policy, then
repositions/repaints the visible overlay without restarting widget workers.
Globally layered widget styles use the same revision and are recomputed when
their next snapshot is requested.

Platform `textScale` is applied after style resolution to both shell text and
generic declarative widget content. The renderer remeasures/reflows with the
scaled font size and letter spacing while preserving non-compounding `em`
inheritance. Complete accessibility-policy application and broad visual
evidence still remain.

An invalid edit must produce source, line, column, severity, stable diagnostic
code, and message in Settings and the host log. It must not flash the built-in
theme, partially apply declarations, restart widget workers, change widget
lifecycle state, or block controller input.

Installed release themes should remain immutable. The current watcher is for
the current-user development catalog; a future installer must stage and switch
versions rather than mutate an installed release in place.

## Accessibility overrides

The native style resolver already has primitives for minimum focus-ring width,
reduced motion, reduced transparency, and an optional contrast adjustment hook.
That is not the same as a complete accessible Settings implementation.

The host-owned final layer must guarantee:

- visible focus that does not rely on color alone;
- contrast correction after theme colors are resolved;
- platform text scaling and remeasurement/reflow (implemented), plus future
  bold-text remeasurement/reflow;
- zero or bounded opacity-only motion under reduced motion;
- opaque fallback surfaces and no blur under reduced transparency; and
- explicit selected/enabled semantics in addition to accent color.

Widget and theme authors cannot opt out. The host must test combined settings,
especially 150% text, high contrast, reduced motion, and reduced transparency
at the same time.

## Resolution and monitor behavior

A global theme describes logical presentation, not a fixed screenshot. It must
remain valid when the host recomputes its centered stage for the active
monitor. Theme authors must not assume 1920×1080 physical pixels, one DPI, a
16:9 monitor, or a fixed widget width.

Use bounded logical dimensions, min/max constraints, flex behavior, line
limits, and supported `vw`/`vh` units. Test at minimum:

- 1280×720, 1920×1080, 2560×1440, and 3840×2160;
- 21:9 and 32:9 viewports with the centered safe stage;
- Windows DPI at 100%, 125%, 150%, and 200%;
- compact widths and long localized labels; and
- every accessibility combination listed above.

The current host has per-monitor DPI and responsive layout primitives, but broad
mixed-DPI, ultrawide, and compact-mode behavior remains an evidence gap. A
successful GBSS compile is not proof of this matrix.

## Authoring and test workflow

### Available now

For widget-local styles:

```powershell
gbar validate .\styles\default.gbss
gbar validate .\MyWidgetPackage
```

The managed settings/theme contract suite is also available to platform
contributors:

```powershell
dotnet run --project .\tests\PlatformSettings.Tests\PlatformSettings.Tests.csproj -c Release
```

It currently covers 12 contracts including strict persistence, concurrency,
theme safety/discovery, layer precedence, version pinning, and last-good reload.

The first-party Settings and bridge suites cover the controller surface and
appearance transport:

```powershell
dotnet run --project .\tests\SettingsWidget.Tests\SettingsWidget.Tests.csproj -c Release
dotnet run --project .\tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj -c Release
```

The current Release results are 13/13 Settings contracts and 14/14 bridge
contracts. They cover nested B scopes, paged theme selection, bounds, busy/
error/reset behavior, lifecycle/no polling, global selector precedence,
last-good revisions, bounded shell appearance, and lazy worker behavior.

Native Release verification also covers the post-style accessibility adapter's
150% font-size/letter-spacing scaling and safe fallback for a non-finite scale.
The full native suite passes, but those contract tests are not a substitute for
the remaining multi-resolution 150% visual/reflow matrix.

Keep theme-like tokens in `:root`, exercise semantic roles/classes in widget
snapshot and bridge tests, and use native renderer tests for properties that
must affect pixels. Treat warnings such as clamping as release-review items.

### Remaining distribution, preview, and native evidence

Global themes are supported in bridge-computed widget styles. The platform still
needs provider-owned tooling for:

- scaffolding a strict theme manifest and safe starter GBSS;
- validating a complete theme package and proposed global selectors;
- previewing shell, dashboard, Settings, and representative widget fixtures;
- installing/removing a theme without editing the development catalog;
- replaying controller navigation through Settings;
- completing the 150% text-scale resolution/reflow matrix and deterministic
  shell/widget visual regression tests; and
- running the resolution/DPI/accessibility matrix with deterministic evidence.

Trusted advanced users may populate the documented development directory for
local testing. Do not present manual copies as an installed, immutable, signed,
or generally distributable theme. Until supported package tooling exists, use
`gbar validate` for GBSS plus the diagnostics in
[troubleshooting](troubleshooting.md).

## Security and lifecycle invariants

Theme code never runs because a theme contains no code. A theme cannot add UI
nodes, actions, controller bindings, network requests, permissions, or widget
lifecycle behavior. It can only influence allowlisted typed presentation after
host validation.

Changing, reloading, resetting, or rejecting a theme must not start, stop,
suspend, or recreate a widget worker. The normal `Created`, `Background`,
`Visible`, `Interactive`, and `Destroying` contract remains independent. A
valid presentation change requests rerender/repaint only where required.

For process ownership and current renderer limits, read [platform
architecture](platform-architecture.md). For the complete documentation map,
return to the [documentation index](README.md).
