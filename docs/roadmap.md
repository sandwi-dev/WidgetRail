# Product roadmap

Status: active product direction, 2026-08-12

The detailed pre-compaction roadmap is preserved in
[`history/roadmap/2026-08-12T02-47-00-07-00.md`](history/roadmap/2026-08-12T02-47-00-07-00.md).
That snapshot is historical evidence only. [`delivery-plan.md`](delivery-plan.md)
is the sole implementation queue and may schedule only bounded assignments with
explicit dependencies and acceptance evidence.

## Product outcome

Build a lightweight Windows gaming control-center overlay with professional
controller-first interaction, first-party reference widgets, and a public
widget platform. It should feel predictable and low-depth like a console
control center without cloning another product's visual design.

The overlay must remain useful while a game is running: low hidden/idle cost,
bounded native-host resources, fast input response, stable focus, responsive
layout, strong failure recovery, and no ambient authority for ordinary
community widgets.

## Product principles

### Controller-first dashboard

- D-pad and left analog stick navigate.
- A opens/activates the selected item.
- B returns or closes the current scope.
- Tap Y enters reorder; bounded hold Y refreshes the tray.
- LB, RB, X, triggers, and other nonreserved controls remain available for
  contextual widget actions.
- A dashboard shortcut is shown only when it is currently actionable and has
  one deterministic owner.
- Focus, pointer, keyboard, controller, painted focus, and accessibility
  semantics must agree.

### Responsive and accessible

- Layout derives from the logical surface/work area, DPI, text scale, and
  interface scale—not monitor-resolution special cases.
- Support documented compact, standard, wide, ultrawide, 720p, 1080p,
  taskbar-reserved, monitor-change, 150%-scale, reduced-motion,
  reduced-transparency, and high-contrast profiles.
- Preserve stable identity/focus across reflow when the item remains valid.
- Essential header, content, guides, and tray stay reachable below preferred
  size through reflow or a bounded viewport.
- Publish accurate names, roles, values, states, actions, bounds, and order.

### Full-application widgets, bounded shared host

An installed widget may privately own an application-scale model, database,
indexes, caches, navigation, computation, and helper processes. The framework
does not impose an arbitrary memory, one-process, or tiny-state product quota.

Every boundary entering or allocating inside the shared native host remains
bounded: IPC frames, current presentation trees, strings, recursion, update
admission, pending actions, queues, caches, decoded/native/GPU resources,
capability payloads, package ingestion, and concurrent host sessions. Large
collections use paging/virtualization and opaque resource handles. Unsafe
submissions fail before native allocation with a precise diagnostic and never
silently truncate content.

### Explicit authority

- Ordinary widgets receive no ambient network, filesystem, registry, process,
  device, credential, token, window, or desktop authority.
- Privileged behavior is provided by narrow typed host capabilities or a
  separately reviewed trusted provider.
- Public identities are stable opaque values; widgets never receive raw paths,
  commands, AUMIDs, package/store IDs, provider records, or credentials.
- Presentation is not authority. The host revalidates exact current identity
  and capability at activation.
- Isolation is a process/capability boundary, never an in-process timeout
  described as a sandbox.

### Pre-release state

There is one development user and no public persistence-compatibility promise.
Prefer one clean current overlay-owned schema over indefinite compatibility
facades. A breaking change may atomically reset only the affected overlay-owned
state and then reconcile from the authoritative provider. Never reset external
provider data, accounts, credentials, or user files.

## Delivery phases

### Phase 0 — platform feasibility: substantially complete

- Guide/controller discovery and fallback.
- Input containment and focus ownership.
- Topmost transparent presentation feasibility.
- Native rendering, DirectComposition, UIA, and responsive placement.
- Out-of-process installed widget execution and typed capabilities.
- Bounded host/process ownership, activation, packaging, and recovery.

Remaining feasibility gates are feature-specific: trusted rich-video cost,
supported audio endpoint setters, and physical hardware/account evidence.
Planner smoke no longer requires production window discovery: the exact visible
surface is controllable through current UIA bounds and coordinate input without
adding taskbar/Alt-Tab presence.

