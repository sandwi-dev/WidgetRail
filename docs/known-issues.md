# Known issues and active bug ledger

This file is the durable source of truth for user-visible bugs found during
local product testing. Roadmap items describe planned capability; entries here
describe behavior that is already expected to work or a current interaction
that must be replaced. A bug is not closed until its acceptance evidence passes
in the packaged Release overlay and the closing commit is recorded.

## Status vocabulary

- **Open** — user-visible behavior is confirmed and acceptance work is not yet complete.
- **Confirmed** — reproduced by a user or deterministic/local evidence.
- **Investigating** — the owning layer is being traced; no root-cause claim yet.
- **Implementing** — a root cause and durable design are selected.
- **Verifying** — the fix is implemented and awaiting packaged/local evidence.
- **Closed** — acceptance evidence and closing commit are recorded.

## Active issues

| ID | Priority | Status | Owning layer | Summary |
| --- | --- | --- | --- | --- |
| GBA-001 | P0 | Verifying | Audio Mixer / broker / Windows audio provider | Per-application controls now target exact session IDs and the provider passes a reversible live-volume test; packaged row control still needs hands-on verification. |
| GBA-002 | P1 | Verifying | Widget protocol / host placement | Per-view compact/standard/wide/adaptive surfaces and host work-area clamping are implemented; YT Music now has a 480 x 340 compact media budget, while packaged visual verification remains. |
| GBA-003 | P1 | Verifying | Audio Mixer / declarative renderer | Accepted DLV-049 (`32af19b`, integrated as `a8bcb27`) locks the exact four-session Microphone-to-Master reverse edge to DLV-021's corrected geometry: one production HWND/UIA Up reaches Master and offset zero without cycling or `value_clamped`. Fresh live keyboard/controller confirmation remains. |
| GBA-004 | P1 | Split: cold-start fix Verifying; DLV-025 Assigned | OverlayHost UI thread / presentation / composition | Accepted DLV-078 `6d30f5e`, integrated through `a072d6f`, retains inert last-good pixels until the cold destination snapshot is admitted while revoking stale authority immediately. The separate real Games & Apps extent-animation gray/black-band defect is now assigned to the user-authorized Windows-10-compatible DirectComposition surface gate in DLV-025. |
| GBA-005 | P0 | Verifying | OverlayHost controller routing | Hierarchical B routing is implemented across nested widget views, root widgets, and the icon tray; packaged controller verification remains. |
| GBA-006 | P1 | Verifying | OverlayHost presentation | All direct snapshot refreshes compare prior/next surface extents; packaged resize verification remains. |
| GBA-007 | P1 | Verifying | Declarative renderer / focus navigation | Nested fixed-point reveal and clip-feasibility filtering are implemented; packaged controller verification remains. |
| GBA-008 | P2 | Verifying | Declarative renderer | Inactive LRU offset eviction and scroll-clipped deferred focus outlines are implemented. |
| GBA-009 | P1 | Verifying | Platform diagnostics transport | First-instance ownership and mutual kernel PID authentication are implemented. |
| GBA-010 | P2 | Verifying | Platform diagnostics transport | End-to-end request deadlines and bounded client timeout validation are implemented. |
| GBA-011 | P0 | Verifying | Widget SDK / host focus / Audio Mixer | Focus-safe disabled/busy semantics and per-session Audio reconciliation are implemented; packaged controller verification remains. |
| GBA-012 | P1 | Verifying | Declarative renderer / component styles | Effective surface/ancestor focus clipping and non-scaling full-width defaults are implemented; packaged visual verification remains. |
| GBA-013 | P1 | Verifying | Network Controls / widget SDK | Separate Wi-Fi and Bluetooth controller views, LB/RB tab switching, explicit scan, and focused-row routing are implemented; packaged visual/controller verification remains. |
| GBA-014 | P0 | Verifying | YT Music / Widget SDK routing / native icons | Window-wide transport shortcuts and Previous/Next glyph orientation are corrected; packaged controller/visual verification remains. |
| GBA-015 | P1 | Verifying | Declarative renderer / built-in widget themes | Fixed regions no longer shrink into clipping, Sliders use a thin native track inside their controller target, and the built-in surfaces use a lighter visual hierarchy; packaged visual verification remains. |
| GBA-016 | P0 | Verifying | OverlayHost focus / lifecycle / controller routing | The selected widget panel remains visible while the tray owns focus; root-boundary return now also recovers responsive focusless roots without trapping input. Packaged controller evidence remains. |
| GBA-017 | P0 | Verifying | Declarative renderer / scroll focus | Focus-follow now snaps the first/last focusable descendant to the true scroll extent; packaged controller verification remains. |
| GBA-018 | P1 | Verifying | OverlayHost controller routing | Pressed D-pad/left-stick Up from the tray now enters the visible widget without dispatching a widget action. |
| GBA-019 | P1 | Verifying | OverlayHost controller guide / layout | The guide is density-aware, contextual, bounded, and no-wrap; compact/high-scale visual evidence remains. |
| GBA-020 | P1 | Verifying | OverlayHost panel clipping / renderer | A cached host-owned rounded viewport clip now masks opaque widget roots; packaged visual evidence remains. |
| GBA-021 | P1 | Verifying | OverlayHost targeting / DPI | Visible DPI, topology, taskbar/work-area, and appearance changes now share one coalesced dynamic refresh policy; physical mixed-monitor/hot-plug evidence remains. |
| GBA-022 | P1 | Verifying | Settings permissions / controller navigation | Hidden package/capability pagination and redundant Back rows were replaced with bounded controller Scroll scopes and B-only Back; packaged controller/high-scale evidence remains. |
| GBA-023 | P1 | Verifying | Widget protocol / bridge / runtime | Versioned keep-alive, cooperative suspend, and bounded idle-unload residency are enforced with legacy migration, cached views, and lazy resume; packaged resource/churn evidence remains. |
| GBA-024 | P1 | Verifying | Gbar CLI / OverlayHost / WidgetBridge | Authenticated candidate-worker readiness, last-good recovery, complete bounded watching, and observable cleanup are implemented; packaged author-workflow evidence remains. |
| GBA-025 | P0 | Verifying | Controller quick actions / capability broker | A dormant non-authorizing host reservation now activates one exact-operation broker lease only at the typed call; packaged controller/media evidence remains. |
| GBA-026 | P1 | Verifying | Reference widgets / package isolation | Four built-in brokered references plus the YT Music Community addon pass real-package generic-AppContainer conformance; packaged overlay evidence remains. |
| GBA-027 | P0 | Verifying | Games & Apps / app-library broker / provider | Games & Apps replaced bundled Recent Apps with bounded Start Menu plus AppsFolder catalog reads and exact source-revalidated opaque-ID launch; packaged controller/visual verification remains. |
| GBA-028 | P0 | Verifying | PlatformBroker consent migration / Settings permissions | The exact retired Recent Apps activation capability is tombstoned; unsupported/inactive details moved to a safe bounded read-only Review page, and packaged visual verification remains. |
| GBA-029 | P1 | Verifying | Settings installed-widget inventory | Installed Widgets now separates read-only Built-in widgets from manageable Community packages instead of omitting bundled first-party widgets; packaged visual/controller verification remains. |
| GBA-030 | P0 | Verifying | YT Music packaging / community isolation / local companion broker | YT Music now uses the public Community package/AppContainer/loopback/secret path without a trusted fallback; clean packaged controller and lifecycle evidence remains. |
| GBA-031 | P1 | Verifying | Widget SDK components / built-in themes / native renderer | The shared default, responsive Row/Grid, Picker, Scrubber, Toast, CodeText, per-edge borders, and protocol-v7 ActionSurface/MediaTile/AppTile exist; Settings and Games & Apps provide production adoption, and the full Release gate is green while hands-on packaged scale/accessibility evidence remains. |
| GBA-032 | P1 | Verifying | GBSS / native renderer / accessibility | Stable declarative nodes interpolate bounded opacity/scale/translation; shell open/close and widget identity reveals use bounded 140/100 ms tracks, and a three-state native-counter smoke observed no settled post-warmup Direct2D frames. PresentMon/packaged visual evidence remains. |
| GBA-033 | P1 | Verifying | Games & Apps / catalog / host launch completion | Durable curation, Start Menu/AppsFolder plus bounded Steam discovery, evidence-backed Steam Game classification, exact revalidated launch, and close-after-correlated-success are implemented; additional launchers and packaged controller evidence remain. |
| GBA-034 | P1 | Verifying | Network Controls / controller state model | Focus/selection is separated from authoritative Wi-Fi/Bluetooth state; pair/manage actions and stable focus/scroll behavior have focused coverage, with packaged churn/hardware verification remaining. |
| GBA-035 | P0 | Verifying | Audio Mixer / capability degradation / focus | Optional device-name and microphone providers now degrade and recover independently without replacing healthy master/session controls; packaged partial-grant verification remains. |
| GBA-036 | P1 | Verifying | OverlayHost / native composition / declarative surface | The native client clears unused pixels to the layered color key and one packaged standard-viewport capture shows no opaque canvas; the broader paint/scale/contrast matrix remains. |
| GBA-037 | P0 | Verifying | Now Playing / media provider / retry | Current-state reads are independent from live subscription failure and Retry creates a fresh generation; packaged provider-failure recovery remains to verify visually. |
| GBA-038 | P1 | Verifying | Games & Apps / catalog loading / responsive text | DLV-004 adds shared loading/empty/failure surfaces, bounded Previous/Next Catalog pages, long/max-library coverage, exact installed conformance, and a retained multi-profile capture matrix; physical packaged shell/controller review remains. |
| GBA-039 | P1 | Verifying | Settings permissions / responsive text / Scroll | Auto-height intrinsic leaves now retain measured wrapped height and long permission-copy scroll extent has native regression coverage; packaged visual verification remains. |
| GBA-040 | P0 | Verifying | Native declarative layout / Spotify / responsive text | Intrinsic leaves now measure height against their authored width/max-width before layout, with exact Spotify state/setup regressions at compact and 150% text scales; packaged visual verification remains. |
| GBA-041 | P0 | Verifying | OverlayHost / controller input ownership | A visibility-scoped GameInput lease keeps navigation alive when foreground activation is denied and uses exclusivity when confirmed; packaged backend/game evidence remains. |
| GBA-042 | P0 | Verifying | Spotify configuration / Settings permissions | Unsigned packages can resolve one unambiguous owning-publisher public configuration document; `final-schema-v2-20260808-final` proves Spotify 0.1.6 unconfigured/setup standalone widget-body rendering, but Settings package-path injection and live configure/connect/revoke evidence remain. |
| GBA-043 | P0 | Verifying | Spotify OAuth / broker lifecycle / pipe timeout | The explicit Connect task remains resident through browser-triggered Visible/Background, tolerates bounded local probes, and now has a fifteen-minute callback plus seventeen-minute exact broker deadline; live packaged authorization evidence remains. |
| GBA-044 | P1 | Verifying | OverlayHost / XInput Guide compatibility / performance | The ordinal-100 timer is now dormant unless GameInput reports an Xbox 360-family device; exact multi-device policy and callback cleanup pass native tests, while legacy-controller short-tap and refreshed hidden-performance evidence remain. |
| GBA-045 | P0 | Verifying | Spotify widget / Widget SDK / protocol v11 | The playlist bridge overflow is fixed through `WidgetPagedResource<TItem>` plus automatic focus-edge pagination: playlist and track lists render one 12-row window, use a six-page/72-item LRU, accept filtered sparse pages, and expose no Load-more row. The host now resolves pagination before focus can leave a boundary Scroll, and changed replacement-page focus outranks stale ordinal memory; focused resource/widget/native tests cover forward, reverse, eviction, stale completion, retry, sparse data, and focus, while packaged controller evidence remains. |
| GBA-046 | P0 | Verifying | Spotify Playback Host / WebView2 deployment | Playback-host bootstrap now publishes the x64 WebView2 loader, serves a host-intercepted synthetic HTTPS page, registers Spotify's ready callback before loading the SDK, and reports script-load failure explicitly; the packaged hidden-host smoke reaches `sdk_loaded`, while live Premium transfer/EME/autoplay evidence remains. |
| GBA-047 | P1 | Verifying | Widget SDK operations/resources / Spotify Community addon | Public bounded SingleFlight, Latest, and Serial lanes bind explicitly to Active, State, or Widget lifetimes and drain before lifecycle callbacks; `Completed` records synchronous no-work success and busy edges still auto-invalidate. Spotify 0.2.10 migrated playlist paging to SDK-owned Active resources, including synchronous cache/reset invalidation, removing its page tasks, generations, dictionaries, eviction loops, and manual page-state invalidation; packaged lifecycle/controller evidence and broader widget migrations remain. |
| GBA-048 | P1 | Verifying | Widget SDK state coordination / authoring experience | Public `WidgetModel<TState>` supplies serialized immutable updates and equality-based one-shot invalidation; Media Sessions now provides the medium production migration and repeat-suppression regression. Packaged evidence remains. |
| GBA-049 | P1 | Verifying | YT Music / loopback performance / semantic icons | Stable playback now reads state first and reuses complete same-track metadata for five bounded minutes, halving ordinary loopback traffic and avoiding cross-transition pairing; protocol-v12 `RepeatOne` supplies distinct non-color feedback. Packaged companion/controller evidence remains. |
| GBA-050 | P0 | Verifying | YT Music / asynchronous playback reconciliation | DLV-009 moves bounded transport reconciliation into an SDK Active Latest lane, rejects cancellation-ignoring stale success/failure/authorization outcomes, and lets repeated toggles supersede earlier refresh work; packaged real-companion evidence remains. |
| GBA-051 | P1 | Verifying | Widget SDK optimistic command coordination / Media Sessions | Public SingleFlight/Latest/Serial optimistic commands now derive projection and provider input from one model revision, preserve provider events through authored merge/rollback, own bounded lifecycle work, and never retry mutations; Media Sessions is the production migration. Packaged evidence and broader migrations remain. |
| GBA-052 | P0 | Verifying | Games & Apps / worker lifecycle | Initial and retry library loads now use runtime-owned Active operations, while toast expiry has one cancellation-source disposer and clears the shared reference before disposal. Focused coverage and three consecutive generic-worker/AppContainer conformance runs pass; packaged churn evidence remains. |
| GBA-053 | P1 | Verifying | Widget SDK resource coordination | Public `WidgetResource<TValue>` now owns bounded non-paged load/cache/retry/last-good/subscription state with lifecycle cancellation and stale-result rejection; broader production migrations and packaged evidence remain. |
| GBA-054 | P0 | Verifying | Widget SDK navigation / controller routing / SDK Gallery | Public bounded navigation, validated hierarchical IDs, exact active-scope action propagation, route cancellation, and remembered return focus are implemented and exercised by SDK Gallery; broader migrations and packaged controller evidence remain. |
| GBA-055 | P0 | Verifying | YT Music Community addon / loopback error safety | Typed status-only service failures and bounded safe UI copy remain intact through DLV-009's immutable presentation/current-attempt migration; focused YT Music coverage passes 51/51 and real-companion failure evidence remains. |
| GBA-056 | P1 | Verifying | Spotify widget focus composition | Accepted DLV-051 (`dc22202`, integrated as `822d29c`) authors the inactive seek Slider's Left edge to the selected wide rail destination or compact Player tab and passes exact semantic/controller replay. Fresh live confirmation remains. |
| GBA-057 | P0 | Verifying | Widget SDK cursor resources / Spotify focus | DLV-022/053 are accepted and integrated through `8c1bbdf`; accepted DLV-055 `efffa53` installs/selects/enables the latest source as `0.2.12`, and the coherent Release is visibly running for live traversal. |
| GBA-058 | P1 | Verifying | SectionHeader / native text geometry / Spotify | Accepted DLV-021 (`b714efe`, integrated by `bc2de86`) unifies DirectWrite measurement/paint and final-width row remeasurement; exact Spotify header bounds pass across compact/standard/wide-150/accessibility profiles. Fresh packaged Spotify verification remains. |
| GBA-059 | P1 | Verifying on packaged main | App-library provider / artwork / Games & Apps / Game Launcher / native bridge/cache | The corrected DLV-094/096/098/099 prefix is accepted and integrated through `c6d76a3`: Steam artwork is demand-only, bounded, stale-safe, and locator promotion/retirement is coupled to the winning source generation. The fully packaged Release is visibly running for the user's live library check. |
| GBA-060 | P1 | Verifying | Native renderer / shared component geometry | Accepted DLV-021 gives Button, ActionSurface, and SectionHeader one measured/painted geometry path and passes exact Games, Spotify, Now Playing, Settings, and SDK Gallery component profiles. Fresh packaged visual confirmation remains. |
| GBA-061 | P0 | Verifying | Spotify lifecycle / provider failure policy | DLV-023 (`3cfdd27`, integrated by `4dc1bd5`) retains the last-good Ready presentation for typed transient refresh/poll faults with bounded backoff, safe warnings, shared manual recovery, and Active-generation rejection. Live Spotify recurrence testing remains. |
| GBA-062 | P1 | Verifying | Audio Mixer / dashboard gesture authority | DLV-019 is accepted as `6afd60b`: LB/RB adjust master volume by five percentage points and X toggles mute through exact snapshot-bound authority. Physical-controller verification remains. |
| GBA-063 | P0 | Verifying | Games & Apps private state / cold start | DLV-017 adds a bounded display-only warm projection, revokes cached AppIds across Active lifetimes/failures, resets incompatible pre-release state atomically, and passes focused SDK 84/84, worker 9/9, and Games 49/49; packaged cold-start timing and physical display/controller proof remain. |
| GBA-064 | P0 | Verifying | Spotify list/header focus / native navigation | DLV-053 `7f5c2fd` is integrated through `8c1bbdf` and removes the singleton row's Down self edge while retaining Play Down to row and row Up to Play. Accepted DLV-055 runs that source as selected `0.2.12`; live closure remains. |
| GBA-065 | P0 | Verifying | Games & Apps mutation / private-state projection | Accepted DLV-024 `d80d9ec`, integrated through `6f401ea`, makes removal commit-before-publish and preserves unrelated rows through Back, restart, provider failure, and CAS conflict. Fresh packaged user confirmation remains. |
| GBA-066 | P1 | Verifying | Games & Apps presentation / surface hints | Accepted DLV-024 `d80d9ec`, integrated through `6f401ea`, keeps one stable Add applications action over last-good Library content and raises preferred height to 600 DIP. Fresh packaged user confirmation remains; native switching is separate. |
| GBA-067 | P1 | Closed | WidgetBridge frame read/write ownership | DLV-045 (`67df1d9`, integrated by `dfbe02d`) deterministically reproduces the decimal JSON-body signature as an abandoned timed-out test read consuming the Stop header, makes test reads terminal and exactly drained on timeout, and independently closes ordinary reply partial-write exposure through one complete-or-abort reply/event frame owner. Two retained focused runs pass WidgetBridge 66/66; the integrated Release package rebuilt and launched successfully. |
| GBA-068 | P0 | Live-confirmed fixed | Community package deployment / WidgetRuntime / WidgetBridge / OverlayHost lifecycle | Accepted DLV-057 `90cadf4` makes warm main, repeated main, detached-root, installed content, and planner post-integration main refresh identical for selected/enabled Spotify `0.2.14`; YT Music remains current at `0.2.7`. The user confirmed the supplied worker-start screenshots no longer reproduce in the current fully packaged Release; production PID 23000 admitted current snapshots from both widgets. Keep the focused regression coverage, but do not reopen this work without a new live recurrence. |
| GBA-069 | P1 | Closed | OverlayHost process ownership / local activation | Accepted DLV-070 `c61a49d`, integrated through `0b21384`, elects one per-user/profile owner before platform initialization and forwards later Show requests over an authenticated bounded local channel. Two exact visible `--show` invocations retained production PID 27520; client PID 3236 exited after one authenticated resident activation. |
| GBA-070 | P2 | Closed | YT Music package metadata / companion handshake | Accepted DLV-086 aligns the source constant, companion `appVersion`, README commands, and validating test with immutable manifest `0.2.7`. YT Music passes 55/55; no package bytes were republished. |
| GBA-071 | P1 | Closed | Game Launcher Hidden route / cursor-resource readiness | Accepted DLV-088 plus DLV-090 make Restore/Back publish an enabled current row without Refresh. Final direct evidence is 43/43 and clean installed generic-worker run `20260811T175350Z-011a57cd` passes 6/6. |
| GBA-072 | P1 | Closed | Settings local-data reset / installed identity | Accepted DLV-089 replaces the synthetic disabled namespace with the canonical version-derived installed identity. Bridge 73/73 covers enabled-to-disabled stale/clear/re-enable behavior with no worker creation and an unaffected neighbor. |
| GBA-073 | P1 | Closed | Protected Wi-Fi host transport / Native Wi-Fi rollback | Accepted DLV-093 `3ce8991`, integrated with DLV-087 through `63ca3a2`, replaces password-bearing JSON/string copies with a bounded mutable zeroed frame and requires an exact per-attempt profile ownership token before deletion. Mismatch, unavailable verification, and delete failure are explicit and preserve current Windows state. |
| GBA-074 | P1 | Verifying on packaged main | Running-app observation / Widget SDK / Games & Apps / Game Launcher | DLV-095 corrected by accepted DLV-097 is integrated through `c6d76a3`; native inspection is bounded before eligibility and confirmed items are fully validated before either widget can mutate state. The fully packaged Release is visibly running for the user's live route check. |
| GBA-075 | P0 | Assigned as DLV-100 | WidgetBridge computed styles / protocol-v15 `TextEntry` / Game Launcher / Network Controls | Current production Game Launcher snapshots fail before native rendering because the bridge style-role resolver omits the already-supported `TextEntry` node kind. Five identical failures are present in the current PID 35472 session and no other current-session error signature was found. |
| GBA-076 | P1 | Ready as DLV-101 | Installed-widget runtime / manifest resources / worker Job policy / authoring contract | Prototype hard worker-memory and one-process ceilings contradict the approved full-application widget model. Private worker execution must not be arbitrarily product-capped; shared-host messages, presentation/native resources, capability traffic, and host admission remain bounded. |

