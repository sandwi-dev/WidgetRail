# Implementation status

Status: integrated Phase 0 platform prototype, 2026-08-09

This repository contains working native and managed components. It is not yet
a production overlay, signed public-distribution trust boundary, end-user
installer, or marketplace.

Responsive Row wrapping, protocol-v7 ActionSurface, protocol-v8 ResponsiveGrid,
MediaTile/AppTile, Toast, semantic CodeText, independent per-edge borders, and
the first production uses of the public Picker and Scrubber are implemented in
source and focused tests. Games & Apps uses AppTile for its full-tile launch/
catalog targets and lifecycle-safe Toast feedback. Settings uses ResponsiveGrid
for root categories and CodeText for schema/worker diagnostics. The authoritative
full Release verification aggregate is green; hands-on packaged visual/
controller evidence remains pending until the relaunched overlay is exercised.

The current authoring-coordination milestone also implements public non-paged
`WidgetResource<TValue>`, bounded `WidgetNavigator<TRoute>`, validated
`WidgetIds`/opaque `KeyedId`, active input-scope propagation on every standard
open-widget action, protocol-v13 explicit focus persistence, and the responsive
`UI.NavigationShell`. SDK Gallery is the production-style navigation/ID
migration. YT Music 0.2.6 maps typed/status-only failures to bounded copy and
uses one lifecycle-owned Active/Latest lane for transport reconciliation; it
neither retains provider response bodies nor renders unknown exception text.
Focused Release suites pass Widget SDK 84/84, SDK Gallery 6/6, YT Music 48/48,
and Gbar CLI 50/50. Packaged controller/companion evidence remains separate.

## Implemented

### Native overlay and input

`src/OverlayHost` starts hidden, uses a GameInput Guide callback, stops ordinary
polling/rendering while hidden, and presents a Win32/Direct2D controller
dashboard. It supports reorder mode, last-widget/order persistence, focus
routing for the reference widget, managed bridge startup, bounded HTTPS
artwork, fixed native semantic icon geometry, responsive logical viewports,
and per-monitor placement.

Asynchronous widget action failures are retained as fixed generic copy in a
host-owned, 256-widget-bounded controller keyed by widget ID and runtime generation.
Concurrent failures for different widgets no longer overwrite each other;
dashboard and open-widget footers resolve only the requested widget's current
generation. Catalog replacement/removal and overlay hide discard retained
state. The controller accepts caller-supplied monotonic time and returns the next
deadline plus whether visible state changed. A dedicated timer removes expired
copy and requests one repaint, with the already-active controller timer providing
a no-extra-repaint fallback if Win32 cannot create that timer.
A thin native host adapter now owns the bounded catalog identity projection,
consumes one complete bridge failure drain as one transition, selects exact
dashboard/open-widget feedback, and applies timer/invalidation callbacks once
per batch. Catalog replacement/removal, hide/show, and Stop use the same seam;
the renderer, HWND, and bridge transport remain outside it. Its deterministic
Release target feeds two widgets through one pump, proves offscreen isolation,
one-shot expiry and controller-timer fallback, rejects a late prior generation,
and prevents feedback resurrection after hide or Stop.

The renderer can retain bounded visible semantic geometry on demand, and a
pure native accessibility-tree builder combines it with exact runtime/snapshot
identity, active input-scope filtering, names, values, focus/state, Invoke
metadata, and Slider ranges. ActionSurface descendants are collapsed into one
semantic target. Collection stays allocation-dormant until the first UIA query. The
HWND now publishes the immutable open-widget tree through `WM_GETOBJECT` and a
free-threaded Windows UI Automation fragment provider. Button/ActionSurface
Invoke and Slider RangeValue enqueue bounded asynchronous requests; the UI
thread revalidates the exact widget/runtime/snapshot/scope/node/action tuple
before using the existing controller-input route. Slider writes are quantized
and coalesced latest-wins. Controller slider optimism now advances one host
presentation revision resolved before the projection key; rendered pixels and
the accessibility tree consume the same presented-value map through adjustment,
acknowledgement, and timeout. A real `IUIAutomation` client test covers provider
publication, traversal, names, physical screen bounds, patterns, and stale
runtime rejection and verifies that the RangeValue event and provider both
carry the presented numeric value. A separate projection-cadence contract proves
stable and animation-only paints do not rebuild, while transition completion
requests one final-geometry projection. The provider now diffs the last
announced immutable tree and coalesces structure, logical-focus, and closed
property events behind one posted window message outside paint. It covers
semantic names/help, enabled/
selected state, RangeValue state, and DPI-aware physical bounds; a real UIA
client-handler test receives structure, focus, property, and live-region
signals. Each root/fragment is also
bound to one HWND generation: explicit pre-destroy detach disconnects UIA,
clears message/action authority, and keeps old providers unavailable across
same-handle reuse. A real client retains original and rebound roots through an
actual `DestroyWindow` and observes element-unavailable results. Root focus/
visibility are UI-thread-published, focus loss does not target the custom root,
and root plus node resize/DPI bounds are announced.
UIA Invoke/RangeValue input now crosses native host, bridge, runtime, and SDK
with an explicit `AccessibilityAutomation` origin. Physical controller remains
the omitted compatibility default. The bridge accepts revalidated automation as
an ordinary action but never mints dashboard gesture authority for it; the
runtime rejects any mismatched authority reservation, while the worker and SDK
independently omit private gesture context. Broker gesture sequences are emitted
only after exact host activation succeeds. The adversarial runtime case invokes
a capability synchronously from an automation-origin dashboard override and
records no context, activation, provider call, or companion grant; a separate
denied-activation case proves no gesture metadata reaches the broker. Focused
Release results are WidgetSdk 84/84, WidgetRuntime 49/49, and WidgetBridge
46/46. Packaged AppContainer/UIA evidence
remains a ship-gate item rather than an inferred OS-boundary claim. The
canonical Release host build and native suites are green for this change.
The dashboard now exposes its exact painted title as a level-one heading. Static
controller guidance is readable non-live text, while transient action feedback
replaces it with one polite status live region. Title, help, status, and catalog
display names participate in the host projection revision, and a real UIA client
observes the heading/live properties plus a status-change event. Tray Focus and
Invoke requests resolve the revalidated stable widget ID in one state transition
instead of replaying up to 256 directional navigation commands. Open widgets now
publish one composite root named for the active page, with widget controls,
closed non-focus-stealing Back/Close commands, exact visible quiet help or live
status, and the visible tray. Switching focus regions changes one focus owner;
it no longer replaces the semantic surface.
Composite elements now carry a closed widget/host-shell/tray owner domain
through AutomationId, runtime identity, lookup, event diffing, and queued action
authority. Cross-domain raw-ID reuse is safe, duplicate same-domain publication
fails closed, and authors do not reserve shell prefixes. Root Back remains a
typed tray transition; nested Back now mirrors the managed pressed-B resolver
for current focus, focusless scope roots, focused Disabled/Busy suppression,
ancestor fallback, stale focus, and nested-scope isolation. Revalidation repeats
the same focus-aware decision before automation-origin dispatch, with no scope
fallback. The native mirror uses allocation-free recursion within the protocol's
bounded tree depth, so the `noexcept` publication path cannot terminate on an
allocation failure.
Focused Release coverage passes Slider Interaction 2086, renderer 4636,
accessibility tree 16, projection cadence 11, host semantics 32, event planning
11, and provider 149 checks. The isolated Release native aggregate passes 24/24,
and the canonical Debug build/test path is green. Legacy MSAA and packaged
Narrator evidence remain open, so full screen-reader support is not yet claimed.

The dashboard and open-widget tray now share one pure `ComputeTrayLayout`
result across painting and pointer hit-testing. The bounded visible window,
selected-slot centering, embedded tray band, below-preferred fallback, and edge
hit policy have focused native coverage. This removes the prior duplicated
geometry formulas and gives the host UIA fragment tree the same final tile
rectangles as pixels and pointer input.

The visible dashboard/open-widget tray now publishes those shared rectangles as
UIA ListItems with required single selection and Invoke. The immutable host tree
retains selected/focused/enabled state and a monotonically increasing host
sequence. Select and Invoke queue a closed `ActivateTrayItem` action with a
stable widget target; the UI thread rejects stale trees, exits reorder mode, and
uses one direct stable-ID tray state transition. The real UIA-client suite
discovers the ListItem and obtains SelectionItem, while direct provider coverage
verifies the typed queued authority. The dashboard also publishes its heading,
static help, and transient live status. The open-widget composite retains those
tray items alongside widget content and typed Back/Close/footer semantics.