### Phase 1 — shell vertical slice: active stabilization

- Stable one-owner overlay process and tray.
- Responsive shell/body/guide/tray placement.
- Controller, keyboard, pointer, accessibility, and contextual actions.
- Widget switching with retained admitted content and bounded transitions.
- Settings, appearance, package management, diagnostics, and recovery.

Current priority is user-reported visible behavior: live first-page smoke,
navigation/focus regressions, clipping, tray stability, transition quality,
shortcut truth, worker/provider recovery, and performance of first paints.

### Phase 2 — public SDK: strong local preview, not public-ready

Implemented locally:

- Declarative responsive components and semantic styling.
- Lifecycle, immutable model, commands, tickers, navigation, stable IDs,
  operation scopes, and bounded paged/nonpaged resources.
- Typed capability services and opaque artwork/resources.
- Basic, data, media, and multipage starters.
- Fake services, deterministic scenarios, focused tests, and MSTest.Sdk 4.3.2
  for new test projects alongside existing executable runners.
- Validation, deterministic packaging, install, list, enable/disable, version
  selection, rollback, and removal.

Remaining:

- Externally published/versioned SDK and template artifacts.
- Compatibility/deprecation/migration/update governance.
- Isolated interactive preview and reliable live targetability.
- Verified publisher/acquisition provenance and update discovery.
- A third-party GitHub repository onboarding proof.
- A full application-scale reference sample.

### Phase 3 — polished first-party widgets and flagship features: active

Visible defects and explicitly requested features outrank internal cleanup.
The current exact queue is in `delivery-plan.md`; the feature intent is below.

### Phase 4 — ecosystem: later

- Public release artifacts and compatibility policy.
- Repository templates, CI examples, semantic fixture bundles, and author
  troubleshooting.
- Verified publisher identity, acquisition provenance, update discovery,
  rollback, and recovery.
- GitHub-hosted community packages without checkout-relative SDK references.
- Gallery/discovery only after package trust and update governance are sound.

## Unified Game Launcher

The authoritative product specification is
[`game-launcher-requirements.md`](game-launcher-requirements.md). It is intended
to exceed Spotify in complexity and acts as the full-application widget proof.

### M1 — trusted installed library and console-home presentation

- Normalized source/item availability, capability, operation, metadata
  provenance, and tile/cover/hero/logo artwork roles.
- Stable opaque SavedId/AppId authority and current launch revalidation.
- Windows/Xbox installed-game discovery through supported registration APIs.
- Optional trusted local store adapters added one at a time with explicit
  enablement and source health.
- Deduplication with distinct launch variants preserved.
- Warm persisted organization and last-good display while providers refresh.
- Search, filter, sort, sources, favorites, hidden, recents, variants, details,
  launch feedback, and exact controller shortcuts.
- Cursor-backed/occurrence-aware virtualization for thousands of games.
- Built-in hero rail, cover wall, carousel, and compact grid with identical
  action and accessibility semantics.

Current dependency chain:

1. Normalize and validate the managed presentation contract.
2. Import supported Windows/Xbox games and then bounded opt-in local stores.
3. Accept the strict data-only Launcher Experience catalog and native presets.
4. Add launcher-scoped style/artwork/recovery ownership.
5. Project production Game Launcher state into every experience.
6. Deliver author CLI, preview, pack, inspect, install, select, rollback, and
   removal for experience packs.

### M2 — safe optional enrichment

- Provider-attributed metadata and artwork with revision, cache, licensing,
  attribution, offline, failure, and rate-limit policy.
- No title-derived authority and no remote URL or provider record exposed to
  ordinary widgets.
- Metadata failure never removes trusted installed identity or grants launch.

External IGDB/SteamGridDB or similar services require a selected legal/API
plan and credentials; they must not block installed-only work.

### M3 — trusted content operations

- Install/update/pause/resume/cancel/repair/move/import/uninstall/cloud-sync and
  add-on operations belong to a separately reviewed trusted provider.