## GBA-001 — Per-application audio controls have no real effect

**Evidence:** On 2026-08-07 the packaged Release overlay displayed three real
Core Audio sessions. Repeated LT/RT and mute input was logged as handled, all
four audio read/control grants were present, but the corresponding Windows
session volume did not change. Master-output control is a separate path and is
not evidence that session control works.

**Implementation evidence:** A reversible, opt-in live provider test changed a
real `System sounds` session from 100% to 95%, read back 95% through Core Audio,
and restored 100%. The redesigned widget no longer infers a selected card from
global trigger shortcuts: every visible application row has stable opaque IDs
and an immutable action map that resolves directly to the provider's exact raw
session ID. Removed or stale actions fail closed. The Audio provider and Audio
Mixer Release suites pass 15/15 and 25/25. This proves the provider and widget
seams independently, but not yet the exact packaged worker-to-broker path
against a playing application.

**Acceptance:**

1. A reversible native test captures one real session's original volume/mute,
   sets a distinct bounded value, reads it back through Core Audio, and restores
   the original state in `finally`.
2. The packaged Audio Mixer changes and restores a playing application's volume
   and mute state, with authoritative UI reconciliation and no stale rollback.
3. Provider, broker, SDK, widget, and packaged Release tests pass.

## GBA-002 — Compact widgets waste most of a constant panel

**Evidence:** Packaged Audio Mixer and Network Controls render a roughly
single-column 440 DIP layout inside the host's common 880 DIP widget panel.

**Implementation evidence:** Snapshot protocol 2 and the public SDK expose
bounded Compact, Standard, Wide, and Adaptive hints with optional preferred and
minimum logical-DIP pairs. The native host resolves those hints without widget
IDs, reserves shell/tray/footer space, grows safely for text accessibility, and
clamps against the selected monitor after DPI and interface scaling. Audio and
Network publish Compact surfaces, YT Music publishes Standard with a flexible
480 x 340 minimum media budget, and Settings publishes Standard. API-1/no-hint
widgets retain the legacy surface. Native
Release placement tests cover 720p, portrait, ultrawide, invalid hints, 200% DPI,
125% interface scale, and 150% text scale.

**Acceptance:**

1. Every view can publish bounded compact/standard/wide/adaptive presentation
   hints through the public protocol and SDK.
2. OverlayHost clamps hints against active-monitor work area, DPI, interface and
   text scale, shell tray/footer reservations, and minimum controller targets.
3. Audio Mixer and Network Controls open as compact floating panels; YT Music
   can request its wider media presentation.
4. Small/portrait/ultrawide and mixed-DPI tests prove containment and responsive
   fallback without hard-coded widget IDs.

## GBA-003 — Audio Mixer needs an all-session scrollable surface

**Evidence:** The reported widget rendered one selected session and cycled
sessions with shoulder shortcuts. This made comparison slow and did not match
the requested mixer mental model.

**Implementation evidence:** The earlier fixed master/device/microphone region
could consume more than the host content viewport and leave its nested session
Scroll partially clipped by a non-scroll ancestor. Audio now publishes one
bounded root Scroll (`audio.root`) containing the header, master output,
sanitized device summary, microphone control, and every application row. Master
and application rows use
the same icon–Slider–percentage composition: the nonfocusable icon exposes mute
state, Left/Right changes the focused Slider's absolute volume, A toggles mute,
and Up/Down moves between rows. Each row is one stable focus target.
LB/RB/LT/RT session cycling, root shortcuts, and dashboard quick actions were
removed. The widget records the last known master, microphone, or stable
application focus target from normal controller input without consuming
host-owned B/navigation. Reopen publishes that target as initial focus; session
churn retains the same opaque session or selects the nearest surviving row.
Host-owned scroll offsets remain keyed by exact worker instance, input scope,
and scroll ID. The Audio Mixer suite passes 25/25 focused Release tests,
including the complete focus chain/restore regression, rapid absolute Slider
updates, 128-session, and long-label cases. Native renderer coverage passes
4,311 checks and walks Audio-like master/input/application geometry at the
actual 464-DIP host content height and a constrained 304-DIP height. These
focused checks do not replace packaged controller and visual verification.

DLV-026 (`979de24`, `68efc70`, corrected by `9cc633a`, integrated as
`4957101`) addressed one scale-edge hypothesis but did not close the later user-
reported reverse trap. Scale
conversion could leave a Slider edge within one native raster pixel of its
matching rounded card clip; the prior feasibility test rejected that target
even though the root Scroll still had range. The renderer now tolerates only
that scale-aware raster edge while retaining negative coverage for wrong-axis,
greater-than-one-pixel, and fixed nested clipping. A screenshot-excluded
production-HWND/UIA manifest records 84 functional states across preferred,
constrained, and 150% surfaces: every 14-control Down path reverses one control
at a time to master output and offset zero, while unrelated removal, focused
removal, nearest fallback, and addition preserve deterministic focus. The fresh
Release disproved product acceptance: with four live application sessions, Up
from Microphone could not return to offscreen Master using either keyboard or
controller, and cycling away/back produced a `value_clamped [audio.root]` repair.
Accepted DLV-049 confirms the emitted production snapshot declares
`audio.input.volume.slider` Up as Master. On DLV-021's corrected shared-geometry
baseline, the exact staged four-session state retains Master as an offscreen but
revealable target at finite `296.6 / 298.2` offset/range. One production HWND/UIA
Up reaches Master and offset zero without cycling; reopening preserves that
canonical state and the log contains no `value_clamped [audio.root]`. Focused
Release evidence passes 4,774 renderer checks, 35 authenticated probe checks,
the single live-shaped production-host scenario, and a fresh host build. The
issue remains Verifying until the freshly launched Release passes the user's
keyboard/controller reproduction.

**Acceptance:**

1. One bounded controller Scroll contains master output, device/microphone
   content, and every current application session without a clipped nested
   viewport.
2. D-pad and analog navigation move through stable per-session controls; no
   bumper/trigger action changes the selected application.
3. Host-owned focus-follow scrolling keeps the focused row fully visible.
4. Closing/reopening restores the focused application and scroll location for
   the same widget runtime/input scope. Session churn retains the stable row or
   selects the nearest surviving row.
5. Empty, one-session, many-session, long-label, 720p/high-scale, and live churn
   tests pass.

## GBA-004 — Widget switching can flash tray/panel spacing

**Evidence:** The user first reported a transient black border/spacing flash
when switching from Audio Mixer to Network Controls. A 2026-08-09 packaged run
now reproduces the same class of flash around Spotify while cycling widgets and
describes the transition itself as jarring. The earlier same-extent/extent-
changing placement optimization is therefore not sufficient evidence that
presentation is visually continuous.

**Root cause and implementation evidence:** The host synchronously painted its
own `Starting isolated ...` surface before the queued destination snapshot
request, and visible extent changes could recreate/clear the Direct2D target
between placement and the later repaint. DLV-020 (`b0c95ca`, integrated by
`7cda335`) retains one bounded admitted snapshot and exact surface as visual-
only content until the destination snapshot is admitted; destination input,
lifecycle, focus, and UI Automation authority transfer immediately. Admission
then retargets a bounded 140 ms extent curve from presented geometry and resizes
the existing HWND render target in place. The focused Release run passed the
placement, targeting, transition, chrome, renderer, and two production-host
fixtures. Forty-four reviewed HWND frames cover Audio, Network, delayed Spotify,
delayed Games & Apps, rapid reversal, and same-identity Games reload without a
startup dialog, black clear, square edge, stale extent, or tray loss. That
evidence was static and synthetic enough to miss temporal product behavior. A
2026-08-10 user capture of the real Games & Apps path shows a pronounced laggy
resize with whole-interface flicker, light-gray exposed areas, black side/lower
bands, and partially committed geometry. Code inspection also confirms that
the 16 ms transition timer, per-frame `SetWindowPos`, synchronous `WM_SIZE` and
Direct2D target resize, and `RedrawWindow(...RDW_UPDATENOW...)` share the Win32
UI thread. DLV-025 then measured populated first paints near 31 ms, only six
successful Spotify paints across 14 inputs over 674 ms, and five consecutive
captures with a dark interior band at final geometry. Removing repeated extent
interpolation did not close the defect: resizing the current single-HWND
Direct2D target can expose its undefined resized back buffer before the next
successful draw, and a later `DwmFlush` cannot retract an already composed
frame. The known-bad prototype remains uncommitted. Resumption requires a
bounded user-authorized offscreen atomic-present, DirectComposition, or
swap-chain design.

The 2026-08-11 full Release refresh for accepted Game Launcher main
`bb449d0` republished the coherent managed/runtime graph, then the unchanged
`WidgetSwitchHostTests` failed because the production host did not paint its
expected Audio Mixer fixture surface. The accepted launcher prefix does not
touch the transition fixture or native presentation path, so this is retained
as additional GBA-004 fixture/product evidence rather than retried unchanged or
used to reject the managed milestone. A future compositor decision must make the
fixture deterministic against the selected presentation architecture instead
of adding retries or capture work.

The next accepted-main refresh through DLV-067 integration `3d8f486` published
the coherent managed/runtime graph and reached the same suite, which failed with
more precise evidence: `Worker startup replaced the prior admitted content with
a transient surface.` This narrowed one defect away from DLV-025's then-blocked HWND
resize/compositor decision. Queued DLV-078 owns retained-content versus first-
snapshot admission ordering only; it starts after DLV-075 releases the shared
native host/build boundary and must pass the existing fixture without retries or
screenshot-based diagnosis.

DLV-078 `6d30f5e`, integrated through `a072d6f`, corrects that narrower
ordering defect. The host now backgrounds the prior worker and clears its
focus, input, and actionable UI Automation authority before synchronously
painting the prior admitted snapshot as inert visual-only content. The posted
refresh establishes the cold destination and replaces retained content only
after its first valid snapshot is admitted. The isolated production-host
fixture proves retained then admitted renderer identity/sequence for delayed
Spotify and Games starts, rapid reversal, and same-identity Games reload; it no
longer interprets capture pixels. Focused evidence passes the exact host fixture
plus 54 targeting and 56 transition checks. Keep this sub-fix Verifying until
the freshly relaunched packaged overlay passes live widget cycling. DLV-025's
separate HWND resize/compositor tearing remains blocked and is not claimed fixed.

**Acceptance:**

1. A timestamped real-product trace identifies the cost and committed-frame
   ordering across timer dispatch, placement, `WM_SIZE`, target resize,
   invalidation/redraw, bridge work, and presentation; static endpoint captures
   are insufficient.
2. Switching widgets keeps the host-owned tray continuously painted; widget
   snapshot/style changes expose no gray, black, transparent, stale, or
   unpainted regions.
3. Repeated real Games & Apps, Spotify, Audio Mixer, and Network switching shows
   no flicker and meets a documented cadence/frame-time budget at supported
   DPI/interface scales. If live HWND resizing cannot meet it, the host uses an
   immediate or composition-only fallback rather than shipping laggy motion.
4. Reduced motion is immediate, reversal is stable, and no idle presentation
   work is added.

## GBA-005 — B must behave as hierarchical Back

**Evidence:** The controller policy was not expressed as one testable hierarchy,
which made root-widget fallback look indistinguishable from an accidental close
and left dashboard close behavior vulnerable to inconsistent special cases.

**Acceptance:** B always goes back exactly one level: the active nested widget
scope handles it first; an unhandled B at the widget root returns to the icon
tray/dashboard; B on the icon tray closes the overlay. Guide continues to toggle
the whole overlay from any level. No other widget action is captured as Back.

**Implementation evidence:** Native routing now expresses dashboard A/B/Y,
open-widget delivery, root fallback, and nested non-bubbling as a pure ownership
policy. Managed snapshot validation and the bridge reject dashboard B quick
actions. Controller navigation passes 73 checks; nested Settings Back behavior
passes its focused Release suite.

## GBA-006 — View-specific surface transitions must resize

**Evidence:** Direct snapshot refreshes after handled actions and catalog
reconciliation replace the cached view before comparing presentation extents,
so a compact nested view can remain inside the previous standard-size HWND.

**Acceptance:** Every snapshot replacement compares the previous and next
resolved extents through the pure presentation policy. Equal extents repaint;
changed extents place exactly once.

**Implementation evidence:** Catalog, action-result, invalidation, and async
snapshot paths use one refresh-and-presentation helper. Targeting passes 31
policy checks and the native Release suite is green.

## GBA-007 — Scroll focus must always remain visibly recoverable

**Evidence:** Nested scroll corrections are computed from one stale layout, and
any descendant under a Scroll is marked revealable even when another clip or the
scroll's cross-axis prevents exposure. Focus can therefore land invisibly or be
overscrolled out of an ancestor.

**Acceptance:** Bounded inner-to-outer correction produces a final visible
target; candidates clipped on an uncorrectable axis or by a non-scroll ancestor
are rejected; D-pad and analog navigation never enter an invisible focus trap.

**Implementation evidence:** Reveal resolves inner-to-outer through a bounded
32-pass fixed point and requires actual ancestor-axis/range feasibility. Native
renderer regressions cover nested, cross-axis, and non-scroll clipping.

