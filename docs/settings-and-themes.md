# Settings and global themes

Status: **controller Settings, data-only theme distribution, and the global
appearance pipeline are implemented**. The first-party Settings worker, strict
settings store, versioned theme catalog, explicit cascade layers, no-poll
watcher, last-good revisions, globally layered widget styles, and safe
`.wrtheme` tooling are covered by Release tests. The native host consumes
live shell styles and host-owned interface scale, text scale, backdrop,
motion, contrast, bold-text, and transparency preferences. The shipped
`builtin.default` is the shared minimalist warm-graphite baseline for shell,
SDK semantic components, and first-party widget styles. Four embedded selectable themes exercise
the same path rather than hard-coded skins: `widgetrail.builtin.cool-slate`
swaps in a distinct navy/slate palette and desaturated blue accent,
`widgetrail.builtin.neon-circuit` adds a deep indigo palette, an electric-cyan
interactive accent, tighter corner geometry, and tracked uppercase labels, and
`widgetrail.builtin.arcade-rush` lifts the ground to a warm plum with a single
fuchsia accent and pill-shaped controller-height controls, and
`widgetrail.builtin.redline` pairs an oxblood ground with a scarlet edge under
every container. All four preserve controller-safe geometry, the neutral focus
ring, and host accessibility policy. Native graphical
theme preview, signing/revocation, theme removal/update UI, auto-scroll, and
the full physical accessibility/resolution matrix are not implemented.

This page separates three concerns that must not be conflated:

1. A widget supplies semantic UI through the [declarative UI
   contract](declarative-ui.md).
2. A widget may ship package-relative WRSS for its own nodes through the
   currently implemented [WRSS styling contract](wrss.md).
3. The global-theme pipeline composes platform, widget, and user layers for
   widget snapshots and publishes a bounded shell appearance consumed by the
   native host.

Accessibility policy remains host-owned and wins after every theme layer.

## Availability at a glance

| Capability | Status now | Author action today |
| --- | --- | --- |
| Validate a widget `.wrss` file | Implemented | Run `wrail validate <style.wrss>`. |
| Package-relative imports and variables | Implemented | Keep imports inside the widget package and use the WRSS allowlist. |
| Widget-computed `base` and `focused` styles | Implemented | Use semantic roles, stable IDs, and classes; see current renderer limits. |
| Controller switch and stepper composites | Implemented | Use `UI.Switch` and `UI.Stepper`; state and persistence remain the caller's responsibility. |
| Strict appearance settings store | Implemented | The first-party Settings worker persists through it and the shell consumes its bounded appearance revision. |
| Versioned theme discovery and immutable install | Implemented | Use `wrail theme install`; an existing ID/version is never overwritten. |
| Exact installed-version management | Implemented | Settings groups versions by theme ID; select a valid exact version or confirm removal of an inactive user version. `wrail theme remove <id> <version>` uses the same policy. |
| Platform → widget → user cascade | Implemented in bridge snapshots | Explicit user-layer priority beats widget selector specificity. |
| No-poll reload and last-good revision | Implemented in bridge | `FileSystemWatcher` events are debounced; invalid reloads retain the prior snapshot. |
| Controller Settings widget | Implemented | Open the first-party Settings card; it uses the generic worker/SDK/renderer path. |
| Installed widget review/enablement | Implemented | Review read-only Built-in widgets separately from CLI-installed Community packages; only Community packages expose enablement and version management, and enablement remains separate from consent. |
| Scaffold, validate, preview, pack, inspect, install, list, and remove themes | Implemented | Use the `wrail theme` command group and the data-only `.wrtheme` format. |
| Select a discovered theme | Implemented | Settings pins an exact valid ID/version after controller review. |
| Native shell appearance | Implemented | `OverlayHost` applies live shell styles, interface scale, shell DirectWrite text scale, backdrop opacity, and motion. Stable declarative nodes interpolate bounded opacity/scale changes only. |
| Declarative widget text scale | Implemented | The host applies bounded text scale after WRSS resolution and remeasures/reflows generic widget content without compounding inherited `em` sizes. |
| Accessibility preferences UI | Implemented subset | Text scale, motion, System/Standard/High contrast, bold text, and reduced transparency are available; auto-scroll is not. |