The visible shell uses separate panel and dimming-backdrop windows on the active
external foreground app's nearest monitor. An outside backdrop click closes the
overlay. Both windows are topmost only while visible. Ordinary controller reads
use a visibility-scoped GameInput lease; confirmed foreground adds exclusivity,
while denied activation remains readable and diagnosed as background-shared.
Alt+Tab/external foreground loss closes rather than retargets the visible overlay. DPI, display
topology, work-area, and client-size messages recompute placement/resources;
reentrant DPI placement is coalesced. This is normal DWM windowing, not game
injection.

Ordinary visible controller state is read through GameInput background delivery
plus best-effort foreground exclusivity. Exclusivity suppresses other GameInput
clients only; XInput, Raw Input,
direct HID, Steam Input, and remapped virtual controllers remain outside a
normal desktop overlay's containment boundary. Arrow keys, Enter, and Escape
provide unadvertised navigation/select/back fallbacks. Mouse hit testing uses
the same clipped active-scope geometry as controller focus; backdrop clicks
close and visible declarative controls receive only semantic A/select.

Guide/Home shows or hides from any depth. Dashboard D-pad/left-stick movement,
A, B, and Y are host-owned; dashboard B closes the overlay. Open widgets receive
their other semantic actions; B falls back to the dashboard only when unhandled
in the root input scope. Nested scopes never bubble to the root.
GameInput is the primary Guide source. A removable XInput compatibility adapter
uses an undocumented ordinal only for drivers observed to omit Guide callbacks;
its 25 ms timer is dormant unless GameInput reports an Xbox 360-family device,
with the previous always-on path retained only if device tracking cannot
register. It is not a universal device-compatibility guarantee.

Open-widget left-stick navigation is two-dimensional with engage/release
hysteresis and bounded repeat. Focus uses explicit neighbors first and
deterministic rendered geometry as a fallback, without wraparound. Disabled
and Busy Buttons/Sliders retain focus but suppress action dispatch. Focused
Sliders consume horizontal input for bounded value adjustment and retain
Up/Down navigation.

### Widget platform

- `WidgetProtocol`: strict version-1 manifests and additive snapshot protocols
  v1–v13, deterministic JSON, stable IDs, focus validation, quick actions with
  optional typed control-operation metadata, Scroll/surface hints, absolute-
  value Sliders, images, closed semantic glyphs, explicit active controller
  scopes and snapshot correlation, and interaction state.
- `WidgetSdk`: typed Stack, Row, protocol-v8 ResponsiveGrid, Text, semantic
  CodeText, Button, Progress, Slider, Scroll, Spacer, Image, Icon,
  LoadingIndicator, and protocol-v7 ActionSurface authoring;
  controller-ready ToggleButton, Stepper,
  IconButton, Card, SectionHeader, StatusBadge, Divider, Alert, EmptyState,
  SegmentedTabs, Switch, ScopedDialog, SettingsRow, and bounded nested
  ActionSheet plus single-select Picker, Scrubber, MediaTile, AppTile, and Toast
  composites with stable semantic
  `gbar-*` theme hooks; button glyphs; focus/shortcut/state helpers; scoped
  shortcut routing; bounded latest-wins Slider coalescing; invalidation;
  runtime-integrated immutable `WidgetModel<TState>` snapshots/updates,
  non-paged resources, model-backed SingleFlight/Latest/Serial optimistic
  commands, bounded route navigation with exact-scope Back/return focus, and
  validated hierarchical/opaque-key IDs; bounded lifecycle-owned operation
  lanes and offset-paged resources; one 16-pending-item active-lifetime action
  FIFO shared by direct/quick/controller ingress with typed admission,
  slider-tail coalescing, cooperative lifecycle drain, late-failure events, and
  active input-scope correlation on every standard open-widget action;
  five-state lifecycle hooks/tokens; bounded, non-overlapping
  Visible/Interactive tickers; transport-neutral capability access; and typed
  audio/network/Bluetooth/recent-activity/app-library/media-session services,
  exact-port loopback JSON, write-only private secrets, durable private JSON
  state with revision/CAS, descriptors, DTOs, events, errors, and public
  deterministic state-test fixtures.
- `WidgetRuntime`: lazy out-of-process workers over bounded framed JSON with
  prompt typed action admission rather than provider-completion acknowledgement,
  protocol-v1 empty-ack/failure-name compatibility, explicit lifecycle
  transitions, per-start host-owned companion sessions, pre-process host-owned
  residency leases, timeouts, asynchronous action/process failure reporting,
  and limited restart. Installed/community workers
  require stable host-derived capability-free Low-integrity AppContainers,
  stripped environments, exact non-inheriting verified-file grants, PID-bound isolated
  pipes, and pre-launch Job Object memory/process/UI/cleanup
  containment. Public custom workers use
  `WidgetWorkerBootstrap`, which validates host arguments, authenticates the
  optional broker before constructing the widget, attaches host services before
  creation, and owns cancellation and transport disposal.
- `WidgetBridge`: current-user-only native sidecar pipe, trusted plus installed
  and manifest-backed bundled catalog discovery, no-poll last-good catalog
  monitoring/semantic revisions, compatible-worker preservation and changed-
  worker retirement, worker forwarding, shared direct/quick/controller action
  admission with explicit inactive/saturated failures, generation-owned late
  action failures, controller input,
  host-owned mandatory community isolation selection, PID/identity/declaration-
  bound broker companions, single-use exact-operation dashboard gesture
  authority, per-session verified-content lease handoff, aggregate resident-worker/count admission with a separate Settings
  control-plane slot, invalidation/failure events, no-poll platform-appearance revisions,
  globally layered widget themes, bounded shell appearance, and computed GBSS
  styles.
- `WidgetBridge` and the native declarative renderer preserve the distinct
  `loadingIndicator` render role (rather than treating it as a container) and
  understand protocol-v7 ActionSurface orientation, computed style, bounded
  layout, full-surface focus/hit/pressed geometry, and fail-closed unknown-kind
  behavior. Reduced motion keeps LoadingIndicator accessible but static.
- `WidgetBridge` and native layout also transport and validate protocol-v8 Grid
  semantics. Grid derives bounded row-major columns from final logical-DIP
  width while preserving child IDs/focus order; it never becomes a focus stop.
- `WidgetWorkerHost`: a packaged generic worker executable that loads one
  installed package's public concrete SDK `Widget` entrypoint and contained
  dependencies inside the mandatory package AppContainer, authenticates an
  optional broker channel, attaches typed host services before creation, then
  serves the standard isolated snapshot/action/lifecycle protocol.
- `WidgetStyling`: single-handle consumed-byte bounds, strict UTF-8 GBSS
  decoding, optional exact per-file SHA-256 inventories, closed typed source-
  failure results with stable sanitized diagnostics, safe package-relative
  imports, variables, explicit trusted cascade layers, typed allowlisted values,
  independent top/right/bottom/left border width/color overrides, and
  diagnostics. The built-in theme provides bounded `.gbar-code-text` wrapping
  with one Windows-baseline `Consolas` family; CSS-style font fallback stacks
  and packaged font loading are not claimed.
- `PlatformSettings`: strict atomic/cross-process appearance persistence,
  version-pinned development themes, built-in default, safe theme discovery,
  layer composition, last-good snapshots, and bounded declared-
  publisher/package-scoped public widget configuration under `widget-config`,
  with unambiguous owning-namespace resolution for unsigned runtime authorities.
- `WidgetCatalog`: safe `.gbarwidget` inspection/extraction, host-sealed content-
  tree integrity with single-handle bounded metadata reads and exact-length
  file hashing plus manifest bytes captured from the verified tree, complete
  relative-path/length/SHA-256 inventories, and bounded per-session launch
  leases that pin every verified file against write/delete replacement,
  version-addressed installs, schema-1 state migration, fail-closed
  exact version pins, discovery,
  enablement, and pin-preserving order persistence. Enabled compatible packages
  join complete validated live bridge revisions and remain lazy until first use.
- `PlatformBroker`: a version-1 audio/network/Bluetooth/recent-activity/app-library/media/
  exact-loopback/private-secret/private-state
  capability foundation with a closed versioned grant vocabulary, a separate
  Bridge-only non-consent state grant, SID/Low-
  label/PID-bound isolated endpoints plus nonce/
  identity authentication, manifest/consent/lifecycle enforcement, strict
  bounded DTOs/events, atomic consent persistence, bounded/coalesced
  subscriptions, and a deterministic simulator. It is connected to widget
  `HostServices`; the trusted bridge composes the real Windows audio, network,
  Bluetooth, foreground-activity, Start Menu app-library, media, and community-
  companion backends.