## GBA-008 — Scroll resource bounds must not create visual corruption

**Evidence:** Crossing the saved-offset cap clears every offset, including the
active view, and deferred focus borders are painted after scroll clips are
removed using the full un-clipped control box.

**Acceptance:** Incremental bounded eviction preserves active state, and focus
outlines remain clipped to every containing scroll viewport without changing
ordinary non-scroll focus rendering.

**Implementation evidence:** The 4,096-entry guard now evicts least-recent
inactive offsets only. Deferred focus uses the effective intersection of scroll
viewports while ordinary focus remains unchanged. Renderer Release tests pass
4,311 checks.

## GBA-009 — Diagnostics client must authenticate its server

**Evidence:** The worker verifies the nonce but not the kernel-reported pipe
server PID before sending it. A same-user process that observes launch data can
race-create the endpoint and forge sanitized-looking diagnostics.

**Acceptance:** Host-owned launch data binds the expected server process, the
client verifies it before sending secrets, and a squatted fake-server test fails
closed without poisoning a later legitimate refresh.

**Implementation evidence:** The bridge pre-creates and retains the first pipe
instance before worker launch. Server and client verify the kernel-reported
peer PID before nonce exchange. Fake-server and squatter regressions fail closed.

## GBA-010 — Diagnostics requests need end-to-end deadlines

**Evidence:** The one-instance server uses only process-lifetime cancellation
for frame reads, provider execution, and response writes; the client constructor
also accepts infinite or nonsensical custom timeouts.

**Acceptance:** A short per-request server deadline covers the complete exchange
and returns to accepting clients after stalled hello/provider cases. Client
timeouts are finite, positive, and capped with deterministic validation.

**Implementation evidence:** A per-connection deadline covers hello, request,
provider, and response work; client timeouts must be finite, positive, and no
more than ten seconds. The diagnostics Release suite passes 8/8, including
stalled hello/provider recovery.

## GBA-011 — Async control state must not move controller focus

**Evidence:** In the packaged Audio Mixer, activating mute beneath an
application row immediately moves focus to the master-output volume `+`
control. The widget globally marks controls unavailable while one broker
request is pending, and the host interprets that transient state as removal
from the focus graph.

**Acceptance:**

1. `Disabled` prevents activation but remains focusable and controller-
   navigable; it exposes a readable unavailable state rather than disappearing.
2. `Busy` remains focused and navigable while duplicate activation or value
   changes are suppressed or coalesced.
3. Only an explicit hidden/non-navigable state removes a control from the focus
   graph. A focused control changing enabled, disabled, or busy state retains
   its exact stable focus ID.
4. Audio pending state is scoped to the exact output/session operation and does
   not disable unrelated rows. Post-acknowledgement stale events cannot roll
   back the optimistic value or move focus.
5. SDK, host focus, bridge/runtime, and Audio Mixer regressions cover focus
   retention, ignored disabled activation, busy coalescing, and session churn.

**Implementation evidence:** Buttons and Sliders remain in the host focus graph
when disabled or busy; those states suppress actions without changing stable
focus identity. Audio now renders one Slider focus target per master/session
row, with A mute and absolute left/right volume. Independent per-session/output
state coalesces rapid volume targets latest-wins, retains authoritative state
through stale post-acknowledgement events, and rolls back bounded failures. The
SDK focused suite passes 70/70 and Audio Mixer passes 25/25, including the exact
application-mute focus regression. Packaged controller evidence is still
required before closing.

## GBA-012 — Focus outlines must stay inside their effective clip

**Evidence:** The Settings category selection outline loses its left and right
edges because the focused full-width row expands beyond the widget drawing
window. Similar full-width list controls can be clipped by a scroll or surface
boundary.

**Acceptance:**

1. Focus decoration is resolved inside the final intersection of the control,
   widget surface, and every ancestor clip at all supported DPI/text scales.
2. Full-width list rows do not use a focus transform that grows outside their
   layout allocation.
3. Renderer and component-gallery regressions cover edge-aligned controls,
   nested scrolling, 720p through 4K, mixed DPI, and increased text scale.

**Implementation evidence:** The built-in focused Button and Slider styles use
an inset outline and no scale transform. Settings removes its full-width focus
growth. Deferred native focus decoration now starts with the render surface,
intersects every Scroll or `overflow: clip` ancestor, and preserves explicit
`overflow: visible`. Platform theme Release tests pass 13/13; native renderer
tests pass 4,311 checks including nested non-Scroll clips, scaled root-edge
controls, visible-overflow freedom, and retained Scroll behavior. Packaged
screenshots at supported scale settings remain required before closing.

## GBA-013 — Network Controls needs a dense controller-first surface

**Evidence:** The compact Network Controls widget constrains its content root
to 44 percent of an already compact host surface. This leaves a large dead area
on the right and forces saved profiles into a single arrow-cycled card rather
than a scannable controller list.

**Acceptance:**

1. Content uses the resolved compact surface width without viewport-relative
   double-constraining or horizontal overflow.
2. Current transport, radio/privacy state, explicit scan, and available-network actions use a
   clear visual hierarchy with concise alert, empty, connecting, and failure
   states.
3. Wi-Fi and Bluetooth use separate bounded vertical controller views with
   stable IDs and independent selected-row restoration. LB/RB changes the
   active view; neither shoulder nor trigger cycles items inside a list.
4. Empty, one-profile, many-profile, long-label, radio-off, wired-only,
   connecting/failure, 720p, high-DPI, and text-scale regressions pass.

**Implementation evidence:** Network now consumes the resolved compact width
and publishes mutually exclusive Wi-Fi and Bluetooth views under one segmented
tab bar. LB/RB changes tabs from any root focus; A can also select a focused
tab. Wi-Fi and Bluetooth own distinct stable Scroll IDs
(`network.wifi.body.scroll` and `network.bluetooth.body.scroll`) and retain
their own opaque selected item, so returning to a tab restores its focus target
and host focus-follow reconstructs the visible location. A explicitly scans
from the Wi-Fi Scan control; A and X connect the exact current saved/open
focused result. No scan occurs on activation or a timer. LT/RT do not cycle
items, and dashboard quick actions remain absent. Credential-required and
unsupported authentication remain typed, sanitized states. Network Controls
passes 17/17 and WindowsNetworkProvider passes 31/31. The shared segmented-tab
theme now reserves a nonshrinking 50-DIP region around its 44-DIP tab targets.
A native renderer regression covers both the normal 364-DIP compact viewport
and a constrained 300-DIP viewport at 1.25 interface scale plus 150% text; each
tab must remain visible, focusable, controller-enabled, within the viewport,
and inset for an unclipped focus ring. Packaged split-view visual/controller/
privacy evidence remains.

## GBA-014 — YT Music window shortcuts and transport glyphs are focus-dependent or reversed

**Evidence:** In the packaged YT Music widget, the user's controller test and
screenshot showed LB, RB, X, and Y working only while focus was on the sibling
transport Button that declared that shortcut. Moving focus to like, dislike,
shuffle, repeat, or another connected control made the intended window command
unavailable. The same screenshot showed Previous and Next rendered with their
directional geometry reversed.

**Acceptance:**

1. In the connected YT Music window, X toggles playback, LB selects Previous,
   RB selects Next, and Y refreshes from every focus target and a focusless root.
2. A remains local activation for the exact focused Button. A shortcut declared
   on a Button remains exact-focus-only and is never discovered through a
   sibling search.
3. Window-wide commands are declared once on the active scope-root container.
   A nested active scope cannot inherit or leak to the parent YT Music scope,
   and stale or mismatched scope/snapshot input remains unhandled.
4. The Previous glyph has its stop bar on the left and triangle pointing left;
   Next has its stop bar on the right and triangle pointing right. Semantic IDs,
   accessibility labels, and actions remain unchanged.
5. Focused YT Music routing tests, native pixel-orientation tests, the complete
   Release gate, and packaged controller/screenshot verification pass.

**Implementation evidence:** The connected view now attaches X/LB/RB/Y once to
the `ytmusic-root` scope container and leaves the sibling transport Buttons
without non-A shortcuts. The SDK still resolves an exact focused-node shortcut
first and then only the active scope root. YT Music passes 38/38, including
every-focus/focusless routing, focused-A ownership, and nested-scope isolation.
Native Icons passes 188 checks, including pixel assertions for the stop-bar and
triangle direction of both glyphs. Packaged hands-on controller and screenshot
verification is still required before closing.

## GBA-015 — Fixed widget regions clip and the built-in visual hierarchy is too heavy

**Evidence:** Packaged Audio Mixer and Network Controls screenshots showed
header copy, row labels, and trailing values crowded or clipped when the
scrollable content needed more room. Sliders appeared as a thick filled capsule
with a second track painted over it, while large radii, heavy typography, and
dense card surfaces made YT Music, Settings, Audio Mixer, and Network Controls
feel visually bulky.

**Root cause:** The layout engine allowed fixed headers, section labels, and
trailing metadata to use the default flex shrink behavior alongside the actual
scroll region. The renderer also painted a Slider node's background as a full
control surface before drawing the Slider track, producing two competing
surfaces instead of one thin control.

**Acceptance:**

1. Headers, section labels, fixed controls, and trailing values use
   non-shrinking allocations; only the intended scroll/content region yields
   space at compact sizes.
2. Long and localized labels truncate or wrap deliberately without hiding the
   value, state, or focused controller target at 720p and supported DPI/text
   scales.
3. A Slider renders one thin track, fill, and thumb inside a minimum 44-DIP
   focus/hit target. It has no duplicate full-height background surface.
4. Built-in theme, YT Music, Settings, Audio Mixer, and Network Controls use a
   consistent lighter hierarchy for typography, spacing, radii, surfaces, and
   focused states without weakening contrast or controller legibility.
5. Layout/style/renderer regressions and packaged screenshots pass across the
   compact, standard, high-DPI, and increased-text-scale matrix.

**Implementation evidence:** Fixed widget regions and trailing metadata now
opt out of flex shrinking while bounded scroll regions retain the available
flex. The native Slider draws a thin track within its unchanged 44-DIP
controller target instead of painting a second full control background. The
built-in platform theme and all four packaged widget themes use the revised
lighter typography, radius, spacing, and surface treatment. Declarative
Renderer passes 4,311 checks. Packaged visual screenshots are still required
before closing.

## GBA-016 — Returning to the tray must not hide the selected widget panel

**Evidence:** The earlier shell modeled the widget panel as visible only while
the widget owned controller focus. Pressing B at the widget root therefore
returned to the tray by hiding the panel, forcing A to reopen it and preventing
PS5-style preview navigation between a persistent tray and the selected panel.

**Acceptance:**

1. While the overlay is open, the selected widget panel and icon tray remain
   visible as separate regions regardless of which region owns focus.
2. An unhandled B at the widget root moves focus to the tray and keeps that
   widget visible. B on the tray closes the overlay. Nested scopes consume
   their own B and never fall through multiple levels.
3. Left/Right D-pad or left-stick navigation on the tray selects the adjacent
   widget and swaps the visible panel automatically. A enters that panel's
   controls; it is not required to reveal the panel.
4. Down from the last root-scope control returns focus to the tray, including a
   deliberate root self-loop. A responsive root with no reachable control also
   returns to the tray on Down; directional escape never crosses a nested scope.
5. Guide remains a host-global toggle detached from either region's navigation
   graph. Tray and widget retain independent focus restoration.
6. Lifecycle distinguishes presentation from interaction: the tray-selected
   panel is `Visible`, the panel owning focus is `Interactive`, and a replaced
   or closed panel becomes `Background`.
7. Native state, routing, lifecycle, and packaged controller tests cover Back,
   tray switching, root-boundary escape, nested containment, focus restoration,
   Guide close/reopen, and rapid region changes without flicker.

**Implementation evidence:** Overlay state now tracks visible widget selection
separately from focus ownership. Root Back transfers focus to the persistent
tray; tray Back closes; tray selection swaps the visible widget; A enters its
controls; and a root-scope Down boundary returns to the tray without escaping
nested scopes. Responsive focus recovery now admits an empty current focus,
selects the first visible root target when one exists, and treats a fully
unreachable root Down as the same tray boundary instead of trapping focus;
fully clipped nested scopes remain contained. Guide remains region-independent.
Lifecycle transitions publish
`Visible` for a previewed panel and `Interactive` only while its controls own
focus. Controller Navigation passes 73 checks and Declarative Renderer passes
4,311 checks. Packaged hands-on controller evidence remains required before
closing.

## GBA-017 — Scroll boundaries do not fully reveal the first/last control

**Evidence:** Current packaged hands-on testing reports that a Scroll can stop
with its top or bottom focus target only partially visible.

**Acceptance:** Focus-follow reveal includes the control, focus decoration, and
container padding at both boundaries; repeated Up/Down cannot leave the focused
target clipped, and compact/high-scale/nested-scroll regressions pass.

**Implementation evidence:** The renderer now identifies the first/last
focusable descendant of each Scroll and snaps those endpoints to offset zero or
the maximum extent while preserving nested fixed-point reveal. Declarative
Renderer passes 4,311 checks. Status remains Verifying pending packaged input
and screenshot evidence.

## GBA-018 — Tray Up should enter the visible widget

**Evidence:** A enters the already-visible widget from the tray, but Up does not
provide the spatially natural equivalent transition.

**Acceptance:** Pressed Up or upward left-stick navigation from a tray item
enters that item's visible panel at its remembered/root focus. It must not
activate a control, change selection, or escape a nested widget scope.

**Implementation evidence:** Host routing maps only a pressed Up boundary from
the tray (not a repeat or widget action) to the existing enter-widget path for
both D-pad and left stick. Controller Navigation passes 73 checks. Status
remains Verifying pending packaged controller evidence.

## GBA-019 — Controller guide wraps and clips

**Evidence:** The current packaged footer/control guide wraps and clips labels,
making button assignments visually noisy and difficult to scan.

**Acceptance:** The guide uses a compact non-wrapping hierarchy, prioritizes
context-relevant actions, truncates or adapts safely at compact/high-scale
sizes, and never overlaps panel or tray bounds.

**Implementation evidence:** The host now chooses guide density from available
width and text scale, emits one no-wrap line, sanitizes bounded labels, and
retains up to three selected-widget quick actions alongside host navigation.
Status remains Verifying pending compact/high-scale visual-matrix evidence.

## GBA-020 — Rounded panel corners clip content

**Evidence:** Packaged hands-on testing reports visible clipping around rounded
floating-panel corners.

**Acceptance:** Background, border, focus decoration, and child clips share one
inset corner geometry at every supported DPI/interface/text scale, with no
content loss or square artifact.

**Implementation evidence:** The native renderer owns a cached rounded viewport
clip/mask, preventing an opaque widget root from painting square panel corners;
a WIC pixel regression covers the mask. Status remains Verifying pending
packaged screenshots.

## GBA-021 — Display changes must refresh all dependent geometry atomically

**Evidence:** DPI, display-topology, and system-setting notifications previously
used separate partial refresh paths, allowing work-area or appearance changes
to miss an authoritative monitor/DPI placement pass.

**Implementation evidence:** The Per-Monitor-V2 host now maps visible DPI,
topology, and system-settings changes through one tested refresh policy that
recreates graphics, conditionally reapplies appearance, and performs one fresh
monitor/work-area/DPI placement. Back-to-back notifications merge into one
posted message-loop refresh while retaining the strongest requested work;
hidden notifications defer all work to the next open. Placement passes 108,545
checks and Targeting 42 checks. Status remains Verifying until physical
mixed-DPI migration, hot-plug, taskbar-edge, and accessibility/theme screenshots
are retained.

## GBA-022 — Permission review hides pages and duplicates Back

**Evidence:** The packaged Audio Mixer permission screen displayed “Page 1 of
2” but exposed no visible page control. The same screen rendered a full-width
Back row even though hierarchical B already owned Back, wasting space and
adding an unnecessary focus stop.

**Acceptance:** Package and capability collections are ordinary bounded
vertical controller Scrolls with stable item IDs, true endpoint reveal, and no
shoulder-button pagination. Package, capability, and decision scopes expose B
as the only Back action. Empty or invalid permission states remain focusless
but B-recoverable, and long decision copy remains scrollable at supported text
scales.

**Implementation evidence:** All three permission scopes now use protocol-v2
vertical Scroll nodes. Package and capability rows are linked through one
stable focus graph, page labels/shortcuts/state and in-content Back Buttons are
removed, and malformed-state fallbacks retain their scoped B action without a
fake focus target. Settings passes 41/41 focused Release tests. Status remains
Verifying pending packaged controller and increased-text-scale evidence.

## GBA-023 — Background residency policy was metadata only

**Evidence:** Manifest-v1 accepted `backgroundPolicy: none|suspend`, but every
launched worker remained resident and the bridge did not distinguish the two.
There was no explicit, bounded way for an author to trade warm state for idle
memory without unsafe process suspension or an external kill heuristic.

**Acceptance:** A versioned manifest vocabulary preserves old package meaning,
defaults to keep-alive, offers cooperative suspend and explicit bounded idle
unload, and applies identically to bundled and installed workers. Unload must
serialize with operations, send Destroying, release worker/companion resources,
retain the last validated view, cancel cleanly on visibility, restore lifecycle
on a lazy new worker, and not consume crash-restart allowance. No policy may
suspend OS threads or authorize ordinary manifest-declared Background broker
work. The bounded host-granted private-state persistence exception remains
available in Background and is denied in Destroying.