## Current contract: widget WRSS

The current bridge loads the trusted `styleFile` configured for a widget,
resolves package-relative imports, compiles the bounded language, and returns
typed property maps to the native host. The end-to-end bridge publishes
complete `base`, `focused`, and transient `pressed` maps. Static snapshot
`selected`, `disabled`, and `busy` state participates while computing all three.
The native host applies `pressed` only to the exact physically held action and
cancels/reconciles it across focus, surface, and snapshot changes. Future
dynamic semantic-state families remain incomplete.

WRSS is deliberately not CSS. It cannot fetch a URL, load a font or file by
path, execute a script, invoke a command, provide a shader, or create native
controls. Widget authors should use:

- semantic node roles rather than renderer-specific element names;
- stable IDs only when one element truly needs a local exception;
- reusable classes for visual variants;
- host semantic variables with safe fallbacks; and
- logical, bounded dimensions that can reflow across monitor sizes.

`wrail validate` validates the language and imports. It does not preview the
managed global cascade or prove that every computed state is rendered by the
current native host.

## Current managed settings foundation

`PlatformSettingsStore` persists strict schema-version-1 JSON at
`%LOCALAPPDATA%\WidgetRail\platform-settings.json` by default. Missing
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
    "motion": "system",
    "contrast": "system",
    "boldText": false,
    "transparency": "full"
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
| `contrast` | `system`, `standard`, `high` | `system` |
| `boldText` | Boolean | `false` |
| `transparency` | `full`, `reduced` | `full` |

These values are persisted and validated by the managed library. The native
overlay applies the complete appearance record. Text scale multiplies themed
or fallback DirectWrite role sizes and enters the native accessibility policy
after widget/theme style resolution. It scales generic widget font size and
letter spacing and participates in layout remeasurement/reflow without
compounding inherited `em` sizes. Schema-version-1 files written before the
additive contrast, bold-text, and transparency fields existed still load with
the safe defaults. Editing the JSON is not a supported end-user settings
experience.

### Public per-widget configuration

`WidgetConfigurationStore` separately persists bounded **non-secret** values in
`%LOCALAPPDATA%\WidgetRail\widget-config` by default. Each document is
isolated by the authenticated publisher and package IDs, capped at 32 entries/
32 KiB, written atomically under bounded process and cross-process locking, and
validated against duplicate fields, unknown fields, identity mismatches,
invalid keys/values, oversize data, and reparse-point paths. It is suitable for
public integration settings such as an OAuth Client ID; passwords, client
secrets, refresh/access tokens, and credentials belong in a host-owned vault.

The current local/test workflow is the `wrail config` CLI:

```powershell
wrail config set <package-id> <key> <value> --publisher <publisher-id>
wrail config get <package-id> <key> --publisher <publisher-id>
wrail config list <package-id> --publisher <publisher-id>
wrail config remove <package-id> <key> --publisher <publisher-id>
wrail config clear <package-id> --publisher <publisher-id>
```

`--settings-root <path>` isolates tests. The CLI rejects key names that look
like secrets, passwords, tokens, or credentials. A generic controller-native
editor and manifest-declared configuration schema are not implemented; do not
ask a Community worker to edit these files directly.

## Current controller Settings widget

Settings is a first-party out-of-process worker registered in the trusted
bridge catalog and packaged by the native build. Its entire view uses the same
public declarative nodes, composites, focus rules, scoped shortcuts, lifecycle,
bridge, and generic native renderer path as other widgets. Trusted first-party
logic writes the platform settings store; community widgets do not inherit that
authority.

The implemented root categories are:

- **Appearance:** current theme and controller-first version management grouped
  by theme ID. Valid exact versions can be selected. Invalid versions remain
  reviewable and removable, but cannot be selected.
- **Accessibility:** text scale in 5% steps, Follow Windows motion, Reduced
  motion, and a nested **Contrast and visibility** page. With neither motion
  toggle selected, the explicit preference is `full`.