- Destructive operations require explicit confirmation, progress, cancellation,
  rollback/recovery, disk/network policy, and exact current authority.
- No raw command, executable path, protocol URL, or store credential enters the
  ordinary widget.

## YouTube video widget and pinned surfaces

This feature requires a narrow trusted rich-media architecture before a widget
can play video.

### Platform prerequisites

- Host-owned pin/unpin lifecycle and persisted placement.
- Focusable, click-through, and hidden interaction modes with deterministic
  controller/pointer/UIA ownership.
- Safe monitor/work-area/DPI placement and recovery after topology change.
- Picture-in-picture and full widget placement without clipping or stealing
  game focus.
- A narrow trusted media process/surface with bounded memory, CPU, GPU, process,
  decode, network, frame, audio, and teardown behavior.
- Visibility/overlay-close semantics: a pinned surface may remain visible when
  the dashboard closes, while an unpinned widget closes normally.
- Reduced-motion/transparency, high-contrast, captions, keyboard/controller,
  and screen-reader behavior.

### Widget behavior

- Search and play credential-free public videos where the selected provider
  contract permits it.
- Player, queue/history, metadata, progress/seek, play/pause, previous/next,
  captions, quality, volume/mute, fullscreen/PiP, pin, move, and close.
- Honest loading, buffering, unavailable, blocked, network, decoder, and
  provider-policy states with retained last-good metadata where safe.
- Background work stops or degrades when hidden/unpinned; pinned playback
  remains explicitly user-owned.
- No generic community WebView, arbitrary URL, HTML/JS package content, raw
  cookies, or credential access.

The previous fixed-video WebView2 prototype exceeded its resource gate at about
348.7 MiB private memory and roughly 4% CPU for one paused visible surface.
Further work requires a newly authorized bounded media experiment or a changed
budget. Authenticated library/history/subscription features also require user-
authorized OAuth and must not block credential-free platform work.

## First-party widget roadmap

### YT Music

- Community-installable through the same public SDK/package path.
- Pairing/disconnected/connecting/connected/unavailable/recovery states.
- Now playing, artwork, progress, play/pause/previous/next, shuffle, repeat,
  rating, refresh, and dashboard actions.
- Explicit optimistic behavior, authoritative reconciliation, lifecycle-bound
  polling/progress/subscriptions, and no work after deactivation.
- Credential-free fake tests plus a bounded real companion procedure.

Real companion/account verification remains manual.

### Spotify

- Continuous occurrence-aware Queue/Playlists navigation without lost entries,
  focus jumps, or page-edge wrapping.
- Player, seek, devices, playlists, queue, transport, and dashboard actions.
- Transient provider/worker failure recovery with last-good presentation and
  bounded retry.
- Responsive headers/text/buttons, accurate shortcut help, stable focus, and
  no clipping or transition border.

OAuth/Web Playback/device behavior requires an authorized account and Premium
eligibility; credential-free structural work continues independently.

### Games & Apps

- Automatically include safely identified game types while allowing explicit
  applications.
- Persistent user library/organization shown immediately at restart, followed
  by background provider reconciliation.
- Trusted artwork/icons with semantic fallback.
- Stable add/remove behavior, available-app list, refresh, focus, and layout.
- Share normalized library/provider infrastructure with Game Launcher without
  duplicating launch authority.

### Audio Mixer

- Master output/input/session visibility and live updates.
- LB/RB master-volume steps and X mute from the tray.
- Complete keyboard/controller scrolling and reverse focus reachability.
- Input/output device selection only through a documented supported Windows
  API; do not use undocumented `PolicyConfig`, registry mutation, or Shell
  automation.

### Now Playing / Media Sessions

- Reliable enumeration, activation, retry, last-good presentation, and precise
  sanitized diagnostics when Windows exposes no identity/session detail.
- Accurate transport state and contextual dashboard actions.

### Network, Bluetooth, and Settings

- Supported APIs only, narrow typed authority, stable cancellation and
  reconciliation, explicit availability/failure states, and no secrets in
  widget snapshots/logs.
