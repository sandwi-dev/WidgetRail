# Prototype roadmap

Status: active implementation sequence with evidence gates, 2026-08-07

The next goal is not “build all widgets.” It is to prove that the operating-system constraints permit the product experience without turning the overlay into injected or driver-backed bloatware.

## Phase 0: platform feasibility

### Spike A: Guide button

Build the smallest event-driven GameInput program that records Guide/Share and ordinary controls.

Test:

- Xbox Series and Elite controllers
- DualSense
- One generic controller
- USB and Bluetooth
- Xbox Game Bar enabled and disabled
- Steam open and closed
- Foreground game requesting exclusive input where possible

Exit criteria:

- A documented supported controller matrix
- Reliable press/release transitions without polling while hidden
- Conflict detection or clear onboarding for Game Bar and Steam
- A controller-only fallback chord decision

### Spike B: input containment

Open and focus a minimal overlay over input test applications using XInput, GameInput, Raw Input, SDL, Unity, and Unreal.

Exit criteria:

- Evidence of which backends stop receiving input and which continue
- No stuck buttons across open/close transitions
- A written support policy that does not imply universal suppression
- Explicit decision to continue without a filter driver, or stop and reassess the product promise

### Spike C: presentation compatibility

Render a transparent controller-navigable strip with D3D11, Direct2D, and DirectComposition.

Test DirectX 11, DirectX 12, Vulkan, and OpenGL applications in windowed, borderless, Fullscreen Optimizations, and true Fullscreen Exclusive modes. Include HDR/SDR, VRR, mixed-DPI multi-monitor, and a hybrid-GPU laptop when available.

Exit criteria:

- Windowed, borderless, and Fullscreen Optimizations are reliable
- True FSE limitation is detected or documented
- Overlay focus and restoration do not strand the user
- No continuous presentation while hidden

### Spike D: resource and UI stack

Implement the same three-card screen in native D2D/DWrite and, only if useful, a minimal WinUI 3 comparison.

Measure private working set, startup, warm activation, frame time, GPU activity, and idle wakeups with ETW/Windows Performance Recorder and PresentMon.

Current single-sample evidence for the visible prototype is 93.2 MB host +
59.1 MB bridge + 51.5 MB worker = **203.8 MB private memory**. Over five
seconds, `OverlayHost` accumulated **78.12 ms CPU**; bridge and worker deltas
were below timer resolution. This does not yet satisfy the repeatable ETW,
hidden-state, GPU, wakeup, or multi-widget evidence gate.

Exit criteria:

- Native host meets or credibly approaches the product budgets
- A measured renderer decision, including the cost of custom focus/accessibility work
- Automated benchmark scripts and stored baseline results

### Spike E: isolated worker

Launch a sample worker, negotiate a protocol over a secured named pipe, render its declarative tree, forward controller events, persist state, and recover from deliberate crash/hang/oversized-message cases.

Exit criteria:

- Worker failure never terminates or blocks the shell
- Lazy cold start is acceptable or hidden by a cached snapshot
- Job Object accounting and termination work (**initial memory, one-process,
  pre-launch assignment, kill-on-close, and UI restrictions implemented and
  tested**)
- Mandatory capability-free AppContainer launch plus exact-SID/Low-label/PID-
  bound main and broker communication is implemented and tested end to end
- The supported Windows/version/architecture matrix and profile cleanup policy
  are explicit before public distribution

## Phase 1: shell vertical slice

Deliver one controller-only executable with:

- Guide toggle
- Dashboard strip with three sample widgets
- Controller edit mode for reorder, visibility, and favorites
- Widget activation and strict internal input routing
- Last widget and stable focus restoration
- Safe mode, reset, diagnostics, and invalid-config recovery
- Minimal GBSS variables, semantic selectors, focus states, and live reload
- Performance overlay for the overlay itself

No capture, Discord, marketplace, web widgets, or general community code yet.

## Phase 2: first public SDK

- Versioned manifest schema
- C# `WidgetRunner` SDK
- Bounded length-prefixed strict JSON protocol over secured named pipes
- Core declarative layout/content/input primitives
- `gbar new`, `gbar dev`, validation, packaging, and input replay
- State API, the five-state host-authoritative lifecycle, crash recovery,
  explicit lifecycle-policy controls, and resource reporting
- Typed transport-neutral audio/network host services over authenticated,
  identity/declaration/consent/lifecycle-bound local broker transport