- `WindowsAudioProvider`: an event-driven Core Audio backend for sanitized
  per-application sessions on the current default multimedia render endpoint.
  A dedicated MTA owns native objects; callbacks only enqueue coalesced refresh
  work. It supports endpoint master volume/mute, per-session volume/mute,
  sanitized current default-device visibility, and volume/mute for the current
  default microphone. There is no undocumented default-device setter and no
  microphone sample capture.
- `WindowsNetworkProvider`: a lazy event-driven Windows backend with a dedicated
  MTA owner, bounded/coalesced queues, coarse IP Helper connectivity hints,
  ACM-only Native Wi-Fi notifications, explicit available-network scanning,
  generation-bound opaque result IDs, and saved/open result connection.
  Automatic status reads still do not query location-sensitive current
  SSID/signal, and no scan runs without an Interactive user action. Separate
  read/control grants expose software Wi-Fi radio state through
  `WlanQueryInterface`/`WlanSetInterface`; hardware/policy state remains
  authoritative and multi-PHY partial failure is explicit.
- `WindowsBluetoothProvider`: a lazy event-driven WinRT backend for sanitized
  Bluetooth software-radio state and bounded nearby/paired/connected discovery.
  It supports software radio On/Off and explicit pairing of one current opaque
  device through Windows Association Endpoint pairing. Unsupported ceremonies,
  paired-device management, and profile-specific operations open the Windows
  Bluetooth Settings surface through a separately consented capability. Native
  device IDs never cross the broker. Host-owned unpair and generic device
  Connect/Disconnect are not implemented.
- `WindowsActivityProvider`: a lazy WinEvent foreground/destroy observer with
  no polling. It keeps at most 16 eligible running applications and publishes
  bounded display names plus per-process-lifetime opaque IDs; activation can
  switch only to a still-running observed window. It does not read UserAssist,
  launch executables, expose PID/path/HWND, or claim game classification.
- `WindowsAppLibraryProvider`: a lazy bounded current-user/all-user Start Menu
  `.lnk` catalog. It publishes sanitized names, conservative kinds, and random
  short-lived launch IDs plus broker-derived authority-scoped durable SavedIds;
  launch re-enumerates and requires one exact unchanged shortcut before
  invoking only the Shell `open` verb. Paths, raw stable identities, targets,
  arguments, AUMIDs,
  package identities, PIDs, and HWNDs never enter the widget contract.
- `WindowsMediaProvider`: a lazy, event-driven Windows Global System Media
  Transport Controls (GSMTC) backend. It publishes bounded sanitized sessions
  with broker-issued process-lifetime IDs, retains multiple sessions from the
  same source application, controls the exact selected session, and does not
  expose AUMID, PID, executable path, window handle, or raw platform objects.
- `WindowsCommunityProvider`: constrained JSON GET/POST to one declared
  nonprivileged IPv4 loopback port plus package-scoped write-only private
  secret slots. It disables DNS, proxy, redirects, cookies, decompression, and
  raw socket exposure; streams bounded strict-JSON responses; injects optional
  Bearer values inside the trusted provider; optionally removes the exact
  rejected scoped Bearer on HTTP 401 while its dependent lease remains valid;
  and stores hashed publisher/
  package/slot targets in Windows Credential Manager without returning values
  to widget IPC. It also owns the separate private-state backend: one strict
  canonical 64 KiB JSON document/tombstone per authenticated publisher/package,
  cross-process serialization, atomic replacement, revision/CAS, persistent
  mutation throttling, and corruption/reparse failure closed.
- `GbarCli`: working `new`, `validate`, `render`, `replay`, deterministic
  `pack`, bounded local/HTTPS/GitHub Release `install`, and catalog `list`,
  `enable`, `disable`, and `version list|select|rollback` commands. Local and
  remote updates share an exact-stream pre-publish enabled-ID guard. Remote
  acquisition requires SHA-256 pinning, reports the actual digest, and installs
  disabled pending explicit review. `gbar new` auto-discovers the source SDK or
  accepts an exact `--sdk-project`; unresolved external use fails before write
  instead of emitting an unpublished placeholder package. `gbar dev` provides a bounded unsigned
  source/package watch-build-run loop through the production generic worker,
  AppContainer, broker, lifecycle, renderer, and Settings permission path. A
  controller/hotkey-free candidate must authenticate its exact catalog/widget/
  instance and return a validated snapshot before replacing the last-good
  interactive generation; failed generations retain or restore last good.

Audio Mixer, Network Controls, Games & Apps, and Now Playing are manifest-backed
`bundledWidgets`: they use the same generic `WidgetWorkerHost`, package-specific
capability-free AppContainer, authenticated capability broker, lifecycle,
renderer, and manifest-derived authority as an independently installed
community package. Their host catalog entries supply only platform-owned shell
presentation identity and package location. A Windows Release conformance suite
packages, installs, enables, resolves, launches, renders, and acts through that
same public path for all four. YT Music is a separately installable Community
package on that same generic AppContainer path; its conformance case builds the
real `.gbarwidget`, installs/enables it through the public catalog, drives
pairing and dashboard transport through the broker, and asserts there is no
trusted catalog/worker fallback. Settings is the only temporary trusted Job-
only exception. The Clock sample also exercises the public package path.
Audio Mixer and Network Controls are bounded integration slices for the larger
controller-first Audio Control and Network Control roadmap items; their
presence here does not mean those product widgets are complete or shipped.
Settings renders through
the generic SDK/bridge/native path, uses nested controller scopes, persists
bounded appearance values, pages valid/invalid themes, exposes diagnostics,
requires confirmation before reset, and provides two separate controller
flows: a source-separated widget inventory with read-only Built-in manifest
details and an honest Community review that labels packages unsigned, treats
the manifest publisher as unverified, shows the full sealed SHA-256 digest,
and repeats that digest at consent; a nested paged Manage versions surface with
digest prefixes and disabled-only exact selection/rollback; plus enable/disable
for Community packages only; then package → capability → grant/deny consent. Enablement is
not consent. Permission grants require explicit
confirmation; deny/revoke is immediate, missing/invalid state fails closed,
and first-party packages are not auto-granted. An exact tombstone migrates the
retired Recent Apps activation decision without discarding current consent;
arbitrary unknown capability IDs still invalidate the document and fail closed.
Unsupported declarations and inactive saved decisions now appear behind one
focusable Review row rather than inline text. Its nested read-only controller
Scroll uses B-only return, stable opaque IDs, sanitized bounded labels, one
shared 16-item detail budget, and exact publisher-authority distinction. It
classifies inactive decisions only when catalog projection and consent are
valid and complete; otherwise it explicitly reports classification unavailable
and publishes no inactive rows. It never removes or rewrites consent.
It reloads settings/themes/
catalog/permissions once per active lifetime and does not poll in Background.
The same public compatibility evaluator gates Bridge and Settings: details show
host API/architectures and a bounded reason, incompatible enablement is blocked,
and disable remains available for recovery.

The current Audio Mixer reference slice is packaged and registered through the
same catalog/bridge/worker path as the other widgets. Its widget code uses only
the public typed audio service,
fetches once on activation, then
reacts to provider events rather than polling. It offers controller session
selection plus optimistic per-session volume/mute controls in Interactive and
renders explicit permission, lifecycle, empty, unavailable, and failure states.
One root controller Scroll contains master, sanitized device summary,
microphone, and every application row; this replaces the clipped nested session
viewport and gives `audio.root` one stable host-owned offset/focus-follow
surface at normal and constrained heights. One explicit Up/Down chain reaches
the true master-output top, optional microphone control, and every application
through the final bottom row. The widget remembers the last focused master,
microphone, or stable application target from controller input; reopen restores
that target, and session churn falls back to the nearest surviving row instead
of jumping to master. It also shows sanitized current
default output/input device names and offers controller sliders/mute for the
current default microphone behind independent optional grants. Endpoint
selection remains display-only because no supported system-default setter has
been adopted.
Broader hardware/churn coverage and end-to-end hidden/visible performance evidence
remain open; this is not yet an end-user release claim.