- **Contrast and visibility:** Follow Windows high contrast, forced High
  contrast, Bold text, and Reduced transparency. With neither contrast toggle
  selected, the explicit preference is `standard`.
- **Overlay:** interface scale and backdrop darkness in 5% steps.
- **Installed widgets:** a scrollable source-separated inventory. Read-only
  Built-in rows show bundled manifest identity, publisher, version, runtime,
  compatibility, and required/optional capabilities. Community rows add active/
  installed versions, host-API range, architectures, compatibility reason,
  enable/disable, version management, and a nested **Permissions &
  configuration** page for that exact identity. Supported required and optional
  declarations expose explicit Grant/Deny/Not decided state there; permissions
  are not a separate root category.
- **Diagnostics:** settings validity, total/invalid themes, and schema version.
  Runtime diagnostics additionally arrive through the bridge-owned,
  process-bound private Settings channel: catalog/appearance last-good state,
  provider and consent availability, and bounded worker health. Native
  overlay/Guide telemetry is explicitly unavailable until a structured host
  contract exists; Settings never guesses from logs. See
  [Diagnostics and recovery](diagnostics-and-recovery.md).
- **Reset:** a confirmation surface that atomically restores built-in theme,
  sizing, backdrop, motion, contrast, bold-text, and transparency defaults.

Every installed entry appears in one bounded host-owned vertical Scroll under
its theme-ID heading. Opening an entry shows its exact ID/version, selection,
and removal policy. Removal requires a second confirmation page; the built-in
and currently selected versions never expose an enabled removal action. B
returns one level, LB/RB remain free for widget actions, and successful removal
focuses the adjacent surviving row. The selected theme persists both ID and
canonical version.

The Settings root uses the public protocol-v8 `UI.ResponsiveGrid` contract for
its bounded category actions (250-DIP minimum columns, at most two columns)
inside the existing vertical Scroll. The root requests the authored preferred
width and content-measured height, bounded by its existing 520x360 minimum and
880x520 preferred envelope. Its category list does not fill unused vertical
space, so the host clamps the measured rows and authored spacing rather than a
guessed replacement height. Compact widths collapse to one row-major column;
when that content exceeds the height ceiling, the existing Scroll reveals every
category without changing category IDs or focus order. Dynamic nested pages
remain preferred-sized so changing rows and diagnostics do not resize the
surface. Diagnostics uses public
`UI.CodeText` for schema/revision and bounded worker-failure lines; these remain
nonfocusable and receive the default semantic monospace class instead of a
Settings-only font/layout escape hatch.

Controller behavior follows the platform model:

- D-pad and left stick move focus.
- A selects or toggles the focused setting.
- B returns one level inside Settings; at the Settings root it returns to the
  dashboard, and dashboard B closes the overlay.
- Guide/Home remains the global toggle from any depth.
- Reset uses a confirmation surface with explicit Reset and Cancel actions.

The Settings surface requires no mouse, keyboard, hover state, or text entry.
Each nested page owns B to return one level. Guide/Home remains the immediate
overlay-wide toggle.

The Installed widgets page uses one bounded controller Scroll with **Built in**
and **Community** sections. Bundled first-party manifests remain visible even
when the community catalog is empty. A opens read-only Built-in details; those
rows cannot be disabled or version-managed because they are updated with the
app. Forged management actions are also rejected rather than being allowed to
mutate a bundled manifest or community catalog state.

Community packages retain pages of at most five rows; LB/RB changes only the
Community page inside the nested scope. A opens package details, and the details
page explicitly labels the package **Unsigned · publisher unverified**, shows
the manifest publisher as an unverified claim, and displays the full sealed
content-tree SHA-256 digest before **Enable unsigned widget**. This is an exact-
bytes review, not publisher verification. Install itself remains a CLI
operation; Settings has no file picker. A shared catalog evaluator matches
bridge host-API/architecture gating: an incompatible package shows a bounded
reason and cannot be enabled, while an already enabled incompatible package can
still be disabled for recovery. Enabling makes a compatible package available
to the overlay but does not grant any declared capability. Required and
optional declarations are shown separately. Open **Permissions &
configuration** on the same widget management page to make an explicit consent
decision.