- One built-in widget and one out-of-process sample implementing equivalent behavior
- Controller-only and accessibility conformance tests

Developer mode is local and unsigned but remains isolated. Public distribution
remains off until the publisher-trust gates in Phase 4.

## Phase 3: local product hardening and useful first-party widgets

- Make every dashboard tile a real catalog-backed worker with deterministic
  local acceptance coverage; placeholders remain absent until complete
- Finish YT Music connection, pairing persistence, transition responsiveness,
  feedback, controller navigation, and failure recovery
- Move YT Music off its trusted desktop exception: implement an exact-port,
  loopback-only HTTP broker for declared local companions plus a private
  per-widget secret vault for optional bearer-token persistence. Prove the
  unchanged widget through the ordinary installed AppContainer path; never
  grant ambient network or direct Credential Manager access to community code.
- Finish Settings controller reachability, diagnostics/recovery, local package
  and theme workflows, and permission/version consistency
- Complete controller-first Audio Control and Network Control through their
  locally testable hardware, privacy, denial, churn, and recovery gates
- Run an explicit reversible Audio Control hardware gate on this machine:
  retain the original default multimedia endpoint, make one bounded master
  volume/mute change, verify callbacks and reconciliation, and restore the
  original scalar/mute in `finally`. This must remain opt-in and outside normal
  verification; output switching is not part of the v1 scope.
- Complete and package the reusable controller Slider: absolute quantized
  values, optimistic native feedback, bounded latest-wins coalescing, stable
  focus, and D-pad/analog horizontal adjustment. Controller scrolling and
  focus-follow list restoration are implemented; Slider remains in the current
  verification milestone.
- Add an original controller-first component library over the public SDK:
  icon buttons, cards, section headers, status badges, dividers, alerts, empty
  states, switches, tabs, and scoped dialogs. Components must keep stable IDs,
  minimum controller target sizes, readable non-color state, nested Back
  behavior, themeable semantic classes, and supported-DPI focus containment.
- Add local worker/provider recovery, crash quarantine, lifecycle enforcement,
  resource evidence, and disk/profile quotas/cleanup
- Performance widget only after its real local diagnostics data and acceptance
  suite exist
- Media controls
- Recent Apps first slice implemented; authoritative game classification,
  history/relaunch, icons, and grouping remain roadmap work
- Capture proof and widget if Windows API tests pass
- Discord proof after eligibility and production communications access are confirmed

Every first-party widget contributes a focused SDK example and regression suite.

### First-party system-control reference widgets

**Audio Control** and **Network Control** are explicit first-party widget
roadmap items. They occupy the same useful system-utility category as the Xbox
Game Bar audio and network surfaces, but their interaction design is
controller-first and belongs to this platform: dashboard summaries, focused
open panels, predictable D-pad/analog navigation, widget-owned shortcuts, and
clear busy/error/permission feedback. They are not privileged shell panels.

The repository has bounded Audio Mixer and Network Controls reference slices
that validate parts of the SDK, broker, provider, packaging, and controller
design. Those slices are evidence for the roadmap, not a claim that either
full product widget below is implemented, shipped, or production-ready.

The generic declarative path, controller Settings/global-theme foundation, and
typed broker/consent path support both integrations. They remain behind their
hardware/privacy/performance evidence gates and must stay ordinary first-party
packages built on the public SDK—not special panels hard-coded into
`OverlayHost`. Any primitive or broker API they need becomes documented,
testable platform surface that community widgets can request under the same
permission policy.

**Audio Control roadmap** starts with a deliberately narrow Audio Mixer slice:

- enumerate sanitized per-application audio sessions on the current default
  multimedia render endpoint;
- observe session/default-endpoint changes through Core Audio callbacks;
- set volume or mute for one opaque session while the widget is Interactive;
- expose a compact dashboard summary and a controller-first session list where
  focus, adjustment, mute, and error feedback remain unambiguous; and
- publish bounded, coalesced session-change events without a timer polling
  loop.

Later Audio Control phases add, in evidence-gated increments:

- controller-first output-device selection where a supported documented
  Windows setter is available;
- broader device, communications, and application-churn coverage for the
  implemented master and per-session volume/mute surface;
- broader capture-device/role visibility and, only where a supported setter
  exists, controlled selection; and
- live device/session/default-role updates without a background polling loop.

