# Prototype roadmap

Status: proposed sequence with evidence gates, 2026-08-06

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
  pre-launch assignment, and kill-on-close policy implemented and tested**)
- AppContainer communication is proven on the chosen minimum Windows versions
- The initial trust/support policy for Windows 10 versus Windows 11 is explicit

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
- Length-prefixed protobuf protocol over secured named pipes
- Core declarative layout/content/input primitives
- `gbar new`, `gbar dev`, validation, packaging, and input replay
- State API, the five-state host-authoritative lifecycle, crash recovery,
  explicit lifecycle-policy controls, and resource reporting
- Typed transport-neutral audio/network host services over authenticated,
  identity/declaration/consent/lifecycle-bound local broker transport
- One built-in widget and one out-of-process sample implementing equivalent behavior
- Controller-only and accessibility conformance tests

Developer mode is local and unsigned but remains isolated. Public distribution remains off until Phase 3.

## Phase 3: security and useful first-party widgets

- AppContainer/Win32 isolation and real provider security work (the typed v1
  SDK, authenticated broker transport, controller consent, and simulator path
  are implemented)
- Signed packages, atomic update, rollback, and crash-loop disable
- Malicious/abusive widget test corpus
- Performance widget
- Media controls
- Recent apps/games
- Capture proof and widget if Windows API tests pass
- Discord proof after eligibility and production communications access are confirmed

Every first-party widget contributes a focused SDK example and regression suite.

### First-party system-control reference widgets

Audio Mixer and Network Controls begin only after the generic declarative widget
path, controller Settings/global-theme foundation, and real-provider security/
privacy gates. The typed broker and consent path already works against a
simulator. The widgets must be ordinary first-party packages built on
the public SDK—not special panels hard-coded into `OverlayHost`. Any primitive
or broker API they need becomes documented, testable platform surface that
community widgets can request under the same permission policy.

**Audio Mixer** uses Windows Core Audio notifications and callbacks rather than
a high-frequency polling loop. Its production scope is:

- master/output volume and mute;
- output-device discovery and, only if a supported public Windows API passes a
  packaging/minimum-version spike, switching;
- per-application audio sessions with volume and mute;
- microphone mute and level where the broker can expose them safely; and
- a future separately reviewed host-mediated dashboard-control path, if needed.

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

**Network Controls** uses Windows WLAN/network change notifications rather than
continuously polling adapters. Its initial production scope is:

- Ethernet and Wi-Fi connection state;
- current network and Wi-Fi signal quality;
- controller selection among already saved Wi-Fi profiles; and
- no capability-backed dashboard control in version 1; any future quick control
  needs separate host-mediated authority and must not silently disclose
  credentials or connect to an unreviewed network.

Password entry, editing/creating Wi-Fi profiles, captive-portal interaction,
and exposing stored network keys are explicitly outside the initial scope. The
broker owns WLAN/network handles and returns sanitized state/events. Switching
uses an existing saved profile only, requires clear focus/feedback, and must
handle adapter removal, airplane/radio state, connection failure, and Ethernet
priority without trapping controller focus.

IP Helper notifications drive aggregate/Ethernet changes. Native Wi-Fi uses a
long-lived WLAN client and asynchronous connection notifications; it does not
scan continuously. SSID/current-connection/signal details that Windows treats
as location-sensitive must degrade to permission-required/denied/revoked
states, never trigger a retry loop or expose BSSID/profile XML/key material.
See [Windows provider architecture](windows-provider-architecture.md) for the
documented API facts, threading/lifetime rules, privacy boundary, and simulator
matrix.

Exit criteria for both widgets:

- the worker uses only published SDK and declared brokered capabilities;
- the same package runs out of process through the generic catalog/bridge path;
- dashboard quick actions and the open surface follow standard input scopes;
- hidden/background CPU and wakeups are measured with no presentation polling;
- capability denial, service/device loss, worker restart, and stale-event races
  have deterministic controller-readable states; and
- source, contract documentation, simulator fixtures, and regression tests are
  suitable as production SDK examples.

## Phase 4: ecosystem

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
| Community widget compromises user | Critical | AppContainer and broker abuse tests | No public executable widgets before isolation passes |
| Worker model feels slow or heavy | High | Cold-start and working-set measurements | Lazy first launch, resident-Background measurement, explicit user lifecycle choices, resource labels |
| GBSS updates break themes | Medium | Theme compatibility fixtures | Stable semantic selectors, typed allowlist, versioned tokens |
| Discord rejects overlay use case | High for social only | Written eligibility/production access | Keep Discord as optional first-party integration, not a core dependency |
| Anti-cheat reacts to overlay | High | Representative signed/unsigned game tests | No injection, no game-memory access, compatibility matrix |
| Feature creep recreates bloatware | High | Continuous resource regression tests | Budgets in CI; every background capability justified |

## Immediate implementation order

1. Create the native solution and diagnostics harness.
2. Complete Guide-button and input-containment spikes before polishing UI.
3. Complete the presentation matrix.
4. Measure native rendering against the budgets.
5. Prove one crashing out-of-process declarative widget.
6. Review the evidence and accept or revise the proposed architecture.

After the current prototype foundation, the product order is: finish generic
widget rendering and the installed-package review/reload experience; finish
the remaining accessibility/global-theme evidence; add stronger worker
isolation and real Windows provider backends; then build Audio Mixer and Network
Controls through those public surfaces. Do not use either widget to
justify a private host API that external widgets cannot exercise.

The first irreversible ecosystem decisions—public API 1.0, package signing rules, marketplace policy, and optional web/WASM tiers—wait until these five steps have evidence.