The SDK now provides verified `UI.Switch(...)` and `UI.Stepper(...)`
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

Permission review starts from the selected Installed widget rather than a
second package list. Its capability page and nested decision/confirmation page
each own one bounded vertical controller Scroll, so every declaration remains
reachable with normal Up/Down navigation and focus-follow. B returns first to
the widget management page and then to Installed widgets. Grant is accepted
only from the active explicit confirmation page. Deny/revoke is immediate
there. Decisions are atomically stored by package ID, publisher ID, and
capability ID. Missing/malformed
catalog or consent state disables actions and shows sanitized diagnostics.
Unknown declarations and stale/undeclared saved decisions are excluded from
normal editable permission rows because they cannot grant current authority;
the focusable **Review unsupported or inactive access** row shows their exact
sanitized identities read-only. Required
capabilities are not auto-granted, including for first-party packages. The
exact retired `system.activity.recent.activate.v1` decision and the six
published retired Spotify decisions (`external.spotify.authorization.v1`,
`external.spotify.configuration.v1`, `external.spotify.local-playback.v1`,
`external.spotify.playback.control.v1`, `external.spotify.playback.read.v1`,
and `external.spotify.playlists.read.v1`) are tombstoned. Loading filters them
without discarding current decisions, and the next atomic write removes them
from persistence. The Spotify entries are matched by their exact immutable IDs,
not by a namespace prefix. This is a closed migration, not general forward
compatibility; every arbitrary unknown capability ID still invalidates the
document and fails closed.

Settings → Installed widgets uses a separate nested controller flow for
package versions. Open a package, choose **Manage versions**, and page through
at most five immutable versions with LB/RB. Each row labels the exact version
as Active, Rollback, or Select newer and reports Compatible or Incompatible.
B returns first to package details and then to the package list. The active row
is selected and non-actionable. While the widget is enabled every version row
is disabled and focus starts on Back; disable the widget before selecting code.
A version selection persists the exact pin and reports that its unsigned digest
and capabilities require review, but never enables it. Version rows include a
digest prefix for distinction. Return to package details and compare the
complete sealed digest, compatibility, and capabilities before using **Enable
unsigned widget**. An incompatible selected version remains reviewable but
cannot be enabled. Permission review repeats the unsigned state and full digest;
consent is bound to that digest-derived authority rather than the manifest
publisher claim.

Installed package/permission state is refreshed with the other Settings state
on activation, not by a polling loop. The bridge independently watches catalog
changes and publishes a complete validated semantic revision without a restart.
A controller-selectable **Refresh** action on the Settings root performs the
same bounded settings, theme, installed-package, and permission reload while
Settings remains visible. Selecting an installed widget version also refreshes
its permission projection immediately, because both capability declarations
and the host-derived unsigned package authority change with verified package
content (including ordinary version changes).
A process/identity/declaration policy change retires the old worker and binds
the next lazy start to the new declarations; presentation-only changes preserve
a compatible worker while atomically replacing validated style/quick-action
metadata. Invalid trusted shell reloads retain last-good state; invalid
installed state/integrity removes Community registrations and retires their
workers. Consent decisions are separate and take effect without a catalog or
worker restart. See [widget
capabilities](capabilities.md) for author behavior and the security boundary.

Disabled Community package details also expose **Uninstall widget**. This is a
separate nested destructive confirmation bound to the exact current publisher
authority, active version, complete installed-version inventory, and an opaque
catalog token. Success removes package versions only and returns to the
installed list with stable access to **Install local widget**. Built-in and
enabled packages remain protected. Widget-private local data, credentials,
provider data, themes, settings, and user files are retained; **Clear local
data** remains a separate explicit choice.

The manifest requests no permissions, budgets 32 MB and 1 Hz, and declares
`suspend` background policy metadata. Lifecycle-policy enforcement remains a
separate platform limitation; the widget itself performs no background loop.

## Built-in themes

Five themes ship inside `PlatformSettings` and appear in Settings →
Appearance without installation. They are protected from removal and cannot be
shadowed by a user-installed directory that reuses their reserved ID.