Audio phase dependencies are: declarative list/slider/toggle states and stable
controller focus; typed read/control grants and Settings consent; an
event-driven Core Audio provider; then hardware/churn/performance evidence.
Output switching and any audio-sample capture additionally require supported
Windows APIs, separate permissions, privacy review, and unmistakable feedback.

Endpoint master volume/mute is implemented on the current default multimedia
render endpoint. Sanitized default output/input names and current default-
microphone volume/mute are also implemented behind independent read/control
grants. Output-device selection and default communications-role changes are not
implemented. Input control does not grant microphone audio capture. Each
remaining item requires a separate capability, privacy/feedback design, and provider/API review. No
undocumented `PolicyConfig`, registry write, or shell-automation output switch
is acceptable.

Version-1 broker control grants are Interactive-only. Do not route master mute,
volume, saved-network switching, or another general control through a Visible
dashboard quick action; that would require a distinct bounded host-mediated
authority and security review.

The host broker owns OS handles, COM lifetime, device/session observation,
permission policy, and sanitized identity. The widget receives bounded semantic
models/events and invokes narrow commands; it never receives a raw endpoint,
session, or microphone handle. It must demonstrate device/session arrival and
removal, default-device changes, application churn, communication-device
policy, and recovery without keeping Visible/Interactive polling alive in
`Background`.

The documented `IMMDeviceEnumerator` surface reads the default endpoint but
does not provide a system-default setter. Do not ship an undocumented
`PolicyConfig` interface, registry write, or shell-automation workaround; if no
supported API passes the spike, default-device switching leaves the initial
scope.

**Recent Apps roadmap** now has a locally testable first slice. A trusted
WinEvent provider observes eligible foreground/destroyed top-level windows only
after an authorized read starts it, keeps at most 16 running applications, and
publishes full coalescible snapshots without polling. The public model contains
only a bounded display name, an opaque process-lifetime ID, running/most-recent
state, and conservative kind. The first slice is now read-only: unreliable
foreground switching duplicated Windows task switching and has been removed.

The first slice intentionally does not read UserAssist or registry history,
query Xbox services, inspect game memory, retain executable paths/PIDs/HWNDs in
the worker, launch closed applications, or infer that an app is a game. Its
replacement is a catalog-backed Games & Apps launcher with authoritative
installed-library sources, icons, and explicit launch contracts; it must not
turn recent activity into an unrestricted process launcher.

The first replacement foundation is implemented and locally testable: a
read-only Start Menu source returns bounded sanitized application records with
random opaque IDs and no launch operation. Next, add the supported AppsFolder
source, then broker the combined catalog before implementing exact revalidated
launch and the controller tile UI.

**Network Control roadmap** follows the Audio Control foundation. Its first
slice uses Windows WLAN/network change notifications rather than continuously
polling adapters and targets:

- Ethernet and Wi-Fi connection state;
- coarse active transport and Wi-Fi adapter/service/radio availability;
- current coarse-status Wi-Fi identity and signal remain omitted when Windows
  precise-location access is unavailable; the separate explicit scan flow
  presents sanitized current results and clear required/denied states;
- controller selection among current available scan results, with connection
  limited to saved-profile-backed or unsaved open results;
- a compact dashboard summary plus a focused open panel with explicit
  Connecting, Connected, Failed, permission, and unavailable states; and
- the dashboard card is read-only and declares no profile-selection or connect
  quick actions; no capability-backed network control runs while merely
  Visible. Any future dashboard connect control needs
  separate host-mediated authority and must not silently disclose credentials
  or connect to an unreviewed network.

Password entry, editing/creating protected Wi-Fi profiles, captive-portal
interaction, and exposing stored network keys are explicitly outside the
current scope. The broker owns WLAN/network handles and returns sanitized
state/events. Connection uses a current saved-profile-backed or open scan
result, requires clear focus/feedback, and must
handle adapter removal, airplane/radio state, connection failure, and Ethernet
priority without trapping controller focus.

IP Helper notifications drive aggregate/Ethernet changes. Native Wi-Fi uses a
long-lived WLAN client and asynchronous ACM connection notifications; it does
not scan continuously or register MSM. Version 1 does not automatically query
SSID/current-connection/signal details that Windows treats as
location-sensitive; it reports privacy-restricted state instead. Any future
explicit access request must degrade to required/denied/revoked states, never
trigger a retry loop, and never expose BSSID/profile XML/key material.
See [Windows provider architecture](windows-provider-architecture.md) for the
documented API facts, threading/lifetime rules, privacy boundary, and simulator
matrix, and the [Network Controls reference](network-controls.md) for the
author-facing contract and completion evidence.