The current Network Controls reference slice runs as a manifest-backed bundled
package through the generic community worker/AppContainer path. It requires
coarse network and available-Wi-Fi read grants,
makes current saved/open result connection optional and Interactive-only,
opens acknowledged status and available-Wi-Fi subscriptions before snapshots,
and never polls. It declares no dashboard quick actions. The open widget has
separate Wi-Fi and Bluetooth views under a segmented tab bar. LB/RB switches
the active view from any root focus, while D-pad/left-stick navigates within the
active view. Wi-Fi and Bluetooth use the independent stable Scroll IDs
`network.wifi.body.scroll` and `network.bluetooth.body.scroll`; each retains its
own opaque selected item so returning restores the focus target and lets host
focus-follow restore its visible location. In Wi-Fi, A starts one explicit scan
from the Scan control and A or X routes a connection through the exact focused
row. LT/RT never cycles list items.
The shared segmented-tab container now has a nonshrinking 50-DIP minimum region
around 44-DIP tab targets. Native layout regression scenarios at the normal
compact viewport and a constrained high-interface/high-text-scale viewport
verify both tabs remain visible, controller-enabled, inside the viewport, and
inset far enough for an unclipped focus border.
The real provider returns on `WlanConnect` acceptance, then publishes
authoritative `Connecting`, `Failed`, and refreshed status events. Its focused
provider/widget suites and the complete Release verifier pass at milestone
`8e8c90a`. Focused checks do not replace hardware/privacy/performance matrices,
which remain open. The current full managed Release suite, native host suite,
package required-file checks, hidden-startup smoke, and controller input-probe
smoke pass.
Available-network broker/SDK contracts and the explicit, event-driven Native
Wi-Fi scan/connect provider are declared and rendered by the bundled widget.
They use generation-bound opaque IDs and cover saved-profile-backed and
unsaved-open connection starts with dedicated broker/provider/widget tests.
Host-owned WPA Personal credential entry remains staged. Software Wi-Fi radio
read/control, Bluetooth radio/discovery, explicit pairing, and a separately
consented Windows Settings management fallback are implemented behind granular
grants. Unpair and generic Bluetooth Connect/Disconnect remain outside the
current broker surface, and physical pairing still needs reversible hardware
verification.

Games & Apps has replaced Recent Apps in the bundled catalog and first-party
package conformance path. It is an ordinary public-SDK package in the generic
AppContainer, requires `system.apps.library.read.v1`, optionally declares the
separate `system.apps.library.launch.v1`, and loads bounded 32-item pages. Its
default Library shows only entries the user adds from a nested vertical
Catalog. Library and Catalog entries are full-width single-focus rows; curated
rows can place the bounded trusted PNG inside that same Button target. A
toggles Catalog membership, X removes from Library, and one
confirmed launch moves that exact item to the front. Curation, selected item,
and recent-first order persist across worker restart/unload as authority-scoped
durable `SavedId` values in `HostServices.PrivateState`; activation resolves
only those saved entries to fresh short-lived launch tokens. The broad provider
catalog is loaded only after the explicit **Add applications** action. Launch is
enabled only while Interactive and targets only the opaque ID associated with
the exact action source. Permission, lifecycle, healthy-empty, unavailable,
stale-item, and generic failure states remain controller reachable and
sanitized. A successful provider result emits one generation-bound host effect
that closes the overlay only after the exact provider launch succeeds; failure,
denial, cancellation, or a stale widget generation keeps it open.

Initial and explicit-retry library reads run in the SDK's runtime-owned Active
operation lane, so leaving the widget cancels and drains provider work before
the lifecycle transition completes. Toast expiry likewise has one disposal
owner and removes its shared cancellation reference before disposal;
deactivation cannot race an already-disposed source. The focused suite covers
cancellation during retry, and the real generic-worker/AppContainer path passed
three consecutive lifecycle conformance runs.

The trusted `WindowsAppLibraryProvider` lazily merges the bounded current-user/
all-user Start Menu Programs roots with the current user's Shell AppsFolder on
one process-wide bounded STA lane. It skips shortcut reparse points, accepts
canonical AUMIDs, deduplicates trusted descriptors, and exposes only sanitized
names, conservative kinds, short-lived random launch IDs, and broker-derived
authority-scoped durable SavedIds. Raw paths, AUMIDs, stable provider identity,
arguments, and returned activation PIDs remain host-only. Before launch it
re-enumerates the exact source. A shortcut must retain its scope, target,
path, and fingerprint before constrained Shell open; an AppsFolder entry must
retain exactly one canonical AUMID/revalidation identity before null-argument
`ActivateApplication`. For curated entries it rasterizes the trusted shortcut
or AppsFolder Shell icon into a bounded inline PNG; broad Catalog discovery
stays text-only. Launcher libraries and authoritative game classification
remain open. Recent Apps remains only as a read-only
foreground-activity API/test reference and is not packaged in the current
overlay. See [Games & Apps](games-and-apps.md).

Now Playing is the public-SDK media-session reference. It uses only the typed
`HostServices.Media` surface over the authenticated broker and the event-driven
GSMTC provider; the worker cannot open GSMTC directly. It retains duplicate
sessions from one application through opaque broker IDs, preserves the selected
session across complete snapshots, renders a compact controller surface with a
bounded host-normalized GSMTC thumbnail when the media app publishes one, and
interpolates active progress locally at four Hz between authoritative events.
All render-facing session, selection, pending-command, view/status, revision,
live-update, and reload state now resides in one `WidgetModel<State>`; a focused
regression proves one invalidation for a changed selection and none for a
repeated equal selection. Command reconciliation remains widget-owned rather
than inferred by the SDK, but SingleFlight admission, lifecycle-owned provider
execution, current-attempt completion, safe error fallback, and rollback
sequencing now use `WidgetOptimisticCommand`. Play/Pause projects immediately;
rollback restores only the affected session when its generation/revision is
still current.
X/LB/RB quick actions expose play-pause/previous/next while the card is merely
Visible. Each quick action names the one media control operation it may invoke;
the bridge first records a dormant host-owned reservation for at most 10 seconds
so bounded serial widget work can reach the exact call. That reservation is not
broker authority. Invoking the exact typed operation atomically activates one
identity/PID/snapshot/input-sequence-bound broker lease for at most two seconds.
It is consumed once without promoting lifecycle or enabling subscriptions.
Normal declaration, consent, payload, and provider checks still apply.

YT Music is the first Community addon integration reference. It is absent from
the trusted/bundled host catalog and runtime-copy list, and its retired custom
desktop worker/Credential Manager adapter are removed. The build helper stages
the real manifest, assembly, and GBSS, then uses public `gbar validate`, `pack`,
`install`, and `enable` commands. The generic package AppContainer accesses
YTMDesktop2 only through declared `network.loopback:13091`; optional
`storage.private-secrets.v1` persists pairing without returning a token to the
worker. Host-side bearer injection requires both grants. The addon performs a
request-scoped host-side invalidation of the exact rejected Bearer slot on HTTP
401, clears only its local connection cache, and never races a second delete.
It performs a non-blocking automatic connection attempt and renders media
metadata, artwork, transport state, and
dashboard quick actions through the declarative protocol. It auto-connects on
first entry into Visible/Interactive, interpolates progress there at four Hz,
reconciles the companion every two seconds, and uses bounded optimistic transport/rating
updates with rollback on command failure. Like, dislike, shuffle, and repeat
publish immediate semantic selected/busy feedback, preserve independent pending
features through stale polls, and clear or roll back on reconciliation.
Accepted play/pause commands now reconcile in a bounded widget-lifetime task
rather than the shorter input-action lifetime. The completion test inspects
pending authoritative confirmation instead of the merged optimistic snapshot,
and repeated toggles supersede the earlier refresh burst without blocking.
Its connected X/LB/RB/Y window shortcuts are declared once on the active root
scope, so they resolve from every connected focus target without sibling
searching and remain isolated from nested scopes. Previous/Next use corrected
native directional geometry.

The overlay now treats the selected widget panel and icon tray as persistent
sibling regions. Tray selection swaps the Visible panel automatically; A moves
focus into its controls and publishes Interactive; root B or a root Down
boundary returns focus to the still-visible tray; tray B closes; and nested
scopes remain contained. Guide is a region-independent global toggle. The
responsive recovery path also accepts an empty current focus: it selects the
first visible root control when possible, and a fully unreachable root returns
to the tray on Down while a clipped nested scope stays contained. The built-in
themes also use non-shrinking fixed regions, a thin native Slider
 track inside the 44-DIP target, and lighter typography/radii/spacing. Pressed
 Up from the tray now enters the visible widget. Scroll focus-follow snaps the
 first and last focusable descendants to the true extent boundaries. The
 host resolves focus-edge pagination against the current row before an ordinary
 move can leave its Scroll, and a changed replacement-page focus request
 outranks stale ordinal focus memory. That request is consumed when focus is
 remembered, so an unrelated refresh cannot steal focus again. Deterministic
 compact and expanded Spotify coverage now drives the real 29-item playlist and
 detail resources through the 12/12/5 forward/reverse sequence, including a slow
 cancellation-ignoring page, joined repeated edge input, cached reverse pages,
 absolute visible IDs, and exact provider call counts. Matching native tests use the
 exact Spotify scroll/rail IDs and five-row final topology. Focused Release
 coverage passes 24 widget-surface focus checks, 47 focus-navigation checks, and
 Spotify 32/32; packaged physical-controller evidence and a live retest remain
 open. The controller guide is
 density-aware and no-wrap, the widget viewport has a
 host-owned rounded clip, and size-changing widget swaps commit one synchronous
 complete repaint after a no-redraw move to avoid an intermediate black frame.
 These