| ID | Name | Character |
| --- | --- | --- |
| `builtin.default` | Default | Warm-graphite minimalist baseline. Also the platform layer every other theme is composed over. |
| `widgetrail.builtin.cool-slate` | Cool Slate | Navy/slate palette with a desaturated blue accent. Tokens only. |
| `widgetrail.builtin.neon-circuit` | Neon Circuit | Deep-indigo palette, electric-cyan interactive accent, magenta label accent, 4–8 DIP corner geometry, and tracked uppercase eyebrows, badges, and state labels. |
| `widgetrail.builtin.arcade-rush` | Arcade Rush | Warm plum palette, a single fuchsia accent, 14–16 DIP panels with pill-shaped 44 DIP controls and circular tray targets, and uppercase reserved for eyebrows. |
| `widgetrail.builtin.redline` | Redline | Oxblood palette with a scarlet accent, a 2 DIP accent edge under every container, heavier tighter titles, and platform geometry left untouched. |

`builtin.default` owns every controller-safe dimension, the neutral focus ring,
and the component structure. A selectable theme is composed over it as the user
layer, so a theme normally only needs to retune `:root` tokens; Cool Slate does
exactly that. Neon Circuit and Arcade Rush additionally show how far a theme
can go while staying inside the platform contract, and that the two can differ
in geometry and label voice as well as hue: Neon Circuit tightens corners and
tracks labels uppercase, while Arcade Rush rounds the same controls into pills
and keeps sentence case everywhere but the eyebrow. Redline changes no geometry
at all and spends its identity on palette plus one structural per-edge border.

When a theme does change `corner-radius`, remember that a rounded container has
to grow its own radius by its padding, or the inner control crops against a
tighter outer corner. Arcade Rush carries a 25 DIP segmented-tab container
around its 22 DIP tabs for exactly this reason.

A theme that adds a per-edge border to a component family must also account for
that family's "transparent" variant, whose uniform border the platform zeroes:
a per-edge override outranks that zero and would reinstate the edge. Redline
resets `.wrail-card--transparent` explicitly.

### Semantic colors are not free to move

`--success`, `--warning`, and `--danger` carry meaning the accent does not, and
a theme that pushes its accent into their hue range has to resolve the
collision rather than ignore it. Redline is the worked example: its accent is
scarlet, so `--danger` moves to rose — still an alarm, roughly 35 degrees away
in hue, and lighter than the accent — while `--warning` moves to a clearly
yellow amber and `--success` stays cool mint. Never resolve a collision by
letting destructive intent and accent resolve to the same value.

### Layer priority beats selector specificity

`ThemeLayerCompiler` resolves each property by layer priority first and only
then by specificity. An explicit widget-package rule therefore defeats a
global-theme rule for the same property even when the global selector is more
specific. A global theme should provide defaults and semantic tokens rather
than attempt to override a widget's authored presentation:

```css
/* A widget that explicitly owns button background keeps this value. */
button { background: rgba(19, 24, 41, 0.98); }
```

Within one layer, selector specificity and source order retain their ordinary
meaning. A widget that directly overrides a state-varied property such as
`background`, `color`, `border-color`, `outline-*`, `opacity`, or `scale` owns
the required focused, pressed, selected, disabled, and busy variants too.
Prefer semantic `:root` tokens when a consistent value should flow through the
platform's complete state treatment.

Run `wrail theme preview <directory>` to confirm the result: it prints computed
`:focused` and `:selected` roles from the real built-in-plus-user cascade.

## Theme authoring and distribution

`wrail theme` is the supported data-only theme workflow:

```powershell
wrail theme new "Slate" --id dev.example.slate --publisher dev.example
wrail theme validate .\Slate
wrail theme preview .\Slate
wrail theme pack .\Slate --output .\dev.example.slate-1.2.3.wrtheme
wrail theme inspect .\dev.example.slate-1.2.3.wrtheme
wrail theme install .\dev.example.slate-1.2.3.wrtheme
wrail theme list
wrail theme remove dev.example.slate 1.2.3
```