The current Network Control milestone includes controller-visible **currently
available Wi-Fi networks**. The closed broker/SDK contracts, trusted provider,
first-party UI, bundled capability declaration/consent, and dedicated tests for
explicit user-initiated `WlanScan`, asynchronous completion/timeout, a bounded
`WlanGetAvailableNetworkList` snapshot, and current saved/open result connection
are implemented. Separate software-radio read/control grants are also
implemented with authoritative reconciliation and explicit multi-PHY partial
failure. Windows gates
both scan/list APIs behind
precise-location consent on current releases, so the flow must begin from a
controller action, explain the OS prompt, and render required, denied, and
revoked states without retrying. The resulting rows use generation-bound opaque
scan IDs that expire on the next scan/provider generation; widgets never receive
BSSID, interface identity, raw WLAN structures, profile XML, or keys.

Connection support currently accepts saved-profile-backed and unsaved open
results. The next step is a host-owned credential prompt for new WPA/WPA2/WPA3
Personal networks. Credentials never enter the widget snapshot,
worker process, widget-owned storage, diagnostics, or logs. Enterprise/802.1X,
certificate, SIM, domain-credential, hidden-network, and captive-portal setup is
unsupported initially. `WlanConnect` remains asynchronous and authoritative
ACM events determine success/failure. `WlanSetInterface` with
`wlan_intf_opcode_radio_state` may control only the software radio state; a
hardware switch, policy, or airplane-mode restriction remains authoritative.

Network Controls now includes a first Bluetooth slice behind separate closed
read and radio-control grants. The trusted WinRT provider reports sanitized,
bounded paired/present/connected device state through event-driven discovery
and controls only the software radio. Pair/unpair remains future host-owned
work; it may use `DeviceInformationPairing.PairAsync`/`UnpairAsync` only after
owner-window, consent, cancellation, and hardware review. No generic Bluetooth
device Connect/Disconnect command is
promised: public Windows communication APIs are profile-specific (for example
GATT services/characteristics and RFCOMM sockets), so each future functional
connection needs its own reviewed profile contract and capability.

Later Network Control phases add:

- a host-owned WPA Personal credential flow after the implemented explicit,
  privacy-gated available-network scans and saved/open connections; enterprise
  authentication remains unsupported initially;
- Bluetooth host-owned pair/unpair after the implemented radio/discovery slice;
- sanitized active-adapter state and Ethernet/Wi-Fi identity;
- SSID and signal/link quality only through an explicit Windows privacy-access
  flow with required/denied/revoked states;
- bounded IP address, gateway, and DNS summaries that never expose credentials
  or raw provider handles;
- throughput, latency, and packet-loss diagnostics with explicit sampling
  ownership, frequency bounds, cancellation, and visible resource cost; and
- safe reconnect/renew/diagnostic actions only after capability and failure-
  recovery review.

Network phase dependencies through scan and saved/open connection are now
implemented: bounded models and controller focus, separate closed grants,
event-driven IP Helper/Native Wi-Fi, explicit scan, generation-bound IDs, and
precise-location denial states. Remaining dependencies are the host-owned
credential prompt, Bluetooth pairing, hardware/privacy matrices, and
measured diagnostic sampling. Identity/address details
and recovery commands do not enter the public contract before those reviews.

The user-visible first-party reference includes explicit scans and unsaved open
networks and software Wi-Fi/Bluetooth radio controls, but still excludes
password entry, protected profile creation/editing, and Bluetooth pairing. The staged credential flow
must remain host-owned and WPA Personal-only at first; it never exposes stored
keys. Captive-portal automation, enterprise/802.1X provisioning, arbitrary
adapter configuration, and privileged troubleshooting scripts remain outside
the initial expanded scope. Diagnostic sampling must stop outside its declared
lifecycle state and must be measured against the overlay's CPU, network,
wakeup, and memory budgets.

Exit criteria for both widgets:

- the worker uses only published SDK and declared brokered capabilities;
- the same package runs out of process through the generic catalog/bridge path;
- dashboard quick actions and the open surface follow standard input scopes;
- hidden/background CPU and wakeups are measured with no presentation polling;
- capability denial, service/device loss, worker restart, and stale-event races
  have deterministic controller-readable states; and