**Implementation evidence:** `residencyPolicy` schema 1 validates
`keep-alive`, `suspend-when-hidden`, and `unload-after-idle` with a required
5–86,400-second bound. Legacy none/suspend map to keep-alive/cooperative
suspend, while mixed vocabularies fail closed. The bridge uses a per-widget
operation gate, generation-canceled timer, Volatile lifecycle state, cached
last-good snapshot, bounded runtime unload, and lazy lifecycle restoration.
Hidden suspended workers cannot render, publish invalidations, receive input,
or access the broker. SDK, Runtime, Bridge, Catalog, CLI, Settings, widget, and
documentation focused suites pass; status remains Verifying pending packaged
idle-memory and repeated hide/show churn evidence.

## GBA-024 — Developer mode can report a broken generation Ready

**Evidence:** The initial `gbar dev` implementation treated a host process that
remained alive for 750 ms as initialized. OverlayHost can remain alive while
showing a bridge/catalog initialization error, so that probe could stop the
actual last-good generation and label a broken one Ready. Cleanup also did not
surface a process tree or temporary generation that resisted reclamation, and
prebuilt package-directory watching omitted supporting payload/assets.

**Acceptance:** A replacement generation must publish an authenticated,
session-scoped readiness signal only after its exact development catalog and
WidgetBridge are usable. The previous generation remains active until then.
Cancellation and replacement must prove the launched process tree exited and
report unreclaimed state. The bounded, reparse-safe watch set must cover every
file packed from a package directory, including new supported subdirectories,
without watching build outputs or unrelated trees.

**Implementation evidence:** `gbar dev` builds each candidate into an immutable
generation and starts a controller/hotkey-free probe. The probe authenticates
the exact catalog, widget ID, and instance with a random nonce only after the
candidate AppContainer worker enters `Visible`, returns a protocol-validated
snapshot for that instance, and returns to `Background`. The interactive
candidate publishes readiness only after the overlay/backdrop are visible and
a fresh exact-generation bridge listing succeeds; active-start failure restarts
the prior generation. Every host starts suspended, is assigned to a CLI-owned
kill-on-close Job Object before its first instruction, and cleanup waits for
zero active descendants. Missing/forged readiness, a missing widget type, and a
persistent child/grandchild all fail closed. Bounded reparse-safe project
watching includes general MSBuild inputs and newly created directories while
excluding `.git`, `.vs`, `bin`, and `obj`. CLI passes 45/45 and all native
focused suites pass; packaged author-workflow evidence remains.

## GBA-025 — Tray quick actions cannot use brokered controls

**Evidence:** Dashboard quick actions correctly run while their selected widget
is `Visible`, while audio/network/activity/media control capabilities correctly
require `Interactive`. Promoting every selected tray widget to Interactive
would unnecessarily broaden authority; leaving the mismatch makes advertised
LB/RB/X controls fail with `lifecycle_denied`.

**Acceptance:** A pressed, declared dashboard quick action may carry an
optional host-validated authority for one exact declared capability operation.
The authority is bound to widget identity, worker session, current snapshot,
controller sequence, and bounded deadlines. Queueing must not start broker
authority: a dormant host-owned reservation may last at most 10 seconds, but
only the exact typed operation may atomically activate a one-use broker lease
lasting at most two seconds. Replay, mismatch, lifecycle loss, replacement, or
shutdown revokes the relevant stage. Neither stage enables a subscription or
promotes lifecycle. Consent and provider checks still apply.

**Implementation evidence:** `WidgetQuickAction` can name one typed capability
operation. The bridge derives a dormant reservation only from the selected
widget's cached snapshot, positive host input sequence, declared control
capability, and `Visible` lifecycle. A private SDK action context requests
activation only when that exact queued or custom handler actually invokes the
typed operation, so time spent behind an earlier action cannot consume the
two-second broker window. The dormant reservation uses a separate host-owned
monotonic clock and expires within 10 seconds; it is not broker authority. The
runtime atomically removes a matching capability, operation, input, and snapshot
reservation before its identity/PID-bound companion starts the at-most-two-
second broker lease; widget APIs cannot mint authority. A continuous bounded worker reader permits
custom async controller handlers to receive the activation acknowledgement
without concurrent pipe readers or deadlock. The broker keeps at most 16
two-second active grants, consumes each exact tuple once, and clears dormant
and active state on lifecycle, consent, process, or session teardown.
Subscriptions remain ineligible and lifecycle is never promoted. Focused SDK,
Broker, Runtime, Bridge, and Now Playing suites pass 70/70, 46/46, 33/33,
36/36, and 16/16 respectively and cover wrong operation/capability,
slow-first rapid-second input, custom async routing, replay, expiry, stale
snapshot, denial, and revocation cases.

## GBA-026 — Reference widgets lack real community-package conformance

**Original evidence:** The references had focused widget tests with injected
fake services while installed-catalog isolation tests used synthetic packages.
No acceptance test packaged each real reference, installed/enabled it, resolved
it through the generic worker host, launched it in the package-specific
AppContainer, and rendered/acted through its authenticated simulated broker.

**Acceptance:** A bounded Windows conformance suite performs that complete
path for every claimed public-SDK reference without real OS mutation. It must
assert immutable package resolution, AppContainer-required launch, exact
declared authority, valid rendered snapshots, lifecycle enforcement, and at
least one read/action path. A widget that depends on a trusted-only facility is
explicitly classified as such instead of passing by exception.

**Implementation evidence:** The shipped catalog now separates trusted worker
entries from ordered `bundledWidgets`. Settings is the only temporary Job-only
exception. Audio Mixer, Network Controls, Games & Apps, and Now Playing derive
entrypoint, publisher, permissions, memory, residency, and
styles from their real manifests and launch through the generic worker in a
capability-free package AppContainer. The Release conformance suite builds and
installs those four `.gbarwidget` packages plus the YT Music Community package,
then merges them through
`WidgetCatalog`/`BridgeCatalog`, grants simulated consent through the production
PID-bound broker companion, and executes both the installed and separately
configured bundled routes. Both forms drive lifecycle and validate rendered
snapshots. Games & Apps proves separate app-library read and launch
authority; the other built-in references exercise their safe brokered control.
YT Music additionally proves the public CLI package flow, pairing/private-
secret/Bearer path, dashboard transport, lifecycle enforcement, and absence of
a trusted fallback. The suite passes 5/5; Bridge passes 36/36. The packaged
build and host catalog contain and select no dedicated worker executable for
these references.

## GBA-027 — Recent Apps switching replaced by a scoped app launcher

**Evidence:** Selecting an observed terminal could create a new tab, a later
attempt could close the terminal, and Windows could reject foreground
activation. The feature also duplicated controller task switching while not
providing the requested installed games/app launcher.

**Acceptance:** No widget input delegates generic Windows foreground authority
to WidgetBridge. Recent activity remains read-only. A separate Games & Apps
package lists bounded installed registrations through opaque IDs and launches
only one current provider-revalidated registration through a separately
declared, consented, Interactive-only capability.

**Implementation evidence:** OverlayHost no longer calls
`AllowSetForegroundWindow` for widget controller input. The retired activation
capability, DTOs, broker dispatch/backend method, provider
`SetForegroundWindow` path, Settings copy, manifest declaration, and SDK method
remain removed. Recent Apps is no longer in the bundled catalog.

Games & Apps now uses the public generic-worker/AppContainer path with required
`system.apps.library.read.v1` and optional
`system.apps.library.launch.v1`. The SDK returns paged sanitized names, kinds,
short-lived launch IDs plus authority-scoped durable SavedIds. The broker gates
read/resolve to Visible/Interactive and launch to
Interactive, rejects path-like payloads, and never grants launch dashboard
gesture authority. The trusted provider re-enumerates immediately before
launch and requires one exact scope/identity/path/shortcut-fingerprint match,
then invokes only Shell `open` on that `.lnk` with no arguments, elevation,
working directory, or window handle. Widget/provider focused suites pass 15/15
and 13/13; SDK, PlatformBroker, Settings, and packaged
AppContainer conformance passes 5/5. The current catalog is Start
Menu-only, iconless, and conservatively Application-only; packaged overlay
hands-on evidence remains before closure.

## GBA-028 — A retired capability invalidates the entire consent document

**Evidence:** Removing the retired Recent Apps foreground-activation capability
from the closed capability vocabulary left its older durable consent decision
behind. Strict consent validation treated that entry as an arbitrary unknown
capability, rejected the complete document as `invalid_consent`, and disabled
permission review and changes even though the user's other decisions were
still current and structurally valid.

**Implementation evidence:** Consent validation now has an exact tombstone for
`system.activity.recent.activate.v1`. Loading an otherwise valid document
filters only that retired decision from the broker-visible result while
preserving its revision and every current decision. The next atomic consent
write omits the tombstoned entry from persistence. The tombstone does not make
unknown capability IDs forward-compatible: arbitrary unknown IDs, duplicate
retired entries, malformed identities, invalid decisions, and invalid document
bounds still reject the complete document and fail closed. Focused broker and
Settings tests cover current Grant/Deny preservation, the next-write cleanup,
unknown-capability rejection, duplicate rejection, and usable permission
review after migration.

Permission diagnostics are now controller reachable without making them
actionable. The package list exposes one focusable Review row; its nested
VerticalScroll contains disabled read-only rows with stable opaque IDs and
B-only return. Unsupported requests and inactive decisions share one 16-item
detail budget with an accurate remainder count. Exact publisher authorities
remain distinguishable, while all labels/accessibility text are sanitized and
bounded. If catalog projection is truncated, catalog data is invalid, or
consent is invalid, inactive classification is explicitly unavailable and no
inactive rows are shown. There is deliberately no consent cleanup action.

**Acceptance:**

1. Only the exact retired capability is migrated; it never produces broker
   authority and cannot become valid again through accidental re-registration.
2. Current decisions and the loaded revision survive migration unchanged, and
   the next atomic write removes the tombstoned entry from durable storage.
3. Arbitrary unknown capabilities and duplicate tombstones continue to fail
   closed as `invalid_consent`.
4. Settings keeps current permission rows actionable and accurately displays
   their retained decisions after migration.
5. The packaged Release Settings permission flow receives controller and visual
   verification with a migrated consent document.

## GBA-029 — Installed Widgets omits bundled first-party widgets

**Evidence:** Settings projected only the installed community-package catalog.
Manifest-backed widgets bundled with the app could appear in the overlay and in
permission review but were absent from Installed Widgets, making the inventory
look incomplete and obscuring the difference between app-owned and
user-installed packages.

**Implementation evidence:** Installed Widgets now renders one bounded
controller Scroll with explicit **Built in** and **Community** sections.
Bundled manifests supply stable read-only rows and details for identity,
publisher, version, runtime, compatibility, and required/optional capabilities.
Built-in details expose no enable/disable or version-management action, and
forged community-management actions cannot mutate built-in manifests or create
community catalog state. Community package paging, review, enablement, and
version management remain separate and unchanged. Focused Settings tests cover
multiple built-ins with an empty community catalog, read-only details, guarded
actions, and valid controller snapshots.

**Acceptance:**

1. Every discovered bundled first-party manifest appears under Built in, even
   when no community package is installed.
2. Installed community packages appear under Community and retain their
   existing review, enable/disable, compatibility, and version workflows.
3. Built-in details are explicitly read-only and cannot mutate bundled files or
   community catalog state through normal or forged actions.
4. B, Up/Down focus-follow, community LB/RB paging, empty sections, long labels,
   and high text/interface scale remain bounded in the packaged Release overlay.
5. Packaged visual/controller verification confirms the two sections are clear
   and the complete inventory is reachable without clipping.

## GBA-030 — YT Music is bundled instead of proving the Community addon path

**Original evidence:** YT Music shipped in the host-owned trusted catalog and
used a custom Job-only desktop worker with direct loopback/Credential Manager
access. That could not prove that an independent developer could package,
install, authorize, run, update, recover, and remove the same addon through the
public Community workflow.

**Required direction:** YT Music is the first Community addon integration
reference, not a permanent Built-in widget. Its local companion access must be
provided by reusable exact-port loopback HTTP broker operations, and optional
token persistence by a private per-widget secret service. The addon must not
receive ambient network authority, direct Credential Manager access, a trusted
desktop token, or another first-party-only escape.

**Implementation evidence:** The host catalog and build no longer publish YT
Music or its retired custom worker, and incremental builds remove the old
runtime directory. The addon declares `network.loopback:13091` plus optional
`storage.private-secrets.v1`, creates its production client only after public
`HostServices` attachment, and never opens a socket, reads a persisted value,
or calls Credential Manager. Its build helper stages the real payload and runs
public validate/pack/install/enable commands. Community conformance builds that
`.gbarwidget`, installs it into the current catalog model, launches the generic
worker in the mandatory AppContainer, grants declarations through the consent
store, pairs through host-side secret persistence, and invokes RB through one
exact dashboard POST lease. It also asserts no trusted catalog/build fallback.
Focused/provider tests cover bounded loopback, response sanitization, vault
round trips, broker lifecycle/revocation, SDK validation, and YT behavior.
Authenticated requests opt into host-side rejected-Bearer invalidation; an
actual 401 deletes the exact scoped slot while the dependent lease is valid,
and the widget clears local state without a second delete. Restart coverage
confirms the rejected slot is absent.

The complete conformance suite and focused YT/PlatformBroker suites cover the
typed companion path. `scripts/Test-YtMusicCommunityAddon.ps1` additionally
uses a unique temporary catalog and consent root to execute clean public
validate/pack/install, explicit consent, generic AppContainer launch,
simulated pairing, dashboard/open-window routing, suspend/resume, deliberate
worker termination and restart, fresh force reload, content-bound update
review, exact rollback, disable, and disabled-only uninstall. It snapshots the
real catalog read-only and fails if that inventory changes during the run. The
generated auth-free evidence does not claim real YTMDesktop2 pairing,
production Credential Manager purge, physical controller input, or packaged
shell pixels; those manual checks remain before closure.

**Acceptance:**

1. The public CLI packages and installs YT Music into the Community catalog;
   it is absent from the host's Built-in catalog and runtime-copy list.
2. Settings displays its real package identity, publisher, version,
   compatibility, declarations, consent state, enablement, and rollback using
   the same surfaces available to another developer.
3. The generic package AppContainer launches it without a trusted-token
   fallback, while authenticated broker operations provide only its declared
   exact-port loopback requests and host-side private-secret use; values are
   never returned to the worker.
4. Auto-connect, progress interpolation, artwork, transport/like/shuffle/repeat
   feedback, dashboard quick actions, nested controller navigation, lifecycle,
   crash recovery, update/rollback, and uninstall retain executable tests.
5. A clean-machine local install and packaged Release playtest prove the full
   author workflow, controller behavior, lifecycle recovery, and removal with
   the trusted exception absent.

## GBA-031 — Default components need a minimalist visual system

**Evidence:** The built-in theme now supplies warm graphite surfaces, regular-
weight typography, smaller radii, thin borders/tracks, compact spacing, and one
inset neutral focus treatment across the semantic component classes. Audio
Mixer, Network Controls, Settings, Now Playing, and Games & Apps were retuned
against those public classes rather than private host geometry. Parser/theme
tests cover the shared defaults, but packaged screenshots across the complete
display/accessibility matrix are not recorded yet.

**Acceptance:**

1. The component/theme layer defines one restrained hierarchy for panel,
   section, row, divider, value, status, slider, and focus surfaces.
2. Minimum controller targets and non-color state remain intact while redundant
   backgrounds, radius, border weight, and typographic emphasis are reduced.
3. First-party widgets consume shared defaults without private geometry or
   focus hacks unavailable to an independent SDK author.
4. Compact through 8K logical viewports, long labels, 150% text, high contrast,
   and reduced transparency remain readable and unclipped.
5. Packaged screenshots and controller traversal verify the complete component
   set, not only one hand-tuned widget.
6. Common product composition no longer requires private widget hacks:
   `SettingsRow`, `ActionSheet`, single-select `Picker`, controller `Scrubber`,
   lifecycle-owned `Toast`, and protocol-v7 `ActionSurface`/`MediaTile`/
   `AppTile` have reviewed bounded contracts, Rows can wrap responsively,
   protocol-v8 Grid provides row-major reflow, per-edge border width/color is
   independently bounded, and CodeText provides semantic monospace diagnostics.
7. The default rejects web-centric stagger/ambient motion, editorial serif or
   faux-macOS chrome, and treats packaged fonts as lower-priority security-
   sensitive assets rather than a baseline dependency.

**Implementation evidence:** Settings now uses the public Picker contract for
its complete theme catalog, including selected focus restoration, disabled
invalid entries, one host-owned Scroll, scope-owned B, and no LB/RB pagination.
Its focused suite passes 41/41. Responsive Row wrapping and the public
controller Scrubber have source and focused regression coverage; Spotify uses
the Scrubber instead of a private seek composition. Protocol-v7
ActionSurface supplies one clipped full-tile focus/pointer/pressed target with
bounded presentation-only descendants. Public MediaTile/AppTile and lifecycle-
owned Toast helpers plus stable theme hooks are implemented, and Games & Apps
adopts AppTile and Toast. Protocol-v8 ResponsiveGrid is implemented across
protocol/SDK/bridge/native layout; Settings uses it for root categories and
uses `UI.CodeText` for schema/worker diagnostics. GBSS/native style supports
independent per-edge width/color overrides, and the default CodeText style uses
single-family Consolas with bounded wrapping. Focused managed suites pass SDK
70/70, WidgetStyling 22/22, PlatformSettings 15/15, Settings 41/41, and Bridge
36/36. The authoritative full Release gate passed, including
all managed suites, documentation, native Release Layout 245,
Motion 39, Renderer 4,530, NativeStyle coverage, and protocol-v8 bridge/render
cases.
Hidden OverlayHost and InputProbe smokes also passed. Hands-on packaged visual/
controller/accessibility evidence remains before ledger closure; broader motion
remains incomplete.