A public package has exact-case root `theme.json` plus only the UTF-8 `.wrss`
files reachable from its entry file. Its strict schema-version-2 manifest is:

```json
{
  "schemaVersion": 2,
  "id": "dev.example.slate",
  "publisher": "dev.example",
  "name": "Slate",
  "version": "1.2.3",
  "entryFile": "theme.wrss"
}
```

The publisher is a lowercase reverse-DNS claim. The theme ID must belong to
that namespace, the version must use canonical dotted numeric notation, and
the entry must be a normalized package-relative `.wrss` path. Runtime discovery
continues to read legacy schema-version-1 local directories, but public packing
accepts schema version 2 only.

`theme pack` produces deterministic ordinal ZIP entries with fixed timestamps
and metadata and prints the SHA-256 digest. `theme inspect` reports identity,
the publisher claim, sizes, and digest without executing content. `theme
preview` compiles the real built-in-plus-user cascade and prints computed
semantic shell/widget roles for terminal or CI review. It is not a native
graphical preview and does not simulate accessibility, physical resolution, or
DPI behavior.

Installation revalidates through the production catalog/compiler, stages on
the settings volume under a random path while holding a bounded cross-process
lock, and atomically publishes `%LOCALAPPDATA%\WidgetRail\themes\<id>\<version>`.
An existing version is never overwritten. Remote installation accepts absolute
HTTPS or `github:owner/repository@tag/asset.wrtheme` and requires a pinned
SHA-256 digest; the GitHub shorthand names one exact release asset and never
uses `latest`, clones source, or builds a repository. Select the installed
version through Settings → Appearance. Settings and `theme remove` share the
same bounded cross-process catalog lock and exact-version retirement policy.
Only inactive user-installed versions can be retired; built-in and selected
versions are protected. Retirement atomically removes the exact version from
discovery before bounded no-reparse cleanup, without rewriting sibling theme
content or the appearance record.

The package boundary rejects traversal, unsafe Windows names, backslashes,
non-NFC or case-colliding paths, explicit directory and symlink entries,
reparse-point source/install paths, malformed/duplicate/unknown JSON, orphan
WRSS, invalid imports/types/variables, and any script, assembly, executable,
font, image, or arbitrary asset. Current package limits are 65 files, 240
characters per path, 4 MiB per entry, 4 MiB expanded total, and a 4 MiB
archive; tighter WRSS compiler limits still apply.

Validation establishes bounded data and a digest establishes exact bytes.
Neither authenticates the publisher. There is no signature, revocation,
automatic update, theme gallery, graphical preview, or asset broker yet. Read
the complete command, format, security, and GitHub workflow in
[theme packaging and distribution](theme-packaging.md).

## Current global cascade and ownership

`ThemeLayerCompiler` now uses explicit trusted layer priorities rather than
plain import order. The implemented managed precedence, from lowest to highest,
is:

1. built-in platform theme;
2. the selected global user theme; and
3. the widget package's own WRSS.

Layer precedence must be deterministic and stronger than selector specificity:
a widget-package base rule defeats a more-specific global-theme rule for the
same property. Specificity and source order continue to resolve conflicts
*within* one layer. A global or platform declaration still supplies every
property the widget omits. This behavior has a regression test.

The bridge uses this cascade when computing every widget snapshot. It caches a
widget's layered theme by global revision and clears the cache only after a
valid appearance publication. Consequently, a theme switch refreshes inherited
defaults without replacing explicit widget-package values or giving native code
a CSS parser.

Custom properties follow the same order as declarations. A widget-package
`:root` definition is explicit and wins over the selected global theme. When
the widget omits that definition, `var(--token)` inherits the selected theme or
the built-in host default. This keeps global themes useful as defaults without
silently restyling a component the widget author deliberately owns.

The bridge also resolves bounded typed shell roles for canvas, backdrop, panel,
tray, tray-item states, title, body, hint, and status. The native host requests
that payload without starting a widget worker, adapts it through the native
style policy, and applies supported colors, typography, radii, and focus
outlines.

