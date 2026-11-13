# Input and Discord refinements

Started 2026-11-12 on `codex/input-discord-refinements`, following accepted local
Discord source cleanup. Local implementation and verification only; no public
release, push, installation or changes to the cancelled desktop-client experiment.

## Authorized scope

- Shared modal Escape and outside-click dismissal. The user explicitly rejected
  adding a Close button; preserve existing headers. Child popups dismiss first,
  underlying controls never receive the dismissal click, and domain cancellation
  remains authoritative.
- Configurable global overlay keyboard shortcut, replacing the F1-only setting,
  with legacy migration, recording, conflict/error feedback and safe rebinding.
- Stable Playnite Home row geometry while indexed posters/favorites load; preserve
  lazy loading, focus and scrolling. Check applicability across collection consumers.
- Discord native capture pacing against advancing deadlines, with bounded recovery
  after stalls and idle behavior retained.
- Borderless live capture when supported and permitted by Windows, with ordinary
  bordered capture retained when unavailable. Do not alter Game Capture recording.
- Bounded backend invalidation events for faster Discord voice/speaking updates,
  using host-owned delivery and widget-owned provider subscriptions.
- Incoming stream health/details and on-demand connection measurements, without
  automatic replay of a share/join/watch side effect.

The earlier `modal-and-overlay-shortcut-plan.md` is background scope; its Close
button recommendation is superseded by the user's correction above. Serein was
reviewed at `29a390ce212342fe5c566ae7bf10ce9067c55cfa` as a source reference, not as
runtime proof or a replacement media engine. Pacing benefits remain a hypothesis
until measured. Do not promise better receiver quality from synthetic checks.

## Coordination and verification

Modal, keyboard and indexed-layout implementation proceed independently. Shared
protocol versioning, API baselines, host builds, integration and native launches
are coordinated centrally. Focus/native ownership changes require actual native
interaction evidence. Generated capture sources are used for unattended tests;
user microphone consent and real broadcast source selection remain user actions.
The user allows restarting the isolated candidate as needed.

Each feature must record affected consumers, compatibility decisions, focused
checks and any remaining live acceptance in this plan or its linked verification
record. Preserve current safeguards if evidence invalidates a proposed abstraction.

## Status

Implementation complete; local qualification and live limitations are recorded below.

- Shared modal contract is protocol 106; existing snapshots remain compatible.
  [Modal ownership and consumer review](modal-dismissal-implementation.md).
- [Keyboard shortcut implementation](maintainers/keyboard-shortcut.md): managed
  settings 33, Settings 94 and presentation-session 363 checks pass.
- [Poster loading refinement](poster-loading-refinement.md): no new placeholder
  protocol; existing responsive WRSS is shared by an inert reserve and real rows.
  Playnite layout tests (57) pass. Initial host compile corrections use a Grid-based
  Spacer because WinUI Border is sealed, plus explicit concrete array casts for
  CsWinRT trimming compatibility.
- Advancing capture deadlines have deterministic repeated-jitter/stall tests;
  native policy and audio queue tests pass. Border suppression uses the supported
  `IGraphicsCaptureSession3.IsBorderRequired(false)` request and gracefully keeps
  normal capture when unavailable. Windows consent/policy and other capture apps
  still decide whether a border is displayed; property assignment is not proof
  of suppression. Game Capture/preview exports and source selection are unchanged.
- Backend invalidations coalesce in the host, are payload-free and limited to one
  outstanding event until an admitted read. Discord observes bounded known stores
  and retires old listeners; low-frequency fallback polling remains for recovery.
  Hidden widgets without pins/sharing retain their existing five-second cadence.
  Node observer/backend checks (25) pass. Only Discord currently uses SessionBackend;
  other shipping widgets use isolated adapters or ordinary WebViews and are not
  given unsolicited actions. Existing ReadMessagesAsync is reused; no new SDK
  event queue or widget-authored focus/navigation machinery is introduced.

## Final local evidence

- SDK 177, SDK compatibility 17, PlatformSettings 33, Settings 94, presentation session 363,
  Playnite layout 57 and Discord 440 managed checks passed. Full Discord regression
  shard passed including source/minified backend, provider, receiver, media policies
  and bounded subscriptions. Managed/native presentation contract parity passed.
- Native modal 51, shortcut 22, poster 23 and backend 18 checks passed. Poster geometry
  remains identical before/after range arrival at 360/470/680 DIP viewport heights
  and 125% scale. Its fixture was corrected to use the real native item-container
  styling path; direct poster-panel styling had incorrectly added a second border.
- The actual shortcut settings editor recorded/enabled/saved a modified key and
  updated its displayed setting in an isolated profile. The trimmed production
  frontend opened/refreshed Discord connection details and real Escape restored
  focus to the caller. Images and JSON evidence are under `artifacts/input-discord`.
- Generated browser source preflight measured 60.04 FPS; its measured run cadence
  was 60.00 FPS. The 1440p60 local capture/WebRTC run passed with 59.79 submitted,
  55.02 encoded and 54.94 decoded FPS averaged over 13.13 seconds. The final instant
  receiver report was 59 FPS with zero decoder-reported drops; the worker's cumulative
  statistics recorded 52 drops, so this is not a claim of a lossless 60-FPS pipeline.
  The worker stopped normally without forced termination. Evidence:
  `artifacts/input-discord/capture-60fps-2/result.json`.
- A copied Debug build could not load the generated WebView source's theme resources.
  Publishing a self-contained validation candidate resolved that fixture startup;
  the failed attempt did not start capture. Test-only validation code is published
  untrimmed; the separate production frontend uses normal trimming and compiled-out
  validation. Both builds passed. No shipped trim checks were weakened.
- Updated Playnite archive validates/packs successfully. The isolated workspace has
  current Bridge/Settings/worker binaries. No existing installation/profile was
  updated, and no merge, push or release was performed. The installed app was closed with user approval
  and is intentionally not restarted.

## Remaining live acceptance

Real Discord account/provider availability, incoming pin counters under moving and
idle content, remote picture/audio quality and Windows border suppression still
need everyday live testing. Border suppression is a supported request, not proof
that Windows or another capturing app permits removal. Playnite's actual installed
widget needs the rebuilt package for the new explicit backdrop policy. Physical
controller/game testing is not replaced by injected native key and fixture checks.
These limits do not reopen the cancelled native-client migration or codec experiments.