## GBA-032 — GBSS transition declarations do not animate

**Evidence:** `transition-duration` and `transition-easing` now drive a bounded
native timeline for stable declarative node opacity, scale, and
`translate-x`/`translate-y`. First
observation snaps, later target changes retarget from the presented value,
durations are capped at two seconds, at most 1,024 nodes are tracked, removed
widgets are forgotten, settled content stops requesting frames, and reduced
motion snaps/cancels. Translation moves true subtree presentation geometry:
paint, clips, focus, hit targets, controller navigation, and Scroll focus-follow
share one result while layout allocation remains static. Replacement identity
does not inherit stale motion, and settled/hidden content does not request an
animation loop. The host now also owns a separate bounded shell/content
timeline: 140 ms ease-out open, 100 ms ease-in close, and 100 ms new/replaced-
identity reveal from 0.78 opacity. Reversals start from the presented alpha,
same-identity snapshots do not flash, entering widget focus can snap the reveal,
physical hide is one-shot after zero opacity, and reduced motion/settlement own
no follow-up frames. Other properties remain immediate, and packaged visual/
performance evidence is still open.

**Acceptance:**

1. Documentation identifies the exact node and shell/content animated sets and
   does not imply that unsupported properties animate.
2. A host-owned bounded clock interpolates only an explicit safe property set,
   with deterministic start, interruption, retarget, and completion behavior.
3. Reduced motion makes every transition immediate and stops outstanding work.
4. Hidden/background widgets produce no animation wakeups; frame scheduling
   coalesces visible nodes and respects performance budgets.
5. Native tests plus packaged visual/performance evidence cover focus, selected,
   busy, rapid reversal, resize/DPI change, and widget replacement.
6. The transient pressed-state pipeline, subtree translation, and bounded
   shell open/close/widget-identity reveal are implemented. Follow-on scope is
   packaged visual/frame-time evidence and later explicitly designed events.

## GBA-033 — Games & Apps needs durable curated launch semantics

**Evidence:** The current slice safely pages executable-backed Start Menu
shortcuts, bounded current-user AppsFolder/AUMID registrations, and bounded
registered Steam manifests into a
nested Catalog where A adds/removes entries from a separate Library view. The
Library supports explicit removal and moves an exact item to
the front only after launch success. The SDK/broker/provider issue an
authority-scoped durable SavedId, resolve it to a fresh launch token, and the
widget persists curation/recent-first order through private compare-and-swap
state. A host-owned effect closes only after the exact provider success and is
rejected for stale widget generations. Start Menu, AppsFolder, and Steam launch
re-enumerate and require one exact unchanged registration before constrained
Shell/null-argument/numeric-URI activation; raw paths, AUMIDs, Steam AppIds,
arguments, and PIDs never cross IPC. Curated shortcut/AppsFolder icons use the
bounded pixel contract. Steam manifests provide the first evidence-backed Game
classification. Source-aware grouping, additional launcher catalogs, and
richer Steam artwork remain absent.

**Acceptance:**

1. Catalog sources are explicit and bounded; supported AppsFolder and launcher
   adapters retain provider-owned opaque identities and exact revalidation.
2. The default view is a curated, user-controllable library with safe fallback
   access to other applications; curation/order use SavedId plus private state
   to persist across worker restart, and game classification is evidence-backed.
3. Icons/artwork cross a bounded broker/cache contract and cannot become an
   arbitrary package file or URL escape.
4. One successful correlated launch closes the overlay and restores normal app
   focus. Stale ID, provider failure, cancellation, or denial keeps the panel
   open with focus and bounded feedback.
5. Update/churn, duplicates, favorites/order, empty sources, and packaged
   controller/resolution behavior have deterministic coverage.

## GBA-034 — Network rows conflate focus and connection state

**Evidence:** Wi-Fi and Bluetooth share controller row/card patterns, but focus,
candidate selection, radio state, saved/paired state, and authoritative
connection state are different concepts. A focused row can look connected, a
Bluetooth row can imply an unsafe generic Connect action, and refresh churn can
jump focus.

**Implementation evidence:** Focus no longer sets selected/connected state.
Wi-Fi rows distinguish saved/open/password-required, pending, connected, and
failure presentation from the controller target. Bluetooth rows expose Pair
only for an actionable unpaired device and Windows-managed details for paired/
connected or unsupported-ceremony cases; no row claims generic Connect. Wi-Fi
and Bluetooth retain independent stable focus/Scroll IDs across tab switches,
and deterministic tests cover churn and nearest-survivor fallback. Packaged
controller churn and reversible physical pairing remain before closure.

**Acceptance:**

1. Wi-Fi focus, saved/open/password-required state, connecting, connected, and
   failure have distinct text/glyph/state cues; only supported `A` actions run.
2. Bluetooth focus, present, paired, connected, and actionable profile/pairing
   operations remain distinct; informational rows do not imply generic Connect.
3. Wi-Fi and Bluetooth tabs preserve independent last focus and scroll position
   across LB/RB switches and widget reopen.
4. Scan/device/radio churn retains the exact stable item when possible, then a
   deterministic nearest survivor, without moving to a destructive action.
5. Empty, denied, radio-off, hardware-off, stale-ID, and packaged controller/
   accessibility cases pass.

## GBA-035 — Optional audio-provider failure can break the mixer

**Evidence:** Audio Mixer composes master output, sessions, default-device
names, and microphone controls from separate capabilities/providers. A denied,
revoked, or unavailable optional slice must not blank healthy sections or
rebuild focus onto an unrelated master control.

**Acceptance:**

1. Each audio section owns independent loading, healthy-empty, denied, revoked,
   unavailable, and retry state.
2. Master and application controls remain usable when microphone or device-name
   services fail; optional loss never becomes a whole-widget error.
3. Focus stays on the same semantic control when its section survives, moves to
   a deterministic nearest control when it disappears, and never jumps to an
   unrelated `+` action after a mute/slider command.
4. Revocation, endpoint generation changes, session churn, partial recovery,
   and rapid lifecycle transitions are race-tested.
5. Packaged controller testing verifies partial grants and real provider
   failure where safely reproducible.

**Implementation evidence:** Device-name and microphone enrichment now run in
independent, event-driven section workers after the required master/session
snapshot is live. Each section records loading, healthy, healthy-empty, denied,
revoked, or unavailable state; provider details remain sanitized; and an
explicit section retry reopens only that capability stream. Optional loss
clears only data owned by that grant. Master output and application rows keep
their stable IDs and remain actionable, while a disappearing microphone moves
semantic focus to its same-section retry or the nearest surviving session
instead of rebuilding focus at the master control. Focused deterministic tests
cover startup denial, live revocation, unavailable-to-healthy recovery,
endpoint/session churn, retry, and lifecycle generation replacement. The
remaining gate is packaged controller testing with safe real grant revocation.

## GBA-036 — Native client canvas was opaque outside widget surfaces

**Original evidence:** Packaged screenshots showed a rectangular opaque native
client/canvas region extending beyond the intended rounded content surface.
That region masks the dimmed application backdrop and makes content-sized
widgets look like they are embedded in an extra black window. This is distinct
from the intentional full-screen dimming backdrop and is not a theme color
choice.

**Implementation/current evidence:** The native client now clears unused
pixels to the exact layered color key rather than the theme canvas color. The
packaged Release capture after `8e8c90a` shows the live Now Playing surface over
the dimmed application without the former extra client rectangle. That single
standard-viewport capture does not yet cover initial paint, replacement,
compact/wide, reduced-transparency, high-contrast, or DPI changes, so the issue
remains Verifying rather than Closed.

**Acceptance:**

1. Pixels outside the declared shell/widget surface remain transparent so the
   host backdrop is visible; no extra rectangular canvas, inset, or border
   appears during initial paint, resize, widget replacement, or animation.
2. Rounded clips affect only the intended content surface and retain correct
   antialiasing at supported DPI/interface scales.
3. Clear/present/composition paths are alpha-correct and do not rely on a theme
   painting over the defect.
4. Native deterministic coverage plus packaged screenshots exercise compact,
   standard, wide, reduced-transparency, and high-contrast presentations.

## GBA-037 — Now Playing retry did not recover provider failure

**Original evidence:** A packaged run rendered the Now Playing provider-failure
surface. Activating **Try again** left the same failed state with no observable
new load, recovery, or changed bounded diagnostic. A focusable Retry control
that acknowledges input without beginning a real attempt is a functional
failure, not merely missing polish.

**Implementation/current evidence:** Subscription startup and current-state
read are separate failure domains. A failed subscription can still publish a
valid current snapshot, a later live-read failure preserves the last valid
snapshot, and Retry cancels the old attempt and creates one fresh bounded
generation. Pending transport feedback is target-specific: invoking Play/Pause
no longer disables or flashes Previous/Next, while the widget-level single-
flight gate still prevents overlapping commands and clears target Busy state
after both success and failure. Focused media tests cover those paths. The post-`8e8c90a` packaged
capture shows a live GSMTC session, proving the normal provider path on this
machine, but it does not reproduce failure followed by recovery; status remains
Verifying.

**Acceptance:**

1. Retry starts exactly one fresh bounded read/subscription attempt while the
   widget is active, publishes Busy feedback, and cannot overlap itself.
2. A recovered provider replaces the failure surface with the authoritative
   current sessions; healthy empty is distinct from provider unavailable.
3. Continued failure clears Busy, retains focus on Retry, and shows one safe
   actionable status without exposing native details or silently succeeding.
4. Lifecycle cancellation, consent revocation, worker restart, provider loss/
   recovery, and repeated controller activation have deterministic tests plus
   packaged reproduction evidence.

## GBA-038 — Games & Apps eagerly loads catalog and clips text

**Evidence:** The original packaged viewport showed eager Start Menu loading
and clipped card/status copy. The widget now resolves only saved entries at the
root and loads the catalog after **Add applications**. Library and Catalog use
vertical full-width rows: the icon and two-line application name share one
Button focus target instead of outlining an inner label. The native column
allocator preserves measured height for auto-height intrinsic wrapped leaves.
Focused lifecycle/layout regressions are green. The auth-free
`final-schema-v2-20260808-final` pipeline additionally launched the exact
retained package in AppContainer,
recorded that the root reached `games.open-catalog` before the first broad
catalog read, then captured catalog add and populated-library return through
the simulated broker. Six native WIC PNGs cover empty, catalog, and populated
snapshots across compact/default, 150%-text/reduced-transparency, standard,
and wide/high-contrast profiles with production computed styles and recorded
SHA-256s. Loading, failure, long-name, maximum-page, focus traversal, and a
hands-on packaged overlay pass were originally outstanding. Accepted DLV-004
`7e0b83e` now uses shared SectionHeader/StatusBadge/Card/EmptyState/Alert/AppTile/
Toast composition, retains only one 32-row Catalog page with explicit bounded
Previous/Next traversal, revalidates actions against the current route and
stable SavedId-derived element, covers shared state surfaces, a 64-entry
long-name Library, maximum Catalog traversal, reverse focus, and mutation, and
retains compact/standard/150%-accessible/wide-high-contrast captures. Its exact
focused groups pass Games & Apps 42/42, installed generic-worker/AppContainer
6/6, and 52 documentation contracts. Physical packaged shell/controller/display
review remains outstanding, so status stays Verifying.

**Acceptance:**

1. Opening the root restores only the durable curated Library data needed for
   that surface; broad Catalog enumeration begins lazily when the user opens
   Add applications (or through another explicit bounded refresh).
2. Catalog loading, paging, cancellation, and retry do not blank or reorder the
   existing Library and do not run while Background merely because the worker
   is resident.
3. Titles, type/status, counts, help, and prompts fit or reflow at compact/wide
   surfaces and 150% text without clipping or covering focus cues.
4. Vertical focus-follow reaches the first/last complete row and preserves
   separate Library/Catalog focus across nested B return.
5. Automated lifecycle/layout tests and packaged screenshots cover empty,
   populated, loading, failure, long-name, and maximum-page states.

## GBA-039 — Permission descriptions did not reflow or scroll fully

**Original evidence:** Settings permission detail screenshots showed long
capability descriptions running beyond their row/content allocation. Text can
be clipped before the next focus target, and the Scroll extent follows focus
rows rather than guaranteeing the full description is readable.

**Implementation/current evidence:** The native column allocator now preserves
measured intrinsic height for auto-height wrapped leaves, and Settings
permission detail composition no longer forces the old clipped allocation.
Native layout/renderer regressions cover long wrapped leaves and Scroll extent.
A packaged long-description controller traversal across the supported scale
matrix has not yet been captured, so status remains Verifying.

**Acceptance:**

1. Capability name, required/optional decision, and long description use a
   bounded responsive row/detail layout with explicit line wrapping and no
   overlap at supported interface/text scales.
2. Controller Scroll can reveal the complete first and last description as
   well as every decision control; fixed footer prompts never cover content.
3. Focus geometry remains inside the surface when a row grows, and B returns
   one scope without a redundant Back row.
4. Unknown/unsupported/inactive declarations use the same safe layout and
   bounded/truncated accessible strings.
5. Tests cover longest valid/localized copy, all decision states, compact and
   wide viewports, 150% text, high contrast, and packaged controller traversal.

## GBA-040 — Authored text width was applied after intrinsic height measurement

**Original evidence:** Spotify's centered Client-ID state and setup view showed
titles, details, and setup steps vertically clipped inside otherwise spacious
cards. Similar failures repeatedly appeared when a text node declared a
`max-width`: the host measured its height against the wider parent, then
clamped only the resulting width. DirectWrite therefore painted wrapped lines
into a box whose height still described the pre-clamp single-line layout.

**Implementation/current evidence:** The native layout engine now applies an
intrinsic leaf's explicit/max outer width, minus padding, to the measurement
constraint before asking the renderer for line metrics. The auto-height leaf
retains that reflowed height through flex allocation. Exact native regressions
cover the Spotify Client-ID state card and setup card at compact width and at
150% text, in addition to the generic centered-state and permission Scroll
coverage. The full native Release suite is green at milestone `9f1af0b`;
refreshed packaged screenshots remain outstanding, so status remains Verifying.
Spotify package 0.1.6 additionally places the instruction card in a controller
VerticalScroll, uses compact responsive wrapping, and relies on shared centered
button icon/label placement; those changes remove widget-specific spacer/
alignment compensation. A fresh Scroll identity on each setup entry prevents a
retained bottom offset from hiding the title/first step. The auth-free
`final-schema-v2-20260808-final` bundle captured Spotify 0.1.6 Client-ID/
setup surfaces at four viewport/DPI/accessibility profiles with complete,
centered labels and instructions. It does not cover localized copy, the full
packaged shell/window matrix, live OAuth, or callback behavior. Status remains
Verifying.

**Acceptance:**

1. Text and labeled controls with authored `width` or `max-width` measure their
   intrinsic height at the effective content width, including padding.
2. Centered state-card title/detail/action flow does not overlap or clip at
   compact and standard surfaces through 150% text scale.
3. Spotify setup title, instructions, exact redirect URI, command, and Check configuration
   action remain fully readable/reachable without widget-specific spacer or
   margin compensation.
4. Width constraints, flex shrink, explicit fixed height, max-lines, Scroll,
   and focus visibility retain deterministic native regression coverage.
5. Packaged Release screenshots confirm Client-ID, setup, long/localized copy,
   and supported DPI/interface/text scale combinations.

## GBA-041 — Foreground-only controller polling could suspend a visible overlay

**Original evidence:** The packaged overlay intermittently stopped responding
to controller navigation while still visible and did not reliably take priority
over the application behind it. Diagnostics showed foreground-acquisition
failures followed by deliberate polling suspension. A no-activate desktop
overlay cannot treat transient Win32 foreground ownership as its sole
navigation lease.

**Implementation/current evidence:** Showing the overlay now creates one
explicit visible-controller lease. GameInput combines background input for
reliable navigation with foreground-exclusive arbitration whenever Windows
confirms foreground ownership. Show performs at most one bounded activation
attempt; polling no longer retries focus stealing or suspends merely because
activation was denied. Hiding ends the lease, and external foreground activation
still closes the overlay. Native ownership tests are green. Desktop APIs still
cannot universally suppress separate XInput, Raw Input, HID, Steam Input, or
virtual-controller delivery, so packaged game-by-game evidence remains open.

**Acceptance:**

1. Navigation remains responsive for the complete visible lifetime, including
   when Windows denies activation or briefly reports another owner.
2. Confirmed foreground uses GameInput exclusive arbitration; denied activation
   uses the documented background-shared path without focus-steal loops.
3. Alt+Tab/external activation closes the overlay and ends controller reads.
4. Diagnostics distinguish foreground-exclusive, background-shared, hidden,
   and unavailable paths without logging controller data.
5. Packaged trials document any input backend needing a future opt-in
   interception layer.

## GBA-042 — Spotify public configuration and permission metadata diverged

**Original evidence:** `gbar config set` stored the Spotify Client ID under the
manifest publisher, while the unsigned installed worker ran under its sealed
content-digest authority. Reloading therefore remained on **Client ID
required**. Settings also rendered all four supported Spotify grants as
**Unsupported capability** because their display metadata was missing.