The native resolver applies host-owned accessibility policy after computed
styles for both shell and generic widget nodes. No WRSS selector can bypass
text scale, reduced motion/transparency, bold-text minimum weight, or the
high-contrast focus/text correction.

The implementation still needs to freeze:

- semantic shell roles/classes available to global themes;
- whether a user can opt to preserve service brand accents;
- namespacing for a global rule that intentionally targets one widget ID; and
- how default variables are inherited without exposing private widget data.

Globally layered widget styles travel through the `base`, `focused`, and
transient `pressed` maps to the generic renderer. Static
`selected`/`disabled`/`busy` snapshot state participates while those maps are
computed. The native renderer applies the pressed map only to the exact active
physical action and interpolates changed `opacity` and `scale` targets for
stable nodes. Separate runtime maps for future dynamic states and animation of
other property families remain incomplete.

## Current persistence and accessibility settings

The managed store already provides versioning, strict validation, bounded
cross-process updates, and atomic replacement. Theme selection is pinned by ID
and canonical version rather than a loose path. Monitor-specific pixel
coordinates are not settings fields.

Contrast, bold text, and transparency are additive optional members of the
strict schema-version-1 appearance record. Missing fields use the defaults,
which preserves earlier files without silently reinterpreting existing values.
Auto-scroll remains a future preference; screen-reader/magnification integration
and combined localization/visual evidence are also open.

## Current no-poll reload and last-good behavior

`ThemeManager.ReloadAsync` serializes a reload, reads the version-
pinned selection, compiles a complete platform/user snapshot, and publishes it
with a monotonically increasing revision only when valid. Missing/invalid
settings, theme lookup failure, unsafe imports, or WRSS errors retain the prior
snapshot and revision while updating bounded diagnostics. `CompileForWidget`
adds the widget layer using the same priorities.

`PlatformAppearanceService` adds no-poll `FileSystemWatcher` subscriptions for
the settings file and versioned theme tree. It coalesces write/create/delete/
rename bursts for 200 ms, then asks the manager to reload. A valid publication
clears layered-widget caches and emits `platform-appearance-changed` with the
new revision. An appearance query returns the exact theme ID/version, scales,
backdrop opacity, motion, contrast, bold-text, transparency, and bounded typed
shell styles without launching any widget worker.

An invalid edit produces bounded diagnostics and retains the prior immutable
snapshot/revision. Watcher errors schedule the same safe reload path. There is
no timer or scan loop while files are unchanged.

The native client parses `platform-appearance-changed`, coalesces the latest
announced revision, requests a complete appearance only when it is newer, and
ignores stale revisions. The host retains its last good appearance on bridge or
parse failure, rebuilds native shell styles, applies the bounded appearance and
accessibility policy, then repositions/repaints the visible overlay without
restarting widget workers. A Windows settings change reapplies the current
immutable appearance revision so System contrast and motion follow the
operating system immediately.
Globally layered widget styles use the same revision and are recomputed when
their next snapshot is requested.

Platform `textScale` is applied after style resolution to both shell text and
generic declarative widget content. The renderer remeasures/reflows with the
scaled font size and letter spacing while preserving non-compounding `em`
inheritance. Broad physical visual evidence still remains.

An invalid edit must produce source, line, column, severity, stable diagnostic
code, and message in Settings and the host log. It must not flash the built-in
theme, partially apply declarations, restart widget workers, change widget
lifecycle state, or block controller input.

Installed releases are immutable. The installer stages and publishes a new
version directory; selection switches the exact pinned ID/version rather than
mutating an installed release in place.

## Accessibility overrides

The final host layer enforces these guarantees after every WRSS layer:

- bounded text scaling with native remeasurement/reflow;
- reduced motion forces widget transition durations to zero; System motion
  follows `SPI_GETCLIENTAREAANIMATION` for both widgets and the shell;
- reduced transparency removes blur and makes shell/widget node surfaces and
  opacity opaque, while leaving the separately bounded full-screen backdrop
  darkness under user control;