- Settings remains the trusted surface for theme/package/source configuration,
  consent, diagnostics, version selection, rollback, repair, and removal.

## Quality gates

### Architecture

- Cohesive owners, narrow APIs, one primary responsibility.
- Explicit task/cancellation/resource/generation ownership.
- No giant-class growth without a before/after responsibility map.
- No compatibility scaffolding without a supported consumer.
- No widget-specific native offsets or duplicated renderer/focus authority.

### Correctness and reliability

- Deterministic lifecycle, failure, stale, cancellation-ignoring, retry,
  rollback, and last-good cases.
- Typed diagnostics at every cross-process/provider boundary.
- Exact current authority revalidated before effects.
- A failed widget/provider never takes down another widget or the host.

### Performance

- Hidden/idle and visible/interactive CPU, GPU, memory, wakeup, process count,
  update frequency, snapshot size, startup, activation, and input latency
  measured on named accepted artifacts.
- Prefer event-driven updates, coalesced publication, lazy resources, and
  minimal semantic/render tree churn.
- Degrade visual effects before input latency.

### Verification

- Tier 1 focused build/tests for every assignment.
- Tier 2 only for the smallest changed cross-component boundary.
- Tier 3 only at a named integration/release/verifier/core-boundary checkpoint.
- Never pay for the same unchanged aggregate on dirty and exact-commit trees.
- Live user testing outranks malformed synthetic captures; bad captures are
  excluded, not debugged during product work.

## Current execution priorities

1. Complete active DLV-143 so the first cold-start dashboard frame is bottom-
   anchored before later platform work.
2. Complete active DLV-134 so real Game Launcher state drives all four accepted
   native experiences with unchanged action and focus authority.
3. Keep DLV-135 author tooling Ready immediately after DLV-134.
4. Keep accepted Spotify DLV-144 live-verifying on PID 25644 and queue any user-
   observed correction ahead of later feature work.
5. Keep user-reported visible regressions ahead of internal refactors.
6. Continue other credential-free roadmap features while account, hardware,
   or supported-API work is blocked.

## Explicit blockers

- Audio default endpoint selection: no accepted supported setter yet.
- Trusted rich-video surface: current visible WebView2 prototype exceeds the
  resource gate.
- Spotify Web Playback and authenticated YouTube/YT Music library behavior:
  credentials/account eligibility.
- Physical controller, display, audio, Bluetooth, game, Narrator/MSAA, and
  mixed-hardware matrices: user hardware evidence.
- External publishing, signing, gallery, and update deployment: separate user
  authority and release plan.

One blocked feature never stops independent visible, framework, reliability,
accessibility, documentation, or performance work in the other lane.

## Risk register

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Native host becomes a monolith | Slow, risky visible changes | Focused owners, hotspot register, before/after maps, no speculative coordinator graph. |
| Full-app widgets overload shared host | Game/overlay instability | Private scale stays in workers; strict IPC/tree/update/native-resource bounds and virtualization. |
| Store/provider data becomes launch authority | Unsafe or wrong launch | Provider-private records, opaque public IDs, fresh exact activation revalidation. |
| Visual work outruns responsiveness | Clipping, focus loss, lag | Logical work-area profiles, semantic geometry agreement, live Release verdict, measured timings. |
| Test suite dominates delivery | Slow visible progress | Tiered bounded verification and one named aggregate checkpoint. |
| Security work never ends | Product stagnation | Installed-widget gate frozen except reproducible P0/threat violation/release blocker. |
| Legacy support cements prototype design | Duplicate models and state | Clean pre-release schema; atomic affected-state reset where necessary. |
| External service dependency blocks roadmap | Idle lanes | Credential-free fakes, installed-only providers, and independent visible work first. |
| Capture artifacts create false bugs | Wasted implementation time | Exclude malformed captures immediately and rely on functional/log/live user evidence. |

The roadmap is intentionally broader than the delivery queue. A feature becomes
implementation work only when the planner decomposes it into a bounded DLV
assignment with ownership, acceptance, verification, dependencies, and stop
conditions.