**Implementation/current evidence:** Non-secret configuration resolves an
exact runtime-authority document first, then permits an unsigned authority to
read one unambiguous declared-publisher document whose namespace owns the
package ID. Ambiguous matches fail closed. Consent, private state, OAuth tokens,
and credentials retain exact digest authority. Spotify Setup **Check configuration** performs
a serialized bounded fresh configuration read without starting OAuth, and
Settings has names/descriptions for all four Spotify capabilities. Focused
configuration, provider, widget, and Settings tests are green. The auth-free
`final-schema-v2-20260808-final` run launched the retained Spotify 0.1.6
package through AppContainer,
captured its unconfigured/setup states and semantic action trace, rendered six
native profile PNGs with production styles, and recorded package/snapshot/PNG
digests. Settings could not start through the same generic worker because its
host-owned settings/catalog services are not injectable there; the run records
that gap. It therefore proves neither Settings permission rendering nor live
configure/connect/revoke. Status remains Verifying.

**Acceptance:**

1. The source-tree CLI command runs without a PATH install, and a successful
   write becomes visible after **Check configuration** without restarting the bridge.
2. Only one owning declared-publisher document can resolve; ambiguous or
   unrelated documents do not cross package boundaries.
3. Client secrets and OAuth tokens remain outside public configuration.
4. Settings shows accurate names, descriptions, required/optional state, and
   decisions for all Spotify capabilities.
5. Packaged testing covers configure, digest update, configuration check, connect,
   revoke, and malformed/ambiguous recovery.

## GBA-043 — Browser activation canceled Spotify authorization

**Original evidence:** Connect opened the system browser, which moved the
overlay/widget out of Interactive. The broker treated that lifecycle transition
like an ordinary inactive control and canceled the temporary loopback listener.
Even without a transition, the pipe's ordinary three-second request deadline
was shorter than the authorization callback window, producing
`ERR_CONNECTION_REFUSED` when Spotify returned.

**Implementation/current evidence:** Package 0.1.7 acknowledges an explicit
Interactive Connect action immediately and runs only its authorization task on
the widget's Created-to-Destroying lifetime. The already-created broker lease
therefore survives browser-triggered Visible/Background. New connect/control
requests in Background remain denied; disconnect has no continuation. The
temporary callback listener is created only for the explicit action and waits
at most fifteen minutes. The exact broker operation has a seventeen-minute deadline,
leaving two bounded minutes for token exchange, retry/backoff, and credential-
vault persistence. The package selects `keep-alive` residency so idle unload
cannot destroy this already-started authorization while the browser owns
foreground; active polling/presentation still follows lifecycle tokens. The
receiver tolerates at most 16 malformed or early-close local probes inside the
same listener window, while still requiring loopback origin, exact host/path,
GET/HTTP/1.1, and matching OAuth state before accepting the callback.
Destroying, consent revocation, caller/pipe cancellation, and callback/deadline
timeout still cancel and close the listener.
The refusal session's host log records Spotify A at `08:23:13.620`, browser
foreground at `08:23:14.050`, and later browser foreground at `08:30:45.100`:
451.48 seconds after the initial action, 151.48 seconds beyond the former
five-minute listener and 31.48 seconds beyond the former seven-minute broker
deadline. That local timing motivated the fifteen/seventeen-minute human-flow
bounds; it is not a successful packaged callback claim.
PlatformBroker's focused Release suite passes with exact-operation,
Background denial, disconnect, Destroying, revocation, cancellation, and
timeout-policy coverage. Widget coverage asserts immediate action completion,
Background continuation, no implicit listener/connect, and Destroying
cancellation. Status remains Verifying until the packaged allowlisted-account
callback evidence in acceptance item 5 is captured.

**Acceptance:**

1. Connect must start from an explicit Interactive action, acknowledge that
   action immediately, and allow only its already-created widget-lifetime task
   to survive browser-triggered Visible/Background.
2. New inactive connect/playback controls and disconnect remain denied.
3. The temporary listener exists only during explicit Connect and waits at most
   fifteen minutes. A bounded number of speculative/malformed local probes cannot
   consume the real callback, and none bypass exact origin/host/path/state
   validation. Only the exact broker Connect receives a seventeen-minute deadline;
   its remaining two minutes are bounded token exchange/retry/vault budget, not
   a general long request timeout.
4. Destroying, revoke/consent loss, caller/pipe cancellation, malformed state,
   and timeout always terminate the listener and request.
5. A packaged allowlisted-account test completes the exact callback and proves
   there is no refusal, token leakage, or hidden retry/polling loop.

## GBA-044 — XInput Guide compatibility keeps a hidden timer active

**Evidence:** Bounded local baseline
`overlay-performance-20260808-201124310-6e6c053e` established an explicit
Hidden state for the Settings reference without inheriting user overlay state.
Across its 29.791551-second post-warmup native interval, schema 2 directly
recorded 943 total timer messages and 943 Guide-compatibility timer messages
(31.65327/s), with zero ordinary controller-timer messages, paint messages, or
successful Direct2D frames. That cadence is `kGuideCompatibilityTimer`,
configured at 25 ms whenever the
quarantined `xinput1_4.dll` ordinal-100 adapter initializes. GameInput Guide is
callback-driven; this compatibility path is not.

The short local run is not OS scheduler-wakeup or CPU-budget evidence. It does,
however, disprove the broader claim that Hidden owns no recurring host timer.
Runtime-record schema 2 now reports the compatibility timer directly, separate
from total and ordinary visible-controller timers, so subsequent evidence does
not depend on subtraction.

**Implementation evidence:** The unchanged 25 ms sampled-state adapter is now
owned by an event-driven GameInput device-presence policy. It starts only while
at least one exact `GameInputFamilyXbox360` device is connected and stops after
the last such device leaves. Duplicate arrival/removal notifications and
multiple legacy devices are idempotent. Modern/no-controller systems retain
callback-driven GameInput Guide with no compatibility timer. If device callback
registration itself fails, the host deliberately preserves the previous
always-on compatibility path rather than silently risking Guide loss. The full
native Debug suite passes, including 15 Guide policy checks and callback cleanup.

This avoids the unsafe alternative of lengthening a sampled-state interval,
which could miss a short press. Release relink and a fresh schema-2 hidden
baseline follow in the integration gate.

**Acceptance:**

1. Hidden runtime evidence reports Guide-compatibility timer messages directly,
   separately from ordinary visible-controller and other host timers.
2. A replacement eliminates or materially reduces continuous Hidden cadence
   without polling ordinary controls, animating, or presenting.
3. Supported GameInput callback behavior remains unchanged.
4. Xbox-360-class and affected 8BitDo hardware trials include very short Guide
   taps and prove no practical regression in open/close detection.
5. Repeated clean-machine Hidden baselines include CPU, timer cadence, and
   scheduler-wakeup/context-switch evidence before this issue closes.

## GBA-048 — Widget authors repeatedly hand-roll state locks and invalidation

**Evidence:** Spotify, Audio Mixer, Network Controls, Games & Apps, Media
Sessions, and YT Music each contain overlapping state locks, equality checks,
snapshot copies, and manual `Invalidate()` calls. Those mechanics make safe
state changes longer than the domain behavior they protect.

**Implementation evidence:** `Widget.CreateModel<TState>` constructs a public
`WidgetModel<TState>` with atomic `Value`/`Snapshot` reads, serialized `Set` and
`Update`, a monotonic model revision, configurable equality, exactly one
invalidation for a changed value, and none for an equal value. Its
result-bearing update derives command input from the same committed transition.
`Changed` observers run outside the model lock, failures are contained, and
Destroying suppresses subsequent render invalidation. Focused Release tests
cover equality, concurrency, result derivation, observer containment, null
rejection, and lifecycle behavior. Media Sessions now keeps all render-facing
state in one model. Its selection regression proves one invalidation for a real
selection change and none for the repeated equal selection, while its existing
lifecycle, command-admission, and stale-generation tests remain intact. This
satisfies the medium-production-widget acceptance item. The widget still owns
its domain projection and reconciliation policy, but its transport execution now
uses the public optimistic coordinator tracked by GBA-051.

**Acceptance:**

1. A record-based widget needs no author-created state lock or manual
   invalidation for ordinary immutable transitions.
2. Concurrent updates cannot lose state or publish a torn value/revision pair.
3. Equal replacements cannot create render storms.
4. At least one medium production widget migrates without weakening its
   lifecycle, command, or stale-result guarantees.

## GBA-049 — YT Music repeats stable metadata work and cannot show Repeat One

**Evidence:** Every two-second refresh previously requested both `/track/state`
and `/track`, producing about 60 loopback calls per minute during a stable song.
Parallel responses could also represent different sides of a track transition.
The closed icon vocabulary exposed only `Repeat`, so repeat-all and repeat-one
depended on copy/classes rather than distinct visible geometry.

**Implementation evidence:** The Community addon now requests state first,
reuses complete immutable metadata for the same nonempty track ID for at most
five minutes, and immediately refreshes metadata on identity change, missing
data, or expiry. Stable playback therefore uses about 30 requests per minute
while transition state and metadata remain coherent. Protocol v12 adds the
closed `RepeatOne` glyph across managed validation/serialization, native bridge
parsing, vector rendering, and YT Music. Focused suites pass 45 YT Music tests,
80 SDK tests, and native icon rendering is part of the native integration gate.

**Acceptance:**

1. Stable same-track polling does not reread metadata before bounded expiry.
2. Track changes and incomplete metadata refresh immediately.
3. Repeat-one has visible, accessible, non-color-only state.
4. A packaged real-companion controller run verifies metadata transitions,
   repeat cycling, and compact/150%-text rendering.

## GBA-050 — YT Music play/pause reconciliation ended with the action request

**Evidence:** Play/pause previously took the immediate refresh path tied to the
input action's cancellation token, while Previous/Next used an asynchronous
transport refresh burst. Once the host completed the action request, the
accepted play/pause reconciliation could therefore be canceled before the
companion published its authoritative state. The refresh completion check also
inspected the rendered optimistic snapshot, allowing the widget's own projected
playback value to look like companion confirmation.

**Implementation evidence:** Toggle Playback now uses the same generation-
superseding transport reconciliation path as Previous/Next. Once the companion
accepts the command, its bounded refresh burst is linked to widget lifetime
rather than the completed action request. Resolution checks the pending
optimistic feature, not the merged render snapshot, so a stale authoritative
poll cannot confirm the widget's own projection. A repeated play/pause action
starts promptly, supersedes the older burst, and preserves command order.
Focused regressions cover repeated toggles, action-request cancellation, and a
stale snapshot followed by confirmation. YT Music still owns this specialized
confirmation-burst logic and has not migrated it to the general coordinator;
GBA-051 does not by itself replace companion-specific confirmation policy.
DLV-009 `08d44db` moves that domain-specific burst into the SDK-owned Active
Latest lane, so supersession and deactivation cancel and drain it without a
widget task/CTS registry. Current-attempt checks under the presentation-state
lock reject cancellation-ignoring stale ordinary and authorization failures;
the companion-specific confirmation/rollback policy correctly remains authored.

**Acceptance:**

1. An accepted play/pause command continues bounded reconciliation after its
   transient input request completes, but stops on a newer transport generation
   or widget-lifecycle cancellation.
2. A stale companion snapshot cannot clear the pending optimistic playback
   intent or masquerade as authoritative confirmation.
3. Repeated play/pause input is not blocked behind the previous refresh burst
   and preserves command order.
4. A packaged real-YTMDesktop2 controller run verifies confirmation, bounded
   expiry/rollback, rapid repeated input, and lifecycle exit.

## GBA-051 — Widget authors hand-roll optimistic command coordination

**Evidence:** Media, audio, network, and companion widgets independently
implemented admission, pending projection, provider execution, stale-result
rejection, cancellation, safe errors, and rollback. Even with immutable models
and operation lanes, authors still had to connect those primitives correctly
for every mutating feature.

**Implementation evidence:** `Widget.CreateOptimisticCommand` constructs a
public `WidgetOptimisticCommand<TState,TRequest,TExecution,TResult>` over one
`WidgetModel<TState>` and the runtime `WidgetOperations` coordinator. Its
SingleFlight, Latest, and Serial policies join, replace, or enqueue through the
existing bounded operation lanes. `Apply` derives optimistic state and exact
provider input from one serialized model revision. `Reconcile`, `Rollback`, and
optional `Fail` receive the current state so authored merges can preserve
unrelated provider events; Latest retains the first baseline across a
replacement chain. Active, State, or Widget lifetime cancellation and draining
remain runtime-owned, non-current completions cannot commit, and rejected or
joined requests do not project state.

`WidgetCommandError` validates a stable code and at most 256 visible message
characters. A throwing/null mapper falls back to the bounded generic error.
Mutations execute once with no automatic retry. Focused tests cover inactive
rejection, Latest replacement with an intervening provider event,
SingleFlight joining, Serial projection order, failure/mapper fallback, and
lifecycle rollback/draining.

Media Sessions is the first production migration. Its SingleFlight transport
command immediately projects Play/Pause, keeps sibling transport presentation
stable, maps capability failures to safe copy, and rolls back only the affected
session when its snapshot generation/revision still permits it.

**Acceptance:**

1. Apply derives optimistic model state and provider execution input atomically
   from the same committed transition.
2. SingleFlight, Latest, and Serial have deterministic projection/admission
   semantics; inactive/capacity rejection and joining never mutate state.
3. Reconcile, rollback, and failure callbacks receive current state so an
   authored feature-local merge preserves unrelated provider updates;
   replacement and lifecycle cancellation cannot let a stale attempt commit.
4. Errors remain bounded and presentation-safe, mapper failure has a safe
   fallback, and mutating calls are never automatically retried.
5. A medium production widget migrates with immediate optimistic feedback,
   correct rollback, stable sibling controls, and packaged controller evidence.

## GBA-052 — Games & Apps lifecycle cleanup could dispose a live worker token

**Evidence:** The full Release verifier intermittently failed the installed
generic-worker route while Games & Apps entered a new lifecycle state. The
worker surfaced `The CancellationTokenSource has been disposed` during
`SetLifecycleStateAsync`. A focused rerun could pass, making this a lifecycle
ordering race rather than a deterministic provider failure.

**Implementation evidence:** Initial and Retry library reads now use a named
runtime-owned Active `WidgetOperations.RunLatest` lane. Deactivation cancels
and drains that lane instead of disposing an author-owned run source. Toast
expiry is the sole disposer of its linked source and clears the shared source
reference under the widget state lock before disposal, so replacement or
deactivation cannot call `Cancel` on an already-disposed source. The focused
Games & Apps suite passes 27 tests, including leaving during a retry, and the
real generic-worker/AppContainer conformance suite passed three consecutive
runs.

**Acceptance:**

1. Leaving Games & Apps during initial or Retry loading cancels and drains the
   provider operation before the Active lifecycle transition completes.
2. Toast replacement, expiry, and deactivation have exactly one disposal owner
   and cannot throw `ObjectDisposedException`.
3. Focused widget coverage and repeated installed generic-worker conformance
   remain green.
4. Packaged rapid widget cycling/retry testing records no worker lifecycle
   failure or leaked process.

## GBA-053 — Widget authors hand-roll non-paged resource coordination

**Evidence:** Single current-value reads still required authors to coordinate
loading/refreshing/error snapshots, freshness caching, duplicate calls,
last-good retention, subscription races, reset, lifecycle cancellation, and
late completion independently. The offset-paged resource was deliberately the
wrong abstraction for this common case.

**Implementation evidence:** `Widget.CreateResource<TValue>` constructs a
public `WidgetResource<TValue>` over the bounded runtime operation coordinator.
Its immutable snapshot publishes `NotLoaded`, `Loading`, `Ready`, `Refreshing`,
or `Error`; `EnsureLoaded` reuses a bounded fresh value and joins an identical
in-flight read; `Refresh`/`Retry`, `Publish`, and `Reset` have explicit
semantics. Publication cancels an older read so it cannot overwrite an
authoritative event. Errors are mapped to bounded `WidgetResourceError`,
last-good retention and lifetime are explicit, and no read starts from render
or through implicit polling/retry. The focused Widget SDK suite passes 80/80.

**Acceptance:**

1. A current-value resource needs no author-owned task, semaphore, generation,
   cache timestamp, or manual invalidation.
2. Duplicate loads coalesce; reset, subscription publication, replacement, and
   lifecycle exit reject stale completion deterministically.
3. Cache, last-good, safe error, retry, reset, and inactive behavior have
   focused coverage without sleeps.
4. At least one suitable production widget migrates and packaged lifecycle/
   recovery behavior is recorded before closure.

## GBA-054 — Multipage widgets hand-roll routes, IDs, Back, and focus restoration

**Evidence:** Advanced widgets repeated route/page fields, modal flags, scope
strings, return-focus fields, Back dispatch, route cancellation, and
provider-derived ID concatenation. Open actions did not uniformly expose their
resolved active scope, encouraging route guesses from source IDs.

**Implementation evidence:** `Widget.CreateNavigator<TRoute>` constructs a
bounded `WidgetNavigator<TRoute>` with stable generated route scopes,
root/nested navigation, exact pressed-B routing, per-route/parent return focus,
and a route cancellation token canceled before invalidation. All standard
open-widget A, focused/root shortcut, and Slider actions now propagate the
active `InputScopeId`. `WidgetIds.Scope` supplies validated hierarchical IDs;
`KeyedId` hashes bounded durable keys into deterministic opaque leaves. SDK
Gallery has migrated to these public contracts without host-only helpers. Its
6/6 focused tests cover page composition, route cancellation, exact nested B,
focus restoration, package parity, and GBSS. Widget SDK passes 80/80.

**Acceptance:**

1. One navigator owns a bounded root/nested stack, stable route scopes,
   route-owned cancellation, exact nested B, and deterministic return focus.
2. Every standard open-widget action carries the exact active scope; a stale
   nested-scope action fails closed.
3. Hierarchical and durable-key IDs are validated, deterministic, bounded, and
   never expose the durable provider key in a snapshot.