- bold text raises rendered text to at least weight 600; and
- forced High contrast, or Windows high contrast while preference is System,
  chooses black or white text/focus color for maximum contrast against the
  actual inherited surface and preserves a geometric focus ring at least 3
  DIPs wide. Standard explicitly ignores Windows high contrast.

Widget and theme authors cannot opt out. Remaining work is physical combined
visual evidence at 150% text, high contrast, reduced motion, and reduced
transparency; long/localized copy; auto-scroll; and screen-reader/magnification
integration.

## Resolution and monitor behavior

A global theme describes logical presentation, not a fixed screenshot. It must
remain valid when the host recomputes the responsive viewport for the active
monitor. Theme authors must not assume 1920×1080 physical pixels, one DPI, a
16:9 monitor, a fixed widget width, or positive desktop coordinates.

Use bounded logical dimensions, min/max constraints, flex behavior, line
limits, and supported `vw`/`vh` units. Test at minimum:

- 1280×720, 1920×1080, 2560×1440, and 3840×2160;
- 21:9 and 32:9 viewports with the centered safe stage;
- Windows DPI at 100%, 125%, 150%, and 200%;
- compact widths and long localized labels; and
- every accessibility combination listed above.

The current host has Per-Monitor-V2 placement, active-foreground monitor
retargeting, live DPI/display/work-area handling, a responsive logical viewport,
and deterministic containment/compact tests from pathological tiny and portrait
inputs through 4K/wide clients at varied DPI and interface scale. Physical
mixed-DPI migration/hot-plug screenshots, long localization, combined
accessibility, and broad on-hardware visual evidence remain gaps. A successful
WRSS compile is not proof of that matrix. See [display and
resolution](display-and-resolution.md) for the exact contract and evidence.

## Authoring and test workflow

### Available now

For widget-local styles:

```powershell
wrail validate .\styles\default.wrss
wrail validate .\MyWidgetPackage
```

For a global theme, use the complete `wrail theme` workflow shown in
[theme packaging and distribution](theme-packaging.md). `theme preview`
exercises the production compiler/cascade in a terminal; use native screenshots
and physical display/accessibility checks for pixel evidence.

The managed settings/theme contract suite is also available to platform
contributors:

```powershell
dotnet run --project .\tests\PlatformSettings.Tests\PlatformSettings.Tests.csproj -c Release
```

It covers strict persistence, concurrency, theme safety/discovery, layer
precedence, version pinning, and last-good reload.

The first-party Settings and bridge suites cover the controller surface and
appearance transport:

```powershell
dotnet run --project .\tests\SettingsWidget.Tests\SettingsWidget.Tests.csproj -c Release
dotnet run --project .\tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj -c Release
```

They cover nested B scopes, paged theme/package/version/permission selection,
disabled-only exact-version rollback, explicit enablement-versus-consent
review, bounds, busy/error/reset behavior, lifecycle/no polling, global selector
precedence, last-good appearance/catalog revisions, bounded shell appearance,
and lazy worker behavior. Current exact evidence is
recorded in [implementation status](implementation-status.md), not duplicated
as a drifting count here.

Native Release verification also covers the post-style accessibility adapter's
150% font-size/letter-spacing scaling and safe fallback, System/forced contrast,
minimum focus geometry, minimum bold weight, reduced transparency, and reduced
motion. Those contract tests are not a substitute for the remaining physical
multi-resolution and combined-accessibility visual matrix.

Keep theme-like tokens in `:root`, exercise semantic roles/classes in widget
snapshot and bridge tests, and use native renderer tests for properties that
must affect pixels. Treat warnings such as clamping as release-review items.

### Remaining preview and native evidence

Theme scaffold, validation, computed preview, deterministic packaging,
inspection, immutable install, and catalog listing are available. Remaining
tooling/evidence includes:

- a native graphical or screenshot preview covering the shell, dashboard,
  Settings, and representative widgets;
- remove/update/rollback commands and controller UI;
- publisher signing, revocation, and a curated gallery;
- `wrail dev` live authoring orchestration;
- deterministic screenshot regression and the full 150% text-scale/reflow
  matrix; and
- the physical mixed-monitor/DPI/accessibility matrix with retained evidence.

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