changes remain in Verifying until packaged controller and screenshot evidence
is recorded in GBA-015 and GBA-016.

### Settings and global theme pipeline

The first-party Settings widget controls text/interface scale, backdrop
opacity, System/Full/Reduced motion, System/Standard/High contrast, bold text,
reduced transparency, exact theme ID/version selection, and confirmed reset.
`PlatformAppearanceService` watches settings and theme files
with event notifications plus a 200 ms debounce—there is no polling loop.
Valid reloads increment an immutable revision, clear per-widget layered-theme
caches, and emit a bridge appearance-change event. Invalid reloads keep the
prior snapshot and revision.

The bridge resolves platform → widget → user layers, with layer priority
stronger than selector specificity. It publishes globally layered widget
`base`/`focused` styles and a bounded shell appearance containing scale,
backdrop, motion, contrast, bold-text, transparency, and semantic shell styles
without launching widget workers.
The native client consumes the initial shell appearance and live revision
events, rejects stale revisions, retains its last good state on failure, and
applies supported shell styles and the complete bounded appearance record. The
host-owned policy runs after every shell/widget GBSS layer: text scale
remeasures/reflows without compounding inherited `em`, reduced motion removes
transitions, reduced transparency removes blur and makes node surfaces opaque,
bold text enforces minimum weight 600, and System/forced high contrast corrects
text/focus against inherited surfaces with a geometric focus ring. Windows
setting changes reapply System contrast and motion immediately.

The built-in default and first-party styles now form one minimalist warm-
graphite baseline with regular-weight hierarchy, fewer nested surfaces,
smaller radii, thin borders/tracks, compact controller targets, and one inset
neutral focus cue. Stable declarative nodes interpolate bounded opacity, scale,
and `translate-x`/`translate-y` targets through a host-owned timeline; first
observation snaps, rapid changes retarget from the presented value, reduced
motion cancels, removed widgets are forgotten, and settled content schedules
no further frames. Translation is true subtree presentation geometry: paint,
clipping, focus, hit testing, controller navigation, and focus-follow scrolling
share the translated boxes while static layout size does not change. The
transient pressed-state map is connected to exact physical actions. Shell/
widget open, close, and replacement transitions plus packaged visual/
accessibility evidence remain open. The embedded selectable
Cool Slate theme exercises the same token and renderer pipeline with a visibly
distinct palette rather than a hard-coded widget skin.

The CLI provides `gbar theme new|validate|preview|pack|inspect|install|list`.
Schema-version-2 `.gbartheme` packages are data-only, deterministic, bounded,
publisher-namespaced, digest-addressable, revalidated through the production
compiler, and installed as immutable ID/version directories through staged
atomic moves. Remote HTTPS/GitHub release installs require a pinned SHA-256.
Preview is computed terminal output, not native pixels; signing, revocation,
remove/update/rollback, asset support, and graphical preview remain open.

### Live package catalog

`BridgeCatalogMonitor` watches the packaged host catalog (trusted `widgets` plus
manifest-backed `bundledWidgets`) and the
current-user `catalog-state.json` and packages subtree. A capacity-one channel
coalesces file hints and debounces write bursts for 175 ms before a complete
bounded reload. Staging, cross-process lock, and atomic temporary files are
ignored. Semantic changes advance a bridge-lifetime revision; invalid trusted
shell state retains the complete last-good catalog/revision. Invalid installed
state or package integrity instead publishes a trusted-only revision,
synchronously removes Community registrations, and retires their workers.
Invalid individual styles or unsupported capabilities are isolated with
bounded diagnostics. Reload/list never starts a worker.
Starting the monitor schedules a complete catch-up reload after both watchers
are active, closing the initial load-to-watch race.

Catalog-state schema 2 adds an optional exact active-version pin while retaining
schema-1 reads. Without a pin, discovery selects the greatest installed
`System.Version`; the next successful mutation migrates legacy state to schema
2. `gbar version list|select|rollback` exposes immutable installed versions.
Selection and rollback require a disabled widget, keep it disabled for review,
and never rewrite package bytes. A missing pinned directory fails discovery
closed with `active_version_missing` rather than silently executing another
version.

`gbar uninstall <widget-id>` is implemented as a disabled-only catalog
operation. It removes the exact ID's state, atomically retires its complete
package directory from discovery, deletes all immutable versions, and
reindexes remaining order. Locked retired files are reported honestly and a
later package mutation retries bounded staging cleanup. It intentionally does
not guess or enumerate
provider-owned private-secret slots; cleanup of known slots remains a widget
host-service responsibility.

Quota failure no longer removes the cleanup control plane. `WidgetCatalog`
publishes a separately bounded health projection from canonical ID/version
directory names plus validated state, without parsing candidate manifests or
loading code. It identifies ID, per-widget-version, and total-version quota
breaches and marks the selected generation as protected, including when it is
enabled. Settings presents only inactive, non-selected candidates behind an
exact confirmation page;
`gbar repair list|remove` provides the same workflow. Exact-version removal
runs under the catalog operation lock, rejects reparse/path ambiguity, checks
cancellation before the atomic staging move, and never exposes a force or
caller-selected recursive deletion path. Normal discovery is re-run after
repair and remains the only publication/launch authority.
The bounded Release regression projects the maximum supported 512-version
repair view in 326.382 ms with 1,711,440 managed bytes allocated on the current
development machine (5 s and 32 MiB test budgets).

Presentation/order-only changes preserve compatible workers while atomically
swapping their validated presentation/quick-action metadata. A package,
publisher, instance, executable, argument, declared-capability, or memory-policy
change retires the prior client; stale events are ignored and the next use
starts/authenticates the replacement lazily. The native host coalesces catalog
events, holds a revision in-flight until an atomic descriptor list parses and
reconciles, and retries a genuine in-flight change event at bounded 250, 500,
and 1000 ms delays. Hiding the overlay cancels and abandons that retry sequence;
the same revision may be announced again, and a later open/list catches up.
Ordinary open-time failures do not create polling. A new bridge session resets
tracking. Reconciliation invalidates affected snapshot/style caches, clears
every runtime-changed widget's focus/lifecycle independently of cache
residency, and safely returns from a disabled/removed active widget.

### Worker lifecycle

The implemented host-authoritative lifecycle separates presentation from
residency:

- **Created:** runtime initialization and the one-time author hook.
- **Background:** resident by default but not selected/open. Visible-lifetime
  work is canceled; explicitly permitted widget-lifetime background work may
  continue.
- **Visible:** the dashboard card is selected and can receive declared quick
  actions.
- **Interactive:** the widget is open and receives its scoped controller
  actions.
- **Destroying:** terminal bounded cleanup after widget-owned tokens are
  canceled.

The managed SDK exposes widget-lifetime, per-state, and shared
Visible/Interactive lifetime tokens, plus creation, stable-state-change, and
destroying hooks. The native host publishes `Visible` for the selected bridge
card, `Interactive` for its open surface, and `Background` when hidden or
switched. Authors cannot request their own lifecycle transitions.

Manifest `residencyPolicy` schema 1 is enforced generically for trusted and
installed workers. `keep-alive` is the default; `suspend-when-hidden` keeps the
process but suppresses hidden presentation/interaction and uses cooperative
`Background` cancellation; `unload-after-idle` requires 5–86,400 seconds,
caches the last validated snapshot, sends bounded `Destroying`, releases the
process/companion, and lazily resumes on visibility. Pending unload cancels on
visibility or work, and intentional unload does not consume crash budget.
Legacy `backgroundPolicy: none|suspend` resolves deterministically to
keep-alive/suspend-when-hidden; declaring both vocabularies fails validation.
No policy suspends Windows threads. The capability broker continues to deny all
new ordinary manifest-declared operations/subscriptions while Background. One
narrow OAuth exception allows only an already-started
`external.spotify.authorization.v1` `connect` request to retain its lease when
an explicit Interactive Connect action opens the browser and moves the widget
through Visible/Background. The action acknowledges immediately; its
authorization task uses the widget's Created-to-Destroying lifetime rather than
the input-action or active-surface token. It does not authorize a new inactive
request or a disconnect; Destroying, consent revocation, pipe/caller
cancellation, and the provider's bounded timeout still terminate it. The
callback listener exists only for that explicit attempt. The host-granted
bounded private-state read/write/clear service
remains available
there for persistence and is denied during Destroying.
Separately, trusted bridge policy now bounds each Windows worker with a Job
Object memory ceiling and one-process limit regardless of lifecycle. Crash,
hang, shutdown, and user-requested termination remain separate safety/
administrative paths.