4. SDK Gallery remains a capability-free generic-worker migration with focused
   tests, and at least one advanced production widget plus packaged controller
   evidence follows before closure.
5. Responsive navigation-shell recipes and analyzer diagnostics remain tracked
   as roadmap work rather than being implied by this primitive.

## GBA-055 — YT Music could expose provider or exception details in status UI

**Evidence:** A local companion can return arbitrary response text, and unknown
runtime/provider exceptions can contain implementation details. Rendering
either directly would make Community-addon status unpredictable and could leak
local data into UI, logs, screenshots, or issue reports.

**Implementation evidence:** YT Music package 0.2.6 uses typed
`YtMusicServiceException` containing only the HTTP status code. Non-success
loopback bodies are not retained. The widget maps known capability codes and
status classes to bounded authored copy and maps every unknown exception to one
generic message; it never appends `Exception.Message` or response JSON. The
focused YT Music suite passes 51/51, including safe typed error regressions,
lifecycle-owned transport-reconciliation cancellation/draining, stale
ordinary/authorization failure rejection after supersession or lifecycle exit,
and cancellation-ignoring pairing and polling completion after deactivation.

**Acceptance:**

1. Non-success companion response bodies never enter an exception retained by
   the addon and never reach rendered state.
2. Known status/capability failures map to bounded actionable copy; unknown
   exception types and messages map to one generic safe status.
3. Focused tests prove status-only mapping, hostile-body exclusion, and unknown
   exception-text exclusion.
4. A packaged real-companion run records representative authorization, 4xx,
   5xx, malformed-response, timeout, and unavailable failures before closure.

## GBA-056 — Spotify seek navigation does not enter the menu to its left

**Evidence:** In the expanded Spotify player, pressing Left while the inactive
seek Slider owns focus moves to Previous track below it. The selected navigation
rail is the visually and structurally intended destination to the left.

**Implementation evidence:** Accepted DLV-051 authors the inactive seek Slider's
Left edge to the stable selected `spotify.nav.wide.*` rail destination and to
`spotify.nav.compact.player` in compact Player. One credential-free test inspects
both emitted responsive branches and replays the single Left input across every
wide destination plus a current-route refresh while preserving seek Down and
Previous/Play/Next edges. Spotify passes 40/40, documentation validates across
53 files, and the normal 0.2.10 package validates/packs. The native resolver has
no Spotify special case. DLV-006/DLV-022 separately own continuous playlist
collection and header/list traversal.

**Acceptance:** Expanded Left enters the selected rail destination; compact
mode uses its corresponding navigation tab; Slider adjustment mode still owns
Left/Right after activation; disabled/busy and responsive changes retain a
valid target; controller tests exercise both explicit and geometric fallbacks.

## GBA-057 — Replacement-page loading makes continuous lists jump focus

**Evidence:** Forward and reverse traversal in auto-loading lists visibly moves
the cursor from the last row to the first row, or the first to the last, when a
new page replaces the current 12-row window. Existing tests assert that exact
replacement behavior, so this is a design gap rather than an untested edge.

**Ownership:** DLV-006's shared SDK/protocol/native cursor-append collection.
Individual widgets must not hide it with duplicate page caches or ordinal
focus hacks.

**Initial candidate review:** DLV-022 candidate `c349bbd` was not accepted. It derives
every media collection/action/focus key solely from the track or episode URI,
while Spotify may return the same URI in multiple queue or playlist positions.
The shared resource correctly rejects duplicate keys within a page and across
retained pages, so the candidate would replace the list with an error. DLV-053
must add bounded collection-context occurrence identity without weakening the
generic invariant or replacing semantic identity with a title/global ordinal.

**Current correction evidence:** DLV-053 commit `7f5c2fd` is accepted at code
level. A private occurrence policy retains URI as semantic identity, creates
unique same-page and retained cross-page action/focus keys, preserves unique
keys across offset changes, matches distinguishable duplicates through refresh
churn, bounds all matcher state to the retained window, clears it on Reset, and
rejects superseded completion before state mutation. Spotify 45/45,
installed-worker conformance 6/6, 53 documentation files, and package validation
pass. DLV-054 closed the preceding artwork correction and the coherent prefix is
integrated through `8c1bbdf`. Accepted DLV-055 `efffa53` publishes, installs,
selects, and enables the changed source as immutable Spotify `0.2.12`; the
coherent Release is visibly running for live traversal.

**Acceptance:** A keyed item and viewport anchor remain continuous across
forward/reverse loads, sparse results, cache hits/eviction, refresh, insertion,
deletion, cancellation, and final pages; focus never wraps merely because a
transport page changed; retained items and artwork remain bounded.

## GBA-058 — Spotify Playlists clips its Library header

**Evidence:** The supplied packaged screenshot shows `LIBRARY` vertically
clipped above the playlists content. Spotify currently overrides shared
SectionHeader content height, while shared intrinsic measurement and trailing
actions also participate in allocation.

**Ownership:** Accepted DLV-021 (`b714efe`, integrated by `bc2de86`) corrects
shared SectionHeader measurement/paint and proves complete bounds through the
production renderer. DLV-022 still verifies the composed live Spotify route
while migrating its continuous list/focus behavior.

**Acceptance:** Eyebrow, title, description, and optional Play/trailing action
fit their measured content at compact/standard/wide and 100-150% scales, remain
unclipped during responsive changes, and expose matching semantic bounds.

**Current evidence:** NativeTextLayout 25, DeclarativeLayout 250,
DeclarativeRenderer 4,769, and shared component geometry 589 pass. Exact Spotify
fixtures retain complete eyebrow/title/description bounds at compact, standard,
wide/150%, and combined high-contrast/reduced-transparency profiles. The issue
remains Verifying until the freshly built packaged overlay confirms the user's
original live surface.

## GBA-059 — Games & Apps lacks trusted artwork for saved games

**Evidence:** The supplied Games & Apps screenshot shows a large Play fallback
for `007 First Light`. DLV-018 projects bounded opaque handles for current
resolved registrations and lazily rasterizes trusted Start Menu/AppsFolder Shell
icons; Steam has no reviewed trusted local artwork source and remains a semantic
fallback. Independent review found provider I/O serialized the native bridge and
that handle/cache identity omitted the registration revalidation revision.
Accepted DLV-054 commit `546f014`, integrated through `8c1bbdf`, acknowledges
demand before provider I/O, admits completion only for the current worker and
artwork session, rotates handles from a host-only exact revalidation digest, and
evicts the old per-row decoded and render bitmap.

**Ownership:** The trusted source adapters, DLV-006 lazy-artwork contract,
DLV-054's exact-generation/revision registry, and a bounded native artwork
request owner that does not block the UI. This is not a widget-authored URL/file
escape or permission to redesign the whole bridge.

**Current assignment:** The corrected dependent prefix is accepted and integrated
through `c6d76a3`. DLV-098 leaves catalog
enumeration artwork-I/O-free, drains artwork/scan/observation lanes before any
source teardown, retains state on bounded terminal failure, and limits the
active locator map to 4,096 current objects. It is not accepted because
`RegisterCatalog` retires the current map during enumeration, before the source
base performs its final cancellation/latest-generation check and commits the
candidate snapshot. A canceled or losing refresh can therefore invalidate
handles in the still-authoritative catalog. Accepted DLV-099 stages candidate locators
and promotes/retires them only with the accepted source generation. No
correction may fetch from Steam, expose app IDs/paths, scan unrelated image
trees, or weaken semantic fallback/stale-handle behavior.

**Acceptance:** Supported sources resolve bounded artwork lazily through opaque
handles, validate identity/format/dimensions/bytes, cap decode/cache/transport
cost, reject stale generation and same-identity changed assets, and use an
honest semantic fallback only when the exact registration has no trusted
artwork. A provider stall or failure cannot delay input, switching, event
pumping, lifecycle work, or overlay shutdown. Focused provider/broker/bridge/
native evidence is green and the coherent main Release rebuilt successfully;
PID 25956 is visibly running for packaged live confirmation.

## GBA-060 — Shared button content remains visibly misaligned

**Evidence:** The supplied Now Playing screenshot and hands-on reports show
text, leading icon, and trailing checkmark/busy content with inconsistent
vertical/optical alignment across multiple first-party widgets even after
DLV-003's focused Button assertions passed.

**Ownership:** Native declarative measurement/paint plus shared component
styles. The new evidence reopens packaged product acceptance without rejecting
DLV-003's narrower automated result.

**Acceptance:** One measured geometry model covers Buttons, action tiles,
icon-label-checkmark/busy variants, wrapped/ellipsized labels, disabled/
selected/focused states, and every supported scale; Games & Apps, Spotify, Now
Playing, Settings, and SDK Gallery require no local pixel offsets.

**Current evidence:** Accepted DLV-021 (`b714efe`, integrated by `bc2de86`)
introduces one NativeTextLayout measurement/paint plan and exact shared-component
fixtures for all five named products across four profile classes. Selected,
busy, disabled, icon, label, trailing cue, wrapped text, and real DirectWrite
paint assertions pass. Keep Verifying until the user checks the new packaged
Release visually.

## GBA-061 — Spotify randomly falls back to a full load-error screen

**Evidence:** The packaged Spotify addon intermittently shows `Spotify could
not be loaded`; refreshing the addon restores normal operation. Current polling
maps an unknown/transient exception to the global Error state even when a
last-good playback/route snapshot exists.

**Ownership:** Spotify refresh/polling state and typed provider-failure policy
first. DLV-023 must preserve fatal permission/configuration/authorization
semantics and escalate if retained diagnostics prove a provider or worker
crash.

**Acceptance:** Recoverable faults retain the last-good usable surface with
bounded warning/backoff; refresh and automatic recovery share one policy;
fatal typed states remain explicit; error copy is safe; lifecycle exit cancels
retry; repeated forced sequences never restart the worker or lose route/focus.

**Implementation evidence:** DLV-023 commit `3cfdd27`, integrated by
`4dc1bd5`, adds one widget-private typed failure policy and a warning carried in
the immutable presentation revision. Focused Release coverage passes Spotify
39/39 on the clean closing commit, Widget SDK 84/84, installed-worker
conformance 6/6, and 52 documentation contracts. Forced transient failures
retain route, focus, playback, and playlist state through 5/15/30-second
bounded backoff; fatal permission/authorization/configuration states remain
explicit; recovery and Active-lifetime stale-result rejection are covered.
Keep this issue Verifying until hands-on live Spotify use shows that the
reported random full-screen replacement no longer recurs.

## GBA-062 — Audio Mixer lacks icon-tray master controls

**Evidence:** Accepted DLV-019 commit `6afd60b` publishes LB/RB/X master-output
actions with current value/state labels, reuses the existing coalesced command
and authoritative reconciliation owner, and preserves open-widget Slider
behavior. Retained run `20260811T003239Z-60b7c837` passes Audio Mixer 45/45,
Platform Broker 51/51, the installed AppContainer worker/bridge/broker route
6/6, and documentation across 52 files. The production route proves LB changes
72% to 67%, RB restores 72%, X mutes, and reopening exposes the reconciled
state.

**Ownership:** Accepted DLV-019 owns the widget/worker/broker slice and
authorizes only current default-output set-volume/set-mute operations for the
exact visible snapshot gesture.

**Acceptance:** LB/RB adjust master volume by a documented clamped step and X
toggles master mute; labels and current state are visible; rapid input
coalesces and reconciles; stale/replayed/expired/wrong-widget/Background/
permission-denied actions fail closed; session, microphone, and device controls
receive no new dashboard authority.

**Disposition:** Automated and packaged-route acceptance is complete at
`6afd60b`. Keep this issue Verifying until the freshly launched Release overlay
passes physical-controller checks for step direction, rapid input, mute,
failure feedback, and open-widget reconciliation.

## GBA-063 — Games & Apps cold start cannot show persisted rows immediately

**Evidence:** Accepted DLV-017 commits `24a8944` and `b844fd8`, integrated as
`5aedfe8`, add schema-v3 display-only rows containing bounded SavedId, sanitized
name prefix, and closed kind. A fresh worker renders those rows disabled as
**Checking…** before delayed provider resolution; current exact AppIds replace
them without changing SavedId-derived order or focus. Active-lifetime changes
and failed refreshes revoke cached AppIds, while Background rejects a
cancellation-ignoring late provider result. Unsupported or semantically invalid
pre-release state resets as a whole before authoritative reconciliation.

**Ownership:** Games & Apps private state and lifecycle reconciliation. Launch
authority remains host/provider-owned and must never be persisted.

**Acceptance:** A fresh worker renders sanitized persisted rows/order/selection
before a delayed provider returns, clearly marks them checking/non-launchable,
then atomically enables exact resolved rows and merges automatic changes without
focus churn; failure retains last-good display; state remains bounded and
contains no AppId/path/AUMID/command/provider identity.

**Disposition:** Automated acceptance is complete: Widget SDK passes 84/84,
worker host 9/9, Games & Apps 49/49, and 52 documentation files validate.
Packaged cold-start first-paint timing plus physical controller/display proof
remain, so the issue is Verifying rather than closed.

## GBA-064 — Reverse Spotify list traversal oscillates at the header boundary

**Evidence:** When scrolling upward through a Spotify playlist, focus repeatedly
jumps between the header Play action and the first list row. The current fixed
header sits outside a replacement-page Scroll, so snapshot replacement,
requested page-entry focus, geometric navigation, and focus memory can compete.

**Ownership:** DLV-006 provides continuous keyed collection focus; DLV-022 owns
Spotify's explicit header/list graph and composed host proof. The host must stay
generic.

**Initial candidate review:** DLV-022 candidate `c349bbd` gives the first row Up to Play
and Play Down to the first row for ordinary lists, but when there is exactly one
row it also sets that row's Down target to itself. DLV-053 must remove the self
edge and retain deterministic non-oscillating header traversal before the
candidate prefix can integrate.

**Current correction evidence:** Accepted DLV-053 commit `7f5c2fd`
keeps Play Down to the first row and row Up to Play, but emits no Down edge for
an exactly one-row playlist. The exact graph case and the complete prior
continuous-list matrix pass within Spotify 45/45. The correction is integrated
through `8c1bbdf`; accepted DLV-055 installs and selects that source as Spotify
`0.2.12`, and the coherent Release is visibly running for live verification.

**Acceptance:** Up from the first retained row reaches Play/header once only
when spatially intended; Down returns predictably; reverse loading cannot steal
focus back; Back, refresh, compact/expanded changes, sparse pages, late
completion, and cache transitions preserve one stable keyed target.

## GBA-065 — Removing one saved application can hide the rest of the Library

**Evidence:** In the accepted DLV-017 build, opening Add applications, removing
one entry marked Saved, and returning to Library can leave every other saved
entry absent. The production widget maintains separate catalog-page items,
Library projection, durable SavedIds/provenance/exclusions, display rows, and
current-lifetime resolved AppIds; a mutation or state-application path can
therefore produce a visually empty Library even when durable intent should
retain the other identities. Current automated DLV-017 evidence did not cover
this exact catalog-remove/Back/product sequence.

**Ownership:** Games & Apps managed mutation and private-state projection.
DLV-024 must reproduce the complete action route before deciding whether the
fault is persistence, in-memory projection, or both. It must not weaken opaque
launch authority or add legacy-schema compatibility.

**Acceptance:** Removing one row changes exactly its membership and, for an
automatic Game, its exclusion/provenance state. All unrelated rows, order,
display projection, selection fallback, and current resolved admissions survive
Back, invalidation, restart, delayed/failed reconciliation, and CAS conflict.

## GBA-066 — Games & Apps Library density and Add action are unstable

**Evidence:** The accepted Library surface requests 820x430 DIPs and shows too
few normal rows on available desktop space. The Add applications action also
disappears and reappears while Library state changes, instead of remaining one
stable reachable action on the last-good Ready tree.

**Ownership:** Games & Apps managed surface hints and presentation continuity in
DLV-024. If evidence instead proves a host sizing/transition defect, stop and
route that narrow finding to the platform lane rather than adding a widget
workaround.

**Acceptance:** The preferred standard surface shows at least two more normal
rows than the 430-DIP baseline when safe area permits; compact and 150% layouts
remain bounded and scrollable. Every Ready Library snapshot contains exactly
one stable Add applications action through refresh, persistence, and background
reconciliation.

## GBA-068 — Community addon workers fail after a latest-Release relaunch

**Evidence:** In the visibly launched accepted Release at main `a5644a7`, the
2026-08-10 21:52-21:54 live log repeatedly records both
`org.gbar.samples.ytmusic` and `org.gbar.samples.spotify` exiting with code 2
before connecting, followed by connection failures. The YT Music path then
requests a snapshot even though its Visible lifecycle transition failed, so the
user sees the secondary and misleading `A hidden suspended widget has no cached
snapshot` message. The installed Spotify `0.2.10` payload is 90,112 bytes with
SHA-256 `99B5EE95...6658`, while the current same-version package artifact is
105,984 bytes with SHA-256 `13AA204E...AFA4`. Installed YT Music is manifest
`0.2.5` while source declares `0.2.6`. Rebuilding only OverlayHost therefore did
not put the current community-addon product state under test.

**Ownership:** DLV-052 is a platform-led serialized release/deployment and
lifecycle-recovery correction. It must first prove the exact safe worker-load
failure code and current-package install result, then correct the narrow owner.
It must not add a permissive same-version public update rule, weaken package
integrity/AppContainer admission, or change public Widget SDK/protocol behavior
while DLV-006 is active.