- source, contract documentation, simulator fixtures, and regression tests are
  suitable as production SDK examples.

Before either package is labeled production-ready, complete controller polish,
physical hardware/device/router matrices, privacy and denial UX, long-running
churn/recovery tests, and hidden/background resource measurements. Every new
primitive or capability must remain reusable by community widgets; these two
packages are the canonical templates for event-driven system-control widgets.

## Phase 4: ecosystem

- Publisher signing/revocation and a signed update channel, mandatory before
  public community distribution but intentionally sequenced after local
  product-completion gates
- Curated catalog and controller-first install/update/rollback
- Publisher identity and moderation process
- Compatibility and resource labels
- WinGet or Microsoft Store discovery where useful
- Theme gallery
- Public API stability policy and migration tooling
- Optional WASM logic tier evaluation
- Optional shared WebView2 tier only if demanded and clearly resource-labeled

## Risk register

| Risk | Severity | Evidence needed | Current response |
| --- | --- | --- | --- |
| Games receive controller input behind overlay | Critical | Backend test matrix | Phase 0 stop/go gate; no driver by default |
| Guide conflict or unavailable system button | Critical | Controller/client matrix | GameInput callback, conflict onboarding, controller-only fallback |
| Overlay not visible in true FSE | High | Presentation matrix | Do not support true FSE initially; no injection |
| Native UI scope expands uncontrollably | High | Three-card implementation effort and accessibility audit | Small primitive set; renderer-independent widget protocol; compare WinUI only with data |
| Community widget compromises user | Critical | AppContainer/broker abuse tests plus signing, quota, and audit evidence | Unsigned local development remains AppContainer-isolated and explicitly labeled; signing/revocation is deferred until local product gates pass but remains mandatory before public community distribution |
| Worker model feels slow or heavy | High | Cold-start and working-set measurements | Lazy first launch, resident-Background measurement, explicit user lifecycle choices, resource labels |
| GBSS updates break themes | Medium | Theme compatibility fixtures | Stable semantic selectors, typed allowlist, versioned tokens |
| Discord rejects overlay use case | High for social only | Written eligibility/production access | Keep Discord as optional first-party integration, not a core dependency |
| Anti-cheat reacts to overlay | High | Representative signed/unsigned game tests | No injection, no game-memory access, compatibility matrix |
| Feature creep recreates bloatware | High | Continuous resource regression tests | Budgets in CI; every background capability justified |

## Original Phase 0 implementation order

1. Create the native solution and diagnostics harness.
2. Complete Guide-button and input-containment spikes before polishing UI.
3. Complete the presentation matrix.
4. Measure native rendering against the budgets.
5. Prove one crashing out-of-process declarative widget.
6. Review the evidence and accept or revise the proposed architecture.

The native foundation, isolated worker path, controller package review/live
catalog reload, and responsive per-monitor geometry seams are now implemented.
The current product order is:

1. make every dashboard tile honest and locally complete: YT Music, Settings,
   Audio Control, and Network Control; remove placeholders until their
   acceptance suites pass;
2. complete controller reachability/auto-scroll and the physical
   resolution/DPI/accessibility visual matrix for every first-party surface;
3. add controller-visible diagnostics, worker/provider recovery, crash
   quarantine, local performance/resource evidence, and lifecycle-policy
   enforcement;
4. complete locally testable Audio/Network hardware, churn, privacy, and denial
   paths; then Performance, general media, recent apps/games, and capture
   feasibility;
5. harden local developer mode and public references: exact-generation
   `gbar dev` readiness, real first-party community-package conformance,
   graphical/native theme preview, known-local package/theme import,
   remove/rollback, and clean-profile end-to-end samples;
6. finish controller/game/presentation/anti-cheat matrices plus CPU and
   disk/profile quotas/cleanup; and
7. only then implement publisher signing/revocation, signed update metadata,
   moderation/gallery policy, and public distribution. Signing remains a hard
   pre-public gate, not a blocker for isolated unsigned local development.

Do not use a first-party widget to justify a private host API that external
widgets cannot exercise. Discord/social work remains deferred until eligibility
and production authentication/communications access exist.

The first irreversible ecosystem decisions—public API 1.0, package signing
rules, marketplace policy, and optional web/WASM tiers—wait until these local
product steps have evidence.