### Current visible resource sample

One live visible-overlay sample measured `OverlayHost` at 93.2 MB,
`WidgetBridge` at 59.1 MB, and one widget worker at 51.5 MB: **203.8 MB private
memory** in total. Across a five-second CPU sample, `OverlayHost` accumulated
**78.12 ms**; bridge and worker deltas were below the measurement timer's
resolution. This is a single prototype observation, not a steady-state budget
pass or a claim about hidden, GPU, wakeup, or multi-widget cost.

`scripts\Measure-OverlayPerformance.ps1` provides a repeatable bounded Windows
process-tree observation for packaged hidden and optional independently
launched visible states. It writes versioned JSON/Markdown with machine/build
metadata, CPU-time deltas normalized by logical processors, working/private
memory, handles, threads, percentiles, readiness proxies, and non-gating target
comparisons. Its deterministic helpers run under `-SelfTest` in verification.
This CIM/performance-counter sampler is diagnostic only: it does not measure
GPU, wakeups, presented-frame or controller latency and does not replace the
planned ETW/PresentMon release harness.

### Test coverage

The repository verification script builds and runs managed suites for the SDK,
protocol, YT Music, first-party Settings, runtime, CLI, styling, platform
settings/themes, catalog, bridge, the generic worker host, broker, the real
first-party-package conformance path, and Windows providers/reference widgets.
The runtime covers suspended pre-containment
launch, memory/process/UI limits, kill-on-close, restart cleanup, and mandatory
community AppContainer authority, exact grant replacement, content-generation
isolation, trusted-runtime/content-root overlap refusal, caller-preserving
content-admission cancellation, timeouts and session release, bounded intentional
unload, and private two-clock dashboard-gesture propagation. Its focused Release
harness passes 44/44;
the isolation probe verifies distinct stable SIDs, Low integrity, zero
capability SIDs, allowed package reads, denied package writes/host and other-
profile reads/network, stripped secrets, private-profile write/isolation, and
bounded cleanup. The current SDK, SDK Gallery, and YT Music focused suites pass
84/84, 6/6, and 48/48 respectively, including navigation/resource contracts,
safe typed companion errors, serialization, and widget recovery for host-side
rejected-Bearer invalidation without a second widget delete. The current
Settings Release suite passes 41/41, including
scrollable identity and permission review,
disabled-only version selection/rollback, required/optional separation,
enablement-versus-consent copy, fail-closed catalog/compatibility behavior,
nested visual-accessibility controls, legacy appearance defaults, and no
polling. Settings passes 42/42; styling and platform settings/themes pass 23/23 and 15/15,
including Busy-state composition and legacy schema-1 theme compatibility. CLI
passes 51/51, including unrelated-directory scaffold failure-before-write and
an explicit-SDK Release build, bounded data-only snapshot rendering, fail-closed DLL
and scenario execution, bounded scenario-manifest discovery,
authenticated candidate/active development readiness,
last-good retention/restart, complete bounded source/package watching, cleanup
failure reporting, version
list/selection/rollback, exact-stream local/remote update policy, theme
scaffold, production validation/computed preview, deterministic
packaging/inspection, pinned-GitHub installation, per-package and aggregate
catalog limits, data-only exact-version recovery, immutable versions, rejection of a 1,025-directory package
before pack output or installed-byte publication, and adversarial package cases. Catalog
passes 34/34, including
schema-1 state migration, exact active-version pins and enabled-history repair,
linearizable concurrent rollback/first-install operations, public-API
disabled-update enforcement, lock-free reads during atomic state replacement,
pin-preserving reorder, shared host-API/architecture evaluation, and exact-
content-tree sealing/tamper rejection, consumed-byte manifest/integrity limits,
misreported and changing-length rejection, verified manifest-byte capture,
session byte pinning, pre-start mutation and late-insertion refusal,
prospective ID/version/file/byte refusal, per-entry/per-64-KiB cancellation and
deadline checks, pre-extraction refusal when implicit file paths would require
more than 1,024 exact authority directories, unexpected-entry rejection, plus
content-bound unsigned authority.
The maximum-inventory fixtures retain 512 verified files: current local Release
evidence acquires and hashes the pinned-file lease in 325.283 ms and applies the
exact AppContainer grants plus lazy activation in 360.426 ms. Clean eligible
run `20260809T152831Z-67b77c73` retains the seven affected Release steps for
commit `6bd60d3` (implementation baseline `6fc9e01`). Focused tests use
separate five-second content-acquisition and ten-second maximum-grant ceilings;
one aggregate production start deadline plus repeated clean/hosted measurements
remain open.
The exact accepted namespace edge is also exercised through the public
pack/install/enable path and a real first-party AppContainer worker: 1,024
authority directories and 258 verified files pack in 376.140 ms and reach a
first validated render in 2,528.883 ms on the current machine. Clean retained
selected run `20260809T155221Z-ae6e5d8d` passed CLI 50/50, Documentation 1/1,
and First-Party Conformance 6/6 for documentation commit `b2956ab` over
implementation `4f903b0`. This is a focused one-machine measurement, not a
production startup budget or a complete all-manifest verification run.
Bridge
passes 45/45, including bounded 16-request correlated dispatch, stable
`bridge_busy` saturation, cooperative stalled-admission list/Stop
responsiveness and cleanup, duplicate-active-ID refusal, per-widget receive
ordering, aggregate count/declared-memory admission, exact crash/
timeout/idle-unload release, Settings control-plane access, race-safe refusal,
semantic catalog revisions/last-good/catch-up reload,
atomic presentation metadata replacement, compatible-worker reconciliation,
trusted Job-only exceptions, manifest-backed bundled packages, mandatory
installed-package isolation metadata, lifecycle residency, and exact dashboard
gesture derivation, including trusted-only publication and worker retirement
when installed state/integrity fails, pre-launch content-race rejection, and
live-byte pinning until asynchronous worker teardown completes. Clean retained
selected run `20260809T161934Z-5d0bee6a` passes Bridge 45/45, Documentation 1/1,
and First-Party Conformance 6/6 in 75.643 seconds for commit `fdcf253`; its
provenance records a clean tree, release eligibility, and zero stderr or output
truncation. The dispatcher does not hard-bound cancellation-ignoring Windows ACL
calls or its final drain, and the shipping native client cannot pipeline while
its synchronous read blocks the UI; those are still release-blocking startup/
availability work.

Clean release-eligible all-lane run `20260809T171327Z-7e90ff88` passes 41/41
steps in 323.958 seconds for action-admission documentation commit `689a933`
over implementation commits `6c5f932`, `7d33ce1`, and `7d92dcd`; it records a
clean source tree, zero stderr, and no output truncation. The subsequent native
per-widget failure-feedback slice now passes its 305-check deterministic target,
including the host pump/surface/timer/catalog/hide-stop composition seam, and the
Release OverlayHost target links successfully. The prior canonical Release
native build/test script also covers the full host and existing state-machine
suites. All-lane run `20260809T225934Z-a39ac018` passes 41/41 steps in
350.377 seconds, including the 305-check target, full native build, hidden-host
smoke test, and documentation contract. Its provenance records dirty base commit
`023ea46` and `releaseEvidenceEligible: false` because this milestone and the two
reviewer-owned ledgers were uncommitted; it is integration evidence, not a clean
immutable release bundle. First-Party Conformance 6/6 now also
runs Spotify's real installed and bundled package through the generic
AppContainer worker, reaches a broker-backed ready snapshot without credentials,
admits `spotify.next` through the production action queue, and observes the
typed simulated broker command. The existing YT Music acceptance route already
proves direct and controller-resolved playback commands through its installed
generic worker; live accounts and browser authorization remain separate gates.