**Acceptance:** A clean bounded build/stage/validate/pack/install/select flow
produces current uniquely versioned Spotify and YT Music packages and launches
both through the generic installed AppContainer path. Relaunching the latest
accepted overlay never silently leaves an older same-version payload selected.
If lifecycle establishment fails, the host does not issue a contradictory
hidden snapshot request or replace the retained surface with a secondary cache
error; it presents one safe actionable failure and a fresh retry can establish
one new worker generation. Safe diagnostics retain the exact bounded load code
without paths, credentials, provider bodies, or exception text.

**Disposition:** DLV-052 source commit `a4dcf0f`, integrated as `fd83b44`,
closes the worker diagnostic and lifecycle-ordering paths. Review of the real
profile caught that the first package scripts installed a new version but then
re-enabled the older selected version; correction `64a4039`, integrated as
`56f6908`, makes both workflows explicitly disable, install, select the manifest
version, and enable. The isolated fixture now seeds enabled Spotify `0.2.10`
and YT Music `0.2.6`, proves `0.2.11`/`0.2.7` plus new digests are selected,
starts both through the generic AppContainer path, and leaves the real catalog
unchanged. The local profile reports both corrected versions enabled and the
fresh main Release is visibly running; keep this issue Verifying until the user
opens both widgets successfully.

**Reopened package-freshness evidence:** Integration `8c1bbdf` changes Spotify
Queue/Playlist behavior and occurrence identity, but its source manifest still
declares `0.2.11` while the local catalog already has an older immutable
`0.2.11` selected. The full main Release rebuild therefore cannot put the new
Spotify code under test without violating the same-version rule. DLV-055 owns a
bounded `0.2.12` manifest/doc update, supported build/install/select/enable flow,
exact installed digest proof, and first snapshot before the visible relaunch.
It may not delete/overwrite `0.2.11` or weaken catalog integrity.

**DLV-055 result and remaining reproducibility defect:** Commit `efffa53`
publishes `0.2.12`; its implementation-worktree archive exactly matches the
selected installed digest, differs from retained inactive `0.2.11`, and returns
a valid required-AppContainer first snapshot. Main then rebuilt the same commit
into the same three-file 146,050-byte package, but the independently derived
sealed digest was `f989ac...` rather than installed `a946b3...`. The immutable
catalog correctly refused replacement; the supported workflow reselected and
re-enabled the accepted latest-source artifact, and the coherent Release is
visibly running. DLV-043 is now accepted and integrated through `d534410`;
active DLV-056 removes absolute-checkout-path influence, proves identical two-
root archive/content hashes, and publishes the exact main artifact as unique
`0.2.13`. DLV-056 candidate `2737852` then matched three clean proof builds, but
the required ordinary warm-main refresh at integration `e694316` produced
archive SHA `e0000c3b...25f` and DLL SHA `540d3309...c26` instead of retained
proof archive `9c4de014...3da` and DLL `1752c820...74`. Accepted DLV-057
`90cadf4` closes both prior-build-state and caller-artifact-root influence. Warm
main, repeated main, detached root, installed content, and the planner's
ordinary post-integration main refresh now share archive `66d85242...270`, DLL
`fea3f92d...485`, and sealed digest `21b8e00d...028`. Spotify `0.2.14` is
selected/enabled; `0.2.11` through `0.2.13` remain distinct inactive rollback
generations. PID 27684 was visibly launched from clean accepted main with no
new startup/lifecycle error; direct opening remains the final user check.

The first post-integration relaunch rebuilt only the native executable and left
the older packaged bridge in `out\Release\runtime`, so the new
`admitSnapshot` field was rejected as an unmapped lifecycle payload. This was a
planner artifact-composition failure, not a provider or widget defect. The
subsequent full Release packaging build republished the managed/native graph
from accepted main; its 2026-08-10 23:23 startup has no lifecycle-payload
failure. The user-supplied screenshots showing the hidden-cache and worker-
connection messages were created at 21:54 and therefore document the earlier
failing process, not the fully packaged 23:23 process. Live Spotify and YT
Music opening in that current process remains the final product check.

## GBA-069 — `--show` can create a second resident OverlayHost

**Evidence:** Accepted main `94d4ee0` had healthy hidden PID 27684 resident from
02:35 with `MainWindowHandle=0`. The approved foreground command
`.\src\OverlayHost\out\Release\OverlayHost.exe --show` returned successfully at
02:46 but created healthy visible PID 17952; PID 27684 remained healthy and
resident. Its lack of a visible main HWND also made a bounded
`CloseMainWindow` request return false, so the planner did not force-terminate
or destructively recover it.

**Ownership:** DLV-070 owns OverlayHost owner election and one bounded per-user
local activation path. Widget workers, provider lifecycle, pinning semantics,
and the compositor are not credible owners for this defect.

**Acceptance:** With a hidden or visible production host resident, subsequent
ordinary/`--show` launches forward exactly one Show request to that owner and
exit without initializing a second bridge, catalog, controller lease, or
window. Simultaneous launches elect one owner, crashed-owner leases recover,
other users/stale clients cannot activate it, and orderly shutdown releases
the transport exactly once. The final coherent Release must retain one PID
across two visible `--show` invocations.

**Closure:** DLV-070 `c61a49d`, integrated through `0b21384`, satisfies the
owner/client boundary and focused failure matrix. The planner rebuilt accepted
main, launched the exact Release command twice, retained resident PID 27520,
and observed client PID 3236 exit after the resident logged one authenticated
Show activation. The earlier development hosts were closed gracefully; no
force termination or destructive recovery was used.

## GBA-070 — YT Music reports a stale package version to its companion

**Evidence:** The single DLV-082 integrated Tier-3 run
`20260811T162358Z-a68acd42` passed the changed SDK and API compatibility steps,
then stopped in the unchanged YT Music suite with `Expected '0.2.6', got
'0.2.7'`. The validating test compares the shipped manifest against
`YtmDesktopApiClient.PackageVersion`; source inspection confirms that constant
is still `0.2.6` and is sent as the local companion handshake `appVersion`,
while the current immutable package manifest is `0.2.7`.

**Ownership:** DLV-086 owns only the YT Music package-version value and exact
manifest/handshake assertion. It follows DLV-083 through DLV-085 so this
non-visual drift does not displace visible Game Launcher and Settings work.
Installer behavior, immutable package replacement, authentication, public SDK,
and broad metadata centralization are outside the correction.

**Acceptance:** The source constant, built test asset, current manifest, and
companion handshake agree on `0.2.7`; the existing mismatch assertion remains
effective; no new package version or different bytes under immutable `0.2.7`
are published. Run the focused YT Music suite and metadata validation once, not
the repository aggregate.

**Closure:** DLV-086 `abbf54b`, integrated through `14f7451`, aligns every
current source/authoring/handshake reference with `0.2.7`. Focused YT Music
passes 55/55 and directly asserts the pairing request's `appVersion`; immutable
installed package bytes were not replaced.

## GBA-071 — Game Launcher Restore can strand the library on unavailable rows

**Evidence:** Candidate DLV-084 `6511dc9` passes the focused Game Launcher
Release suite 42/42, including hide/restore after explicitly waiting for each
resource operation to become idle. Its required installed generic-worker run
`20260811T170031Z-c45cff26` passes 5/6. Under ordinary serialized actions, the
fixture hides an exact current game, opens Hidden, restores it, returns Back,
and then observes all 64 games plus the manual entry only as disabled
`Unavailable` warm rows. Sending the visible Refresh action still does not
produce an enabled launch row within five seconds.

**Ownership:** DLV-088 owns the narrow Hidden-to-Library readiness and
replacement ordering after DLV-085. Candidate `8295983` passes its final direct
suite 43/43, but retained installed run `20260811T174504Z-9a7a377a` still
reproduces the original unavailable rows because it ran before the final route-
token correction. DLV-090 owns one clean final installed-route proof after
active DLV-089. Provider discovery, broker/SDK contracts, native rendering,
arbitrary delays, and broad cursor-resource redesign are not authorized.
Candidates DLV-084 and DLV-088 remain unintegrated until that proof is accepted.

**Acceptance:** Hide, Hidden, Restore, and Back through the installed generic
worker expose the exact restored game as a current enabled launch row without
manual Refresh. Premature empty-Hidden state cannot admit route actions that
allow a late or canceled resource generation to restore old warm-only rows.
Focused slow/cancellation coverage passes, and one clean installed-route run
from the final candidate prefix passes. The earlier red run remains retained;
unchanged direct fixtures are not rerun repeatedly.

**Closure:** DLV-088 `8295983` plus DLV-090 evidence commit `dc1bc16`, integrated
through `dc1bc16`, satisfy the final product seam. Direct Game Launcher passes
43/43 and clean installed run `20260811T175350Z-011a57cd` passes 6/6 with no
manual Refresh.

## GBA-072 — Disabled widget local-data reset targets a synthetic identity

**Evidence:** Candidate DLV-085 `e0edfb8` correctly keeps the private document
inside the host, retires a running selected worker before clear, publishes one
fresh generation, and preserves a neighbor. Its disabled-package fallback,
however, constructs `InstanceId = $"{manifest.Id}.disabled"`.
`BridgeCatalog.LoadWithInstalledAsync` configures that same package/version as
`installed.<SHA256(widget-id@version)>`. Private state written while enabled is
therefore absent from the synthetic lookup and survives a reported disabled
widget reset. The focused tests cover running configured identities and an
already-disabled catalog entry without first writing through the production
enabled identity; they do not close the transition case required by DLV-085.

**Ownership:** DLV-089 owns one canonical host-internal installed-instance
identity derivation and the enabled-to-disabled production-shaped fixture. It
follows active DLV-088 so review does not interrupt the implementation lane.
Private-state schemas, package removal, global reset, public SDK/protocol,
native host behavior, and broad catalog refactoring are outside the correction.

**Acceptance:** Write exact package/version state while enabled, disable the
real installed package, inspect and clear that same state from Settings without
starting a worker, then re-enable and observe clean state. A removed or replaced
package and stale confirmation fail closed; a neighboring identity is
unchanged. No `.disabled` state surrogate remains, and production plus
management paths share one derivation owner.

**Closure:** DLV-089 `022ffc4`, integrated through `dc1bc16`, establishes one
`InstalledWidgetInstanceIdentity` owner. Final Bridge 73/73 proves the real
enabled-to-disabled stale/clear/re-enable transition, no disabled worker
creation, and an unchanged neighbor.

## GBA-073 — Protected Wi-Fi retains secret copies and rollback ownership is not exact

**Resolution:** Closed by DLV-093 `3ce8991`, integrated with the visible DLV-087
flow through `63ca3a2`. The password is now carried in one bounded mutable
native byte frame and one managed mutable owner, both zeroed on terminal paths;
the modal clears its EDIT text before destruction, and provider command/profile
XML owners are cleared without creating a raw immutable test string. Temporary
profiles use random per-attempt names and 32-byte custom-data ownership tokens.
Every delete verifies the exact interface, profile, and token; replacement,
unavailable verification, and deletion failure never remove current state and
produce explicit bounded results. Focused evidence passes Windows Network
55/55, WidgetBridge 76/76, Network Controls 22/22, generic AppContainer 6/6,
both native secret targets, and 55 documentation files. The one exact-commit
aggregate `20260811T201128Z-f9af2cba` reached the changed groups and stopped
later at an unrelated accepted Game Launcher fixture; it was retained and not
rerun.

**Evidence:** DLV-087 candidate `8c2949c` collects a masked password in the
native host and correctly bypasses the ordinary AppContainer widget. The send
path nevertheless calls `JsonValue::CreateStringValue`, `JsonObject::Stringify`,
and `winrt::to_string`, leaving ordinary immutable/native JSON and UTF buffers.
`BridgeFrameChannel.ReadAsync` reads the complete request into a managed
`byte[]` that is never cleared and deserializes it into a `JsonElement`; the
classifier and request handler each deserialize `BridgeProtectedWifiRequest`,
creating separate immutable `Secret` strings. Clearing only the later `char[]`
and profile XML cannot satisfy the assigned post-operation zeroization
contract. The standard password EDIT control is destroyed without first
overwriting its text.

The same candidate creates a per-user profile named directly from the SSID and
records only interface, profile name, and a `CreatedProfile` Boolean. Terminal
failure, timeout, adapter disposal, and provider disposal call
`WlanDeleteProfile` by that common name. `bOverwrite = FALSE` protects state
present at creation time, but nothing proves that the profile still stored
under that name is the attempt-owned generation when rollback runs. A
neighboring process can replace it after creation and before the asynchronous
terminal event, allowing rollback to delete newer state.

**Ownership:** DLV-093 follows already-active visible DLV-091. It owns one
mutable, explicitly zeroed trusted transport from the modal through native and
managed framing into the provider, plus an attempt-unique profile identity and
conditional exact rollback. It does not reopen widget UX, add credential
persistence, encrypt the current-user authenticated pipe without evidence, or
expand supported Wi-Fi modes.

**Acceptance:** Every terminal path clears the EDIT control and every native/
managed transport, command, P/Invoke, and XML buffer; no immutable password
string or retained raw-password test artifact is created. A profile that
predates the attempt or is created/replaced by a neighbor is never changed or
deleted. Failed rollback is reported explicitly, and only the exact still-
current attempt-owned profile can be removed. Focused buffer/race evidence, one
installed production-host route, and one clean exact-commit aggregate pass
before DLV-087 is integrated and the Release is relaunched.

## GBA-074 — Running-app inspection is not fully bounded or validated

**Evidence:** DLV-095 candidate `bd270c9` adds a separate on-demand read grant,
maps current windows to normalized SavedIds inside the trusted provider,
confirms the current observation before CAS mutation, and keeps process/window/
path identity out of widget IPC. Its native observer stops after 256 appended
eligible process observations, not after 256 top-level windows visited. A
desktop with many owned, cloaked, unreadable, elevated, or otherwise excluded
windows can therefore cause an unbounded number of eligibility checks and
process-open attempts before the observer reaches its named cap. Separately,
`WidgetAppLibraryService.ConfirmRunningAsync` verifies only that a returned
item's SavedId equals the request, then returns AppId, kind, display, and source
fields without the validation applied to ordinary app-library items.

**Ownership:** Accepted and integrated DLV-097 `46d1938` owns one deterministic
examined-window bound, complete SDK validation for a non-null confirmed item,
focused boundary/malformed-response fixtures, and corrected implementation-
status wording. It does not change matching, consent, persistence, launch
authority, widget layout, provider architecture, or artwork work.

**Acceptance:** At most the documented number of top-level windows are visited
and process-open attempts cannot exceed that bound, including an all-ineligible
fixture. The public result remains capped at 64 with stable duplicate collapse.
A valid exact confirmation succeeds; invalid opaque IDs, mismatched SavedId,
undefined kind, and empty, overlong, or control-bearing display/source values
fail as `malformed_response` before either widget writes state. Neighbor state
is unchanged and the complete corrected prefix passes its bounded focused and
installed evidence before integration.

**Current evidence:** Provider 57/57, SDK 87/87, compatibility 12/12, Games &
Apps 59/59, Game Launcher 45/45, and documentation 55/55 pass. Both widgets
retain neighbor state and perform no private-state CAS mutation on malformed
confirmation. Only the user's live packaged verification remains.

## GBA-075 — `TextEntry` is rejected by bridge style resolution

**Evidence:** The current production session begins at 2026-08-11 16:00:20 for
PID 35472. It contains five Game Launcher failures between 16:53:06 and
16:55:11, all reporting `Unsupported view node kind 'TextEntry'`. A search of
the 1,224 current-session log lines found no other failure, error, exception,
timeout, malformed, denial, crash, protocol, warning, rejection, or unavailable
signature. Older August 10 Spotify/YT Music worker-start and Settings payload
failures did not recur and are not reopened by this audit.

**Root cause:** Protocol v15 validation, the public SDK, Game Launcher, Network
Controls, the native parser, and the host-owned text modal all support
`TextEntry`. `BridgeRenderStyles.RoleFor(ViewNodeKind)` does not map that node
kind and throws while constructing computed styles, so a valid snapshot never
reaches the native host. Existing component tests cover each side of the seam
but omit the production bridge style path.

**Ownership and acceptance:** DLV-100 owns one canonical distinct GBSS role,
default/themed resolution, production-shaped Game Launcher and protected
Network Controls fixtures, and the smallest installed generic-worker proof.
Game Launcher must open and Search must reach the unchanged host-owned modal;
unknown future kinds must still fail closed. No widget-local fallback or public
protocol/native modal redesign is permitted.

## GBA-076 — Prototype worker quotas block full-application widgets

**Evidence:** Current public documentation defines a 16-256 MiB worker memory
request and a one-active-process Job policy, while other platform guidance says
the public framework should support application-scale media, multipage, and
large-collection experiences. These are prototype execution quotas, not trust-
boundary limits, and they make a game-library application, rich media client,
or helper-process architecture fail for reasons unrelated to native-host
availability.

**Approved product boundary:** A widget may own full application-scale private
models, databases, indexes, caches, computation, navigation, and an owned helper
process tree. The shared native host remains strictly bounded at IPC, current
presentation, native/GPU allocation, update/action admission, capability,
package-ingestion, cache, and concurrent-session boundaries. Job accounting,
non-breakaway ownership, kill-on-close, integrity, UI restriction, and bounded
teardown remain; ambient OS authority remains capability-gated.

**Ownership and acceptance:** DLV-101 removes the default hard worker memory
and one-process ceilings, aligns manifest semantics and public documentation,
proves an owned child process cannot escape and is reclaimed, and preserves
fail-closed oversized-message/snapshot behavior without harming a neighboring
widget. It must also name the scalable widget-private data path rather than
misrepresent the bounded host `PrivateState` document as application storage.

## Closed issues

Closed entries remain here with their acceptance evidence and commit instead of
being deleted.