PlatformBroker focused coverage passes
48/48 and includes closed isolated-
client SID/
pipe scopes, nonce/full-identity authentication, bounded requests/events,
consent/lifecycle gates, revocation, and cancellation of already in-flight
provider work when lifecycle or consent changes, plus dormant-reservation and
exact-operation single-use broker-lease expiry/replay/revocation checks and
dependent-lease 401 Bearer invalidation. An
actual AppContainer-to-broker
request integration also passes with the exact SID, Low-label global endpoint,
expected PID, nonce, and widget identity checks in force.
 The generic worker-host suite passes 9/9, including that real typed broker
 request from an AppContainer worker. Audio provider and Audio Mixer pass 15/15
 and 25/25; Network provider and Network Controls pass 31/31 and 17/17.
 Bluetooth provider passes 11/11; the constrained Windows Community provider
 passes 10/10, including restart/authority isolation, CAS, a real two-process
 race, quota/corruption/reparse, rate, and cancellation. Games & Apps and its
 Windows app-library provider retain focused suites covering the in-memory
 Library/Catalog flow and exact shortcut launch revalidation.
 The retained Recent Apps and Windows activity reference suites pass 8/8 and
 10/10; Windows Media provider and Now Playing pass 11/11 and 17/17. Games &
 Apps passes 26/26, including its vertical full-tile AppTile Library/Catalog
 focus model and lifecycle-bound Toast feedback.
 The first-party conformance suite passes 6/6 by building and installing the
actual Audio Mixer, Network Controls, Games & Apps, Now Playing, and YT Music
packages, launching each with the generic host in its package AppContainer, and
observing safe brokered reads/actions through simulated platform providers.
It additionally packs, installs, enables, admits, and renders a real first-party
package at the exact 1,024-directory authority limit.
 The YT case additionally exercises public validate/pack/install/enable,
 pairing, host-side private-secret persistence and Bearer injection, dashboard
 transport, lifecycle enforcement, and the absence of a trusted fallback. Pre-
 resume native fault injection remains an explicit release-test gap.
Network Controls and its Windows provider retain their focused 17/17 and 31/31
coverage for controller/focus, lifecycle/no-poll subscription ordering,
privacy/explicit state, optimistic command reconciliation, opaque identity,
native churn, cancellation, bounded failure, owner-thread disposal, responsive
GBSS, and privacy-safe real Windows read smoke.

Clean all-lane run `20260809T141527Z-8946c731` is the current authoritative
local `scripts/Verify.ps1 -Configuration Release` evidence. It passed all 41
manifest steps and 755 JUnit-adapted cases with zero failures, errors, skips,
stderr output, or stream truncation in 313.016 seconds against exact clean
commit `dc6b092`; `releaseEvidenceEligible` is true. The retained bundle records
stdout/stderr and JUnit per step, the manifest digest, 29 Community package
digests, and exact selected native-toolchain versions and hashes. The runner
includes the previously omitted WidgetTicker suite and enforces per-step
process-tree plus shared overall deadlines. This is clean local evidence, not a
referenced immutable hosted workflow artifact; hosted execution remains open.
Focused native Release suites report Declarative Layout 245 checks, Native Icons 206,
Declarative Motion 39, Overlay Targeting 42, Controller Navigation 81, Pressed
Interaction 29, Slider Interaction 2,082, Focus Navigation 41, Widget Surface
Focus 20, and Declarative Renderer 4,632; the remaining native suites also
pass. Managed feature-negotiation regressions assert the highest feature
version required by the complete tree—such as Grid v8—rather than incorrectly
pinning an inline-PNG tree to its older v6 minimum. Display-sensitive evidence
also includes 108,545 placement/render-metric/surface-geometry checks and
deterministic tiny/portrait/negative-coordinate/wide/4K and 72–480-DPI math
plus 150% font-size/letter-spacing adaptation. The platform
diagnostics suite passes 8/8 and the catalog suite passes 21/21. The packaged
hidden startup smoke remained resident for its 1.2-second observation. Physical
mixed-monitor migration/hot-plug screenshots and the broader 150% visual matrix
remain evidence gaps.

Run managed verification:

```powershell
.\scripts\Verify.ps1 -Configuration Release -Lane managed
```

Run the complete Windows verification with Visual Studio Desktop development
with C++ installed:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

## Honest limitations

- The generic native renderer handles the current declarative node kinds and
  renders YT Music, Settings, Audio Mixer, Network Controls, Games & Apps, Now
  Playing, and installed widgets through catalog descriptors. Responsive
  viewport/containment math is
  covered broadly; physical mixed-DPI, localization, accessibility, and visual
  regression evidence is still incomplete.
- Spotify now has a typed v1 broker contract, a composed trusted provider,
  and a controller-first Community addon core for public Client-ID configuration, PKCE with the exact
  `http://127.0.0.1:43827/callback/`, Windows credential-vault refresh tokens,
  player snapshots/controls, restrictions, playback events, bounded
  `Retry-After`, and sanitized errors. The local `gbar config` workflow is
  implemented and tested. The provider is composed by `WidgetBridge`; the
  addon is packaged locally through the public SDK/AppContainer path and shows
  setup guidance without opening OAuth automatically. Community package 0.1.7
  uses a compact responsive layout, puts the complete setup instructions in a
  controller VerticalScroll, and uses shared centered icon/label button
  placement rather than widget-specific offsets. Setup now shows the
  source-tree-runnable `dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config ...`
  command; every explicit setup entry receives a fresh Scroll identity so a
  restored bottom offset cannot hide the heading, and **Check configuration** performs one bounded fresh configuration and
  authorization read so a newly saved Client ID takes effect without restarting
  the worker, while B remains navigation-only. An Interactive Connect that has
  already opened the system browser acknowledges the action immediately and
  retains only that exact broker request through Visible/Background using the
  widget's Created-to-Destroying lifetime. The listener waits at most fifteen
  minutes; the broker's exact Connect deadline is seventeen minutes so bounded
  token exchange, retry/backoff, and credential-vault persistence have the
  remaining two minutes. No listener exists before an explicit Connect. New
  Background controls remain denied, while revoke, Destroying, and cancellation
  still terminate the attempt. Its explicit `keep-alive` residency prevents
  idle unload from destroying this already-started authorization while the
  browser owns foreground; active polling and ordinary presentation work still
  stop outside their lifecycle. The loopback receiver tolerates at most 16
  malformed/early-close local probes inside the same fifteen-minute listener,
  while requiring loopback origin, exact host, GET/HTTP/1.1, callback path,
  and OAuth state before accepting the real callback. There is no live
  allowlisted-account evidence. Devices/queue/search/recent/library/
  playlists/albums/artists remain staged work. A separately trusted singleton
  Web Playback SDK/WebView2 host now implements a bounded, hardened process and
  protocol with offline tests. Provider orchestration, PID-reuse-safe parent
  ownership, incremental `streaming` consent, Premium/EME/autoplay live proof,
  and any applicable Spotify streaming approval remain. See [Spotify
  integration status](spotify-integration.md).
- The code clears unused native client pixels to the layered color key,
  separates Now Playing snapshot reads from live-subscription failures, loads
  the Games catalog only after Add, and preserves measured intrinsic height for
  wrapped text/buttons. The packaged Release captured after milestone
  `8e8c90a` shows the Now Playing surface over the dimmed application without
  the former opaque client rectangle and with one live GSMTC session. That is
  useful standard-viewport evidence, not proof of Retry recovery or the full
  compact/wide, DPI, contrast, transparency, and long-copy matrix. GBA-036
  through GBA-039 therefore remain Verifying.
- Local and bounded remote package/catalog commands plus CLI and Settings
  immutable-version selection/rollback are implemented. There is no
  graphical/file-picker installer, automatic release/update discovery, signed
  publisher workflow, version removal/garbage collection, or marketplace yet.
  Settings provides controller identity/version review and enable/disable after
  CLI installation. The
  bridge publishes accepted catalog changes live with last-good retention and
  safe worker reconciliation. Closed capability declarations are connected to
  the separate Settings consent flow.
- Mandatory capability-free AppContainer isolation is implemented for every
  installed/community worker. YT Music now uses that path with exact-loopback
  and write-only private-secret broker services; Settings is the only temporary
  trusted Job-only worker. Every installed worker start/restart reacquires and
  pins the complete verified path/length/hash inventory, and the AppContainer
  receives only exact non-inheriting file/traversal grants. Changed bytes and
  pre-admission insertions fail before launch; later insertions do not receive
  worker authority. Each verified content generation receives a distinct
  AppContainer identity, so a later version cannot inherit an older root grant.
  Publisher
  signing/revocation, CPU quotas, disk/profile quotas and cleanup, provider
  hardening, and the security audit/history UI are not production-ready. The
  narrow Core Audio and Windows network backends still need broader hardware/
  privacy/churn and hidden-state performance evidence. Isolation plus
  capability transport/consent does not establish a public publisher-trust
  boundary.
- Win32k system-call disable is not enabled for managed workers because the
  tested mitigation caused CoreCLR DLL initialization failure (`0xC0000142`).
  Job Object UI restrictions remain enabled.
- Closed audio/network/Bluetooth/recent-activity manifest permissions are
  enforced through the broker, as are the media-session read/control
  permissions and the narrowly bounded dashboard gesture exception for one
  exact control operation.
  Community AppContainers have zero OS capabilities/network authority and no
  general desktop token; direct resource access is limited to the generic
  runtime plus exact session-verified package files. OS capability APIs remain
  brokered.
- `gbar render` accepts only bounded data-only snapshots; DLL input and selected
  scenario execution fail closed. Executable integration uses the isolated
  `gbar dev` AppContainer path until a dedicated scenario worker exists.
- Universal controller suppression is unresolved for games using background
  Raw Input or direct HID access.
- The quarantined XInput Guide fallback depends on an undocumented system-DLL
  ordinal/bit and may vary by controller model, mode, firmware, and transport.
- True Fullscreen Exclusive, HDR, physical mixed-DPI/hot-plug coverage,
  anti-cheat compatibility, latency, and steady-state resource budgets need a
  larger measured matrix.
- Topmost/foreground reassertion is best effort. Secure desktop, elevated
  windows, exclusive render paths, and multi-monitor backdrop coverage are not
  supported contracts.
- The manifest protocol retains `keep-alive` as its compatibility default, but
  the controller scaffold and Clock sample now select five-minute idle unload.
  The bridge atomically caps application residency at eight workers and 512 MiB
  of declared Job memory by default, refuses overcommit without evicting pinned
  workers, and preserves a separately reported trusted Settings control-plane
  slot. Per-widget measured telemetry, user overrides, critical-work leases,
  packaged resource measurements, and long-duration churn remain open.
- Recent activity observation starts lazily on the first authorized read, then
  remains event-driven until the bridge/backend is disposed. Consent revocation
  blocks delivery and activation and cancels in-flight broker requests, but
  immediately stopping provider-side observation and clearing its bounded
  in-memory history on read-consent revoke is future hardening.
- GBSS compilation and bridge-global layering are implemented. `selected`,
  `disabled`, and `busy` snapshot state participates in the complete
  `base`/`focused` maps. The bridge also publishes a transient `pressed` map;
  the native host applies it only while the exact physical controller action
  remains down and reconciles it across snapshot/focus changes. Stable node
  opacity/scale/translation targets animate through bounded native transitions.
  Translation uses shared subtree paint/clip/focus/hit/navigation/Scroll
  geometry without changing layout. The shell now opens with a bounded 140 ms
  ease-out opacity track, closes with a 100 ms ease-in track, and reveals a new
  or replaced widget identity from subtle 0.78 opacity over 100 ms. Reversals
  retarget from the presented value, ordinary same-identity snapshots never
  flash, entering widget focus snaps content visible, reduced motion snaps and
  cancels, and settled/hidden state schedules no idle frames. Packaged visual/
  frame-time evidence and future dynamic semantic states remain open.
- Controller Settings, strict persistence, version-pinned theme selection,
  no-poll watching, last-good revisions, and globally layered widget styles are
  implemented. Safe theme scaffold/validate/computed-preview/pack/inspect/
  install/list tooling and native post-cascade contrast/bold/reduced-
  transparency policy are implemented. Native graphical preview, signing,
  removal/update/rollback, auto-scroll, screen-reader/magnification integration,
  and the physical combined 150% accessibility matrix are not. See [settings
  and global themes](settings-and-themes.md) and [theme packaging and
  distribution](theme-packaging.md).
- Audio Mixer is a bounded first-party public-SDK reference slice: its widget,
  package/catalog integration, and event-driven per-session Core Audio provider
  exist, while the controller-first **Audio Control** roadmap remains
  incomplete. It now includes explicit-capability default-microphone mute/level
  and sanitized default-device visibility, but supported output/default-role
  selection remains unresolved and microphone sample capture is out of scope.
  Network Controls is likewise a bounded
  reference slice with provider, worker, catalog, controller, and packaging
  evidence—not a completed **Network Control** product widget. Its roadmap adds
  privacy-gated identity/link details, bounded IP/gateway/DNS summaries,
  measured throughput/latency/loss diagnostics, and reviewed recovery actions.
  The implemented reference includes precise-location-gated explicit
  available-network scans and current saved/open result connections; it
  excludes password entry, profile creation, and automatic current SSID/signal
  access. Software Wi-Fi radio control plus separately capability-gated
  Bluetooth radio/discovery and explicit association pairing are implemented.
  Staged roadmap work adds a host-owned WPA Personal credential prompt and
  unpair/profile-specific Bluetooth operations. Enterprise Wi-Fi and generic Bluetooth
  Connect/Disconnect are not promised. See
  [widget capabilities](capabilities.md) and [Windows provider
  architecture](windows-provider-architecture.md) and [Network Controls
  reference](network-controls.md).

## Diagnostics

- Latest overlay initialization error:
  `%LOCALAPPDATA%\GameBarAlternative\startup-error.log`
- Overlay order/last-widget state:
  `%LOCALAPPDATA%\GameBarAlternative\overlay-state.ini`
- Input-probe logs: timestamped in the launch directory by default, or the
  explicit `--log` path.

See the [documentation index](README.md), [security and trust](security-and-trust.md),
and [troubleshooting](troubleshooting.md).

## Next vertical slices

1. Continue packaged GBA-036 through GBA-042 plus physical mixed-DPI/
   accessibility/visual-regression evidence, including long/error states, the
   150% text-scale matrix, controller focus/Scroll reachability, a denied-
   activation controller lease, Settings permission copy, and the uncaptured
   error/long/max-page states. The auth-free
   `final-schema-v2-20260808-final` bundle records retained package
   archives, AppContainer snapshots, computed styles, 12 standalone widget-
   body PNGs, traces, source/toolchain provenance, and an exact SHA-256 file
   inventory for the covered GBA-038/GBA-042 paths. It includes Spotify 0.1.6
   and the accepted setup/button layout. Settings generic-worker activation
   remains an explicit gap. Live Spotify authorization, callback behavior,
   shell/window composition, and hardware input are separate evidence gates,
   so GBA-038 and GBA-042 remain Verifying.
2. Complete the remaining real-companion and packaged physical-controller/
   shell proof for YT Music. The clean isolated auth-free workflow already
   proves install/consent/AppContainer launch, dashboard/open routing,
   lifecycle/crash/force-reload recovery, content-bound update/rollback, and
   disabled-only uninstall without a trusted fallback.
3. Complete hands-on packaged visual/accessibility/controller evidence for the
   public component milestone. The full Release gate is green. `SettingsRow`,
   bounded nested `ActionSheet`, Picker,
   Scrubber, Toast, rich tiles, protocol-v8 ResponsiveGrid, per-edge borders,
   and CodeText are implemented; Settings, Spotify, and Games & Apps provide
   production uses. After that gate, design advanced/virtualized collections,
   optional packaged-font brokering, and further motion polish without
   importing browser layout or arbitrary asset loading.
4. Extend Games & Apps beyond its bounded Start Menu, AppsFolder, and Steam
   sources with additional reviewed launcher adapters,
   running-program capture, and a host-owned file picker while preserving
   opaque exact launch identities.
5. Extend the bounded process sampler with ETW/PresentMon automation, stored
   comparable baselines, latency scenarios, and per-widget resource diagnostics.
6. Add native graphical theme preview, package remove/update discovery,
   editor schemas, controller/focus inspection, and scenario-based Gallery
   preview/capture coverage. The public SDK Gallery is implemented. Complete
   packaged author-workflow evidence for the implemented `gbar dev` loop.
7. Implement publisher signing/revocation, crash quarantine, CPU and disk/
   profile quotas/cleanup, and security audit UI before public community
   distribution; migrate trusted built-ins as their desktop dependencies become
   brokered.
8. Advance the controller-first Audio Control and Network Control roadmap from
   their bounded reference slices through hardware/privacy/performance gates,
   then continue non-auth Performance, richer media, recent games detection,
   and capture references.
9. In parallel when an allowlisted Spotify Development Mode account is
   available, prove the already-composed PKCE/Web API provider and packaged
   Player Community addon against live login/playback. Then add nested device,
   queue, search, recent, library, playlist, album, and artist surfaces. Keep
   the Web Playback SDK local-audio host as a separate later security and
   performance slice.
10. Run the documented controller/game/presentation/anti-cheat matrix.
